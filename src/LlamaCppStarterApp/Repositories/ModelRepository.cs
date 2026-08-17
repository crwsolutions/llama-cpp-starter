using Dapper;
using LlamaCppStarterApp.Models;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

public partial class ModelRepository : IModelRepository
{
    private readonly string _dbPath;

    public ModelRepository()
    {
        _dbPath = Database.DbPath;
    }

    public async Task<List<Model>> GetAllAsync()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "SELECT Id, Path, Name, Quant, SizeBytes, MmprojPath, ScannedAt FROM Models ORDER BY Name";
        return (await connection.QueryAsync<Model>(sql)).ToList();
    }

    public async Task<Model?> GetByIdAsync(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "SELECT Id, Path, Name, Quant, SizeBytes, MmprojPath, ScannedAt FROM Models WHERE Id = @Id";
        return await connection.QueryFirstOrDefaultAsync<Model>(sql, new { Id = id });
    }

    public async Task UpsertManyAsync(IEnumerable<Model> models)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        const string upsertSql = """
            INSERT INTO Models (Path, Name, Quant, SizeBytes, MmprojPath, ScannedAt)
            VALUES (@Path, @Name, @Quant, @SizeBytes, @MmprojPath, @ScannedAt)
            ON CONFLICT (Path) DO UPDATE SET
                Name = excluded.Name,
                Quant = excluded.Quant,
                SizeBytes = excluded.SizeBytes,
                MmprojPath = excluded.MmprojPath,
                ScannedAt = excluded.ScannedAt;
            """;

        await connection.ExecuteAsync(upsertSql, models.ToList());
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "DELETE FROM Models WHERE Id = @Id";
        await connection.ExecuteAsync(sql, new { Id = id });
    }
}
