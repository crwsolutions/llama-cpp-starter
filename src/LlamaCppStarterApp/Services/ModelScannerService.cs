using System.Text.Json;
using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;

namespace LlamaCppStarterApp.Services;

public class ModelScannerService
{
    private const string GlobalLaunchDefaultsSetting = "GlobalLaunchDefaults";

    private readonly IModelRepository _modelRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly IAppSettingsRepository _appSettings;

    public ModelScannerService(IModelRepository modelRepository, IProfileRepository profileRepository, IAppSettingsRepository appSettings)
    {
        _modelRepository = modelRepository;
        _profileRepository = profileRepository;
        _appSettings = appSettings;
    }

    /// <summary>Number of companion files (projector/draft/MTP) kept out of the model list during the last scan.</summary>
    public int SkippedCompanionCount { get; private set; }

    /// <summary>
    /// Recursively scan for *.gguf (reparse points/system files are skipped).
    /// Exclusion: projector/draft/MTP names + standalone spec architectures (GGUF read per file).
    /// Upsert on Path (incl. ModelId/MetadataJson); files that disappeared from this folder are deleted.
    /// Models without a Default profile are seeded with the app-global launch defaults.
    /// </summary>
    public async Task<List<Model>> ScanAsync(string directory)
    {
        var models = new List<Model>();
        var skippedCompanions = 0;
        var scannedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (Directory.Exists(directory))
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
            };

            foreach (var file in Directory.EnumerateFiles(directory, "*.gguf", options)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsModelGguf(file))
                {
                    skippedCompanions++;
                    continue;
                }

                models.Add(await Task.Run(() => BuildModel(directory, file, scannedAt)));
            }
        }

        SkippedCompanionCount = skippedCompanions;

        await _modelRepository.UpsertManyAsync(models);

        // Remove files that disappeared from this folder (only within the scanned folder)
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

        // Default profile seeding: only the still-existing DB rows from the scanned folder
        // (the local upsert list has no autoincrement Ids yet).
        all = await _modelRepository.GetAllAsync();
        var scannedDbModels = all
            .Where(m => m.Path.Replace('\\', '/').ToLowerInvariant().StartsWith(dirPrefix, StringComparison.Ordinal))
            .ToList();
        await SeedDefaultProfilesAsync(scannedDbModels);

        return all;
    }

    private static Model BuildModel(string root, string file, long scannedAt)
    {
        var metadata = GgufMetadataReader.TryRead(file);
        var quant = ModelCompanionService.InferQuant(file);
        if (string.IsNullOrWhiteSpace(quant))
        {
            var fileType = metadata.TryGetValue("general.file_type", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
            quant = string.IsNullOrWhiteSpace(fileType) ? "unknown" : fileType.ToUpperInvariant();
        }

        return new Model
        {
            Path = file,
            ModelId = ModelCompanionService.ModelIdForPath(root, file),
            Name = ModelCompanionService.FriendlyName(Path.GetFileNameWithoutExtension(file)),
            Quant = quant,
            SizeBytes = new FileInfo(file).Length,
            MmprojPath = FindMmproj(Path.GetDirectoryName(file)),
            ScannedAt = scannedAt,
            MetadataJson = BuildMetadataJson(file, quant, scannedAt, metadata)
        };
    }

    /// <summary>
    /// Metadata JSON blob with exactly the 9 specified fields (source: GGUF header + file name).
    /// Corrupt/unreadable non-GGUF → ggufMetadataAvailable=false + "unknown" fields (must not crash).
    /// </summary>
    public static string BuildMetadataJson(string file, string quant, long scannedAt, IReadOnlyDictionary<string, object?> metadata)
    {
        var architecture = metadata.TryGetValue("general.architecture", out var architectureValue)
            ? architectureValue?.ToString() ?? string.Empty
            : string.Empty;
        var contextLength = ModelCapabilityService.ContextLength(metadata, architecture);

        var fields = new Dictionary<string, object?>
        {
            ["sourceFolder"] = Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty,
            ["modelFile"] = file,
            ["quant"] = quant,
            ["registeredAt"] = scannedAt,
            ["ggufMetadataAvailable"] = metadata.Count > 0
        };
        fields["ggufArchitecture"] = string.IsNullOrWhiteSpace(architecture) ? "unknown" : architecture;
        fields["ggufQuantization"] = string.IsNullOrWhiteSpace(quant) ? "unknown" : quant;
        if (contextLength > 0)
        {
            fields["ggufContextLength"] = contextLength;
        }
        fields["ggufHasChatTemplate"] = metadata.ContainsKey("tokenizer.chat_template");

        return JsonSerializer.Serialize(fields, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>
    /// A .gguf file is a model unless its name is a projector/draft/MTP head
    /// or its GGUF header contains a standalone spec architecture (eagle3/dflash/*-assistant).
    /// </summary>
    public static bool IsModelGguf(string file)
    {
        var name = Path.GetFileName(file);
        return name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
            && !ModelCompanionService.LooksLikeVisionProjectorName(name)
            && !ModelCompanionService.LooksLikeDraftOrMtpHeadName(name)
            && !ModelCompanionService.HasStandaloneSpeculativeArchitecture(file);
    }

    /// <summary>
    /// Default profile seeding for all scanned models without a Default: ParamsJson =
    /// GlobalLaunchDefaults from AppSettings (fallback: app-global defaults).
    /// </summary>
    private async Task SeedDefaultProfilesAsync(List<Model> models)
    {
        // Missing/empty/corrupt AppSettings row (e.g. "{}") → fall back to the app-global defaults.
        var defaultsJson = await _appSettings.GetValueAsync(GlobalLaunchDefaultsSetting);
        var parameters = !string.IsNullOrWhiteSpace(defaultsJson)
            && ProfileParameters.TryParse(defaultsJson, out var parsed, out _)
            && !parsed.IsEmpty()
            ? parsed
            : ProfileParameters.GlobalLaunchDefaults;
        var paramsJson = parameters.ToJson();

        foreach (var model in models)
        {
            var profiles = await _profileRepository.GetByModelAsync(model.Id);
            if (profiles.All(p => !p.IsDefault))
            {
                await _profileRepository.UpsertAsync(new Profile
                {
                    Name = "Default",
                    ModelId = model.Id,
                    IsDefault = true,
                    Port = 8080,
                    ParamsJson = paramsJson
                });
            }
        }
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
