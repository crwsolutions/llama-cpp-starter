using System.Diagnostics;
using System.Globalization;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// Hardware card probe: nvidia-smi ONLY (user decision 2026-08-19; no AMD/Intel/CPU probes).
/// Port of the nvidia-smi parts of the reference project
/// (LocalLlmConsole.Services.GpuStatusProbeService); any error → "Unavailable".
/// </summary>
public sealed class GpuStatusProbeService
{
    public async Task<string> SummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                NvidiaSmiStartInfo(
                    "--query-gpu=index,name,utilization.gpu,temperature.gpu,memory.used,memory.total",
                    "--format=csv,noheader,nounits"),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (result.ExitCode != 0) return "Unavailable";

            var lines = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(GpuStatusService.FormatNvidiaSmiCsvLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(4)
                .ToArray();
            return lines.Length == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NVIDIA GPU summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    /// <summary>
    /// GPU rig of the given process: match --query-compute-apps=gpu_uuid,pid on the PID,
    /// then --query-gpu for those uuids. No match/error → "Unavailable" (fallback on the caller).
    /// </summary>
    public async Task<string> SummaryForProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        if (processId <= 0) return "Unavailable";

        try
        {
            var processResult = await ProcessRunner.RunAsync(
                NvidiaSmiStartInfo(
                    "--query-compute-apps=gpu_uuid,pid",
                    "--format=csv,noheader,nounits"),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (processResult.ExitCode != 0) return "Unavailable";

            var usedGpuUuids = processResult.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(',').Select(part => part.Trim()).ToArray())
                .Where(parts => parts.Length >= 2
                                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                                && pid == processId)
                .Select(parts => parts[0])
                .Where(uuid => !string.IsNullOrWhiteSpace(uuid))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (usedGpuUuids.Count == 0) return "Unavailable";

            var gpuResult = await ProcessRunner.RunAsync(
                NvidiaSmiStartInfo(
                    "--query-gpu=uuid,index,name,utilization.gpu,temperature.gpu,memory.used,memory.total",
                    "--format=csv,noheader,nounits"),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (gpuResult.ExitCode != 0) return "Unavailable";

            var lines = gpuResult.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(',').Select(part => part.Trim()).ToArray())
                .Where(parts => parts.Length >= 7 && usedGpuUuids.Contains(parts[0]))
                .Select(parts => GpuStatusService.FormatNvidiaSmiCsvLine(string.Join(",", parts.Skip(1))))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(4)
                .ToArray();
            return lines.Length == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NVIDIA process GPU summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    private static ProcessStartInfo NvidiaSmiStartInfo(params string[] args)
    {
        var psi = new ProcessStartInfo(FindNvidiaSmi());
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        return psi;
    }

    private static string FindNvidiaSmi()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var nvidia = string.IsNullOrWhiteSpace(programFiles)
            ? ""
            : Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        if (!string.IsNullOrWhiteSpace(nvidia) && File.Exists(nvidia))
        {
            return nvidia;
        }

        foreach (var directory in PathEntries())
        {
            var candidate = Path.Combine(directory, "nvidia-smi.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return "nvidia-smi.exe"; // missing → ProcessRunner grep → "Unavailable"
    }

    private static IEnumerable<string> PathEntries()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var expanded = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(expanded) || !Directory.Exists(expanded)) continue;
            yield return Path.GetFullPath(expanded);
        }
    }

    /// <summary>Small internal process runner: stdout capture + timeout → Kill(entireProcessTree).</summary>
    private static class ProcessRunner
    {
        public static async Task<(int ExitCode, string Output)> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // process already stopped
                }
                throw;
            }

            return (process.ExitCode, await outputTask);
        }
    }
}
