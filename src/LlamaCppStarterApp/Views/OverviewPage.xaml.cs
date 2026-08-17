namespace LlamaCppStarterApp.Views;

public partial class OverviewPage : ContentPage
{
    private readonly OverviewViewModel _viewModel;

    public OverviewPage(OverviewViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        await _viewModel.EnsureLoadedAsync();
    }
}
