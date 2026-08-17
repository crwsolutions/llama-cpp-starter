namespace LlamaCppStarterApp.Views;

public partial class ModelsPage : ContentPage
{
    private readonly ModelsViewModel _viewModel;

    public ModelsPage(ModelsViewModel viewModel)
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
