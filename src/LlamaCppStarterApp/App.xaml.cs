using LlamaCppStarterApp.Services;

namespace LlamaCppStarterApp;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    private LlamaServerProcessService? _processService;

    public App(IServiceProvider serviceProvider)
    {
        Services = serviceProvider;

        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

        // App exit: stop a running llama-server synchronously so no orphan
        // process is left behind (see LlamaServerProcessService.ShutdownServer).
        _processService = Services.GetService<LlamaServerProcessService>();
        window.Destroying += OnWindowDestroying;

        return window;
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        try
        {
            _processService?.ShutdownServer();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OnExit unload failed: {ex.Message}");
        }
    }
}
