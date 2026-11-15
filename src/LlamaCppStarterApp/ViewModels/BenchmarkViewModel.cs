using System.Text.Json;
using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;
using LlamaCppStarterApp.Services;

namespace LlamaCppStarterApp.ViewModels;

public partial class BenchmarkViewModel : BaseViewModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private const string RepetitionsSetting = "BenchmarkRepetitions";
    private const string WarmupEnabledSetting = "BenchmarkWarmupEnabled";

    private readonly IBenchmarkRepository _repository;
    private readonly IAppSettingsRepository _appSettings;
    private readonly LlamaServerProcessService _processService;
    private readonly BenchmarkRunnerService _runner;
    private const int DefaultRepetitions = 3;
    private readonly OverviewViewModel _overview;
    private readonly List<double> _livePromptRates = new();
    private readonly List<double> _liveGenerationRates = new();
    private readonly SemaphoreSlim _historyRefreshLock = new(1, 1);
    private bool _isLoadingPreferences;
    private bool _updatingComparisonSelection;
    private long? _comparisonRunId;
    private int _liveFailedToolCalls;
    private int _liveValidMeasurements;
    private CancellationTokenSource? _runCancellation;
    private WorkloadDataset? _dataset;

    public BenchmarkViewModel(
        IBenchmarkRepository repository,
        IAppSettingsRepository appSettings,
        LlamaServerProcessService processService,
        BenchmarkRunnerService runner,
        OverviewViewModel overview)
    {
        _repository = repository;
        _appSettings = appSettings;
        _processService = processService;
        _runner = runner;
        _overview = overview;
        Title = "Benchmark";
        _runner.ProgressChanged += OnProgressChanged;
        _processService.StateChanged += OnServerStateChanged;
        _overview.PropertyChanged += OnOverviewPropertyChanged;
    }

    [ObservableProperty]
    public partial ObservableCollection<BenchmarkHistoryItem> History { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<BenchmarkCategoryResult> CategoryResults { get; set; } = new();

    [ObservableProperty]
    public partial BenchmarkHistoryItem? SelectedHistoryItem { get; set; }

    [ObservableProperty]
    public partial string SelectedModelText { get; set; } = "Not selected";

    [ObservableProperty]
    public partial string SelectedProfileText { get; set; } = "Not selected";

    [ObservableProperty]
    public partial string SelectedRuntimeText { get; set; } = "Not selected";

    public string CommandPreview => _overview.CommandPreview;

    [ObservableProperty]
    public partial string ActiveSessionText { get; set; } = "Select a model, profile, and runtime on Overview.";

    [ObservableProperty]
    public partial bool IsServerReady { get; set; }

    [ObservableProperty]
    public partial int Repetitions { get; set; } = DefaultRepetitions;

    [ObservableProperty]
    public partial bool WarmupEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string Comment { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string LastRunSummary { get; set; } = "No benchmark run yet.";

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    partial void OnIsRunningChanged(bool value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnRepetitionsChanged(int value)
    {
        if (!_isLoadingPreferences && value is >= 1 and <= 30)
        {
            _ = SavePreferenceSafelyAsync(RepetitionsSetting, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    partial void OnWarmupEnabledChanged(bool value)
    {
        if (!_isLoadingPreferences)
        {
            _ = SavePreferenceSafelyAsync(WarmupEnabledSetting, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await LoadPreferencesAsync();
            await _overview.EnsureLoadedAsync();
            await _runner.RecoverInterruptedRunsAsync();
            _dataset = await _runner.LoadDatasetAsync();
            await RefreshSessionAsync();
            await RefreshHistoryAsync();
            if (string.IsNullOrEmpty(StatusText))
            {
                StatusText = $"Fixed workload: {_dataset.Scenarios.Count} scenarios, {Repetitions} measurements per scenario.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load benchmark data: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        await RefreshSessionAsync();
        if (!IsServerReady)
        {
            StatusText = ActiveSessionText;
            return;
        }
        if (_dataset is null)
        {
            try
            {
                _dataset = await _runner.LoadDatasetAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to load workload dataset: {ex.Message}";
                return;
            }
        }

        if (Repetitions is < 1 or > 30)
        {
            StatusText = "Measurements per scenario must be between 1 and 30.";
            return;
        }

        var settings = CreateSettings();
        _livePromptRates.Clear();
        _liveGenerationRates.Clear();
        _liveFailedToolCalls = 0;
        _liveValidMeasurements = 0;
        LastRunSummary = "Preparing measurements…";
        IsRunning = true;
        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "Starting benchmark…";
        StatusText = string.Empty;
        _runCancellation = new CancellationTokenSource();
        try
        {
            var run = await _runner.RunAsync(settings, _runCancellation.Token);
            LastRunSummary = await CreateRunSummaryAsync(run);
            StatusText = run.ErrorMessage is null
                ? $"Benchmark {run.Status.ToLowerInvariant()}."
                : $"Benchmark {run.Status.ToLowerInvariant()}: {run.ErrorMessage}";
            await RefreshHistoryAsync(selectNewest: true);
        }
        catch (Exception ex)
        {
            StatusText = $"Benchmark could not start: {ex.Message}";
        }
        finally
        {
            _runCancellation.Dispose();
            _runCancellation = null;
            IsRunning = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Stop() => _runCancellation?.Cancel();

    public async Task DeleteRunAsync(BenchmarkHistoryItem item)
    {
        if (item.Run.Status == "Running")
        {
            return;
        }

        try
        {
            await _repository.DeleteRunAsync(item.Run.Id);
            if (_comparisonRunId == item.Run.Id)
            {
                _comparisonRunId = null;
            }
            if (SelectedHistoryItem?.Run.Id == item.Run.Id)
            {
                SelectedHistoryItem = null;
                CategoryResults.Clear();
            }
            await RefreshHistoryAsync();
            StatusText = $"Deleted benchmark run from {item.DateText}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to delete benchmark run: {ex.Message}";
        }
    }

    public void SetComparisonRun(BenchmarkHistoryItem item, bool isComparisonRun)
    {
        if (_updatingComparisonSelection)
        {
            return;
        }

        _comparisonRunId = isComparisonRun ? item.Run.Id : _comparisonRunId == item.Run.Id ? null : _comparisonRunId;
        _updatingComparisonSelection = true;
        try
        {
            foreach (var historyItem in History)
            {
                historyItem.IsComparisonRun = historyItem.Run.Id == _comparisonRunId;
            }
        }
        finally
        {
            _updatingComparisonSelection = false;
        }

        _ = SelectRunAsync(SelectedHistoryItem);
    }

    public async Task SelectRunAsync(BenchmarkHistoryItem? item)
    {
        SelectedHistoryItem = item;
        CategoryResults.Clear();
        if (item is null)
        {
            return;
        }

        try
        {
            var measurements = await _repository.GetMeasurementsAsync(item.Run.Id);
            var comparisonMeasurements = _comparisonRunId is long comparisonRunId && comparisonRunId != item.Run.Id
                ? await _repository.GetMeasurementsAsync(comparisonRunId)
                : new List<BenchmarkMeasurement>();
            var comparisonGroups = comparisonMeasurements.Where(measurement => !measurement.IsWarmup)
                .GroupBy(measurement => measurement.Category, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var results = measurements.Where(measurement => !measurement.IsWarmup)
                .GroupBy(measurement => measurement.Category)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    comparisonGroups.TryGetValue(group.Key, out var comparisonGroup);
                    var comparisonStatus = comparisonGroup is null
                        ? string.Empty
                        : " (" + (History.FirstOrDefault(historyItem => historyItem.Run.Id == _comparisonRunId)?.Run.Status ?? "—") + ")";
                    return new BenchmarkCategoryResult
                    {
                        Category = group.Key,
                        Measurements = group.Count(),
                        FailedToolCalls = group.Sum(measurement => measurement.FailedToolCalls),
                        Status = item.Run.Status,
                        GenerationSpeed = FormatMedian(group.Where(IsComparable).Select(measurement => measurement.GenerationTokensPerSecond)),
                        PromptSpeed = FormatMedian(group.Where(IsComparable).Select(measurement => measurement.PromptTokensPerSecond)),
                        GenerationComparisonText = comparisonGroup is null ? string.Empty : $" ({FormatMedian(comparisonGroup.Where(IsComparable).Select(measurement => measurement.GenerationTokensPerSecond))})",
                        PromptComparisonText = comparisonGroup is null ? string.Empty : $" ({FormatMedian(comparisonGroup.Where(IsComparable).Select(measurement => measurement.PromptTokensPerSecond))})",
                        FailedComparisonText = comparisonGroup is null ? string.Empty : $" ({comparisonGroup.Sum(measurement => measurement.FailedToolCalls)})",
                        StatusComparisonText = comparisonStatus
                    };
                });
            CategoryResults = new ObservableCollection<BenchmarkCategoryResult>(results);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load run details: {ex.Message}";
        }
    }

    private bool CanStart() => !IsRunning;

    private BenchmarkSettings CreateSettings() => new()
    {
        DatasetVersion = _dataset?.Version ?? string.Empty,
        ScenarioIds = _dataset?.Scenarios.Select(scenario => scenario.Id).ToList() ?? new List<string>(),
        Repetitions = Repetitions,
        Warmups = WarmupEnabled ? 1 : 0,
        Comment = Comment.Trim()
    };

    private async Task LoadPreferencesAsync()
    {
        _isLoadingPreferences = true;
        try
        {
            var repetitions = await _appSettings.GetValueAsync(RepetitionsSetting);
            if (int.TryParse(repetitions, NumberStyles.Integer, CultureInfo.InvariantCulture, out var savedRepetitions)
                && savedRepetitions is >= 1 and <= 30)
            {
                Repetitions = savedRepetitions;
            }

            var warmupEnabled = await _appSettings.GetValueAsync(WarmupEnabledSetting);
            if (bool.TryParse(warmupEnabled, out var savedWarmupEnabled))
            {
                WarmupEnabled = savedWarmupEnabled;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load saved benchmark settings: {ex.Message}";
        }
        finally
        {
            _isLoadingPreferences = false;
        }
    }

    private async Task SavePreferenceSafelyAsync(string key, string value)
    {
        try
        {
            await _appSettings.SetAsync(key, value);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not save benchmark settings: {ex.Message}";
        }
    }

    private Task RefreshSessionAsync()
    {
        var model = _overview.SelectedModel;
        var profile = _overview.SelectedProfile;
        var runtime = _overview.SelectedRuntime;
        SelectedModelText = model is null ? "Not selected" : string.IsNullOrWhiteSpace(model.Quant) ? model.Name : $"{model.Name} ({model.Quant})";
        SelectedProfileText = profile?.Name ?? "Not selected";
        SelectedRuntimeText = runtime?.Name ?? "Not selected";

        var session = _processService.Session;
        IsServerReady = session is not null
            && _processService.State == LlamaServerState.Running
            && model?.Id == session.Model.Id
            && profile?.Id == session.LoadedProfileId
            && runtime?.Id == session.Runtime.Id;
        ActiveSessionText = IsServerReady
            ? $"Selected target is loaded (PID {session!.ProcessId}, port {session.Port})."
            : session is null || _processService.State != LlamaServerState.Running
                ? "Load the selected model, profile, and runtime on Overview before starting."
                : "The running server differs from the Overview selection. Load the selected target on Overview first.";
        return Task.CompletedTask;
    }

    private async Task RefreshHistoryAsync(bool selectNewest = false)
    {
        await _historyRefreshLock.WaitAsync();
        try
        {
            var runs = await _repository.GetRecentRunsAsync();
            var measurements = runs.Count == 0
                ? new List<BenchmarkMeasurement>()
                : await _repository.GetMeasurementsForRunsAsync(runs.Select(run => run.Id).ToArray());
            var groupedMeasurements = measurements.GroupBy(measurement => measurement.RunId).ToDictionary(group => group.Key, group => group.ToList());
            if (_comparisonRunId is long comparisonRunId && runs.All(run => run.Id != comparisonRunId))
            {
                _comparisonRunId = null;
            }
            var history = runs.Select(run =>
            {
                var item = CreateHistoryItem(run, groupedMeasurements.GetValueOrDefault(run.Id) ?? new List<BenchmarkMeasurement>());
                item.IsComparisonRun = run.Id == _comparisonRunId;
                return item;
            }).ToList();
            History = new ObservableCollection<BenchmarkHistoryItem>(history);
            if (history.Count > 0)
            {
                var selected = selectNewest
                    ? history[0]
                    : history.FirstOrDefault(item => item.Run.Id == SelectedHistoryItem?.Run.Id) ?? history[0];
                await SelectRunAsync(selected);
            }
            else
            {
                CategoryResults.Clear();
                SelectedHistoryItem = null;
            }
        }
        finally
        {
            _historyRefreshLock.Release();
        }
    }

    private BenchmarkHistoryItem CreateHistoryItem(BenchmarkRun run, IReadOnlyCollection<BenchmarkMeasurement> measurements)
    {
        var useful = measurements.Where(measurement => !measurement.IsWarmup && IsComparable(measurement)).ToList();
        var generation = Median(useful.Select(measurement => measurement.GenerationTokensPerSecond));
        var prompt = Median(useful.Select(measurement => measurement.PromptTokensPerSecond));
        var failed = measurements.Where(measurement => !measurement.IsWarmup).Sum(measurement => measurement.FailedToolCalls);
        var elapsed = run.EndedAtUtc.HasValue ? Math.Max(0, run.EndedAtUtc.Value - run.StartedAtUtc) : 0;
        return new BenchmarkHistoryItem
        {
            Run = run,
            DateText = DateTimeOffset.FromUnixTimeMilliseconds(run.StartedAtUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ModelText = string.IsNullOrWhiteSpace(run.ModelQuant) ? run.ModelName : $"{run.ModelName} ({run.ModelQuant})",
            ProfileText = run.ProfileName,
            BuildText = run.BuildIdentity,
            LlamaCppVersionText = string.IsNullOrWhiteSpace(run.LlamaCppVersion) ? "Unavailable" : run.LlamaCppVersion,
            MeasurementsText = run.Repetitions.ToString(CultureInfo.InvariantCulture),
            WarmupsText = run.Warmups.ToString(CultureInfo.InvariantCulture),
            CommentText = run.Comment,
            GenerationSpeedText = FormatSpeed(generation),
            PromptSpeedText = FormatSpeed(prompt),
            FailedText = failed.ToString(CultureInfo.InvariantCulture),
            StatusText = run.Status,
            DurationText = TimeSpan.FromMilliseconds(elapsed).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture)
        };
    }

    private async Task<string> CreateRunSummaryAsync(BenchmarkRun run)
    {
        var measurements = await _repository.GetMeasurementsAsync(run.Id);
        var useful = measurements.Where(measurement => !measurement.IsWarmup && IsComparable(measurement)).ToList();
        var generation = Median(useful.Select(measurement => measurement.GenerationTokensPerSecond));
        var prompt = Median(useful.Select(measurement => measurement.PromptTokensPerSecond));
        var failed = measurements.Where(measurement => !measurement.IsWarmup).Sum(measurement => measurement.FailedToolCalls);
        var elapsed = run.EndedAtUtc.HasValue ? Math.Max(0, run.EndedAtUtc.Value - run.StartedAtUtc) : 0;
        return $"{run.Status} | PP {FormatSpeed(prompt)} | TG {FormatSpeed(generation)} | Failed tool calls {failed} | {TimeSpan.FromMilliseconds(elapsed).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture)}";
    }

    private void OnProgressChanged(object? sender, BenchmarkProgressEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressValue = e.Total == 0 ? 0 : (double)e.Completed / e.Total;
            var phase = e.IsWarmup ? "warm-up" : "measurement";
            ProgressText = e.Status is "Completed" or "Cancelled" or "Failed"
                ? $"{e.Status}: {e.Completed}/{e.Total} measurements"
                : $"{e.Completed}/{e.Total} | {e.ScenarioTitle} ({e.Category}) | {phase} {e.Repetition}";

            if (IsRunning)
            {
                UpdateLiveResult(e.Measurement);
                _ = RefreshHistorySafelyAsync(selectNewest: true);
            }
        });
    }

    private async Task RefreshHistorySafelyAsync(bool selectNewest)
    {
        try
        {
            await RefreshHistoryAsync(selectNewest);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to refresh benchmark history: {ex.Message}";
        }
    }

    private void UpdateLiveResult(BenchmarkMeasurement? measurement)
    {
        if (measurement is not null)
        {
            if (!measurement.IsWarmup)
            {
                _liveFailedToolCalls += measurement.FailedToolCalls;
            }
            if (!measurement.IsWarmup && IsComparable(measurement))
            {
                if (measurement.PromptTokensPerSecond is double promptRate)
                {
                    _livePromptRates.Add(promptRate);
                }
                if (measurement.GenerationTokensPerSecond is double generationRate)
                {
                    _liveGenerationRates.Add(generationRate);
                }
                _liveValidMeasurements++;
            }
        }

        LastRunSummary = $"Running | PP {FormatSpeed(Median(_livePromptRates.Cast<double?>()))} | TG {FormatSpeed(Median(_liveGenerationRates.Cast<double?>()))} | uncached {_liveValidMeasurements} measurements | failed tool calls {_liveFailedToolCalls}";
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () => await RefreshSessionSafelyAsync());

    private void OnOverviewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverviewViewModel.SelectedModel)
            or nameof(OverviewViewModel.SelectedProfile)
            or nameof(OverviewViewModel.SelectedRuntime))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(CommandPreview));
                _ = RefreshSessionSafelyAsync();
            });
        }
    }

    private async Task RefreshSessionSafelyAsync()
    {
        try
        {
            await RefreshSessionAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to refresh loaded server: {ex.Message}";
        }
    }

    private static bool IsComparable(BenchmarkMeasurement measurement) =>
        measurement.CacheState == "uncached" && string.IsNullOrEmpty(measurement.ErrorMessage);

    private static string FormatMedian(IEnumerable<double?> values) => FormatSpeed(Median(values));

    private static double? Median(IEnumerable<double?> values)
    {
        var ordered = values.Where(value => value.HasValue && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2d;
    }

    private static string FormatSpeed(double? value) => value.HasValue
        ? $"{value.Value:0.00}"
        : "—";
}
