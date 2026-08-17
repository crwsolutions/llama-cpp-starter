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

    /// <summary>Eén instantie per geselecteerd profiel; rechtsonder-paneel bindt hier direct aan.</summary>
    [ObservableProperty]
    public partial ProfileParameters? CurrentParameters { get; set; }

    /// <summary>Read-only runtime command-preview (live bij invoer).</summary>
    [ObservableProperty]
    public partial string CommandPreview { get; set; } = string.Empty;

    /// <summary>Door Overzicht ("Add") gezet: nieuw profiel aanmaken voor dit model na navigatie.</summary>
    public int? PendingNewProfileModelId { get; set; }

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

        await HandlePendingNewProfileAsync();
    }

    internal async Task RefreshAsync()
    {
        var prevModelId = SelectedModel?.Id;
        Models = new ObservableCollection<Model>(await _modelRepository.GetAllAsync());

        var target = prevModelId is int id ? Models.FirstOrDefault(m => m.Id == id) : null;
        SelectedModel = target ?? Models.FirstOrDefault();
        await LoadProfilesAsync();
    }

    partial void OnSelectedModelChanged(Model? value) => _ = LoadProfilesAsync();

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

        // Nieuw model zonder profiel → seed Default (port 8080, lege JSON)
        if (profiles.All(p => !p.IsDefault))
        {
            var def = new Profile
            {
                Name = "Default",
                ModelId = model.Id,
                IsDefault = true,
                Port = 8080,
                ParamsJson = new ProfileParameters().ToJson()
            };
            def.ModelName = model.Name;
            await _profileRepository.UpsertAsync(def);
            profiles.Insert(0, def);
        }

        // Model is tijdens het laden gewisseld → resultaat negeren
        if (!ReferenceEquals(SelectedModel, model))
        {
            return;
        }

        Profiles = new ObservableCollection<Profile>(profiles);
        SelectedProfile = (prevProfileId is int pid ? profiles.FirstOrDefault(p => p.Id == pid) : null)
            ?? profiles.FirstOrDefault(p => p.IsDefault)
            ?? profiles.FirstOrDefault();
    }

    partial void OnSelectedProfileChanged(Profile? value)
    {
        if (value is null)
        {
            CurrentParameters = null;
            CommandPreview = string.Empty;
            return;
        }

        // Corrupte/oude blob → fallback naar leeg profiel + melding (crashen mag niet)
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

        var args = LlamaServerCommandBuilder.BuildArgs(null, SelectedModel, CurrentParameters, SelectedProfile.Port);
        CommandPreview = LlamaServerCommandBuilder.BuildCommandLine(args);
    }

    /// <summary>Modellenmap scannen (GGUF-bestanden) + Default-profielen seeden voor nieuwe modellen.</summary>
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

            foreach (var model in models)
            {
                var profiles = await _profileRepository.GetByModelAsync(model.Id);
                if (profiles.All(p => !p.IsDefault))
                {
                    await _profileRepository.UpsertAsync(new Profile
                    {
                        Name = "Default",
                        ModelId = model.Id,
                        IsDefault = true,
                        Port = 8080,
                        ParamsJson = new ProfileParameters().ToJson()
                    });
                }
            }

            var prevId = SelectedModel?.Id;
            SelectedModel = (prevId is int id ? models.FirstOrDefault(m => m.Id == id) : null) ?? models.FirstOrDefault();
            await LoadProfilesAsync();
            StatusText = $"{Models.Count} model(len) gevonden in {FolderPath}";
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

    /// <summary>Andere modellenmap kiezen (onthouden in AppSettings) en direct scannen.</summary>
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

    /// <summary>Leeg profiel aanmaken voor het geselecteerde model.</summary>
    [RelayCommand]
    private async Task AddProfileAsync() => await AddProfileCoreAsync();

    private async Task AddProfileCoreAsync()
    {
        if (SelectedModel is null)
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

        var profile = new Profile
        {
            Name = name,
            ModelId = SelectedModel.Id,
            Port = 8080,
            ParamsJson = new ProfileParameters().ToJson()
        };
        profile.ModelName = SelectedModel.Name;
        await _profileRepository.UpsertAsync(profile);

        await LoadProfilesAsync();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        StatusText = $"Nieuw profiel '{name}' aangemaakt voor {SelectedModel.Name}.";
    }

    /// <summary>Opslaan: ProfileParameters → JSON-blob → repo.</summary>
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

        SelectedProfile.ParamsJson = CurrentParameters.ToJson();
        await _profileRepository.UpsertAsync(SelectedProfile);
        StatusText = $"Profiel '{SelectedProfile.Name}' opgeslagen.";
    }

    /// <summary>Verwijderen; Default-profiel is geblokkeerd.</summary>
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

    /// <summary>Model verwijderen (profielen verdwijnen via ON DELETE CASCADE).</summary>
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

    private async Task HandlePendingNewProfileAsync()
    {
        if (PendingNewProfileModelId is not int modelId)
        {
            return;
        }

        PendingNewProfileModelId = null;

        var model = Models.FirstOrDefault(m => m.Id == modelId) ?? Models.FirstOrDefault();
        if (model is null)
        {
            return;
        }

        if (!ReferenceEquals(SelectedModel, model))
        {
            SelectedModel = model;
        }

        await LoadProfilesAsync();
        await AddProfileCoreAsync();
    }
}
