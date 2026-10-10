using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameTracker;

/// <summary>
/// A banner at the top centre of the screen naming what is still to get in the area you just walked into, so it is
/// noticed without reading the overlay. It stays while there is something to get here, and fades out once it was
/// picked up or you left the area; clicks go through to the game and it never takes focus.
/// </summary>
public sealed class ToastWindow : Window
{
    static readonly Brush Mako = Brush("#5EEAD4"), Danger = Brush("#F87171"), Muted = Brush("#94A3B8");
    readonly TextBlock _text = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 900 };

    public ToastWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Bahnschrift");
        Opacity = 0;
        Content = new Border
        {
            Child = _text,
            Background = Brush("#D90A1220"),
            BorderBrush = Mako,
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(28, 12, 28, 14),
        };
        SourceInitialized += (_, _) => new Native(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)).SetClickThrough(true);
        SizeChanged += (_, _) => Place();
    }

    /// <summary>
    /// Shows the steps of the area: the area name small, the steps large, missables in red with a tag. Fades in when
    /// <paramref name="fadeIn"/> (something new to see); otherwise only the text changes (a step was ticked).
    /// </summary>
    public void Show(string area, IReadOnlyList<(string Name, bool Missable)> steps, bool fadeIn)
    {
        _text.Inlines.Clear();
        _text.Inlines.Add(new System.Windows.Documents.Run(Lang.T("◆ IN THIS AREA · ", "◆ DI AREA INI · ") + area.ToUpperInvariant() + "\n") { Foreground = Mako, FontSize = 14, FontWeight = FontWeights.SemiBold });
        for (int i = 0; i < steps.Count; i++)
        {
            if (i > 0) _text.Inlines.Add(new System.Windows.Documents.Run("   ·   ") { Foreground = Muted, FontSize = 24 });
            _text.Inlines.Add(new System.Windows.Documents.Run(steps[i].Name) { Foreground = steps[i].Missable ? Danger : Brushes.White, FontSize = 26, FontWeight = FontWeights.SemiBold });
            if (steps[i].Missable) _text.Inlines.Add(new System.Windows.Documents.Run(" !") { Foreground = Danger, FontSize = 16, FontWeight = FontWeights.Black });
        }
        if (!IsVisible) Show();
        Place();
        if (fadeIn || _fadingOut)
        {
            _fadingOut = false;
            BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(300)));
        }
    }

    bool _fadingOut;

    /// <summary>Fades the banner out: nothing left to get in this area, or you left it.</summary>
    public void FadeOut()
    {
        if (!IsVisible || _fadingOut) return;
        _fadingOut = true;
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(600)));
    }

    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + area.Height * 0.08;
    }

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
