namespace LlamaCppStarterApp.Converters;

/// <summary>
/// Flyout-titel → FontAwesome6 Free Solid glyph (huis, map, code, tandenwiel).
/// Gebruikt in Shell.ItemTemplate omdat ShellContent.Icon alleen ImageSource accepteert.
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
