using Dapper;
using LlamaCppStarterApp.Models;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

public partial class PromptRepository : IPromptRepository
{
    private readonly string _dbPath;

    public PromptRepository()
    {
        _dbPath = Database.DbPath;
    }

    public async Task<PromptEntry?> GetLastAsync()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "SELECT Id, Prompt FROM PromptEntries ORDER BY Id DESC LIMIT 1";
        return await connection.QueryFirstOrDefaultAsync<PromptEntry>(sql);
    }

    public async Task UpsertAsync(string prompt)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        const string upsertSql = "INSERT INTO PromptEntries (Prompt) VALUES (@Prompt)";

        await connection.ExecuteAsync(upsertSql, new { Prompt = prompt });
    }
}
