using LlamaCppStarterApp.Models;

namespace LlamaCppStarterApp.Converters;

/// <summary>
/// Picker label <-> MmprojMode for the Vision pane (Startinstellingen).
/// The labels double as the picker option list (ModelsViewModel.MmprojModeOptions),
/// so the converted value must equal the item text for the picker to display it.
/// </summary>
public class MmprojModePickerConverter : IValueConverter
{
    public const string Auto = "Aan (auto)";
    public const string Off = "Uit";
    public const string Custom = "Andere (bladeren)";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            MmprojMode.Auto => Auto,
            MmprojMode.Off => Off,
            _ => Custom
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var label = value as string;
        return label switch
        {
            Auto => MmprojMode.Auto,
            Off => MmprojMode.Off,
            _ => MmprojMode.Custom
        };
    }
}

/// <summary>MmprojMode -> visibility: only "Andere (bladeren)" shows the path row (entry + browse button).</summary>
public class MmprojModeCustomToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MmprojMode.Custom;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
