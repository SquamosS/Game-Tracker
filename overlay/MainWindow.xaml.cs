using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameTracker;

public partial class MainWindow : Window, IProgressHost
{
    static readonly Brush Accent = Brush("#38BDF8"), Muted = Brush("#CBD5E1"), Done = Brush("#64748B"), Mako = Brush("#5EEAD4"),
        Danger = Brush("#F87171"), Current = Brush("#1A38BDF8"), Now = Brush("#4ADE80"), Late = Brush("#FBBF24");

    /// <summary>The whole guide (the one shown, Hard-only steps left out outside Hard mode, is the tracker's Guide).</summary>
    Guide? _guideAll;
    /// <summary>Playing on Hard, as set with Ctrl+Shift+H (the game's difficulty is not read yet).</summary>
    bool _hardMode = false;
    Native? _native;
    FileSystemWatcher? _watcher;
    bool _clickThrough;
    bool _showDone;
    /// <summary>Full checklist instead of the compact quest tracker (Ctrl+Shift+A).</summary>
    bool _full;
    /// <summary>The game's live reader (game.json "reader"); without one nothing is known (NoGameReader).</summary>
    readonly IGameReader _reader;
    IGameNames _names => _reader.Names;
    string? _itemStatus;
    /// <summary>The notice line: what the overlay just did on its own, shown for a few seconds of visible time.</summary>
    string? _notice;
    bool _noticeAlert;
    int _noticeSeconds;
    string _detectStatus = "";
    string? _error;

    /// <summary>The game this overlay tracks (the first known game when none is given).</summary>
    readonly GameModule? _game;

    /// <summary>
    /// The game has a live reader: steps tick themselves and the overlay follows the game. Without one the
    /// same checklist is ticked and paged by hotkey, always shown.
    /// </summary>
    readonly bool _live;

    /// <summary>The game's own folder data\&lt;id&gt;\ and its logs: progress, trail, chests, points, diagnostics.</summary>
    readonly string _data, _logs;

    /// <summary>Files of the old shared layout (data\...) that a live game wrote: moved into its folder once.</summary>
    static readonly string[] LiveFiles = ["trail.json", "area-links.json", "points-recorded.tsv", "chests",
        .. new[] { "area.log", "chest-flag.log", "field-actors.log", "items.log", "missed.log", "position.log", "quest-choice.log", "state.log", "trail.log" }
            .Select(log => Path.Combine("logs", log))];

    /// <summary>The guide's rules for this game: step roles (game.json "stepTypes"), name matching, rewards, areas.</summary>
    readonly GuideRules _rules;

    /// <summary>Keeps the ticks in step with the game (Modules\Progress); the window shows them and adds manual ones.</summary>
    readonly ProgressTracker _tracker;

    /// <summary>The guide's modules (Modules\\): whether steps are open, where you are, the chests, the trail.</summary>
    readonly StepStatus _status;
    readonly AreaTracker _area;
    readonly ChestGuide _chests;
    readonly TrailRecorder _trails;
    readonly Checklist _checklist;

    Chapter? CurrentChapter => _tracker.CurrentChapter;
    Objective? CurrentStory => _tracker.CurrentStory;

    void IProgressHost.Notify(string text) => Notify(text);
    void IProgressHost.Save() => Save();
    void IProgressHost.Persist() => Persist();
    void IProgressHost.Error(string text) => _error = text;
    void IProgressHost.ItemsHandedOver(HashSet<int> ids) => _chests.LearnOpened(ids, _gameState);
    void IProgressHost.LogItems(IEnumerable<OwnedItem> items) => _trails.LogItems(items);

    public MainWindow() : this(null) { }

