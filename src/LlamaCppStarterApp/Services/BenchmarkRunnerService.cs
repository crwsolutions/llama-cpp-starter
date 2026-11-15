using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;

namespace LlamaCppStarterApp.Services;

public sealed class BenchmarkRunnerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);

    private readonly IBenchmarkRepository _repository;
    private readonly LlamaServerProcessService _processService;
    private readonly HttpClient _httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly SemaphoreSlim _datasetLock = new(1, 1);
    private WorkloadDataset? _dataset;
    private CancellationTokenSource? _sessionLostCts;

    public event EventHandler<BenchmarkProgressEventArgs>? ProgressChanged;

    public BenchmarkRunnerService(
        IBenchmarkRepository repository,
        LlamaServerProcessService processService)
    {
        _repository = repository;
        _processService = processService;
        _processService.StateChanged += OnServerStateChanged;
    }

    public async Task<WorkloadDataset> LoadDatasetAsync()
    {
        if (_dataset is not null)
        {
            return _dataset;
        }

        await _datasetLock.WaitAsync();
        try
        {
            if (_dataset is not null)
            {
                return _dataset;
            }

            await using var stream = await FileSystem.OpenAppPackageFileAsync("benchmark-workloads.json");
            var dataset = await JsonSerializer.DeserializeAsync<WorkloadDataset>(stream, JsonOptions)
                ?? throw new InvalidDataException("The workload dataset is empty or invalid.");
            ValidateDataset(dataset);
            _dataset = dataset;
            return dataset;
        }
        finally
        {
            _datasetLock.Release();
        }
    }

    public async Task RecoverInterruptedRunsAsync()
    {
        if (!await _runLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            await _repository.FailInterruptedRunsAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        finally
        {
            _runLock.Release();
        }
    }

    public async Task<BenchmarkRun> RunAsync(
        BenchmarkSettings settings,
        CancellationToken cancellationToken)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("A benchmark is already running.");
        }

        try
        {
            var dataset = await LoadDatasetAsync();
            ValidateSettings(settings, dataset);
            var session = _processService.Session;
            if (_processService.State != LlamaServerState.Running || session is null)
            {
                throw new InvalidOperationException("Load a model and wait until the server is running before starting a benchmark.");
            }
            if (session.Port is < 1 or > 65535)
            {
                throw new InvalidOperationException("The loaded server has an invalid port.");
            }

            using var sessionLostCts = new CancellationTokenSource();
            _sessionLostCts = sessionLostCts;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionLostCts.Token);

            await VerifyEndpointAsync(session, linkedCts.Token);
            EnsureSameSession(session);
            var profileName = session.ProfileName;
            var apiModelName = GetApiModelName(session, profileName);
            var run = new BenchmarkRun
            {
                ConfigurationName = "Fixed workload",
                ConfigurationSnapshotJson = JsonSerializer.Serialize(settings, JsonOptions),
                StartedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Status = "Running",
                ModelName = session.Model.Name,
                ModelPath = session.Model.Path,
                ModelQuant = session.Model.Quant,
                ProfileName = profileName,
                ProfileId = session.LoadedProfileId,
                RuntimeName = session.Runtime.Name,
                RuntimePath = session.Runtime.ExecutablePath,
                BuildIdentity = string.IsNullOrWhiteSpace(session.Runtime.Backend)
                    ? session.Runtime.Name
                    : $"{session.Runtime.Name} ({session.Runtime.Backend})",
                LlamaCppVersion = await ReadLlamaCppVersionAsync(session.Runtime.ExecutablePath, linkedCts.Token),
                Repetitions = settings.Repetitions,
                Warmups = settings.Warmups,
                Comment = settings.Comment,
                ServerPid = session.ProcessId,
                LaunchParametersJson = session.Parameters.ToJson(),
                DatasetVersion = dataset.Version
            };
            run.Id = await _repository.CreateRunAsync(run);

            var selectedScenarios = settings.ScenarioIds
                .Select(id => dataset.Scenarios.Single(scenario => string.Equals(scenario.Id, id, StringComparison.Ordinal)))
                .ToList();
            var total = selectedScenarios.Count * (settings.Warmups + settings.Repetitions);
            var completed = 0;
            string? runError = null;
            var runStatus = "Completed";

            try
            {
                foreach (var scenario in selectedScenarios)
                {
                    for (var index = 0; index < settings.Warmups + settings.Repetitions; index++)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();
                        EnsureSameSession(session);
                        var isWarmup = index < settings.Warmups;
                        var repetition = isWarmup ? index + 1 : index - settings.Warmups + 1;
                        PublishProgress(completed, total, scenario, repetition, isWarmup, "Running");

                        var measurement = await RunMeasurementAsync(run.Id, scenario, settings, session, apiModelName, repetition, isWarmup, linkedCts.Token);
                        completed++;
                        PublishProgress(completed, total, scenario, repetition, isWarmup, "Running", measurement);
                        if (measurement.ErrorMessage is not null)
                        {
                            runError = measurement.ErrorMessage;
                            runStatus = "Failed";
                            break;
                        }
                    }

                    if (runError is not null)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || sessionLostCts.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    runStatus = "Cancelled";
                    runError = "Benchmark cancelled by the user.";
                }
                else
                {
                    runStatus = "Failed";
                    runError = "The loaded server stopped or changed during the benchmark.";
                }
            }
            catch (Exception ex)
            {
                runStatus = "Failed";
                runError = ex.Message;
            }
            finally
            {
                run.EndedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                run.Status = runStatus;
                run.ErrorMessage = runError;
                await _repository.CompleteRunAsync(run.Id, runStatus, run.EndedAtUtc.Value, runError);
                PublishProgress(completed, total, selectedScenarios[Math.Min(completed / Math.Max(1, settings.Warmups + settings.Repetitions), selectedScenarios.Count - 1)], 0, false, runStatus);
            }

            return run;
        }
        finally
        {
            _sessionLostCts = null;
            _runLock.Release();
        }
    }

    private async Task VerifyEndpointAsync(LoadedSession session, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var response = await _httpClient.GetAsync(CreateEndpointUri(session, "health"), timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"The loaded server health endpoint returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The loaded server health endpoint timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("The loaded server endpoint is not reachable.", ex);
        }

        EnsureSameSession(session);
    }

    private static Uri CreateEndpointUri(LoadedSession session, string path)
    {
        var host = session.Parameters.HostBind?.Trim();
        if (string.IsNullOrWhiteSpace(host) || host is "0.0.0.0" or "::" or "*")
        {
            host = "127.0.0.1";
        }

        return new UriBuilder(Uri.UriSchemeHttp, host, session.Port, path).Uri;
    }

    private static string GetApiModelName(LoadedSession session, string profileName)
    {
        if (!string.IsNullOrWhiteSpace(session.Parameters.ModelAlias))
        {
            return session.Parameters.ModelAlias;
        }

        return string.IsNullOrWhiteSpace(session.Model.Name)
            ? $"[{profileName}]"
            : $"{session.Model.Name} [{profileName}]";
    }

    private async Task<BenchmarkMeasurement> RunMeasurementAsync(
        long runId,
        WorkloadScenario scenario,
        BenchmarkSettings settings,
        LoadedSession session,
        string apiModelName,
        int repetition,
        bool isWarmup,
        CancellationToken cancellationToken)
    {
        var measurement = new BenchmarkMeasurement
        {
            RunId = runId,
            IsWarmup = isWarmup,
            ScenarioId = scenario.Id,
            ScenarioVersion = scenario.Version,
            Category = scenario.Category,
            Repetition = repetition,
            CacheState = "unknown"
        };
        var stopwatch = Stopwatch.StartNew();
        Exception? requestError = null;
        var promptTokenTotal = 0;
        var generatedTokenTotal = 0;
        var cachedTokenTotal = 0;
        var promptMillisecondsTotal = 0d;
        var generationMillisecondsTotal = 0d;
        var hasPromptTokenCount = true;
        var hasGeneratedTokenCount = true;
        var hasCacheCount = true;
        var hasPromptTiming = true;
        var hasGenerationTiming = true;
        var promptCountObserved = false;
        var generatedCountObserved = false;
        var cacheCountObserved = false;

        try
        {
            var messages = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["role"] = "user",
                    ["content"] = scenario.CodeContext is null
                        ? scenario.Prompt
                        : $"{scenario.Prompt}\n\nContext:\n{scenario.CodeContext}"
                }
            };
            var toolRounds = 0;
            var requests = 0;

            while (requests < scenario.MaxRequests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSameSession(session);
                requests++;
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestCts.CancelAfter(RequestTimeout);

                var request = new Dictionary<string, object?>
                {
                    ["model"] = apiModelName,
                    ["messages"] = messages,
                    ["stream"] = false,
                    ["cache_prompt"] = false
                };
                if (scenario.Tools.Count > 0)
                {
                    request["tools"] = scenario.Tools.Select(tool => new
                    {
                        type = "function",
                        function = new { name = tool.Name, description = tool.Description, parameters = tool.Parameters }
                    }).ToArray();
                    request["tool_choice"] = "auto";
                }

                using var response = await _httpClient.PostAsJsonAsync(
                    CreateEndpointUri(session, "v1/chat/completions"), request, JsonOptions, requestCts.Token);
                EnsureSameSession(session);
                if (!response.IsSuccessStatusCode)
                {
                    var details = await response.Content.ReadAsStringAsync(requestCts.Token);
                    throw new HttpRequestException(
                        $"Completion request returned HTTP {(int)response.StatusCode} ({response.StatusCode}): {Truncate(details, 1000)}",
                        null,
                        response.StatusCode);
                }

                await using var responseStream = await response.Content.ReadAsStreamAsync(requestCts.Token);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: requestCts.Token);
                var root = document.RootElement;
                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                {
                    throw new InvalidDataException("Completion response did not contain a choice.");
                }

                var choice = choices[0];
                if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Completion response did not contain a message.");
                }

                var cachedTokensBeforeRequest = cachedTokenTotal;
                AccumulateTimings(root, ref promptTokenTotal, ref generatedTokenTotal, ref cachedTokenTotal,
                    ref promptMillisecondsTotal, ref generationMillisecondsTotal,
                    ref hasPromptTokenCount, ref hasGeneratedTokenCount, ref hasCacheCount,
                    ref hasPromptTiming, ref hasGenerationTiming,
                    ref promptCountObserved, ref generatedCountObserved, ref cacheCountObserved);
                if (cachedTokenTotal > cachedTokensBeforeRequest)
                {
                    throw new InvalidDataException("The server reused cached prompt tokens despite cache_prompt being disabled. Benchmark stopped to avoid reporting cached performance.");
                }

                if (!message.TryGetProperty("tool_calls", out var toolCalls))
                {
                    break;
                }
                if (toolCalls.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("Completion response contained malformed tool_calls data.");
                }
                if (toolCalls.GetArrayLength() == 0)
                {
                    break;
                }

                var callCount = toolCalls.GetArrayLength();
                if (toolCalls.EnumerateArray().Any(call => call.ValueKind != JsonValueKind.Object
                    || !call.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(id.GetString())))
                {
                    measurement.ToolCalls += callCount;
                    measurement.FailedToolCalls += callCount;
                    measurement.ErrorMessage = "Model returned malformed tool-call records; scenario stopped.";
                    break;
                }
                if (requests >= scenario.MaxRequests)
                {
                    measurement.ToolCalls += callCount;
                    measurement.FailedToolCalls += callCount;
                    measurement.ErrorMessage = "Tool-call request limit reached.";
                    break;
                }

                var toolRoundLimitReached = toolRounds >= scenario.MaxToolRounds;
                if (!toolRoundLimitReached)
                {
                    toolRounds++;
                }
                messages.Add(JsonSerializer.Deserialize<Dictionary<string, object?>>(message.GetRawText(), JsonOptions)
                    ?? throw new InvalidDataException("Completion tool-call message could not be read."));
                foreach (var call in toolCalls.EnumerateArray())
                {
                    measurement.ToolCalls++;
                    var callId = call.GetProperty("id").GetString()!;
                    var function = call.TryGetProperty("function", out var functionElement) ? functionElement : default;
                    var toolName = function.ValueKind == JsonValueKind.Object && function.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString()
                        : null;
                    var arguments = function.ValueKind == JsonValueKind.Object && function.TryGetProperty("arguments", out var argumentElement)
                        ? argumentElement.ValueKind == JsonValueKind.String ? argumentElement.GetString() ?? "" : argumentElement.GetRawText()
                        : "";
                    bool failed;
                    string result;
                    if (toolRoundLimitReached)
                    {
                        failed = true;
                        result = "Tool error: maximum tool rounds reached. No action was performed.";
                    }
                    else
                    {
                        result = TrySimulateTool(scenario.Tools, toolName, arguments, out failed);
                    }
                    if (failed)
                    {
                        measurement.FailedToolCalls++;
                    }
                    messages.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = callId,
                        ["content"] = result
                    });
                }
            }

            }
            catch (Exception ex)
        {
            requestError = ex;
            measurement.ErrorMessage ??= ex is OperationCanceledException && !cancellationToken.IsCancellationRequested
                ? "Completion request timed out."
                : ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            measurement.PromptTokens = promptCountObserved ? promptTokenTotal : null;
            measurement.GeneratedTokens = generatedCountObserved ? generatedTokenTotal : null;
            measurement.CachedPromptTokens = cacheCountObserved ? cachedTokenTotal : null;
            measurement.CacheState = hasCacheCount ? cachedTokenTotal > 0 ? "cached" : "uncached" : "unknown";
            measurement.PromptMilliseconds = hasPromptTiming && promptCountObserved ? promptMillisecondsTotal : null;
            measurement.GenerationMilliseconds = hasGenerationTiming && generatedCountObserved ? generationMillisecondsTotal : null;
            measurement.PromptTokensPerSecond = requestError is null && measurement.ErrorMessage is null
                && hasPromptTokenCount && hasPromptTiming && promptMillisecondsTotal > 0
                    ? promptTokenTotal * 1000d / promptMillisecondsTotal
                    : null;
            measurement.GenerationTokensPerSecond = requestError is null && measurement.ErrorMessage is null
                && hasGeneratedTokenCount && hasGenerationTiming && generationMillisecondsTotal > 0
                    ? generatedTokenTotal * 1000d / generationMillisecondsTotal
                    : null;
            measurement.TotalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            measurement.ClientWallMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            await _repository.SaveMeasurementAsync(measurement);
        }

        if (requestError is not null)
        {
            throw requestError;
        }

        return measurement;
    }

    private static void AccumulateTimings(
        JsonElement root,
        ref int promptTokens,
        ref int generatedTokens,
        ref int cachedTokens,
        ref double promptMilliseconds,
        ref double generationMilliseconds,
        ref bool hasPromptTokenCount,
        ref bool hasGeneratedTokenCount,
        ref bool hasCacheCount,
        ref bool hasPromptTiming,
        ref bool hasGenerationTiming,
        ref bool promptCountObserved,
        ref bool generatedCountObserved,
        ref bool cacheCountObserved)
    {
        var timing = root.TryGetProperty("timings", out var timingElement) && timingElement.ValueKind == JsonValueKind.Object
            ? timingElement
            : default;
        var hasTiming = timing.ValueKind == JsonValueKind.Object;
        var promptN = 0;
        var predictedN = 0;
        var promptMs = 0d;
        var predictedMs = 0d;
        var hasPromptN = hasTiming && TryGetInt(timing, "prompt_n", out promptN);
        var hasPredictedN = hasTiming && TryGetInt(timing, "predicted_n", out predictedN);
        var hasPromptMs = hasTiming && TryGetDouble(timing, "prompt_ms", out promptMs);
        var hasPredictedMs = hasTiming && TryGetDouble(timing, "predicted_ms", out predictedMs);

        if (hasPromptN && promptN >= 0)
        {
            promptTokens += promptN;
            promptCountObserved = true;
        }
        else if (root.TryGetProperty("usage", out var usage) && TryGetInt(usage, "prompt_tokens", out var usagePromptTokens) && usagePromptTokens >= 0)
        {
            promptTokens += usagePromptTokens;
            promptCountObserved = true;
            hasPromptTiming = false;
        }
        else
        {
            hasPromptTokenCount = false;
            hasPromptTiming = false;
        }

        if (hasPredictedN && predictedN >= 0)
        {
            generatedTokens += predictedN;
            generatedCountObserved = true;
        }
        else if (root.TryGetProperty("usage", out var generatedUsage) && TryGetInt(generatedUsage, "completion_tokens", out var usageGeneratedTokens) && usageGeneratedTokens >= 0)
        {
            generatedTokens += usageGeneratedTokens;
            generatedCountObserved = true;
            hasGenerationTiming = false;
        }
        else
        {
            hasGeneratedTokenCount = false;
            hasGenerationTiming = false;
        }

        if (hasPromptN && hasPromptMs && promptMs >= 0)
        {
            promptMilliseconds += promptMs;
        }
        else
        {
            hasPromptTiming = false;
        }

        if (hasPredictedN && hasPredictedMs && predictedMs >= 0)
        {
            generationMilliseconds += predictedMs;
        }
        else
        {
            hasGenerationTiming = false;
        }

        if (hasTiming && TryGetInt(timing, "cache_n", out var cacheN) && cacheN >= 0)
        {
            cachedTokens += cacheN;
            cacheCountObserved = true;
        }
        else if (hasTiming && TryGetInt(timing, "cached_prompt_tokens", out cacheN) && cacheN >= 0)
        {
            cachedTokens += cacheN;
            cacheCountObserved = true;
        }
        else
        {
            hasCacheCount = false;
        }
    }

    private static string TrySimulateTool(IReadOnlyList<WorkloadTool> tools, string? name, string arguments, out bool failed)
    {
        var tool = tools.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (tool is null)
        {
            failed = true;
            return "Tool error: unknown tool name. No action was performed.";
        }

        try
        {
            using var document = JsonDocument.Parse(arguments);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failed = true;
                return "Tool error: arguments must be a JSON object. No action was performed.";
            }

            var schemaProperties = tool.Parameters.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object
                    ? properties
                    : default;
            var requiredNames = new HashSet<string>(tool.Required, StringComparer.Ordinal);
            if (tool.Parameters.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } requiredName)
                    {
                        requiredNames.Add(requiredName);
                    }
                }
            }
            foreach (var required in requiredNames)
            {
                if (!root.TryGetProperty(required, out _))
                {
                    failed = true;
                    return $"Tool error: required argument '{required}' is missing. No action was performed.";
                }
            }

            var denyExtraProperties = tool.Parameters.TryGetProperty("additionalProperties", out var additionalProperties)
                && additionalProperties.ValueKind == JsonValueKind.False;
            foreach (var property in root.EnumerateObject())
            {
                var propertySchema = default(JsonElement);
                var hasSchema = schemaProperties.ValueKind == JsonValueKind.Object
                    && schemaProperties.TryGetProperty(property.Name, out propertySchema);
                if (denyExtraProperties && !hasSchema)
                {
                    failed = true;
                    return $"Tool error: argument '{property.Name}' is not allowed. No action was performed.";
                }

                var expectedType = tool.PropertyTypes.GetValueOrDefault(property.Name);
                if (expectedType is null && hasSchema && propertySchema.ValueKind == JsonValueKind.Object
                    && propertySchema.TryGetProperty("type", out var schemaType) && schemaType.ValueKind == JsonValueKind.String)
                {
                    expectedType = schemaType.GetString();
                }
                if (expectedType is null)
                {
                    continue;
                }

                var valid = expectedType switch
                {
                    "string" => property.Value.ValueKind == JsonValueKind.String,
                    "number" => property.Value.ValueKind == JsonValueKind.Number,
                    "integer" => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out _),
                    "boolean" => property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    "object" => property.Value.ValueKind == JsonValueKind.Object,
                    "array" => property.Value.ValueKind == JsonValueKind.Array,
                    _ => false
                };
                if (!valid)
                {
                    failed = true;
                    return $"Tool error: argument '{property.Name}' must be a {expectedType}. No action was performed.";
                }
            }

            failed = false;
            return tool.SimulatedResponse;
        }
        catch (JsonException)
        {
            failed = true;
            return "Tool error: arguments are not valid JSON. No action was performed.";
        }
    }

    private void EnsureSameSession(LoadedSession expected)
    {
        if (!IsSameSession(expected))
        {
            throw new InvalidOperationException("The loaded server stopped or changed during the benchmark.");
        }
    }

    private bool IsSameSession(LoadedSession expected)
    {
        var current = _processService.Session;
        return _processService.State == LlamaServerState.Running
            && current is not null
            && current.ProcessId == expected.ProcessId
            && current.Port == expected.Port
            && current.LoadedProfileId == expected.LoadedProfileId
            && string.Equals(current.ProfileName, expected.ProfileName, StringComparison.Ordinal)
            && string.Equals(current.Runtime.ExecutablePath, expected.Runtime.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.Model.Path, expected.Model.Path, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.Parameters.ToJson(), expected.Parameters.ToJson(), StringComparison.Ordinal);
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs e)
    {
        if (e.State != LlamaServerState.Running && _sessionLostCts is { } sessionLostCts)
        {
            try
            {
                sessionLostCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run completed while the process-state notification was in flight.
            }
        }
    }

    private void PublishProgress(int completed, int total, WorkloadScenario scenario, int repetition, bool isWarmup, string status, BenchmarkMeasurement? measurement = null) =>
        ProgressChanged?.Invoke(this, new BenchmarkProgressEventArgs(completed, total, scenario.Title, scenario.Category, repetition, isWarmup, status, measurement));

    private static async Task<string> ReadLlamaCppVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("--version");

        try
        {
            if (!process.Start())
            {
                return "Unavailable (could not start version check)";
            }
        }
        catch (Win32Exception ex)
        {
            return $"Unavailable ({ex.Message})";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Unavailable ({ex.Message})";
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linkedToken.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }

            await process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            return "Unavailable (version check timed out)";
        }

        var output = await standardOutput + Environment.NewLine + await standardError;
        return output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("version:", StringComparison.OrdinalIgnoreCase))
            ?? (process.ExitCode == 0 ? "Unavailable (version not reported)" : $"Unavailable (exit code {process.ExitCode})");
    }

    private static void ValidateDataset(WorkloadDataset dataset)
    {
        if (string.IsNullOrWhiteSpace(dataset.Version) || dataset.Scenarios is null || dataset.Scenarios.Count == 0)
        {
            throw new InvalidDataException("The workload dataset must have a version and at least one scenario.");
        }
        if (dataset.Scenarios.Any(scenario => scenario is null
            || string.IsNullOrWhiteSpace(scenario.Id)
            || string.IsNullOrWhiteSpace(scenario.Version)
            || string.IsNullOrWhiteSpace(scenario.Category)
            || string.IsNullOrWhiteSpace(scenario.Title)
            || string.IsNullOrWhiteSpace(scenario.Prompt)
            || scenario.MaxToolRounds is < 0 or > 5
            || scenario.MaxRequests is < 1 or > 10)
            || dataset.Scenarios.Select(scenario => scenario.Id).Distinct(StringComparer.Ordinal).Count() != dataset.Scenarios.Count)
        {
            throw new InvalidDataException("The workload dataset contains a malformed or duplicate scenario.");
        }
        foreach (var scenario in dataset.Scenarios)
        {
            if (scenario.Tools is null
                || scenario.Tools.Select(tool => tool?.Name).Distinct(StringComparer.Ordinal).Count() != scenario.Tools.Count
                || scenario.Tools.Any(tool => !IsValidToolDefinition(tool)))
            {
                throw new InvalidDataException($"Scenario '{scenario.Id}' contains an invalid tool definition.");
            }
        }
    }

    private static bool IsValidToolDefinition(WorkloadTool? tool)
    {
        if (tool is null || string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.Description)
            || string.IsNullOrWhiteSpace(tool.SimulatedResponse) || tool.Parameters.ValueKind != JsonValueKind.Object
            || tool.Required is null || tool.PropertyTypes is null
            || !tool.Parameters.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String || type.GetString() != "object"
            || !tool.Parameters.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object
            || tool.Required.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (property.Value.TryGetProperty("type", out var propertyType)
                && (propertyType.ValueKind != JsonValueKind.String || !IsSupportedJsonType(propertyType.GetString())))
            {
                return false;
            }
        }

        if (tool.Parameters.TryGetProperty("required", out var schemaRequired))
        {
            if (schemaRequired.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            foreach (var required in schemaRequired.EnumerateArray())
            {
                if (required.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(required.GetString())
                    || !properties.TryGetProperty(required.GetString()!, out _))
                {
                    return false;
                }
            }
        }
        if (tool.Parameters.TryGetProperty("additionalProperties", out var additionalProperties)
            && additionalProperties.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        foreach (var propertyType in tool.PropertyTypes)
        {
            if (!IsSupportedJsonType(propertyType.Value)
                || !properties.TryGetProperty(propertyType.Key, out var propertySchema)
                || (propertySchema.TryGetProperty("type", out var schemaType)
                    && schemaType.GetString() != propertyType.Value))
            {
                return false;
            }
        }

        foreach (var required in tool.Required)
        {
            if (!properties.TryGetProperty(required, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSupportedJsonType(string? type) =>
        type is "string" or "number" or "integer" or "boolean" or "object" or "array";

    private static void ValidateSettings(BenchmarkSettings settings, WorkloadDataset dataset)
    {
        if (settings.ScenarioIds is null || !string.Equals(settings.DatasetVersion, dataset.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Configuration dataset version '{settings.DatasetVersion}' does not match installed dataset '{dataset.Version}' or has invalid scenario settings.");
        }
        if (settings.ScenarioIds.Count == 0 || settings.ScenarioIds.Distinct(StringComparer.Ordinal).Count() != settings.ScenarioIds.Count
            || settings.ScenarioIds.Any(id => dataset.Scenarios.All(scenario => !string.Equals(scenario.Id, id, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException("Select at least one valid workload scenario.");
        }
        if (settings.Repetitions is < 1 or > 30 || settings.Warmups is < 0 or > 10)
        {
            throw new InvalidOperationException("Benchmark settings are outside supported limits.");
        }
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out value);
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static string Truncate(string value, int limit) => value.Length <= limit ? value : value[..limit];
}
