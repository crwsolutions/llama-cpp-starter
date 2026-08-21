using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlamaCppStarterApp.Models;

/// <summary>
/// All launch parameters of a profile in one strongly-typed class.
/// Dual use: editor model for the Startinstellingen panel and storage model
/// (JSON blob in the Profiles.Params column). Null = flag not passed (llama.cpp default).
/// </summary>
public partial class ProfileParameters : ObservableObject
{
    // Dropdown options (source: docs/llama-server-help.txt)
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

    // Option lists for the pickers, including "(default)" = flag not passed
    public static readonly IReadOnlyList<string> SplitModeOptions = new[] { DefaultPlaceholder }.Concat(SplitModes).ToArray();
    public static readonly IReadOnlyList<string> FlashAttnOptions = new[] { DefaultPlaceholder }.Concat(FlashAttnValues).ToArray();
    public static readonly IReadOnlyList<string> CacheTypeOptions = new[] { DefaultPlaceholder }.Concat(CacheTypes).ToArray();
    public static readonly IReadOnlyList<string> SpecTypeOptions = new[] { DefaultPlaceholder }.Concat(SpecTypes).ToArray();
    public static readonly IReadOnlyList<string> RopeScalingOptions = new[] { DefaultPlaceholder }.Concat(RopeScalingValues).ToArray();
    public static readonly IReadOnlyList<string> ThinkingValues = new[] { "off", "low", "medium", "xhigh" };
    public static readonly IReadOnlyList<string> ThinkingOptions = new[] { DefaultPlaceholder }.Concat(ThinkingValues).ToArray();

    // --- Basic launch ---

    /// <summary>--ctx-size</summary>
    [ObservableProperty]
    public partial int? CtxSize { get; set; }

    /// <summary>--split-mode (UI label "GPU mode"). Null = default "layer" is used.</summary>
    [ObservableProperty]
    public partial string? SplitMode { get; set; }

    /// <summary>-ngl (e.g. "999", "auto", "all").</summary>
    [ObservableProperty]
    public partial string? Ngl { get; set; }

    /// <summary>--tensor-split (e.g. "24,8").</summary>
    [ObservableProperty]
    public partial string? TensorSplit { get; set; }

    /// <summary>--threads</summary>
    [ObservableProperty]
    public partial int? Threads { get; set; }

    /// <summary>--host bind address. Null = flag not passed (llama.cpp default 127.0.0.1).</summary>
    [ObservableProperty]
    public partial string? HostBind { get; set; }

    /// <summary>--no-host</summary>
    [ObservableProperty]
    public partial bool? NoHost { get; set; }

    /// <summary>-np (number of server slots)</summary>
    [ObservableProperty]
    public partial int? Parallel { get; set; }

    /// <summary>--keep</summary>
    [ObservableProperty]
    public partial int? Keep { get; set; }

    /// <summary>--ctx-checkpoints</summary>
    [ObservableProperty]
    public partial int? CtxCheckpoints { get; set; }

    // --- Performance & Memory ---

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

    // --- Speculation / MTP ---

    /// <summary>--spec-type</summary>
    [ObservableProperty]
    public partial string? SpecType { get; set; }

    /// <summary>--spec-draft-n-max</summary>
    [ObservableProperty]
    public partial int? SpecDraftNMax { get; set; }

    /// <summary>
    /// Draft model override for --spec-draft-model. Null = auto-resolution at load time
    /// (companion file in the model folder; embedded MTP → no flag); empty/explicit path wins.
    /// </summary>
    [ObservableProperty]
    public partial string? SpecDraftPath { get; set; }

    // --- Prompt cache ---

    /// <summary>--cache-prompt (true = on, false = --no-cache-prompt, null = not passed)</summary>
    [ObservableProperty]
    public partial bool? CachePrompt { get; set; }

    // --- Vision ---

    /// <summary>-mm override. Null = auto-linked mmproj of the model; empty = off.</summary>
    [ObservableProperty]
    public partial string? MmprojPath { get; set; }

    /// <summary>--image-min-tokens</summary>
    [ObservableProperty]
    public partial int? ImageMinTokens { get; set; }

