using CommunityToolkit.Maui.Storage;
using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;
using LlamaCppStarterApp.Services;

namespace LlamaCppStarterApp.ViewModels;

public partial class RuntimesViewModel : BaseViewModel
{
    public const string RuntimeDirectorySetting = "RuntimeDirectory";

    private readonly IRuntimeRepository _runtimeRepository;
    private readonly RuntimeScannerService _runtimeScanner;
    private readonly IAppSettingsRepository _appSettings;

    public RuntimesViewModel(
        IRuntimeRepository runtimeRepository,
        RuntimeScannerService runtimeScanner,
        IAppSettingsRepository appSettings)
    {
        _runtimeRepository = runtimeRepository;
        _runtimeScanner = runtimeScanner;
        _appSettings = appSettings;
        Title = "Runtimes";
    }

    [ObservableProperty]
    public partial ObservableCollection<Runtime> Runtimes { get; set; } = new();

    [ObservableProperty]
    public partial string Directory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    internal async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Directory = await _appSettings.GetValueAsync(RuntimeDirectorySetting) ?? string.Empty;
            Runtimes = new ObservableCollection<Runtime>(await _runtimeRepository.GetAllAsync());
            if (Runtimes.Count == 0)
            {
                StatusText = "Nog geen runtimes gevonden. Kies een map en scan op llama-server.exe.";
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

    /// <summary>FolderPicker: map kiezen (na kiezen direct scannen) en onthouden in AppSettings.</summary>
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

            Directory = result.Folder.Path;
            await _appSettings.SetAsync(RuntimeDirectorySetting, result.Folder.Path);
            await ScanAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Fout bij map kiezen: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(Directory) || !System.IO.Directory.Exists(Directory))
        {
            StatusText = "Map niet gevonden. Kies eerst een map.";
            return;
        }

        IsBusy = true;
        try
        {
            var runtimes = await _runtimeScanner.ScanAsync(Directory);
            Runtimes = new ObservableCollection<Runtime>(runtimes);
            StatusText = Runtimes.Count > 0
                ? $"{Runtimes.Count} runtime(s) gevonden in {Directory}"
                : $"Geen llama-server.exe gevonden in {Directory}";
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

    /// <summary>Delete = alleen de DB-rij, bestanden blijven staan.</summary>
    [RelayCommand]
    private async Task DeleteAsync(Runtime? runtime)
    {
        if (runtime is null)
        {
            return;
        }

        await _runtimeRepository.DeleteAsync(runtime.Id);
        Runtimes.Remove(runtime);
        StatusText = $"Runtime verwijderd uit de database: {runtime.Name} (bestanden blijven staan)";
    }
}
