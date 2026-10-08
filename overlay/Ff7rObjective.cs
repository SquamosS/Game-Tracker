using System.IO;
namespace GameTracker;

/// <summary>
/// The story objective the game is on right now (the green "!" target), read live from memory.
///
/// The game keeps a table with every story objective of every chapter, in story order. Each row (an object)
/// holds, from +0x58, three strings: title key ("$str040_TOWN7_Chap04_EnterPlace"), description key (title +
/// "_d" / "_Done_d" ...) and a billboard sprite name. When an objective starts, the game adds an entry that
/// points at its row, and moves that pointer along as sub-objectives advance. So the current objective is the
/// furthest row in the current chapter that any such entry points at. The localization table pairs keys with
/// their English text. Rows, entries and texts are found by signature once per game launch.
/// </summary>
public sealed partial class Ff7rChapterReader
{
    public string ObjectiveDebug { get; private set; } = "";

    /// <summary>Every objective of the chapter that some entry points at, from the last ReadObjective.</summary>
    public List<Objective> Candidates { get; } = new();

    /// <summary>Where each candidate came from: its entry address and the entry's parent (for the choice log).</summary>
    public List<(Objective Objective, long Slot, long Parent)> CandidateSlots { get; } = new();

    public record Objective(long Row, int Order, string TitleKey, string DescKey, string? Title, string? Text);

    Dictionary<long, Objective>? _objectiveRows;
    List<long> _objectiveSlots = new();
    Task<(Dictionary<long, Objective>, List<long>)>? _objectiveSearch;
    readonly Dictionary<long, (long Row, long Seen)> _slotRows = new();
    readonly Dictionary<long, long> _slotParents = new();
    long _changes;
    int _questCounter = int.MinValue;

    /// <summary>
    /// The current story objective, or null while it is still being looked for. The entry that moved last wins;
    /// right after loading, when nothing has moved yet, the last entry in the array (the newest objective) does.
    /// </summary>
    public Objective? ReadObjective(int chapter)
    {
        if (!Attach()) return null;
        if (_objectiveSearch is { IsCompleted: true })
        {
            bool first = _objectiveRows is null;
            (_objectiveRows, _objectiveSlots) = _objectiveSearch.Result;
            _objectiveSearch = null;
            foreach (long slot in _objectiveSlots)
            {
                if (!_slotRows.ContainsKey(slot)) _slotRows[slot] = (ReadInt64(slot), first ? 0 : ++_changes);
                _slotParents[slot] = ReadInt64(slot - 8);
            }
        }
        // The save data's event counter goes up whenever the objective changes: new entries may exist, look again.
        if (_lists.Count > 0)
        {
            var (materia, _) = _lists.MaxBy(l => (uint)ReadInt32(l.Gil));
            int counter = ReadInt32(materia - 0x1EA0);
            if (counter != _questCounter)
            {
                _questCounter = counter;
                _objectiveSearch ??= Task.Run(SafeFindObjectives);
            }
        }
        if (_objectiveRows is null)
        {
            _objectiveSearch ??= Task.Run(SafeFindObjectives);
            return null;
        }

        // New objectives get a new entry next to the existing ones: look around them every few seconds.
        if (DateTime.Now - _lastNearbyScan > TimeSpan.FromSeconds(3))
        {
            _lastNearbyScan = DateTime.Now;
            ScanNearEntries();
        }

        Candidates.Clear();
        CandidateSlots.Clear();
        Objective? best = null;
        long bestSeen = -1, bestSlot = 0;
        bool stale = false;
        foreach (long slot in _objectiveSlots)
        {
            long row = ReadInt64(slot);
            // An entry whose parent changed was freed: the game moved the array (it grew). Search again.
            if (_slotParents.TryGetValue(slot, out long parent) && ReadInt64(slot - 8) != parent) { stale = true; continue; }
            var (before, seen) = _slotRows.GetValueOrDefault(slot);
            if (row != before) _slotRows[slot] = (row, seen = ++_changes);
            if (!_objectiveRows.TryGetValue(row, out var objective) || !InChapter(objective, chapter)) continue;
            // Entries sit in one array in the order objectives started, so a later slot is the newer objective.
            Candidates.Add(objective);
            CandidateSlots.Add((objective, slot, ReadInt64(slot - 8)));
            if (seen > bestSeen || (seen == bestSeen && slot > bestSlot)) (best, bestSeen, bestSlot) = (objective, seen, slot);
        }
        // Only the chapter's title row (or nothing) means the chapter's steps were not loaded at the last search,
        // as right after a chapter change: search again, at most every 20 seconds.
        if ((stale || best is null || Candidates.All(c => c.TitleKey.EndsWith("_Parent") || c.TitleKey.EndsWith("_Parent02")))
            && DateTime.Now - _lastSearch > TimeSpan.FromSeconds(20))
        {
            _lastSearch = DateTime.Now;
            _objectiveSearch ??= Task.Run(SafeFindObjectives);
        }
        return best;
    }
    DateTime _lastNearbyScan, _lastSearch;

