namespace LlamaCppStarterApp.Converters;

/// <summary>
/// Flyout title → FontAwesome6 Free Solid glyph (home, folder, code, gear).
/// Used in Shell.ItemTemplate because ShellContent.Icon only accepts ImageSource.
/// </summary>
public class TitleToGlyphConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Overzicht"] = "\uf015",   // house
        ["Modellen"] = "\uf07b",    // folder
        ["Runtimes"] = "\uf2db",    // processor
        ["Instellingen"] = "\uf013" // gear
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string title && Glyphs.TryGetValue(title, out var glyph) ? glyph : "\uf015";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
