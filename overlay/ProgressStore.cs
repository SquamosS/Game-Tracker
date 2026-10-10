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
    /// <summary>Playing on Hard (set with Ctrl+Shift+H): shows the Hard-only steps and notes.</summary>
    public bool Hard { get; set; }
}

public static class ProgressStore
{
    static readonly string Dir = DataPaths.Data;

    static string FileName(string game) => string.Concat(game.Split(Path.GetInvalidFileNameChars()));

    static string PathFor(string game) => Path.Combine(Dir, FileName(game) + ".json");

    /// <summary>Set when the progress file was unreadable and the newest backup was used instead.</summary>
    public static string? Recovered { get; private set; }

    /// <summary>
    /// The saved progress. A damaged file (the PC lost power mid-write) falls back to the newest readable backup
    /// of the same game rather than silently starting from zero.
    /// </summary>
    public static Progress Load(string game)
    {
        Recovered = null; // a recovery reported by an earlier load must not stick to this one
        if (!File.Exists(PathFor(game))) return new();
        if (TryRead(PathFor(game)) is { } progress) return progress;
        foreach (var backup in BackupsOf(game))
            if (TryRead(backup) is { } restored)
            {
                Recovered = Lang.T($"Progress file damaged; restored from backup {Path.GetFileName(backup)}", $"File progress rusak; dipulihkan dari backup {Path.GetFileName(backup)}");
                return restored;
            }
        Recovered = Lang.T("Progress file damaged and no readable backup found", "File progress rusak dan tidak ada backup yang bisa dibaca");
        return new();
    }

    static Progress? TryRead(string path)
    {
        try { return JsonSerializer.Deserialize<Progress>(File.ReadAllText(path)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    static string BackupDir => Path.Combine(Dir, "backups");

    const int KeepBackups = 50;

    /// <summary>
    /// This game's backups ("&lt;game&gt;-yyyyMMdd-HHmm.json"), newest first. The exact length check keeps game "X" from
    /// picking up the backups of a game named "X-something". Old backups named only by time are left alone: they
    /// cannot say which game they belong to, so restoring one into another game would be worse than not restoring.
    /// </summary>
    static IEnumerable<string> BackupsOf(string game)
    {
        string prefix = FileName(game) + "-";
        try
        {
            if (!Directory.Exists(BackupDir)) return [];
            return Directory.GetFiles(BackupDir, prefix + "*.json")
                .Where(f => Path.GetFileNameWithoutExtension(f) is var name
                    && name.Length == prefix.Length + "yyyyMMdd-HHmm".Length
                    && DateTime.TryParseExact(name[prefix.Length..], "yyyyMMdd-HHmm", null, System.Globalization.DateTimeStyles.None, out _))
                .OrderByDescending(f => f)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>
    /// Copies the progress file to the backups folder (one per minute at most) before it is rewritten wholesale,
    /// then drops this game's backups beyond the newest <see cref="KeepBackups"/> so the folder does not grow forever.
    /// </summary>
    public static void Backup(string game)
    {
        if (!File.Exists(PathFor(game))) return;
        try
        {
            var dir = Directory.CreateDirectory(BackupDir).FullName;
            File.Copy(PathFor(game), Path.Combine(dir, $"{FileName(game)}-{DateTime.Now:yyyyMMdd-HHmm}.json"), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; } // best effort: the progress file itself is untouched
        foreach (var old in BackupsOf(game).Skip(KeepBackups))
        {
            try { File.Delete(old); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // try again on the next backup
        }
    }

    /// <summary>
    /// Writes a temporary file and then swaps it in, so a power loss mid-write never leaves a half file. False when
    /// the file could not be written (locked by antivirus or a sync tool, disk full); the progress stays in memory.
    /// </summary>
    public static bool Save(string game, Progress progress)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string path = PathFor(game), temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
