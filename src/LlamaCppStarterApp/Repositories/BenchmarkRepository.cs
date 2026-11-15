using Dapper;
using LlamaCppStarterApp.Models;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

public sealed class BenchmarkRepository : IBenchmarkRepository
{
    private readonly string _dbPath;

    public BenchmarkRepository() => _dbPath = Database.DbPath;

    public async Task<List<BenchmarkRun>> GetRecentRunsAsync(int limit = 100)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        return (await connection.QueryAsync<BenchmarkRun>("""
            SELECT Id, ConfigurationId, ConfigurationName, ConfigurationSnapshotJson, StartedAtUtc, EndedAtUtc,
                   Status, ModelName, ModelPath, ModelQuant, ProfileName, ProfileId, RuntimeName, RuntimePath,
                   BuildIdentity, LlamaCppVersion, Repetitions, Warmups, Comment, ServerPid, LaunchParametersJson,
                   DatasetVersion, ErrorMessage
            FROM BenchmarkRuns ORDER BY StartedAtUtc DESC LIMIT @Limit
            """, new { Limit = Math.Clamp(limit, 1, 1000) })).ToList();
    }

    public async Task<List<BenchmarkMeasurement>> GetMeasurementsAsync(long runId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        return (await connection.QueryAsync<BenchmarkMeasurement>("""
            SELECT Id, RunId, IsWarmup, ScenarioId, ScenarioVersion, Category, Repetition, PromptTokens,
                   GeneratedTokens, CachedPromptTokens, CacheState, PromptMilliseconds, GenerationMilliseconds,
                   TotalMilliseconds, ClientWallMilliseconds, PromptTokensPerSecond, GenerationTokensPerSecond,
                   ToolCalls, FailedToolCalls, ErrorMessage
            FROM BenchmarkMeasurements WHERE RunId = @RunId ORDER BY Id
            """, new { RunId = runId })).ToList();
    }

    public async Task<List<BenchmarkMeasurement>> GetMeasurementsForRunsAsync(IReadOnlyCollection<long> runIds)
    {
        if (runIds.Count == 0)
        {
            return new List<BenchmarkMeasurement>();
        }

        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        return (await connection.QueryAsync<BenchmarkMeasurement>("""
            SELECT Id, RunId, IsWarmup, ScenarioId, ScenarioVersion, Category, Repetition, PromptTokens,
                   GeneratedTokens, CachedPromptTokens, CacheState, PromptMilliseconds, GenerationMilliseconds,
                   TotalMilliseconds, ClientWallMilliseconds, PromptTokensPerSecond, GenerationTokensPerSecond,
                   ToolCalls, FailedToolCalls, ErrorMessage
            FROM BenchmarkMeasurements WHERE RunId IN @RunIds ORDER BY Id
            """, new { RunIds = runIds })).ToList();
    }

    public async Task DeleteRunAsync(long runId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("DELETE FROM BenchmarkMeasurements WHERE RunId = @RunId", new { RunId = runId }, transaction);
        var deleted = await connection.ExecuteAsync("DELETE FROM BenchmarkRuns WHERE Id = @RunId", new { RunId = runId }, transaction);
        if (deleted == 0)
        {
            throw new InvalidOperationException($"Benchmark run {runId} was not found.");
        }
        transaction.Commit();
    }

    public async Task FailInterruptedRunsAsync(long endedAtUtc)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.ExecuteAsync("""
            UPDATE BenchmarkRuns
            SET Status = 'Failed', EndedAtUtc = @EndedAtUtc,
                ErrorMessage = COALESCE(ErrorMessage, 'Application exited before the run completed.')
            WHERE Status = 'Running'
            """, new { EndedAtUtc = endedAtUtc });
    }

    public async Task<long> CreateRunAsync(BenchmarkRun run)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        return await connection.ExecuteScalarAsync<long>("""
            INSERT INTO BenchmarkRuns (
                ConfigurationId, ConfigurationName, ConfigurationSnapshotJson, StartedAtUtc, EndedAtUtc, Status,
                ModelName, ModelPath, ModelQuant, ProfileName, ProfileId, RuntimeName, RuntimePath, BuildIdentity,
                LlamaCppVersion, Repetitions, Warmups, Comment, ServerPid, LaunchParametersJson, DatasetVersion, ErrorMessage)
            VALUES (
                @ConfigurationId, @ConfigurationName, @ConfigurationSnapshotJson, @StartedAtUtc, @EndedAtUtc, @Status,
                @ModelName, @ModelPath, @ModelQuant, @ProfileName, @ProfileId, @RuntimeName, @RuntimePath, @BuildIdentity,
                @LlamaCppVersion, @Repetitions, @Warmups, @Comment, @ServerPid, @LaunchParametersJson, @DatasetVersion, @ErrorMessage);
            SELECT last_insert_rowid();
            """, run);
    }

    public async Task SaveMeasurementAsync(BenchmarkMeasurement measurement)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.ExecuteAsync("""
            INSERT INTO BenchmarkMeasurements (
                RunId, IsWarmup, ScenarioId, ScenarioVersion, Category, Repetition, PromptTokens, GeneratedTokens,
                CachedPromptTokens, CacheState, PromptMilliseconds, GenerationMilliseconds, TotalMilliseconds,
                ClientWallMilliseconds, PromptTokensPerSecond, GenerationTokensPerSecond, ToolCalls, FailedToolCalls, ErrorMessage)
            VALUES (
                @RunId, @IsWarmup, @ScenarioId, @ScenarioVersion, @Category, @Repetition, @PromptTokens, @GeneratedTokens,
                @CachedPromptTokens, @CacheState, @PromptMilliseconds, @GenerationMilliseconds, @TotalMilliseconds,
                @ClientWallMilliseconds, @PromptTokensPerSecond, @GenerationTokensPerSecond, @ToolCalls, @FailedToolCalls, @ErrorMessage)
            """, measurement);
    }

    public async Task CompleteRunAsync(long runId, string status, long endedAtUtc, string? errorMessage)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.ExecuteAsync("""
            UPDATE BenchmarkRuns SET Status = @Status, EndedAtUtc = @EndedAtUtc, ErrorMessage = @ErrorMessage WHERE Id = @RunId
            """, new { RunId = runId, Status = status, EndedAtUtc = endedAtUtc, ErrorMessage = errorMessage });
    }
}
