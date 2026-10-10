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
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long ItemTableRva = 0x4CCCE48, EquipmentTableRva = 0x4CC3438, MateriaTableRva = 0x4CCA208, RewardTableRva = 0x4CE1290, ChestTableRva = 0x4CD6488;

    /// <summary>A chest: its id, where it stands (null when its point was not found or is ambiguous) and the ids it holds.</summary>
    public sealed record Chest(string Id, Position? At, int[] Items);

    /// <summary>
    /// Every chest row of the loaded maps, also those without a known position (so "only one chest holds it" counts
    /// them), swapped whole after a search (the UI thread reads it).
    /// </summary>
    public IReadOnlyList<Chest> Chests { get; private set; } = [];

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
        var rowsFound = new List<(string Id, long Point, int[] Items)>();
        foreach (var (_, table) in usable.Where(t => t.Kind == 4))
        {
            var (rows, count) = TableRows(table, 0xA0)!.Value;
            for (int r = 0; r < count; r++)
            {
                long row = rows + (long)r * 0xA0;
                var keys = Enumerable.Range(0, 8).Select(k => FNameAt(row + 0x38 + k * 8)).Where(k => k.StartsWith("rwr")).ToList();
                var items = keys.SelectMany(k => rewards.GetValueOrDefault(k) ?? []).Select(c => ids.GetValueOrDefault(c)).Where(id => id > 0).ToArray();
                if (keys.Count > 0 && items.Length == 0) complete = false;
                rowsFound.Add((FNameAt(row), (uint)ReadInt32(row + 0x30) | (long)ReadInt32(row + 0x34) << 32, items));
            }
        }
        // Points: rows of FName (index, number), pointer, rotation, X, Y, Z, scale 1, 1, 1. A name found at two different
        // places is ambiguous and left without a position.
        var wanted = rowsFound.Select(r => r.Point).Where(p => p != 0).ToHashSet();
        var points = new System.Collections.Concurrent.ConcurrentDictionary<long, Position?>();
        ForEachChunk(cancel, (a, buf, length) =>
        {
            for (int i = 0; i + 0x3C <= length; i += 8)
            {
                long name = (uint)BitConverter.ToInt32(buf, i) | (long)BitConverter.ToInt32(buf, i + 4) << 32;
                if (!wanted.Contains(name)) continue;
                if (BitConverter.ToSingle(buf, i + 0x30) != 1f || BitConverter.ToSingle(buf, i + 0x34) != 1f || BitConverter.ToSingle(buf, i + 0x38) != 1f) continue;
                var p = new Position(BitConverter.ToSingle(buf, i + 0x20), BitConverter.ToSingle(buf, i + 0x24), BitConverter.ToSingle(buf, i + 0x28));
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) continue;
                points.AddOrUpdate(name, p, (_, old) => old == p ? old : null);
            }
        });
        var chests = rowsFound.Select(r => new Chest(r.Id, points.GetValueOrDefault(r.Point), r.Items)).ToList();
        if (chests.Any(c => c.At is null)) complete = false;
        // A search cancelled by Detach must not fill the list of a game that closed.
        cancel.ThrowIfCancellationRequested();
        Chests = chests;
        (_chestTablesFrom, _chestsComplete, _chestsBuilt) = (key, complete, DateTime.Now);
    }
}
