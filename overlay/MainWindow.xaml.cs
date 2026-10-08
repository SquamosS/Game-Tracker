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
    /// <summary>Full checklist instead of the compact quest tracker (Ctrl+Shift+A).</summary>
    bool _full;
    readonly Ff7rChapterReader _reader = new();
    int? _detectedChapter;
    bool _inGame;
    int _menuTicks;
    readonly ItemMap _itemMap = ItemMap.Load();
    HashSet<(int, uint)>? _seenOwned;
    /// <summary>Last id seen in each inventory slot.</summary>
    readonly Dictionary<long, int> _slotIds = new();
    readonly long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    readonly List<(int Id, DateTime When)> _unknownNew = new();
    string? _itemStatus;
    HashSet<string>? _seenFlags;
    Ff7rChapterReader.Objective? _objective;
    /// <summary>The live sub-objective of _objective ("Find Stamp" › "Train Yard Security"), if any.</summary>
    Ff7rChapterReader.Objective? _subObjective;
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
        bool wasInGame = _inGame;
        if (chapter is not null) { _inGame = true; _menuTicks = 0; }
        else if (_reader.Version is null || ++_menuTicks >= 3) _inGame = false; // game closed: no grace period
        if (!_inGame) { chapter = null; _detectedChapter = null; }
        // Back in game after the title screen or a load (or the overlay just started): the save may be another
        // one, even the same chapter, so check the ticks against it.
        if (_inGame && !wasInGame) { _reconcile = true; _loadedSlots = []; }

        bool changed = chapter is not null && chapter != _detectedChapter;
        if (chapter is not null) _detectedChapter = chapter;
        if (changed && _guide is not null)
        {
            if (_guide.Chapters.Any(c => c.Number == chapter))
            {
                // Moving on to a later chapter means the previous one's story steps, its completion trophy and its
                // end-of-chapter reward are done (that reward arrives during the chapter change, with a save copy).
                // Any other jump (an earlier chapter, or several ahead) is another save being loaded: rebuild the
                // ticks from what that save holds.
                if (chapter != _progress.Chapter && chapter != _progress.Chapter + 1) _reconcile = true;
                else if (chapter > _progress.Chapter && _guide.Chapters.FirstOrDefault(c => c.Number == _progress.Chapter) is { } finished)
                    foreach (var o in finished.Objectives.Where(o => o.Type == "cerita" || RewardTag(o) == "REWARD CHAPTER"
                        || (o.Type == "trofi" && ChapterEndTrophy(o))))
                        if (_progress.Done.Add(o.Id)) _progress.History.Add(o.Id);
                _progress.Chapter = chapter!.Value;
                ProgressStore.Save(_guide.Game, _progress);
            }
            else _error = $"Chapter {chapter} terdeteksi, tapi belum ada di panduan";
        }
        // An item step right after the current story step may be handed over any moment: look for it more often.
        _reader.ListRefresh = ExpectingItem() ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(1);
        changed |= FollowItems();
        if (_reconcile && _seenOwned is not null) changed |= Reconcile();
        changed |= FollowFlags();
        changed |= FollowObjective();
        changed |= FollowCompleted();

        string status = _reader.Problem
            ?? (_reader.Version is null ? "FF7R belum jalan"
                : !_inGame ? $"FF7R {_reader.Version} terdeteksi, menunggu save di-load"
                : $"FF7R {_reader.Version}: Chapter {_detectedChapter} terdeteksi"
                    + (_seenOwned is null ? "" : ", inventory terbaca") + (_itemStatus is null ? "" : $"\n{_itemStatus}"));
        if (changed || status != _detectStatus || wasInGame != _inGame) { _detectStatus = status; Render(); }
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
            foreach (var o in owned) _slotIds[o.Slot] = o.Id;
            foreach (var o in owned)
                if (_itemMap.Name(o.Id) is { } disc)
                    foreach (var step in _guide.Chapters.SelectMany(c => c.Objectives))
                        if (step.Type == "music disc" && Matches(step, disc) && _progress.Done.Add(step.Id)) changed = true;
            if (changed) Save();
            return true;
        }
        // Only things obtained since the overlay started: older records come from save-buffer copies of other saves.
        // New: a record obtained since the overlay started, or an id put into a slot that held something else
        // (the game reuses slots, and a reused slot keeps its old time). Many slots changing at once is a save
        // being loaded or copied, not items being handed over.
        var changedSlots = owned.Where(o => o.Id > 0 && _slotIds.TryGetValue(o.Slot, out int before) && before != o.Id)
            .Select(o => o.Slot).ToHashSet();
        foreach (var o in owned) _slotIds[o.Slot] = o.Id;
        bool handedOver = changedSlots.Count is > 0 and <= 3;
        if (changedSlots.Count > 3) { _reconcile = true; _loadedSlots = changedSlots; } // a save was loaded (or copied)
        bool IsNew(Ff7rChapterReader.Owned o) =>
            (_seenOwned.Add((o.Id, o.Obtained)) && o.Obtained >= _startedAt - 120) | (handedOver && changedSlots.Contains(o.Slot));
        foreach (var o in owned.Where(o => o.Id > 0 && o.Id != 20).Where(IsNew).ToList())
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

    /// <summary>The open step this item belongs to: the current one first, then ones left behind.</summary>
    Objective? StepFor(string name)
    {
        var objectives = CurrentChapter?.Objectives ?? [];
        int next = CurrentStory is { } story ? Array.IndexOf(objectives, story) : objectives.Length;
        int due = Array.FindIndex(objectives, next + 1, o => o.Type == "cerita");
        var open = objectives.Select((o, i) => (o, i))
            .Where(x => x.o.Type != "cerita" && !_progress.Done.Contains(x.o.Id) && Matches(x.o, name)).ToList();
        // Prefer the step of the current story step (where you are), then ones left behind, then later ones:
        // the same item can be listed twice (an MP Up on the catwalk and one in a vending machine).
        return open.FirstOrDefault(x => x.i > next && (due < 0 || x.i < due)).o
            ?? open.FirstOrDefault(x => x.i < next).o
            ?? open.FirstOrDefault().o;
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

    static readonly HashSet<string> ItemTypes = ["materia", "aksesori", "armor", "senjata", "summon", "music disc", "manuskrip"];

    /// <summary>Whether an item step not yet done follows the current story step (before the next one).</summary>
    bool ExpectingItem() => CurrentChapter is { } chapter && CurrentStory is { } story
        && chapter.Objectives.SkipWhile(o => o != story).Skip(1).TakeWhile(o => o.Type != "cerita")
            .Any(o => ItemTypes.Contains(o.Type) && !_progress.Done.Contains(o.Id));

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

    /// <summary>Remembers that the game's current objective belongs to the story step you just marked.</summary>
    void LearnStory()
    {
        // You marked where you are: remember that the game's current objective belongs to that story step.
        if (_objective is { } objective && CurrentStory is { } current && CurrentChapter?.Number == _detectedChapter)
        {
            _itemMap.LearnFlag("Q:" + objective.TitleKey, current.Id);
            _itemStatus = $"Dipelajari: objektif {objective.TitleKey} = {current.Name}";
        }
    }

    /// <summary>Story steps carry the game's quest names; one step may cover several ("A / B").</summary>
    static bool NamedAs(Objective step, string title) =>
        step.Name.Split(" / ").Any(part => part.Trim().Equals(title, StringComparison.OrdinalIgnoreCase));

    string _lastChoiceLog = "";

    /// <summary>
    /// Writes the candidates and the pick to data\logs\quest-choice.log whenever either changes, marking ties
    /// (two candidates on the same guide step), so wrong picks can be traced without a screenshot.
    /// </summary>
    /// <summary>Sub-objective keys: "..._Step060_s030_030", "..._Step20_S10", "..._toPark_sub01".</summary>
    static bool IsSub(string key) => System.Text.RegularExpressions.Regex.IsMatch(key, @"_(s|S|sub)\d+(_\d+)?$");

    /// <summary>The objective of the last entry in the longest run of adjacent entries, ignoring chapter titles.</summary>
    Ff7rChapterReader.Objective? NewestEntry()
    {
        var slots = _reader.CandidateSlots
            .Where(c => !c.Objective.TitleKey.Contains("_Parent") && !c.Objective.TitleKey.EndsWith("_End") && !IsSub(c.Objective.TitleKey))
            .OrderBy(c => c.Slot).ToList();
        List<(Ff7rChapterReader.Objective Objective, long Slot, long Parent)> best = [], run = [];
        foreach (var c in slots)
        {
            if (run.Count > 0 && c.Slot - run[^1].Slot > 0x400) run = [];
            run.Add(c);
            if (run.Count >= best.Count) best = run;
        }
        return best.Count >= 2 ? best[^1].Objective : null;
    }

    void LogChoice(Ff7rChapterReader.Objective chosen, Func<Ff7rChapterReader.Objective, int> index, Ff7rChapterReader.Objective? guidePick = null)
    {
        var lines = _reader.CandidateSlots
            .Select(c => $"   {(c.Objective == chosen ? "*" : " ")} {c.Objective.Title ?? "?"} | {c.Objective.TitleKey} | order {c.Objective.Order}{(c.Objective.Finished ? " finished" : "")} | guide {index(c.Objective)} | slot {c.Slot:X} parent {c.Parent:X}")
            .Distinct().ToList();
        string text = string.Join(Environment.NewLine, lines);
        if (text == _lastChoiceLog) return;
        _lastChoiceLog = text;
        int top = index(chosen);
        bool tie = _reader.Candidates.Count(c => index(c) == top && c.Row != chosen.Row) > 0;
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(DataPaths.Logs, "quest-choice.log"),
                $"{DateTime.Now:HH:mm:ss} chapter {_detectedChapter}: {chosen.Title ?? chosen.TitleKey}{(tie ? "  [RAGU: kandidat lain di langkah guide yang sama]" : "")}{(guidePick is null ? "" : $"  [BEDA: guide memilih {guidePick.Title}]")}{Environment.NewLine}{text}{Environment.NewLine}");
        }
        catch (System.IO.IOException) { }
    }

    /// <summary>
    /// Follows the game's live story objective: shows its text, and moves the guide to the story step it was
    /// learned for (objectives are grouped by their title key, so sub-objectives map to the same step).
    /// </summary>
    /// <summary>
    /// Ticks discoveries and side quests the game shows as done: a finished objective's entry points at its
    /// finishing row (see Ff7rChapterReader.Objective.Finished).
    /// </summary>
    bool FollowCompleted()
    {
        if (CurrentChapter is not { } chapter || chapter.Number != _detectedChapter) return false;
        bool changed = false;
        foreach (var done in _reader.Candidates.Where(c => c.Title is not null && c.Finished))
            foreach (var step in chapter.Objectives.Where(o => o.Type is "kejadian" or "side quest" && SameQuest(o.Name, done.Title!)))
                if (_progress.Done.Add(step.Id))
                {
                    _progress.History.Add(step.Id);
                    _itemStatus = $"Otomatis dicentang: {step.Name}";
                    changed = true;
                }
        if (changed) Save();
        return changed;
    }

    /// <summary>"Discovery: Collapsed Passageway" is the game's "Collapsed Passageway".</summary>
    static bool SameQuest(string guideName, string title) =>
        guideName.Replace("Discovery:", "").Trim().StartsWith(title, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Items handed over automatically need no searching: "REWARD BOSS" (after a boss), "REWARD CHAPTER" (at the
    /// end of the chapter) or "REWARD" (otherwise automatic). Null for anything you have to find yourself.
    /// </summary>
    static string? RewardTag(Objective o)
    {
        if (o.Type is "cerita" or "trofi" || o.Optional || !o.Where.Contains("otomatis", StringComparison.OrdinalIgnoreCase)) return null;
        string where = o.Where.ToLowerInvariant();
        if (where.Contains("akhir chapter")) return "REWARD CHAPTER";
        if (new[] { "boss", "kalah", "drop", "fight" }.Any(where.Contains)) return "REWARD BOSS";
        return "REWARD";
    }

    /// <summary>The trophy that comes with finishing the chapter ("Otomatis saat Chapter 6 tamat/selesai").</summary>
    static bool ChapterEndTrophy(Objective o) => o.Where.Contains("otomatis saat chapter", StringComparison.OrdinalIgnoreCase)
        || o.Where.Contains("Trofi otomatis", StringComparison.OrdinalIgnoreCase);

    /// <summary>What a step is, as shown next to its name.</summary>
    static string TypeLabel(Objective o) => o.Type == "kejadian" && o.Name.StartsWith("Discovery") ? "discovery" : o.Type;

    /// <summary>One colour per kind of step, so discoveries, gear and collectibles are told apart at a glance.</summary>
    static Brush TypeBrush(string label) => label switch
    {
        "cerita" => Accent,
        "side quest" => SideQuestColor,
        "discovery" => DiscoveryColor,
        "materia" => MateriaColor,
        "senjata" => WeaponColor,
        "armor" => ArmorColor,
        "aksesori" => AccessoryColor,
        "music disc" => DiscColor,
        "summon" => SummonColor,
        "trofi" => TrophyColor,
        "manuskrip" => ManuscriptColor,
        _ => Muted,
    };

    static readonly Brush SideQuestColor = Brush("#22D3EE"), DiscoveryColor = Brush("#C084FC"), MateriaColor = Brush("#4ADE80"),
        WeaponColor = Brush("#FB923C"), ArmorColor = Brush("#2DD4BF"), AccessoryColor = Brush("#A3E635"), DiscColor = Brush("#F472B6"),
        SummonColor = Brush("#E879F9"), TrophyColor = Brush("#FCD34D"), ManuscriptColor = Brush("#D6A77A");

    bool _reconcile, _storyMayGoBack;
    /// <summary>Inventory slots that changed when a save was last loaded: they sit in the copy that save went to.</summary>
    HashSet<long> _loadedSlots = [];

    /// <summary>
    /// Another save was loaded: make the ticks match it. Steps of later chapters are not done yet; earlier
    /// chapters' story steps are; gear, discs and summons listed once in the guide are done when the save holds
    /// them. Weapons, discs and summons cannot be sold, so a missing one is unticked. Trophies belong to the account and stay.
    /// The story position follows the game's objective once it is read. The old progress is backed up first.
    /// </summary>
    bool Reconcile()
    {
        if (_guide is null || _detectedChapter is not int loaded || _reader.ReadLiveOwnedIds(_loadedSlots) is not { } live) return false;
        _reconcile = false;
        ProgressStore.Backup(_guide.Game);
        _progress.Ever.UnionWith(_progress.Done);
        var ownedNames = live.Select(id => _itemMap.Name(id)).OfType<string>().ToList();
        var itemSteps = _guide.Chapters.SelectMany(c => c.Objectives).Where(o => ItemTypes.Contains(o.Type)).ToList();
        bool sameStory(Chapter c) => (c.Number >= 21) == (loaded >= 21); // INTERmission is its own story
        foreach (var chapter in _guide.Chapters.Where(sameStory))
            foreach (var o in chapter.Objectives.Where(o => o.Type != "trofi"))
            {
                if (chapter.Number > loaded) Untick(o);
                else if (o.Type == "cerita") { if (chapter.Number < loaded) Tick(o); }
                // Earlier chapters: what was once ticked there stays ticked (discoveries, side quests, items that
                // the inventory cannot vouch for), e.g. after loading an older save and coming back.
                else if (chapter.Number < loaded && _progress.Ever.Contains(o.Id)) Tick(o);
                // Only items listed once: owning "an MP Up" says nothing about which of several MP Up spots you
                // visited. Those are left to the per-quest item monitor (FollowItems).
                else if (ItemTypes.Contains(o.Type) && itemSteps.Count(s => s.Name == o.Name) == 1)
                {
                    if (ownedNames.Any(n => Matches(o, n))) Tick(o);
                    else if (o.Type is "senjata" or "music disc" or "summon") Untick(o);
                }
            }
        _progress.Chapter = loaded;
        _storyMayGoBack = true;
        _itemStatus = $"Progress disesuaikan dengan save Chapter {loaded} (backup tersimpan)";
        Save();
        return true;

        void Tick(Objective o) { if (_progress.Done.Add(o.Id)) _progress.History.Add(o.Id); }
        void Untick(Objective o) { if (_progress.Done.Remove(o.Id)) _progress.History.Remove(o.Id); }
    }

    bool FollowObjective()
    {
        var objective = _inGame && _detectedChapter is int chapterNow ? _reader.ReadObjective(chapterNow) : null;
        // The current objective is the one furthest along the guide: older ones stay referenced (history, Story
        // menu), but nothing points at objectives that have not started yet.
        if (objective is not null && CurrentChapter is { } guideChapter)
        {
            int Index(Ff7rChapterReader.Objective o) => o.Title is { } t
                ? Array.FindIndex(guideChapter.Objectives, s => s.Type == "cerita" && NamedAs(s, t))
                : -1;
            // One guide step can cover several quests ("A / B"): then the later row in the game's table wins.
            var furthest = _reader.Candidates.MaxBy(o => (Index(o), o.Order));
            if (furthest is not null && Index(furthest) < 0) furthest = null;
            // The game's own order comes first: each objective that starts gets the next entry in one array
            // (entries 0x188 apart), so the last entry of the longest run is the newest objective. The guide
            // order is the fallback when there is no such run.
            var newest = NewestEntry();
            objective = newest ?? furthest ?? objective;
            LogChoice(objective, Index, newest is not null && furthest is not null && furthest.Row != newest.Row ? furthest : null);
        }
        // Sub-objectives have entries of their own in a second array; the newest one under this objective is live.
        var sub = objective is null ? null : _reader.CandidateSlots
            .Where(c => IsSub(c.Objective.TitleKey) && c.Objective.TitleKey.StartsWith(objective.TitleKey + "_"))
            .OrderByDescending(c => c.Slot).Select(c => c.Objective).FirstOrDefault();
        if (objective?.Row == _objective?.Row && sub?.Row == _subObjective?.Row && !_storyMayGoBack) return false;
        if (_objective is not null) _reader.RefreshListsSoon();
        _objective = objective;
        _subObjective = sub;
        // Guide story steps carry the game's own quest names, so match by name; a learned mapping wins. A
        // sub-objective can be a guide step of its own ("Train Yard Security"), and then it is the one to follow.
        Objective? StepFor(Ff7rChapterReader.Objective? live, Chapter chapter) => live is null ? null
            : _itemMap.FlagName("Q:" + live.TitleKey) is { } stepId ? chapter.Objectives.FirstOrDefault(o => o.Id == stepId)
            : chapter.Objectives.FirstOrDefault(o => o.Type == "cerita" && live.Title is { } title && NamedAs(o, title));
        if (objective is not null && CurrentChapter is { } chapter && chapter.Number == _detectedChapter
            && (StepFor(sub, chapter) ?? StepFor(objective, chapter)) is { } step)
        {
            // Right after another save was loaded the game's objective is where that save really is: the story
            // goes there, and what comes after it in this chapter cannot have been collected yet.
            if (_storyMayGoBack)
            {
                SetStoryPosition(step);
                int next = Array.FindIndex(chapter.Objectives, Array.IndexOf(chapter.Objectives, step) + 1, o => o.Type == "cerita");
                if (next >= 0)
                    foreach (var o in chapter.Objectives.Skip(next).Where(o => o.Type != "trofi"))
                        if (_progress.Done.Remove(o.Id)) _progress.History.Remove(o.Id);
                _storyMayGoBack = false;
                Save();
            }
            // Otherwise the guide only moves forward: browsing the Story menu must not undo progress.
            else if (step != CurrentStory && (CurrentStory is not { } now || Array.IndexOf(chapter.Objectives, step) > Array.IndexOf(chapter.Objectives, now)))
                SetStoryPosition(step);
        }
        return true;
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
        Top = area.Top + area.Height * 0.3; // below the game's minimap
    }

    void ToggleVisible()
    {
        if (IsVisible) Hide();
        else { Show(); Topmost = true; }
    }

    /// <summary>Ctrl+Shift+A: compact tracker, then the full checklist, then the full checklist with finished steps.</summary>
    void ToggleArchive()
    {
        if (!_full) { _full = true; _showDone = false; }
        else if (!_showDone) _showDone = true;
        else _full = _showDone = false;
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

    static readonly Brush QuestTitle = Brush("#38BDF8"), QuestText = Brush("#BAE6FD"), SubTitle = Brush("#FBBF24"), SubText = Brush("#E2E8F0");

    /// <summary>
    /// The live quest as the game shows it: the quest (blue) with its description, then the active sub-quest
    /// (amber, indented) with its own description.
    /// </summary>
    void RenderObjective()
    {
        ObjectiveText.Inlines.Clear();
        ObjectiveText.ToolTip = _subObjective?.Text ?? _objective?.Text;
        if (_objective is not { } live)
        {
            if (_inGame) ObjectiveText.Inlines.Add(new System.Windows.Documents.Run("▶ (mencari objektif aktif...)") { Foreground = QuestTitle });
            return;
        }
        ObjectiveText.Inlines.Add(new System.Windows.Documents.Run("▶ " + (live.Title ?? live.TitleKey)) { Foreground = QuestTitle, FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (live.Text is { Length: > 0 } text)
            ObjectiveText.Inlines.Add(new System.Windows.Documents.Run("\n   " + text) { Foreground = QuestText, FontSize = 12 });
        if (_subObjective is not { } sub) return;
        ObjectiveText.Inlines.Add(new System.Windows.Documents.Run("\n   › " + (sub.Title ?? sub.TitleKey)) { Foreground = SubTitle, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
        if (sub.Text is { Length: > 0 } subText)
            ObjectiveText.Inlines.Add(new System.Windows.Documents.Run("\n      " + subText) { Foreground = SubText, FontSize = 11.5, FontStyle = FontStyles.Italic });
    }

    bool WarningOpen(Objective o) => o.Warning is not null && (o.Needs is not { Length: > 0 } needs || !needs.All(_progress.Done.Contains));

    void Render()
    {
        List.Children.Clear();
        var chapter = CurrentChapter;
        GameText.Text = _guide?.Game ?? "Game Tracker";
        RenderObjective();
        // No status or hotkey help (the user knows them): the footer only appears when something is wrong.
        FooterText.Text = _error ?? _reader.Problem ?? "";
        FooterText.Visibility = _error is not null || _reader.Problem is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_full) { SizeToContent = SizeToContent.Manual; MaxHeight = double.PositiveInfinity; if (Height < 400) Height = 640; }
        else { SizeToContent = SizeToContent.Height; MaxHeight = 520; }

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
        // Trophies are left out everywhere: the goal is collecting everything in one run, not the trophy list.
        var counted = objectives.Where(o => o.Type != "trofi").ToList();
        int done = counted.Count(o => _progress.Done.Contains(o.Id));
        CountText.Text = $"{done}/{counted.Count} selesai di chapter ini";
        Bar.Width = counted.Count == 0 ? 0 : (ActualWidth > 0 ? ActualWidth - 30 : 370) * done / counted.Count;

        // Warn about the nearest point of no return and the missables still open before it.
        var pending = objectives.Where(o => !_progress.Done.Contains(o.Id)).ToList();
        var gate = pending.FirstOrDefault(o => o.Warning is not null);
        var openBefore = gate is null ? pending : pending.TakeWhile(o => o != gate).ToList();
        // Name what is still to get, so the notice shrinks as things are picked up: this chapter's missables before
        // the point of no return, plus the steps the warning itself waits for (possibly from an earlier chapter).
        var steps = _guide?.Chapters.SelectMany(c => c.Objectives).GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First()) ?? [];
        var toGet = openBefore.Where(o => o.Missable && o.Type is not ("cerita" or "trofi")).Select(o => o.Name)
            .Concat((gate?.Needs ?? []).Where(id => !_progress.Done.Contains(id) && steps.ContainsKey(id)).Select(id => steps[id].Name))
            .Distinct().ToList();
        // What closes behind you: the warning's sentence that starts with "Setelah" ("after this ...").
        string? reason = gate?.Warning is { } warning
            ? System.Text.RegularExpressions.Regex.Split(warning, @"(?<=\.)\s+").FirstOrDefault(s => s.StartsWith("Setelah"))
            : null;
        WarnBox.Visibility = toGet.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarnText.Text = toGet.Count == 0 ? "" : string.Join(Environment.NewLine, new[]
        {
            gate is null ? $"⚠ Belum diambil: {string.Join(", ", toGet)}." : $"⚠ Ambil sebelum \"{gate.Name}\": {string.Join(", ", toGet)}.",
            reason,
        }.Where(s => s is not null));

        string? nextId = NextStep(objectives)?.Id;
        // Items listed after a story step can be done while that step is the current one.
        int current = CurrentStory is { } story ? Array.IndexOf(objectives, story) : objectives.Length;
        int phase = -1;
        if (!_full)
        {
            RenderCompact(objectives, current);
            return;
        }
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

    /// <summary>
    /// The quest itself comes from the game; below it only what the game does not tell you: a hint for the
    /// current story step, and the items and side quests open now or left behind (missables first).
    /// </summary>
    void RenderCompact(Objective[] objectives, int current)
    {
        // Only once the live objective is known: before that the guide position is just the last saved one.
        if (_objective is not null && CurrentStory is { } story && !string.IsNullOrWhiteSpace(story.Where))
            List.Children.Add(new TextBlock { Text = story.Where, TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12, Margin = new Thickness(2, 0, 0, 6) });
        int phase = -1;
        var open = new List<(Objective Step, string Tag)>();
        for (int i = 0; i < objectives.Length; i++)
        {
            var o = objectives[i];
            if (o.Type == "cerita") { phase = i; continue; }
            if (phase > current || _progress.Done.Contains(o.Id)) continue;
            // Trophies are not tracked here: the rewards they come with are steps of their own.
            if (o.Type == "trofi") continue;
            // Optional pick-ups (also sold in shops) only matter while you pass them.
            if (o.Optional && phase < current) continue;
            open.Add((o, phase == current ? "SEKARANG" : "TERTINGGAL"));
        }
        foreach (var (step, tag) in open.OrderByDescending(x => x.Step.Missable))
            List.Children.Add(Row(step, false, false, tag));
    }

    FrameworkElement Row(Objective o, bool done, bool isNext, string? tag)
    {
        var box = new CheckBox { IsChecked = done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
        box.Click += (_, _) => SetDone(o.Id, box.IsChecked == true);

        var title = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = isNext ? FontWeights.SemiBold : FontWeights.Normal };
        if (tag is not null) title.Inlines.Add(new System.Windows.Documents.Run(tag + " ") { Foreground = tag == "SEKARANG" ? Now : Late, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        if (o.Missable && !done) title.Inlines.Add(new System.Windows.Documents.Run("MISSABLE ") { Foreground = Danger, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        if (o.Optional && !done) title.Inlines.Add(new System.Windows.Documents.Run("OPSIONAL ") { Foreground = Muted, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        if (RewardTag(o) is { } reward && !done) title.Inlines.Add(new System.Windows.Documents.Run(reward + " ") { Foreground = TrophyColor, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        title.Inlines.Add(new System.Windows.Documents.Run(o.Name) { Foreground = done ? Done : o.Type == "cerita" ? Brushes.White : TypeBrush(TypeLabel(o)), TextDecorations = done ? TextDecorations.Strikethrough : null });
        title.Inlines.Add(new System.Windows.Documents.Run($"  {TypeLabel(o)}") { Foreground = TypeBrush(TypeLabel(o)), FontSize = 10.5, FontWeight = FontWeights.SemiBold });

        var text = new StackPanel();
        text.Children.Add(title);
        if (!done) text.Children.Add(new TextBlock { Text = o.Where, TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 });
        if (!done && WarningOpen(o))
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
