namespace LlamaCppStarterApp.Views;

internal static class AddViewsExtension
{
    internal static MauiAppBuilder AddViews(this MauiAppBuilder builder)
    {
        // Register views and view models
        // (VMs as singletons so selections survive navigation)
        builder.Services.AddSingleton<OverviewViewModel>();
        builder.Services.AddSingleton<ModelsViewModel>();
        builder.Services.AddSingleton<RuntimesViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();


        return builder;
    }
}
