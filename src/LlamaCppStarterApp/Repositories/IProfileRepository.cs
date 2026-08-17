using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Repositories;

public interface IProfileRepository
{
    Task<List<Profile>> GetAllAsync();
    Task<List<Profile>> GetByModelAsync(int modelId);
    Task<Profile?> GetByIdAsync(int id);
    Task<Profile> UpsertAsync(Profile profile);
    Task DeleteAsync(int id);
}
