using System.IO;
namespace GameTracker;

/// <summary>
/// The story objective the game is on right now (the green "!" target), read live from memory.
///
/// The game keeps a table with every story objective of every chapter, in story order. Each row (an object)
/// holds, from +0x58, three strings: title key ("$str040_TOWN7_Chap04_EnterPlace"), description key (title +
/// "_d" / "_Done_d" ...) and a billboard sprite name. When an objective starts, the game appends an entry that
/// points at its row to one array (sub-objectives get their own array), and moves that pointer along as the
/// objective advances. Old entries stay, so the newest entry is the current objective (picked in MainWindow).
/// The arrays move when they grow. The localization table pairs keys with their English text. Rows, entries and
/// texts are found by signature: once per game launch, after a chapter change, and when the arrays move.
/// </summary>
public sealed partial class Ff7rChapterReader
{
    public string ObjectiveDebug { get; private set; } = "";

    /// <summary>Every objective of the chapter that some entry points at, from the last ReadObjective.</summary>
    public List<Objective> Candidates { get; } = new();

    /// <summary>Where each candidate came from: its entry address and the entry's parent (for the choice log).</summary>
    public List<(Objective Objective, long Slot, long Parent)> CandidateSlots { get; } = new();

    /// <summary>
    /// One row of the objective table. An objective has several rows (one per stage); an entry moves along them.
    /// Finished: main steps end on "..._990_d"; discoveries move past their first row "..._Mate_01_d" to
    /// "..._Mate_011_d" (cleared) and "..._Mate_012_d".
    /// </summary>
    public record Objective(long Row, int Order, string TitleKey, string DescKey, string? Title, string? Text)
    {
        public bool Finished => DescKey.EndsWith("_990_d") || DescKey.Contains("_Done")
            || (TitleKey.Contains("_Mate_") && DescKey != TitleKey + "_d");
    }

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
                _objectiveSearch ??= Scan(SafeFindObjectives);
            }
        }
        if (_objectiveRows is null)
        {
            _objectiveSearch ??= Scan(SafeFindObjectives);
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
            _objectiveSearch ??= Scan(SafeFindObjectives);
        }
        return best;
    }
    DateTime _lastNearbyScan, _lastSearch;
    // Reused by every window of every nearby scan (every 3 s on the UI thread) instead of a new 2 MB array each.
    byte[]? _nearWindow;

    void ScanNearEntries()
    {
        if (_objectiveRows is null || _objectiveSlots.Count == 0) return;
        var parents = _objectiveSlots.Select(s => ReadInt64(s - 8)).ToHashSet();
        var known = _objectiveSlots.ToHashSet();
        foreach (long window in _objectiveSlots.Select(s => s & ~0xFFFFFL).Distinct().Take(32).ToList())
        {
            var buf = _nearWindow ??= new byte[0x200000];
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

    (Dictionary<long, Objective>, List<long>) SafeFindObjectives(CancellationToken cancel)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = FindObjectives(cancel);
            File.WriteAllLines(Path.Combine(DataPaths.Logs, "objective-titles.txt"),
                result.Item1.Values.OrderBy(o => o.Row).Select(o => $"{o.TitleKey}\t{o.Title}").Distinct());
            File.AppendAllText(Path.Combine(DataPaths.Logs, "objective-search.log"),
                $"{DateTime.Now:HH:mm:ss} search {sw.Elapsed.TotalSeconds:F1}s {ObjectiveDebug}{Environment.NewLine}");
            return result;
        }
        // Cancelled by Detach: the game closed, nobody reads this result; leave the new session's debug text alone.
        catch (OperationCanceledException) { return (new(), new()); }
        catch (Exception e) { ObjectiveDebug = "error: " + e.Message; return (new(), new()); }
    }

    (Dictionary<long, Objective>, List<long>) FindObjectives(CancellationToken cancel)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = new System.Collections.Concurrent.ConcurrentBag<(long Row, string Title, string Desc)>();
        var texts = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        long stringReads = 0;

        ForEachChunk(cancel, (a, buf, length) =>
        {
            // Strings sit close together on the heap: read them through a small per-chunk block cache.
            var cache = new StringReader(this);
            for (int i = 0; i + 48 <= length; i += 8)
            {
                long p1 = BitConverter.ToInt64(buf, i);
                int l1 = BitConverter.ToInt32(buf, i + 8), m1 = BitConverter.ToInt32(buf, i + 12);
                if (p1 < 0x10000000000 || p1 > 0x7FF000000000 || l1 < 8 || l1 > 100 || m1 < l1 || m1 > 256) continue;
                long p2 = BitConverter.ToInt64(buf, i + 16);
                int l2 = BitConverter.ToInt32(buf, i + 24), m2 = BitConverter.ToInt32(buf, i + 28);
                if (p2 < 0x10000000000 || p2 > 0x7FF000000000 || l2 < 2 || m2 < l2 || m2 > 1024) continue;
                if (!cache.StartsWith(p1, "$str")) continue;
                string first = cache.Read(p1, l1);
                string second = cache.Read(p2, l2);

                // Objective row: title key, description key, then a billboard sprite.
                long p3 = BitConverter.ToInt64(buf, i + 32);
                int l3 = BitConverter.ToInt32(buf, i + 40);
                // The description key normally extends the title key, but not always: Chapter 5 has title
                // "$str050TNNL4_..." with description "$str050_TNNL4_..._d".
                if (second.StartsWith("$str") && second.EndsWith("_d") && second != first && l3 is > 8 and < 128 && cache.StartsWith(p3, "U_Com"))
                    rows.Add((a + i - 0x58, first, second));
                // Localization entry: key, then its English text.
                else if (!second.StartsWith("$") && !second.StartsWith("U_") && second.Length > 0)
                    texts.TryAdd(first, second);
            }
            Interlocked.Add(ref stringReads, cache.Reads);
        });
        long rowsMs = sw.ElapsedMilliseconds;

        // The table exists in more than one copy; order rows by position within their own copy.
        var byAddress = new Dictionary<long, Objective>();
        int order = 0;
        long previous = 0;
        foreach (var (row, title, desc) in rows.OrderBy(r => r.Row))
        {
            if (row - previous > 0x10000) { order = 0; } // gap: a new copy of the table starts
            previous = row;
            byAddress[row] = new Objective(row, order++, title, desc, texts.GetValueOrDefault(title), texts.GetValueOrDefault(desc));
        }
        // Entries pointing at a row; skip the table's own lists (several row pointers side by side).
        var found = new System.Collections.Concurrent.ConcurrentBag<long>();
        ForEachChunk(cancel, (a, buf, length) =>
        {
            for (int i = 8; i + 16 <= length; i += 8)
            {
                long p = BitConverter.ToInt64(buf, i);
                if (!byAddress.ContainsKey(p)) continue;
                if (byAddress.ContainsKey(BitConverter.ToInt64(buf, i - 8)) || byAddress.ContainsKey(BitConverter.ToInt64(buf, i + 8))) continue;
                if (found.Count < 100_000) found.Add(a + i);
            }
        });
        var slots = found.OrderBy(s => s).ToList();
        // Objective entries share a parent pointer just before the row pointer; keep only those groups.
        var parents = slots.GroupBy(s => ReadInt64(s - 8)).Where(g => g.Key > 0x10000000000 && g.Key < 0x7FF000000000 && g.Count() >= 2).ToList();
        var entries = parents.SelectMany(g => g).ToList();
        ObjectiveDebug = $"rows {byAddress.Count} ({rowsMs} ms, {stringReads} reads), slots {slots.Count} ({sw.ElapsedMilliseconds - rowsMs} ms), entries {entries.Count}, parents {string.Join(",", parents.Select(g => g.Count()))}";
        return (byAddress, entries.Count > 0 ? entries : slots);
    }

    /// <summary>
    /// Reads UTF-16 strings of the game through a cache of 64 KB blocks, so neighbouring strings cost one read.
    /// Not thread-safe: one per chunk.
    /// </summary>
    sealed class StringReader(Ff7rChapterReader reader)
    {
        readonly Dictionary<long, byte[]?> _blocks = new();
        public long Reads { get; private set; }

        public string Read(long address, int lengthWithNull)
        {
            int bytes = (lengthWithNull - 1) * 2;
            if (bytes <= 0) return "";
            var block = Block(address >> 16);
            int offset = (int)(address & 0xFFFF);
            if (block is not null && offset + bytes <= block.Length)
                return System.Text.Encoding.Unicode.GetString(block, offset, bytes);
            return reader.ReadUtf16(address, lengthWithNull); // crosses a block edge
        }

        public bool StartsWith(long address, string prefix) => Read(address, prefix.Length + 1) == prefix;

        byte[]? Block(long index)
        {
            if (_blocks.TryGetValue(index, out var block)) return block;
            if (_blocks.Count > 256) _blocks.Clear();
            block = new byte[0x10000];
            Reads++;
            if (!ReadProcessMemory(reader._handle, (IntPtr)(index << 16), block, block.Length, out _)) block = null;
            return _blocks[index] = block;
        }
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

    /// <summary>
    /// Calls back with every 4 MB chunk of the game's writable memory, a few chunks at a time on worker threads
    /// (callbacks must be thread-safe). Half the cores at most, at below-normal priority, so the game keeps its
    /// frame rate. The callback gets the chunk's length: only that much of the buffer is the chunk.
    /// </summary>
    void ForEachChunk(CancellationToken cancel, Action<long, byte[], int> visit)
    {
        var chunks = new List<(long Start, int Length)>();
        long address = 0;
        while (VirtualQueryEx(_handle, (IntPtr)address, out var mbi, (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryInfo>()) != 0)
        {
            long start = (long)mbi.BaseAddress, size = (long)mbi.RegionSize;
            address = start + size;
            if (mbi.State == 0x1000 && (mbi.Protect & 0x04) != 0 && (mbi.Protect & 0x100) == 0)
                for (long a = start; a < start + size; a += 1 << 22)
                    chunks.Add((a, (int)Math.Min(1 << 22, start + size - a)));
            if (address <= 0 || address >= 0x7FFFFFFFFFFF) break;
        }
        // One 4 MB buffer per worker, reused for all its chunks (a new one per chunk churned the large object heap).
        // A rented array may be longer than asked and the last chunk of a region shorter: pass the chunk's length.
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2), CancellationToken = cancel };
        Parallel.ForEach(chunks, options,
            () =>
            {
                var thread = Thread.CurrentThread;
                var before = thread.Priority;
                thread.Priority = ThreadPriority.BelowNormal;
                return (Buffer: System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 22), Thread: thread, Before: before);
            },
            (chunk, state, worker) =>
            {
                if (ReadProcessMemory(_handle, (IntPtr)chunk.Start, worker.Buffer, chunk.Length, out _)) visit(chunk.Start, worker.Buffer, chunk.Length);
                return worker;
            },
            worker =>
            {
                // Pool threads are shared: put the priority back (localInit and localFinally run on the same thread).
                worker.Thread.Priority = worker.Before;
                System.Buffers.ArrayPool<byte>.Shared.Return(worker.Buffer);
            });
    }
}
