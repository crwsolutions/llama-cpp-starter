using System.ComponentModel;
using CommunityToolkit.Maui.Storage;
using LlamaCppStarterApp.Converters;
using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;
using LlamaCppStarterApp.Services;

namespace LlamaCppStarterApp.ViewModels;

public partial class ModelsViewModel : BaseViewModel
{
    public const string ModelsDirectorySetting = "ModelsDirectory";

    private readonly IModelRepository _modelRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly ModelScannerService _modelScanner;
    private readonly IAppSettingsRepository _appSettings;

    private bool _loaded;
    private string? _selectedProfileOriginalName;

    /// <summary>Suppressed while the mode change itself is being processed (no re-sync loop).</summary>
    private bool _suppressMmprojSync;

    public ModelsViewModel(
        IModelRepository modelRepository,
        IProfileRepository profileRepository,
        ModelScannerService modelScanner,
        IAppSettingsRepository appSettings)
    {
        _modelRepository = modelRepository;
        _profileRepository = profileRepository;
        _modelScanner = modelScanner;
        _appSettings = appSettings;
        Title = "Modellen";
    }

    [ObservableProperty]
    public partial ObservableCollection<Model> Models { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<Profile> Profiles { get; set; } = new();

    [ObservableProperty]
    public partial string FolderPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Model? SelectedModel { get; set; }

    [ObservableProperty]
    public partial Profile? SelectedProfile { get; set; }

    /// <summary>One instance per selected profile; the bottom-right panel binds directly to it.</summary>
    [ObservableProperty]
    public partial ProfileParameters? CurrentParameters { get; set; }

    /// <summary>Read-only runtime command preview (live on input).</summary>
    [ObservableProperty]
    public partial string CommandPreview { get; set; } = string.Empty;

    /// <summary>Capability chips (metadata summary) of the selected model; empty = not selected yet.</summary>
    [ObservableProperty]
    public partial string SelectedModelCapabilitySummaryText { get; set; } = string.Empty;

    /// <summary>True if the selected model is (likely) vision capable → Vision section visible.</summary>
    [ObservableProperty]
    public partial bool SelectedModelHasVision { get; set; }

    /// <summary>
    /// Editor state of the MM-projector picker. Auto = null (auto-linked mmproj of the model),
    /// Off = empty (no --mmproj), Custom = explicit path (override). Not persisted; derived from
    /// MmprojPath + the model's linked mmproj so "just opening" a profile never writes.
    /// </summary>
    [ObservableProperty]
    public partial MmprojMode MmprojMode { get; set; }

    /// <summary>
    /// Effective mmproj the launch command will load (GetEffectiveMmproj) — file name, "—" when none.
    /// Keeps the label, the command preview and the real load in one source of truth (D6).
    /// </summary>
    [ObservableProperty]
    public partial string MmprojEffectivePath { get; set; } = string.Empty;

    /// <summary>Picker option list (labels double as option text; see MmprojModePickerConverter).</summary>
    public static readonly IReadOnlyList<string> MmprojModeOptions =
    [
        MmprojModePickerConverter.Auto,
        MmprojModePickerConverter.Off,
        MmprojModePickerConverter.Custom
    ];

    internal async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            IsBusy = true;
            try
            {
                FolderPath = await _appSettings.GetValueAsync(ModelsDirectorySetting) ?? string.Empty;
                Models = new ObservableCollection<Model>(await _modelRepository.GetAllAsync());
                if (Models.Count > 0)
                {
                    // Triggers OnSelectedModelChanged → loads profiles + capabilities.
                    SelectedModel = Models[0];
                }
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

        var target = prevModelId is int id ? Models.FirstOrDefault(m => m.Id == id) : null;
        SelectedModel = target ?? Models.FirstOrDefault();
        await LoadProfilesAsync();
        await LoadCapabilityAsync();
    }

    partial void OnSelectedModelChanged(Model? value)
    {
        SyncMmprojEditorState();
        _ = LoadProfilesAsync();
        _ = LoadCapabilityAsync();
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

        // Model changed while loading → ignore the result
        if (!ReferenceEquals(SelectedModel, model))
        {
            return;
        }

        Profiles = new ObservableCollection<Profile>(profiles);
        SelectedProfile = (prevProfileId is int pid ? profiles.FirstOrDefault(p => p.Id == pid) : null)
            ?? profiles.FirstOrDefault(p => p.IsDefault)
            ?? profiles.FirstOrDefault();
    }

    /// <summary>
    /// Capability of the selected model: DB blob + fingerprint check; on miss/stale
    /// Inspect runs in the background, after which the cache blob is stored.
    /// </summary>
    private async Task LoadCapabilityAsync()
    {
        var model = SelectedModel;
        if (model is null)
        {
            SelectedModelCapabilitySummaryText = string.Empty;
            SelectedModelHasVision = false;
            return;
        }

        ModelCapabilitySummary? summary = null;
        var summaryText = string.Empty;
        if (ModelCapabilityService.TryReadCached(model, out var cached, out summaryText))
        {
            summary = cached;
        }
        else
        {
            var inspected = await Task.Run(() => ModelCapabilityService.Inspect(model));
            // Model changed during the inspection → ignore the result
            if (!ReferenceEquals(SelectedModel, model))
            {
                return;
            }

            summary = inspected;
            summaryText = ModelCapabilityService.SummaryText(summary);
            model.CapabilitiesJson = ModelCapabilityService.BuildCacheJson(model, summary, summaryText);
            await _modelRepository.UpdateCapabilityAsync(model.ModelId, model.CapabilitiesJson);
        }

        SelectedModelCapabilitySummaryText = summaryText;
        SelectedModelHasVision = summary.LikelyVision;
    }

    partial void OnSelectedProfileChanged(Profile? value)
    {
        if (value is null)
        {
            CurrentParameters = null;
            CommandPreview = string.Empty;
            _selectedProfileOriginalName = null;
            SyncMmprojEditorState();
            return;
        }

        _selectedProfileOriginalName = value.Name;

        // Corrupt/old blob → fall back to an empty profile + message (must not crash)
        ProfileParameters.TryParse(value.ParamsJson, out var parameters, out var error);
        if (error is not null)
        {
            StatusText = error;
        }

        if (CurrentParameters is not null)
        {
            CurrentParameters.PropertyChanged -= OnParametersChanged;
        }

        CurrentParameters = parameters;
        CurrentParameters.PropertyChanged += OnParametersChanged;

        value.PropertyChanged -= OnProfileChanged;
        value.PropertyChanged += OnProfileChanged;

        UpdateCommandPreview();
        SyncMmprojEditorState();
    }

    /// <summary>
    /// Picker writes to MmprojPath are applied through the _suppressMmprojSync guard, so a
    /// MmprojPath change seen here without that guard = a manually typed path → mode becomes
    /// Custom (the typed value is never overwritten — R3, no feedback loop).
    /// </summary>
    private void OnParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateCommandPreview();

        if (e.PropertyName == nameof(ProfileParameters.MmprojPath))
        {
            UpdateMmprojEffectivePath();

            if (!_suppressMmprojSync)
            {
                SetMmprojMode(ComputeMmprojMode());
            }
        }
    }

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Profile.Port))
        {
            UpdateCommandPreview();
        }
    }

    private void UpdateCommandPreview()
    {
        if (SelectedModel is null || SelectedProfile is null || CurrentParameters is null)
        {
            CommandPreview = string.Empty;
            return;
        }

        // Same resolution as the real load (pure static) → the preview shows exactly what is started.
        var draftModelPath = ModelCompanionService.ResolveDraftModelPath(SelectedModel.Path, CurrentParameters.SpecType, CurrentParameters.SpecDraftPath);
        var args = LlamaServerCommandBuilder.BuildArgs(null, SelectedModel, CurrentParameters, SelectedProfile.Port, draftModelPath);
        CommandPreview = LlamaServerCommandBuilder.BuildCommandLine(args);
    }

    /// <summary>
    /// Effective mmproj file name shown next to the Vision controls; "—" when the launch
    /// command will not load a projector. Updated on model/profile switch and on parameter changes.
    /// </summary>
    private void UpdateMmprojEffectivePath()
    {
        var effective = SelectedModel is null || CurrentParameters is null
            ? null
            : CurrentParameters.GetEffectiveMmproj(SelectedModel);
        MmprojEffectivePath = string.IsNullOrWhiteSpace(effective) ? "—" : Path.GetFileName(effective);
    }

    /// <summary>
    /// Derives the picker state from the profile value + the model's linked mmproj (D5):
    /// Custom when an explicit path is set, Auto when MmprojPath is null and the model has a
    /// linked mmproj, Off otherwise. Pure — reads only, never writes.
    /// </summary>
    private MmprojMode ComputeMmprojMode()
    {
        var path = CurrentParameters?.MmprojPath;
        if (path is null)
        {
            return string.IsNullOrWhiteSpace(SelectedModel?.MmprojPath) ? MmprojMode.Off : MmprojMode.Auto;
        }

        return string.IsNullOrWhiteSpace(path) ? MmprojMode.Off : MmprojMode.Custom;
    }

    /// <summary>
    /// Programmatic mode assignment (selection sync / revert after a cancelled pick):
    /// applied without triggering the user-action side effects in OnMmprojModeChanged (R3).
    /// </summary>
    private void SetMmprojMode(MmprojMode mode)
    {
        _suppressMmprojSync = true;
        try
        {
            MmprojMode = mode;
        }
        finally
        {
            _suppressMmprojSync = false;
        }
    }

    /// <summary>
    /// Re-derives the whole mmproj editor state; never writes to the profile (D5).
    /// Called on model/profile switch and after a scan/refresh.
    /// </summary>
    private void SyncMmprojEditorState()
    {
        UpdateMmprojEffectivePath();

        if (CurrentParameters is null)
        {
            return;
        }

        SetMmprojMode(ComputeMmprojMode());
    }

    partial void OnMmprojModeChanged(MmprojMode value)
    {
        // Only user picker selections reach here; programmatic sets (sync/revert) are suppressed.
        if (CurrentParameters is null || _suppressMmprojSync)
        {
            return;
        }

        switch (value)
        {
            case MmprojMode.Auto:
                CurrentParameters.MmprojPath = null;
                break;
            case MmprojMode.Off:
                CurrentParameters.MmprojPath = string.Empty;
                break;
            case MmprojMode.Custom:
                // MmprojPath is unchanged at this point → ComputeMmprojMode() is still the
                // previous displayed mode, used for the cancel-revert (D4).
                _ = PickMmprojFileAsync();
                break;
        }
    }

    /// <summary>
    /// Pick an explicit mmproj GGUF (Essentials FilePicker; *.gguf filter; null = cancelled →
    /// revert to the previous mode, D4). Reached from the picker (mode Custom) and the "Blader…"
    /// button (only visible in Custom mode). A manually typed path is handled by OnParametersChanged.
    /// </summary>
    [RelayCommand]
    private async Task PickMmprojFileAsync()
    {
        var parameters = CurrentParameters;
        if (parameters is null)
        {
            return;
        }

        var previousMode = ComputeMmprojMode();
        try
        {
            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = new[] { ".gguf" }
            });
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Kies mmproj GGUF",
                FileTypes = fileTypes
            });

            // Profile changed while the dialog was open → apply nothing.
            if (!ReferenceEquals(CurrentParameters, parameters))
            {
                return;
            }

            if (result is null)
            {
                SetMmprojMode(previousMode); // cancelled
                return;
            }

            if (!result.FileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            {
                SetMmprojMode(previousMode);
                StatusText = "Selecteer een GGUF-bestand (.gguf).";
                return;
            }

            parameters.MmprojPath = result.FullPath;
            StatusText = string.Empty;
        }
        catch (Exception ex)
        {
            SetMmprojMode(previousMode);
            StatusText = $"Fout bij bestand kiezen: {ex.Message}";
        }
    }

    /// <summary>Step size for the +/- buttons next to Contextgrootte (± one 32768-token step).</summary>
    private const int ContextSizeStep = 32768;

    /// <summary>Increase the context size by one step (null = 0; result never below 0).</summary>
    [RelayCommand]
    private void IncreaseContextSize()
    {
        var parameters = CurrentParameters;
        if (parameters is null)
        {
            return;
        }

        parameters.CtxSize = Math.Max(0, (parameters.CtxSize ?? 0) + ContextSizeStep);
    }

    /// <summary>Decrease the context size by one step (null = 0; result never below 0).</summary>
    [RelayCommand]
    private void DecreaseContextSize()
    {
        var parameters = CurrentParameters;
        if (parameters is null)
        {
            return;
        }

        parameters.CtxSize = Math.Max(0, (parameters.CtxSize ?? 0) - ContextSizeStep);
    }

    /// <summary>Scan the models folder (GGUF files; Default profiles are seeded by the scanner).</summary>
    [RelayCommand]
    private async Task ScanModelsAsync()
    {
        if (string.IsNullOrEmpty(FolderPath) || !System.IO.Directory.Exists(FolderPath))
        {
            StatusText = "Modellenmap niet gevonden. Kies eerst een map.";
            return;
        }

        IsBusy = true;
        try
        {
            var models = await _modelScanner.ScanAsync(FolderPath);
            Models = new ObservableCollection<Model>(models);

            var prevId = SelectedModel?.Id;
            SelectedModel = (prevId is int id ? models.FirstOrDefault(m => m.Id == id) : null) ?? models.FirstOrDefault();
            await LoadProfilesAsync();
            await LoadCapabilityAsync();

            var status = $"{Models.Count} model(len) gevonden in {FolderPath}";
            if (_modelScanner.SkippedCompanionCount > 0)
            {
                status += $" ({_modelScanner.SkippedCompanionCount} bestanden overgeslagen: projector/draft/MTP)";
            }
            StatusText = status;
        }
        catch (Exception ex)
        {
            StatusText = $"Fout bij scannen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Pick a different models folder (remembered in AppSettings) and scan immediately.</summary>
    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync();
            if (!result.IsSuccessful || result.Folder is null || string.IsNullOrEmpty(result.Folder.Path))
            {
                return;
            }

            FolderPath = result.Folder.Path;
            await _appSettings.SetAsync(ModelsDirectorySetting, result.Folder.Path);
            await ScanModelsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Fout bij map kiezen: {ex.Message}";
        }
    }

    /// <summary>Create a new profile for the selected model, seeded with the app-global launch defaults.</summary>
    [RelayCommand]
    private async Task AddProfileAsync() => await AddProfileCoreAsync();

    private async Task AddProfileCoreAsync()
    {
        var model = SelectedModel;
        if (model is null)
        {
            StatusText = "Selecteer eerst een model.";
            return;
        }

        var name = $"Profiel {Profiles.Count + 1}";
        var i = 2;
        while (Profiles.Any(p => p.Name == name))
        {
            name = $"Profiel {i++}";
        }

        // Same seed as the scanner's Default-profile seeding: GlobalLaunchDefaults from
        // AppSettings (fallback: app-global defaults); for models WITHOUT MTP (nextn)
        // the speculative fields are cleared (no --spec-type/--spec-draft-n-max).
        var seed = await _modelScanner.ResolveLaunchDefaultsAsync(model);

        var profile = new Profile
        {
            Name = name,
            ModelId = model.Id,
            Port = 8080,
            ParamsJson = seed.ToJson()
        };
        profile.ModelName = model.Name;
        await _profileRepository.UpsertAsync(profile);

        await LoadProfilesAsync();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        StatusText = $"Nieuw profiel '{name}' aangemaakt voor {model.Name}.";
    }

    /// <summary>Save: ProfileParameters → JSON blob → repo. Renaming the Default profile is blocked.</summary>
    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (SelectedProfile is null || CurrentParameters is null)
        {
            StatusText = "Geen profiel geselecteerd.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfile.Name))
        {
            StatusText = "Geef het profiel een naam.";
            return;
        }

        if (SelectedProfile.IsDefault
            && _selectedProfileOriginalName is not null
            && !string.Equals(SelectedProfile.Name, _selectedProfileOriginalName, StringComparison.Ordinal))
        {
            // Revert the name and do not save.
            SelectedProfile.Name = _selectedProfileOriginalName;
            StatusText = "Het Default-profiel kan niet worden gehernoemd.";
            return;
        }

        SelectedProfile.ParamsJson = CurrentParameters.ToJson();
        await _profileRepository.UpsertAsync(SelectedProfile);
        _selectedProfileOriginalName = SelectedProfile.Name;
        StatusText = $"Profiel '{SelectedProfile.Name}' opgeslagen.";
    }

    /// <summary>Delete; the Default profile is blocked.</summary>
    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        if (SelectedProfile.IsDefault)
        {
            StatusText = "Het Default-profiel kan niet worden verwijderd.";
            return;
        }

        var name = SelectedProfile.Name;
        await _profileRepository.DeleteAsync(SelectedProfile.Id);
        await LoadProfilesAsync();
        StatusText = $"Profiel '{name}' verwijderd.";
    }

    /// <summary>Delete a model (profiles disappear via ON DELETE CASCADE).</summary>
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
}