    public MainWindow(GameModule? game)
    {
        _game = game ?? GameRegistry.All.FirstOrDefault();
        var reader = GameReaders.Create(_game?.Reader);
        _live = reader is not null;
        _reader = reader ?? new NoGameReader();
        _rules = new GuideRules(_game?.StepTypes ?? new Dictionary<string, StepType>(), _reader.Names);
        _data = DataPaths.Game(_game?.Id);
        _logs = DataPaths.GameLogs(_game?.Id);
        // The old shared layout kept the live files in data\: only a game with a reader wrote them.
        if (_live) foreach (var old in LiveFiles) DataPaths.MoveOld(_data, old);
        _tracker = new ProgressTracker(_reader, _rules, _data, _logs, this);
        _status = new StepStatus(_tracker, _rules, _reader);
        _area = new AreaTracker(_reader, _tracker, _data, _logs);
        _chests = new ChestGuide(_reader, _tracker, _rules, _area, _status, _live, _data, _logs);
        _trails = new TrailRecorder(_reader, _tracker, _rules, _area, _status, _live, _data, _logs);
        _checklist = new Checklist(_tracker, _rules, _status, _area);
        _area.Load();
        _chests.Load();
        _trails.Load();
        InitializeComponent();
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        // Handled, so a click on the switch does not start dragging the overlay.
        LangSwitch.MouseLeftButtonDown += (_, e) => { e.Handled = true; Lang.Set(!Lang.Indonesian); };
        WarnBox.MouseLeftButtonDown += (_, e) => { e.Handled = true; _warnOpen = !_warnOpen; Render(); };
        Lang.Changed += OnLanguageChanged;
        // The quest pop-up shows with the overlay, whatever hid it (a menu, Ctrl+Shift+G, the tray).
        IsVisibleChanged += (_, _) => _quest.Allowed = _location.Allowed = IsVisible;
        // The quest sits under the location panel: follow its size and whether it shows.
        _quest.Below = _location;
        _location.SizeChanged += (_, _) => _quest.Reposition();
        _location.IsVisibleChanged += (_, _) => _quest.Reposition();
        Loaded += (_, _) => DockRight();
        SourceInitialized += (_, _) => SetupHotkeys();
        // The poll timer and the guide watcher must stop too: left running, a closed overlay keeps reading the game
        // and saving its own, older progress over the one a reopened overlay saves.
        Closed += (_, _) => { Lang.Changed -= OnLanguageChanged; _toast.Close(); _quest.Close(); _location.Close(); _poll.Stop(); _watcher?.Dispose(); _native?.Dispose(); _reader.Dispose(); };
        LoadGuide();
        WatchGuides();
        WatchGame();
    }

    /// <summary>The point-of-no-return warning opened by a click: the steps left and the reason, not just the count.</summary>
    bool _warnOpen;

    void OnLanguageChanged() => Render();

    static readonly Brush SwitchOn = Brush("#5EEAD4"), SwitchOnText = Brush("#0F172A");

    /// <summary>Lights the side of the EN | IN switch that is the language shown.</summary>
    void RenderLanguageSwitch()
    {
        bool id = Lang.Indonesian;
        LangEn.Background = id ? Brushes.Transparent : SwitchOn;
        LangIn.Background = id ? SwitchOn : Brushes.Transparent;
        LangEnText.Foreground = id ? Muted : SwitchOnText;
        LangInText.Foreground = id ? SwitchOnText : Muted;
    }

    void LoadGuide()
    {
        try
        {
            string? file = _game is not null && File.Exists(_game.GuideFile) ? _game.GuideFile : null;
            if (file is null)
            {
                _error = _game is null ? Lang.T("No game in the games folder yet", "Belum ada game di folder games")
                    : Lang.T($"No guide: {_game.GuideFile}", $"Tidak ada panduan: {_game.GuideFile}");
                _tracker.Guide = null;
            }
            else
            {
                _guideAll = Guide.Load(file);
                _trails.Points = TrailRecorder.LoadPoints(Path.Combine(_game!.Folder, "points.json"));
                _tracker.Progress = ProgressStore.Load(_data, _guideAll.Game);
                _hardMode = _tracker.Progress.Hard;
                _tracker.Guide = _guideAll.ForMode(_hardMode);
                _error = ProgressStore.Recovered;
            }
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            // Keep showing the last good guide while a file is being edited.
            _error = Lang.T($"Could not read the guide: {e.Message}", $"Gagal membaca panduan: {e.Message}");
        }
        Render();
    }

    /// <summary>Reloads the guide when its JSON changes, so edits show up without restarting.</summary>
    void WatchGuides()
    {
        if (_game is null || !Directory.Exists(_game.Folder)) return;
        _watcher = new FileSystemWatcher(_game.Folder, "guide.json") { EnableRaisingEvents = true };
        _watcher.Changed += (_, _) => Dispatcher.BeginInvoke(LoadGuide);
    }

