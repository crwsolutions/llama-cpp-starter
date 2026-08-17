using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Repositories;

public interface IRuntimeRepository
{
    Task<List<Runtime>> GetAllAsync();
    Task<Runtime> UpsertAsync(Runtime runtime);
    Task DeleteAsync(int id);
}
