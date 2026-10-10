namespace GameTracker;

/// <summary>What you do by hand: tick or untick a step, undo, say where you are, page the chapter.</summary>
public sealed partial class ProgressTracker
{
    /// <summary>
    /// The step you are on: the first open one after the last finished story step. Items skipped before
    /// that stay open (and counted as missables) but do not hold the guide back.
    /// </summary>
    public Objective? NextStep(Objective[] objectives)
    {
        int lastStory = Array.FindLastIndex(objectives, o => _rules.IsStory(o) && Progress.Done.Contains(o.Id));
        return objectives.Skip(lastStory + 1).FirstOrDefault(o => !Progress.Done.Contains(o.Id))
            ?? objectives.FirstOrDefault(o => !Progress.Done.Contains(o.Id));
    }

    /// <summary>Ticks or unticks a step by hand (saved), then learns what the game just did for it (LearnFrom).</summary>
    public void SetDone(string id, bool done)
    {
        if (done && Progress.Done.Add(id)) Progress.History.Add(id);
        if (!done && Progress.Done.Remove(id)) Progress.History.Remove(id);
        _host.Save();
        if (done) LearnFrom(id);
    }

    /// <summary>Unticks the step ticked last; its id, or null when nothing was ticked. Not saved: the window says what it undid first.</summary>
    public string? Undo()
    {
        if (Progress.History.Count == 0) return null;
        string id = Progress.History[^1];
        Progress.History.RemoveAt(Progress.History.Count - 1);
        Progress.Done.Remove(id);
        return id;
    }

    /// <summary>Double-click "I am at this step": the story steps before it are done, it is open; saved, and the game's objective learned for it.</summary>
    public void JumpTo(Objective target)
    {
        var objectives = CurrentChapter?.Objectives ?? [];
        foreach (var o in objectives.TakeWhile(o => o != target).Where(o => _rules.IsStory(o)))
            if (Progress.Done.Add(o.Id)) Progress.History.Add(o.Id);
        Progress.Done.Remove(target.Id);
        _host.Save();
        LearnStory();
    }

    /// <summary>Ctrl+Shift+PageUp/PageDown: the guide's previous or next chapter. False at either end (nothing changed, not saved).</summary>
    public bool ChangeChapter(int delta)
    {
        if (Guide is null) return false;
        int[] numbers = Guide.Chapters.Select(c => c.Number).ToArray();
        int index = Array.IndexOf(numbers, CurrentChapter!.Number) + delta;
        if (index < 0 || index >= numbers.Length) return false;
        Progress.Chapter = numbers[index];
        return true;
    }
}
