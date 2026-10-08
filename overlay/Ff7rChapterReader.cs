using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameTracker;

/// <summary>
/// Reads the current chapter number from FF7 Remake's memory (read-only), the same way LiveSplit's
/// autosplitter does. Offsets come from https://github.com/Mysterion06/FF7RSplitter (FF7R.asl).
/// </summary>
public sealed class Ff7rChapterReader : IDisposable
{
    const string ProcessName = "ff7remake_";
    const uint PROCESS_VM_READ = 0x10, PROCESS_QUERY_INFORMATION = 0x400;

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

    Process? _process;
    IntPtr _handle;
    IntPtr _chapterAddress;
    IntPtr _moduleBase;
    DateTime _retryAt;

    /// <summary>
    /// Pointer chains (module offset, then offsets to follow) to the story-progress counter, per build.
    /// Empty for now: the first chain found (modul+0x57E99C0 0x58 0x598 0x48) turned out to be the current
    /// area, not story progress, so it would move the guide whenever you walk into a shop.
    /// </summary>
    static readonly Dictionary<string, long[][]> StoryChains = new()
    {
    };

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

    /// <summary>The story-progress counter, or null when unknown for this build or not readable.</summary>
    public int? ReadStoryProgress()
    {
        if (!Attach() || Version is null || !StoryChains.TryGetValue(Version, out var chains)) return null;
        foreach (var chain in chains)
        {
            long address = Follow(chain);
            var buffer = new byte[4];
            if (address == 0 || !ReadProcessMemory(_handle, (IntPtr)address, buffer, 4, out _)) continue;
            int value = BitConverter.ToInt32(buffer);
            if (value is > 0 and < 100_000) return value;
        }
        return null;
    }

    // The item list (records of 0x18 bytes: obtained time, id, count, category) and the materia list
    // (records of 0x20 bytes: obtained time, slot, level/flags, id) sit 0x33630 bytes apart in the save data.
    // No pointer chain to them survived a game restart, so they are found by signature once per launch.
    // The game keeps several copies (save buffers); all are read and records are told apart by obtained time.
    const int ItemsToGil = 0x33630;
    List<(long Materia, long Gil)> _lists = new();
    Task<List<(long, long)>>? _search;

    public record Owned(int Id, int Count, uint Obtained);

    /// <summary>Everything in the item and materia lists, or null while they are still being looked for.</summary>
    public List<Owned>? ReadOwned()
    {
        if (!Attach()) return null;
        if (_search is { IsCompleted: true }) { _lists = _search.Result; _search = null; }
        _lists.RemoveAll(l => ReadInt32(l.Gil + 8) != 20);
        if (_lists.Count == 0)
        {
            _search ??= Task.Run(FindLists);
            return null;
        }
        var owned = new List<Owned>();
        foreach (var (materia, gil) in _lists)
        {
            long items = gil;
            while (ReadInt32(items - 0x18 + 8) is > 0 and < 100_000 && ReadInt32(items - 0x18 + 4) == 0) items -= 0x18;
            owned.AddRange(ReadRecords(items, 0x18, 600, b => new Owned(BitConverter.ToInt32(b, 8), BitConverter.ToInt32(b, 12), BitConverter.ToUInt32(b, 0))));
            owned.AddRange(ReadRecords(materia, 0x20, 600, b => new Owned(BitConverter.ToInt32(b, 20), 1, BitConverter.ToUInt32(b, 0))));
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
        var set = new HashSet<string>();
        for (int i = 0; i < buffer.Length; i += 4)
        {
            uint word = BitConverter.ToUInt32(buffer, i);
            for (int bit = 0; bit < 32; bit++)
                if ((word & (1u << bit)) != 0) set.Add($"{i:X}:{bit}");
        }
        // Log of side-quest step events (one id per step, e.g. "found cat 2"), kept in the save; -1 = empty slot.
        var events = new byte[EventBytes];
        if (ReadProcessMemory(_handle, (IntPtr)(materia + EventStart), events, events.Length, out _))
            for (int i = 0; i < events.Length; i += 4)
                if (BitConverter.ToInt32(events, i) is var id && id > 0) set.Add($"E:{id}");
        return set;
    }

    const int FlagStart = 0x40E00, FlagBytes = 0x1000, EventStart = 0xE9A4, EventBytes = 0x400;

    /// <summary>Scans writable memory for materia lists (6+ consecutive materia records) with the gil record at the known distance.</summary>
    List<(long, long)> FindLists()
    {
        var found = new List<(long, long)>();
        long address = 0;
        while (VirtualQueryEx(_handle, (IntPtr)address, out var mbi, (uint)Marshal.SizeOf<MemoryInfo>()) != 0)
        {
            long start = (long)mbi.BaseAddress, size = (long)mbi.RegionSize;
            address = start + size;
            if (mbi.State != 0x1000 || (mbi.Protect & 0x04) == 0 || (mbi.Protect & 0x100) != 0) continue;
            for (long a = start; a < start + size; a += 1 << 22)
            {
                var buf = new byte[(int)Math.Min(1 << 22, start + size - a)];
                if (!ReadProcessMemory(_handle, (IntPtr)a, buf, buf.Length, out _)) continue;
                for (int i = 0; i + 0x20 * 6 <= buf.Length; i += 0x20)
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
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryInfo { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public ushort PartitionId; public IntPtr RegionSize; public uint State, Protect, Type; }
    [DllImport("kernel32.dll")] static extern int VirtualQueryEx(IntPtr process, IntPtr address, out MemoryInfo info, uint length);

    int ReadInt32(long address)
    {
        var buffer = new byte[4];
        return ReadProcessMemory(_handle, (IntPtr)address, buffer, 4, out _) ? BitConverter.ToInt32(buffer) : 0;
    }

    List<Owned> ReadRecords(long address, int size, int max, Func<byte[], Owned> parse)
    {
        var buffer = new byte[size * max];
        var list = new List<Owned>();
        if (!ReadProcessMemory(_handle, (IntPtr)address, buffer, buffer.Length, out _)) return list;
        for (int i = 0; i < max; i++)
        {
            var o = parse(buffer[(i * size)..((i + 1) * size)]);
            if (o.Id > 0 && o.Id < 100_000) list.Add(o);
        }
        return list;
    }
    /// <summary>Follows module+chain[0] -> [ptr]+chain[1] -> ... and returns the final address (0 if broken).</summary>
    long Follow(long[] chain)
    {
        long address = _moduleBase + (nint)chain[0];
        foreach (long offset in chain.Skip(1))
        {
            long pointer = ReadLong(address);
            if (pointer == 0) return 0;
            address = pointer + offset;
        }
        return address;
    }
    long ReadLong(long address)
    {
        var buffer = new byte[8];
        return ReadProcessMemory(_handle, (IntPtr)address, buffer, 8, out _) ? BitConverter.ToInt64(buffer) : 0;
    }

    int ReadByte(IntPtr address)
    {
        var buffer = new byte[1];
        return ReadProcessMemory(_handle, address, buffer, 1, out _) ? buffer[0] : -1;
    }

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
                Problem = $"Versi FF7R tidak dikenali (ukuran modul {module.ModuleMemorySize}), deteksi chapter mati";
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
            Problem = $"FF7R tidak bisa dibaca ({e.Message}). Coba jalankan overlay sebagai administrator";
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

    int ReadInt(IntPtr address)
    {
        var buffer = new byte[4];
        return ReadProcessMemory(_handle, address, buffer, 4, out _) ? BitConverter.ToInt32(buffer) : 0;
    }

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
        _search = null;
    }

    public void Dispose() => Detach();
}
