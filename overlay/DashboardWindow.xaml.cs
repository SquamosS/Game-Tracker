using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GameTracker;

/// <summary>
/// The start window: a library of trackable games on the left (with search), the selected game on the right over
/// its art, with play time and START (game + overlay) or OVERLAY SAJA (overlay only).
/// </summary>
public partial class DashboardWindow : Window
{
    static readonly Brush Muted = Brush("#94A3B8"), Faint = Brush("#64748B"), Mako = Brush("#5EEAD4"), Cyan = Brush("#38BDF8"),
        Row = Brush("#990A1220"), RowSelected = Brush("#CC0E2A33"), Live = Brush("#4ADE80");
    static readonly FontFamily Display = new("Bahnschrift");

    /// <summary>Asks the app to open a game's overlay; true = start the game too.</summary>
    public event Action<GameModule, bool>? OpenRequested;

    GameModule? _selected = GameRegistry.All.FirstOrDefault();
    string? _shownPicture;

    public DashboardWindow()
    {
        InitializeComponent();
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaxButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Application.Current.Shutdown();
        StateChanged += (_, _) => OnStateChanged();
        Search.TextChanged += (_, _) => { SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Refresh(); };
        StartButton.Click += (_, _) => { if (_selected is { } g) OpenRequested?.Invoke(g, !g.IsRunning); };
        OverlayButton.Click += (_, _) => { if (_selected is { } g) OpenRequested?.Invoke(g, false); };
        Activated += (_, _) => Refresh();
        Loaded += (_, _) => BringToFront();
        // Running state and play time follow the games while the dashboard is open.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { if (IsVisible) Refresh(); };
        timer.Start();
        Refresh();
    }

