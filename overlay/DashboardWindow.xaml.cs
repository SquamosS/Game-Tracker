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
    // Which games ran at the last refresh: looking up processes is not free, so once per refresh for every row.
    HashSet<GameModule> _running = new();
    DateTime _refreshedAt;

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
        timer.Tick += (_, _) => { if (IsVisible) Tick(); };
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

    /// <summary>
    /// While a game is played the dashboard usually sits behind it: then only watch which games run (a start or exit
    /// still shows within 3 seconds) and rebuild everything every 30 seconds for the play time.
    /// </summary>
    void Tick()
    {
        if (IsActive || _running.Count == 0 || DateTime.Now - _refreshedAt > TimeSpan.FromSeconds(30)) Refresh();
        else if (!_running.SetEquals(GameRegistry.All.Where(g => g.IsRunning))) Refresh();
    }

    public void Refresh()
    {
        _running = GameRegistry.All.Where(g => g.IsRunning).ToHashSet();
        _refreshedAt = DateTime.Now;
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
        bool running = _running.Contains(game), selected = game == _selected;
        var cover = new Border { Width = 46, Height = 68, Background = Brush("#1E293B"), Margin = new Thickness(0, 0, 12, 0), ClipToBounds = true };
        if (Image(game.Cover ?? SteamOf(game)?.CoverArt, 120) is { } image) cover.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        else cover.Child = new TextBlock { Text = Initials(game.DisplayName), Foreground = Muted, FontFamily = Display, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = game.DisplayName, FontFamily = Display, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = selected ? Brushes.White : Brush("#CBD5E1") });
        info.Children.Add(new TextBlock
        {
            Text = running ? "● SEDANG BERJALAN" : TimeOf(game, running) is { } time ? $"{Duration(time.Seconds)} dimainkan" : "Steam belum login",
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
        string? picture = game is null ? null : game.Background ?? SteamOf(game)?.HeroArt;
        if (picture != _shownPicture)
        {
            _shownPicture = picture;
            Picture.Background = Image(_shownPicture) is { } image
                ? new ImageBrush(image) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Center }
                : null;
        }
        if (game is null) return;

        bool running = _running.Contains(game);
        var time = TimeOf(game, running);
        HeroTitle.Text = game.DisplayName;
        StatusText.Text = running ? "● SEDANG BERJALAN" : "SIAP DIMAINKAN";
        StatusText.Foreground = running ? Live : Mako;
        StartButton.Content = running ? "▶  LANJUT" : "▶  START";
        StartButton.ToolTip = running ? "Game sudah berjalan: buka overlay" : "Jalankan game lewat Steam dan buka overlay";

        var steam = SteamOf(game);
        var online = game.SteamAppId is int appId ? OnlineOf(appId) : null;
        Stats.Children.Clear();
        if (time is { } t)
        {
            Stats.Children.Add(Stat("WAKTU MAIN", Duration(t.Seconds)));
            Stats.Children.Add(Stat("TERAKHIR MAIN", t.LastPlayed is { } last ? Ago(last) : "—"));
        }
        if (steam?.Playtime2WeeksMinutes is long recent) Stats.Children.Add(Stat("2 MINGGU TERAKHIR", Duration(recent * 60)));
        if (steam?.AchievementsUnlocked is int got && steam.AchievementsTotal is int all and > 0)
            Stats.Children.Add(Stat("ACHIEVEMENT", $"{got}/{all} · {got * 100 / all}%"));
        if (steam?.CloudState is { } cloud)
            Stats.Children.Add(Stat("CLOUD SAVE", cloud switch { "synchronized" => "Tersinkron", "changeslocally" => "Perubahan lokal", _ => cloud },
                cloud == "synchronized" ? Live : Brush("#FBBF24")));
        if (steam?.UpToDate is bool upToDate)
            Stats.Children.Add(Stat("UPDATE", upToDate ? "Up to date" : "Butuh update", upToDate ? Live : Brush("#FBBF24"),
                steam.BuildId is { } build ? $"Build {build}" + (steam.LastUpdated is { } at ? $" · {at:dd MMM yyyy}" : "") : null));
        if (steam?.SizeOnDisk is long size)
            Stats.Children.Add(Stat("UKURAN", $"{size / 1e9:0.#} GB", tip: steam.LibraryPath));
        if (online?.Players is int players) Stats.Children.Add(Stat("ONLINE SEKARANG", $"{players:N0} pemain"));
        if (online?.Price is { } price)
            Stats.Children.Add(Stat("HARGA STEAM", online.DiscountPercent is int off and > 0 ? $"{price} (-{off}%)" : price,
                online.DiscountPercent is > 0 ? Live : null));
        if (game.SteamAppId is null) Stats.Children.Add(Stat("PLATFORM", "—"));
        else if (steam?.Account is null) Stats.Children.Add(Stat("AKUN STEAM", "Belum login", Brush("#FBBF24"), tip: "Data akun (waktu main, achievement, cloud) tampil setelah login Steam"));
        else Stats.Children.Add(Stat("AKUN STEAM", steam.PersonaName ?? steam.Account));
        ShowSide(steam, online);
    }

    readonly Dictionary<string, (DateTime At, SteamInfo? Info)> _steam = new();
    readonly Dictionary<int, SteamInfo.Online?> _online = new();
    readonly HashSet<int> _fetching = new();
    readonly Dictionary<int, DateTime> _fetchedAt = new();

    /// <summary>Steam's local facts about a game, re-read every 30 seconds (the dashboard refreshes every 3).</summary>
    SteamInfo? SteamOf(GameModule game)
    {
        if (game.SteamAppId is not int id) return null;
        // Re-read at once when the logged-in account changes.
        if (_steam.TryGetValue(game.Id, out var c) && DateTime.Now - c.At < TimeSpan.FromSeconds(30) && c.Info?.Account == SteamInfo.ActiveAccount) return c.Info;
        var info = SteamInfo.Local(id, game.ScreenshotGlob);
        _steam[game.Id] = (DateTime.Now, info);
        return info;
    }

    /// <summary>News, players and price: fetched in the background, shown once they arrive.</summary>
    SteamInfo.Online? OnlineOf(int appId)
    {
        // Fetch again every 15 minutes, or after a minute when the last try got nothing (offline).
        var known = _online.GetValueOrDefault(appId);
        bool stale = !_fetchedAt.TryGetValue(appId, out var at) || DateTime.Now - at > (known is null ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(15));
        if (stale && _fetching.Add(appId))
            _ = Task.Run(() => SteamInfo.FetchOnline(appId)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                if (t.IsCompletedSuccessfully && t.Result is { } result) _online[appId] = result;
                _fetchedAt[appId] = DateTime.Now;
                _fetching.Remove(appId);
                ShowHero(_selected);
            }));
        return known;
    }

    /// <summary>The right-hand Steam panel: latest screenshot and the latest news.</summary>
    void ShowSide(SteamInfo? steam, SteamInfo.Online? online)
    {
        Side.Children.Clear();
        if (steam?.LatestScreenshot is { } shot && Image(shot, 600) is { } image)
        {
            Side.Children.Add(Heading($"SCREENSHOT · {steam.ScreenshotCount}"));
            var thumb = new Border
            {
                Height = 146, Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill }, BorderBrush = Brush("#5538BDF8"),
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 16), Cursor = Cursors.Hand, ToolTip = "Buka folder screenshot",
            };
            thumb.MouseLeftButtonDown += (_, _) => OpenPath("/select,\"" + shot + "\"", "explorer.exe");
            Side.Children.Add(thumb);
        }
        if (online?.News is { Count: > 0 } news)
        {
            Side.Children.Add(Heading("BERITA STEAM"));
            foreach (var item in news)
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                panel.Children.Add(new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 12.5 });
                panel.Children.Add(new TextBlock { Text = item.Date.ToString("dd MMM yyyy"), Foreground = Faint, FontSize = 11 });
                var link = new Border { Child = panel, Padding = new Thickness(10, 7, 10, 7), Background = Brush("#990A1220"), BorderBrush = Brush("#2238BDF8"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 6) };
                // The address comes from the web: hand only real web links to the shell, never a file or program.
                link.MouseLeftButtonDown += (_, _) =>
                {
                    if (Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        OpenPath(uri.AbsoluteUri);
                };
                Side.Children.Add(link);
            }
        }
    }

    static TextBlock Heading(string text) =>
        new() { Text = text, FontFamily = Display, FontSize = 12, Foreground = Mako, Margin = new Thickness(2, 0, 0, 8) };

    static void OpenPath(string target, string? program = null)
    {
        try
        {
            var start = program is null ? new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }
                : new System.Diagnostics.ProcessStartInfo(program, target) { UseShellExecute = true };
            System.Diagnostics.Process.Start(start)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Play time and last played: Steam's own record when the game is on Steam, else the tracker's.</summary>
    static (long Seconds, DateTime? LastPlayed)? TimeOf(GameModule game, bool running)
    {
        // Steam games: only the logged-in account's time; nothing when Steam is not logged in.
        if (game.SteamAppId is int id) return SteamStats.For(id, running) is { } steam ? (steam.Seconds, steam.LastPlayed) : null;
        var tracked = PlayTime.Instance.For(game.Id);
        return (tracked.Seconds, tracked.LastPlayed);
    }

    /// <summary>A small HUD tile: label over value, with a cut corner.</summary>
    static FrameworkElement Stat(string label, string value, Brush? color = null, string? note = null, string? tip = null)
    {
        var text = new StackPanel { Margin = new Thickness(12, 7, 12, 6), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label, FontFamily = Display, FontSize = 10.5, Foreground = Cyan });
        text.Children.Add(new TextBlock { Text = value, FontFamily = Display, FontSize = value.Length > 12 ? 13.5 : 16, FontWeight = FontWeights.SemiBold,
            Foreground = color ?? Brushes.White, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        if (note is not null) text.Children.Add(new TextBlock { Text = note, FontSize = 10, Foreground = Faint, TextTrimming = TextTrimming.CharacterEllipsis });
        var grid = new Grid { Margin = new Thickness(0, 0, 10, 10), Width = 138, Height = note is null ? 58 : 68, ToolTip = tip ?? note };
        grid.Children.Add(new Path
        {
            Data = Geometry.Parse("M0,0 L140,0 L150,10 L150,60 L0,60 Z"), Stretch = Stretch.Fill,
            Fill = Brush("#B30A1220"), Stroke = Brush("#3338BDF8"), StrokeThickness = 1,
        });
        grid.Children.Add(text);
        return grid;
    }

    // Decoded pictures by file and size, so the refresh every 3 seconds does not decode covers and screenshots
    // (up to 4K) again; a picture is decoded again only when its file is rewritten.
    static readonly Dictionary<(string Path, int Width), (DateTime Written, BitmapImage? Image)> Images = new();

    /// <summary>Loads a picture without locking the file, so it can be replaced while the app runs.</summary>
    static BitmapImage? Image(string? path, int decodeWidth = 0)
    {
        if (path is null) return null;
        DateTime written;
        try { written = System.IO.File.GetLastWriteTimeUtc(path); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        if (Images.TryGetValue((path, decodeWidth), out var cached) && cached.Written == written) return cached.Image;
        if (Images.Count > 32) Images.Clear(); // old screenshots would pile up otherwise
        var loaded = Decode(path, decodeWidth);
        Images[(path, decodeWidth)] = (written, loaded);
        return loaded;
    }

    static BitmapImage? Decode(string path, int decodeWidth)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.DecodePixelWidth = decodeWidth;
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
