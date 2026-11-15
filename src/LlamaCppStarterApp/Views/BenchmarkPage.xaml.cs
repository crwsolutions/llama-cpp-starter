namespace LlamaCppStarterApp.Views;

public partial class BenchmarkPage : ContentPage
{
    private readonly BenchmarkViewModel _viewModel;

    public BenchmarkPage(BenchmarkViewModel viewModel)
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

    private async void Runs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        await _viewModel.SelectRunAsync(e.CurrentSelection.FirstOrDefault() as Models.BenchmarkHistoryItem);
    }

    private void CompareRun_CheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (sender is CheckBox { BindingContext: Models.BenchmarkHistoryItem item })
        {
            _viewModel.SetComparisonRun(item, e.Value);
        }
    }

    private async void DeleteRun_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Models.BenchmarkHistoryItem item })
        {
            return;
        }

        var confirmed = await DisplayAlertAsync("Delete benchmark run", $"Delete the run from {item.DateText}? Its measurements will also be deleted.", "Delete", "Cancel");
        if (confirmed)
        {
            await _viewModel.DeleteRunAsync(item);
        }
    }
}
