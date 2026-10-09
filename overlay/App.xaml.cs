using System.Windows;
using Forms = System.Windows.Forms;

namespace GameTracker;

/// <summary>
/// Starts on the dashboard (or straight into a game's overlay with --game &lt;id&gt;). While an overlay is open the
/// dashboard hides and a tray icon brings it back. One instance only: two would fight over hotkeys and progress.
/// </summary>
public partial class App : Application
{
    Mutex? _single;
    DashboardWindow? _dashboard;
    MainWindow? _overlay;
    Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, @"Local\GameTracker.Single", out bool first);
        if (!first)
        {
            MessageBox.Show("Game Tracker sudah berjalan (lihat ikon di dekat jam).", "Game Tracker");
            Shutdown();
            return;
        }
        PlayTime.Instance.Start();
        _dashboard = new DashboardWindow();
        _dashboard.OpenRequested += Open;
        CreateTray();

        int at = Array.FindIndex(e.Args, a => a.Equals("--game", StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && at + 1 < e.Args.Length && GameRegistry.Find(e.Args[at + 1]) is { } game) Open(game, false);
        else _dashboard.Show();
    }

    /// <summary>Opens the game's overlay (starting the game first when asked) and hides the dashboard.</summary>
    void Open(GameModule game, bool launch)
    {
        if (launch) game.Launch();
        if (_overlay is null)
        {
            // Not activated: focus (and the controller) stays with the game.
            _overlay = new MainWindow(game) { ShowActivated = false };
            _overlay.Closed += (_, _) => { _overlay = null; ShowDashboard(); };
            _overlay.Show();
        }
        _dashboard?.Hide();
    }

    void ShowDashboard()
    {
        if (_dashboard is null) return;
        _dashboard.Show();
        _dashboard.WindowState = WindowState.Normal;
        _dashboard.Activate();
        _dashboard.Refresh();
    }

    void CreateTray()
    {
        var icon = GameRegistry.All.Select(g => g.Icon).FirstOrDefault(p => p is not null);
        _tray = new Forms.NotifyIcon
        {
            Text = "Game Tracker",
            Icon = icon is not null ? IconFrom(icon) : System.Drawing.SystemIcons.Application,
            Visible = true,
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Dashboard", null, (_, _) => ShowDashboard());
        menu.Items.Add("Tampilkan/sembunyikan overlay", null, (_, _) => { if (_overlay is { } o) { if (o.IsVisible) o.Hide(); else o.Show(); } });
        menu.Items.Add("Tutup overlay", null, (_, _) => _overlay?.Close());
        menu.Items.Add("-");
        menu.Items.Add("Keluar", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowDashboard();
    }

    static System.Drawing.Icon IconFrom(string png)
    {
        try
        {
            using var bitmap = new System.Drawing.Bitmap(png);
            return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
        }
        catch (Exception e) when (e is ArgumentException or System.IO.IOException) { return System.Drawing.SystemIcons.Application; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PlayTime.Instance.Save();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _single?.Dispose();
        base.OnExit(e);
    }
}
