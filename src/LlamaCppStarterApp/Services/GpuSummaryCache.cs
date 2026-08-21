using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// Simple single-slot key + 10 s freshness cache for the nvidia-smi listing,
/// so the poller does not have to run nvidia-smi every poll tick.
/// Stored records are immutable (GpuSummary), so sharing them is safe.
/// </summary>
public sealed class GpuSummaryCache
{
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(10);
    private static readonly IReadOnlyList<GpuSummary> None = Array.Empty<GpuSummary>();

    private string _key = "";
    private IReadOnlyList<GpuSummary> _summary = None;
    private DateTimeOffset _capturedAt = DateTimeOffset.MinValue;

    public bool TryGet(string key, DateTimeOffset now, out IReadOnlyList<GpuSummary> summary)
    {
        if (string.Equals(_key, key ?? "", StringComparison.Ordinal)
            && _capturedAt != DateTimeOffset.MinValue
            && now - _capturedAt < Freshness)
        {
            summary = _summary;
            return true;
        }

        summary = None;
        return false;
    }

    public IReadOnlyList<GpuSummary> Store(string key, IReadOnlyList<GpuSummary> summary, DateTimeOffset capturedAt)
    {
        _key = key ?? "";
        _summary = summary ?? None;
        _capturedAt = capturedAt;
        return _summary;
    }

    public void Clear()
    {
        _key = "";
        _summary = None;
        _capturedAt = DateTimeOffset.MinValue;
    }
}
