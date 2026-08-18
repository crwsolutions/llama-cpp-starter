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

    public static readonly IReadOnlyList<string> RopeScalingValues = new[] { "none", "linear", "yarn" };

    // Optielijsten voor de pickers, inclusief "(default)" = vlag niet doorgeven
    public static readonly IReadOnlyList<string> SplitModeOptions = new[] { DefaultPlaceholder }.Concat(SplitModes).ToArray();
    public static readonly IReadOnlyList<string> FlashAttnOptions = new[] { DefaultPlaceholder }.Concat(FlashAttnValues).ToArray();
    public static readonly IReadOnlyList<string> CacheTypeOptions = new[] { DefaultPlaceholder }.Concat(CacheTypes).ToArray();
    public static readonly IReadOnlyList<string> SpecTypeOptions = new[] { DefaultPlaceholder }.Concat(SpecTypes).ToArray();
    public static readonly IReadOnlyList<string> RopeScalingOptions = new[] { DefaultPlaceholder }.Concat(RopeScalingValues).ToArray();

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

    /// <summary>--rope-scaling {none,linear,yarn}</summary>
    [ObservableProperty]
    public partial string? RopeScaling { get; set; }

    /// <summary>--rope-scale N</summary>
    [ObservableProperty]
    public partial int? RopeScale { get; set; }

    /// <summary>--rope-freq-base N</summary>
    [ObservableProperty]
    public partial int? RopeFreqBase { get; set; }

    /// <summary>--rope-freq-scale N</summary>
    [ObservableProperty]
    public partial int? RopeFreqScale { get; set; }

    // --- Speculatie / MTP ---

    /// <summary>--spec-type</summary>
    [ObservableProperty]
    public partial string? SpecType { get; set; }

    /// <summary>--spec-draft-n-max</summary>
    [ObservableProperty]
    public partial int? SpecDraftNMax { get; set; }

    /// <summary>
    /// Draft-model override voor --spec-draft-model. Null = auto-resolutie op het moment van laden
    /// (companion-bestand in de modelmap; embedded MTP → geen flag); leeg/expliciet pad wint.
    /// </summary>
    [ObservableProperty]
    public partial string? SpecDraftPath { get; set; }

    // --- Prompt cache ---

    /// <summary>--cache-prompt (true = aan, false = --no-cache-prompt, null = niet meegeven)</summary>
    [ObservableProperty]
    public partial bool? CachePrompt { get; set; }

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

    /// <summary>
    /// App-globale defaults (exacte waarden = referentie-opdracht uit het core-plan,
    /// incl. spec-type + vision-waarden). Seed voor elk nieuw Default-profiel;
    /// opgeslagen als AppSettings-rij `GlobalLaunchDefaults` (JSON-blob).
    /// </summary>
    public static ProfileParameters GlobalLaunchDefaults
    {
        get
        {
            if (_globalLaunchDefaults is null)
            {
                var defaults = new ProfileParameters
                {
                    CtxSize = 192144,
                    SplitMode = "layer",
                    Ngl = "999",
                    BatchSize = 256,
                    UbatchSize = 256,
                    Threads = 8,
                    Temperature = 1.0,
                    TopP = 0.95,
                    TopK = 20,
                    MinP = 0.00,
                    FlashAttn = "on",
                    TensorSplit = "24,8",
                    CacheTypeK = "q8_0",
                    CacheTypeV = "q8_0",
                    Parallel = 1,
                    PresencePenalty = 0.0,
                    RepeatPenalty = 1.0,
                    Jinja = true,
                    Keep = 1024,
                    CtxCheckpoints = 128,
                    SpecType = "draft-mtp",
                    SpecDraftNMax = 4,
                    ImageMinTokens = 1024
                };
                _globalLaunchDefaults = defaults;
            }
            return _globalLaunchDefaults;
        }
    }

    private static ProfileParameters? _globalLaunchDefaults;

    /// <summary>JSON van <see cref="GlobalLaunchDefaults"/> (seed voor AppSettings-rij GlobalLaunchDefaults).</summary>
    public static string GlobalLaunchDefaultsJson() => GlobalLaunchDefaults.ToJson();

    // Let op: het concrete type expliciet megeven. `this`/de generic-variant lost een
    // partial class op op het gedeclareerde type, waardoor de source-generated properties
    // (de [ObservableProperty]-velden) NIET (de)serialiseerd werden → lege profielen.
    public string ToJson() => JsonSerializer.Serialize(this, typeof(ProfileParameters), JsonOptions);

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
    /// True als er geen enkele parameter expliciet is ingevuld (alle velden null; Jinja staat
    /// op zijn class-default true). Gebruikt om leeg-geseede Default-profielen te herkennen.
    /// </summary>
    public bool IsEmpty() =>
        CtxSize is null && SplitMode is null && Ngl is null && TensorSplit is null
        && Threads is null && HostBind is null && NoHost is null && Parallel is null
        && Keep is null && CtxCheckpoints is null
        && BatchSize is null && UbatchSize is null && FlashAttn is null
        && CacheTypeK is null && CacheTypeV is null
        && RopeScaling is null && RopeScale is null && RopeFreqBase is null && RopeFreqScale is null
        && SpecType is null && SpecDraftNMax is null && SpecDraftPath is null
        && CachePrompt is null
        && MmprojPath is null && ImageMinTokens is null
        && Temperature is null && TopP is null && TopK is null && MinP is null
        && PresencePenalty is null && RepeatPenalty is null
        && Jinja is not false;

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
