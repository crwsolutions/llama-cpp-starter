using System.Globalization;

namespace LlamaCppStarterApp.Models;

/// <summary>
/// One GPU row for the Hardware card (nvidia-smi data; card content is English per spec).
/// All display strings use the invariant culture. Null fields (nvidia-smi "N/A"/missing)
/// render as "—" with an empty bar (0); the bars are 0..1 fractions for ProgressBar.Value.
/// </summary>
public sealed record GpuSummary(
    string Id,
    string Name,
    double? GpuUsagePercent,
    double? TemperatureCelsius,
    double? MemoryUsedGb,
    double? MemoryAvailableGb)
{
    private const string Unknown = "—";

    /// <summary>"GPU 0: NVIDIA GeForce RTX 5060 Ti" (id only when the name is empty).</summary>
    public string DisplayText
        => string.IsNullOrWhiteSpace(Name) ? Id : $"{Id} {Name}";

    /// <summary>"62%" (unknown → "—").</summary>
    public string UsageText
        => GpuUsagePercent is double usage
            ? string.Format(CultureInfo.InvariantCulture, "{0:0}%", usage)
            : Unknown;

    /// <summary>"58°C" (unknown → "—").</summary>
    public string TemperatureText
        => TemperatureCelsius is double temperature
            ? string.Format(CultureInfo.InvariantCulture, "{0:0}°C", temperature)
            : Unknown;

    /// <summary>"14.2/16.0 GiB" = used/total (unknown → "—").</summary>
    public string MemoryText
        => MemoryUsedGb is double used && MemoryTotalGb is double total
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.0}/{1:0.0} GiB", used, total)
            : Unknown;

    /// <summary>Total memory (GiB) = used + available (null when unknown).</summary>
    public double? MemoryTotalGb
        => MemoryUsedGb is double used && MemoryAvailableGb is double available
            ? used + available
            : null;

    /// <summary>GPU-usage bar value, 0..1 (0 = unknown/empty).</summary>
    public double GpuUsageBarValue
        => GpuUsagePercent is double usage
            ? Math.Clamp(usage / 100.0, 0, 1)
            : 0;

    /// <summary>Memory-usage bar value (used / total), 0..1 (0 = unknown/empty).</summary>
    public double MemoryUsedBarValue
        => MemoryUsedGb is double used && MemoryTotalGb is double total && total > 0
            ? Math.Clamp(used / total, 0, 1)
            : 0;

    /// <summary>
    /// Parse one nvidia-smi CSV row (already split and trimmed):
    /// index,name,utilization.gpu,temperature.gpu,memory.used,memory.total (6 parts; MB→GiB).
    /// Each unparseable number → null field (display "—", bar 0); fewer than 6 parts → null.
    /// </summary>
    public static GpuSummary? TryParseParts(string[] parts)
    {
        if (parts.Length < 6)
        {
            return null;
        }

        return new GpuSummary(
            Id: parts[0],
            Name: parts[1],
            GpuUsagePercent: ParseDouble(parts[2]),
            TemperatureCelsius: ParseDouble(parts[3]),
            MemoryUsedGb: ParseGigabytes(parts[4]),
            MemoryAvailableGb: AvailableGigabytes(parts[4], parts[5]));
    }

    private static double? ParseGigabytes(string value)
        => ParseDouble(value) is double mb ? mb / 1024.0 : null;

    // available = max(total − used, 0); null when either side is unknown.
    private static double? AvailableGigabytes(string usedPart, string totalPart)
        => ParseDouble(usedPart) is double usedMb && ParseDouble(totalPart) is double totalMb
            ? Math.Max((totalMb - usedMb) / 1024.0, 0)
            : null;

    private static double? ParseDouble(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
