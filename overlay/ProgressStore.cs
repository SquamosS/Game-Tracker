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
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameTracker");

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

    public static void Save(string game, Progress progress)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(PathFor(game), JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true }));
    }
}
