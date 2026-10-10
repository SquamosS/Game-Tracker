using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameTracker;

/// <summary>
/// Reads the current chapter number from FF7 Remake's memory (read-only), the same way LiveSplit's
/// autosplitter does. Offsets come from https://github.com/Mysterion06/FF7RSplitter (FF7R.asl).
/// </summary>
public sealed partial class Ff7rChapterReader : IDisposable
{
    const string ProcessName = "ff7remake_";
    const uint PROCESS_VM_READ = 0x10, PROCESS_QUERY_INFORMATION = 0x400;

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out nint read);
    // Single values straight into a local: no array per read (several hundred reads a second while playing).
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr process, IntPtr address, out long value, int size, out nint read);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr process, IntPtr address, out int value, int size, out nint read);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr process, IntPtr address, out byte value, int size, out nint read);

    Process? _process;
    IntPtr _handle;
    IntPtr _chapterAddress;
    IntPtr _moduleBase;
    DateTime _retryAt;

    /// <summary>"Steam 1.0.0.7" etc. once attached, null while the game is not running.</summary>
    public string? Version { get; private set; }

    /// <summary>Set when the game runs but cannot be read (unknown version, no access).</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// The chapter the game is in: 1–18 for the main story, 21–22 for INTERmission.
    /// Null when the game is not running, in a menu, or between chapters (the game stores 255 then).
    /// </summary>
    public int? ReadChapter()
    {
        if (!Attach()) return null;
        int chapter = ReadByte(_chapterAddress);
        return chapter is >= 1 and <= 18 or 21 or 22 ? chapter : null;
    }

    // The item list (records of 0x18 bytes: obtained time, id, count, category) and the materia list
    // (records of 0x20 bytes: obtained time, slot, level/flags, id) sit 0x33630 bytes apart in the save data.
    // No pointer chain to them survived a game restart, so they are found by signature once per launch.
    // The game keeps several copies (save buffers); all are read and records are told apart by obtained time.
    const int ItemsToGil = 0x33630;
    const int EquipmentBytes = 0x2000;
    List<(long Materia, long Gil)> _lists = new();
    Task<List<(long, long)>>? _search;
    DateTime _listsFound;

    /// <summary>How often to look for new copies of the save data. A full look takes ~10 s of one core.</summary>
    public TimeSpan ListRefresh { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Look for new copies right away (the objective changed: rewards often come with it).</summary>
    public void RefreshListsSoon() => _listsFound = DateTime.MinValue;

    /// <summary>One inventory record; Slot is its address, which keeps holding the same id unless something new is put there.</summary>
    public record Owned(int Id, int Count, uint Obtained, long Slot = 0);

    /// <summary>Everything in the item and materia lists, or null while they are still being looked for.</summary>
    /// <summary>
    /// Ids in the copy of the save data the loaded save went to, unlike ReadOwned, which merges every copy: the
    /// copy holding most of the slots that changed at the load, else the one whose gil changed last. Null while
    /// the lists are still being looked for.
    /// </summary>
    public HashSet<int>? ReadLiveOwnedIds(IReadOnlyCollection<long> changedSlots)
    {
        if (ReadOwned() is null || _lists.Count == 0) return null;
        static bool Holds((long Materia, long Gil) l, long slot) => slot >= l.Materia - 0x40000 && slot < l.Gil + 0x10000;
        (long Materia, long Gil) live;
        if (changedSlots.Count > 0 && _lists.Any(l => changedSlots.Any(s => Holds(l, s))))
            live = _lists.MaxBy(l => changedSlots.Count(s => Holds(l, s)));
        else if (LiveCopy() is { } copy) live = copy;
        else return null; // asked again on the next poll
        var (materia, gil) = live;
        var ids = new HashSet<int>();
        long items = ItemsStart(gil);
        foreach (var o in ReadRecords(items, 0x18, 600, (b, slot) => new Owned(BitConverter.ToInt32(b[8..]), BitConverter.ToInt32(b[12..]), 0, slot)))
            if (o.Id > 0 && o.Count > 0) ids.Add(o.Id);
        foreach (var o in ReadRecords(materia, 0x20, 600, (b, slot) => new Owned(BitConverter.ToInt32(b[20..]), 1, 0, slot)))
            if (o.Id > 0) ids.Add(o.Id);
        foreach (var o in ReadRecords(materia - EquipmentBytes, 0x10, EquipmentBytes / 0x10, (b, slot) =>
                     (BitConverter.ToInt32(b[0..]) & 0xFF) is 1 or 2 ? new Owned(BitConverter.ToInt32(b[4..]), 1, 0, slot) : new Owned(0, 0, 0, slot)))
            if (o.Id is >= 1000 and < 10000) ids.Add(o.Id);
        return ids;
    }

    Dictionary<long, byte[]>? _probe;
    DateTime _probeAt;

    /// <summary>
    /// The copy of the save data the game is playing on: other copies (save buffers) stay still, the live one keeps
    /// changing (play time, position...). Compares two reads at least a second apart; null until the second read.
    /// </summary>
    (long Materia, long Gil)? LiveCopy()
    {
        byte[] Read((long Materia, long Gil) l)
        {
            var buffer = new byte[(int)(l.Gil - l.Materia + EquipmentBytes + 0x1000)];
            ReadProcessMemory(_handle, (IntPtr)(l.Materia - EquipmentBytes), buffer, buffer.Length, out _);
            return buffer;
        }
        if (_probe is null || _probe.Count != _lists.Count || !_lists.All(l => _probe.ContainsKey(l.Materia)))
        {
            _probe = _lists.ToDictionary(l => l.Materia, Read);
            _probeAt = DateTime.Now;
            return null;
        }
        if (DateTime.Now - _probeAt < TimeSpan.FromSeconds(1)) return null;
        int Changes((long Materia, long Gil) l)
        {
            var now = Read(l); var before = _probe[l.Materia]; int n = 0;
            for (int i = 0; i < Math.Min(now.Length, before.Length); i++) if (now[i] != before[i]) n++;
            return n;
        }
        var live = _lists.MaxBy(Changes);
        _probe = null;
        return live;
    }

    public List<Owned>? ReadOwned()
    {
        if (!Attach()) return null;
        // A failed search counts as finding nothing (looked for again below); its Result would throw on every poll.
        if (_search is { IsCompleted: true }) { _lists = _search.IsCompletedSuccessfully ? _search.Result : new(); _search = null; _listsFound = DateTime.Now; }
        // The game makes new copies of the save data (autosave, chapter change): look for new lists now and then.
        if (_lists.Count > 0 && DateTime.Now - _listsFound > ListRefresh) _search ??= Scan(FindLists);
        _lists.RemoveAll(l => ReadInt32(l.Gil + 8) != 20);
        if (_lists.Count == 0)
        {
            _search ??= Scan(FindLists);
            return null;
        }
        var owned = new List<Owned>();
        foreach (var (materia, gil) in _lists)
        {
            long items = ItemsStart(gil);
            owned.AddRange(ReadRecords(items, 0x18, 600, (b, slot) => new Owned(BitConverter.ToInt32(b[8..]), BitConverter.ToInt32(b[12..]), BitConverter.ToUInt32(b[0..]), slot)));
            owned.AddRange(ReadRecords(materia, 0x20, 600, (b, slot) => new Owned(BitConverter.ToInt32(b[20..]), 1, BitConverter.ToUInt32(b[0..]), slot)));
            // Weapons (and other equipment) sit in a list of 0x10-byte records {kind 1/2, id} just before the
            // materia list (Metal Knuckles 3002 at materia - 0xF50); new ones are appended into empty records.
            owned.AddRange(ReadRecords(materia - EquipmentBytes, 0x10, EquipmentBytes / 0x10, (b, slot) =>
                BitConverter.ToInt32(b[0..]) is var kind && (kind & 0xFF) is 1 or 2 && kind >> 16 == 0
                    && BitConverter.ToInt32(b[4..]) is var id && id is >= 1000 and < 10000
                    ? new Owned(id, 1, 0, slot) : new Owned(0, 0, 0, slot)));
        }
        return owned;
    }

    /// <summary>
    /// Bit flags the game sets when quests and story events complete, from the copy of the save data that
    /// changed last (its gil record has the newest time). Key: "offset:bit" relative to materia list + 0x40E00.
    /// Chapter 3 side quests sit in one run of bits: +0x4C0 bits 29-31, +0x4C4 bits 0-2.
    /// </summary>
    public HashSet<string>? ReadFlags()
    {
        if (!Attach() || _lists.Count == 0) return null;
        var (materia, _) = _lists.MaxBy(l => (uint)ReadInt32(l.Gil));
        var buffer = new byte[FlagBytes];
        if (!ReadProcessMemory(_handle, (IntPtr)(materia + FlagStart), buffer, buffer.Length, out _)) return null;
        var events = new byte[EventBytes];
        bool eventsRead = ReadProcessMemory(_handle, (IntPtr)(materia + EventStart), events, events.Length, out _);
        // Flags rarely change: the same bytes give the same set (callers only read it), without rebuilding thousands of strings.
        if (_flagsFrom == materia && _flagSet is not null && eventsRead == _flagEventsRead
            && buffer.AsSpan().SequenceEqual(_flagBytes) && events.AsSpan().SequenceEqual(_flagEvents)) return _flagSet;
        var set = new HashSet<string>();
        for (int i = 0; i < buffer.Length; i += 4)
        {
            uint word = BitConverter.ToUInt32(buffer, i);
            for (int bit = 0; bit < 32; bit++)
                if ((word & (1u << bit)) != 0) set.Add($"{i:X}:{bit}");
        }
        // Log of side-quest step events (one id per step, e.g. "found cat 2"), kept in the save; -1 = empty slot.
        if (eventsRead)
            for (int i = 0; i < events.Length; i += 4)
                if (BitConverter.ToInt32(events, i) is var id && id > 0) set.Add($"E:{id}");
        (_flagsFrom, _flagBytes, _flagEvents, _flagEventsRead, _flagSet) = (materia, buffer, events, eventsRead, set);
        return set;
    }

    long _flagsFrom;
    byte[] _flagBytes = [], _flagEvents = [];
    bool _flagEventsRead;
    HashSet<string>? _flagSet;

    const int FlagStart = 0x40E00, FlagBytes = 0x1000, EventStart = 0xE9A4, EventBytes = 0x400;

    /// <summary>Scans writable memory for materia lists (6+ consecutive materia records) with the gil record at the known distance.</summary>
    List<(long, long)> FindLists(CancellationToken cancel)
    {
        var found = new List<(long, long)>();
        // One 4 MB buffer for the whole scan (a new one per chunk churned the large object heap). Chunks at the
        // end of a region are shorter and a rented array may be longer: only the chunk's length is scanned.
        var buf = ArrayPool<byte>.Shared.Rent(1 << 22);
        try
        {
            long address = 0;
            while (VirtualQueryEx(_handle, (IntPtr)address, out var mbi, (uint)Marshal.SizeOf<MemoryInfo>()) != 0)
            {
                long start = (long)mbi.BaseAddress, size = (long)mbi.RegionSize;
                address = start + size;
                if (mbi.State != 0x1000 || (mbi.Protect & 0x04) == 0 || (mbi.Protect & 0x100) != 0) continue;
                for (long a = start; a < start + size; a += 1 << 22)
                {
                    cancel.ThrowIfCancellationRequested();
                    int length = (int)Math.Min(1 << 22, start + size - a);
                    if (!ReadProcessMemory(_handle, (IntPtr)a, buf, length, out _)) continue;
                    for (int i = 0; i + 0x20 * 6 <= length; i += 0x20)
                    {
                        bool run = true;
                        for (int k = 0; k < 6 && run; k++)
                        {
                            int p = i + k * 0x20;
                            run = BitConverter.ToInt32(buf, p + 4) == 0 && BitConverter.ToInt32(buf, p + 8) == k && BitConverter.ToInt32(buf, p + 12) == 0
                                && BitConverter.ToInt32(buf, p + 20) is >= 10000 and < 20000 && buf[p + 16] is >= 1 and <= 5;
                        }
                        if (run && ReadInt32(a + i + ItemsToGil + 8) == 20) found.Add((a + i, a + i + ItemsToGil));
                    }
                }
                if (address <= 0 || address >= 0x7FFFFFFFFFFF) break;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
        return found;
    }

    /// <summary>
    /// Cancelled by Detach: a scan of a game that closed (or restarted) stops at its next chunk instead of running
    /// on next to the scan of the new process. Its result is never used anyway (Detach drops the task).
    /// </summary>
    CancellationTokenSource _scanCancel = new();

    /// <summary>
    /// Runs a full memory scan on a worker thread at below-normal priority, so the game wins any fight for the CPU
    /// and the scan only takes the time the game leaves free.
    /// </summary>
    Task<T> Scan<T>(Func<CancellationToken, T> scan)
    {
        var cancel = _scanCancel.Token; // this session's, taken now: Detach may replace it before the task starts
        return Task.Run(() => AtLowPriority(() => scan(cancel)), cancel);
    }

    static T AtLowPriority<T>(Func<T> work)
    {
        // Pool threads are shared: put the priority back when done.
        var thread = Thread.CurrentThread;
        var before = thread.Priority;
        thread.Priority = ThreadPriority.BelowNormal;
        try { return work(); }
        finally { thread.Priority = before; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryInfo { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public ushort PartitionId; public IntPtr RegionSize; public uint State, Protect, Type; }
    [DllImport("kernel32.dll")] static extern int VirtualQueryEx(IntPtr process, IntPtr address, out MemoryInfo info, uint length);

    int ReadInt32(long address) => ReadProcessMemory(_handle, (IntPtr)address, out int value, 4, out _) ? value : 0;

    delegate Owned RecordParser(ReadOnlySpan<byte> record, long slot);

    /// <summary>Records parsed straight from one read (no copy per record: this runs for every save copy every second).</summary>
    List<Owned> ReadRecords(long address, int size, int max, RecordParser parse)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(size * max);
        var list = new List<Owned>(max);
        try
        {
            if (!ReadProcessMemory(_handle, (IntPtr)address, buffer, size * max, out _)) return list;
            for (int i = 0; i < max; i++)
            {
                var o = parse(buffer.AsSpan(i * size, size), address + (long)i * size);
                if (o.Id >= 0 && o.Id < 100_000) list.Add(o); // empty slots (id 0) too: a new item may go there
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        return list;
    }

    /// <summary>
    /// The first record of the item list ending at the gil record: records before it while they look like items
    /// (id 1..99999, zero high word). One read of the area before it instead of two reads per record.
    /// </summary>
    long ItemsStart(long gil)
    {
        const int Max = 2000;
        var buffer = ArrayPool<byte>.Shared.Rent(0x18 * Max);
        try
        {
            long from = gil - 0x18 * Max;
            if (!ReadProcessMemory(_handle, (IntPtr)from, buffer, 0x18 * Max, out _))
            {
                // Part of that area is unreadable (the list starts near its allocation's edge): walk back record by record.
                long start = gil;
                while (ReadInt32(start - 0x18 + 8) is > 0 and < 100_000 && ReadInt32(start - 0x18 + 4) == 0) start -= 0x18;
                return start;
            }
            long items = gil;
            for (int i = Max - 1; i >= 0; i--)
            {
                var r = buffer.AsSpan(i * 0x18, 0x18);
                if (BitConverter.ToInt32(r[8..]) is not (> 0 and < 100_000) || BitConverter.ToInt32(r[4..]) != 0) break;
                items = from + i * 0x18;
            }
            return items;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    int ReadByte(IntPtr address) => ReadProcessMemory(_handle, address, out byte value, 1, out _) ? value : -1;

    bool Attach()
    {
        if (_process is not null && !_process.HasExited && (_chapterAddress != IntPtr.Zero || DateTime.Now < _retryAt))
            return _chapterAddress != IntPtr.Zero;
        Detach();
        _process = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (_process is null) return false;
        try
        {
            var module = _process.MainModule!;
            _handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, _process.Id);
            if (_handle == IntPtr.Zero) throw new InvalidOperationException("akses ditolak");
            (Version, long offset) = Identify(module.ModuleMemorySize, module.BaseAddress);
            if (offset == 0)
            {
                Problem = Lang.T($"Unknown FF7R version (module size {module.ModuleMemorySize}); chapter detection is off", $"Versi FF7R tidak dikenali (ukuran modul {module.ModuleMemorySize}), deteksi chapter mati");
                return false;
            }
            _moduleBase = module.BaseAddress;
            _chapterAddress = module.BaseAddress + (nint)offset;
            Problem = null;
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Usually the game runs as administrator and the overlay does not, or it is still starting up.
            _retryAt = DateTime.Now.AddSeconds(5);
            Problem = Lang.T($"Cannot read FF7R ({e.Message}). Try running the overlay as administrator", $"FF7R tidak bisa dibaca ({e.Message}). Coba jalankan overlay sebagai administrator");
            return false;
        }
    }

    /// <summary>Picks the chapter offset by game build, keyed on the main module size like the .asl does.</summary>
    (string?, long) Identify(int moduleSize, IntPtr baseAddress) => moduleSize switch
    {
        99311616 => ("Steam 1.0.0.0", 0x5988C20),
        99393536 => ("Steam 1.0.0.1", 0x599C6B0),
        99438592 => ("Epic 1.0.0.4", 0x59A6AD0),
        99467264 => ("Steam 1.0.0.4", 0x59ADBF0),
        99561472 => ReadInt(baseAddress + 0x5DB5E78) == 7 ? ("Epic 1.0.0.7", 0x59C3FF0) : ("Epic 1.0.0.6", 0x59C46B0),
        99594240 => ("Steam 1.0.0.6", 0x59CC7F0),
        99598336 => ("Steam 1.0.0.7", 0x59CD160),
        _ => (null, 0),
    };

    int ReadInt(IntPtr address) => ReadInt32(address);

    void Detach()
    {
        if (_handle != IntPtr.Zero) CloseHandle(_handle);
        _handle = IntPtr.Zero;
        _chapterAddress = IntPtr.Zero;
        _process?.Dispose();
        _process = null;
        Version = null;
        Problem = null;
        _lists.Clear();
        // Not disposed: a running scan may still check its token. Without timers or links it holds nothing to free.
        _scanCancel.Cancel();
        _scanCancel = new();
        _search = null;
        _objectiveRows = null;
        _slotRows.Clear();
        _slotParents.Clear();
        _questCounter = int.MinValue;
        _objectiveSlots = new();
        _objectiveSearch = null;
        _positionObjects = new();
        _nearScan = null;
        _flagSet = null;
        _playerPosition = 0;
        _naviTexts = new();
        _naviVolumes = new();
        Chests = [];
        _chestTablesFrom = "";
        _chestsComplete = false;
        _volumes = new();
    }

    public void Dispose() => Detach();
}
