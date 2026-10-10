namespace GameTracker;

/// <summary>
/// Whether a step of the guide is open yet and whether it is the quest the game is playing: from the progress, the
/// game's quest page and live objective, and the guide's After column. Used by the area banner, the chests and the trail.
/// </summary>
public sealed class StepStatus(ProgressTracker tracker, GuideRules rules, IGameReader reader)
{
    readonly ProgressTracker _tracker = tracker;
    readonly GuideRules _rules = rules;
    readonly IGameReader _reader = reader;

    /// <summary>
    /// Whether the game's quest page shows this side quest (IGameReader.SideQuests): "???" quests
    /// have none yet. Null when the page cannot tell: not a side quest, or none of this chapter's side quests has an entry
    /// (another chapter, or the objective search has not run yet).
    /// </summary>
    public bool? SideQuestOpen(Objective o)
    {
        if (!_rules.IsQuest(o) || _tracker.CurrentChapter is not { } chapter) return null;
        var sides = _reader.SideQuests;
        bool Listed(Objective q) => sides.Any(s => GuideRules.SameQuest(q, s.Title));
        if (!chapter.Objectives.Any(q => _rules.IsQuest(q) && Listed(q))) return null;
        return Listed(o);
    }

    /// <summary>
    /// A step named by After or Revisit is reached once it is the current story step or done: Ch8's side quests open when
    /// "Requests for the Mercenary" starts, not when it ends.
    /// </summary>
    public bool Reached(string id) => _tracker.Progress.Done.Contains(id) || _tracker.CurrentStory?.Id == id;

    /// <summary>
    /// Not open yet: the guide puts the step after the current story step, or the step that opens it (After) is not done.
    /// </summary>
    public bool NotYet(Objective o, Chapter chapter)
    {
        if (SideQuestOpen(o) is { } open) return !open; // the game's quest page decides (Ch8)
        if (o.After is { } after) return !Reached(after); // After decides, wherever the guide lists the step
        int index = Array.IndexOf(chapter.Objectives, o);
        if (index < 0 || _tracker.CurrentStory is not { } story) return false;
        int current = Array.IndexOf(chapter.Objectives, story);
        // The story step this one belongs to: the last one before it.
        int phase = Array.FindLastIndex(chapter.Objectives, index, s => _rules.IsStory(s));
        return current >= 0 && phase > current;
    }

    /// <summary>A side quest or discovery that is the game's live objective now.</summary>
    /// Not one left for later (GameObjective.Later): that one shows only in its area.
    public bool IsLiveQuest(Objective o) => _rules.IsQuestOrEvent(o) && _tracker.LiveObjective?.Title is { Length: >= 3 } title && GuideRules.SameQuest(o, title)
        && !_tracker.LiveObjective.Later;
}
