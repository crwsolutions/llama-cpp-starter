namespace LlamaCppStarterApp.Converters;

/// <summary>long bytes → "18,7 GB" (Nederlandse cultuur).</summary>
public class SizeToGbConverter : IValueConverter
{
    private static readonly CultureInfo NlCulture = new("nl-NL");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            var gb = bytes / (1024.0 * 1024.0 * 1024.0);
            return $"{gb:0.#} GB";
        }

        return "0 GB";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
