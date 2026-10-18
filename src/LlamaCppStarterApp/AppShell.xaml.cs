
namespace LlamaCppStarterApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
    }

    private double? _flyoutWidth;

    private void TapGestureRecognizer_Tapped(object sender, TappedEventArgs e)
    {
        const int Minimized = 60;
        _flyoutWidth ??= FlyoutWidth;

        if (FlyoutWidth > Minimized)
        {
            Animate(_flyoutWidth.Value, Minimized, "\uf152");
        }
        else
        {
            Animate(Minimized, _flyoutWidth.Value, "\uf191");
        }
    }

    private void Animate(double a, double b, string c)
    {
        var animation = new Animation(v => FlyoutWidth = v, a, b);
        animation.Commit(this, "FlyoutWidthAnimation", easing: Easing.CubicInOut, finished: (_, _) =>
        {
            Expander.Text = c;
        });
    }
}
