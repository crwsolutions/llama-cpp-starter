using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Converters;

/// <summary>
/// Picker item ↔ nullable string. Null is displayed as "(default)"
/// (first item in the option lists of the Startinstellingen panel).
/// </summary>
public class PickerDefaultConverter : IValueConverter
{
    public const string Placeholder = ProfileParameters.DefaultPlaceholder;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Placeholder : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && s == Placeholder ? null : value;
}
