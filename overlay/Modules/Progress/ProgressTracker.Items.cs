namespace GameTracker;

/// <summary>Ticks item steps from the inventory and learns names of items the game did not know.</summary>
public sealed partial class ProgressTracker
{
    HashSet<(int, uint)>? _seenOwned;

    /// <summary>Last id seen in each inventory slot.</summary>
    readonly Dictionary<long, (int Id, int Count)> _slotIds = new();
    readonly long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    readonly List<(int Id, DateTime When)> _unknownNew = new();

    /// <summary>The inventory has been read once (the status line says so).</summary>
    public bool InventoryRead => _seenOwned is not null;

    /// <summary>Item ids obtained while the overlay runs: a chest holding one of them has been emptied.</summary>
    public HashSet<int> Obtained { get; } = [];

    /// <summary>
    /// Ticks guide steps when the game gives you the matching item, materia or disc. Only items obtained
    /// while the overlay runs count (except music discs, which are unique), since owning a Fire Materia
    /// from earlier says nothing about this chapter's one.
    /// </summary>
    bool FollowItems()
    {
        if (!InGame || Guide is null) return false;
        var owned = _reader.ReadOwned();
        if (owned is null) return false;
        bool changed = false;
        if (_seenOwned is null)
        {
            _seenOwned = owned.Select(o => (o.Id, o.Obtained)).ToHashSet();
            foreach (var o in owned) _slotIds[o.Slot] = (o.Id, o.Count);
            foreach (var o in owned)
                if (_names.Name(o.Id) is { } disc)
                    foreach (var step in Guide.Chapters.SelectMany(c => c.Objectives))
                        if (_rules.TypeOf(step).Unique && _rules.Matches(step, disc) && Progress.Done.Add(step.Id)) changed = true;
            if (changed) _host.Save();
            return true;
        }
        // Only things obtained since the overlay started: older records come from save-buffer copies of other saves.
        // New: a record obtained since the overlay started, or an id put into a slot that held something else
        // (the game reuses slots, and a reused slot keeps its old time). Many slots changing at once is a save
        // being loaded or copied, not items being handed over.
        // A stack growing counts too: a chest with a fifth Bulletproof Vest only raises the count.
        var changedSlots = owned.Where(o => o.Id > 0 && _slotIds.TryGetValue(o.Slot, out var before)
                && (before.Id != o.Id || o.Count > before.Count))
            .Select(o => o.Slot).ToHashSet();
        foreach (var o in owned) _slotIds[o.Slot] = (o.Id, o.Count);
        bool handedOver = changedSlots.Count is > 0 and <= 3;
        // Items handed over in front of a chest that holds them: that chest is opened now (ChestOpened).
        if (handedOver) _host.ItemsHandedOver(owned.Where(o => changedSlots.Contains(o.Slot) && o.Id > 0).Select(o => o.Id).ToHashSet());
        if (changedSlots.Count > 3) { _reconcile = true; _loadedSlots = changedSlots; ForgetRecent(); _reader.ForgetChestCopy(); _reader.ForgetSideQuests(); } // a save was loaded (or copied)
        bool IsNew(OwnedItem o) =>
            (_seenOwned.Add((o.Id, o.Obtained)) && o.Obtained >= _startedAt - 120) | (handedOver && changedSlots.Contains(o.Slot));
        var newItems = owned.Where(o => o.Id > 0 && !_names.IsCurrency(o.Id)).Where(IsNew).ToList();
        // A new item may sit in a slot that was empty (the end of the list: key items, the Graveyard Key), so not in
        // changedSlots: log what is new, unless a save was loaded.
        if (changedSlots.Count <= 3) _host.LogItems(newItems.Concat(owned.Where(o => handedOver && changedSlots.Contains(o.Slot) && o.Id > 0)).DistinctBy(o => o.Slot));
        foreach (var o in newItems)
        {
            // Consumables (potions...) are never guide steps, so they are not learned. Neither is anything that came
            // with a loaded save.
            Obtained.Add(o.Id); // its chest, if any, is empty now: no distance to it any more
            if (_names.Name(o.Id) is not { } name) { if (!_names.IsConsumable(o.Id) && changedSlots.Count <= 3) _unknownNew.Add((o.Id, DateTime.Now)); continue; }
            var step = StepFor(name);
            if (step is null || !Progress.Done.Add(step.Id)) continue;
            Progress.History.Add(step.Id);
            _host.Save();
            _host.Notify(Lang.T($"Auto-ticked: {step.Name}", $"Otomatis dicentang: {step.Name}"));
            changed = true;
        }
        return changed;
    }

    /// <summary>The open step this item belongs to: the current one first, then ones left behind.</summary>
    Objective? StepFor(string name)
    {
        var objectives = CurrentChapter?.Objectives ?? [];
        int next = CurrentStory is { } story ? Array.IndexOf(objectives, story) : objectives.Length;
        int due = Array.FindIndex(objectives, next + 1, o => _rules.IsStory(o));
        var open = objectives.Select((o, i) => (o, i))
            .Where(x => !_rules.IsStory(x.o) && !Progress.Done.Contains(x.o.Id) && _rules.Matches(x.o, name)).ToList();
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
        // Learn only when a single unknown of the right kind for this step came in.
        var ids = _unknownNew.Where(u => _names.FitsStep(u.Id, step.Type)).Select(u => u.Id).Distinct().ToList();
        if (ids.Count == 0) return;
        if (ids.Count > 1) { _host.Notify(Lang.T("Not learned: more than one new item", "Tidak dipelajari: lebih dari satu item baru")); return; }
        int id = ids[0];
        _unknownNew.RemoveAll(u => u.Id == id);
        _names.Learn(id, step.Name);
        _host.Notify(Lang.T($"Learned: item {id} = {step.Name}", $"Dipelajari: item {id} = {step.Name}"));
    }

    /// <summary>Whether an item step not yet done follows the current story step (before the next one).</summary>
    bool ExpectingItem() => CurrentChapter is { } chapter && CurrentStory is { } story
        && chapter.Objectives.SkipWhile(o => o != story).Skip(1).TakeWhile(o => !_rules.IsStory(o))
            .Any(o => _rules.IsItem(o) && !Progress.Done.Contains(o.Id));
}
