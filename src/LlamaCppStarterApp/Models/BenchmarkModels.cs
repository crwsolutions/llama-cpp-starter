using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LlamaCppStarterApp.Models;

public sealed class BenchmarkSettings
{
    public string DatasetVersion { get; set; } = "1.0";
    public List<string> ScenarioIds { get; set; } = new();
    public int Repetitions { get; set; } = 3;
    public int Warmups { get; set; } = 1;
    public string Comment { get; set; } = string.Empty;
}

public sealed class WorkloadDataset
{
    public string Version { get; set; } = string.Empty;
    public List<WorkloadScenario> Scenarios { get; set; } = new();
}

public sealed class WorkloadScenario
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = "1";
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? CodeContext { get; set; }
    public string? ExpectedOutput { get; set; }
    public int MaxToolRounds { get; set; } = 2;
    public int MaxRequests { get; set; } = 4;
    public List<WorkloadTool> Tools { get; set; } = new();
}

public sealed class WorkloadTool
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonElement Parameters { get; set; }
    public List<string> Required { get; set; } = new();
    public Dictionary<string, string> PropertyTypes { get; set; } = new(StringComparer.Ordinal);
    public string SimulatedResponse { get; set; } = string.Empty;
}

public sealed class BenchmarkRun
{
    public long Id { get; set; }
    public int? ConfigurationId { get; set; }
    public string ConfigurationName { get; set; } = string.Empty;
    public string ConfigurationSnapshotJson { get; set; } = string.Empty;
    public long StartedAtUtc { get; set; }
    public long? EndedAtUtc { get; set; }
    public string Status { get; set; } = "Running";
    public string ModelName { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string ModelQuant { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public int ProfileId { get; set; }
    public string RuntimeName { get; set; } = string.Empty;
    public string RuntimePath { get; set; } = string.Empty;
    public string BuildIdentity { get; set; } = string.Empty;
    public string LlamaCppVersion { get; set; } = string.Empty;
    public int Repetitions { get; set; } = 3;
    public int Warmups { get; set; } = 1;
    public string Comment { get; set; } = string.Empty;
    public int ServerPid { get; set; }
    public string LaunchParametersJson { get; set; } = string.Empty;
    public string DatasetVersion { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public sealed class BenchmarkMeasurement
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public bool IsWarmup { get; set; }
    public string ScenarioId { get; set; } = string.Empty;
    public string ScenarioVersion { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Repetition { get; set; }
    public int? PromptTokens { get; set; }
    public int? GeneratedTokens { get; set; }
    public int? CachedPromptTokens { get; set; }
    public string CacheState { get; set; } = "unknown";
    public double? PromptMilliseconds { get; set; }
    public double? GenerationMilliseconds { get; set; }
    public double? TotalMilliseconds { get; set; }
    public double? ClientWallMilliseconds { get; set; }
    public double? PromptTokensPerSecond { get; set; }
    public double? GenerationTokensPerSecond { get; set; }
    public int ToolCalls { get; set; }
    public int FailedToolCalls { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class BenchmarkProgressEventArgs : EventArgs
{
    public BenchmarkProgressEventArgs(int completed, int total, string scenarioTitle, string category, int repetition, bool isWarmup, string status, BenchmarkMeasurement? measurement = null)
    {
        Completed = completed;
        Total = total;
        Measurement = measurement;
        ScenarioTitle = scenarioTitle;
        Category = category;
        Repetition = repetition;
        IsWarmup = isWarmup;
        Status = status;
    }

    public int Completed { get; }
    public int Total { get; }
    public BenchmarkMeasurement? Measurement { get; }
    public string ScenarioTitle { get; }
    public string Category { get; }
    public int Repetition { get; }
    public bool IsWarmup { get; }
    public string Status { get; }
}

public sealed class BenchmarkHistoryItem : ObservableObject
{
    private bool _isComparisonRun;

    public BenchmarkRun Run { get; set; } = new();
    public bool IsComparisonRun
    {
        get => _isComparisonRun;
        set => SetProperty(ref _isComparisonRun, value);
    }
    public string DateText { get; set; } = string.Empty;
    public string ModelText { get; set; } = string.Empty;
    public string ProfileText { get; set; } = string.Empty;
    public string BuildText { get; set; } = string.Empty;
    public string LlamaCppVersionText { get; set; } = string.Empty;
    public string MeasurementsText { get; set; } = string.Empty;
    public string WarmupsText { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public string GenerationSpeedText { get; set; } = string.Empty;
    public string PromptSpeedText { get; set; } = string.Empty;
    public string FailedText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string DurationText { get; set; } = string.Empty;
    public bool CanDelete => Run.Status != "Running";
}

public sealed class BenchmarkCategoryResult
{
    public string Category { get; set; } = string.Empty;
    public string GenerationSpeed { get; set; } = string.Empty;
    public string PromptSpeed { get; set; } = string.Empty;
    public int FailedToolCalls { get; set; }
    public int Measurements { get; set; }
    public string Status { get; set; } = string.Empty;
    public string GenerationComparisonText { get; set; } = string.Empty;
    public string PromptComparisonText { get; set; } = string.Empty;
    public string FailedComparisonText { get; set; } = string.Empty;
    public string StatusComparisonText { get; set; } = string.Empty;
    public string GenerationSpeedText => GenerationSpeed;
    public string PromptSpeedText => PromptSpeed;
    public string FailedToolCallsText => FailedToolCalls.ToString();
}
