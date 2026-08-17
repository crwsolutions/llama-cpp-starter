using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Converters;

/// <summary>
/// Picker-item ↔ nullable string. Null wordt weergegeven als "(default)"
/// (eerste item in de optielijsten van het Startinstellingen-paneel).
/// </summary>
public class PickerDefaultConverter : IValueConverter
{
    public const string Placeholder = ProfileParameters.DefaultPlaceholder;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Placeholder : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && s == Placeholder ? null : value;
}
