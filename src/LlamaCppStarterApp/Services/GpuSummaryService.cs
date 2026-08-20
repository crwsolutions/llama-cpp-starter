using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// GPU summary for the Hardware card (nvidia-smi only).
/// With a session → per-PID probe (uuid match on the llama-server PID); without a session
/// (or when the per-PID probe fails) → fallback to the full --query-gpu listing, so the
/// hardware is always shown (it is not dependent on a loaded model).
/// 10 s cache; the idle listing lives in the shared "machine" key so the per-session
/// and per-machine paths never evict each other.
/// (The reference name RuntimeGpuSummaryApplicationService is too broad for this nvidia-smi-only scope.)
/// </summary>
public sealed class GpuSummaryService
{
    /// <summary>Cache key for the machine-wide listing (no loaded model / per-PID fallback).</summary>
    public const string MachineCacheKey = "machine";

    public static string CacheKeyFor(LoadedSession session)
        => $"{session.Model.ModelId}|{session.Port}|{session.ProcessId}";

    private readonly GpuStatusProbeService _probe;
    private readonly GpuSummaryCache _cache;

    public GpuSummaryService(GpuStatusProbeService probe, GpuSummaryCache cache)
    {
        _probe = probe;
        _cache = cache;
    }

    public async Task<string> SummaryAsync(LoadedSession? session, CancellationToken cancellationToken = default)
    {
        if (session is not null)
        {
            var key = CacheKeyFor(session);
            if (_cache.TryGet(key, DateTimeOffset.UtcNow, out var sessionSummary))
            {
                return sessionSummary;
            }

            sessionSummary = await _probe.SummaryForProcessAsync(session.ProcessId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(sessionSummary) && sessionSummary != "Unavailable")
            {
                return _cache.Store(key, sessionSummary, DateTimeOffset.UtcNow);
            }
        }

        // No session, or per-PID probe unavailable → full machine listing.
        if (_cache.TryGet(MachineCacheKey, DateTimeOffset.UtcNow, out var machineSummary))
        {
            return machineSummary;
        }

        machineSummary = await _probe.SummaryAsync(cancellationToken);
        return _cache.Store(MachineCacheKey, machineSummary, DateTimeOffset.UtcNow);
    }
}
