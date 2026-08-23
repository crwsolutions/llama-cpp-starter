using System.Diagnostics;
using System.Runtime.InteropServices;
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
/// Session info of the (last) loaded server: which runtime/model/profile-parameter combination
/// is running in which process. Null = no loaded server. Used by the Overview cards
/// (nvidia-smi PID match, /metrics toggle, configured slots).
/// </summary>
public sealed record LoadedSession(Runtime Runtime, Model Model, int LoadedProfileId, ProfileParameters Parameters, int Port, int ProcessId);

/// <summary>
/// Manages a single "current server" (llama-server.exe process).
/// Unload: Ctrl+C (console CTRL_C_EVENT via AttachConsole) → wait max 30 s → Kill(entireProcessTree).
/// App exit: ShutdownServer() — synchronous, short (Ctrl+C → 5 s → kill)
/// and without UI events, so the kill still runs even if the window is already deactivated.
/// The server (console-subsystem exe) starts with CreateNoWindow = true: no visible
/// console window, but it still has its own (hidden) console, so a real Ctrl+C can be
/// delivered and llama.cpp's console handler performs a graceful stop.
/// </summary>
public class LlamaServerProcessService
{
    private const int UnloadWaitSeconds = 30;
    private const int ExitShutdownWaitSeconds = 5;

    private readonly SemaphoreSlim _operationLock = new(1, 1);

#if WINDOWS
    // One console can be attached by a process at a time; this guards the
    // process-wide attach/detach state so a second unload/shutdown cannot attach
    // while the first one is still attached.
    private static readonly object _consoleLock = new();
    private bool _consoleAttached;
#endif

    // true during app exit (Shutdown): StateChanged/LogReceived are then
    // suppressed, so the stop sequence never touches the UI dispatcher and the kill
    // completes even if the window is already deactivated/disposed (no orphan process).
    private volatile bool _shuttingDown;

    private Process? _process;
    private LlamaServerState _state = LlamaServerState.Idle;
    private int _port;
    private string? _modelName;
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
    /// Info about the loaded session (null = no server loaded). Updated on
    /// LoadAsync and cleared on UnloadAsync as well as on natural process exit (CheckAlive/wait loop).
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

    /// <summary>
    /// True while ShutdownServer() is running (app exit). Background pollers
    /// (e.g. the Overview hardware timer) must stop touching the UI dispatcher
    /// in that window — the window is already deactivated.
    /// </summary>
    public bool IsShuttingDown => _shuttingDown;

    /// <summary>Log line from stdout/stderr (stderr gets a "[stderr] " prefix).</summary>
    public event EventHandler<ServerLogEventArgs>? LogReceived;

