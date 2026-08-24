using LlamaCppStarterApp.Platforms;

namespace LlamaCppStarterApp.Behaviors;

/// <summary>
/// Attached behavior: sets the pointer cursor while hovering the element, e.g.
/// behaviors:CursorBehavior.Cursor="Hand" (ported from the reference project).
/// The platform-specific cursor handling lives in the CursorExtensions classes.
/// </summary>
public class CursorBehavior
{
    public static readonly BindableProperty CursorProperty = BindableProperty.CreateAttached(
        "Cursor",
        typeof(CursorIcon),
        typeof(CursorBehavior),
        CursorIcon.Arrow,
        propertyChanged: CursorChanged);

    private static void CursorChanged(BindableObject bindable, object oldvalue, object newvalue)
    {
        if (bindable is VisualElement visualElement)
        {
            // The attached property is set during XAML inflation, before the element is
            // connected to a window (visualElement.Window is null at that point), so the
            // single app window's MauiContext is used (Application.MainPage is deprecated
            // in .NET 10, but still the only way to reach the context at inflation time).
#pragma warning disable CS0618 // Application.MainPage.get is obsolete
            visualElement.SetCustomCursor((CursorIcon)newvalue, Application.Current?.MainPage?.Handler?.MauiContext);
#pragma warning restore CS0618
        }
    }

    public static CursorIcon GetCursor(BindableObject view) => (CursorIcon)view.GetValue(CursorProperty);

    public static void SetCursor(BindableObject view, CursorIcon value) => view.SetValue(CursorProperty, value);
}
