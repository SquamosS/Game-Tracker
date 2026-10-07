using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace GameTracker;

public partial class MainWindow : Window
{
    static readonly string GuidesDir = Path.Combine(AppContext.BaseDirectory, "guides");
    static readonly Brush Accent = Brush("#38BDF8"), Muted = Brush("#94A3B8"), Done = Brush("#64748B"),
        Danger = Brush("#F87171"), Current = Brush("#1A38BDF8");

    Guide? _guide;
    Progress _progress = new();
    Native? _native;
    FileSystemWatcher? _watcher;
    bool _clickThrough;
    string? _error;

    public MainWindow()
    {
        InitializeComponent();
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => DockRight();
        SourceInitialized += (_, _) => SetupHotkeys();
        Closed += (_, _) => _native?.Dispose();
        LoadGuide();
        WatchGuides();
    }

    Chapter? CurrentChapter =>
        _guide?.Chapters.FirstOrDefault(c => c.Number == _progress.Chapter) ?? _guide?.Chapters.FirstOrDefault();

    void LoadGuide()
    {
        try
        {
            string? file = Directory.Exists(GuidesDir) ? Directory.GetFiles(GuidesDir, "*.json").OrderBy(f => f).FirstOrDefault() : null;
            if (file is null)
            {
                _error = $"Tidak ada file panduan di {GuidesDir}";
                _guide = null;
            }
            else
            {
                _guide = Guide.Load(file);
                _progress = ProgressStore.Load(_guide.Game);
                _error = null;
            }
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            // Keep showing the last good guide while a file is being edited.
            _error = $"Gagal membaca panduan: {e.Message}";
        }
        Render();
    }

    /// <summary>Reloads the guide when its JSON changes, so edits show up without restarting.</summary>
    void WatchGuides()
    {
        if (!Directory.Exists(GuidesDir)) return;
        _watcher = new FileSystemWatcher(GuidesDir, "*.json") { EnableRaisingEvents = true };
        _watcher.Changed += (_, _) => Dispatcher.BeginInvoke(LoadGuide);
    }

    void SetupHotkeys()
    {
        _native = new Native(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle));
        var failed = new List<string>();
        void Bind(Key key, string label, Action action) { if (!_native.Hotkey(key, action)) failed.Add(label); }

        Bind(Key.G, "Ctrl+Shift+G", ToggleVisible);
        Bind(Key.Space, "Ctrl+Shift+Space", TickNext);
        Bind(Key.Back, "Ctrl+Shift+Backspace", Undo);
        Bind(Key.PageDown, "Ctrl+Shift+PageDown", () => ChangeChapter(+1));
        Bind(Key.PageUp, "Ctrl+Shift+PageUp", () => ChangeChapter(-1));
        Bind(Key.T, "Ctrl+Shift+T", ToggleClickThrough);
        if (failed.Count > 0) _error = $"Hotkey dipakai aplikasi lain: {string.Join(", ", failed)}";
        Render();
    }

    void DockRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 24;
        Top = area.Top + 24;
    }

    void ToggleVisible()
    {
        if (IsVisible) Hide();
        else { Show(); Topmost = true; }
    }

    void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        _native?.SetClickThrough(_clickThrough);
        Render();
    }

    void ChangeChapter(int delta)
    {
        if (_guide is null) return;
        int[] numbers = _guide.Chapters.Select(c => c.Number).ToArray();
        int index = Array.IndexOf(numbers, CurrentChapter!.Number) + delta;
        if (index < 0 || index >= numbers.Length) return;
        _progress.Chapter = numbers[index];
        Save();
    }

    void TickNext()
    {
        var next = CurrentChapter?.Objectives.FirstOrDefault(o => !_progress.Done.Contains(o.Id));
        if (next is not null) SetDone(next.Id, true);
    }

    void Undo()
    {
        if (_progress.History.Count == 0) return;
        string id = _progress.History[^1];
        _progress.History.RemoveAt(_progress.History.Count - 1);
        _progress.Done.Remove(id);
        Save();
    }

    void SetDone(string id, bool done)
    {
        if (done && _progress.Done.Add(id)) _progress.History.Add(id);
        if (!done && _progress.Done.Remove(id)) _progress.History.Remove(id);
        Save();
    }

    void Save()
    {
        if (_guide is not null) ProgressStore.Save(_guide.Game, _progress);
        Render();
    }

    void Render()
    {
        List.Children.Clear();
        var chapter = CurrentChapter;
        GameText.Text = _guide?.Game ?? "Game Tracker";
        ChapterText.Text = chapter is null ? "Belum ada panduan" : $"Chapter {chapter.Number}: {chapter.Title}";

        var objectives = chapter?.Objectives ?? [];
        int done = objectives.Count(o => _progress.Done.Contains(o.Id));
        CountText.Text = $"{done}/{objectives.Length} selesai di chapter ini";
        Bar.Width = objectives.Length == 0 ? 0 : (ActualWidth > 0 ? ActualWidth - 30 : 370) * done / objectives.Length;

        int missableLeft = objectives.Count(o => o.Missable && !_progress.Done.Contains(o.Id));
        WarnBox.Visibility = chapter?.PointOfNoReturn is null && missableLeft == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarnText.Text = string.Join("\n", new[]
        {
            missableLeft > 0 ? $"⚠ {missableLeft} item MISSABLE belum diambil." : null,
            chapter?.PointOfNoReturn,
        }.Where(s => s is not null));

        string? nextId = objectives.FirstOrDefault(o => !_progress.Done.Contains(o.Id))?.Id;
        foreach (var o in objectives) List.Children.Add(Row(o, _progress.Done.Contains(o.Id), o.Id == nextId));

        FooterText.Text = (_error is null ? "" : _error + "\n") +
            "Ctrl+Shift+G tampil/sembunyi · Space centang berikutnya · Backspace batal · " +
            "PgUp/PgDn ganti chapter · T mode mouse " + (_clickThrough ? "(tembus ke game)" : "(klik overlay)");
    }

    UIElement Row(Objective o, bool done, bool isNext)
    {
        var box = new CheckBox { IsChecked = done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
        box.Click += (_, _) => SetDone(o.Id, box.IsChecked == true);

        var title = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = isNext ? FontWeights.SemiBold : FontWeights.Normal };
        if (o.Missable && !done) title.Inlines.Add(new System.Windows.Documents.Run("MISSABLE ") { Foreground = Danger, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        title.Inlines.Add(new System.Windows.Documents.Run(o.Name) { Foreground = done ? Done : Brushes.White, TextDecorations = done ? TextDecorations.Strikethrough : null });
        title.Inlines.Add(new System.Windows.Documents.Run($"  {o.Type}") { Foreground = Accent, FontSize = 10.5 });

        var text = new StackPanel();
        text.Children.Add(title);
        if (!done) text.Children.Add(new TextBlock { Text = o.Where, TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 });

        var row = new DockPanel();
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        row.Children.Add(text);
        return new Border
        {
            Child = row,
            Padding = new Thickness(6, 5, 6, 5),
            CornerRadius = new CornerRadius(6),
            Background = isNext ? Current : Brushes.Transparent,
        };
    }

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
