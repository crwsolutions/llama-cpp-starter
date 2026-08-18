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

    /// <summary>Aantal companion-bestanden (projector/draft/MTP) dat uit de modellijst is gehouden tijdens de laatste scan.</summary>
    public int SkippedCompanionCount { get; private set; }

    /// <summary>
    /// Recursief scannen op *.gguf (reparse-points/systeembestanden worden overgeslagen).
    /// Uitsluiting: projector-/draft-/MTP-namen + standalone-spec-architectuur (GGUF-lezen per bestand).
    /// Upsert op Path (incl. ModelId/MetadataJson); verdwenen bestanden uit deze map worden verwijderd.
    /// Modellen zonder Default-profiel worden geseed met de app-globale launch-defaults.
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

        // Default-profile-seeding: alleen de nog bestaande DB-rijen uit de gescande map
        // (de lokale upsert-list heeft nog geen autoincrement Id's).
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
    /// Metadata-JSON-blob met exact de 9 gespecificeerde velden (bron: GGUF-kop + bestandsnaam).
    /// Corrupte/leesbare-niet GGUF → ggufMetadataAvailable=false + "unknown"-velden (crashen mag niet).
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
    /// Een .gguf-bestand is een model tenzij de naam een projector/draft/MTP-head is
    /// of de GGUF-kop een standalone-spec-architectuur bevat (eagle3/dflash/*-assistant).
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
    /// Default-profiel-seeding voor alle gescande modellen zonder Default: ParamsJson =
    /// GlobalLaunchDefaults uit AppSettings (fallback: app-globale defaults).
    /// </summary>
    private async Task SeedDefaultProfilesAsync(List<Model> models)
    {
        // Ontbrekende/leeg/corrupte AppSettings-rij (bv. "{}") → fallback op de app-globale defaults.
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
