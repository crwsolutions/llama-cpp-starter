namespace LlamaCppStarterApp.Controls;

public partial class SectionTitleView : ContentView
{
	public SectionTitleView()
	{
		InitializeComponent();
	}

    static readonly Color PrimaryColor = Color.FromRgb(0x51, 0x2B, 0xD4); // #512BD4, the "Primary" resource

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(SectionTitleView), string.Empty);

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(SectionTitleView), PrimaryColor);


    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }
}