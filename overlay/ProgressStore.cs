using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>Which chapter the player is on and which objectives are done, per game.</summary>
public class Progress
{
    public int Chapter { get; set; } = 1;
    public HashSet<string> Done { get; set; } = new();
    public List<string> History { get; set; } = new();
}

public static class ProgressStore
{
    static readonly string Dir = DataPaths.Data;

    static string PathFor(string game) =>
        Path.Combine(Dir, string.Concat(game.Split(Path.GetInvalidFileNameChars())) + ".json");

    public static Progress Load(string game)
    {
        try
        {
            return JsonSerializer.Deserialize<Progress>(File.ReadAllText(PathFor(game))) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>Copies the progress file to dataackups (one per minute at most) before it is rewritten wholesale.</summary>
    public static void Backup(string game)
    {
        if (!File.Exists(PathFor(game))) return;
        var dir = Directory.CreateDirectory(Path.Combine(Dir, "backups")).FullName;
        File.Copy(PathFor(game), Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmm}.json"), overwrite: true);
    }

    public static void Save(string game, Progress progress)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(PathFor(game), JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true }));
    }
}