    void ScanNearEntries()
    {
        if (_objectiveRows is null || _objectiveSlots.Count == 0) return;
        var parents = _objectiveSlots.Select(s => ReadInt64(s - 8)).ToHashSet();
        var known = _objectiveSlots.ToHashSet();
        foreach (long window in _objectiveSlots.Select(s => s & ~0xFFFFFL).Distinct().Take(32).ToList())
        {
            var buf = new byte[0x200000];
            if (!ReadProcessMemory(_handle, (IntPtr)(window - 0x80000), buf, buf.Length, out _)) continue;
            for (int i = 8; i + 8 <= buf.Length; i += 8)
            {
                long slot = window - 0x80000 + i;
                if (known.Contains(slot) || !_objectiveRows.ContainsKey(BitConverter.ToInt64(buf, i))) continue;
                if (!parents.Contains(BitConverter.ToInt64(buf, i - 8))) continue;
                _objectiveSlots.Add(slot);
                known.Add(slot);
                _slotRows[slot] = (BitConverter.ToInt64(buf, i), ++_changes);
                _slotParents[slot] = BitConverter.ToInt64(buf, i - 8);
            }
        }
    }

    static bool InChapter(Objective row, int chapter) => ChapterOf(row.TitleKey) == chapter;

    /// <summary>
    /// The chapter a title key belongs to. Keys name a chapter ("_Chap04_", "_Chapter05_"), but from Chapter 8 on
    /// the number is one ahead ("_Chapter09_" is Ch. 8), and "_Chapter14_" covers both Ch. 13 and Ch. 14.
    /// Chapter 3 side quests ("$str030_SLUM7_qst...") carry no number.
    /// </summary>
    public static int? ChapterOf(string key)
    {
        if (key.StartsWith("$str030_SLUM7_qst")) return 3;
        var m = System.Text.RegularExpressions.Regex.Match(key, @"_Chap(?:ter)?(\d+)_");
        if (!m.Success) return null;
        int n = int.Parse(m.Groups[1].Value);
        if (n is >= 9 and <= 13) return n - 1;
        if (n != 14) return n;
        // Ch. 13: the rubble and Sector 7 (str120); Ch. 14: the rest, including its side quests.
        if (key.StartsWith("$str120_")) return 13;
        bool thirteen = key.StartsWith("$str110_") && System.Text.RegularExpressions.Regex.IsMatch(key,
            @"_Chapter14_(Parent|Start|toSlum5|toPark)(_sub0[13])?$");
        return thirteen ? 13 : 14;
    }

