using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Repositories;

public interface IPromptRepository
{
    Task<PromptEntry?> GetLastAsync();
    Task UpsertAsync(string prompt);
}
