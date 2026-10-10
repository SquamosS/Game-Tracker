namespace GameTracker;

/// <summary>Follows the game's live objective (the story position) and ticks quests the game shows as finished.</summary>
public sealed partial class ProgressTracker
{
    string _lastChoiceLog = "";

    /// <summary>
    /// Follows the game's live story objective: shows its text, and moves the guide to the story step it was
    /// learned for (objectives are grouped by their title key, so sub-objectives map to the same step).
    /// </summary>
    bool FollowObjective()
    {
        var objective = InGame && DetectedChapter is int chapterNow ? _reader.ReadObjective(chapterNow) : null;
        // The current objective is the one furthest along the guide: older ones stay referenced (history, Story
        // menu), but nothing points at objectives that have not started yet.
        if (objective is not null && CurrentChapter is { } guideChapter)
        {
            int Index(GameObjective o) => o.Title is { } t
                ? Array.FindIndex(guideChapter.Objectives, s => _rules.IsStory(s) && GuideRules.NamedAs(s, t))
                : -1;
            // One guide step can cover several quests ("A / B"): then the later row in the game's table wins.
            var furthest = _reader.Candidates.MaxBy(o => (Index(o), o.Order));
            if (furthest is not null && Index(furthest) < 0) furthest = null;
            // The game's own order comes first (the objective it started last); the guide order is the fallback.
            var newest = _reader.NewestObjective();
            // A discovery or side quest just finished is still the newest entry, but the story goes on: follow the
            // story step again (the Story menu shows it ticked, not as the current objective).
            if (newest is { Finished: true } && Index(newest) < 0)
                newest = _reader.Candidates.Where(o => !o.Sub && Index(o) >= 0).OrderByDescending(o => (Index(o), o.Order)).FirstOrDefault() ?? newest;
            objective = newest ?? furthest ?? objective;
            LogChoice(objective, Index, newest is not null && furthest is not null && furthest.Row != newest.Row ? furthest : null);
        }
        var sub = objective is null ? null : _reader.SubObjectiveOf(objective);
        if (objective?.Row == LiveObjective?.Row && sub?.Row == LiveSubObjective?.Row && !_storyMayGoBack) return false;
        if (LiveObjective is not null) _reader.RefreshListsSoon();
        LiveObjective = objective;
        LiveSubObjective = sub;
        // Guide story steps carry the game's own quest names, so match by name; a learned mapping wins. A
        // sub-objective can be a guide step of its own ("Train Yard Security"), and then it is the one to follow.
        Objective? StepFor(GameObjective? live, Chapter chapter) => live is null ? null
            : _names.ObjectiveStep(live.TitleKey) is { } stepId ? chapter.Objectives.FirstOrDefault(o => o.Id == stepId)
            : chapter.Objectives.FirstOrDefault(o => _rules.IsStory(o) && live.Title is { } title && GuideRules.NamedAs(o, title));
        if (objective is not null && CurrentChapter is { } chapter && chapter.Number == DetectedChapter
            && (StepFor(sub, chapter) ?? StepFor(objective, chapter)) is { } step)
        {
            // Right after another save was loaded the game's objective is where that save really is: the story
            // goes there, and what comes after it in this chapter cannot have been collected yet.
            if (_storyMayGoBack)
            {
                SetStoryPosition(step);
                int next = Array.FindIndex(chapter.Objectives, Array.IndexOf(chapter.Objectives, step) + 1, o => _rules.IsStory(o));
                if (next >= 0)
                {
                    // An item the loaded save owns is done, wherever the guide lists it (Ch8's Moogle Emporium goods come
                    // after "Battle Intel & VR" in the guide but are bought earlier): only items listed once, as in Reconcile.
                    // Side quests come back from the game's quest page (FollowCompleted).
                    var itemSteps = Guide!.Chapters.SelectMany(c => c.Objectives).Where(s => _rules.IsItem(s)).ToList();
                    bool Owned(Objective o) => _rules.IsItem(o) && itemSteps.Count(s => s.Name == o.Name) == 1 && _liveOwnedNames.Any(n => _rules.Matches(o, n));
                    foreach (var o in chapter.Objectives.Skip(next).Where(o => !_rules.IsTrophy(o) && !Owned(o)))
                        if (Progress.Done.Remove(o.Id)) Progress.History.Remove(o.Id);
                }
                _storyMayGoBack = false;
                _host.Save();
            }
            // Otherwise the guide only moves forward: browsing the Story menu must not undo progress.
            else if (step != CurrentStory && (CurrentStory is not { } now || Array.IndexOf(chapter.Objectives, step) > Array.IndexOf(chapter.Objectives, now)))
                SetStoryPosition(step);
        }
        return true;
    }

