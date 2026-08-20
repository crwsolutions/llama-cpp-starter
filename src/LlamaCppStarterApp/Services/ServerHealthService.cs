using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

public class ServerHealthEventArgs : EventArgs
{
    public ServerHealthEventArgs(bool healthy) => Healthy = healthy;
    public bool Healthy { get; }
}

/// <summary>
/// Light poll of http://{host}:{port}/health (every 2 s, only while the server
/// is Starting/Running). Host = HostBind when localized, otherwise 127.0.0.1.
/// </summary>
public class ServerHealthService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly LlamaServerProcessService _processService;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private bool _healthy;

    public bool Healthy
    {
        get => _healthy;
        private set => _healthy = value;
    }

    public event EventHandler<ServerHealthEventArgs>? HealthChanged;

    public ServerHealthService(LlamaServerProcessService processService)
    {
        _processService = processService;
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
            SetHealthy(false);
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
            var state = _processService.State;
            if (state is not (LlamaServerState.Starting or LlamaServerState.Running))
            {
                break;
            }

            var healthy = await CheckHealthAsync(_processService.Port);
            SetHealthy(healthy);

            if (healthy && state == LlamaServerState.Starting)
            {
                _processService.MarkRunning();
            }

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

    private async Task<bool> CheckHealthAsync(int port)
    {
        try
        {
            var response = await _httpClient.GetAsync($"http://127.0.0.1:{port}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void SetHealthy(bool value)
    {
        if (_healthy == value)
        {
            return;
        }

        _healthy = value;
        HealthChanged?.Invoke(this, new ServerHealthEventArgs(value));
    }
}
