using System.Diagnostics;
using System.IO;

namespace GameTracker;

/// <summary>
/// A game the tracker knows: its guide, its process, how to start it, and its pictures. Pictures live in
/// overlay\assets\games\&lt;id&gt;\ (background.jpg/png 1920x1080, cover.jpg/png 600x900, icon.png 256x256) and are
/// optional; Steam's own artwork is used when they are missing. ScreenshotGlob: the game's own screenshot folder
/// and file pattern, relative to its install folder.
/// </summary>
public sealed record GameModule(string Id, string DisplayName, string GuideFile, string ProcessName, int? SteamAppId,
    string? ScreenshotGlob = null)
{
    public string AssetDir => Path.Combine(DataPaths.Root, "overlay", "assets", "games", Id);

    public string? Background => Picture("background");
    public string? Cover => Picture("cover");
    public string? Icon => Picture("icon");

    string? Picture(string name) =>
        new[] { ".jpg", ".jpeg", ".png" }.Select(ext => Path.Combine(AssetDir, name + ext)).FirstOrDefault(File.Exists);

    public bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var p in processes) p.Dispose();
            return processes.Length > 0;
        }
    }

    /// <summary>Starts the game through Steam (the library may be on any drive). False when it has no Steam id.</summary>
    public bool Launch()
    {
        if (SteamAppId is not int id) return false;
        Process.Start(new ProcessStartInfo($"steam://rungameid/{id}") { UseShellExecute = true })?.Dispose();
        return true;
    }
}

/// <summary>All trackable games. A new game is one more line here plus its guide (and, for live tracking, a reader).</summary>
public static class GameRegistry
{
    public static IReadOnlyList<GameModule> All { get; } =
    [
        new("ff7r", "FINAL FANTASY VII REMAKE INTERGRADE", "ff7r-chapters.json", "ff7remake_", 1462040,
            ScreenshotGlob: @"End\Binaries\Win64\ff7remake_*.png"),
    ];

    public static GameModule? Find(string id) => All.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
