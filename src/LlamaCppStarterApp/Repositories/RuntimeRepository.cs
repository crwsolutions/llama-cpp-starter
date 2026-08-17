using Dapper;
using LlamaCppStarterApp.Models;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

public partial class RuntimeRepository : IRuntimeRepository
{
    private readonly string _dbPath;

    public RuntimeRepository()
    {
        _dbPath = Database.DbPath;
    }

    public async Task<List<Runtime>> GetAllAsync()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "SELECT Id, Name, ExecutablePath, Backend, Status, Location FROM Runtimes ORDER BY Name";
        return (await connection.QueryAsync<Runtime>(sql)).ToList();
    }

    public async Task<Runtime> UpsertAsync(Runtime runtime)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        if (runtime.Id == 0)
        {
            const string insertSql = """
                INSERT INTO Runtimes (Name, ExecutablePath, Backend, Status, Location, CreatedAt)
                VALUES (@Name, @ExecutablePath, @Backend, @Status, @Location, @CreatedAt)
                ON CONFLICT (ExecutablePath) DO UPDATE SET
                    Name = excluded.Name,
                    Backend = excluded.Backend,
                    Status = excluded.Status,
                    Location = excluded.Location;
                SELECT COALESCE(last_insert_rowid(), (SELECT Id FROM Runtimes WHERE ExecutablePath = @ExecutablePath));
                """;
            runtime.Id = await connection.ExecuteScalarAsync<int>(insertSql, new
            {
                runtime.Name,
                runtime.ExecutablePath,
                runtime.Backend,
                runtime.Status,
                runtime.Location,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }
        else
        {
            const string updateSql = """
                UPDATE Runtimes
                SET Name = @Name, Backend = @Backend, Status = @Status, Location = @Location
                WHERE Id = @Id;
                """;
            await connection.ExecuteAsync(updateSql, runtime);
        }

        return runtime;
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        const string sql = "DELETE FROM Runtimes WHERE Id = @Id";
        await connection.ExecuteAsync(sql, new { Id = id });
    }
}
