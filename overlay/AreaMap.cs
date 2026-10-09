using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>
/// Positions where the game's map named the area, learned whenever you open the map (data\ff7r-areas.json), and the
/// area of the nearest one in the same chapter, so the overlay can say where you are while the map is closed.
/// </summary>
public sealed class AreaMap
{
    public sealed record Sample(int Chapter, string Key, string Area, string? Floor, float X, float Y, float Z);

    static readonly string File_ = Path.Combine(DataPaths.Data, "ff7r-areas.json");

    public List<Sample> Samples { get; set; } = new();

    public static AreaMap Load()
    {
        try { return JsonSerializer.Deserialize<AreaMap>(File.ReadAllText(File_)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    /// <summary>Remembers this position for the area. True when it is new: one sample per 3 m of an area is enough.</summary>
    public bool Learn(int chapter, Ff7rChapterReader.MapArea area, Ff7rChapterReader.Position p)
    {
        if (Samples.Any(s => s.Key == area.Key && Distance(s, p) < 300)) return false;
        Samples.Add(new Sample(chapter, area.Key, area.Area, area.Floor, p.X, p.Y, p.Z));
        Save();
        return true;
    }

    /// <summary>The nearest sample of this chapter within 30 m, or null. Chapters can reuse coordinates for other places.</summary>
    public Sample? Nearest(int chapter, Ff7rChapterReader.Position p) =>
        Samples.Where(s => s.Chapter == chapter).Select(s => (Sample: s, Distance: Distance(s, p)))
            .Where(x => x.Distance < 3000).OrderBy(x => x.Distance).Select(x => x.Sample).FirstOrDefault();

    /// <summary>In cm; height counts triple, as floors sit only a few metres apart.</summary>
    static double Distance(Sample s, Ff7rChapterReader.Position p)
    {
        double dx = s.X - p.X, dy = s.Y - p.Y, dz = (s.Z - p.Z) * 3;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    void Save()
    {
        try
        {
            File.WriteAllText(File_ + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(File_ + ".tmp", File_, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // kept in memory; written with the next one
    }
}
