using LlamaCppStarterApp.Repositories;

namespace LlamaCppStarterApp;

public partial class AppShell : Shell
{
    public const string FlyoutMinimizedSettingKey = "FlyoutMinimized";
    public const double FlyoutMinimizedWidth = 60;
    private const double FlyoutDefaultWidth = 152;

    private readonly IAppSettingsRepository _settings;
    private double? _flyoutWidth;

    public AppShell(IAppSettingsRepository settings)
    {
        _settings = settings;
        InitializeComponent();

        var minimized = _settings.GetValueAsync(FlyoutMinimizedSettingKey).GetAwaiter().GetResult() == "true";
        if (minimized)
        {
            FlyoutWidth = FlyoutMinimizedWidth;
            Expander.Text = "\uf152"; // angle-right glyph (collapsed)
        }
        else
        {
            FlyoutWidth = FlyoutDefaultWidth;
            Expander.Text = "\uf191"; // angle-left glyph (expanded)
        }
        _flyoutWidth = FlyoutDefaultWidth;
    }

    private void TapGestureRecognizer_Tapped(object sender, TappedEventArgs e)
    {
        _flyoutWidth ??= FlyoutWidth;

        if (FlyoutWidth > FlyoutMinimizedWidth)
        {
            Animate(_flyoutWidth.Value, FlyoutMinimizedWidth, "\uf152");
        }
        else
        {
            Animate(FlyoutMinimizedWidth, _flyoutWidth.Value, "\uf191");
        }
    }

    private void Animate(double a, double b, string c)
    {
        var animation = new Animation(v => FlyoutWidth = v, a, b);
        animation.Commit(this, "FlyoutWidthAnimation", easing: Easing.CubicInOut, finished: (_, _) =>
        {
            Expander.Text = c;

            _ = _settings.SetAsync(FlyoutMinimizedSettingKey, b <= FlyoutMinimizedWidth ? "true" : "false"); //Save state
        });
    }
}
