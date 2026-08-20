using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;
using LlamaCppStarterApp.Services;

namespace LlamaCppStarterApp.ViewModels;

public partial class OverviewViewModel : BaseViewModel
{
    private const int MaxLogLines = 2000;

    // AppSettings keys for the selected Model/Startprofiel/Runtime dropdowns,
    // so the last selection survives an app restart.
    public const string SelectedModelIdSetting = "OverviewSelectedModelId";
    public const string SelectedProfileIdSetting = "OverviewSelectedProfileId";
    public const string SelectedRuntimeIdSetting = "OverviewSelectedRuntimeId";

    // Spec defaults (idle content of the 6 status cards; card content is English per spec).
    // The Hardware card has no idle text: the hardware exists regardless of a loaded model,
    // so it always shows the live nvidia-smi listing (polled on its own 10 s cadence).
    private const string IdleStatsText = "Active 0/1 | Queued 0\nBusy/decode: 0, 0";
    private const string IdleTokensText = "No runtime";
    private const string IdleMtpTokensText = "Inactive";
    private const string IdleKvCacheText = "Used Unknown\nCapacity Unknown";

    // Hardware card refresh cadence: nvidia-smi is cheap and cached 10 s in GpuSummaryCache,
    // so a 10 s poll keeps the card live (utilization/temperature/memory) without spamming.
    private static readonly TimeSpan HardwarePollInterval = TimeSpan.FromSeconds(10);

    private readonly IModelRepository _modelRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly IRuntimeRepository _runtimeRepository;
    private readonly IAppSettingsRepository _appSettings;
    private readonly LlamaServerProcessService _processService;
    private readonly ServerHealthService _healthService;
    private readonly RuntimeMetricPollerService _metricsPoller;
    private readonly GpuSummaryService _gpuSummary;
    private readonly ModelRuntimeStatusTracker _statusTracker = new();

    private bool _loaded;
    private bool _hardwarePolling;
    private MetricCardsSnapshot? _lastMetrics;
    private int? _savedProfileId;
    private Timer? _hardwareTimer;
    private readonly SemaphoreSlim _hardwareRefreshLock = new(1, 1);

    public OverviewViewModel(
        IModelRepository modelRepository,
        IProfileRepository profileRepository,
        IRuntimeRepository runtimeRepository,
        IAppSettingsRepository appSettings,
        LlamaServerProcessService processService,
        ServerHealthService healthService,
        RuntimeMetricPollerService metricsPoller,
        GpuSummaryService gpuSummary)
    {
        _modelRepository = modelRepository;
        _profileRepository = profileRepository;
        _runtimeRepository = runtimeRepository;
        _appSettings = appSettings;
        _processService = processService;
        _healthService = healthService;
        _metricsPoller = metricsPoller;
        _gpuSummary = gpuSummary;
        Title = "Overzicht";

        _processService.LogReceived += OnServerLog;
        _processService.StateChanged += OnServerStateChanged;
        _healthService.HealthChanged += OnHealthChanged;
        _metricsPoller.MetricsUpdated += OnMetricsUpdated;

        // Hardware is machine-wide (nvidia-smi) and independent of a loaded model:
        // poll it continuously, per-PID when a server is running, full listing otherwise.
        _hardwareTimer = new Timer(_ => _ = RefreshHardwareAsync());
    }

