namespace LlamaCppStarterApp.Views;

public partial class RuntimesPage : ContentPage
{
    private readonly RuntimesViewModel _viewModel;

    public RuntimesPage(RuntimesViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        await _viewModel.LoadAsync();
    }
}
