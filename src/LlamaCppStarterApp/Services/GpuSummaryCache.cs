namespace LlamaCppStarterApp.Services;

/// <summary>
/// Eenvoudige key + 10 s-freshness cache voor de nvidia-smi-opsomming
/// (per sessie-key), zodat de poller nvidia-smi niet elke 2 s hoeft te draaien.
/// Port 1:1 uit het referentieproject (LocalLlmConsole.Services.GpuSummaryCache).
/// </summary>
public sealed class GpuSummaryCache
{
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(10);

    private string _key = "";
    private string _summary = "Unavailable";
    private DateTimeOffset _capturedAt = DateTimeOffset.MinValue;

    public bool TryGet(string key, DateTimeOffset now, out string summary)
    {
        if (string.Equals(_key, key ?? "", StringComparison.Ordinal)
            && _capturedAt != DateTimeOffset.MinValue
            && now - _capturedAt < Freshness)
        {
            summary = _summary;
            return true;
        }

        summary = "Unavailable";
        return false;
    }

    public string Store(string key, string summary, DateTimeOffset capturedAt)
    {
        _key = key ?? "";
        _summary = string.IsNullOrWhiteSpace(summary) ? "Unavailable" : GpuStatusService.NormalizeMetricSeparators(summary);
        _capturedAt = capturedAt;
        return _summary;
    }

    public void Clear()
    {
        _key = "";
        _summary = "Unavailable";
        _capturedAt = DateTimeOffset.MinValue;
    }
}
