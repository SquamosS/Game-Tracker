using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>Which chapter the player is on and which objectives are done, per game.</summary>
public class Progress
{
    public int Chapter { get; set; } = 1;
    public HashSet<string> Done { get; set; } = new();
    public List<string> History { get; set; } = new();
    /// <summary>Every step ever ticked in this playthrough, so loading an older save and back loses nothing.</summary>
    public HashSet<string> Ever { get; set; } = new();
}

public static class ProgressStore
{
    static readonly string Dir = DataPaths.Data;

    static string PathFor(string game) =>
        Path.Combine(Dir, string.Concat(game.Split(Path.GetInvalidFileNameChars())) + ".json");

    /// <summary>Set when the progress file was unreadable and the newest backup was used instead.</summary>
    public static string? Recovered { get; private set; }

    /// <summary>
    /// The saved progress. A damaged file (the PC lost power mid-write) falls back to the newest readable backup
    /// rather than silently starting from zero.
    /// </summary>
    public static Progress Load(string game)
    {
        if (!File.Exists(PathFor(game))) return new();
        if (TryRead(PathFor(game)) is { } progress) return progress;
        var backups = Directory.Exists(BackupDir) ? Directory.GetFiles(BackupDir, "*.json").OrderByDescending(f => f) : Enumerable.Empty<string>();
        foreach (var backup in backups)
            if (TryRead(backup) is { } restored)
            {
                Recovered = $"File progress rusak; dipulihkan dari backup {Path.GetFileName(backup)}";
                return restored;
            }
        Recovered = "File progress rusak dan tidak ada backup yang bisa dibaca";
        return new();
    }

    static Progress? TryRead(string path)
    {
        try { return JsonSerializer.Deserialize<Progress>(File.ReadAllText(path)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    static string BackupDir => Path.Combine(Dir, "backups");

    /// <summary>Copies the progress file to the backups folder (one per minute at most) before it is rewritten wholesale.</summary>
    public static void Backup(string game)
    {
        if (!File.Exists(PathFor(game))) return;
        var dir = Directory.CreateDirectory(BackupDir).FullName;
        File.Copy(PathFor(game), Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmm}.json"), overwrite: true);
    }

    /// <summary>Writes a temporary file and then swaps it in, so a power loss mid-write never leaves a half file.</summary>
    public static void Save(string game, Progress progress)
    {
        Directory.CreateDirectory(Dir);
        string path = PathFor(game), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}
