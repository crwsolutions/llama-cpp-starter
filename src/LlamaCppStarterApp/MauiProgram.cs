using CommunityToolkit.Maui;
using LlamaCppStarterApp.Repositories;
using LlamaCppStarterApp.Services;
using LlamaCppStarterApp.Views;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;


namespace LlamaCppStarterApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("FontAwesome6FreeSolid.otf", "FontAwesomeSolid");
                fonts.AddFont("Consolas.ttf", "Consolas");
            })
            .AddViews();

#if DEBUG
		builder.Logging.AddDebug();
#endif

        // Register repositories
        builder.Services.AddSingleton<IModelRepository, ModelRepository>();
        builder.Services.AddSingleton<IProfileRepository, ProfileRepository>();
        builder.Services.AddSingleton<IRuntimeRepository, RuntimeRepository>();
        builder.Services.AddSingleton<IAppSettingsRepository, AppSettingsRepository>();

        // Register services
        builder.Services.AddSingleton<ModelScannerService>();
        builder.Services.AddSingleton<RuntimeScannerService>();
        builder.Services.AddSingleton<LlamaServerProcessService>();
        builder.Services.AddSingleton<ServerHealthService>();

        var app = builder.Build();
        Database.Initialize();
        return app;
    }
}
