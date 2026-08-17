using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Repositories;

public interface IModelRepository
{
    Task<List<Model>> GetAllAsync();
    Task<Model?> GetByIdAsync(int id);
    Task UpsertManyAsync(IEnumerable<Model> models);
    Task DeleteAsync(int id);
}
