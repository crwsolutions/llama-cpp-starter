namespace LlamaCppStarterApp.Converters;

/// <summary>null → false, otherwise → true (feeds IsVisible, which is a bool).</summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>bool? ↔ bool for CheckBox binding (null = unchecked).</summary>
public class BoolNullableConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true;
}

/// <summary>Non-empty string → true, null/empty → false (feeds IsVisible, which is a bool).</summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// bool → status color: true → StatusActive (green), false/null → StatusIdle (gray).
/// Feeds the running-indicator dot and the Modelstatus card accent.
/// Colors come from the Colors.xaml resources (merged into the app resources).
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Application.Current is non-null: the converter is only reached through XAML binding,
        // which exists only after the app has been built.
        var app = Application.Current!;
        return (Color)app.Resources[value is true ? "StatusActive" : "StatusIdle"]!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
