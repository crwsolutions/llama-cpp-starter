using LlamaCppStarterApp.Repositories;

namespace LlamaCppStarterApp.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private const string HelloExePath = @"E:\temp\hello.exe";
    private readonly IPromptRepository _promptRepository;
    private readonly SemaphoreSlim _processLock = new(1, 1);

    public MainViewModel(IPromptRepository promptRepository)
    {
        _promptRepository = promptRepository;
        Title = "Llama.cpp - Starter";
    }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Not connected";

    [ObservableProperty]
    public partial string ProcessOutput { get; set; } = string.Empty;

    private void AppendOutput(string line)
    {
        // Marshal process events to the main UI thread.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProcessOutput = string.IsNullOrEmpty(ProcessOutput) ? line : ProcessOutput + Environment.NewLine + line;
        });
    }

    internal async Task LoadDataAsync()
    {
        try
        {
            IsBusy = true;

            var last = await _promptRepository.GetLastAsync();
            if (last is not null)
            {
                StatusText = $"Last prompt: {last.Prompt}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading data: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunHelloAsync()
    {
        if (!File.Exists(HelloExePath))
        {
            StatusText = $"Not found: {HelloExePath}";
            ProcessOutput = $"File not found: {HelloExePath}";
            return;
        }

        // Only one process at a time.
        if (!await _processLock.WaitAsync(0))
        {
            StatusText = "Already running...";
            return;
        }

        Process? process = null;
        try
        {
            IsBusy = true;
            ProcessOutput = string.Empty;
            StatusText = "Running...";

            var startInfo = new ProcessStartInfo
            {
                FileName = HelloExePath,
                WorkingDirectory = Path.GetDirectoryName(HelloExePath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process = new Process { StartInfo = startInfo };

            // Append each line as it arrives.
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    AppendOutput(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    AppendOutput($"[stderr] {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            StatusText = $"Finished (exit code {process.ExitCode})";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            ProcessOutput = string.IsNullOrEmpty(ProcessOutput)
                ? ex.Message
                : ProcessOutput + Environment.NewLine + ex.Message;
        }
        finally
        {
            IsBusy = false;
            process?.Dispose();
            _processLock.Release();
        }
    }
}
