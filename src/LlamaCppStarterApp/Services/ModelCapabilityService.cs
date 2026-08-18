using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// Capability-opsomming per model (11 velden, exacte naam/volgorde per spec).
/// Port uit het referentieproject (ModelCapabilityService), aangepast op het
/// starter-Model (Path/Name/MetadataJson) en op DB-caché (zie <see cref="TryReadCached"/>).
/// </summary>
public sealed record ModelCapabilitySummary(
    bool HasMetadata,
    string Architecture,
    string Quantization,
    int ContextLength,
    bool HasChatTemplate,
    bool HasVisionProjector,
    bool LikelyVision,
    bool IsMoe,
    bool LikelyReasoning,
    bool IsEmbedding,
    bool IsFim);

/// <summary>
/// Pure static capability-inspectie: leest het GGUF-bestand zelf (per selectie,
/// niet bij scan), voegt HF-hints uit de metadata-blob en companion-detectie toe,
/// en bouwt de read-only chip-samenvatting ("  |  "-gescheiden) voor de UI.
/// </summary>
public static class ModelCapabilityService
{
    private sealed record HuggingFaceModelHints(HashSet<string> Capabilities, bool HasVisionProjector);

    public static ModelCapabilitySummary Empty()
        => new(false, "unknown", "unknown", 0, false, false, false, false, false, false, false);

    /// <summary>
    /// Fingerprint van wat de inspectie beïnvloedt: model-pad + size + lastwrite,
    /// plus het auto-gevonden projector-pad + zijn size + lastwrite.
    /// </summary>
    public static string Fingerprint(Model model)
    {
        var length = 0L;
        var updated = DateTime.MinValue;
        var projectorPath = "";
        var projectorLength = 0L;
        var projectorUpdated = DateTime.MinValue;
        try
        {
            var info = new FileInfo(model.Path);
            if (info.Exists)
            {
                length = info.Length;
                updated = info.LastWriteTimeUtc;
            }

            projectorPath = ModelCompanionService.FindVisionProjector(model.Path) ?? "";
            if (!string.IsNullOrWhiteSpace(projectorPath))
            {
                var projectorInfo = new FileInfo(projectorPath);
                if (projectorInfo.Exists)
                {
                    projectorLength = projectorInfo.Length;
                    projectorUpdated = projectorInfo.LastWriteTimeUtc;
                }
            }
        }
        catch
        {
            // Fallback op null-waarden: fingerprint mist alleen als het bestand onleesbaar is.
        }

        return $"{model.Path}|{length}|{updated:O}|{projectorPath}|{projectorLength}|{projectorUpdated:O}";
    }

    /// <summary>
    /// Lees de capability-JSON-blob van het model; geldig alleen zodra de fingerprint
    /// nog klopt (bestand/projector niet gewijzigd). false = miss of stale (herinspecteer).
    /// </summary>
    public static bool TryReadCached(Model model, out ModelCapabilitySummary summary, out string summaryText)
    {
        if (string.IsNullOrWhiteSpace(model.CapabilitiesJson))
        {
            summary = Empty();
            summaryText = SummaryText(summary);
            return false;
        }

        try
        {
            var node = JsonNode.Parse(model.CapabilitiesJson);
            var fingerprint = node?["fingerprint"]?.GetValue<string>() ?? "";
            if (!string.Equals(fingerprint, Fingerprint(model), StringComparison.Ordinal))
            {
                summary = Empty();
                summaryText = SummaryText(summary);
                return false;
            }

            summary = node?["summary"]?.Deserialize<ModelCapabilitySummary>() ?? Empty();
            summaryText = node?["summaryText"]?.GetValue<string>() ?? SummaryText(summary);
            return true;
        }
        catch (JsonException)
        {
            // Corrupte blob → behandelen als miss (crashen mag niet).
            summary = Empty();
            summaryText = SummaryText(summary);
            return false;
        }
    }

    /// <summary>Cache-blob: fingerprint + samenvatting + summary-tekst in één JSON-document.</summary>
    public static string BuildCacheJson(Model model, ModelCapabilitySummary summary, string summaryText)
        => JsonSerializer.Serialize(new { fingerprint = Fingerprint(model), summaryText, summary });

