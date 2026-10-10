namespace GameTracker;

/// <summary>Ticks quest and story steps from the save's completion flags and learns which flag is which step.</summary>
public sealed partial class ProgressTracker
{
    HashSet<string>? _seenFlags;
    readonly List<(string Flag, DateTime When)> _newFlags = new();
    (Objective Step, DateTime When)? _pendingStory;

    /// <summary>
    /// Ticks quest and story steps from the save data's completion flags. Flags are persistent, so steps whose
    /// flag is already set when a save loads are ticked too.
    /// </summary>
    bool FollowFlags()
    {
        if (!InGame || CurrentChapter is not { } chapter) return false;
        var flags = _reader.ReadFlags();
        if (flags is null) return false;
        bool first = _seenFlags is null;
        // Nothing learns from flags older than the pending story window, so they need not be kept.
        _newFlags.RemoveAll(f => DateTime.Now - f.When > TimeSpan.FromMinutes(15));
        if (!first) foreach (var f in flags.Except(_seenFlags!)) _newFlags.Add((f, DateTime.Now));
        // Story flags are written at the next autosave, often minutes after you ticked the step: learn them then.
        // Only when that autosave set exactly one unknown flag; with several, any of them could be the step's.
        if (_pendingStory is var (pending, since) && DateTime.Now - since < TimeSpan.FromMinutes(15))
        {
            var late = _newFlags.Where(f => f.When > since && _names.FlagName(f.Flag) is null).Select(f => f.Flag).Distinct().ToList();
            if (late.Count == 1)
            {
                _names.LearnFlag(late[0], pending.Id);
                _host.Notify(Lang.T($"Learned: flag {late[0]} = {pending.Name}", $"Dipelajari: flag {late[0]} = {pending.Name}"));
                _pendingStory = null;
                _newFlags.Clear();
            }
            else if (late.Count > 1)
            {
                _host.Notify(Lang.T("Not learned: more than one new flag", "Tidak dipelajari: lebih dari satu flag baru"));
                _pendingStory = null;
            }
        }
        _seenFlags = flags;
        bool changed = false;
        foreach (var flag in flags)
            if (_names.FlagName(flag) is { } name
                && chapter.Objectives.FirstOrDefault(o => !Progress.Done.Contains(o.Id) && (o.Id == name || _rules.Matches(o, name))) is { } step)
            {
                Progress.Done.Add(step.Id);
                Progress.History.Add(step.Id);
                _host.Notify(Lang.T($"Auto-ticked: {step.Name}", $"Otomatis dicentang: {step.Name}"));
                changed = true;
            }
        if (changed) _host.Save();
        return changed;
    }

    /// <summary>
    /// You ticked a quest or story step: the unknown flag set in the last 3 minutes belongs to it, but only if it
    /// was the only one. A learned pair ticks the step from then on, so a wrong guess is worse than none.
    /// </summary>
    void LearnFlag(Objective step)
    {
        _newFlags.RemoveAll(f => DateTime.Now - f.When > TimeSpan.FromMinutes(3) || _names.FlagName(f.Flag) is not null);
        if (_newFlags.Count == 0)
        {
            if (_rules.IsStory(step)) _pendingStory = (step, DateTime.Now);
            return;
        }
        var flags = _newFlags.Select(f => f.Flag).Distinct().ToList();
        if (flags.Count > 1) { _host.Notify(Lang.T("Not learned: more than one new flag", "Tidak dipelajari: lebih dari satu flag baru")); return; }
        _newFlags.Clear();
        _names.LearnFlag(flags[0], step.Id);
        _host.Notify(Lang.T($"Learned: flag {flags[0]} = {step.Name}", $"Dipelajari: flag {flags[0]} = {step.Name}"));
    }
}