    [ObservableProperty]
    public partial ObservableCollection<Model> Models { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<Profile> Profiles { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<Runtime> Runtimes { get; set; } = new();

    // Read-only preview of the exact command that "Laden" would start (same pure static
    // resolution as the real load). Computed on demand; the NotifyPropertyChangedFor
    // attributes on the selection properties keep the bound Label current.
    public string CommandPreview
    {
        get
        {
            if (SelectedModel is null || SelectedProfile is null)
            {
                return string.Empty;
            }

            var parameters = ProfileParameters.FromJson(SelectedProfile.ParamsJson);
            var draftModelPath = ModelCompanionService.ResolveDraftModelPath(
                SelectedModel.Path, parameters.SpecType, parameters.SpecDraftPath);
            return LlamaServerCommandBuilder.BuildCommandLine(
                LlamaServerCommandBuilder.BuildArgs(null, SelectedModel, parameters, SelectedProfile.Port, draftModelPath));
        }
    }

    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    [NotifyPropertyChangedFor(nameof(LoadButtonText))]
    [ObservableProperty]
    public partial Model? SelectedModel { get; set; }

    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    [NotifyPropertyChangedFor(nameof(LoadButtonText))]
    [ObservableProperty]
    public partial Profile? SelectedProfile { get; set; }

    [NotifyPropertyChangedFor(nameof(LoadButtonText))]
    [ObservableProperty]
    public partial Runtime? SelectedRuntime { get; set; }

    // "Laden" while the same model + profile + runtime is already running = reload;
    // "Laad om" when the selection differs from the loaded session (auto unload + load).
    // Not an [ObservableProperty]: computed on demand, kept current by the
    // NotifyPropertyChangedFor attributes on the selection properties and IsRunning.
    public string LoadButtonText
    {
        get
        {
            var session = _processService.Session;
            return IsRunning && session is not null && SelectionDiffersFromSession(session)
                ? "Laad om"
                : "Laden";
        }
    }

    /// <summary>True when the selected model/profile/runtime differs from the loaded session.</summary>
    private bool SelectionDiffersFromSession(LoadedSession session) =>
        SelectedModel is null
        || SelectedProfile is null
        || SelectedRuntime is null
        || SelectedModel.Id != session.Model.Id
        || SelectedProfile.Id != session.LoadedProfileId
        || SelectedRuntime.Id != session.Runtime.Id;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "No runtime is loaded for the selected model.";

    // Live runtime log: per-line ObservableCollection (the CollectionView virtualizes;
    // old lines are trimmed once MaxLogLines is exceeded)
    [ObservableProperty]
    public partial ObservableCollection<string> LogLines { get; set; } = new();

    [NotifyPropertyChangedFor(nameof(LoadButtonText))]
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    // --- Status cards (middle grid 3×2) ---

    [ObservableProperty]
    public partial string ModelStatusText { get; set; } = "Stopped";

    // "Unavailable" is only the pre-first-probe placeholder: the first poll replaces it
    // with the nvidia-smi listing (or "Unavailable" when no NVIDIA GPU/driver is found).
    [ObservableProperty]
    public partial string HardwareText { get; set; } = "Unavailable";

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
                Models = new ObservableCollection<Model>(await _modelRepository.GetAllAsync());
                Runtimes = new ObservableCollection<Runtime>(await _runtimeRepository.GetAllAsync());

                // Restore the last dropdown choice (survives an app restart).
                // Stale/missing IDs fall back to the existing default selection ([0] / Default profile).
                var savedModelId = TryParseId(await _appSettings.GetValueAsync(SelectedModelIdSetting));
                var savedRuntimeId = TryParseId(await _appSettings.GetValueAsync(SelectedRuntimeIdSetting));
                var savedProfileId = TryParseId(await _appSettings.GetValueAsync(SelectedProfileIdSetting));

                if (Models.Count > 0)
                {
                    var restored = savedModelId is int mid ? Models.FirstOrDefault(m => m.Id == mid) : null;
                    // Profile restore only when the model actually comes from the saved ID
                    // (profile IDs are globally unique but belong to one model → no profile match on model fallback).
                    _savedProfileId = restored is not null ? savedProfileId : null;
                    SelectedModel = restored ?? Models[0];
                }
                if (Runtimes.Count > 0)
                {
                    SelectedRuntime = (savedRuntimeId is int rid ? Runtimes.FirstOrDefault(r => r.Id == rid) : null) ?? Runtimes[0];
                }

                await RefreshStatusAsync();
                StartHardwarePolling();
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

    /// <summary>
    /// Start the continuous nvidia-smi poll for the Hardware card (10 s cadence,
    /// first probe immediate). Runs for the lifetime of the Overview VM (app singleton);
    /// the cache in GpuSummaryService keeps nvidia-smi invocations at ~1 per 10 s.
    /// </summary>
    private void StartHardwarePolling()
    {
        var timer = _hardwareTimer;
        if (timer is null || _hardwarePolling)
        {
            return;
        }

        _hardwarePolling = true;
        timer.Change(TimeSpan.Zero, HardwarePollInterval);
    }

    /// <summary>
    /// Hardware card: machine-wide nvidia-smi listing, independent of a loaded model.
    /// While a server is running, GpuSummaryService first resolves the GPUs of the
    /// llama-server process (uuid match); without a session the full listing is shown.
    /// </summary>
    private async Task RefreshHardwareAsync()
    {
        if (!_hardwarePolling || _processService.IsShuttingDown)
        {
            return; // app exit: no UI updates (the window is already deactivated)
        }

        if (!_hardwareRefreshLock.Wait(0))
        {
            return; // probe still running (nvidia-smi timeout ~2 s) → skip this tick
        }

        try
        {
            var session = _processService.Session;
            var text = await _gpuSummary.SummaryAsync(session);

            if (_processService.IsShuttingDown)
            {
                return; // shutdown started while the probe was running
            }

            // The session can change (load/unload) while the probe was running;
            // a stale per-session result must not overwrite the current card state.
            if (ReferenceEquals(_processService.Session, session))
            {
                HardwareText = text;
            }
        }
        catch (Exception)
        {
            // probe failed/timed out → keep the previous text, next tick retries
        }
        finally
        {
            _hardwareRefreshLock.Release();
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
    /// Card update: Modelstatus via the tracker (Loading/Loaded/Fallback);
    /// the remaining cards from the last metrics snapshot, or the spec defaults when there is no session.
    /// (Hardware is excluded: it is machine-wide nvidia-smi data, refreshed by its own poll.)
    /// </summary>
    private void UpdateStatusCards()
    {
        UpdateModelStatusText();

        var session = _processService.Session;
        if (session is null)
        {
            StatsText = IdleStatsText;
            TokensText = IdleTokensText;
            MtpTokensText = IdleMtpTokensText;
            KvCacheText = IdleKvCacheText;
            return;
        }

        var metrics = _lastMetrics;
        if (metrics is null)
        {
            return; // no poller tick yet (Starting) → keep defaults until the first tick
        }

        StatsText = metrics.StatsText;
        TokensText = metrics.TokensText;
        MtpTokensText = MtpGatedText(metrics.MtpTokensText);
        KvCacheText = metrics.KvCacheText;
    }

    /// <summary>Modelstatus card: Loading/Loaded (tracker) or "Stopped {model}" (fallback).</summary>
    private void UpdateModelStatusText()
    {
        var session = _processService.Session;
        var modelName = session?.Model.Name ?? SelectedModel?.Name ?? string.Empty;
        var display = _statusTracker.StatusFor(session?.Model.ModelId, $"Stopped {modelName}", DateTimeOffset.UtcNow);
        ModelStatusText = display.MetricText;
    }

    /// <summary>
    /// MTP tokens card: "Inactive" unless the loaded profile's SpecType starts with draft-*/mtp
    /// (per technical note 5); otherwise the live poller text.
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
        // Marshal process events to the main UI thread (AppendOutput pattern)
        MainThread.BeginInvokeOnMainThread(() => AppendOutput(e.Line));
    }

    private void AppendOutput(string line)
    {
        LogLines.Add(line);

        // Limit the log to MaxLogLines lines (drop the oldest lines;
        // in practice exactly 1 removal per addition)
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
        // Poller events to the main UI thread (existing marshal pattern).
        // The empty stop-snapshot restores the server-dependent idle defaults;
        // Hardware is left alone (its own poll keeps the machine listing live).
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _lastMetrics = e.Snapshot;
            StatsText = string.IsNullOrEmpty(e.Snapshot.StatsText) ? IdleStatsText : e.Snapshot.StatsText;
            TokensText = string.IsNullOrEmpty(e.Snapshot.TokensText) ? IdleTokensText : e.Snapshot.TokensText;
            MtpTokensText = string.IsNullOrEmpty(e.Snapshot.MtpTokensText)
                ? IdleMtpTokensText
                : MtpGatedText(e.Snapshot.MtpTokensText);
            KvCacheText = string.IsNullOrEmpty(e.Snapshot.KvCacheText) ? IdleKvCacheText : e.Snapshot.KvCacheText;
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

    /// <summary>
    /// Load: start the selected model + profile + runtime. If a server is already running,
    /// unload it first and then load (no confirmation): a different combination = swap,
    /// the same combination = reload.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return; // Defensive guard: the button is disabled while IsBusy, but a direct command call is still possible.
        }

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

        // Capture the selection before the awaits so it cannot change mid-swap.
        var model = SelectedModel;
        var profile = SelectedProfile;
        var runtime = SelectedRuntime;
        var port = profile.Port;
        var parameters = ProfileParameters.FromJson(profile.ParamsJson);

        // A server is already running → stop it first, then load the selection
        // (swap when the combination differs, reload when it is the same).
        var session = _processService.Session;
        if (session is not null)
        {
            StatusText = SelectionDiffersFromSession(session)
                ? "Verwisselen: draaiende server wordt gestopt…"
                : "Herladen: draaiende server wordt gestopt…";
            await UnloadAsync();
        }

        var started = await _processService.LoadAsync(runtime, model, profile.Id, parameters, port);
        if (!started)
        {
            StatusText = "Kon server niet starten (zie logboek).";
        }
        else
        {
            _statusTracker.StartLoading(
                model.ModelId,
                model.Name,
                $"http://127.0.0.1:{port}",
                DateTimeOffset.UtcNow);
            await RefreshStatusAsync();
        }
    }

    /// <summary>Unload: POST /exit, wait max 30 s, then kill.</summary>
    [RelayCommand]
    private async Task UnloadAsync()
    {
        await _processService.UnloadAsync();
        await RefreshStatusAsync();
    }
}