    /// <summary>GGUF-bestand + HF-hints uit de metadata-blob + companion-scan → summary.</summary>
    public static ModelCapabilitySummary Inspect(Model model)
    {
        var metadata = GgufMetadataReader.TryRead(model.Path);
        var hfHints = HuggingFaceHintsFromMetadata(model.MetadataJson);
        var architecture = StringMetadata(metadata, "general.architecture");
        var quant = ModelCompanionService.InferQuant(model.Path);
        if (string.IsNullOrWhiteSpace(quant))
            quant = StringMetadata(metadata, "general.file_type");
        var context = ContextLength(metadata, architecture);
        var chatTemplate = StringMetadata(metadata, "tokenizer.chat_template");
        var metadataText = string.Join(" ", metadata.Select(pair => $"{pair.Key} {pair.Value}"));
        var nameText = $"{model.Name} {Path.GetFileName(model.Path)} {model.MetadataJson ?? string.Empty}";
        var combined = $"{metadataText} {nameText}";
        var hasVisionProjector = hfHints.HasVisionProjector || ModelCompanionService.FindVisionProjector(model.Path) is not null;
        var likelyVision = hasVisionProjector || hfHints.Capabilities.Contains("vision") || LooksVisionCapable(combined);
        var isMoe = metadata.Keys.Any(key => key.EndsWith(".expert_count", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith(".expert_used_count", StringComparison.OrdinalIgnoreCase))
            || combined.Contains("ffn_gate_exp", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("moe", StringComparison.OrdinalIgnoreCase)
            || hfHints.Capabilities.Contains("moe");
        var likelyReasoning = !string.IsNullOrWhiteSpace(chatTemplate)
                && (chatTemplate.Contains("think", StringComparison.OrdinalIgnoreCase)
                    || chatTemplate.Contains("reasoning", StringComparison.OrdinalIgnoreCase))
            || nameText.Contains("qwen3", StringComparison.OrdinalIgnoreCase)
            || nameText.Contains("deepseek-r1", StringComparison.OrdinalIgnoreCase)
            || nameText.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
            || nameText.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase)
            || hfHints.Capabilities.Contains("reasoning");
        var isEmbedding = nameText.Contains("embed", StringComparison.OrdinalIgnoreCase)
            || nameText.Contains("rerank", StringComparison.OrdinalIgnoreCase)
            || StringMetadata(metadata, "pooling_type").Length > 0
            || hfHints.Capabilities.Contains("embedding");
        var isFim = combined.Contains("fim", StringComparison.OrdinalIgnoreCase)
            || metadata.Keys.Any(key => key.Contains("fim", StringComparison.OrdinalIgnoreCase))
            || hfHints.Capabilities.Contains("fim");
        return new ModelCapabilitySummary(
            metadata.Count > 0,
            string.IsNullOrWhiteSpace(architecture) ? "unknown" : architecture,
            string.IsNullOrWhiteSpace(quant) ? "unknown" : quant,
            context,
            !string.IsNullOrWhiteSpace(chatTemplate),
            hasVisionProjector,
            likelyVision,
            isMoe,
            likelyReasoning,
            isEmbedding,
            isFim);
    }

    /// <summary>Read-only chips-tekst voor de UI, gescheiden door "  |  " (per spec).</summary>
    public static string SummaryText(ModelCapabilitySummary capabilities)
    {
        var chips = new List<string>
        {
            $"Arch: {capabilities.Architecture}",
            $"Quant: {capabilities.Quantization}"
        };
        chips.Add(capabilities.ContextLength > 0 ? $"Context: {capabilities.ContextLength:N0}" : "Context: unknown");
        chips.Add(capabilities.HasChatTemplate ? "Chat template: found" : "Chat template: unknown");
        if (capabilities.HasVisionProjector) chips.Add("Vision: mmproj found");
        else if (capabilities.LikelyVision) chips.Add("Vision: likely, projector not found");
        if (capabilities.IsMoe) chips.Add("MoE");
        if (capabilities.LikelyReasoning) chips.Add("Reasoning likely");
        if (capabilities.IsEmbedding) chips.Add("Embedding/reranker");
        if (capabilities.IsFim) chips.Add("FIM");
        if (!capabilities.HasMetadata) chips.Add("GGUF metadata: unavailable");
        return string.Join("  |  ", chips);
    }

    /// <summary>Contextlengte: eerst "{architecture}.context_length", anders eerste "*.context_length" > 0.</summary>
    public static int ContextLength(IReadOnlyDictionary<string, object?> metadata, string architecture)
    {
        if (!string.IsNullOrWhiteSpace(architecture)
            && IntMetadata(metadata, $"{architecture}.context_length") is { } context)
            return context;
        foreach (var pair in metadata)
        {
            if (pair.Key.EndsWith(".context_length", StringComparison.OrdinalIgnoreCase)
                && TryInt(pair.Value, out var value))
                return value;
        }
        return 0;
    }

    public static bool LooksVisionCapable(string text)
    {
        var markers = new[]
        {
            "vision", "visual", "image", "multimodal", "mmproj", "projector",
            "llava", "bakllava", "qwen2-vl", "qwen2.5-vl", "qwen3-vl",
            "internvl", "minicpm-v", "pixtral", "moondream", "mllama",
            "llama-3.2-vision", "gemma-3", "gemma3"
        };
        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryInt(object? value, out int number)
    {
        switch (value)
        {
            case byte b:
                number = b;
                return true;
            case sbyte sb:
                number = sb;
                return true;
            case ushort us:
                number = us;
                return true;
            case short s:
                number = s;
                return true;
            case uint ui when ui <= int.MaxValue:
                number = (int)ui;
                return true;
            case int i:
                number = i;
                return true;
            case ulong ul when ul <= int.MaxValue:
                number = (int)ul;
                return true;
            case long l when l <= int.MaxValue && l >= int.MinValue:
                number = (int)l;
                return true;
            case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                number = parsed;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    // --- Interne helpers ---

    private static string StringMetadata(IReadOnlyDictionary<string, object?> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";

    private static int? IntMetadata(IReadOnlyDictionary<string, object?> metadata, string key)
        => metadata.TryGetValue(key, out var value) && TryInt(value, out var number) ? number : null;

    private static bool BoolMetadata(JsonNode? node, string key)
        => node?[key] is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    /// <summary>
    /// Defensieve HF-hint-parser: CapabilityHints/pipelineTag/Tags/HasVisionProjector
    /// uit de metadata-blob + ruwe-tekstscan. Starter-blob heeft geen HF-velden;
    /// afwezig/corrupt → geen hints (crashen mag niet).
    /// </summary>
    private static HuggingFaceModelHints HuggingFaceHintsFromMetadata(string? metadataJson)
    {
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasVisionProjector = false;
        if (string.IsNullOrWhiteSpace(metadataJson)) return new HuggingFaceModelHints(capabilities, hasVisionProjector);

        try
        {
            var node = JsonNode.Parse(metadataJson);
            AddCapabilityHints(capabilities, node?["CapabilityHints"]?.ToString());
            AddCapabilityHints(capabilities, node?["capabilityHints"]?.ToString());
            AddCapabilityHints(capabilities, node?["pipelineTag"]?.ToString());
            AddCapabilityHints(capabilities, node?["PipelineTag"]?.ToString());
            var tags = node?["Tags"] as JsonArray ?? node?["tags"] as JsonArray;
            if (tags is not null)
            {
                foreach (var tag in tags)
                    AddCapabilityHints(capabilities, tag?.ToString());
            }

            hasVisionProjector = BoolMetadata(node, "HasVisionProjector")
                || BoolMetadata(node, "hasVisionProjector");
            var text = metadataJson;
            if (LooksVisionCapable(text)) capabilities.Add("vision");
            if (text.Contains("reasoning", StringComparison.OrdinalIgnoreCase) || text.Contains("deepseek-r1", StringComparison.OrdinalIgnoreCase)) capabilities.Add("reasoning");
            if (text.Contains("embedding", StringComparison.OrdinalIgnoreCase) || text.Contains("feature-extraction", StringComparison.OrdinalIgnoreCase)) capabilities.Add("embedding");
            if (text.Contains("fim", StringComparison.OrdinalIgnoreCase) || text.Contains("infill", StringComparison.OrdinalIgnoreCase)) capabilities.Add("fim");
            if (text.Contains("moe", StringComparison.OrdinalIgnoreCase)) capabilities.Add("moe");
        }
        catch (JsonException)
        {
            // Oude/andere metadata betekent simpelweg: geen HF-hints.
        }

        return new HuggingFaceModelHints(capabilities, hasVisionProjector);
    }

    private static void AddCapabilityHints(HashSet<string> capabilities, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var part in value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains("vision", StringComparison.OrdinalIgnoreCase)
                || part.Contains("image", StringComparison.OrdinalIgnoreCase)
                || part.Contains("multimodal", StringComparison.OrdinalIgnoreCase)
                || part.Contains("llava", StringComparison.OrdinalIgnoreCase)
                || part.Contains("vl", StringComparison.OrdinalIgnoreCase))
                capabilities.Add("vision");
            if (part.Contains("chat", StringComparison.OrdinalIgnoreCase)
                || part.Contains("conversational", StringComparison.OrdinalIgnoreCase)
                || part.Contains("instruct", StringComparison.OrdinalIgnoreCase)
                || part.Contains("text-generation", StringComparison.OrdinalIgnoreCase))
                capabilities.Add("chat");
            if (part.Contains("embed", StringComparison.OrdinalIgnoreCase)
                || part.Contains("feature-extraction", StringComparison.OrdinalIgnoreCase)
                || part.Contains("sentence-similarity", StringComparison.OrdinalIgnoreCase)
                || part.Contains("rerank", StringComparison.OrdinalIgnoreCase))
                capabilities.Add("embedding");
            if (part.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
                || part.Contains("qwen3", StringComparison.OrdinalIgnoreCase)
                || part.Contains("deepseek-r1", StringComparison.OrdinalIgnoreCase))
                capabilities.Add("reasoning");
            if (part.Contains("moe", StringComparison.OrdinalIgnoreCase) || part.Contains("expert", StringComparison.OrdinalIgnoreCase))
                capabilities.Add("moe");
            if (part.Contains("fim", StringComparison.OrdinalIgnoreCase)
                || part.Contains("infill", StringComparison.OrdinalIgnoreCase)
                || part.Contains("fill-in-the-middle", StringComparison.OrdinalIgnoreCase))
                capabilities.Add("fim");
        }
    }
}
