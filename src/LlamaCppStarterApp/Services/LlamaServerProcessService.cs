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
/// Sessie-informatie van de (laatst) geladen server: welke runtime/model/profiel-parametercombinatie
/// draait op welk proces. Null = geen geladen server. Gebruikt door de Overzicht-kaarten
/// (nvidia-smi-PID-match, /metrics-toggel, configured slots).
/// </summary>
public sealed record LoadedSession(Runtime Runtime, Model Model, ProfileParameters Parameters, int Port, int ProcessId);

/// <summary>
/// Beheert één "current server" (llama-server.exe proces).
/// Unload: POST /exit (5 s timeout) → max 30 s wachten → Kill(entireProcessTree).
/// App-uitgang: ShutdownServer() — synchroon, kort (POST /exit 2 s → 5 s → kill)
/// en zonder UI-events, zodat het kill ook doorgaat als de window al is gedeactiveerd.
/// </summary>
public class LlamaServerProcessService
{
    private const int UnloadWaitSeconds = 30;
    private const int ExitPostTimeoutMs = 5000;
    private const int ExitPostFastTimeoutMs = 2000;
    private const int ExitShutdownWaitSeconds = 5;

    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMilliseconds(ExitPostTimeoutMs) };

    // true tijdens app-uitgang (Shutdown): dan worden StateChanged/LogReceived
    // onderdrukt, zodat de stop-sequence de UI-dispatcher niet raakt en de kill
    // doorloopt ook al de window al gedeactiveerd/vernietigd is (geen weestproces).
    private volatile bool _shuttingDown;

    private Process? _process;
    private LlamaServerState _state = LlamaServerState.Idle;
    private int _port;
    private string? _modelName;
    private string? _hostBind;
    private int _lastExitCode;
    private LoadedSession? _session;

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

    /// <summary>
    /// Informatie over de geladen sessie (null = geen server geladen). Wordt bijgewerkt bij
    /// LoadAsync en opgeborgen bij UnloadAsync én bij natuurlijke processtop (CheckAlive/wait-loop).
    /// </summary>
    public LoadedSession? Session
    {
        get
        {
            CheckAlive();
            return _session;
        }
        private set => _session = value;
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
            Session = new LoadedSession(runtime, model, parameters, port, process.Id);
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
                        Session = null;
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

    /// <summary>
    /// Unload de current server: POST /exit (5 s timeout), max 30 s wachten,
    /// daarna Kill(entireProcessTree).
    /// </summary>
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
                using var postCts = new CancellationTokenSource(ExitPostTimeoutMs);
                await _httpClient.PostAsync($"http://{host}:{_port}/exit", new StringContent(""), postCts.Token);
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
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
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
            Session = null;
            ModelName = null;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// App-uitgang: de server synchroon stoppen zodat er geen weestproces achterblijft.
    /// Blokkerend (max ~7 s: POST /exit 2 s + 5 s wachten + kill) op de aanroepende
    /// (UI-)thread: de afsluitflow van MAUI gaat niet door tot deze methode retour is,
    /// dus het kill haalt het vóór de app stopt. Gedurende deze methode worden
    /// StateChanged/LogReceived onderdrukt (via _shuttingDown), zodat er geen
    /// MainThread.BeginInvokeOnMainThread naar de al gedeactiveerde window gaat —
    /// die path gooit anders "Window was already deactivated" en brak de stop-sequence
    /// af vóór het kill.
    /// </summary>
    public void ShutdownServer()
    {
        // Concurrente unload/load is bezig: niet blokkeren op het lock (de UI-thread
        // zou anders een deadlock met de draaiende async-continuatie kunnen veroorzaken),
        // maar het proces direct (best-effort) neerhalen zodat er toch geen weestproces is.
        if (!_operationLock.Wait(0))
        {
            var running = _process;
            if (running is not null)
            {
                try
                {
                    running.Kill(entireProcessTree: true);
                    running.WaitForExit();
                }
                catch
                {
                    // best-effort
                }
            }

            return;
        }

        _shuttingDown = true;
        try
        {
            var process = _process;
            if (process is null || _state is not (LlamaServerState.Running or LlamaServerState.Starting))
            {
                return;
            }

            _state = LlamaServerState.Stopping;

            // 1) Best-effort POST /exit (2 s timeout; .Result = blokkerend, géén async
            //    await → géén continuatie die de afsluitflow kan missen)
            try
            {
                var host = string.IsNullOrWhiteSpace(_hostBind) || _hostBind == "0.0.0.0"
                    ? "127.0.0.1"
                    : _hostBind;
                using var postCts = new CancellationTokenSource(ExitPostFastTimeoutMs);
                _httpClient.PostAsync($"http://{host}:{_port}/exit", new StringContent(""), postCts.Token).GetAwaiter().GetResult();
            }
            catch
            {
                // negeren; fallback op proces-wachten/kill
            }

            // 2) Max 5 s wachten op proceseindiging
            bool exited;
            try
            {
                exited = process.WaitForExit(ExitShutdownWaitSeconds * 1000);
            }
            catch (Exception)
            {
                exited = false;
            }

            // 3) Nog niet gestopt → hard kill van het hele procesboom
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
                catch
                {
                    // laatste redmiddel; het proces verdwijnt met de app-afsluiting
                }
            }

            _lastExitCode = SafeExitCode(process);
            _state = LlamaServerState.Idle;
            _process = null;
            _session = null;
            _modelName = null;
        }
        finally
        {
            _shuttingDown = false;
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Markeert de server als Running zodra health-polling een 200 op /health ziet.
    /// </summary>
    public void MarkRunning()
    {
        // Tijdens app-uitgang niet naar Running flippen: een health-poller-tick kan
        // in de race lopen met ShutdownServer (process al gestopt of op het punt van stoppen).
        if (_shuttingDown)
        {
            return;
        }

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
            Session = null;
        }
    }

    private void SetState(LlamaServerState value)
    {
        if (_state == value)
        {
            return;
        }

        _state = value;

        // Bij app-uitgang de UI-events niet sturen: de listeners marshalen via
        // MainThread.BeginInvokeOnMainThread naar de al gedeactiveerde window
        // ("Window was already deactivated").
        if (_shuttingDown)
        {
            return;
        }

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

    private void RaiseLog(string line)
    {
        // Bij app-uitgang géén log-events (zelfde reden als SetState).
        if (_shuttingDown)
        {
            return;
        }

        LogReceived?.Invoke(this, new ServerLogEventArgs(line));
    }
}
