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

        // App-uitgang: draaiende llama-server unloaden zodat er geen weestproces achterblijft.
        _processService = Services.GetService<LlamaServerProcessService>();
        window.Destroying += OnWindowDestroying;

        return window;
    }

    private async void OnWindowDestroying(object? sender, EventArgs e)
    {
        try
        {
            if (_processService is not null)
            {
                await _processService.UnloadAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OnExit unload failed: {ex.Message}");
        }
    }
}