    /// <summary>
    /// Writes the candidates and the pick to data\logs\quest-choice.log whenever either changes, marking ties
    /// (two candidates on the same guide step), so wrong picks can be traced without a screenshot.
    /// </summary>
    void LogChoice(GameObjective chosen, Func<GameObjective, int> index, GameObjective? guidePick = null)
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
            System.IO.File.AppendAllText(System.IO.Path.Combine(_logsDir, "quest-choice.log"),
                $"{DateTime.Now:HH:mm:ss} chapter {DetectedChapter}: {chosen.Title ?? chosen.TitleKey}{(tie ? "  [RAGU: kandidat lain di langkah guide yang sama]" : "")}{(guidePick is null ? "" : $"  [BEDA: guide memilih {guidePick.Title}]")}{Environment.NewLine}{text}{Environment.NewLine}");
        }
        catch (System.IO.IOException) { }
    }

    /// <summary>
    /// Ticks discoveries and side quests the game shows as done: a finished objective's entry points at its
    /// finishing row (see GameObjective.Finished).
    /// </summary>
    bool FollowCompleted()
    {
        if (CurrentChapter is not { } chapter || chapter.Number != DetectedChapter) return false;
        bool changed = false;
        // Chapter 8's side quests are entries with their own texts (IGameReader.SideQuests).
        foreach (var side in _reader.SideQuests.Where(s => s.Finished))
            foreach (var step in chapter.Objectives.Where(o => _rules.IsQuest(o) && GuideRules.SameQuest(o, side.Title)))
                if (Progress.Done.Add(step.Id))
                {
                    Progress.History.Add(step.Id);
                    _host.Notify(Lang.T($"Auto-ticked: {step.Name}", $"Otomatis dicentang: {step.Name}"));
                    changed = true;
                }
        foreach (var done in _reader.Candidates.Where(c => c.Title is not null && c.Finished))
            foreach (var step in chapter.Objectives.Where(o => _rules.IsQuestOrEvent(o) && GuideRules.SameQuest(o, done.Title!)))
                if (Progress.Done.Add(step.Id))
                {
                    Progress.History.Add(step.Id);
                    _host.Notify(Lang.T($"Auto-ticked: {step.Name}", $"Otomatis dicentang: {step.Name}"));
                    changed = true;
                }
        if (changed) _host.Save();
        return changed;
    }

    /// <summary>Story steps before the target become done, the target and later ones open. Items are left alone.</summary>
    public void SetStoryPosition(Objective target)
    {
        bool before = true;
        foreach (var o in CurrentChapter?.Objectives ?? [])
        {
            if (o == target) before = false;
            if (!_rules.IsStory(o)) continue;
            if (before) { if (Progress.Done.Add(o.Id)) Progress.History.Add(o.Id); }
            else if (Progress.Done.Remove(o.Id)) Progress.History.Remove(o.Id);
        }
        _host.Save();
    }

    /// <summary>Remembers that the game's current objective belongs to the story step you just marked.</summary>
    public void LearnStory()
    {
        // You marked where you are: remember that the game's current objective belongs to that story step.
        if (LiveObjective is { } objective && CurrentStory is { } current && CurrentChapter?.Number == DetectedChapter)
        {
            _names.LearnObjective(objective.TitleKey, current.Id);
            _host.Notify(Lang.T($"Learned: objective {objective.TitleKey} = {current.Name}", $"Dipelajari: objektif {objective.TitleKey} = {current.Name}"));
        }
    }

    /// <summary>
    /// You ticked a step by hand: learn what the game just did for it (its objective, a new flag, an unknown item).
    /// </summary>
    public void LearnFrom(string id)
    {
        var step = CurrentChapter?.Objectives.FirstOrDefault(o => o.Id == id);
        if (step is not null && _rules.IsStory(step)) LearnStory();
        if (step is not null && (_rules.IsStory(step) || _rules.IsQuestOrEvent(step))) LearnFlag(step);
        else if (step is not null) LearnItem(step);
    }
}
