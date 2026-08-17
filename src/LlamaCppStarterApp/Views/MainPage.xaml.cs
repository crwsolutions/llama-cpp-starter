namespace LlamaCppStarterApp.Views;

public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel viewModel)
    {
        BindingContext = viewModel;
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        if (args.NavigationType == NavigationType.Replace && BindingContext is MainViewModel vm)
        {
            Debug.WriteLine("Start Loading data");
            await vm.LoadDataAsync();

            Debug.WriteLine("Loaded data");
        }
    }
}
