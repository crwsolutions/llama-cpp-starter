using CommunityToolkit.Maui;
using LlamaCppStarterApp.Repositories;
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
            })
            .AddViews();

#if DEBUG
		builder.Logging.AddDebug();
#endif

        // Register repositories
        builder.Services.AddSingleton<IPromptRepository, PromptRepository>();

        var app = builder.Build();
        Database.Initialize();
        return app;
    }
}
