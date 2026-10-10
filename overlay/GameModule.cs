using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>
/// A game the tracker knows, from its folder overlay\games\&lt;id&gt;\ (see games\README.md): game.json (name, process,
/// Steam id, screenshot folder, live reader), guide.json (the checklist) and assets\ (background, cover, icon; all
/// optional, Steam's own artwork is used when they are missing). Reader: the id of a live reader ([GameReader], e.g. "ff7r") that reads the game's memory and ticks
/// steps on its own; none (null) gives the same overlay with manual ticking and chapter changes (hotkeys).
/// </summary>
public sealed record GameModule(string Id, string DisplayName, string ProcessName, int? SteamAppId,
    string? ScreenshotGlob = null, string? Reader = null, string? Short = null, IReadOnlyDictionary<string, StepType>? StepTypes = null)
{
    /// <summary>The name in short status lines ("FF7R"): game.json "shortName", else the full name.</summary>
    public string ShortName => Short ?? DisplayName;

    /// <summary>The game's folder as built (guide.json and other data files are copied next to the exe).</summary>
    public string Folder => Path.Combine(AppContext.BaseDirectory, "games", Id);

    public string GuideFile => Path.Combine(Folder, "guide.json");

    /// <summary>Pictures are read from the project folder, so one can be swapped without rebuilding.</summary>
    public string AssetDir => Path.Combine(DataPaths.Root, "overlay", "games", Id, "assets");

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

/// <summary>
/// All trackable games: one folder each under games\ with a game.json (folders starting with "_" are skipped, for
/// templates). A new game needs no code unless it gets a live reader.
/// </summary>
public static class GameRegistry
{
    record Info(string DisplayName, string ProcessName, int? SteamAppId, string? ScreenshotGlob, string? Reader, string? ShortName,
        Dictionary<string, StepType>? StepTypes);

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static IReadOnlyList<GameModule> All { get; } = Load();

    static List<GameModule> Load()
    {
        var games = new List<GameModule>();
        string root = Path.Combine(AppContext.BaseDirectory, "games");
        if (!Directory.Exists(root)) return games;
        foreach (string dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetFileName(dir), file = Path.Combine(dir, "game.json");
            // "logs" and "backups" are shared folders in data\ (DataPaths.Game): such an id would mix its files with them.
            if (id.StartsWith('_') || id.Equals("logs", StringComparison.OrdinalIgnoreCase) || id.Equals("backups", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(file)) continue;
            try
            {
                if (JsonSerializer.Deserialize<Info>(File.ReadAllText(file), Options) is { DisplayName.Length: > 0, ProcessName.Length: > 0 } info)
                    games.Add(new GameModule(id, info.DisplayName, info.ProcessName, info.SteamAppId, info.ScreenshotGlob, info.Reader, info.ShortName, info.StepTypes));
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // A broken game.json leaves that game out; say why in data\logs\games.log (a typo in stepTypes is easy to make).
                try { File.AppendAllText(Path.Combine(DataPaths.Logs, "games.log"), $"{DateTime.Now:s} {file}: {e.Message}{Environment.NewLine}"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return games;
    }

    public static GameModule? Find(string id) => All.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
