namespace LlamaCppStarterApp.Services;

public enum ModelRuntimeStatusKind
{
    Fallback,
    Loading,
    Loaded
}

public sealed record ModelRuntimeStatusDisplay(
    string ModelId,
    string MetricText,
    ModelRuntimeStatusKind Kind,
    string? StatusText = null);

/// <summary>
/// Houdt de Modelstatus-kaart bij: Loading/Loaded/Fallback + Loading Time
/// (teller vanaf _loadingStartedAt). Port 1:1 uit het referentieproject
/// (LocalLlmConsole.Services.ModelRuntimeStatusTracker; ElapsedFormatter lokaal).
/// Wordt in de OverviewViewModel geconstrueerd — geen DI nodig.
/// </summary>
public sealed class ModelRuntimeStatusTracker
{
    private DateTimeOffset _loadingStartedAt;
    private string _loadingModelId = "";
    private string _loadingModelName = "";
    private string _loadingEndpoint = "";
    private string _loadedStatusModelId = "";
    private string _loadedStatusText = "";

    public bool HasLoadingStatus => !string.IsNullOrWhiteSpace(_loadingModelName);

    public void StartLoading(string modelId, string modelName, string endpointDisplay, DateTimeOffset now)
    {
        ClearLoadedStatus();
        _loadingModelId = modelId ?? "";
        _loadingModelName = modelName ?? "";
        _loadingEndpoint = endpointDisplay ?? "";
        _loadingStartedAt = now;
    }

    public ModelRuntimeStatusDisplay? LoadingStatusFor(string? selectedModelId, DateTimeOffset now)
    {
        if (!HasLoadingStatus || !AppliesToSelectedModel(selectedModelId, _loadingModelId))
            return null;

        var elapsed = Elapsed(now - _loadingStartedAt);
        return new ModelRuntimeStatusDisplay(
            _loadingModelId,
            $"Loading Model: {_loadingModelName}\nLoading Time: {elapsed}",
            ModelRuntimeStatusKind.Loading,
            $"Loading {_loadingModelName} at {_loadingEndpoint}.");
    }

    public ModelRuntimeStatusDisplay? StopLoading(
        bool showLoadedDuration,
        string loadedModelName,
        DateTimeOffset now)
    {
        var hadLoadingStatus = HasLoadingStatus;
        if (!hadLoadingStatus)
            return null;

        ClearLoadedStatus();

        var elapsed = now - _loadingStartedAt;
        var modelId = _loadingModelId;
        var modelName = string.IsNullOrWhiteSpace(loadedModelName) ? _loadingModelName : loadedModelName;

        _loadingModelId = "";
        _loadingModelName = "";
        _loadingEndpoint = "";

        if (!showLoadedDuration || !hadLoadingStatus || string.IsNullOrWhiteSpace(modelName))
            return null;

        _loadedStatusModelId = modelId;
        _loadedStatusText = $"Loaded Model: {modelName}\nLoading Time: {Elapsed(elapsed)}";
        return new ModelRuntimeStatusDisplay(_loadedStatusModelId, _loadedStatusText, ModelRuntimeStatusKind.Loaded);
    }

    public bool IsLoadingModel(string modelId)
        => HasLoadingStatus && string.Equals(_loadingModelId, modelId, StringComparison.OrdinalIgnoreCase);

    public ModelRuntimeStatusDisplay? LoadedStatusFor(string? selectedModelId)
    {
        if (string.IsNullOrWhiteSpace(_loadedStatusText)
            || !AppliesToSelectedModel(selectedModelId, _loadedStatusModelId))
            return null;

        return new ModelRuntimeStatusDisplay(_loadedStatusModelId, _loadedStatusText, ModelRuntimeStatusKind.Loaded);
    }

    public ModelRuntimeStatusDisplay StatusFor(string? selectedModelId, string fallbackModelStatus, DateTimeOffset now)
        => LoadingStatusFor(selectedModelId, now)
            ?? LoadedStatusFor(selectedModelId)
            ?? new ModelRuntimeStatusDisplay(selectedModelId ?? "", fallbackModelStatus, ModelRuntimeStatusKind.Fallback);

    public void ClearLoadedStatus()
    {
        _loadedStatusModelId = "";
        _loadedStatusText = "";
    }

    private static bool AppliesToSelectedModel(string? selectedModelId, string statusModelId)
        => string.IsNullOrWhiteSpace(selectedModelId)
            || string.Equals(selectedModelId, statusModelId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Elapsed-formatter uit het referentieproject (DisplayFormatService.Elapsed).</summary>
    private static string Elapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed.TotalMinutes < 1)
            return $"{Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds))}s";
        if (elapsed.TotalHours < 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s";
        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m {elapsed.Seconds:00}s";
    }
}
