using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;
using LlamaCppStarterApp.Services;

namespace LlamaCppStarterApp.ViewModels;

public partial class OverviewViewModel : BaseViewModel
{
    private const int MaxLogLines = 2000;

    // AppSettings-keys voor de geselecteerde Model/Startprofiel/Runtime-dropdowns,
    // zodat de laatste selectie behouden blijft over een app-herstart.
    public const string SelectedModelIdSetting = "OverviewSelectedModelId";
    public const string SelectedProfileIdSetting = "OverviewSelectedProfileId";
    public const string SelectedRuntimeIdSetting = "OverviewSelectedRuntimeId";

    // Spec-defaults (idle-inhoud van de 6 status-kaarten; kaart-inhoud Engels per spec)
    private const string IdleHardwareText = "No loaded model";
    private const string IdleStatsText = "Active 0/1 | Queued 0\nBusy/decode: 0, 0";
    private const string IdleTokensText = "No runtime";
    private const string IdleMtpTokensText = "Inactive";
    private const string IdleKvCacheText = "Used Unknown\nCapacity Unknown";

    private readonly IModelRepository _modelRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly IRuntimeRepository _runtimeRepository;
    private readonly IAppSettingsRepository _appSettings;
    private readonly LlamaServerProcessService _processService;
    private readonly ServerHealthService _healthService;
    private readonly RuntimeMetricPollerService _metricsPoller;
    private readonly ModelRuntimeStatusTracker _statusTracker = new();

    private bool _loaded;
    private MetricCardsSnapshot? _lastMetrics;
    private int? _savedProfileId;

    public OverviewViewModel(
        IModelRepository modelRepository,
        IProfileRepository profileRepository,
        IRuntimeRepository runtimeRepository,
        IAppSettingsRepository appSettings,
        LlamaServerProcessService processService,
        ServerHealthService healthService,
        RuntimeMetricPollerService metricsPoller)
    {
        _modelRepository = modelRepository;
        _profileRepository = profileRepository;
        _runtimeRepository = runtimeRepository;
        _appSettings = appSettings;
        _processService = processService;
        _healthService = healthService;
        _metricsPoller = metricsPoller;
        Title = "Overzicht";

        _processService.LogReceived += OnServerLog;
        _processService.StateChanged += OnServerStateChanged;
        _healthService.HealthChanged += OnHealthChanged;
        _metricsPoller.MetricsUpdated += OnMetricsUpdated;
    }

