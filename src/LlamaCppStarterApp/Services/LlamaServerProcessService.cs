using System.Diagnostics;
using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Services;

public class ServerLogEventArgs : EventArgs
{
    public ServerLogEventArgs(string line) => Line = line;
    public string Line { get; }
}

public class ServerStateChangedEventArgs : EventArgs
{
    public ServerStateChangedEventArgs(LlamaServerState state) => State = state;
    public LlamaServerState State { get; }
}

/// <summary>
/// Beheert één "current server" (llama-server.exe proces).
/// Load/Unload met best-effort POST /exit, max 30 s wachten, daarna Kill(entireProcessTree).
/// </summary>
public class LlamaServerProcessService
{
    private const int UnloadWaitSeconds = 30;
    private const int ExitPostTimeoutMs = 5000;

    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMilliseconds(ExitPostTimeoutMs) };

    private Process? _process;
    private LlamaServerState _state = LlamaServerState.Idle;
    private int _port;
    private string? _modelName;
    private string? _hostBind;
    private int _lastExitCode;

    public LlamaServerState State
    {
        get
        {
            CheckAlive();
            return _state;
        }
        private set => SetState(value);
    }

    public int Port
    {
        get
        {
            CheckAlive();
            return _port;
        }
        private set => _port = value;
    }

    public string? ModelName
    {
        get
        {
            CheckAlive();
            return _modelName;
        }
        private set => _modelName = value;
    }

    public int LastExitCode
    {
        get
        {
            CheckAlive();
            return _lastExitCode;
        }
        private set => _lastExitCode = value;
    }

    /// <summary>Log-regel uit stdout/stderr (stderr krijgt een "[stderr] " prefix).</summary>
    public event EventHandler<ServerLogEventArgs>? LogReceived;

    /// <summary>Statuswijziging (Idle/Starting/Running/Stopping).</summary>
    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    public async Task<bool> LoadAsync(Runtime runtime, Model model, ProfileParameters parameters, int port)
    {
        if (!await _operationLock.WaitAsync(0))
        {
            return false; // al bezig met een andere operatie
        }

        try
        {
            if (State is LlamaServerState.Starting or LlamaServerState.Running)
            {
                return false;
            }

            if (!File.Exists(runtime.ExecutablePath))
            {
                RaiseLog($"Runtime niet gevonden: {runtime.ExecutablePath}");
                return false;
            }

            if (!File.Exists(model.Path))
            {
                RaiseLog($"Model niet gevonden: {model.Path}");
                return false;
            }

            // --spec-draft-model-resolutie op het moment van laden (pure static; embedded MTP → null).
            var draftModelPath = ModelCompanionService.ResolveDraftModelPath(model.Path, parameters.SpecType, parameters.SpecDraftPath);
            var args = LlamaServerCommandBuilder.BuildArgs(runtime, model, parameters, port, draftModelPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = runtime.ExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = runtime.Location ?? Path.GetDirectoryName(runtime.ExecutablePath) ?? string.Empty
            };
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    RaiseLog(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    RaiseLog($"[stderr] {e.Data}");
                }
            };

            if (!process.Start())
            {
                RaiseLog("Kon llama-server.exe niet starten.");
                process.Dispose();
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;
            Port = port;
            ModelName = model.Name;
            _hostBind = parameters.HostBind;
            State = LlamaServerState.Starting;

            RaiseLog($"Opstarten: {LlamaServerCommandBuilder.BuildCommandLine(args)}");

            // Model laden duurt lang; de loop blijft draaien tot het proces eindigt.
            _ = Task.Run(async () =>
            {
                try
                {
                    await process.WaitForExitAsync();
                    _lastExitCode = process.ExitCode;
                    if (_state is LlamaServerState.Starting or LlamaServerState.Running)
                    {
                        RaiseLog($"llama-server gestopt (exit code {_lastExitCode}).");
                        State = LlamaServerState.Idle;
                        _process = null;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // proces al afgesloten
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            RaiseLog($"Fout bij opstarten: {ex.Message}");
            State = LlamaServerState.Idle;
            _process = null;
            return false;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task UnloadAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            var process = _process;
            if (process is null || _state is not (LlamaServerState.Running or LlamaServerState.Starting))
            {
                return;
            }

            State = LlamaServerState.Stopping;
            RaiseLog("Unloading…");

            // 1) Best-effort POST /exit (5 s timeout, errors negeren)
            try
            {
                var host = string.IsNullOrWhiteSpace(_hostBind) || _hostBind == "0.0.0.0"
                    ? "127.0.0.1"
                    : _hostBind;
                await _httpClient.PostAsync($"http://{host}:{_port}/exit", new StringContent(""));
            }
            catch
            {
                // negeren; fallback op proces-wachten/kill
            }

            // 2) Max 30 s wachten op proceseindiging
            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(UnloadWaitSeconds));
            try
            {
                await process.WaitForExitAsync(exitCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 3) Timeout → hard kill van het hele procesboom
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    RaiseLog($"[stderr] Kon proces niet beëindigen: {ex.Message}");
                }
            }

            _lastExitCode = SafeExitCode(process);
            RaiseLog("Server gestopt.");
            State = LlamaServerState.Idle;
            _process = null;
            ModelName = null;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Markeert de server als Running zodra health-polling een 200 op /health ziet.
    /// </summary>
    public void MarkRunning()
    {
        if (_state == LlamaServerState.Starting)
        {
            State = LlamaServerState.Running;
        }
    }

    /// <summary>
    /// Als het proces al is gestopt maar de status dat nog niet weergeeft, bijwerken.
    /// </summary>
    public void CheckAlive()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        bool exited;
        try
        {
            exited = process.HasExited;
        }
        catch (ObjectDisposedException)
        {
            exited = true;
        }

        if (exited && _state is LlamaServerState.Starting or LlamaServerState.Running)
        {
            _lastExitCode = SafeExitCode(process);
            RaiseLog($"llama-server gestopt (exit code {_lastExitCode}).");
            State = LlamaServerState.Idle;
            _process = null;
        }
    }

    private void SetState(LlamaServerState value)
    {
        if (_state == value)
        {
            return;
        }

        _state = value;
        StateChanged?.Invoke(this, new ServerStateChangedEventArgs(value));
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private void RaiseLog(string line) => LogReceived?.Invoke(this, new ServerLogEventArgs(line));
}
