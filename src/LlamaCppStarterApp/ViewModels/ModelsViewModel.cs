using System.ComponentModel;
using CommunityToolkit.Maui.Storage;
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
    }

    private void OnParametersChanged(object? sender, PropertyChangedEventArgs e) => UpdateCommandPreview();

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
