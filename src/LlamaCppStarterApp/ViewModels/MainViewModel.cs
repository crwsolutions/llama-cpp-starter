using LlamaCppStarterApp.Models;
using LlamaCppStarterApp.Repositories;

namespace LlamaCppStarterApp.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly IPromptRepository _promptRepository;
    private bool _isLoading = true;
    private int _count = 0;

    public MainViewModel(IPromptRepository promptRepository)
    {
        _promptRepository = promptRepository;
        Title = "Llama.cpp - Starter";
    }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Not connected";

    [ObservableProperty]
    public partial string PromptText { get; set; } = string.Empty;

    partial void OnPromptTextChanged(string value)
    {
        if (_isLoading) return;
        StatusText = string.IsNullOrWhiteSpace(value) ? "Not connected" : "Ready to send";
    }

    [ObservableProperty]
    public partial string CounterText { get; set; } = "Click me";

    public ObservableCollection<PromptEntryViewModel> Prompts { get; } = [];

    internal async Task LoadDataAsync()
    {
        try
        {
            _isLoading = true;
            IsBusy = true;

            var last = await _promptRepository.GetLastAsync();
            if (last is not null)
            {
                Prompts.Add(new PromptEntryViewModel(last));
                PromptText = last.Prompt;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading data: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClickCounter()
    {
        _count++;
        CounterText = _count == 1 ? "Clicked 1 time" : $"Clicked {_count} times";
        SemanticScreenReader.Announce(CounterText);
    }

    [RelayCommand]
    private async Task SendPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(PromptText)) return;

        try
        {
            IsBusy = true;
            StatusText = "Sending prompt...";

            await _promptRepository.UpsertAsync(PromptText.Trim());

            // TODO: Send the prompt to the llama.cpp server here.

            StatusText = "Sent";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class PromptEntryViewModel : ObservableObject
{
    public PromptEntryViewModel(PromptEntry entry)
    {
        Id = entry.Id;
        Prompt = entry.Prompt;
    }

    public int Id { get; }

    [ObservableProperty]
    public partial string Prompt { get; set; } = string.Empty;
}
