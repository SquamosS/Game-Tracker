using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>
/// Learned link between the game's story-progress counter and guide steps, per chapter:
/// "from counter value V on, you are at story step S". Filled when you mark your position by hand
/// (double-click a step, or tick a story step), and seeded by "progress" values in the guide.
/// </summary>
public class StoryMap
{
    static readonly string File_ = Path.Combine(DataPaths.Data, "ff7r-story-map.json");

    public Dictionary<int, SortedDictionary<int, string>> Chapters { get; set; } = new();

    public static StoryMap Load()
    {
        try { return JsonSerializer.Deserialize<StoryMap>(File.ReadAllText(File_)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Record(int chapter, int value, string stepId)
    {
        if (!Chapters.TryGetValue(chapter, out var steps)) Chapters[chapter] = steps = new();
        steps[value] = stepId;
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The story step for this counter value: the one recorded at the highest value not above it.</summary>
    public string? Lookup(Chapter chapter, int value)
    {
        var known = new SortedDictionary<int, string>();
        foreach (var o in chapter.Objectives.Where(o => o.Progress is not null)) known[o.Progress!.Value] = o.Id;
        if (Chapters.TryGetValue(chapter.Number, out var learned))
            foreach (var (v, id) in learned) known[v] = id;
        return known.LastOrDefault(kv => kv.Key <= value).Value;
    }
}
