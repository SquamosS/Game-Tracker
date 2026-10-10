namespace GameTracker;

/// <summary>
/// The chests of the loaded maps: where each one stands and what it holds, from the game's own data tables
/// (research\notes.md, "Peti harta &amp; isinya"; Steam 1.0.0.7).
///
/// All tables are data objects with their rows in a TArray at +0x38 (count at +0x40), each row starting with an FName
/// key (index, then number: "E_ARM" number 2003 is "E_ARM_2002") and, for the item tables, the inventory id at +0x10:
/// Item (vtable module+0x4CCCE48, rows 0x158), Equipment (+0x4CC3438, rows 0x288), Materia (+0x4CCA208, rows 0x160).
/// Reward (+0x4CE1290, rows 0x50) holds the item codes of each "rwr..." key in a TArray at +0x28 (elements 0x10, FName
/// first). Each map has an ObjectTreasure table (+0x4CD6488, rows 0xA0): +0x00 chest id, +0x30 its point's FName,
/// +0x38.. the reward keys. A point is a row of FName, 0, pointer, rotation, then X, Y, Z at +0x20 and scale 1, 1, 1 at
/// +0x30, found by its FName in one extra pass whenever the set of chest tables changes (another map loaded).
/// Checked in Ch8: the chest in front of Cloud on the rooftops was treasure0040 = it_atel = 3 = Ether.
///
/// Opened: each chest row names its flag at +0x18 ("stfTreasure_obt080_treasure0080"); the flag's row (FName, 0, module
/// pointer, int at +0x10) gives its number (0x207D), found in the same pass as the points. The game sets bit number + 0xA80
/// of the flag block (materia list + 0x40E00) in the live copy of the save data the moment the chest opens; the other copies
/// follow at the next save. Found 10 Oct 2026 with a full snapshot diff (Remedy treasure0080 = bit 0x2AFD), checked on Ether
/// treasure0110 (0x2B00), and it matches chests 0030-0070 opened earlier and Chapter 3's; unopened Talisman (0010) = 0.
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long ItemTableRva = 0x4CCCE48, EquipmentTableRva = 0x4CC3438, MateriaTableRva = 0x4CCA208, RewardTableRva = 0x4CE1290, ChestTableRva = 0x4CD6488;

    /// <summary>A chest flag's number + this = its bit in the save data's flag block.</summary>
    const int ChestFlagBit = 0xA80;

    long _chestFlagCopy, _chestCopyCandidate;
    DateTime _chestCopyNextTry, _chestFlagsRead;
    readonly byte[] _chestFlagBytes = new byte[FlagBytes];
    bool _chestFlagsOk;

    /// <summary>
    /// Whether the game has this chest opened: its bit in the live copy of the save data. Null when unknown (another
    /// version, the chest's flag not found, the live copy not told apart yet).
    /// </summary>
    public bool? ChestOpened(GameChest chest)
    {
        if (Version != "Steam 1.0.0.7" || chest.Flag is not { } flag || !ReadChestFlags()) return null;
        int bit = flag + ChestFlagBit;
        return bit >> 3 < FlagBytes ? (_chestFlagBytes[bit >> 3] >> (bit & 7) & 1) == 1 : null;
    }

    /// <summary>
    /// A save was loaded (or play started): which copy is live is not known any more. Chests read unknown (the learned
    /// fallback) until LiveCopy names the new one twice.
    /// </summary>
    public void ForgetChestCopy()
    {
        (_chestFlagCopy, _chestCopyCandidate, _chestCopyNextTry, _chestFlagsRead, _chestFlagsOk) = (0, 0, DateTime.MinValue, DateTime.MinValue, false);
        _chestProbe.Data = null;
    }

    /// <summary>
    /// The flag block of the live save copy, read at most once a second. Which copy is live is asked again every 30 s
    /// (10 s after no answer, e.g. paused): LiveCopy compares two reads a second apart. Another copy is taken only when it
    /// is named twice in a row, 2 s apart, so the buffer an autosave is writing (it changes most for a moment) is not.
    /// </summary>
    bool ReadChestFlags()
    {
        var now = DateTime.Now;
        if (now - _chestFlagsRead < TimeSpan.FromSeconds(1)) return _chestFlagsOk;
        _chestFlagsRead = now;
        if (!_lists.Any(l => l.Materia == _chestFlagCopy)) _chestFlagCopy = 0;
        if (_lists.Count > 0 && (_chestProbe.Pending || now >= _chestCopyNextTry))
        {
            var live = LiveCopy(_chestProbe);
            if (!_chestProbe.Pending)
            {
                _chestCopyNextTry = now + TimeSpan.FromSeconds(live is null ? 10 : 30);
                if (live is { } l && l.Materia != _chestFlagCopy)
                {
                    if (l.Materia == _chestCopyCandidate) (_chestFlagCopy, _chestCopyCandidate) = (l.Materia, 0);
                    else (_chestCopyCandidate, _chestCopyNextTry) = (l.Materia, now + TimeSpan.FromSeconds(2));
                }
                else if (live is not null) _chestCopyCandidate = 0;
            }
        }
        return _chestFlagsOk = _chestFlagCopy != 0
            && ReadProcessMemory(_handle, (IntPtr)(_chestFlagCopy + FlagStart), _chestFlagBytes, FlagBytes, out _);
    }

    /// <summary>
    /// Every chest row of the loaded maps, also those without a known position (so "only one chest holds it" counts
    /// them), swapped whole after a search (the UI thread reads it).
    /// </summary>
    public IReadOnlyList<GameChest> Chests { get; private set; } = [];

    /// <summary>The tables (addresses and row counts) the chests were built from, and whether that build was complete.</summary>
    string _chestTablesFrom = "";
    bool _chestsComplete;
    DateTime _chestsBuilt;

    /// <summary>The table vtables to collect during the objective search (none on another game version).</summary>
    (long Item, long Equipment, long Materia, long Reward, long Chest) TableVtables => Version == "Steam 1.0.0.7"
        ? ((long)_moduleBase + ItemTableRva, (long)_moduleBase + EquipmentTableRva, (long)_moduleBase + MateriaTableRva, (long)_moduleBase + RewardTableRva, (long)_moduleBase + ChestTableRva)
        : (0, 0, 0, 0, 0);

    /// <summary>An FName with its number, as the game writes it ("E_ARM" number 2003 = "E_ARM_2002").</summary>
    string FNameAt(long address)
    {
        string name = FName(ReadInt32(address));
        int number = ReadInt32(address + 4);
        return name.Length == 0 || number <= 0 ? name : $"{name}_{number - 1}";
    }

    /// <summary>The rows of a data table: (row address, count) when they look sane.</summary>
    (long Rows, int Count)? TableRows(long table, int stride)
    {
        long rows = ReadInt64(table + 0x38);
        int count = ReadInt32(table + 0x40);
        return rows == 0 || count is <= 0 or > 5000 || stride <= 0 ? null : (rows, count);
    }

    /// <summary>
    /// Builds the chest list from the tables found by the objective search (worker thread) when the tables changed (another
    /// map loaded), or every 2 minutes while the last build missed points or contents. One extra pass finds the points.
    /// </summary>
    void UpdateChests(List<(int Kind, long Table)> tables, CancellationToken cancel)
    {
        int Stride(int kind) => kind switch { 0 => 0x158, 1 => 0x288, 2 => 0x160, 3 => 0x50, _ => 0xA0 };
        var usable = tables.Where(t => TableRows(t.Table, Stride(t.Kind)) is not null).OrderBy(t => t.Table).ToList();
        string key = string.Join(",", usable.Select(t => $"{t.Kind}:{t.Table:X}:{ReadInt32(t.Table + 0x40)}"));
        if (key == _chestTablesFrom && (_chestsComplete || DateTime.Now - _chestsBuilt < TimeSpan.FromMinutes(2))) return;

        // Item code -> inventory id, from the three item tables.
        var ids = new Dictionary<string, int>();
        foreach (var (kind, table) in usable.Where(t => t.Kind <= 2))
        {
            var (rows, count) = TableRows(table, Stride(kind))!.Value;
            for (int r = 0; r < count; r++)
                if (FNameAt(rows + (long)r * Stride(kind)) is { Length: > 0 } code && ReadInt32(rows + (long)r * Stride(kind) + 0x10) is > 0 and var id)
                    ids.TryAdd(code, id);
        }
        // Reward key -> item codes.
        var rewards = new Dictionary<string, List<string>>();
        foreach (var (_, table) in usable.Where(t => t.Kind == 3))
        {
            var (rows, count) = TableRows(table, 0x50)!.Value;
            for (int r = 0; r < count; r++)
            {
                long row = rows + (long)r * 0x50, list = ReadInt64(row + 0x28);
                int n = ReadInt32(row + 0x30);
                if (list == 0 || n is <= 0 or > 16) continue;
                rewards.TryAdd(FNameAt(row), Enumerable.Range(0, n).Select(k => FNameAt(list + k * 0x10)).ToList());
            }
        }
        // Chest rows: id, point (FName index and number), reward keys -> ids.
        bool complete = true;
        var rowsFound = new List<(string Id, long Point, long Flag, int[] Items)>();
        foreach (var (_, table) in usable.Where(t => t.Kind == 4))
        {
            var (rows, count) = TableRows(table, 0xA0)!.Value;
            for (int r = 0; r < count; r++)
            {
                long row = rows + (long)r * 0xA0;
                var keys = Enumerable.Range(0, 8).Select(k => FNameAt(row + 0x38 + k * 8)).Where(k => k.StartsWith("rwr")).ToList();
                var items = keys.SelectMany(k => rewards.GetValueOrDefault(k) ?? []).Select(c => ids.GetValueOrDefault(c)).Where(id => id > 0).ToArray();
                if (keys.Count > 0 && items.Length == 0) complete = false;
                // The flag is taken only when it is named like one ("stfTreasure_obt080_treasure0080").
                long flagName = FName(ReadInt32(row + 0x18)).StartsWith("stfTreasure_") ? (uint)ReadInt32(row + 0x18) | (long)ReadInt32(row + 0x1C) << 32 : 0;
                rowsFound.Add((FNameAt(row), (uint)ReadInt32(row + 0x30) | (long)ReadInt32(row + 0x34) << 32, flagName, items));
            }
        }
        // Points: rows of FName (index, number), pointer, rotation, X, Y, Z, scale 1, 1, 1. A name found at two different
        // places is ambiguous and left without a position.
        var wanted = rowsFound.Select(r => r.Point).Where(p => p != 0).ToHashSet();
        var wantedFlags = rowsFound.Select(r => r.Flag).Where(f => f != 0).ToHashSet();
        var points = new System.Collections.Concurrent.ConcurrentDictionary<long, GamePosition?>();
        // Flag rows: FName (index, number), module pointer, flag number at +0x10. Two different numbers = ambiguous (-1).
        var flags = new System.Collections.Concurrent.ConcurrentDictionary<long, int>();
        long moduleStart = (long)_moduleBase, moduleEnd = moduleStart + 0x8000000;
        ForEachChunk(cancel, (a, buf, length) =>
        {
            for (int i = 0; i + 0x3C <= length; i += 8)
            {
                long name = (uint)BitConverter.ToInt32(buf, i) | (long)BitConverter.ToInt32(buf, i + 4) << 32;
                if (wantedFlags.Contains(name) && BitConverter.ToInt64(buf, i + 8) is var mp && mp >= moduleStart && mp < moduleEnd
                    && BitConverter.ToInt32(buf, i + 0x10) is > 0 and < 0x7000 and var number)
                    flags.AddOrUpdate(name, number, (_, old) => old == number ? old : -1);
                if (!wanted.Contains(name)) continue;
                if (BitConverter.ToSingle(buf, i + 0x30) != 1f || BitConverter.ToSingle(buf, i + 0x34) != 1f || BitConverter.ToSingle(buf, i + 0x38) != 1f) continue;
                var p = new GamePosition(BitConverter.ToSingle(buf, i + 0x20), BitConverter.ToSingle(buf, i + 0x24), BitConverter.ToSingle(buf, i + 0x28));
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) continue;
                points.AddOrUpdate(name, p, (_, old) => old == p ? old : null);
            }
        });
        // A number shared by more than two chests is not theirs (Ch14's obt110 rows mostly read 0x100); two may be one chest
        // in two chapters' tables (obt080/obt110 treasure2030 = 0x2089).
        int? FlagOf(long name) => flags.TryGetValue(name, out int n) && n > 0 ? n : null;
        var shared = rowsFound.Select(r => FlagOf(r.Flag)).OfType<int>().GroupBy(n => n).Where(g => g.Count() > 2).Select(g => g.Key).ToHashSet();
        var chests = rowsFound.Select(r => new GameChest(r.Id, points.GetValueOrDefault(r.Point), r.Items,
            FlagOf(r.Flag) is { } n && !shared.Contains(n) ? n : null)).ToList();
        if (chests.Any(c => c.At is null)) complete = false;
        // A search cancelled by Detach must not fill the list of a game that closed.
        cancel.ThrowIfCancellationRequested();
        Chests = chests;
        (_chestTablesFrom, _chestsComplete, _chestsBuilt) = (key, complete, DateTime.Now);
    }
}
