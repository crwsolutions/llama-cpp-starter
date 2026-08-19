using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// GPU-samenvatting voor de Hardware-kaart (nvidia-smi-alleen).
/// Geen sessie → "No loaded model"; met sessie → per-PID-probe (uuid-match op het
/// llama-server-PID) met fallback op de volledige --query-gpu-lijst; 10 s-caché per sessie-key.
/// (Referentie-naam RuntimeGpuSummaryApplicationService is te breed voor deze nvidia-smi-only scope.)
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
