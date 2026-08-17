namespace LlamaCppStarterApp.Views;

internal static class AddViewsExtension
{
    internal static MauiAppBuilder AddViews(this MauiAppBuilder builder)
    {
        // Register views and view models
        // (VM's als singleton zodat selecties overleven bij navigeren)
        builder.Services.AddSingleton<OverviewViewModel>();
        builder.Services.AddSingleton<ModelsViewModel>();
        builder.Services.AddSingleton<RuntimesViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();


        return builder;
    }
}