    /// <summary>Follows the chapter the game is in. Manual PgUp/PgDn still works until the game changes chapter.</summary>
    void WatchGame()
    {
        _poll.Tick += (_, _) => PollGame();
        _poll.Start();
        PollGame();
    }

    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };

    void PollGame()
    {
        // No reader for this game: the checklist is all there is, from the chapter you page to.
        if (!_live)
        {
            if (_tracker.StartManual()) Render();
            return;
        }
        bool changed = _tracker.Poll(out bool wasInGame);
        FollowRecap();
        var position = _tracker.InGame ? _reader.ReadPosition() : null;
        _area.LogPosition(position);
        FollowGameState();
        changed |= _area.Follow(position);
        _trails.Follow();
        if (_tracker.InGame) { _chests.LogChests(); _chests.LogChestFlags(); _chests.LogFieldActors(); }
        // Distances to chests change as you walk: redraw when a rounded one does, at most every 2 s.
        if (DateTime.Now - _distancesAt >= TimeSpan.FromSeconds(2))
        {
            string distances = string.Join("|", (CurrentChapter?.Objectives ?? []).Where(o => !_tracker.Progress.Done.Contains(o.Id)).Select(StepDistance))
                + "#" + string.Join("|", _chests.ChestsHere());
            if (distances != _distances) { _distances = distances; _distancesAt = DateTime.Now; changed = true; }
        }
        // Side quests taken or cleared show under the quest (QuestsUnderWay): redraw when the quest page changes.
        string sides = string.Join("|", _status.QuestsUnderWay().Select(q => q.Title + "#" + q.Text));
        if (sides != _sides) { _sides = sides; changed = true; }
        // The notice counts down only while you can see it: a chapter's recap must not run out behind a cutscene.
        if (_notice is not null && IsVisible && --_noticeSeconds <= 0) { _notice = null; RenderNotice(); }

        string game = _game?.ShortName ?? "";
        string status = _reader.Problem
            ?? (_reader.Version is null ? Lang.T($"{game} is not running", $"{game} belum jalan")
                : !_tracker.InGame ? Lang.T($"{game} {_reader.Version} detected, waiting for a save to load", $"{game} {_reader.Version} terdeteksi, menunggu save di-load")
                : Lang.T($"{game} {_reader.Version}: Chapter {_tracker.DetectedChapter} detected", $"{game} {_reader.Version}: Chapter {_tracker.DetectedChapter} terdeteksi")
                    + (!_tracker.InventoryRead ? "" : Lang.T(", inventory read", ", inventory terbaca")) + (_itemStatus is null ? "" : $"\n{_itemStatus}"));
        if (changed || status != _detectStatus || wasInGame != _tracker.InGame) { _detectStatus = status; Render(); }
    }

    static readonly System.Text.RegularExpressions.Regex HardNote = new(@"\s*\(?Hard:.*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Open items, side quests and discoveries of this chapter in the area you are in, plus the side quest or
    /// discovery the game has active now wherever you are: once started it stays up until it is ticked. Steps the guide
    /// puts after the current story step are not there yet (The Language of Flowers comes after the Rude fight, even in
    /// the same garden), as in the compact list.
    /// </summary>
    List<Objective> HereSteps() => CurrentChapter is not { } chapter ? []
        : chapter.Objectives.Where(o => !_rules.IsStory(o) && !_rules.IsTrophy(o) && !_tracker.Progress.Done.Contains(o.Id)
            && (_status.IsLiveQuest(o) || (_area.IsHere(o) && !_status.NotYet(o, chapter) && _chests.ChestPlaced(o) != false))
            && !(GuideRules.RewardOf(o, chapter) is { } quest && !_tracker.Progress.Done.Contains(quest.Id))).ToList();

    string _hereShown = "";
    readonly ToastWindow _toast = new();

    /// <summary>The "here" box: what is still to get in this area, missables in red. Pulses when it changes.</summary>
    void RenderHere()
    {
        var steps = _tracker.InGame ? HereSteps() : [];
        HereBox.Visibility = steps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HereText.Inlines.Clear();
        if (steps.Count > 0)
        {
            HereText.Inlines.Add(new System.Windows.Documents.Run(Lang.T("IN THIS AREA  ", "DI AREA INI  ")) { Foreground = Mako, FontWeight = FontWeights.Bold, FontSize = 11 });
            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0) HereText.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Muted });
                HereText.Inlines.Add(new System.Windows.Documents.Run(steps[i].Name)
                    { Foreground = steps[i].Missable ? Danger : Brushes.White, FontWeight = FontWeights.SemiBold });
                if (StepDistance(steps[i]) is { } distance) HereText.Inlines.Add(new System.Windows.Documents.Run(" " + distance) { Foreground = Mako, FontSize = 11.5 });
            }
        }
        string shown = string.Join("|", steps.Select(s => s.Id));
        // The banner stays while something is left here: it fades in for something new to see (another area), only
        // updates when a step was just ticked, and fades out once nothing is left or you walk out.
        var before = _hereShown.Split('|').ToHashSet();
        // Hidden with the overlay: drawn when it shows again (FollowGameState), new steps then still fade in.
        if (_menuOpen || _userHidden) return;
        if (steps.Count == 0) _toast.FadeOut();
        else _toast.Show(_area.Here?.Area ?? "", steps.Select(o => (StepDistance(o) is { } d ? $"{o.Name} · {d}" : o.Name, o.Missable)).ToList(), steps.Any(o => !before.Contains(o.Id)));
        if (shown != _hereShown && steps.Count > 0)
            HereBox.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(350))
                { AutoReverse = false, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(4) });
        _hereShown = shown;
    }

    /// <summary>A step's "where" as shown: its closing "Hard: ..." note (Hard-only rewards) only in Hard mode.</summary>
    string ShownWhere(Objective o) => _hardMode ? o.ShownWhere : HardNote.Replace(o.ShownWhere, "");

    string _distances = "", _sides = "";
    DateTime _distancesAt;

    // ---- Step types as shown: the game's own list (game.json "stepTypes", GuideRules.TypeOf) --------------------------

    /// <summary>A step kind as written on the overlay: the guide's own type name, or its English one.</summary>
    string TypeText(Objective o) => Lang.Indonesian ? o.Type : _rules.TypeOf(o).En ?? o.Type;

    /// <summary>The compact tracker's marker for a kind of step, drawn in its TypeBrush colour.</summary>
    string TypeIcon(Objective o) => _rules.TypeOf(o).Icon ?? "•";

    readonly Dictionary<string, Brush> _typeBrushes = [];

    /// <summary>One colour per kind of step, so discoveries, gear and collectibles are told apart at a glance.</summary>
    Brush TypeBrush(Objective o)
    {
        if (_rules.TypeOf(o).Color is not { } color) return Muted;
        if (!_typeBrushes.TryGetValue(color, out var brush))
        {
            try { brush = Brush(color); brush.Freeze(); }
            catch (FormatException) { brush = Muted; } // a typo in game.json greys the type instead of crashing
            _typeBrushes[color] = brush;
        }
        return brush;
    }

    /// <summary>The REWARD tags' colour.</summary>
    static readonly Brush RewardColor = Brush("#FCD34D");

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
        Bind(Key.H, "Ctrl+Shift+H", ToggleHard);
        // Ctrl+Shift+P is VS Code's command palette (and other apps'): Alt added.
        if (!_native.Hotkey(Key.P, RecordSpot, alt: true)) failed.Add("Ctrl+Shift+Alt+P");
        // Language: Ctrl+Shift+L is often taken by other apps, so Ctrl+Shift+Alt+L stands in; the switch's tooltip names the one in use.
        void SwitchLanguage() => Lang.Set(!Lang.Indonesian);
        string? langKey = _native.Hotkey(Key.L, SwitchLanguage) ? "Ctrl+Shift+L"
            : _native.Hotkey(Key.L, SwitchLanguage, alt: true) ? "Ctrl+Shift+Alt+L" : null;
        if (langKey is null) failed.Add("Ctrl+Shift+L");
        LangSwitch.ToolTip = "English / Bahasa Indonesia" + (langKey is null ? "" : $" ({langKey})");
        if (failed.Count > 0) _error = Lang.T($"Hotkeys taken by another app: {string.Join(", ", failed)}", $"Hotkey dipakai aplikasi lain: {string.Join(", ", failed)}");
        Render();
    }

    void DockRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 24;
        Top = area.Top + area.Height * 0.3; // below the game's minimap
    }

    /// <summary>Ctrl+Shift+G: hidden by you stays hidden; a menu only hides it while it is open (FollowGameState).</summary>
    void ToggleVisible()
    {
        _userHidden = !_userHidden;
        ApplyVisibility();
        if (!_userHidden) RenderHere();
    }

    bool _userHidden, _menuOpen;

    void ApplyVisibility()
    {
        bool show = !_userHidden && !_menuOpen;
        if (show && !IsVisible) { Show(); Topmost = true; }
        else if (!show && IsVisible) Hide();
        _toast.Visibility = show ? Visibility.Visible : Visibility.Hidden;
    }

    GameState? _gameState;

    /// <summary>
    /// Shows the overlay and the area banner only while exploring (not in menus, the map, cutscenes or battles), and logs every change of the game's
    /// state values to data\logs\state.log with the objective, to learn what cutscenes and battles look like.
    /// </summary>
    void FollowGameState()
    {
        var state = _reader.ReadGameState();
        if (state == _gameState) return;
        _gameState = state;
        try
        {
            File.AppendAllText(Path.Combine(_logs, "state.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}	{(state is null ? "-" : state.Detail)}	{_area.Here?.Area}	{_tracker.LiveObjective?.Title}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        // Shown only while exploring (1): menus (3, also a battle's command menu), cutscenes (5) and battles (0) hide
        // it. Unknown (another game version) shows it.
        bool menu = state is { } && !state.Exploring;
        if (menu != _menuOpen)
        {
            _menuOpen = menu;
            ApplyVisibility();
            // The banner kept what it showed before the menu: bring it up to date (picked up, walked on, new steps).
            if (!menu) RenderHere();
        }
    }

    /// <summary>Ctrl+Shift+A: compact tracker, then the full checklist, then the full checklist with finished steps.</summary>
    void ToggleArchive()
    {
        if (!_full) { _full = true; _showDone = false; }
        else if (!_showDone) _showDone = true;
        else _full = _showDone = false;
        Render();
    }

    /// <summary>Ctrl+Shift+H: Normal or Hard mode; Hard shows the Hard-only steps and reward notes.</summary>
    void ToggleHard()
    {
        if (_guideAll is null) return;
        _hardMode = _tracker.Progress.Hard = !_hardMode;
        _tracker.Guide = _guideAll.ForMode(_hardMode);
        Save();
    }

    void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        _native?.SetClickThrough(_clickThrough);
        Render();
    }

    void ChangeChapter(int delta)
    {
        if (_tracker.ChangeChapter(delta)) Save();
    }

    void TickNext()
    {
        var next = _tracker.NextStep(CurrentChapter?.Objectives ?? []);
        if (next is null) return;
        Notify(Lang.T($"✓ Ticked: {next.Name}", $"✓ Dicentang: {next.Name}"));
        SetDone(next.Id, true);
    }


    void Undo()
    {
        if (_tracker.Undo() is not { } id) return;
        Notify(Lang.T($"↶ Undone: {StepName(id)}", $"↶ Dibatalkan: {StepName(id)}"));
        Save();
    }

    string StepName(string id) => _tracker.Guide?.Chapters.SelectMany(c => c.Objectives).FirstOrDefault(o => o.Id == id)?.Name ?? id;

    void SetDone(string id, bool done) => _tracker.SetDone(id, done);

    void Save()
    {
        Persist();
        Render();
    }

    static string SaveFailed => Lang.T("Could not save progress (file locked or disk full?). Retried at the next tick.",
        "Gagal menyimpan progress (file dikunci/disk penuh?). Dicoba lagi saat centang berikutnya.");

    /// <summary>The save error shown, in the language it was shown in; cleared by the next save that works.</summary>
    string? _saveFailed;

    /// <summary>Writes the progress; a failed write shows in the footer until a later one succeeds.</summary>
    void Persist()
    {
        if (_tracker.Guide is null) return;
        if (!ProgressStore.Save(_data, _tracker.Guide.Game, _tracker.Progress)) _error = _saveFailed = SaveFailed;
        else if (_saveFailed is not null)
        {
            if (_error == _saveFailed) _error = null;
            _saveFailed = null;
        }
    }

    static readonly Brush QuestTitle = Brush("#38BDF8"), QuestText = Brush("#BAE6FD");

    /// <summary>The live quest's own pop-up at the top left (QuestWindow), apart from the checklist.</summary>
    readonly QuestWindow _quest = new();

    /// <summary>Where Cloud is, at the top left above the quest (LocationWindow).</summary>
    readonly LocationWindow _location = new();

    /// <summary>
    /// The live quest as the game shows it goes to the quest pop-up; the overlay keeps only a note while it is being
    /// looked for. A game without a reader has no live quest: the guide's story step shows on the overlay instead.
    /// </summary>
    void RenderObjective()
    {
        ObjectiveText.Inlines.Clear();
        FillObjective();
        ObjectiveText.Visibility = ObjectiveText.Inlines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void FillObjective()
    {
        _quest.SetQuest(_live ? _tracker.LiveObjective?.Title ?? _tracker.LiveObjective?.TitleKey : null, _tracker.LiveObjective?.Text,
            _tracker.LiveSubObjective is { } s ? s.Title ?? s.TitleKey : null, _tracker.LiveSubObjective?.Text,
            _live && _tracker.InGame ? _status.QuestsUnderWay().Select(q => (q.Step is { } o ? TypeIcon(o) : "•", q.Step is { } step ? TypeBrush(step) : QuestTitle, q.Title,
                q.Step is { } kind ? TypeText(kind) : Lang.T("side quest", "side quest"), q.Text, q.Step is { } far ? StepDistance(far) : null)).ToList() : []);
        // Without a reader the guide's own story step is the quest: its name, then where.
        if (!_live)
        {
            if (CurrentStory is not { } step) return;
            ObjectiveText.Inlines.Add(new System.Windows.Documents.Run(step.Name) { Foreground = QuestTitle, FontSize = 16, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrWhiteSpace(step.Where))
                ObjectiveText.Inlines.Add(new System.Windows.Documents.Run("\n" + ShownWhere(step)) { Foreground = QuestText, FontSize = 13 });
            return;
        }
        if (_tracker.LiveObjective is null && _tracker.InGame)
            ObjectiveText.Inlines.Add(new System.Windows.Documents.Run(Lang.T("Looking for the active objective...", "Mencari objektif aktif...")) { Foreground = Muted, FontSize = 13 });
    }

    bool WarningOpen(Objective o) => o.ShownWarning is not null && (o.Needs is not { Length: > 0 } needs || !needs.All(_tracker.Progress.Done.Contains));

    void Render()
    {
        List.Children.Clear();
        RenderLanguageSwitch();
        var chapter = CurrentChapter;
        RenderObjective();
        // Where you are has its own panel at the top left, above the quest: the area large, the floor small.
        _location.SetLocation(_tracker.InGame ? _area.Here?.Area : null, _area.Here?.Floor, _tracker.InGame ? _chests.ChestsHere() : []);
        RenderRoute(chapter);
        RenderNotice();
        RenderHere();
        // No status or hotkey help (the user knows them): the footer only appears when something is wrong.
        FooterText.Text = _error ?? _reader.Problem ?? "";
        FooterText.Visibility = _error is not null || _reader.Problem is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_full) { SizeToContent = SizeToContent.Manual; MaxHeight = double.PositiveInfinity; if (Height < 400) Height = 640; }
        else { SizeToContent = SizeToContent.Height; MaxHeight = 520; }

        // No checklist until a save is loaded: the chapter would only be a guess.
        if (!_tracker.InGame && _reader.Problem is null)
        {
            ChapterText.Text = _reader.Version is null ? Lang.T("WAITING FOR THE GAME", "MENUNGGU GAME") : Lang.T("WAITING FOR A SAVE TO LOAD", "MENUNGGU SAVE DI-LOAD");
            CountText.Text = "";
            ObjectiveText.Inlines.Add(new System.Windows.Documents.Run(_reader.Version is null
                ? Lang.T($"Start {_game?.ShortName}; the overlay follows your chapter on its own.", $"Buka {_game?.ShortName}, overlay akan mengikuti chapter kamu otomatis.")
                : Lang.T("Load a save or start a chapter; the checklist shows up on its own.", "Load save atau mulai chapter, checklist-nya muncul otomatis.")) { Foreground = Muted, FontSize = 13 });
            ObjectiveText.Visibility = Visibility.Visible;
            Bar.Width = 0;
            WarnBox.Visibility = Visibility.Collapsed;
            return;
        }
        // A small label; a chapter of another story (FF7R's INTERmission, guide.json "story") is named by its title alone.
        ChapterText.Text = chapter is null ? Lang.T("NO GUIDE YET", "BELUM ADA PANDUAN")
            : (_hardMode ? "HARD · " : "") + (chapter.Story is not null ? chapter.Title.ToUpperInvariant() : $"CH {chapter.Number} · {chapter.Title.ToUpperInvariant()}");

        var objectives = chapter?.Objectives ?? [];
        // Trophies are left out everywhere: the goal is collecting everything in one run, not the trophy list.
        var counted = objectives.Where(o => !_rules.IsTrophy(o)).ToList();
        int done = counted.Count(o => _tracker.Progress.Done.Contains(o.Id));
        CountText.Text = $"{done}/{counted.Count}";
        Bar.Width = counted.Count == 0 ? 0 : (ActualWidth > 0 ? ActualWidth - 30 : 370) * done / counted.Count;

        // Warn about the nearest point of no return and the missables still open before it.
        var pending = objectives.Where(o => !_tracker.Progress.Done.Contains(o.Id)).ToList();
        var gate = pending.FirstOrDefault(o => o.Warning is not null);
        var openBefore = gate is null ? pending : pending.TakeWhile(o => o != gate).ToList();
        // Name what is still to get, so the notice shrinks as things are picked up: this chapter's missables before
        // the point of no return, plus the steps the warning itself waits for (possibly from an earlier chapter).
        var steps = _tracker.Guide?.Chapters.SelectMany(c => c.Objectives).GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First()) ?? [];
        var toGet = openBefore.Where(o => o.Missable && !_rules.IsStory(o) && !_rules.IsTrophy(o)).Select(o => o.Name)
            .Concat((gate?.Needs ?? []).Where(id => !_tracker.Progress.Done.Contains(id) && steps.ContainsKey(id)).Select(id => steps[id].Name))
            .Distinct().ToList();
        // What closes behind you (guide column closes: the warning's "Setelah ..." sentence).
        string? reason = gate?.ShownCloses;
        // Amber while the point of no return is still ahead; red once it is the story step you are on. The reason
        // (closes) only shows then, or in the full checklist: a notice that is always loud gets ignored.
        bool urgent = gate is not null && gate == CurrentStory;
        WarnBox.Visibility = toGet.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarnBox.BorderBrush = WarnText.Foreground = urgent ? Danger : Late;
        // Short by default (the count and the gate, one line); a click opens what is left and why.
        string head = gate is null ? Lang.T($"Not picked up yet ({toGet.Count})", $"Belum diambil ({toGet.Count})")
            : Lang.T($"{toGet.Count} left before {gate.Name}", $"{toGet.Count} lagi sebelum {gate.Name}");
        WarnText.TextWrapping = _warnOpen ? TextWrapping.Wrap : TextWrapping.NoWrap;
        WarnText.Text = toGet.Count == 0 ? "" : !_warnOpen ? $"⚠ {head}  ▸"
            : string.Join(Environment.NewLine, new[] { $"⚠ {head}  ▾", string.Join(" · ", toGet), urgent || _full ? reason : null }.Where(s => s is not null));

        string? nextId = _tracker.NextStep(objectives)?.Id;
        // Items listed after a story step can be done while that step is the current one.
        int current = CurrentStory is { } story ? Array.IndexOf(objectives, story) : objectives.Length;
        int phase = -1;
        if (!_full)
        {
            RenderCompact(objectives, current);
            return;
        }
        // Finished steps go to an archive that one click (or Ctrl+Shift+A) opens again.
        int archived = objectives.Count(o => _tracker.Progress.Done.Contains(o.Id));
        if (archived > 0)
        {
            var toggle = new TextBlock
            {
                Text = _showDone ? Lang.T($"▾ Hide {archived} finished steps (Ctrl+Shift+A)", $"▾ Sembunyikan {archived} langkah selesai (Ctrl+Shift+A)")
                    : Lang.T($"▸ Archive: {archived} finished steps (click or Ctrl+Shift+A)", $"▸ Arsip: {archived} langkah selesai (klik atau Ctrl+Shift+A)"),
                Foreground = Muted, FontSize = 11.5, Margin = new Thickness(6, 0, 0, 6), Cursor = Cursors.Hand,
            };
            toggle.MouseLeftButtonDown += (_, _) => ToggleArchive();
            List.Children.Add(toggle);
        }
        for (int i = 0; i < objectives.Length; i++)
        {
            var o = objectives[i];
            if (_rules.IsStory(o)) phase = i;
            bool isDone = _tracker.Progress.Done.Contains(o.Id);
            if (isDone && !_showDone) continue;
            string? tag = _checklist.FullTag(o, isDone, phase, current);
            var row = Row(o, isDone, o.Id == nextId, tag);
            List.Children.Add(row);
            if (o.Id == nextId) Dispatcher.BeginInvoke(() => row.BringIntoView(), DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// The quest itself comes from the game; below it only what the game does not tell you: a hint for the
    /// current story step, and the items and side quests open now or left behind (missables first).
    /// </summary>
    void RenderCompact(Objective[] objectives, int current)
    {
        // Only once the live objective is known: before that the guide position is just the last saved one.
        if (_tracker.LiveObjective is not null && CurrentStory is { } story && !string.IsNullOrWhiteSpace(story.Where))
            List.Children.Add(new TextBlock { Text = ShownWhere(story), TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 13, Margin = new Thickness(2, 0, 0, 6) });
        foreach (var (step, tag) in _checklist.OpenSteps(objectives, current))
            List.Children.Add(Row(step, false, false, tag));
    }

    const string TagNow = Checklist.TagNow, TagHere = Checklist.TagHere, TagBehind = Checklist.TagBehind;

    static string TagText(string tag) => tag switch
    {
        TagNow => Lang.T("NOW", "SEKARANG"),
        TagHere => Lang.T("HERE", "DI SINI"),
        TagBehind => Lang.T("LEFT BEHIND", "TERTINGGAL"),
        _ => tag,
    };

    /// <summary>
    /// One step. The compact tracker is read mid-game and is usually click-through: a coloured type icon instead
    /// of a checkbox (ticking is by hotkey), the name in white, one line of where, optional steps dimmed rather
    /// than labelled. The full checklist keeps checkboxes and every label, for sorting things out by mouse.
    /// </summary>
    FrameworkElement Row(Objective o, bool done, bool isNext, string? tag)
    {
        bool compact = !_full;
        FrameworkElement marker;
        if (compact)
        {
            // Icon column: the type icon, then a red "!" for a missable step (a fixed width keeps names aligned).
            // Two equal centred cells so the icon and the "!" line up whatever font draws each glyph.
            TextBlock Cell(string text, Brush brush, FontWeight weight) => new()
            {
                Text = text, Foreground = brush, FontWeight = weight, FontSize = 16, Width = 16, Height = 20,
                TextAlignment = TextAlignment.Center, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 20,
            };
            var icons = new StackPanel { Orientation = Orientation.Horizontal, Width = 34, Margin = new Thickness(0, -1, 4, 0), VerticalAlignment = VerticalAlignment.Top };
            icons.Children.Add(Cell(TypeIcon(o), TypeBrush(o), FontWeights.Normal));
            if (o.Missable && !done) icons.Children.Add(Cell("!", Danger, FontWeights.Black));
            marker = icons;
        }
        else
        {
            var box = new CheckBox { IsChecked = done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
            box.Click += (_, _) => SetDone(o.Id, box.IsChecked == true);
            marker = box;
        }

        var title = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = isNext ? FontWeights.SemiBold : FontWeights.Normal };
        if (tag is not null) title.Inlines.Add(new System.Windows.Documents.Run(TagText(tag) + " ") { Foreground = tag is TagNow or TagHere ? Now : Late, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        if (o.Missable && !done && !compact) title.Inlines.Add(new System.Windows.Documents.Run("! ") { Foreground = Danger, FontWeight = FontWeights.Black, FontSize = 14 });
        if (o.Optional && !done && !compact) title.Inlines.Add(new System.Windows.Documents.Run(Lang.T("OPTIONAL ", "OPSIONAL ")) { Foreground = Muted, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        // Somewhere else than where you are: the guide's area as a label, so a step of this story step that lies ahead is
        // not mistaken for one around you.
        string? elsewhere = compact && !done && tag != TagHere && GuideRules.AreaOf(o) is var (stepArea, _) ? stepArea : null;
        if (elsewhere is not null) title.Inlines.Add(new System.Windows.Documents.Run("➜ " + elsewhere.ToUpperInvariant() + " ") { Foreground = Mako, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        if (_rules.RewardTag(o) is { } reward && !done) title.Inlines.Add(new System.Windows.Documents.Run(reward + " ") { Foreground = RewardColor, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        // The icon alone is too small to tell a music disc from an item: name the kind unless the name already says it.
        if (compact && !_rules.IsStory(o) && !o.Name.Contains(TypeText(o), StringComparison.OrdinalIgnoreCase))
            title.Inlines.Add(new System.Windows.Documents.Run(TypeText(o).ToUpperInvariant() + " ") { Foreground = TypeBrush(o), FontWeight = FontWeights.Bold, FontSize = 10.5 });
        title.Inlines.Add(new System.Windows.Documents.Run(o.Name) { Foreground = done ? Done : _rules.IsStory(o) || compact ? Brushes.White : TypeBrush(o), TextDecorations = done ? TextDecorations.Strikethrough : null });
        if (!done && StepDistance(o) is { } distance) title.Inlines.Add(new System.Windows.Documents.Run("  " + distance) { Foreground = Mako, FontSize = 11.5, FontWeight = FontWeights.SemiBold });
        if (!compact) title.Inlines.Add(new System.Windows.Documents.Run($"  {TypeText(o)}") { Foreground = TypeBrush(o), FontSize = 10.5, FontWeight = FontWeights.SemiBold });

        var text = new StackPanel();
        text.Children.Add(title);
        if (!done) text.Children.Add(compact
            ? new TextBlock { Text = ShownWhere(o), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Muted, FontSize = 13 }
            : new TextBlock { Text = ShownWhere(o), TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 13 });
        if (!done && WarningOpen(o))
            text.Children.Add(new TextBlock { Text = "⚠ " + o.ShownWarning, TextWrapping = TextWrapping.Wrap, Foreground = Danger, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });

        var row = new DockPanel();
        DockPanel.SetDock(marker, Dock.Left);
        row.Children.Add(marker);
        row.Children.Add(text);
        var border = new Border
        {
            Child = row,
            Padding = new Thickness(compact ? 0 : 6, 5, 6, 5),
            Margin = new Thickness(_rules.IsStory(o) || compact ? 0 : 16, _rules.IsStory(o) ? 6 : 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Background = isNext ? Current : Brushes.Transparent,
            Opacity = compact && o.Optional ? 0.55 : elsewhere is not null ? 0.75 : 1,
            ToolTip = compact ? ShownWhere(o) : Lang.T("Double-click: I am at this step", "Double-click: aku sudah di langkah ini"),
        };
        border.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) JumpTo(o); };
        return border;
    }

    /// <summary>
    /// "I'm here": marks every earlier story step done so this step becomes current. Earlier items and
    /// side quests stay unticked, so missables you may have skipped still show up.
    /// </summary>
    void JumpTo(Objective target) => _tracker.JumpTo(target);

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
