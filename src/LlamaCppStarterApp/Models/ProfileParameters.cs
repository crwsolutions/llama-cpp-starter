using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlamaCppStarterApp.Models;

/// <summary>
/// Alle startparameters van een profiel in één strongly-typed class.
/// Dubbel gebruik: editor-model voor het Startinstellingen-paneel én opslagmodel
/// (JSON-blob in de Profiles.Params-kolom). Null = vlag niet doorgeven (llama.cpp-default).
/// </summary>
public partial class ProfileParameters : ObservableObject
{
    // Dropdown-opties (bron: docs/llama-server-help.txt)
    public const string DefaultPlaceholder = "(default)";

    public static readonly IReadOnlyList<string> SplitModes = new[] { "none", "layer", "row", "tensor" };
    public static readonly IReadOnlyList<string> FlashAttnValues = new[] { "auto", "on", "off" };
    public static readonly IReadOnlyList<string> CacheTypes = new[] { "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1" };
    public static readonly IReadOnlyList<string> SpecTypes = new[]
    {
        "none", "draft-simple", "draft-eagle3", "draft-mtp", "draft-dflash", "draft-dspark",
        "ngram-simple", "ngram-map-k", "ngram-map-k4v", "ngram-mod", "ngram-cache"
    };

    // Optielijsten voor de pickers, inclusief "(default)" = vlag niet doorgeven
    public static readonly IReadOnlyList<string> SplitModeOptions = new[] { DefaultPlaceholder }.Concat(SplitModes).ToArray();
    public static readonly IReadOnlyList<string> FlashAttnOptions = new[] { DefaultPlaceholder }.Concat(FlashAttnValues).ToArray();
    public static readonly IReadOnlyList<string> CacheTypeOptions = new[] { DefaultPlaceholder }.Concat(CacheTypes).ToArray();
    public static readonly IReadOnlyList<string> SpecTypeOptions = new[] { DefaultPlaceholder }.Concat(SpecTypes).ToArray();

    // --- Basisstart ---

    /// <summary>--ctx-size</summary>
    [ObservableProperty]
    public partial int? CtxSize { get; set; }

    /// <summary>--split-mode (UI-label "GPU mode"). Null = default "layer" wordt gebruikt.</summary>
    [ObservableProperty]
    public partial string? SplitMode { get; set; }

    /// <summary>-ngl (bv. "999", "auto", "all").</summary>
    [ObservableProperty]
    public partial string? Ngl { get; set; }

    /// <summary>--tensor-split (bv. "24,8").</summary>
    [ObservableProperty]
    public partial string? TensorSplit { get; set; }

    /// <summary>--threads</summary>
    [ObservableProperty]
    public partial int? Threads { get; set; }

    /// <summary>--host bind-adres. Null = "0.0.0.0".</summary>
    [ObservableProperty]
    public partial string? HostBind { get; set; }

    /// <summary>--no-host</summary>
    [ObservableProperty]
    public partial bool? NoHost { get; set; }

    /// <summary>-np (aantal server slots)</summary>
    [ObservableProperty]
    public partial int? Parallel { get; set; }

    /// <summary>--keep</summary>
    [ObservableProperty]
    public partial int? Keep { get; set; }

    /// <summary>--ctx-checkpoints</summary>
    [ObservableProperty]
    public partial int? CtxCheckpoints { get; set; }

    // --- Prestaties & Geheugen ---

    /// <summary>--batch-size</summary>
    [ObservableProperty]
    public partial int? BatchSize { get; set; }

    /// <summary>--ubatch-size</summary>
    [ObservableProperty]
    public partial int? UbatchSize { get; set; }

    /// <summary>--flash-attn {auto,on,off}</summary>
    [ObservableProperty]
    public partial string? FlashAttn { get; set; }

    /// <summary>--cache-type-k</summary>
    [ObservableProperty]
    public partial string? CacheTypeK { get; set; }

    /// <summary>--cache-type-v</summary>
    [ObservableProperty]
    public partial string? CacheTypeV { get; set; }

    // --- Speculatie / MTP ---

    /// <summary>--spec-type</summary>
    [ObservableProperty]
    public partial string? SpecType { get; set; }

    /// <summary>--spec-draft-n-max</summary>
    [ObservableProperty]
    public partial int? SpecDraftNMax { get; set; }

    // --- Vision ---

    /// <summary>-mm override. Null = auto-gekoppelde mmproj van het model; leeg = uit.</summary>
    [ObservableProperty]
    public partial string? MmprojPath { get; set; }

    /// <summary>--image-min-tokens</summary>
    [ObservableProperty]
    public partial int? ImageMinTokens { get; set; }

    // --- Standaardwaarden generatie ---

    /// <summary>--temp</summary>
    [ObservableProperty]
    public partial double? Temperature { get; set; }

    /// <summary>--top-p</summary>
    [ObservableProperty]
    public partial double? TopP { get; set; }

    /// <summary>--top-k</summary>
    [ObservableProperty]
    public partial int? TopK { get; set; }

    /// <summary>--min-p</summary>
    [ObservableProperty]
    public partial double? MinP { get; set; }

    /// <summary>--presence-penalty</summary>
    [ObservableProperty]
    public partial double? PresencePenalty { get; set; }

    /// <summary>--repeat-penalty</summary>
    [ObservableProperty]
    public partial double? RepeatPenalty { get; set; }

    /// <summary>--jinja (true = --jinja, false = --no-jinja, null = niet meegeven). Default true.</summary>
    [ObservableProperty]
    public partial bool? Jinja { get; set; } = true;

    // --- JSON (de)serialisatie voor de Params-blob ---

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ProfileParameters FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProfileParameters();
        }

        try
        {
            return JsonSerializer.Deserialize<ProfileParameters>(json, JsonOptions) ?? new ProfileParameters();
        }
        catch (JsonException)
        {
            // Corrupte/oude blob → fallback naar leeg profiel (crashen mag niet).
            return new ProfileParameters();
        }
    }

    public static bool TryParse(string? json, out ProfileParameters parameters, out string? error)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            parameters = new ProfileParameters();
            error = null;
            return true;
        }

        try
        {
            parameters = JsonSerializer.Deserialize<ProfileParameters>(json, JsonOptions) ?? new ProfileParameters();
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            parameters = new ProfileParameters();
            error = $"Ongeldige profiel-JSON, leeg profiel geladen: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Effectieve mmproj-pad: profiel-override (null = auto-gekoppelde mmproj van het model;
    /// expliciet leeg = mmproj uit).
    /// </summary>
    public string? GetEffectiveMmproj(Model model)
    {
        if (MmprojPath is not null)
        {
            return string.IsNullOrWhiteSpace(MmprojPath) ? null : MmprojPath;
        }

        return model?.MmprojPath;
    }
}
