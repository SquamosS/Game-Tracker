namespace GameTracker;

/// <summary>
/// Which steps of a chapter the checklist shows and how they are tagged: a step of the current story step (NOW), one in
/// the area you are in (HERE), one left behind (BEHIND). The window only draws them.
/// </summary>
public sealed class Checklist(ProgressTracker tracker, GuideRules rules, StepStatus status, AreaTracker area)
{
    readonly ProgressTracker _tracker = tracker;
    readonly GuideRules _rules = rules;
    readonly StepStatus _status = status;
    readonly AreaTracker _area = area;

    /// <summary>A step's tag: a step of the current story step, one in your area, one left behind.</summary>
    public const string TagNow = "NOW", TagHere = "HERE", TagBehind = "BEHIND";

    /// <summary>
    /// The compact tracker's steps: open items and side quests up to the current story step, those in your area
    /// first (TagHere), then missables; earlier ones tagged TagBehind. Optional pick-ups only while you pass them.
    /// </summary>
    public List<(Objective Step, string? Tag)> OpenSteps(Objective[] objectives, int current)
    {
        int phase = -1;
        var open = new List<(Objective Step, string? Tag)>();
        for (int i = 0; i < objectives.Length; i++)
        {
            var o = objectives[i];
            if (_rules.IsStory(o)) { phase = i; continue; }
            // A step with After opens when that step is reached, wherever the guide lists it (Ch8's side quests come after
            // "Battle Intel & VR" in the guide but open with "Requests for the Mercenary").
            if (_tracker.Progress.Done.Contains(o.Id)) continue;
            // The game's quest page first (Ch8's side quests); else After, wherever the guide lists the step; else the order.
            if (!(_status.SideQuestOpen(o) is { } listed ? listed || _status.IsLiveQuest(o) : o.After is { } opens ? _status.Reached(opens) || _status.IsLiveQuest(o) : phase <= current)) continue;
            // Trophies are not tracked here: the rewards they come with are steps of their own.
            if (_rules.IsTrophy(o)) continue;
            if (o.Optional && phase < current) continue;
            // Behind you on a stretch you cannot walk back: shown again once you can (Revisit).
            if (phase < current && o.Revisit is { } back && !_status.Reached(back)) continue;
            open.Add((o, _area.IsHere(o) ? TagHere : phase < current ? TagBehind : null));
        }
        return open.OrderByDescending(x => x.Tag == TagHere).ThenByDescending(x => x.Step.Missable).ToList();
    }

    /// <summary>
    /// The full checklist's tag for a step (phase: index of the story step it belongs to; current: the current story
    /// step's index): NOW for the current story step's steps, BEHIND for open ones before it (once you can go back).
    /// </summary>
    public string? FullTag(Objective o, bool done, int phase, int current) => _rules.IsStory(o) || done ? null : phase == current ? TagNow
        : phase < current && (o.Revisit is null || _status.Reached(o.Revisit)) ? TagBehind : null;
}
