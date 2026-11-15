using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Repositories;

public interface IBenchmarkRepository
{
    Task<List<BenchmarkRun>> GetRecentRunsAsync(int limit = 100);
    Task<List<BenchmarkMeasurement>> GetMeasurementsAsync(long runId);
    Task<List<BenchmarkMeasurement>> GetMeasurementsForRunsAsync(IReadOnlyCollection<long> runIds);
    Task DeleteRunAsync(long runId);
    Task FailInterruptedRunsAsync(long endedAtUtc);
    Task<long> CreateRunAsync(BenchmarkRun run);
    Task SaveMeasurementAsync(BenchmarkMeasurement measurement);
    Task CompleteRunAsync(long runId, string status, long endedAtUtc, string? errorMessage);
}
