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
    readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
    string _shown = "";
    /// <summary>Off until the overlay itself shows (MainWindow.IsVisibleChanged); a quest that changed while hidden fades in on show.</summary>
    bool _hasQuest, _allowed, _pendingFade, _closed;

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
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;
        Content = new Border
        {
            Child = _text,
            Background = Brush("#E60A1220"),
            BorderBrush = QuestTitle,
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 18, 10),
        };
        SourceInitialized += (_, _) => new Native(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)).SetClickThrough(true);
        Closed += (_, _) => _closed = true;
        Place();
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
        if (changed && _hasQuest) _pendingFade = true;
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
        bool show = _hasQuest && _allowed;
        if (show && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
        if (show && _pendingFade)
        {
            _pendingFade = false;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
        }
    }

    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + 24;
        Top = area.Top + area.Height * 0.08;
    }

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