    void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        BringToFront();
    }

    /// <summary>
    /// Brings the dashboard in front of other programs. Windows refuses a plain Activate() to a window it did not
    /// just start in the foreground, so it is briefly made topmost.
    /// </summary>
    public void BringToFront()
    {
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>A borderless window maximises over the taskbar: keep it to the work area, square the corners.</summary>
    void OnStateChanged()
    {
        // Minimised = out of the way: to the tray icon (no taskbar button to steal focus from the game).
        if (WindowState == WindowState.Minimized) { Hide(); return; }
        bool max = WindowState == WindowState.Maximized;
        MaxWidth = max ? SystemParameters.WorkArea.Width + 14 : double.PositiveInfinity;
        MaxHeight = max ? SystemParameters.WorkArea.Height + 14 : double.PositiveInfinity;
        Frame.Margin = max ? new Thickness(7) : new Thickness(0);
        Frame.CornerRadius = new CornerRadius(max ? 0 : 10);
        MaxButton.Content = max ? "" : "";
        MaxButton.ToolTip = max ? "Restore" : "Maximize";
    }

    public void Refresh()
    {
        string query = Search.Text.Trim();
        var games = GameRegistry.All.Where(g => query.Length == 0 || g.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        Games.Children.Clear();
        foreach (var game in games) Games.Children.Add(RowFor(game));
        Empty.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Count.Text = $"{games.Count} dari {GameRegistry.All.Count} game";
        ShowHero(_selected);
    }

    FrameworkElement RowFor(GameModule game)
    {
        bool running = game.IsRunning, selected = game == _selected;
        var cover = new Border { Width = 46, Height = 68, Background = Brush("#1E293B"), Margin = new Thickness(0, 0, 12, 0), ClipToBounds = true };
        if (Image(game.Cover) is { } image) cover.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        else cover.Child = new TextBlock { Text = Initials(game.DisplayName), Foreground = Muted, FontFamily = Display, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = game.DisplayName, FontFamily = Display, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = selected ? Brushes.White : Brush("#CBD5E1") });
        info.Children.Add(new TextBlock
        {
            Text = running ? "● SEDANG BERJALAN" : $"{Duration(PlayTime.Instance.For(game.Id).Seconds)} dimainkan",
            Foreground = running ? Live : Faint, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0),
        });

        var row = new DockPanel();
        DockPanel.SetDock(cover, Dock.Left);
        row.Children.Add(cover);
        row.Children.Add(info);

        // Angular row: a cut corner, and a mako bar on the left when selected.
        var shape = new Path
        {
            Data = Geometry.Parse("M0,0 L318,0 L330,12 L330,92 L0,92 Z"), Stretch = Stretch.Fill,
            Fill = selected ? RowSelected : Row, Stroke = selected ? Mako : Brush("#1F38BDF8"), StrokeThickness = 1,
        };
        var accent = new Rectangle { Width = 3, Fill = selected ? Mako : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left };
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        grid.Children.Add(shape);
        grid.Children.Add(accent);
        grid.Children.Add(new Border { Child = row, Padding = new Thickness(14, 11, 14, 11) });
        grid.MouseLeftButtonDown += (_, e) =>
        {
            _selected = game;
            if (e.ClickCount == 2) OpenRequested?.Invoke(game, !game.IsRunning);
            Refresh();
        };
        grid.MouseEnter += (_, _) => { if (game != _selected) shape.Stroke = Brush("#6638BDF8"); };
        grid.MouseLeave += (_, _) => { if (game != _selected) shape.Stroke = Brush("#1F38BDF8"); };
        return grid;
    }

    void ShowHero(GameModule? game)
    {
        Hero.Visibility = game is null ? Visibility.Collapsed : Visibility.Visible;
        if (game?.Background != _shownPicture)
        {
            _shownPicture = game?.Background;
            Picture.Background = Image(_shownPicture) is { } image
                ? new ImageBrush(image) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Center }
                : null;
        }
        if (game is null) return;

        bool running = game.IsRunning;
        var time = PlayTime.Instance.For(game.Id);
        HeroTitle.Text = game.DisplayName;
        StatusText.Text = running ? "● SEDANG BERJALAN" : "SIAP DIMAINKAN";
        StatusText.Foreground = running ? Live : Mako;
        StartButton.Content = running ? "▶  LANJUT" : "▶  START";
        StartButton.ToolTip = running ? "Game sudah berjalan: buka overlay" : "Jalankan game lewat Steam dan buka overlay";

        Stats.Children.Clear();
        Stats.Children.Add(Stat("WAKTU MAIN", Duration(time.Seconds)));
        Stats.Children.Add(Stat("TERAKHIR MAIN", time.LastPlayed is { } last ? Ago(last) : "—"));
        Stats.Children.Add(Stat("PLATFORM", game.SteamAppId is null ? "—" : "Steam"));
    }

    /// <summary>A small HUD tile: label over value, with a cut corner.</summary>
    static FrameworkElement Stat(string label, string value)
    {
        var text = new StackPanel { Margin = new Thickness(14, 9, 18, 10) };
        text.Children.Add(new TextBlock { Text = label, FontFamily = Display, FontSize = 10.5, Foreground = Cyan });
        text.Children.Add(new TextBlock { Text = value, FontFamily = Display, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 0) });
        var grid = new Grid { Margin = new Thickness(0, 0, 10, 10), Width = 170, Height = 62 };
        grid.Children.Add(new Path
        {
            Data = Geometry.Parse("M0,0 L140,0 L150,10 L150,60 L0,60 Z"), Stretch = Stretch.Fill,
            Fill = Brush("#B30A1220"), Stroke = Brush("#3338BDF8"), StrokeThickness = 1,
        });
        grid.Children.Add(text);
        return grid;
    }

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

    static string Duration(long seconds) => seconds < 60 ? "0 menit"
        : seconds < 3600 ? $"{seconds / 60} menit" : $"{seconds / 3600} jam {seconds % 3600 / 60} mnt";

    static string Ago(DateTime when)
    {
        var span = DateTime.Now - when;
        return span.TotalMinutes < 2 ? "Baru saja" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} menit lalu"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} jam lalu" : $"{(int)span.TotalDays} hari lalu";
    }

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
