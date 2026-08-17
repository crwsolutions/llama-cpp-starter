using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;

namespace LlamaCppStarterApp.Services;

public class ModelScannerService
{
    private static readonly System.Text.RegularExpressions.Regex QuantRegex =
        new(@"\b(IQ\d+_[A-Z0-9_]+|Q\d+_[A-Z0-9_]+|Q\d+|BF16|F16|F32)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private readonly IModelRepository _modelRepository;

    public ModelScannerService(IModelRepository modelRepository)
    {
        _modelRepository = modelRepository;
    }

    /// <summary>
    /// Recursief scannen op *.gguf (mmproj-bestanden worden gekoppeld, niet opgenomen in de modellijst).
    /// Upsert op Path; verdwenen bestanden uit deze map worden verwijderd.
    /// </summary>
    public async Task<List<Model>> ScanAsync(string directory)
    {
        var models = new List<Model>();
        var scannedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.gguf", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (IsMmproj(fileName))
                {
                    continue;
                }

                models.Add(new Model
                {
                    Path = file,
                    Name = fileName,
                    Quant = DetectQuant(fileName),
                    SizeBytes = new FileInfo(file).Length,
                    MmprojPath = FindMmproj(Path.GetDirectoryName(file)),
                    ScannedAt = scannedAt
                });
            }
        }

        await _modelRepository.UpsertManyAsync(models);

        // Verdwenen bestanden uit deze map verwijderen (alleen binnen de gescande map)
        var dirPrefix = directory.Replace('\\', '/').TrimEnd('/').ToLowerInvariant() + "/";
        var existingPaths = models.Select(m => m.Path.Replace('\\', '/').ToLowerInvariant()).ToHashSet();
        var all = await _modelRepository.GetAllAsync();
        foreach (var model in all)
        {
            var path = model.Path.Replace('\\', '/').ToLowerInvariant();
            if (path.StartsWith(dirPrefix, StringComparison.Ordinal) && !existingPaths.Contains(path))
            {
                await _modelRepository.DeleteAsync(model.Id);
            }
        }

        return await _modelRepository.GetAllAsync();
    }

    public static bool IsMmproj(string fileName) =>
        fileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase);

    public static string DetectQuant(string fileName)
    {
        var match = QuantRegex.Match(fileName);
        return match.Success ? match.Value : "unknown";
    }

    private static string? FindMmproj(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(folder, "*.gguf")
            .FirstOrDefault(f => f.Contains("mmproj", StringComparison.OrdinalIgnoreCase));
    }
}
