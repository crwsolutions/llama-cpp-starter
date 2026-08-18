using System.Globalization;
using System.Text.RegularExpressions;
using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// Pure static detectie van companion-bestanden (vision-projectors, draft/MTP-heads)
/// in de map van het hoofdmiddel. Port uit het referentieproject (ModelCatalogService.Companions):
/// alleen dezelfde map wordt gepeild, family-versie + parametergrootte moeten matchen
/// (f16-projector krijgt prioriteit). Elke filesystem-fout → leeg resultaat.
/// </summary>
public static class ModelCompanionService
{
    public enum SpeculativeCompanionKind
    {
        Unknown,
        Mtp,
        DSpark,
        DFlash,
        Eagle3,
        DraftModel
    }

    // --- Vision-projectors ---

    /// <summary>
    /// Effectief projector-pad: configured-pad (profiel-override) wint; null/leeg = auto-detectie
    /// in de map van het hoofdmiddel (eerste match, f16 eerst).
    /// </summary>
    public static string? ResolveVisionProjectorPath(string modelPath, string? configuredProjectorPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredProjectorPath))
        {
            var fullPath = Path.GetFullPath(configuredProjectorPath.Trim());
            return File.Exists(fullPath) ? fullPath : null;
        }

        return FindVisionProjectors(modelPath).FirstOrDefault();
    }

    public static string? FindVisionProjector(string modelPath)
        => FindVisionProjectors(modelPath).FirstOrDefault();

    public static IReadOnlyList<string> FindVisionProjectors(string modelPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];

        return CandidateCompanions(folder)
            .Where(file =>
            {
                var name = Path.GetFileName(file);
                return !string.Equals(Path.GetFullPath(file), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase)
                    && LooksLikeVisionProjectorName(name)
                    && LooksCompatibleWithMainModel(Path.GetFileName(modelPath), name);
            })
            .OrderBy(file => Path.GetFileName(file).Contains("f16", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // --- Draft / MTP-heads ---

    public static string? FindDraftModel(string modelPath)
        => FindDraftModels(modelPath, null).FirstOrDefault();

    public static string? FindDraftModel(string modelPath, string? speculativeType)
        => FindDraftModels(modelPath, speculativeType).FirstOrDefault();

    public static IReadOnlyList<string> FindDraftModels(string modelPath, string? speculativeType)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];

        var mainPath = Path.GetFullPath(modelPath);
        var normalizedType = NormalizeSpecType(speculativeType);
        return CandidateCompanions(folder)
            .Select(file => (File: file, Kind: ClassifySpeculativeCompanion(file)))
            .Where(candidate =>
            {
                var file = candidate.File;
                var name = Path.GetFileName(file);
                return !string.Equals(Path.GetFullPath(file), mainPath, StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                    && !LooksLikeVisionProjectorName(name)
                    && candidate.Kind != SpeculativeCompanionKind.Unknown
                    && LooksCompatibleWithMainModel(
                        Path.GetFileName(modelPath),
                        name,
                        requireSameParameterSize: candidate.Kind != SpeculativeCompanionKind.DraftModel)
                    && MatchesSpeculativeType(candidate.Kind, normalizedType);
            })
            .OrderBy(candidate => SpeculativeCompanionPriority(candidate.Kind))
            .ThenBy(candidate => candidate.File, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.File)
            .ToArray();
    }

    /// <summary>
    /// Effectief draft-pad voor --spec-draft-model: alleen "draft-*"-typen;
    /// configured-pad (profiel-override) wint altijd; "draft-mtp" + embedded
    /// MTP-laag in het hoofdmiddel → null (llama.cpp gebruikt de eigen laag).
    /// </summary>
    public static string? ResolveDraftModelPath(string modelPath, string? speculativeType, string? configuredDraftPath)
    {
        var normalizedType = NormalizeSpecType(speculativeType);
        if (!normalizedType.StartsWith("draft-", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!string.IsNullOrWhiteSpace(configuredDraftPath))
            return configuredDraftPath.Trim();
        if (normalizedType.Equals("draft-mtp", StringComparison.OrdinalIgnoreCase)
            && HasEmbeddedDraftMtp(modelPath))
            return null;
        return FindDraftModels(modelPath, normalizedType).FirstOrDefault();
    }

    /// <summary>True als het hoofdmiddel een eigen (embedded) MTP-head heeft: "*.nextn_predict_layers" > 0.</summary>
    public static bool HasEmbeddedDraftMtp(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath)) return false;

        return HasPositiveNextNPredictLayers(GgufMetadataReader.TryRead(modelPath));
    }

    // --- Naam-markers ---

    public static bool LooksLikeVisionProjectorName(string name)
    {
        var normalized = (name ?? "").Replace('_', '-').Replace('.', '-');
        return normalized.Contains("mmproj", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("projector", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("clip", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("vision-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("visual-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("image-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("head-vision", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("head-visual", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("head-image", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("mtp-vision", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("vision-mtp", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeDraftOrMtpHeadName(string name)
    {
        var normalized = (name ?? "").Replace('_', '-').Replace('.', '-');
        return normalized.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("-mtp-", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("-mtp-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("mtp-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("dspark", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("dflash", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("eagle3", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("eagle-3", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("-draft-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("-spec-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("spec-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True als het bestand een standalone-speculatie-architectuur bevat
    /// (eagle3/dflash of *-assistant) → geen standalone model, uitsluiten uit de modellijst.
    /// </summary>
    public static bool HasStandaloneSpeculativeArchitecture(string path)
    {
        var architecture = MetadataString(GgufMetadataReader.TryRead(path), "general.architecture");
        return architecture.Equals("eagle3", StringComparison.OrdinalIgnoreCase)
            || architecture.Equals("dflash", StringComparison.OrdinalIgnoreCase)
            || architecture.EndsWith("-assistant", StringComparison.OrdinalIgnoreCase)
            || architecture.EndsWith("_assistant", StringComparison.OrdinalIgnoreCase);
    }

    // --- Classificatie ---

    public static SpeculativeCompanionKind ClassifySpeculativeCompanion(string path)
    {
        var name = Path.GetFileName(path).Replace('_', '-').Replace('.', '-');
        if (name.Contains("dspark", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.DSpark;
        if (name.Contains("dflash", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.DFlash;
        if (name.Contains("eagle3", StringComparison.OrdinalIgnoreCase)
            || name.Contains("eagle-3", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.Eagle3;

        var metadata = GgufMetadataReader.TryRead(path);
        var architecture = MetadataString(metadata, "general.architecture");
        if (architecture.Equals("eagle3", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.Eagle3;
        if (architecture.Equals("dflash", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.DFlash;
        if (HasPositiveNextNPredictLayers(metadata)
            || name.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase)
            || name.Contains("-mtp-head", StringComparison.OrdinalIgnoreCase)
            || name.Contains("mtp-head", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.Mtp;
        if (name.Contains("-draft-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)
            || name.Contains("-spec-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("spec-", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.DraftModel;
        return SpeculativeCompanionKind.Unknown;
    }

    public static bool MatchesSpeculativeType(SpeculativeCompanionKind kind, string speculativeType)
        => speculativeType switch
        {
            "draft-mtp" => kind == SpeculativeCompanionKind.Mtp,
            "draft-dspark" => kind == SpeculativeCompanionKind.DSpark,
            "draft-dflash" => kind == SpeculativeCompanionKind.DFlash,
            "draft-eagle3" => kind == SpeculativeCompanionKind.Eagle3,
            "draft-simple" => kind == SpeculativeCompanionKind.DraftModel,
            "" or "none" => kind != SpeculativeCompanionKind.Unknown,
            _ => false
        };

    // --- Helpers ---

    private static string NormalizeSpecType(string? value)
        => (value ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// Deterministisch model-id: relatief pad t.o.v. de models-root (extensie eraf),
    /// veilig gestreept (`[^a-z0-9._-]` → `-`), max 86 tekens + `-{8-hex SHA256(lowercase full path)}`.
    /// </summary>
    public static string ModelIdForPath(string scopeRoot, string modelPath)
    {
        var fullPath = Path.GetFullPath(modelPath);
        var seed = RelativePathOrFullPath(scopeRoot, fullPath);
        seed = Path.ChangeExtension(seed, null) ?? seed;
        var safe = SafeId(seed);
        var hash = ShortHash(fullPath);
        var safePrefix = safe[..Math.Min(86, safe.Length)];
        return $"{safePrefix}-{hash}";
    }

    /// <summary>Vriendelijke weergavenaam: underscores weg, per deel PascalCase (bv. `my-model_q4_k_m` → `My Model Q4 K M`).</summary>
    public static string FriendlyName(string value)
        => string.Join(" ", (value ?? "Local model").Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>
    /// Kwantificatie uit de bestandsnaam (bv. `Q4_K_M`, `IQ2_XXS`, `F16`, `BF16`, `f32`);
    /// leeg als niet herkend (fallback `general.file_type` uit de GGUF-metadata).
    /// </summary>
    public static string InferQuant(string file)
    {
        var name = Path.GetFileName(file).ToLowerInvariant();
        var match = Regex.Match(name, @"(?:^|[-_.])(iq\d_[a-z0-9]+|q\d(?:_[a-z0-9]+)+|f16|bf16|f32)(?:[-_.]|$)");
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";
    }

    private static string RelativePathOrFullPath(string scopeRoot, string modelPath)
    {
        var root = Path.GetFullPath(scopeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        return modelPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, modelPath)
            : modelPath;
    }

    private static string ShortHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    internal static string SafeId(string value)
    {
        var safe = new string((value ?? "model").ToLowerInvariant().Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "model" : safe[..Math.Min(96, safe.Length)];
    }

    private static int SpeculativeCompanionPriority(SpeculativeCompanionKind kind) => kind switch
    {
        SpeculativeCompanionKind.Mtp => 0,
        SpeculativeCompanionKind.DSpark => 1,
        SpeculativeCompanionKind.DFlash => 2,
        SpeculativeCompanionKind.Eagle3 => 3,
        SpeculativeCompanionKind.DraftModel => 4,
        _ => 5
    };

    /// <summary>Family+versie moeten matchen (bv. "qwen3:0.6b"); parametergrootte ook, tenzij null.</summary>
    public static bool LooksCompatibleWithMainModel(
        string mainName,
        string companionName,
        bool requireSameParameterSize = true)
    {
        var mainFamily = FamilyVersion(mainName);
        var companionFamily = FamilyVersion(companionName);
        if (mainFamily is not null && companionFamily is not null
            && !mainFamily.Equals(companionFamily, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!requireSameParameterSize) return true;

        var mainSize = ParameterSize(mainName);
        var companionSize = ParameterSize(companionName);
        return mainSize is null || companionSize is null
            || mainSize.Equals(companionSize, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Bv. "qwen3:0.6" uit "Qwen3-0.6B-Q4_K_M.gguf" (geen match → null).</summary>
    public static string? FamilyVersion(string name)
    {
        var match = Regex.Match(
            name ?? "",
            @"(?ix)(?:^|[^a-z0-9])
              (?<family>qwen|gemma|llama|mistral|ministral|mixtral|pixtral|deepseek|glm|phi|command-r|internvl|minicpm)
              (?:[\s._-]+(?:small|large|nemo))?
              [\s._-]*(?:v|r)?(?<version>\d+(?:[._-]\d+)?)
              (?:[^0-9]|$)");
        if (!match.Success) return null;
        var version = match.Groups["version"].Value.Replace('_', '.').Replace('-', '.');
        return $"{match.Groups["family"].Value.ToLowerInvariant()}:{version}";
    }

    /// <summary>Bv. "0.6" uit "Qwen3-0.6B-Q4_K_M.gguf" (geen match → null).</summary>
    public static string? ParameterSize(string name)
    {
        var match = Regex.Match(name ?? "", @"(?i)(?:^|[^a-z0-9])(?<size>\d+(?:\.\d+)?)\s*b(?:[^a-z0-9]|$)");
        return match.Success ? match.Groups["size"].Value : null;
    }

    private static string MetadataString(IReadOnlyDictionary<string, object?> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";

    private static bool HasPositiveNextNPredictLayers(IReadOnlyDictionary<string, object?> metadata)
        => metadata.Any(pair => pair.Key.EndsWith(".nextn_predict_layers", StringComparison.OrdinalIgnoreCase)
            && IsPositiveNumber(pair.Value));

    private static bool IsPositiveNumber(object? value) => value switch
    {
        byte number => number > 0,
        sbyte number => number > 0,
        ushort number => number > 0,
        short number => number > 0,
        uint number => number > 0,
        int number => number > 0,
        ulong number => number > 0,
        long number => number > 0,
        float number => number > 0,
        double number => number > 0,
        _ => false
    };

    private static IEnumerable<string> CandidateCompanions(string folder)
    {
        // Automatische koppeling blijft bewust beperkt tot de map van het geselecteerde model;
        // parent/child-scans zouden bij de verkeerde model een sidecar kunnen koppelen.
        return Directory.EnumerateFiles(folder, "*.gguf", SearchOption.TopDirectoryOnly).Take(500);
    }
}
