namespace GameTracker;

/// <summary>What the tracker asks of the window it runs in: tell the player, save, log. No WPF in the tracker itself.</summary>
public interface IProgressHost
{
    /// <summary>A short notice of what the tracker did on its own (ticked, learned).</summary>
    void Notify(string text);

    /// <summary>Save the progress and show it.</summary>
    void Save();

    /// <summary>Save the progress without redrawing.</summary>
    void Persist();
    void Error(string text);

    /// <summary>Items just handed over in play (not a loaded save): the chest they came from is opened now.</summary>
    void ItemsHandedOver(HashSet<int> ids);

    /// <summary>Items that arrived in play, for data\&lt;id&gt;\logs\items.log.</summary>
    void LogItems(IEnumerable<OwnedItem> items);
}

/// <summary>
/// Keeps the guide's ticks in step with the game, from what the reader sees: the chapter being played, items handed
/// over (Items), completion flags (Flags), the live objective and finished quests (Story), and a loaded save
/// (Load). Moved unchanged out of MainWindow (11 Oct 2026); the window shows it and adds what you tick by hand.
/// Without WPF, so it is tested in tools\tracker-tests with a fake reader.
/// </summary>
public sealed partial class ProgressTracker(IGameReader reader, GuideRules rules, string dataDir, string logsDir, IProgressHost host)
{
    readonly IGameReader _reader = reader;
    readonly GuideRules _rules = rules;
    readonly string _dataDir = dataDir, _logsDir = logsDir;
    readonly IProgressHost _host = host;
    IGameNames _names => _reader.Names;

    /// <summary>The guide as shown (Hard-only steps left out outside Hard mode).</summary>
    public Guide? Guide { get; set; }

    public Progress Progress { get; set; } = new();

    /// <summary>The chapter the game is in, null outside a game.</summary>
    public int? DetectedChapter { get; private set; }

    /// <summary>A save is being played (a few seconds of grace while loading).</summary>
    public bool InGame { get; private set; }
    int _menuTicks;

    /// <summary>When the game was last entered (title screen or a load left).</summary>
    public DateTime InGameSince { get; private set; }

    /// <summary>The game's live objective, and its live sub-objective ("Find Stamp" › "Train Yard Security"), if any.</summary>
    public GameObjective? LiveObjective { get; private set; }
    public GameObjective? LiveSubObjective { get; private set; }

    /// <summary>Chapter whose recap waits, and since when (the window recaps it).</summary>
    public (int Chapter, DateTime Since)? PendingRecap { get; set; }

    /// <summary>A loaded save is still being matched (ticks and story position): nothing is learned from what changes meanwhile.</summary>
    public bool LoadPending => _reconcile || _storyMayGoBack;

    /// <summary>A game without a reader: it is "in game" from the start, ticked by hand. True the first time (redraw).</summary>
    public bool StartManual()
    {
        if (InGame) return false;
        InGame = true;
        return true;
    }

    /// <summary>
    /// One poll (once a second): the chapter and whether a save is played, then items, a loaded save, flags, the live
    /// objective and finished quests. True when anything shown changed; wasInGame: whether a save was played before.
    /// </summary>
    public bool Poll(out bool wasInGame)
    {
        int? chapter = _reader.ReadChapter();

        // The chapter byte is only valid in game (0/255 in menus and between chapters). Wait a few seconds before
        // dropping the checklist so a flicker during loads doesn't hide it.
        wasInGame = InGame;
        if (chapter is not null) { InGame = true; _menuTicks = 0; }
        else if (_reader.Version is null || ++_menuTicks >= 3) InGame = false; // game closed: no grace period
        if (!InGame) { chapter = null; DetectedChapter = null; }
        // Back in game after the title screen or a load (or the overlay just started): the save may be another
        // one, even the same chapter, so check the ticks against it.
        if (InGame && !wasInGame) { _reconcile = true; _loadedSlots = []; ForgetRecent(); InGameSince = DateTime.Now; _reader.ForgetChestCopy(); _reader.ForgetSideQuests(); }

        bool changed = chapter is not null && chapter != DetectedChapter;
        if (chapter is not null) DetectedChapter = chapter;
        if (changed && Guide is not null)
        {
            if (Guide.Chapters.Any(c => c.Number == chapter))
            {
                // Moving on to a later chapter means the previous one's story steps, its completion trophy and its
                // end-of-chapter reward are done (that reward arrives during the chapter change, with a save copy).
                // Any other jump (an earlier chapter, or several ahead) is another save being loaded: rebuild the
                // ticks from what that save holds.
                if (chapter != Progress.Chapter && chapter != Progress.Chapter + 1) { _reconcile = true; ForgetRecent(); _reader.ForgetChestCopy(); _reader.ForgetSideQuests(); }
                else if (chapter > Progress.Chapter && Guide.Chapters.FirstOrDefault(c => c.Number == Progress.Chapter) is { } finished)
                    foreach (var o in finished.Objectives.Where(o => _rules.IsStory(o) || _rules.RewardTag(o) == "REWARD CHAPTER"
                        || (_rules.IsTrophy(o) && GuideRules.ChapterEndTrophy(o))))
                        if (Progress.Done.Add(o.Id)) Progress.History.Add(o.Id);
                // Played into the next chapter (not a save loaded or the overlay just started): recap the one that ended
                // once the last pickups had time to be read (FollowRecap).
                if (wasInGame && !_reconcile && chapter == Progress.Chapter + 1) PendingRecap = (Progress.Chapter, DateTime.Now);
                Progress.Chapter = chapter!.Value;
                _host.Persist();
            }
            else _host.Error(Lang.T($"Chapter {chapter} detected, but it is not in the guide yet", $"Chapter {chapter} terdeteksi, tapi belum ada di panduan"));
        }
        // An item step right after the current story step may be handed over any moment: look for it more often.
        _reader.ListRefresh = ExpectingItem() ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(1);
        changed |= FollowItems();
        if (_reconcile && _seenOwned is not null) changed |= Reconcile();
        changed |= FollowFlags();
        changed |= FollowObjective();
        changed |= FollowCompleted();
        return changed;
    }

    /// <summary>The guide's chapter the progress is on (the first one when it has none yet).</summary>
    public Chapter? CurrentChapter =>
        Guide?.Chapters.FirstOrDefault(c => c.Number == Progress.Chapter) ?? Guide?.Chapters.FirstOrDefault();

    /// <summary>The first story step not done yet: where you are in the chapter.</summary>
    public Objective? CurrentStory => CurrentChapter?.Objectives.FirstOrDefault(o => _rules.IsStory(o) && !Progress.Done.Contains(o.Id));

    /// <summary>
    /// A save was loaded: the flags and items that look new now came with it, not with the step you tick next.
    /// Drop them and take the next flags read as the new baseline, so nothing is learned from the load.
    /// </summary>
    void ForgetRecent()
    {
        _newFlags.Clear();
        _unknownNew.Clear();
        _pendingStory = null;
        _seenFlags = null;
    }
}
