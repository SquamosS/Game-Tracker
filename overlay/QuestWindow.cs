using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameTracker;

/// <summary>
/// The live quest at the top left of the screen, apart from the checklist: the quest (blue) with its description,
/// then the active sub-quest (amber) with its own. It stays up while you explore and fades in again when the quest
/// or sub-quest changes; clicks go through to the game and it never takes focus.
/// </summary>
public sealed class QuestWindow : Window
{
    static readonly Brush QuestTitle = Brush("#38BDF8"), Text = Brush("#BAE6FD"), SubTitle = Brush("#FBBF24"), SubText = Brush("#E2E8F0");
    readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 460, Effect = Outline.Create() };
    string _shown = "";
    bool _hasQuest, _allowed = true;

    public QuestWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Bahnschrift");
        Content = new Border
        {
            Child = _text,
            Background = Brush("#990A1220"),
            BorderBrush = QuestTitle,
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 18, 10),
        };
        SourceInitialized += (_, _) => new Native(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)).SetClickThrough(true);
        SizeChanged += (_, _) => Place();
    }

    /// <summary>The quest to show (null: none known, the window hides). Redrawn and faded in only when it changed.</summary>
    public void SetQuest(string? title, string? text, string? subTitle, string? subText)
    {
        _hasQuest = title is not null;
        string key = $"{title}\n{text}\n{subTitle}\n{subText}";
        bool changed = key != _shown;
        _shown = key;
        if (changed && _hasQuest)
        {
            _text.Inlines.Clear();
            _text.Inlines.Add(new System.Windows.Documents.Run(title) { Foreground = QuestTitle, FontSize = 20, FontWeight = FontWeights.SemiBold });
            if (text is { Length: > 0 }) _text.Inlines.Add(new System.Windows.Documents.Run("\n" + text) { Foreground = Text, FontSize = 14 });
            if (subTitle is not null)
            {
                _text.Inlines.Add(new System.Windows.Documents.Run("\n› " + subTitle) { Foreground = SubTitle, FontSize = 16, FontWeight = FontWeights.SemiBold });
                if (subText is { Length: > 0 }) _text.Inlines.Add(new System.Windows.Documents.Run("\n   " + subText) { Foreground = SubText, FontSize = 13.5, FontStyle = FontStyles.Italic });
            }
        }
        Update();
        if (changed && IsVisible) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
    }

    /// <summary>False while the overlay is hidden (menus, battles, cutscenes, Ctrl+Shift+G).</summary>
    public bool Allowed
    {
        set { _allowed = value; Update(); }
    }

    void Update()
    {
        bool show = _hasQuest && _allowed;
        if (show && !IsVisible) { Show(); Place(); }
        else if (!show && IsVisible) Hide();
    }

    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + 24;
        Top = area.Top + area.Height * 0.08;
    }

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

/// <summary>A thin dark halo around text, so it reads over bright scenes too (WPF has no text outline of its own).</summary>
public static class Outline
{
    public static System.Windows.Media.Effects.DropShadowEffect Create()
    {
        var effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 4, Opacity = 1 };
        effect.Freeze();
        return effect;
    }
}
