using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Repositories;

public interface IModelRepository
{
    Task<List<Model>> GetAllAsync();
    Task<Model?> GetByIdAsync(int id);
    Task UpsertManyAsync(IEnumerable<Model> models);
    /// <summary>Updates only the model's capability cache blob (CapabilitiesJson), via the deterministic ModelId.</summary>
    Task UpdateCapabilityAsync(string modelId, string? capabilitiesJson);
    Task DeleteAsync(int id);
}
