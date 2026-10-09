using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GameTracker;

/// <summary>
/// The start window: the trackable games, a search box, play time per game, and Start (game + overlay) or
/// Overlay (overlay only). The selected game's picture fills the background.
/// </summary>
public partial class DashboardWindow : Window
{
    static readonly Brush Accent = Brush("#38BDF8"), Muted = Brush("#94A3B8"), Faint = Brush("#64748B"),
        Card = Brush("#B30F172A"), CardSelected = Brush("#E61E293B"), Running = Brush("#4ADE80");

    /// <summary>Asks the app to open a game's overlay; true = start the game too.</summary>
    public event Action<GameModule, bool>? OpenRequested;

    GameModule? _selected;

    public DashboardWindow()
    {
        InitializeComponent();
        TitleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Application.Current.Shutdown();
        Search.TextChanged += (_, _) => { SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Refresh(); };
        Activated += (_, _) => Refresh();
        // Running badges and play time follow the games while the dashboard is open.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { if (IsVisible) Refresh(); };
        timer.Start();
        _selected = GameRegistry.All.FirstOrDefault();
        Refresh();
    }

    public void Refresh()
    {
        string query = Search.Text.Trim();
        var games = GameRegistry.All.Where(g => query.Length == 0 || g.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        Games.Children.Clear();
        foreach (var game in games) Games.Children.Add(CardFor(game));
        Empty.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowPicture(_selected);
    }

    FrameworkElement CardFor(GameModule game)
    {
        bool running = game.IsRunning;
        var time = PlayTime.Instance.For(game.Id);

        var cover = new Border { Width = 64, Height = 96, CornerRadius = new CornerRadius(6), Background = Brush("#1E293B"), Margin = new Thickness(0, 0, 14, 0) };
        if (Image(game.Cover) is { } coverImage) cover.Background = new ImageBrush(coverImage) { Stretch = Stretch.UniformToFill };
        else cover.Child = new TextBlock { Text = Initials(game.DisplayName), Foreground = Muted, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = game.DisplayName, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        info.Children.Add(new TextBlock { Text = $"Waktu main: {Duration(time.Seconds)}", Foreground = Muted, Margin = new Thickness(0, 4, 0, 0) });
        info.Children.Add(new TextBlock
        {
            Text = running ? "● Sedang berjalan" : time.LastPlayed is { } last ? $"Terakhir main: {Ago(last)}" : "Belum pernah dimainkan",
            Foreground = running ? Running : Faint, FontSize = 12, Margin = new Thickness(0, 2, 0, 8),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var start = new Button { Content = running ? "▶  Lanjut" : "▶  Start", Style = (Style)FindResource("ActionButton"), Background = Accent, Foreground = Brush("#0F172A"), Margin = new Thickness(0, 0, 8, 0) };
        start.ToolTip = running ? "Game sudah berjalan: buka overlay" : "Jalankan game lewat Steam dan buka overlay";
        start.Click += (_, _) => OpenRequested?.Invoke(game, !running);
        var overlay = new Button { Content = "Overlay", Style = (Style)FindResource("ActionButton"), ToolTip = "Buka overlay saja" };
        overlay.Click += (_, _) => OpenRequested?.Invoke(game, false);
        buttons.Children.Add(start);
        buttons.Children.Add(overlay);
        info.Children.Add(buttons);

        var row = new DockPanel();
        DockPanel.SetDock(cover, Dock.Left);
        row.Children.Add(cover);
        row.Children.Add(info);
        var card = new Border
        {
            Child = row, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(10),
            Background = game == _selected ? CardSelected : Card,
            BorderBrush = running ? Running : game == _selected ? Brush("#5538BDF8") : Brush("#1AFFFFFF"), BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            _selected = game;
            if (e.ClickCount == 2) OpenRequested?.Invoke(game, !game.IsRunning);
            Refresh();
        };
        return card;
    }

    void ShowPicture(GameModule? game) =>
        Picture.Background = Image(game?.Background) is { } image ? new ImageBrush(image) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right } : null;

    /// <summary>Loads a picture without locking the file, so it can be replaced while the app runs.</summary>
    static BitmapImage? Image(string? path)
    {
        if (path is null) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is System.IO.IOException or NotSupportedException or UriFormatException) { return null; }
    }

    static string Initials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(w => w[0]));

    static string Duration(long seconds) => seconds < 60 ? "belum tercatat"
        : seconds < 3600 ? $"{seconds / 60} menit" : $"{seconds / 3600} jam {seconds % 3600 / 60} menit";

    static string Ago(DateTime when)
    {
        var span = DateTime.Now - when;
        return span.TotalMinutes < 2 ? "baru saja" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} menit lalu"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} jam lalu" : $"{(int)span.TotalDays} hari lalu";
    }

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
