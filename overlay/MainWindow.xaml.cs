using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameTracker;

public partial class MainWindow : Window
{
    static readonly string GuidesDir = Path.Combine(AppContext.BaseDirectory, "guides");
    static readonly Brush Accent = Brush("#38BDF8"), Muted = Brush("#94A3B8"), Done = Brush("#64748B"),
        Danger = Brush("#F87171"), Current = Brush("#1A38BDF8"), Now = Brush("#4ADE80"), Late = Brush("#FBBF24");

    Guide? _guide;
    Progress _progress = new();
    Native? _native;
    FileSystemWatcher? _watcher;
    bool _clickThrough;
    bool _showDone;
    readonly Ff7rChapterReader _reader = new();
    int? _detectedChapter;
    bool _inGame;
    int _menuTicks;
    int? _story;
    readonly StoryMap _storyMap = StoryMap.Load();
    readonly ItemMap _itemMap = ItemMap.Load();
    HashSet<(int, uint)>? _seenOwned;
    readonly long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    readonly List<(int Id, DateTime When)> _unknownNew = new();
    string? _itemStatus;
    HashSet<string>? _seenFlags;
    readonly List<(string Flag, DateTime When)> _newFlags = new();
    (Objective Step, DateTime When)? _pendingStory;
    string _detectStatus = "";
    string? _error;

    public MainWindow()
    {
        InitializeComponent();
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => DockRight();
        SourceInitialized += (_, _) => SetupHotkeys();
        Closed += (_, _) => { _native?.Dispose(); _reader.Dispose(); };
        LoadGuide();
        WatchGuides();
        WatchGame();
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

    /// <summary>Follows the chapter the game is in. Manual PgUp/PgDn still works until the game changes chapter.</summary>
    void WatchGame()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => PollGame();
        timer.Start();
        PollGame();
    }

    void PollGame()
    {
        int? chapter = _reader.ReadChapter();

        // The chapter byte is only valid in game (0/255 in menus and between chapters). Wait a few seconds before
        // dropping the checklist so a flicker during loads doesn't hide it.
        if (chapter is not null) { _inGame = true; _menuTicks = 0; }
        else if (++_menuTicks >= 3) _inGame = false;
        if (!_inGame) { chapter = null; _detectedChapter = null; }

        bool changed = chapter is not null && chapter != _detectedChapter;
        if (chapter is not null) _detectedChapter = chapter;
        if (changed && _guide is not null)
        {
            if (_guide.Chapters.Any(c => c.Number == chapter))
            {
                // Moving on to a later chapter means the previous one's story steps and its completion trophy are done.
                if (chapter > _progress.Chapter && _guide.Chapters.FirstOrDefault(c => c.Number == _progress.Chapter) is { } finished)
                    foreach (var o in finished.Objectives.Where(o => o.Type == "cerita" || (o.Type == "trofi" && o.Where.Contains("selesai", StringComparison.OrdinalIgnoreCase))))
                        if (_progress.Done.Add(o.Id)) _progress.History.Add(o.Id);
                _progress.Chapter = chapter!.Value;
                ProgressStore.Save(_guide.Game, _progress);
            }
            else _error = $"Chapter {chapter} terdeteksi, tapi belum ada di panduan";
        }
        changed |= FollowStory();
        changed |= FollowItems();
        changed |= FollowFlags();

        string status = _reader.Problem
            ?? (_reader.Version is null ? "FF7R belum jalan"
                : !_inGame ? $"FF7R {_reader.Version} terdeteksi, menunggu save di-load"
                : $"FF7R {_reader.Version}: Chapter {_detectedChapter} terdeteksi" + (_story is null ? "" : $", progres cerita {_story}")
                    + (_seenOwned is null ? "" : ", inventory terbaca") + (_itemStatus is null ? "" : $"\n{_itemStatus}"));
        if (changed || status != _detectStatus) { _detectStatus = status; Render(); }
    }