    /// <summary>State change (Idle/Starting/Running/Stopping).</summary>
    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    public async Task<bool> LoadAsync(Runtime runtime, Model model, int profileId, ProfileParameters parameters, int port)
    {
        if (!await _operationLock.WaitAsync(0))
        {
            return false; // already busy with another operation
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

            // --spec-draft-model resolution at load time (pure static; embedded MTP → null).
            var draftModelPath = ModelCompanionService.ResolveDraftModelPath(model.Path, parameters.SpecType, parameters.SpecDraftPath);
            var args = LlamaServerCommandBuilder.BuildArgs(runtime, model, parameters, port, draftModelPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = runtime.ExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // No console window, but the (console-subsystem) server still gets its
                // own hidden console — required so a real Ctrl+C can be delivered on unload.
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
            Session = new LoadedSession(runtime, model, profileId, parameters, port, process.Id);
            State = LlamaServerState.Starting;

            RaiseLog($"Opstarten: {LlamaServerCommandBuilder.BuildCommandLine(args)}");

            // Loading the model takes a long time; the loop keeps running until the process exits.
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
                    // process already closed
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
    /// Unload the current server: send Ctrl+C (console CTRL_C_EVENT), wait max 30 s,
    /// then Kill(entireProcessTree).
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

            // 1) Best-effort Ctrl+C (llama.cpp's console handler performs the graceful
            //    stop); if the console cannot be reached, fall back to waiting/kill below
            if (TrySendCtrlC(process))
            {
                RaiseLog($"Ctrl+C gestuurd (PID {process.Id}).");
            }
            else
            {
                RaiseLog($"[stderr] Kon geen Ctrl+C sturen (PID {process.Id}); stop via kill na timeout.");
            }

            // 2) Wait max 30 s for the process to exit
            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(UnloadWaitSeconds));
            try
            {
                await process.WaitForExitAsync(exitCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 3) Timeout → hard kill of the whole process tree
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
            // Detach after the wait so a pending Ctrl+C cannot reach our own app.
            DetachConsole();
            _operationLock.Release();
        }
    }

    /// <summary>
    /// App exit: stop the server synchronously so no orphan process is left behind.
    /// Blocking (max ~5 s: Ctrl-C signal + 5 s wait + kill) on the calling
    /// (UI) thread: MAUI's shutdown flow does not continue until this method returns,
    /// so the kill lands before the app exits. During this method
    /// StateChanged/LogReceived are suppressed (via _shuttingDown), so no
    /// MainThread.BeginInvokeOnMainThread goes to the already deactivated window —
    /// that path would otherwise throw "Window was already deactivated" and break the
    /// stop sequence before the kill.
    /// </summary>
    public void ShutdownServer()
    {
        // A concurrent unload/load is in progress: don't block on the lock (the UI thread
        // could otherwise deadlock with the running async continuation),
        // but bring the process down directly (best-effort) so no orphan process remains.
        if (!_operationLock.Wait(0))
        {
            var running = _process;
            if (running is not null)
            {
                try
                {
                    // Best-effort Ctrl-C, short wait, then hard kill.
                    TrySendCtrlC(running);
                    if (!running.WaitForExit(2000))
                    {
                        running.Kill(entireProcessTree: true);
                    }
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

            // 1) Best-effort Ctrl+C (synchronous; llama.cpp's console handler performs
            //    the graceful stop); if the console cannot be reached, fall back to
            //    waiting/kill below
            TrySendCtrlC(process);

            // 2) Wait max 5 s for the process to exit
            bool exited;
            try
            {
                exited = process.WaitForExit(ExitShutdownWaitSeconds * 1000);
            }
            catch (Exception)
            {
                exited = false;
            }

            // 3) Not stopped yet → hard kill of the whole process tree
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
                catch
                {
                    // last resort; the process disappears with the app shutdown
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
            // Detach after the wait so a pending Ctrl+C cannot reach our own app.
            DetachConsole();
            _shuttingDown = false;
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Marks the server as Running once health polling sees a 200 on /health.
    /// </summary>
    public void MarkRunning()
    {
        // Do not flip to Running during app exit: a health poller tick can
        // race with ShutdownServer (process already stopped or about to stop).
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
    /// If the process has already stopped but the state does not reflect that yet, update it.
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

        // During app exit, do not fire UI events: the listeners marshal via
        // MainThread.BeginInvokeOnMainThread to the already deactivated window
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
        // No log events during app exit (same reason as SetState).
        if (_shuttingDown)
        {
            return;
        }

        LogReceived?.Invoke(this, new ServerLogEventArgs(line));
    }

    /// <summary>
    /// Sends a real Ctrl+C (CTRL_C_EVENT) to the server's console (Windows: attach
    /// to the server's console, ignore the event for ourselves and generate it for
    /// console process group 0; llama.cpp's own console handler then performs the
    /// graceful stop). Returns false when the console cannot be reached (server
    /// already gone, non-Windows, …); callers fall back to the wait/kill path.
    /// Must be paired with DetachConsole() AFTER the wait for exit — detaching
    /// earlier can deliver the pending Ctrl+C to our own process.
    /// </summary>
    private bool TrySendCtrlC(Process process)
    {
#if WINDOWS
        lock (_consoleLock)
        {
            try
            {
                if (!AttachConsole((uint)process.Id))
                {
                    return false;
                }

                _consoleAttached = true;
                // Stop this process from handling the Ctrl+C that is about to be sent.
                SetConsoleCtrlHandler(null, true);
                // 0 = the console process group we are now attached to (the server).
                return GenerateConsoleCtrlEvent(CtrlCEVENT, 0);
            }
            catch
            {
                // best-effort; fall back to the wait/kill path
                return false;
            }
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// Restores our own Ctrl+C handling and detaches from the server's console.
    /// Must be called after the wait for the server to exit (see TrySendCtrlC).
    /// </summary>
    private void DetachConsole()
    {
#if WINDOWS
        lock (_consoleLock)
        {
            if (!_consoleAttached)
            {
                return;
            }

            _consoleAttached = false;
            try
            {
                SetConsoleCtrlHandler(null, false);
                FreeConsole();
            }
            catch
            {
                // best-effort; nothing else to do
            }
        }
#endif
    }

#if WINDOWS
    private const uint CtrlCEVENT = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handlerRoutine, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    private delegate bool ConsoleCtrlDelegate(uint ctrlType);
#endif
}