    [ObservableProperty]
    public partial ObservableCollection<Model> Models { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<Profile> Profiles { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<Runtime> Runtimes { get; set; } = new();

    [ObservableProperty]
    public partial string ModelsFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Model? SelectedModel { get; set; }

    [ObservableProperty]
    public partial Profile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial Runtime? SelectedRuntime { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "No runtime is loaded for the selected model.";

    // Live runtime-logboek: per-regel ObservableCollection (CollectionView virtualiseert;
    // oude regels trimmed zodra MaxLogLines overschreden)
    [ObservableProperty]
    public partial ObservableCollection<string> LogLines { get; set; } = new();

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    // --- Status-kaarten (middenraster 3×2) ---

    [ObservableProperty]
    public partial string ModelStatusText { get; set; } = "Stopped";

    [ObservableProperty]
    public partial string HardwareText { get; set; } = IdleHardwareText;

    [ObservableProperty]
    public partial string StatsText { get; set; } = IdleStatsText;

    [ObservableProperty]
    public partial string TokensText { get; set; } = IdleTokensText;

    [ObservableProperty]
    public partial string MtpTokensText { get; set; } = IdleMtpTokensText;

    [ObservableProperty]
    public partial string KvCacheText { get; set; } = IdleKvCacheText;

    internal async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            IsBusy = true;
            try
            {
                ModelsFolder = await _appSettings.GetValueAsync(ModelsViewModel.ModelsDirectorySetting) ?? string.Empty;
                Models = new ObservableCollection<Model>(await _modelRepository.GetAllAsync());
                Runtimes = new ObservableCollection<Runtime>(await _runtimeRepository.GetAllAsync());

                // Herstel de laatste dropdown-keuze (overleefdt app-herstart).
                // Stale/ontbrekende ID's vallen terug op de bestaande default-selectie ([0] / Default-profiel).
                var savedModelId = TryParseId(await _appSettings.GetValueAsync(SelectedModelIdSetting));
                var savedRuntimeId = TryParseId(await _appSettings.GetValueAsync(SelectedRuntimeIdSetting));
                var savedProfileId = TryParseId(await _appSettings.GetValueAsync(SelectedProfileIdSetting));

                if (Models.Count > 0)
                {
                    var restored = savedModelId is int mid ? Models.FirstOrDefault(m => m.Id == mid) : null;
                    // Profiel-herstel alleen wanneer het model daadwerkelijk uit de bewaarde ID komt
                    // (profiel-IDs zijn globaal uniek maar horen bij één model → bij model-fallback geen profiel-match).
                    _savedProfileId = restored is not null ? savedProfileId : null;
                    SelectedModel = restored ?? Models[0];
                }
                if (Runtimes.Count > 0)
                {
                    SelectedRuntime = (savedRuntimeId is int rid ? Runtimes.FirstOrDefault(r => r.Id == rid) : null) ?? Runtimes[0];
                }

                await RefreshStatusAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Fout bij laden: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
        else
        {
            await RefreshAsync();
        }
    }

    internal async Task RefreshAsync()
    {
        var prevModelId = SelectedModel?.Id;
        Models = new ObservableCollection<Model>(await _modelRepository.GetAllAsync());
        Runtimes = new ObservableCollection<Runtime>(await _runtimeRepository.GetAllAsync());

        if (SelectedModel is null)
        {
            SelectedModel = Models.FirstOrDefault();
        }
        else
        {
            var prev = prevModelId is int id ? Models.FirstOrDefault(m => m.Id == id) : null;
            SelectedModel = prev ?? SelectedModel;
        }

        await LoadProfilesAsync();
        await RefreshStatusAsync();
    }

    private static int? TryParseId(string? value) => int.TryParse(value, out var id) ? id : null;

    private async Task LoadProfilesAsync()
    {
        var model = SelectedModel;
        if (model is null)
        {
            Profiles = new ObservableCollection<Profile>();
            SelectedProfile = null;
            return;
        }

        var prevProfileId = SelectedProfile?.Id;
        var profiles = (await _profileRepository.GetByModelAsync(model.Id)).ToList();
        foreach (var profile in profiles)
        {
            profile.ModelName = model.Name;
        }

        if (!ReferenceEquals(SelectedModel, model))
        {
            return;
        }

        Profiles = new ObservableCollection<Profile>(profiles);
        SelectedProfile = (prevProfileId is int pid ? profiles.FirstOrDefault(p => p.Id == pid) : null)
            ?? (_savedProfileId is int spid ? profiles.FirstOrDefault(p => p.Id == spid) : null)
            ?? profiles.FirstOrDefault(p => p.IsDefault)
            ?? profiles.FirstOrDefault();
    }

    partial void OnSelectedModelChanged(Model? value)
    {
        _ = _appSettings.SetAsync(SelectedModelIdSetting, value?.Id.ToString() ?? string.Empty);
        _ = LoadProfilesAsync();
    }

    partial void OnSelectedProfileChanged(Profile? value)
    {
        _ = _appSettings.SetAsync(SelectedProfileIdSetting, value?.Id.ToString() ?? string.Empty);
    }

    partial void OnSelectedRuntimeChanged(Runtime? value)
    {
        _ = _appSettings.SetAsync(SelectedRuntimeIdSetting, value?.Id.ToString() ?? string.Empty);
    }

    private async Task RefreshStatusAsync()
    {
        var state = _processService.State;
        IsRunning = state is LlamaServerState.Running or LlamaServerState.Starting or LlamaServerState.Stopping;

        switch (state)
        {
            case LlamaServerState.Running:
                StatusText = $"Running (port {_processService.Port})";
                break;
            case LlamaServerState.Starting:
                StatusText = $"Loading… (port {_processService.Port})";
                break;
            case LlamaServerState.Stopping:
                StatusText = "Unloading…";
                break;
            default:
                StatusText = "No runtime is loaded for the selected model.";
                break;
        }

        UpdateStatusCards();
    }

    /// <summary>
    /// Kaarten-update: Modelstatus via de tracker (Loading/Loaded/Fallback);
    /// de overige kaarten uit de laatste metrics-snapshot, óf de spec-defaults bij geen sessie.
    /// </summary>
    private void UpdateStatusCards()
    {
        UpdateModelStatusText();

        var session = _processService.Session;
        if (session is null)
        {
            HardwareText = IdleHardwareText;
            StatsText = IdleStatsText;
            TokensText = IdleTokensText;
            MtpTokensText = IdleMtpTokensText;
            KvCacheText = IdleKvCacheText;
            return;
        }

        var metrics = _lastMetrics;
        if (metrics is null)
        {
            return; // nog geen poller-tick (Starting) → defaults behouden tot de eerste tick
        }

        HardwareText = metrics.HardwareText;
        StatsText = metrics.StatsText;
        TokensText = metrics.TokensText;
        MtpTokensText = MtpGatedText(metrics.MtpTokensText);
        KvCacheText = metrics.KvCacheText;
    }

    /// <summary>Modelstatus-kaart: Loading/Loaded (tracker) óf "Stopped {model}" (fallback).</summary>
    private void UpdateModelStatusText()
    {
        var session = _processService.Session;
        var modelName = session?.Model.Name ?? SelectedModel?.Name ?? string.Empty;
        var display = _statusTracker.StatusFor(session?.Model.ModelId, $"Stopped {modelName}", DateTimeOffset.UtcNow);
        ModelStatusText = display.MetricText;
    }

    /// <summary>
    /// MTP-tokens-kaart: "Inactive" tenzij het geladen profiel SpecType op draft-*/mtp begint
    /// (per technische notitie 5); anders de live poller-tekst.
    /// </summary>
    private string MtpGatedText(string pollerMtpText)
    {
        var specType = _processService.Session?.Parameters.SpecType;
        var mtpActive = specType is not null
            && (specType.StartsWith("draft", StringComparison.OrdinalIgnoreCase)
                || specType.Contains("mtp", StringComparison.OrdinalIgnoreCase));
        return mtpActive ? pollerMtpText : IdleMtpTokensText;
    }

    private void OnServerLog(object? sender, ServerLogEventArgs e)
    {
        // Marshal process-events naar de main UI-thread (AppendOutput-patroon)
        MainThread.BeginInvokeOnMainThread(() => AppendOutput(e.Line));
    }

    private void AppendOutput(string line)
    {
        LogLines.Add(line);

        // Log beperken tot MaxLogLines regels (oudste regels weggooien;
        // in de praktijk precies 1 verwijdering per toevoeging)
        while (LogLines.Count > MaxLogLines)
        {
            LogLines.RemoveAt(0);
        }
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (e.State)
            {
                case LlamaServerState.Running:
                    var session = _processService.Session;
                    if (session is not null)
                    {
                        _statusTracker.StopLoading(true, session.Model.Name, DateTimeOffset.UtcNow);
                    }
                    break;
                case LlamaServerState.Idle or LlamaServerState.Stopping:
                    _statusTracker.ClearLoadedStatus();
                    break;
            }

            _ = RefreshStatusAsync();
        });
    }

    private void OnMetricsUpdated(object? sender, MetricCardsUpdatedEventArgs e)
    {
        // Poller-events naar de main UI-thread (bestaand marshal-patroon)
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _lastMetrics = e.Snapshot;
            if (e.Snapshot.HasRuntime)
            {
                HardwareText = e.Snapshot.HardwareText;
                StatsText = e.Snapshot.StatsText;
                TokensText = e.Snapshot.TokensText;
                MtpTokensText = MtpGatedText(e.Snapshot.MtpTokensText);
                KvCacheText = e.Snapshot.KvCacheText;
            }
            else
            {
                HardwareText = IdleHardwareText;
                StatsText = IdleStatsText;
                TokensText = IdleTokensText;
                MtpTokensText = IdleMtpTokensText;
                KvCacheText = IdleKvCacheText;
            }
            UpdateModelStatusText();
        });
    }

    private void OnHealthChanged(object? sender, ServerHealthEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (e.Healthy && _processService.State == LlamaServerState.Running)
            {
                AppendOutput("Server healthy (http://127.0.0.1:" + _processService.Port + "/health).");
            }
        });
    }

    /// <summary>Laden: geselecteerd model + profiel + runtime starten.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (SelectedModel is null)
        {
            StatusText = "Selecteer eerst een model.";
            return;
        }

        if (SelectedProfile is null)
        {
            StatusText = "Selecteer eerst een startprofiel.";
            return;
        }

        if (SelectedRuntime is null)
        {
            StatusText = "Selecteer eerst een runtime.";
            return;
        }

        var parameters = ProfileParameters.FromJson(SelectedProfile.ParamsJson);
        var started = await _processService.LoadAsync(SelectedRuntime, SelectedModel, parameters, SelectedProfile.Port);
        if (!started)
        {
            StatusText = "Kon server niet starten (zie logboek).";
        }
        else
        {
            _statusTracker.StartLoading(
                SelectedModel.ModelId,
                SelectedModel.Name,
                $"http://127.0.0.1:{SelectedProfile.Port}",
                DateTimeOffset.UtcNow);
            await RefreshStatusAsync();
        }
    }

    /// <summary>Unload: POST /exit, max 30 s wachten, daarna kill.</summary>
    [RelayCommand]
    private async Task UnloadAsync()
    {
        await _processService.UnloadAsync();
        await RefreshStatusAsync();
    }
}
