using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;

namespace LlamaCppStarterApp.Services;

public class RuntimeScannerService
{
    private readonly IRuntimeRepository _runtimeRepository;

    public RuntimeScannerService(IRuntimeRepository runtimeRepository)
    {
        _runtimeRepository = runtimeRepository;
    }

    /// <summary>
    /// Recursief zoeken naar llama-server.exe in de opgegeven map. Upsert op ExecutablePath.
    /// </summary>
    public async Task<List<Runtime>> ScanAsync(string directory)
    {
        var runtimes = new List<Runtime>();

        if (Directory.Exists(directory))
        {
            foreach (var exe in Directory.EnumerateFiles(directory, "llama-server.exe", SearchOption.AllDirectories))
            {
                var location = Path.GetDirectoryName(exe) ?? string.Empty;
                runtimes.Add(new Runtime
                {
                    Name = Path.GetFileName(location),
                    ExecutablePath = exe,
                    Backend = DetectBackend(location, exe),
                    Status = "Built Native",
                    Location = location
                });
            }
        }

        foreach (var runtime in runtimes)
        {
            await _runtimeRepository.UpsertAsync(runtime);
        }

        return await _runtimeRepository.GetAllAsync();
    }

    public static string DetectBackend(string location, string executablePath)
    {
        var haystack = (location + Path.DirectorySeparatorChar + Path.GetFileName(executablePath)).ToLowerInvariant();

        if (haystack.Contains("cuda")) return "Cuda";
        if (haystack.Contains("vulkan")) return "Vulkan";
        if (haystack.Contains("rocm") || haystack.Contains("hip")) return "Rocm";
        if (haystack.Contains("metal")) return "Metal";
        return "CPU";
    }
}
