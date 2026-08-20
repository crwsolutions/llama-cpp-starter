using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.UI.Xaml.Controls;

namespace LlamaCppStarterApp;

public class CustomCollectionViewHandler : CollectionViewHandler
{
    protected override ListViewBase CreatePlatformView()
    {
        var platformView = base.CreatePlatformView();
        platformView.ItemContainerTransitions = null;   // no more fade/insert animations
        return platformView;
    }
}
