using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

/// <summary>
/// Pure static builder for the llama-server.exe launch arguments.
/// Order follows the reference command (see plan). Null/empty = flag omitted (llama.cpp default).
/// </summary>
public static class LlamaServerCommandBuilder
{
    /// <summary>
    /// <paramref name="draftModelPath"/> = resolved --spec-draft-model path
    /// (via ModelCompanionService.ResolveDraftModelPath; null = no flag, e.g. embedded MTP).
    /// </summary>
    public static string[] BuildArgs(Runtime? runtime, Model model, ProfileParameters p, int port, string? draftModelPath)
    {
        var args = new List<string>();

        // -m <model>
        args.Add("--model");
        args.Add(Quote(model.Path));

        // -mm <mmproj> (only if profile override or model-linked projector)
        var mmproj = p.GetEffectiveMmproj(model);
        if (!string.IsNullOrWhiteSpace(mmproj))
        {
            args.Add("--mmproj");
            args.Add(Quote(mmproj));
        }

        // --host {bind} (only when set; llama.cpp default = 127.0.0.1)
        if (!string.IsNullOrWhiteSpace(p.HostBind))
        {
            args.Add("--host");
            args.Add(p.HostBind);
        }

        // --port {port}
        args.Add("--port");
        args.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (p.CtxSize is not null)
        {
            args.Add("--ctx-size");
            args.Add(p.CtxSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // --split-mode (default layer)
        args.Add("--split-mode");
        args.Add(string.IsNullOrWhiteSpace(p.SplitMode) ? "layer" : p.SplitMode);

        if (!string.IsNullOrWhiteSpace(p.Ngl))
        {
            args.Add("--gpu-layers");
            args.Add(p.Ngl);
        }

        if (p.BatchSize is not null)
        {
            args.Add("--batch-size");
            args.Add(p.BatchSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.UbatchSize is not null)
        {
            args.Add("--ubatch-size");
            args.Add(p.UbatchSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.Threads is not null)
        {
            args.Add("--threads");
            args.Add(p.Threads.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.Temperature is not null)
        {
            args.Add("--temp");
            args.Add(FormatDouble(p.Temperature.Value, "0.0"));
        }

        if (p.TopP is not null)
        {
            args.Add("--top-p");
            args.Add(FormatDouble(p.TopP.Value, "0.##"));
        }

        if (p.TopK is not null)
        {
            args.Add("--top-k");
            args.Add(p.TopK.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.MinP is not null)
        {
            args.Add("--min-p");
            args.Add(FormatDouble(p.MinP.Value, "0.00"));
        }

        if (!string.IsNullOrWhiteSpace(p.FlashAttn))
        {
            args.Add("--flash-attn");
            args.Add(p.FlashAttn);
        }

        if (!string.IsNullOrWhiteSpace(p.TensorSplit))
        {
            args.Add("--tensor-split");
            args.Add(p.TensorSplit);
        }

        if (p.NoHost == true)
        {
            args.Add("--no-host");
        }

        if (!string.IsNullOrWhiteSpace(p.CacheTypeK))
        {
            args.Add("--cache-type-k");
            args.Add(p.CacheTypeK);
        }

        if (!string.IsNullOrWhiteSpace(p.CacheTypeV))
        {
            args.Add("--cache-type-v");
            args.Add(p.CacheTypeV);
        }

        // --rope-* (after the cache types)
        if (!string.IsNullOrWhiteSpace(p.RopeScaling))
        {
            args.Add("--rope-scaling");
            args.Add(p.RopeScaling);
        }

        if (p.RopeScale is not null)
        {
            args.Add("--rope-scale");
            args.Add(p.RopeScale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.RopeFreqBase is not null)
        {
            args.Add("--rope-freq-base");
            args.Add(p.RopeFreqBase.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.RopeFreqScale is not null)
        {
            args.Add("--rope-freq-scale");
            args.Add(p.RopeFreqScale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.Parallel is not null)
        {
            args.Add("--parallel");
            args.Add(p.Parallel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.PresencePenalty is not null)
        {
            args.Add("--presence-penalty");
            args.Add(FormatDouble(p.PresencePenalty.Value, "0.0"));
        }

        if (p.RepeatPenalty is not null)
        {
            args.Add("--repeat-penalty");
            args.Add(FormatDouble(p.RepeatPenalty.Value, "0.0"));
        }

        // --jinja (true = --jinja, false = --no-jinja, null = not passed)
        if (p.Jinja is not null)
        {
            args.Add(p.Jinja.Value ? "--jinja" : "--no-jinja");
        }

        if (p.Keep is not null)
        {
            args.Add("--keep");
            args.Add(p.Keep.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.CtxCheckpoints is not null)
        {
            args.Add("--ctx-checkpoints");
            args.Add(p.CtxCheckpoints.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // --cache-prompt (true = on, false = --no-cache-prompt, null = not passed)
        if (p.CachePrompt is not null)
        {
            args.Add(p.CachePrompt.Value ? "--cache-prompt" : "--no-cache-prompt");
        }

        if (!string.IsNullOrWhiteSpace(p.SpecType))
        {
            args.Add("--spec-type");
            args.Add(p.SpecType);
        }

        if (p.SpecDraftNMax is not null)
        {
            args.Add("--spec-draft-n-max");
            args.Add(p.SpecDraftNMax.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(p.SpecDraftDevice))
        {
            args.Add("--spec-draft-device");
            args.Add(p.SpecDraftDevice);
        }

        // --spec-draft-model (only if the resolved path is non-whitespace; null = e.g. embedded MTP)
        if (!string.IsNullOrWhiteSpace(draftModelPath))
        {
            args.Add("--spec-draft-model");
            args.Add(Quote(draftModelPath));
        }

        if (p.ImageMinTokens is not null)
        {
            args.Add("--image-min-tokens");
            args.Add(p.ImageMinTokens.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // --no-mmproj-offload (true = pass the flag; false/null = not passed, default = offload enabled)
        if (p.NoMmprojOffload == true)
        {
            args.Add("--no-mmproj-offload");
        }

        // --metrics (off by default in llama-server; EnableMetrics is not false = on, default true)
        if (p.EnableMetrics is not false)
        {
            args.Add("--metrics");
        }

        // --reasoning off (Thinking picker "off"; llama.cpp deprecated the
        // --chat-template-kwargs {"enable_thinking": false} equivalent).
        if (p.ThinkingLevel == "off")
        {
            args.Add("--reasoning");
            args.Add("off");
        }

        // --chat-template-kwargs (Thinking picker low/medium/xhigh; null = flag not passed).
        // The JSON value is passed verbatim as one ArgumentList element (no extra quoting:
        // the process argv must be exactly the JSON object string, e.g. {"reasoning_effort": "low"}).
        var chatTemplateKwargs = ChatTemplateKwargsFor(p.ThinkingLevel);
        if (chatTemplateKwargs is not null)
        {
            args.Add("--chat-template-kwargs");
            args.Add(chatTemplateKwargs);
        }

        return args.ToArray();
    }

    /// <summary>Read-only preview: volledige command line inclusief executable.</summary>
    public static string BuildCommandLine(string[] args) =>
        "llama-server.exe " + string.Join(' ', args);

    /// <summary>
    /// Double formatting with the precision of the reference command
    /// (e.g. --temp 1.0, --top-p 0.95, --min-p 0.00, --presence-penalty 0.0, --repeat-penalty 1.0).
    /// </summary>
    private static string FormatDouble(double value, string format) =>
        value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Quote a value when it contains spaces (e.g. "C:\path with space\file.gguf").</summary>
    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    /// <summary>
    /// --chat-template-kwargs JSON value for a Thinking picker level (null = flag not passed).
    /// "off" has its own --reasoning off flag (kwargs enable_thinking is deprecated);
    /// low/medium/xhigh → {"reasoning_effort": "&lt;level&gt;"}.
    /// </summary>
    private static string? ChatTemplateKwargsFor(string? thinkingLevel) => thinkingLevel switch
    {
        "low" or "medium" or "xhigh" => $"{{\"reasoning_effort\": \"{thinkingLevel}\"}}",
        _ => null
    };
}
