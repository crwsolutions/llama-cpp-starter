using LlamaCppStarterApp.Behaviors;
using Microsoft.Maui.ApplicationModel;

namespace LlamaCppStarterApp.Controls;

/// <summary>
/// Icon button that copies <see cref="Text"/> to the clipboard. Shows the FontAwesome
/// copy glyph (f0c5) by default and a green check (f00c) for a few seconds after a
/// successful copy, then reverts to the copy glyph.
/// </summary>
public partial class CopyIconButton : ContentView
{
    // How long the green check stays visible after a successful copy.
    private const int RevertTicks = 4000;

    private CancellationTokenSource? _copiedCts;

    public CopyIconButton()
    {
        InitializeComponent();
        CopyButton.Clicked += OnCopied;
    }

    /// <summary>Text to copy to the clipboard. Re-read on copy, so a computed
    /// binding (e.g. a command preview) always yields the current value.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(CopyIconButton), default(string));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Icon size in device-independent units.</summary>
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(CopyIconButton), 14d);

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>True while the green check is shown (a successful copy happened
    /// within the revert window). Hides the copy button while true so nothing
    /// shows through the check.</summary>
    public static readonly BindableProperty IsCopiedProperty = BindableProperty.Create(
        nameof(IsCopied), typeof(bool), typeof(CopyIconButton), default(bool),
        propertyChanged: OnCheckIsCopiedChanged);

    public bool IsCopied
    {
        get => (bool)GetValue(IsCopiedProperty);
        set => SetValue(IsCopiedProperty, value);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        ApplyCursor();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        ApplyCursor();
    }

    private static void OnCheckIsCopiedChanged(BindableObject sender, object oldValue, object newValue)
    {
        var control = (CopyIconButton)sender;
        control.CopyButton.IsVisible = !control.IsCopied;
    }

    /// <summary>
    /// The hand cursor is only active while the element is in the visual tree (the
    /// CursorBehavior hooks into the handler/platform view), so it is (re)applied
    /// whenever the control joins a page.
    /// </summary>
    private void ApplyCursor()
    {
        CursorBehavior.SetCursor(this, CursorIcon.Hand);
    }

    private async void OnCopied(object? sender, EventArgs e)
    {
        if (IsCopied)
        {
            // Already showing the check: extend the sequence instead of flashing
            // the copy glyph back.
            _copiedCts?.Cancel();
            ShowCopiedState();
            return;
        }

        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            // Nothing to copy: keep the copy glyph.
            return;
        }

        try
        {
            // The copy runs on a background task; it only needs to have started
            // before the "copied" state is shown (a slow copy must not delay the
            // feedback).
            await Clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
            // Clipboard write failed (transient OS error): keep the copy glyph.
            return;
        }

        ShowCopiedState();
    }

    private async void ShowCopiedState()
    {
        _copiedCts?.Cancel();
        _copiedCts = new CancellationTokenSource();
        var token = _copiedCts.Token;
        IsCopied = true;
        try
        {
            await Task.Delay(RevertTicks, token);
        }
        catch (OperationCanceledException)
        {
            // A newer copy started (or the control was recycled): do nothing.
            return;
        }

        if (!_copiedCts.IsCancellationRequested)
        {
            IsCopied = false;
        }
    }
}
