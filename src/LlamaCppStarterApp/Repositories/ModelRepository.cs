using Dapper;
using LlamaCppStarterApp.Models;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

public partial class ModelRepository : IModelRepository
{
    private const string SelectColumns =
        "Id, ModelId, Path, Name, Quant, SizeBytes, MmprojPath, ScannedAt, MetadataJson, CapabilitiesJson";

    private readonly string _dbPath;

    public ModelRepository()
    {
        _dbPath = Database.DbPath;
    }

    public async Task<List<Model>> GetAllAsync()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = $"SELECT {SelectColumns} FROM Models ORDER BY Name";
        return (await connection.QueryAsync<Model>(sql)).ToList();
    }

    public async Task<Model?> GetByIdAsync(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = $"SELECT {SelectColumns} FROM Models WHERE Id = @Id";
        return await connection.QueryFirstOrDefaultAsync<Model>(sql, new { Id = id });
    }

    public async Task UpsertManyAsync(IEnumerable<Model> models)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        const string upsertSql = """
            INSERT INTO Models (Path, Name, Quant, SizeBytes, MmprojPath, ScannedAt, ModelId, MetadataJson)
            VALUES (@Path, @Name, @Quant, @SizeBytes, @MmprojPath, @ScannedAt, @ModelId, @MetadataJson)
            ON CONFLICT (Path) DO UPDATE SET
                Name = excluded.Name,
                Quant = excluded.Quant,
                SizeBytes = excluded.SizeBytes,
                MmprojPath = excluded.MmprojPath,
                ScannedAt = excluded.ScannedAt,
                ModelId = excluded.ModelId,
                MetadataJson = excluded.MetadataJson;
            """;

        await connection.ExecuteAsync(upsertSql, models.ToList());
    }

    public async Task UpdateCapabilityAsync(string modelId, string? capabilitiesJson)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "UPDATE Models SET CapabilitiesJson = @CapabilitiesJson WHERE ModelId = @ModelId";
        await connection.ExecuteAsync(sql, new { ModelId = modelId, CapabilitiesJson = capabilitiesJson });
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "DELETE FROM Models WHERE Id = @Id";
        await connection.ExecuteAsync(sql, new { Id = id });
    }
}