    /// <summary>
    /// Ticks guide steps when the game gives you the matching item, materia or disc. Only items obtained
    /// while the overlay runs count (except music discs, which are unique), since owning a Fire Materia
    /// from earlier says nothing about this chapter's one.
    /// </summary>
    bool FollowItems()
    {
        if (!_inGame || _guide is null) return false;
        var owned = _reader.ReadOwned();
        if (owned is null) return false;
        bool changed = false;
        if (_seenOwned is null)
        {
            _seenOwned = owned.Select(o => (o.Id, o.Obtained)).ToHashSet();
            foreach (var o in owned)
                if (_itemMap.Name(o.Id) is { } disc)
                    foreach (var step in _guide.Chapters.SelectMany(c => c.Objectives))
                        if (step.Type == "music disc" && Matches(step, disc) && _progress.Done.Add(step.Id)) changed = true;
            if (changed) Save();
            return true;
        }
        // Only things obtained since the overlay started: older records come from save-buffer copies of other saves.
        foreach (var o in owned.Where(o => o.Id != 20 && _seenOwned.Add((o.Id, o.Obtained)) && o.Obtained >= _startedAt - 120))
        {
            // Consumables (ids below 100: potions, gil...) are never guide steps, so they are not learned.
            if (_itemMap.Name(o.Id) is not { } name) { if (o.Id >= 100) _unknownNew.Add((o.Id, DateTime.Now)); continue; }
            var step = StepFor(name);
            if (step is null || !_progress.Done.Add(step.Id)) continue;
            _progress.History.Add(step.Id);
            Save();
            _itemStatus = $"Otomatis dicentang: {step.Name}";
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Ticks quest and story steps from the save data's completion flags. Flags are persistent, so steps whose
    /// flag is already set when a save loads are ticked too.
    /// </summary>
    bool FollowFlags()
    {
        if (!_inGame || CurrentChapter is not { } chapter) return false;
        var flags = _reader.ReadFlags();
        if (flags is null) return false;
        bool first = _seenFlags is null;
        if (!first) foreach (var f in flags.Except(_seenFlags!)) _newFlags.Add((f, DateTime.Now));
        // Story flags are written at the next autosave, often minutes after you ticked the step: learn them then.
        if (_pendingStory is var (pending, since) && DateTime.Now - since < TimeSpan.FromMinutes(15)
            && _newFlags.LastOrDefault(f => f.When > since && _itemMap.FlagName(f.Flag) is null) is { Flag: not null } late)
        {
            _itemMap.LearnFlag(late.Flag, pending.Id);
            _itemStatus = $"Dipelajari: flag {late.Flag} = {pending.Name}";
            _pendingStory = null;
            _newFlags.Clear();
        }        _seenFlags = flags;
        bool changed = false;
        foreach (var flag in flags)
            if (_itemMap.FlagName(flag) is { } name
                && chapter.Objectives.FirstOrDefault(o => !_progress.Done.Contains(o.Id) && (o.Id == name || Matches(o, name))) is { } step)
            {
                _progress.Done.Add(step.Id);
                _progress.History.Add(step.Id);
                _itemStatus = $"Otomatis dicentang: {step.Name}";
                changed = true;
            }
        if (changed) Save();
        return changed;
    }

    /// <summary>You ticked a quest or story step: the newest unknown flag set in the last 3 minutes belongs to it.</summary>
    void LearnFlag(Objective step)
    {
        _newFlags.RemoveAll(f => DateTime.Now - f.When > TimeSpan.FromMinutes(3) || _itemMap.FlagName(f.Flag) is not null);
        if (_newFlags.Count == 0)
        {
            if (step.Type == "cerita") _pendingStory = (step, DateTime.Now);
            return;
        }
        var (flag, _) = _newFlags[^1];
        _newFlags.Clear();
        _itemMap.LearnFlag(flag, step.Id);
        _itemStatus = $"Dipelajari: flag {flag} = {step.Name}";
    }

    /// <summary>"Shiva Materia" also matches a step called just "Shiva".</summary>
    static bool Matches(Objective step, string name) =>
        step.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
        || (name.EndsWith(" Materia") && step.Name.Contains(name[..^8], StringComparison.OrdinalIgnoreCase));

    /// <summary>The open step this item belongs to, preferring ones that are due now or left behind.</summary>
    Objective? StepFor(string name)
    {
        var objectives = CurrentChapter?.Objectives ?? [];
        int next = CurrentStory is { } story ? Array.IndexOf(objectives, story) : objectives.Length;
        int due = Array.FindIndex(objectives, next + 1, o => o.Type == "cerita");
        var open = objectives.Select((o, i) => (o, i))
            .Where(x => x.o.Type != "cerita" && !_progress.Done.Contains(x.o.Id) && Matches(x.o, name)).ToList();
        return open.FirstOrDefault(x => due < 0 || x.i < due).o ?? open.FirstOrDefault().o;
    }

    /// <summary>You ticked an item step right after the game gave you an item it did not know: remember the pair.</summary>
    void LearnItem(Objective step)
    {
        _unknownNew.RemoveAll(u => DateTime.Now - u.When > TimeSpan.FromMinutes(2));
        if (_unknownNew.Count == 0) return;
        // Materia ids are 10000 and up; pick the newest unknown of the right kind for this step.
        int pick = _unknownNew.FindLastIndex(u => (u.Id >= 10000) == (step.Type == "materia"));
        if (pick < 0) return;
        var (id, _) = _unknownNew[pick];
        _unknownNew.RemoveAt(pick);
        _itemMap.Learn(id, step.Name);
        _itemStatus = $"Dipelajari: item {id} = {step.Name}";
    }

    /// <summary>Moves the guide to the story step the game is on, once that counter value has been learned.</summary>
    bool FollowStory()
    {
        int? story = _inGame ? _reader.ReadStoryProgress() : null;
        if (story == _story) return false;
        _story = story;
        var chapter = CurrentChapter;
        if (story is not null && chapter is not null && chapter.Number == _detectedChapter
            && _storyMap.Lookup(chapter, story.Value) is { } stepId && stepId != CurrentStory?.Id)
            SetStoryPosition(chapter.Objectives.First(o => o.Id == stepId));
        return true;
    }

    Objective? CurrentStory => CurrentChapter?.Objectives.FirstOrDefault(o => o.Type == "cerita" && !_progress.Done.Contains(o.Id));

    /// <summary>Story steps before the target become done, the target and later ones open. Items are left alone.</summary>
    void SetStoryPosition(Objective target)
    {
        bool before = true;
        foreach (var o in CurrentChapter?.Objectives ?? [])
        {
            if (o == target) before = false;
            if (o.Type != "cerita") continue;
            if (before) { if (_progress.Done.Add(o.Id)) _progress.History.Add(o.Id); }
            else if (_progress.Done.Remove(o.Id)) _progress.History.Remove(o.Id);
        }
        Save();
    }

    /// <summary>Remembers that the current counter value belongs to the story step you just marked.</summary>
    void LearnStory()
    {
        if (_story is { } value && CurrentStory is { } step && CurrentChapter?.Number == _detectedChapter)
            _storyMap.Record(CurrentChapter!.Number, value, step.Id);
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
        Bind(Key.A, "Ctrl+Shift+A", ToggleArchive);
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

    void ToggleArchive()
    {
        _showDone = !_showDone;
        Render();
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
        var next = NextStep(CurrentChapter?.Objectives ?? []);
        if (next is not null) SetDone(next.Id, true);
    }

    /// <summary>
    /// The step you are on: the first open one after the last finished story step. Items skipped before
    /// that stay open (and counted as missables) but do not hold the guide back.
    /// </summary>
    Objective? NextStep(Objective[] objectives)
    {
        int lastStory = Array.FindLastIndex(objectives, o => o.Type == "cerita" && _progress.Done.Contains(o.Id));
        return objectives.Skip(lastStory + 1).FirstOrDefault(o => !_progress.Done.Contains(o.Id))
            ?? objectives.FirstOrDefault(o => !_progress.Done.Contains(o.Id));
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
        var step = CurrentChapter?.Objectives.FirstOrDefault(o => o.Id == id);
        if (done && step?.Type == "cerita") LearnStory();
        if (done && step is not null && step.Type is "cerita" or "side quest" or "kejadian") LearnFlag(step);
        else if (done && step is not null) LearnItem(step);
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
        FooterText.Text = (_error is null ? "" : _error + "\n") + _detectStatus + "\n" +
            "Ctrl+Shift+G tampil/sembunyi · Space centang berikutnya · Backspace batal · " +
            "PgUp/PgDn ganti chapter · A arsip · T mode mouse · double-click langkah = posisiku " + (_clickThrough ? "(tembus ke game)" : "(klik overlay)");

        // No checklist until a save is loaded: the chapter would only be a guess.
        if (!_inGame && _reader.Problem is null)
        {
            ChapterText.Text = _reader.Version is null ? "Menunggu game..." : "Menunggu save di-load...";
            CountText.Text = _reader.Version is null
                ? "Buka FF7R, overlay akan mengikuti chapter kamu otomatis."
                : "Load save atau mulai chapter, checklist-nya muncul otomatis.";
            Bar.Width = 0;
            WarnBox.Visibility = Visibility.Collapsed;
            return;
        }
        ChapterText.Text = chapter is null ? "Belum ada panduan" : $"Chapter {chapter.Number}: {chapter.Title}";

        var objectives = chapter?.Objectives ?? [];
        int done = objectives.Count(o => _progress.Done.Contains(o.Id));
        CountText.Text = $"{done}/{objectives.Length} selesai di chapter ini";
        Bar.Width = objectives.Length == 0 ? 0 : (ActualWidth > 0 ? ActualWidth - 30 : 370) * done / objectives.Length;

        // Warn about the nearest point of no return and the missables still open before it.
        var pending = objectives.Where(o => !_progress.Done.Contains(o.Id)).ToList();
        var gate = pending.FirstOrDefault(o => o.Warning is not null);
        var openBefore = gate is null ? pending : pending.TakeWhile(o => o != gate).ToList();
        int missableLeft = openBefore.Count(o => o.Missable);
        WarnBox.Visibility = gate is null && chapter?.PointOfNoReturn is null && missableLeft == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarnText.Text = string.Join("\n", new[]
        {
            missableLeft > 0 ? (gate is null ? $"⚠ {missableLeft} item MISSABLE belum diambil." : $"⚠ {missableLeft} item MISSABLE belum diambil sebelum \"{gate.Name}\".") : null,
            gate?.Warning ?? chapter?.PointOfNoReturn,
        }.Where(s => s is not null));

        string? nextId = NextStep(objectives)?.Id;
        // Items listed after a story step can be done while that step is the current one.
        int current = CurrentStory is { } story ? Array.IndexOf(objectives, story) : objectives.Length;
        int phase = -1;
        // Finished steps go to an archive that one click (or Ctrl+Shift+A) opens again.
        int archived = objectives.Count(o => _progress.Done.Contains(o.Id));
        if (archived > 0)
        {
            var toggle = new TextBlock
            {
                Text = _showDone ? $"▾ Sembunyikan {archived} langkah selesai (Ctrl+Shift+A)" : $"▸ Arsip: {archived} langkah selesai (klik atau Ctrl+Shift+A)",
                Foreground = Muted, FontSize = 11.5, Margin = new Thickness(6, 0, 0, 6), Cursor = Cursors.Hand,
            };
            toggle.MouseLeftButtonDown += (_, _) => ToggleArchive();
            List.Children.Add(toggle);
        }
        for (int i = 0; i < objectives.Length; i++)
        {
            var o = objectives[i];
            if (o.Type == "cerita") phase = i;
            bool isDone = _progress.Done.Contains(o.Id);
            if (isDone && !_showDone) continue;
            string? tag = o.Type == "cerita" || isDone ? null : phase == current ? "SEKARANG" : phase < current ? "TERTINGGAL" : null;
            var row = Row(o, isDone, o.Id == nextId, tag);
            List.Children.Add(row);
            if (o.Id == nextId) Dispatcher.BeginInvoke(() => row.BringIntoView(), DispatcherPriority.Loaded);
        }
    }

    FrameworkElement Row(Objective o, bool done, bool isNext, string? tag)
    {
        var box = new CheckBox { IsChecked = done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
        box.Click += (_, _) => SetDone(o.Id, box.IsChecked == true);

        var title = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = isNext ? FontWeights.SemiBold : FontWeights.Normal };
        if (tag is not null) title.Inlines.Add(new System.Windows.Documents.Run(tag + " ") { Foreground = tag == "SEKARANG" ? Now : Late, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        if (o.Missable && !done) title.Inlines.Add(new System.Windows.Documents.Run("MISSABLE ") { Foreground = Danger, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        title.Inlines.Add(new System.Windows.Documents.Run(o.Name) { Foreground = done ? Done : Brushes.White, TextDecorations = done ? TextDecorations.Strikethrough : null });
        title.Inlines.Add(new System.Windows.Documents.Run($"  {o.Type}") { Foreground = Accent, FontSize = 10.5 });

        var text = new StackPanel();
        text.Children.Add(title);
        if (!done) text.Children.Add(new TextBlock { Text = o.Where, TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 });
        if (!done && o.Warning is not null)
            text.Children.Add(new TextBlock { Text = "⚠ " + o.Warning, TextWrapping = TextWrapping.Wrap, Foreground = Danger, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });

        var row = new DockPanel();
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        row.Children.Add(text);
        var border = new Border
        {
            Child = row,
            Padding = new Thickness(6, 5, 6, 5),
            Margin = new Thickness(o.Type == "cerita" ? 0 : 16, o.Type == "cerita" ? 6 : 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Background = isNext ? Current : Brushes.Transparent,
            ToolTip = "Double-click: aku sudah di langkah ini",
        };
        border.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) JumpTo(o); };
        return border;
    }

    /// <summary>
    /// "I'm here": marks every earlier story step done so this step becomes current. Earlier items and
    /// side quests stay unticked, so missables you may have skipped still show up.
    /// </summary>
    void JumpTo(Objective target)
    {
        var objectives = CurrentChapter?.Objectives ?? [];
        foreach (var o in objectives.TakeWhile(o => o != target).Where(o => o.Type == "cerita"))
            if (_progress.Done.Add(o.Id)) _progress.History.Add(o.Id);
        _progress.Done.Remove(target.Id);
        Save();
        LearnStory();
    }

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
