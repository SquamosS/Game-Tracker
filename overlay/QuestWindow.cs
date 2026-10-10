using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameTracker;

/// <summary>
/// A small panel in a corner of the screen, apart from the checklist. It shows while the overlay does (Allowed) and
/// has something to show, fades in again when its content changes (also when that happened while hidden); clicks go
/// through to the game and it never takes focus.
/// </summary>
public abstract class CornerWindow : Window
{
    protected readonly TextBlock Text = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
    string _shown = "";
    /// <summary>Off until the overlay itself shows (MainWindow.IsVisibleChanged).</summary>
    bool _hasContent, _allowed, _pendingFade, _closed;

    protected CornerWindow(Brush accent, Thickness accentEdge)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Bahnschrift");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;
        Content = new Border
        {
            Child = Text,
            Background = Brush("#E60A1220"),
            BorderBrush = accent,
            BorderThickness = accentEdge,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 18, 10),
        };
        SourceInitialized += (_, _) => new Native(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)).SetClickThrough(true);
        Closed += (_, _) => _closed = true;
        SizeChanged += (_, _) => Place();
        Place();
    }

    /// <summary>
    /// Shows content identified by <paramref name="key"/> (null: nothing, the window hides); <paramref name="draw"/>
    /// fills Text only when the key changed.
    /// </summary>
    protected void SetContent(string? key, Action draw)
    {
        _hasContent = key is not null;
        bool changed = key is not null && key != _shown;
        _shown = key ?? "";
        if (changed)
        {
            Text.Inlines.Clear();
            draw();
            _pendingFade = true;
        }
        Update();
    }

    /// <summary>False while the overlay is hidden (menus, battles, cutscenes, Ctrl+Shift+G).</summary>
    public bool Allowed
    {
        set { _allowed = value; Update(); }
    }

    void Update()
    {
        // A queued guide reload can still render after the overlay closed: a closed window cannot show again.
        if (_closed) return;
        bool show = _hasContent && _allowed;
        if (show && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
        if (show && _pendingFade)
        {
            _pendingFade = false;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
        }
    }

    /// <summary>Sets Left and Top in the work area (ActualWidth/Height may still be 0 before the first layout).</summary>
    protected abstract void Place();

    protected static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

/// <summary>
/// The live quest at the top left of the screen: the quest (blue) with its description, then the active sub-quest
/// (amber) with its own.
/// </summary>
public sealed class QuestWindow() : CornerWindow(QuestTitle, new Thickness(3, 0, 0, 0))
{
    static readonly Brush QuestTitle = Brush("#38BDF8"), QuestText = Brush("#BAE6FD"), SubTitle = Brush("#FBBF24"), SubText = Brush("#E2E8F0");

    /// <summary>The quest to show (null: none known, the window hides).</summary>
    public void SetQuest(string? title, string? text, string? subTitle, string? subText) =>
        SetContent(title is null ? null : $"{title}\n{text}\n{subTitle}\n{subText}", () =>
        {
            Text.Inlines.Add(new System.Windows.Documents.Run(title) { Foreground = QuestTitle, FontSize = 20, FontWeight = FontWeights.SemiBold });
            if (text is { Length: > 0 }) Text.Inlines.Add(new System.Windows.Documents.Run("\n" + text) { Foreground = QuestText, FontSize = 14 });
            if (subTitle is null) return;
            Text.Inlines.Add(new System.Windows.Documents.Run("\n› " + subTitle) { Foreground = SubTitle, FontSize = 16, FontWeight = FontWeights.SemiBold });
            if (subText is { Length: > 0 }) Text.Inlines.Add(new System.Windows.Documents.Run("\n   " + subText) { Foreground = SubText, FontSize = 13.5, FontStyle = FontStyles.Italic });
        });

    protected override void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + 24;
        Top = area.Top + area.Height * 0.08;
    }
}

/// <summary>
/// Where Cloud is, at the bottom right of the screen: the area as the game's map names it (large) and its floor or
/// district (small), from the game's area volumes (Ff7rMapArea.cs). Hidden when the area is not known.
/// </summary>
public sealed class LocationWindow() : CornerWindow(Mako, new Thickness(0, 0, 3, 0))
{
    static readonly Brush Mako = Brush("#5EEAD4"), Floor = Brush("#CBD5E1");

    public void SetLocation(string? area, string? floor) =>
        SetContent(area is null ? null : $"{area}\n{floor}", () =>
        {
            Text.TextAlignment = TextAlignment.Right;
            Text.Inlines.Add(new System.Windows.Documents.Run("⌖ ") { Foreground = Mako, FontSize = 18 });
            Text.Inlines.Add(new System.Windows.Documents.Run(area) { Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
            if (floor is { Length: > 0 }) Text.Inlines.Add(new System.Windows.Documents.Run("\n" + floor) { Foreground = Floor, FontSize = 13 });
        });

    protected override void Place()
    {
        var area = SystemParameters.WorkArea;
        double width = ActualWidth > 0 ? ActualWidth : 260, height = ActualHeight > 0 ? ActualHeight : 60;
        Left = area.Right - width - 24;
        // Above the bottom edge, clear of the game's own corner prompts.
        Top = area.Bottom - height - area.Height * 0.2;
    }
}
