using System.Globalization;
using System.Text.RegularExpressions;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// Formatter voor nvidia-smi-CSV-regels (en normalisatie). Port van de twee gebruikte
/// onderdelen van het referentieproject (LocalLlmConsole.Services.GpuStatusService).
/// </summary>
public static class GpuStatusService
{
    public static string FormatNvidiaSmiCsvLine(string line)
    {
        var parts = line.Split(',').Select(part => part.Trim()).ToArray();
        if (parts.Length < 6) return "";
        var index = parts[0];
        var name = parts[1];
        var utilization = parts[2];
        var temperature = parts[3];
        var used = double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var usedMb) ? usedMb / 1024 : 0;
        var total = double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalMb) ? totalMb / 1024 : 0;
        var memory = total > 0 ? $"{used:0.0}/{total:0.0} GiB" : $"{parts[4]}/{parts[5]} MiB";
        var identity = string.IsNullOrWhiteSpace(name) ? $"GPU {index}" : $"GPU {index}: {name}";
        return NormalizeMetricSeparators($"{identity} | {utilization}% | {temperature}C | {memory}");
    }

    public static string NormalizeMetricSeparators(string text)
        => Regex.Replace(text.Trim(), @"\s*\|\s*", " | ");
}
