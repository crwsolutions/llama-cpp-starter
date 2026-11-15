namespace LlamaCppStarterApp.Converters;

/// <summary>
/// Flyout title → FontAwesome6 Free Solid glyphs.
/// Used in Shell.ItemTemplate because ShellContent.Icon only accepts ImageSource.
/// </summary>
public class TitleToGlyphConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Overview"] = "\uf015",   // house
        ["Models"] = "\uf07b",     // folder
        ["Runtimes"] = "\uf2db",   // processor
        ["Benchmark"] = "\uf0e7",  // bolt
        ["Settings"] = "\uf013"    // gear
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string title && Glyphs.TryGetValue(title, out var glyph) ? glyph : "\uf015";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
