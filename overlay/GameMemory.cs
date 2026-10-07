using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameTracker;

/// <summary>
/// Reads the current chapter number out of FINAL FANTASY VII REMAKE INTERGRADE's memory, read-only,
/// the same way the community LiveSplit autosplitter does (https://github.com/Mysterion06/FF7RSplitter).
/// </summary>
sealed class GameMemory : IDisposable
{
    const string ProcessName = "ff7remake_";
    const uint ProcessVmRead = 0x0010, ProcessQueryLimitedInformation = 0x1000;

    /// <summary>Chapter byte offsets from the game's main module, keyed by module size (one per game build).</summary>
    static readonly Dictionary<int, (string Version, int Chapter)> Builds = new()
    {
        [99311616] = ("Steam 1.0.0.0", 0x5988C20),
        [99393536] = ("Steam 1.0.0.1", 0x599C6B0),
        [99467264] = ("Steam 1.0.0.4", 0x59ADBF0),
        [99594240] = ("Steam 1.0.0.6", 0x59CC7F0),
        [99598336] = ("Steam 1.0.0.7", 0x59CD160),
        [99438592] = ("Epic 1.0.0.4", 0x59A6AD0),
    };

    // Epic 1.0.0.6 and 1.0.0.7 share a module size; the autosplitter tells them apart by this int.
    const int EpicSharedSize = 99561472, EpicVersionProbe = 0x5DB5E78;

    Process? _process;
    IntPtr _handle;
    IntPtr _chapterAddress;

    /// <summary>Human-readable state for the overlay footer.</summary>
    public string Status { get; private set; } = "Game belum berjalan";

    /// <summary>The chapter the game reports, or null while it is not running, unsupported or between chapters.</summary>
    public int? ReadChapter()
    {
        if (!Attach()) return null;
        byte[] buffer = new byte[1];
        if (!ReadProcessMemory(_handle, _chapterAddress, buffer, 1, out _))
        {
            Status = $"Gagal membaca memori game (error {Marshal.GetLastWin32Error()})";
            Detach();
            return null;
        }
        // 255 while a chapter is ending or loading, 0 before any save is loaded.
        int value = buffer[0];
        return value is >= 1 and <= 30 ? value : null;
    }

    bool Attach()
    {
        if (_process is { HasExited: false } && _handle != IntPtr.Zero && _chapterAddress != IntPtr.Zero) return true;
        Detach();

        _process = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (_process is null) { Status = "Game belum berjalan"; return false; }

        try
        {
            var module = _process.MainModule!;
            _handle = OpenProcess(ProcessVmRead | ProcessQueryLimitedInformation, false, _process.Id);
            if (_handle == IntPtr.Zero) throw new Win32Exception();

            int size = module.ModuleMemorySize;
            (string Version, int Chapter) build;
            if (size == EpicSharedSize)
                build = ReadInt(module.BaseAddress + EpicVersionProbe) == 7 ? ("Epic 1.0.0.7", 0x59C3FF0) : ("Epic 1.0.0.6", 0x59C46B0);
            else if (!Builds.TryGetValue(size, out build))
            {
                Status = $"Versi game tidak dikenal (ukuran modul {size}), pindah chapter manual";
                Detach();
                return false;
            }

            _chapterAddress = module.BaseAddress + build.Chapter;
            Status = $"Terhubung ke FF7R {build.Version}";
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Status = $"Tidak bisa membuka proses game: {e.Message}";
            Detach();
            return false;
        }
    }

    int ReadInt(IntPtr address)
    {
        byte[] buffer = new byte[4];
        return ReadProcessMemory(_handle, address, buffer, 4, out _) ? BitConverter.ToInt32(buffer) : -1;
    }

    void Detach()
    {
        if (_handle != IntPtr.Zero) CloseHandle(_handle);
        _handle = IntPtr.Zero;
        _chapterAddress = IntPtr.Zero;
        _process?.Dispose();
        _process = null;
    }

    public void Dispose() => Detach();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);
}
