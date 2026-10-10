namespace GameTracker;

/// <summary>Matches the ticks to a save that was just loaded.</summary>
public sealed partial class ProgressTracker
{
    bool _reconcile, _storyMayGoBack;

    /// <summary>Inventory slots that changed when a save was last loaded: they sit in the copy that save went to.</summary>
    HashSet<long> _loadedSlots = [];

    /// <summary>Names of what the loaded save owns, as Reconcile read them (the copy the save went to).</summary>
    List<string> _liveOwnedNames = [];

    /// <summary>
    /// Another save was loaded: make the ticks match it. Steps of later chapters are not done yet; earlier
    /// chapters' story steps are; gear, discs and summons listed once in the guide are done when the save holds
    /// them. Weapons, discs and summons cannot be sold, so a missing one is unticked. Trophies belong to the account and stay.
    /// The story position follows the game's objective once it is read. The old progress is backed up first.
    /// </summary>
    bool Reconcile()
    {
        if (Guide is null || DetectedChapter is not int loaded || _reader.ReadLiveOwnedIds(_loadedSlots) is not { } live) return false;
        _reconcile = false;
        ProgressStore.Backup(_dataDir, Guide.Game);
        Progress.Ever.UnionWith(Progress.Done);
        // Money is always owned and its name is part of "Gil Up": leave it out, as FollowItems does.
        var ownedNames = live.Where(id => !_names.IsCurrency(id)).Select(id => _names.Name(id)).OfType<string>().ToList();
        _liveOwnedNames = ownedNames;
        var itemSteps = Guide.Chapters.SelectMany(c => c.Objectives).Where(o => _rules.IsItem(o)).ToList();
        // A chapter of another story (guide.json "story": FF7R's INTERmission) is neither the past nor the future of this one.
        string? story = Guide.Chapters.FirstOrDefault(c => c.Number == loaded)?.Story;
        bool sameStory(Chapter c) => c.Story == story;
        foreach (var chapter in Guide.Chapters.Where(sameStory))
            foreach (var o in chapter.Objectives.Where(o => !_rules.IsTrophy(o)))
            {
                if (chapter.Number > loaded) Untick(o);
                else if (_rules.IsStory(o)) { if (chapter.Number < loaded) Tick(o); }
                // Earlier chapters: what was once ticked there stays ticked (discoveries, side quests, items that
                // the inventory cannot vouch for), e.g. after loading an older save and coming back.
                else if (chapter.Number < loaded && Progress.Ever.Contains(o.Id)) Tick(o);
                // Only items listed once: owning "an MP Up" says nothing about which of several MP Up spots you
                // visited. Those are left to the per-quest item monitor (FollowItems).
                // Optional items are also sold, so a bought copy says nothing about the chest either.
                else if (_rules.IsItem(o) && !o.Optional && itemSteps.Count(s => s.Name == o.Name) == 1)
                {
                    if (ownedNames.Any(n => _rules.Matches(o, n))) Tick(o);
                    else if (_rules.TypeOf(o).NeverLost) Untick(o);
                }
            }
        Progress.Chapter = loaded;
        _storyMayGoBack = true;
        _host.Notify(Lang.T($"Progress matched to the Chapter {loaded} save (backup saved)", $"Progress disesuaikan dengan save Chapter {loaded} (backup tersimpan)"));
        _host.Save();
        return true;

        void Tick(Objective o) { if (Progress.Done.Add(o.Id)) Progress.History.Add(o.Id); }
        void Untick(Objective o) { if (Progress.Done.Remove(o.Id)) Progress.History.Remove(o.Id); }
    }
}
