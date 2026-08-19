using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

/// <summary>Kaarten-snapshot die de poller naar de UI stuurt (naar main thread maren).</summary>
public sealed record MetricCardsSnapshot(
    string StatsText,
    string TokensText,
    string MtpTokensText,
    string KvCacheText,
    string HardwareText,
    bool HasRuntime);

public sealed class MetricCardsUpdatedEventArgs : EventArgs
{
    public MetricCardsUpdatedEventArgs(MetricCardsSnapshot snapshot) => Snapshot = snapshot;
    public MetricCardsSnapshot Snapshot { get; }
}

/// <summary>
/// Pollt per tick /slots (altijd) en /metrics (alleen EnableMetrics is not false) van de
/// current server (ServerHealthService-patroon: StateChanged → start/stop, 2 s-tick, 2 s timeout).
/// Niet-success status op /metrics (bv. 501 "not enabled") = metrics niet beschikbaar:
/// leeg-lijst, géén log-spam/fout; /slots-data + last-known-retentie blijven gelden.
/// </summary>
public sealed class RuntimeMetricPollerService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly LlamaServerProcessService _processService;
    private readonly RuntimeMetricSummaryTracker _summaryTracker;
    private readonly GpuSummaryService _gpuSummary;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    private CancellationTokenSource? _cts;

    /// <summary>Kaarten-data voor de Overzicht-kaarten (op de thread waar MetricsUpdated brand).</summary>
    public event EventHandler<MetricCardsUpdatedEventArgs>? MetricsUpdated;

    public RuntimeMetricPollerService(
        LlamaServerProcessService processService,
        RuntimeMetricSummaryTracker summaryTracker,
        GpuSummaryService gpuSummary)
    {
        _processService = processService;
        _summaryTracker = summaryTracker;
        _gpuSummary = gpuSummary;
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
                return; // al aan het pollen
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
                KvCacheText: string.Empty,
                HardwareText: string.Empty,
                HasRuntime: false));
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
        // 1) /slots — altijd (enabled door llama-server)
        RuntimeSlotSnapshot? slotSnapshot = null;
        try
        {
            var raw = await _httpClient.GetStringAsync($"http://127.0.0.1:{session.Port}/slots", token);
            slotSnapshot = RuntimeDashboardService.ParseSlotSnapshot(raw);
        }
        catch
        {
            // server nog niet bereikbaar → alleen last-known-retentie
        }

        // 2) /metrics — alleen wanneer EnableMetrics is not false.
        // Niet-success (bv. 501) = endpoint niet ingeschakeld: leeg-lijst, géén fout-log.
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
                // niet bereikbaar/timeout → leeg-lijst (last-known-retentie via de tracker)
            }
        }

        // 3) Summary-tracker (rates, totals, last-known) + GPU-summary (cache)
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
        var hardwareText = await _gpuSummary.SummaryAsync(session, token);

        RaiseUpdated(new MetricCardsSnapshot(
            StatsText: RuntimeDashboardService.RuntimeSlotsLabel(samples, slotSnapshot, context.ParallelSlots),
            TokensText: summary.Tokens,
            MtpTokensText: summary.MtpTokens,
            KvCacheText: summary.KvCache,
            HardwareText: hardwareText,
            HasRuntime: true));
    }

    private void RaiseUpdated(MetricCardsSnapshot snapshot) =>
        MetricsUpdated?.Invoke(this, new MetricCardsUpdatedEventArgs(snapshot));
}
