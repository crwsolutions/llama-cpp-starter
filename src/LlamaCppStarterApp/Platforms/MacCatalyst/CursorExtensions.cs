using LlamaCppStarterApp.Behaviors;
using Microsoft.Maui.Platform;

namespace LlamaCppStarterApp.Platforms;

/// <summary>
/// MacCatalyst cursor handling for CursorBehavior (ported from the reference project):
/// hover gesture recognizer swaps the pointer cursor on enter/exit.
/// </summary>
public static class CursorExtensions
{
    public static void SetCustomCursor(this VisualElement visualElement, CursorIcon cursor, IMauiContext? mauiContext)
    {
        ArgumentNullException.ThrowIfNull(mauiContext);
        var view = visualElement.ToPlatform(mauiContext);
        if (view.GestureRecognizers is not null)
        {
            foreach (var recognizer in view.GestureRecognizers.OfType<PointerUIHoverGestureRecognizer>())
            {
                view.RemoveGestureRecognizer(recognizer);
            }
        }

        view.AddGestureRecognizer(new PointerUIHoverGestureRecognizer(r =>
        {
            switch (r.State)
            {
                case UIKit.UIGestureRecognizerState.Began:
                    GetNSCursor(cursor).Set();
                    break;
                case UIKit.UIGestureRecognizerState.Ended:
                    AppKit.NSCursor.ArrowCursor.Set();
                    break;
            }
        }));
    }

    static AppKit.NSCursor GetNSCursor(CursorIcon cursor)
    {
        return cursor switch
        {
            CursorIcon.Hand => AppKit.NSCursor.OpenHandCursor,
            CursorIcon.IBeam => AppKit.NSCursor.IBeamCursor,
            CursorIcon.Cross => AppKit.NSCursor.CrosshairCursor,
            CursorIcon.Arrow => AppKit.NSCursor.ArrowCursor,
            CursorIcon.SizeAll => AppKit.NSCursor.ResizeUpCursor,
            CursorIcon.Wait => AppKit.NSCursor.OperationNotAllowedCursor,
            _ => AppKit.NSCursor.ArrowCursor,
        };
    }

    // PointerUIHoverGestureRecognizer is only available on .NET 9+ MacCatalyst.
#if NET9_0_OR_GREATER
    class PointerUIHoverGestureRecognizer(Action<UIKit.UIHoverGestureRecognizer> action) : UIKit.UIHoverGestureRecognizer(action);
#endif
}
