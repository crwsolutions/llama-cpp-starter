using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// GPU summary for the Hardware card (nvidia-smi only).
/// No session → "No loaded model"; with session → per-PID probe (uuid match on the
/// llama-server PID) with fallback to the full --query-gpu listing; 10 s cache per session key.
/// (The reference name RuntimeGpuSummaryApplicationService is too broad for this nvidia-smi-only scope.)
/// </summary>
public sealed class GpuSummaryService
{
    private readonly GpuStatusProbeService _probe;
    private readonly GpuSummaryCache _cache;

    public GpuSummaryService(GpuStatusProbeService probe, GpuSummaryCache cache)
    {
        _probe = probe;
        _cache = cache;
    }

    public static string CacheKeyFor(LoadedSession session)
        => $"{session.Model.ModelId}|{session.Port}|{session.ProcessId}";

    public async Task<string> SummaryAsync(LoadedSession? session, CancellationToken cancellationToken = default)
    {
        if (session is null)
        {
            return "No loaded model";
        }

        var key = CacheKeyFor(session);
        if (_cache.TryGet(key, DateTimeOffset.UtcNow, out var summary))
        {
            return summary;
        }

        summary = await _probe.SummaryForProcessAsync(session.ProcessId, cancellationToken);
        if (string.IsNullOrWhiteSpace(summary) || summary == "Unavailable")
        {
            summary = await _probe.SummaryAsync(cancellationToken);
        }

        return _cache.Store(key, summary, DateTimeOffset.UtcNow);
    }
}
