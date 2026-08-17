using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;
using LlamaCppStarterApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LlamaCppStarterApp.ViewModels;

public partial class OverviewViewModel : BaseViewModel
{
    private const int MaxLogLines = 2000;

    private readonly IModelRepository _modelRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly IRuntimeRepository _runtimeRepository;
    private readonly IAppSettingsRepository _appSettings;
    private readonly LlamaServerProcessService _processService;
    private readonly ServerHealthService _healthService;

    private bool _loaded;
    private readonly System.Text.StringBuilder _logBuffer = new();
    private int _logLineCount;

    public OverviewViewModel(
        IModelRepository modelRepository,
        IProfileRepository profileRepository,
        IRuntimeRepository runtimeRepository,
        IAppSettingsRepository appSettings,
        LlamaServerProcessService processService,
        ServerHealthService healthService)
    {
        _modelRepository = modelRepository;
        _profileRepository = profileRepository;
        _runtimeRepository = runtimeRepository;
        _appSettings = appSettings;
        _processService = processService;
        _healthService = healthService;
        Title = "Overzicht";

        _processService.LogReceived += OnServerLog;
        _processService.StateChanged += OnServerStateChanged;
        _healthService.HealthChanged += OnHealthChanged;
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

    [ObservableProperty]
    public partial string LogText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

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

                if (Models.Count > 0)
                {
                    SelectedModel = Models[0];
                }
                if (Runtimes.Count > 0)
                {
                    SelectedRuntime = Runtimes[0];
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
            ?? profiles.FirstOrDefault(p => p.IsDefault)
            ?? profiles.FirstOrDefault();
    }

    partial void OnSelectedModelChanged(Model? value) => _ = LoadProfilesAsync();

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
    }

    private void OnServerLog(object? sender, ServerLogEventArgs e)
    {
        // Marshal process-events naar de main UI-thread (AppendOutput-patroon)
        MainThread.BeginInvokeOnMainThread(() => AppendOutput(e.Line));
    }

    private void AppendOutput(string line)
    {
        _logBuffer.AppendLine(line);
        _logLineCount++;

        // Buffer beperken tot ~2000 regels (oudste regels weggooien)
        if (_logLineCount > MaxLogLines)
        {
            var lines = _logBuffer.ToString().Split('\n');
            _logBuffer.Clear();
            foreach (var l in lines.Skip(lines.Length - MaxLogLines))
            {
                _logBuffer.AppendLine(l);
            }
            _logLineCount = MaxLogLines;
        }

        LogText = _logBuffer.ToString();
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => _ = RefreshStatusAsync());
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

    /// <summary>"Add" → Modellen-scherm in profiel-editor (nieuw profiel voor geselecteerd model).</summary>
    [RelayCommand]
    private async Task AddProfileAsync()
    {
        if (SelectedModel is null)
        {
            StatusText = "Selecteer eerst een model.";
            return;
        }

        var modelsVm = App.Services.GetRequiredService<ModelsViewModel>();
        modelsVm.PendingNewProfileModelId = SelectedModel.Id;
        await Shell.Current.GoToAsync(nameof(ModelsPage));
    }

    [RelayCommand]
    private async Task OpenFolderAsync(Model? model)
    {
        if (model is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(model.Path);
        if (folder is not null && System.IO.Directory.Exists(folder))
        {
            try
            {
                await Launcher.OpenAsync(new Uri(folder));
            }
            catch (Exception ex)
            {
                StatusText = $"Kon map niet openen: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task DeleteModelAsync(Model? model)
    {
        if (model is null)
        {
            return;
        }

        var name = model.Name;
        await _modelRepository.DeleteAsync(model.Id);
        Models.Remove(model);

        if (ReferenceEquals(SelectedModel, model))
        {
            SelectedModel = Models.FirstOrDefault();
        }

        StatusText = $"Model '{name}' verwijderd (incl. profielen).";
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(Profile? profile)
    {
        if (profile is null || profile.IsDefault)
        {
            return;
        }

        var name = profile.Name;
        await _profileRepository.DeleteAsync(profile.Id);
        Profiles.Remove(profile);

        if (ReferenceEquals(SelectedProfile, profile))
        {
            SelectedProfile = Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles.FirstOrDefault();
        }

        StatusText = $"Profiel '{name}' verwijderd.";
    }
}
