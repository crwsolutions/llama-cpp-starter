namespace LlamaCppStarterApp.Repositories;

public interface IAppSettingsRepository
{
    Task<string?> GetValueAsync(string key);
    Task SetAsync(string key, string value);
}
