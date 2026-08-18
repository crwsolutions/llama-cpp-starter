using Dapper;
using LlamaCppStarterApp.Models;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

public partial class ProfileRepository : IProfileRepository
{
    private readonly string _dbPath;

    public ProfileRepository()
    {
        _dbPath = Database.DbPath;
    }

    public async Task<List<Profile>> GetAllAsync()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "SELECT Id, Name, ModelId, IsDefault, Port, ParamsJson FROM Profiles ORDER BY Name";
        return (await connection.QueryAsync<Profile>(sql)).ToList();
    }

    public async Task<List<Profile>> GetByModelAsync(int modelId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "SELECT Id, Name, ModelId, IsDefault, Port, ParamsJson FROM Profiles WHERE ModelId = @ModelId ORDER BY Name";
        return (await connection.QueryAsync<Profile>(sql, new { ModelId = modelId })).ToList();
    }

    public async Task<Profile?> GetByIdAsync(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "SELECT Id, Name, ModelId, IsDefault, Port, ParamsJson FROM Profiles WHERE Id = @Id";
        return await connection.QueryFirstOrDefaultAsync<Profile>(sql, new { Id = id });
    }

    public async Task<Profile> UpsertAsync(Profile profile)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        if (profile.Id == 0)
        {
            const string insertSql = """
                INSERT INTO Profiles (Name, ModelId, IsDefault, Port, ParamsJson)
                VALUES (@Name, @ModelId, @IsDefault, @Port, @ParamsJson);
                SELECT last_insert_rowid();
                """;
            profile.Id = await connection.ExecuteScalarAsync<int>(insertSql, profile);
        }
        else
        {
            const string updateSql = """
                UPDATE Profiles
                SET Name = @Name, ModelId = @ModelId, IsDefault = @IsDefault, Port = @Port, ParamsJson = @ParamsJson
                WHERE Id = @Id;
                """;
            await connection.ExecuteAsync(updateSql, profile);
        }

        return profile;
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "DELETE FROM Profiles WHERE Id = @Id";
        await connection.ExecuteAsync(sql, new { Id = id });
    }
}
