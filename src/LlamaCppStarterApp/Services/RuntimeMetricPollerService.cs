using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

/// <summary>Cards snapshot the poller sends to the UI (marshaled to the main thread).</summary>
/// (Hardware is not part of this snapshot: the Hardware card polls the machine-wide
/// nvidia-smi listing on its own, independent of a loaded model.)
public sealed record MetricCardsSnapshot(
    string StatsText,
    string TokensText,
    string MtpTokensText,
    string KvCacheText);

public sealed class MetricCardsUpdatedEventArgs : EventArgs
{
    public MetricCardsUpdatedEventArgs(MetricCardsSnapshot snapshot) => Snapshot = snapshot;
    public MetricCardsSnapshot Snapshot { get; }
}

/// <summary>
/// Polls /slots (always) and /metrics (only when EnableMetrics is not false) of the
/// current server per tick (ServerHealthService pattern: StateChanged → start/stop, 2 s tick, 2 s timeout).
/// Non-success status on /metrics (e.g. 501 "not enabled") = metrics not available:
/// empty list, no log spam/error; /slots data + last-known retention remain valid.
/// </summary>
public sealed class RuntimeMetricPollerService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly LlamaServerProcessService _processService;
    private readonly RuntimeMetricSummaryTracker _summaryTracker;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    private CancellationTokenSource? _cts;

    /// <summary>Card data for the Overview cards (on the thread where MetricsUpdated fires).</summary>
    public event EventHandler<MetricCardsUpdatedEventArgs>? MetricsUpdated;

    public RuntimeMetricPollerService(
        LlamaServerProcessService processService,
        RuntimeMetricSummaryTracker summaryTracker)
    {
        _processService = processService;
        _summaryTracker = summaryTracker;
        _processService.StateChanged += OnProcessStateChanged;
    }

    private void OnProcessStateChanged(object? sender, ServerStateChangedEventArgs e)
    {
        if (e.State is LlamaServerState.Starting or LlamaServerState.Running)
        {
            StartPolling();
        }
        else
        {
            StopPolling();
        }
    }

    private void StartPolling()
    {
        if (!_pollLock.Wait(0))
        {
            return;
        }

        try
        {
            if (_cts is not null)
            {
                return; // already polling
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => PollLoopAsync(token), token);
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private void StopPolling()
    {
        if (!_pollLock.Wait(0))
        {
            return;
        }

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _summaryTracker.Reset();
            RaiseUpdated(new MetricCardsSnapshot(
                StatsText: string.Empty,
                TokensText: string.Empty,
                MtpTokensText: string.Empty,
                KvCacheText: string.Empty));
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var session = _processService.Session;
            var state = _processService.State;
            if (session is null || state is not (LlamaServerState.Starting or LlamaServerState.Running))
            {
                break;
            }

            await TickAsync(session, token);

            try
            {
                await Task.Delay(PollInterval, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(LoadedSession session, CancellationToken token)
    {
        // 1) /slots — always (enabled by llama-server)
        RuntimeSlotSnapshot? slotSnapshot = null;
        try
        {
            var raw = await _httpClient.GetStringAsync($"http://127.0.0.1:{session.Port}/slots", token);
            slotSnapshot = RuntimeDashboardService.ParseSlotSnapshot(raw);
        }
        catch
        {
            // server not reachable yet → last-known retention only
        }

        // 2) /metrics — only when EnableMetrics is not false.
        // Non-success (e.g. 501) = endpoint not enabled: empty list, no error log.
        IReadOnlyList<PrometheusSample> samples = [];
        if (session.Parameters.EnableMetrics is not false)
        {
            try
            {
                using var response = await _httpClient.GetAsync($"http://127.0.0.1:{session.Port}/metrics", token);
                if (response.IsSuccessStatusCode)
                {
                    samples = RuntimeMetrics.ParsePrometheus(await response.Content.ReadAsStringAsync(token));
                }
            }
            catch
            {
                // not reachable/timeout → empty list (last-known retention via the tracker)
            }
        }

        // 3) Summary tracker (rates, totals, last-known).
        // The Hardware card is polled separately by the Overview VM (nvidia-smi is
        // independent of a loaded model).
        var context = new RuntimeMetricContext(
            session.Parameters.Parallel is > 0 ? session.Parameters.Parallel.Value : 1,
            session.Parameters.CtxSize);
        var summary = _summaryTracker.Apply(
            $"{session.Model.ModelId}|{session.Port}",
            samples,
            context,
            slotSnapshot,
            null,
            DateTimeOffset.UtcNow);

        RaiseUpdated(new MetricCardsSnapshot(
            StatsText: RuntimeDashboardService.RuntimeSlotsLabel(samples, slotSnapshot, context.ParallelSlots),
            TokensText: summary.Tokens,
            MtpTokensText: summary.MtpTokens,
            KvCacheText: summary.KvCache));
    }

    private void RaiseUpdated(MetricCardsSnapshot snapshot) =>
        MetricsUpdated?.Invoke(this, new MetricCardsUpdatedEventArgs(snapshot));
}
