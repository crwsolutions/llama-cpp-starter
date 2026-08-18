using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Repositories;

public interface IModelRepository
{
    Task<List<Model>> GetAllAsync();
    Task<Model?> GetByIdAsync(int id);
    Task UpsertManyAsync(IEnumerable<Model> models);
    /// <summary>Alleen de capability-cache-blob (CapabilitiesJson) van het model updaten, via deterministische ModelId.</summary>
    Task UpdateCapabilityAsync(string modelId, string? capabilitiesJson);
    Task DeleteAsync(int id);
}
