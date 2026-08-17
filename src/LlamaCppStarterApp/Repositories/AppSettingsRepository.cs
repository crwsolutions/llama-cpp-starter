using Dapper;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

public partial class AppSettingsRepository : IAppSettingsRepository
{
    private readonly string _dbPath;

    public AppSettingsRepository()
    {
        _dbPath = Database.DbPath;
    }

    public async Task<string?> GetValueAsync(string key)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "SELECT Value FROM AppSettings WHERE Key = @Key";
        return await connection.QueryFirstOrDefaultAsync<string?>(sql, new { Key = key });
    }

    public async Task SetAsync(string key, string value)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        const string upsertSql = """
            INSERT INTO AppSettings (Key, Value) VALUES (@Key, @Value)
            ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
            """;

        await connection.ExecuteAsync(upsertSql, new { Key = key, Value = value });
    }
}