    (Dictionary<long, Objective>, List<long>) SafeFindObjectives()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = FindObjectives();
            File.WriteAllLines(Path.Combine(DataPaths.Logs, "objective-titles.txt"),
                result.Item1.Values.OrderBy(o => o.Row).Select(o => $"{o.TitleKey}\t{o.Title}").Distinct());
            File.AppendAllText(Path.Combine(DataPaths.Logs, "objective-search.log"),
                $"{DateTime.Now:HH:mm:ss} search {sw.Elapsed.TotalSeconds:F1}s {ObjectiveDebug}{Environment.NewLine}");
            return result;
        }
        catch (Exception e) { ObjectiveDebug = "error: " + e.Message; return (new(), new()); }
    }

    (Dictionary<long, Objective>, List<long>) FindObjectives()
    {
        var rows = new List<(long Row, string Title, string Desc)>();
        var texts = new Dictionary<string, string>();

        ForEachChunk((a, buf) =>
        {
            for (int i = 0; i + 48 <= buf.Length; i += 8)
            {
                long p1 = BitConverter.ToInt64(buf, i);
                int l1 = BitConverter.ToInt32(buf, i + 8), m1 = BitConverter.ToInt32(buf, i + 12);
                if (p1 < 0x10000000000 || p1 > 0x7FF000000000 || l1 < 8 || l1 > 100 || m1 < l1 || m1 > 256) continue;
                long p2 = BitConverter.ToInt64(buf, i + 16);
                int l2 = BitConverter.ToInt32(buf, i + 24), m2 = BitConverter.ToInt32(buf, i + 28);
                if (p2 < 0x10000000000 || p2 > 0x7FF000000000 || l2 < 2 || m2 < l2 || m2 > 1024) continue;
                string first = ReadUtf16(p1, l1);
                if (!first.StartsWith("$str")) continue;
                string second = ReadUtf16(p2, l2);

                // Objective row: title key, description key starting with the title, then a billboard sprite.
                long p3 = BitConverter.ToInt64(buf, i + 32);
                int l3 = BitConverter.ToInt32(buf, i + 40);
                // The description key normally extends the title key, but not always: Chapter 5 has title
                // "$str050TNNL4_..." with description "$str050_TNNL4_..._d".
                if (second.StartsWith("$str") && second.EndsWith("_d") && second != first && l3 is > 8 and < 128 && ReadUtf16(p3, l3).StartsWith("U_Com"))
                    rows.Add((a + i - 0x58, first, second));
                // Localization entry: key, then its English text.
                else if (!second.StartsWith("$") && !second.StartsWith("U_") && second.Length > 0)
                    texts.TryAdd(first, second);
            }
        });

        // The table exists in more than one copy; order rows by position within their own copy.
        var byAddress = new Dictionary<long, Objective>();
        int order = 0;
        long previous = 0;
        foreach (var (row, title, desc) in rows.OrderBy(r => r.Row))
        {
            if (row - previous > 0x10000) order = 0; // gap: a new copy of the table starts
            previous = row;
            byAddress[row] = new Objective(row, order++, title, desc, texts.GetValueOrDefault(title), texts.GetValueOrDefault(desc));
        }

        // Entries pointing at a row; skip the table's own lists (several row pointers side by side).
        var slots = new List<long>();
        ForEachChunk((a, buf) =>
        {
            for (int i = 8; i + 16 <= buf.Length; i += 8)
            {
                long p = BitConverter.ToInt64(buf, i);
                if (!byAddress.ContainsKey(p)) continue;
                if (byAddress.ContainsKey(BitConverter.ToInt64(buf, i - 8)) || byAddress.ContainsKey(BitConverter.ToInt64(buf, i + 8))) continue;
                if (slots.Count < 100_000) slots.Add(a + i);
            }
        });
        // Objective entries share a parent pointer just before the row pointer; keep only those groups.
        var parents = slots.GroupBy(s => ReadInt64(s - 8)).Where(g => g.Key > 0x10000000000 && g.Key < 0x7FF000000000 && g.Count() >= 2);
        var entries = parents.SelectMany(g => g).ToList();
        ObjectiveDebug = $"rows {byAddress.Count}, slots {slots.Count}, entries {entries.Count}, parents {string.Join(",", parents.Select(g => g.Count()))}";
        return (byAddress, entries.Count > 0 ? entries : slots);
    }

    long ReadInt64(long address)
    {
        var buffer = new byte[8];
        return ReadProcessMemory(_handle, (IntPtr)address, buffer, 8, out _) ? BitConverter.ToInt64(buffer) : 0;
    }

    string ReadUtf16(long address, int lengthWithNull)
    {
        var buffer = new byte[(lengthWithNull - 1) * 2];
        return ReadProcessMemory(_handle, (IntPtr)address, buffer, buffer.Length, out _)
            ? System.Text.Encoding.Unicode.GetString(buffer) : "";
    }

    /// <summary>Calls back with every 4 MB chunk of the game's writable memory.</summary>
    void ForEachChunk(Action<long, byte[]> visit)
    {
        long address = 0;
        while (VirtualQueryEx(_handle, (IntPtr)address, out var mbi, (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryInfo>()) != 0)
        {
            long start = (long)mbi.BaseAddress, size = (long)mbi.RegionSize;
            address = start + size;
            if (mbi.State == 0x1000 && (mbi.Protect & 0x04) != 0 && (mbi.Protect & 0x100) == 0)
                for (long a = start; a < start + size; a += 1 << 22)
                {
                    var buf = new byte[(int)Math.Min(1 << 22, start + size - a)];
                    if (ReadProcessMemory(_handle, (IntPtr)a, buf, buf.Length, out _)) visit(a, buf);
                }
            if (address <= 0 || address >= 0x7FFFFFFFFFFF) break;
        }
    }
}