    // --- Thinking ---

    /// <summary>
    /// Thinking level (off = --reasoning off; low/medium/xhigh = --chat-template-kwargs
    /// reasoning_effort). Null = flags not passed (llama.cpp default).
    /// </summary>
    [ObservableProperty]
    public partial string? ThinkingLevel { get; set; }

    // --- Generation defaults ---

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

    /// <summary>--jinja (true = --jinja, false = --no-jinja, null = not passed). Default true.</summary>
    [ObservableProperty]
    public partial bool? Jinja { get; set; } = true;

    /// <summary>
    /// Metrics endpoint: true = pass the flag (default; needed for the Overview metrics cards),
    /// false = /metrics off (llama-server default), null = not passed (≡ off).
    /// Old Params blobs without the key → true via the initializer.
    /// </summary>
    [ObservableProperty]
    public partial bool? EnableMetrics { get; set; } = true;

    // --- JSON (de)serialization for the Params blob ---

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// App-global defaults (exact values = reference command from the core plan,
    /// incl. spec-type + vision values). Seed for each new Default profile;
    /// stored as the AppSettings row `GlobalLaunchDefaults` (JSON blob).
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

    /// <summary>JSON of <see cref="GlobalLaunchDefaults"/> (seed for the AppSettings row GlobalLaunchDefaults).</summary>
    public static string GlobalLaunchDefaultsJson() => GlobalLaunchDefaults.ToJson();

    /// <summary>Field-by-field copy (new instance, so callers never mutate the shared defaults instance).</summary>
    public ProfileParameters Clone() => new()
    {
        CtxSize = CtxSize,
        SplitMode = SplitMode,
        Ngl = Ngl,
        TensorSplit = TensorSplit,
        Threads = Threads,
        HostBind = HostBind,
        NoHost = NoHost,
        Parallel = Parallel,
        Keep = Keep,
        CtxCheckpoints = CtxCheckpoints,
        BatchSize = BatchSize,
        UbatchSize = UbatchSize,
        FlashAttn = FlashAttn,
        CacheTypeK = CacheTypeK,
        CacheTypeV = CacheTypeV,
        RopeScaling = RopeScaling,
        RopeScale = RopeScale,
        RopeFreqBase = RopeFreqBase,
        RopeFreqScale = RopeFreqScale,
        SpecType = SpecType,
        SpecDraftNMax = SpecDraftNMax,
        SpecDraftPath = SpecDraftPath,
        CachePrompt = CachePrompt,
        MmprojPath = MmprojPath,
        ImageMinTokens = ImageMinTokens,
        ThinkingLevel = ThinkingLevel,
        Temperature = Temperature,
        TopP = TopP,
        TopK = TopK,
        MinP = MinP,
        PresencePenalty = PresencePenalty,
        RepeatPenalty = RepeatPenalty,
        Jinja = Jinja,
        EnableMetrics = EnableMetrics
    };

    /// <summary>
    /// Clone with the speculative/MTP launch fields cleared (no --spec-type/--spec-draft-n-max flags).
    /// SpecDraftPath is an explicit user override and is left untouched.
    /// </summary>
    public ProfileParameters WithoutMtpSpec()
    {
        var clone = Clone();
        clone.SpecType = null;
        clone.SpecDraftNMax = null;
        return clone;
    }

    // Note: pass the concrete type explicitly. `this`/the generic variant resolves a
    // partial class to the declared type, so the source-generated properties
    // (the [ObservableProperty] fields) would NOT (de)serialize → empty profiles.
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
            // Corrupt/old blob → fall back to an empty profile (must not crash).
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
    /// True when no parameter has been explicitly filled in (all fields null; Jinja is at its
    /// class default true). Used to recognize empty-seeded Default profiles.
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
        && ThinkingLevel is null
        && Temperature is null && TopP is null && TopK is null && MinP is null
        && PresencePenalty is null && RepeatPenalty is null
        && Jinja is not false
        && EnableMetrics is not false;

    /// <summary>
    /// Effective mmproj path: profile override (null = auto-linked mmproj of the model;
    /// explicitly empty = mmproj off).
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
