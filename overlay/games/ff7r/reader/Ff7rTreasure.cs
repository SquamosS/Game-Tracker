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

    public sealed record Chest(string Id, float X, float Y, float Z, int[] Items);

    /// <summary>The chests of the loaded maps, swapped whole after a search (the UI thread reads it).</summary>
    public IReadOnlyList<Chest> Chests { get; private set; } = [];

    /// <summary>The chest tables the chests were built from, to rebuild only when another map loads.</summary>
    long[] _chestTablesFrom = [];

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
    /// Builds the chest list from the tables found by the objective search (worker thread), when the chest tables are
    /// not the ones it was built from. One extra pass over memory finds the points.
    /// </summary>
    void UpdateChests(List<(int Kind, long Table)> tables, CancellationToken cancel)
    {
        var chestTables = tables.Where(t => t.Kind == 4).Select(t => t.Table).Where(t => TableRows(t, 0xA0) is not null).OrderBy(t => t).ToArray();
        if (chestTables.SequenceEqual(_chestTablesFrom)) return;

        // Item code -> inventory id, from the three item tables.
        var ids = new Dictionary<string, int>();
        foreach (var (kind, table) in tables)
        {
            int stride = kind switch { 0 => 0x158, 1 => 0x288, 2 => 0x160, _ => 0 };
            if (stride == 0 || TableRows(table, stride) is not var (rows, count)) continue;
            for (int r = 0; r < count; r++)
                if (FNameAt(rows + (long)r * stride) is { Length: > 0 } code && ReadInt32(rows + (long)r * stride + 0x10) is > 0 and var id)
                    ids.TryAdd(code, id);
        }
        // Reward key -> item codes.
        var rewards = new Dictionary<string, List<string>>();
        foreach (var (_, table) in tables.Where(t => t.Kind == 3))
        {
            if (TableRows(table, 0x50) is not var (rows, count)) continue;
            for (int r = 0; r < count; r++)
            {
                long row = rows + (long)r * 0x50, list = ReadInt64(row + 0x28);
                int n = ReadInt32(row + 0x30);
                if (list == 0 || n is <= 0 or > 16) continue;
                rewards.TryAdd(FNameAt(row), Enumerable.Range(0, n).Select(k => FNameAt(list + k * 0x10)).ToList());
            }
        }
        // Chest rows: id, point, reward keys.
        var rowsFound = new List<(string Id, int Point, int[] Items)>();
        foreach (long table in chestTables)
        {
            if (TableRows(table, 0xA0) is not var (rows, count)) continue;
            for (int r = 0; r < count; r++)
            {
                long row = rows + (long)r * 0xA0;
                var items = Enumerable.Range(0, 8).Select(k => FNameAt(row + 0x38 + k * 8)).Where(k => k.StartsWith("rwr"))
                    .SelectMany(k => rewards.GetValueOrDefault(k) ?? []).Select(c => ids.GetValueOrDefault(c)).Where(id => id > 0).ToArray();
                rowsFound.Add((FNameAt(row), ReadInt32(row + 0x30), items));
            }
        }
        // Points: rows of FName (number 0), pointer, rotation, X, Y, Z, scale 1, 1, 1.
        var wanted = rowsFound.Select(r => r.Point).Where(p => p != 0).ToHashSet();
        var points = new System.Collections.Concurrent.ConcurrentDictionary<int, (float X, float Y, float Z)>();
        ForEachChunk(cancel, (a, buf, length) =>
        {
            for (int i = 0; i + 0x3C <= length; i += 8)
            {
                int name = BitConverter.ToInt32(buf, i);
                if (!wanted.Contains(name) || BitConverter.ToInt32(buf, i + 4) != 0) continue;
                if (BitConverter.ToSingle(buf, i + 0x30) != 1f || BitConverter.ToSingle(buf, i + 0x34) != 1f || BitConverter.ToSingle(buf, i + 0x38) != 1f) continue;
                float x = BitConverter.ToSingle(buf, i + 0x20), y = BitConverter.ToSingle(buf, i + 0x24), z = BitConverter.ToSingle(buf, i + 0x28);
                if (float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z)) points.TryAdd(name, (x, y, z));
            }
        });
        Chests = rowsFound.Where(r => points.ContainsKey(r.Point))
            .Select(r => { var p = points[r.Point]; return new Chest(r.Id, p.X, p.Y, p.Z, r.Items); }).ToList();
        _chestTablesFrom = chestTables;
    }
}
