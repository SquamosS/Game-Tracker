using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>
/// FF7R item/materia id -> name used to find the matching guide step. Seeded with ids seen while scanning,
/// extended when you tick a step right after the game gave you an item it did not know yet.
/// </summary>
public class ItemMap
{
    static readonly string File_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameTracker", "ff7r-item-map.json");

    static readonly Dictionary<int, string> Seed = new()
    {
        [257] = "The Prelude",
        [260] = "Hip Hop de Chocobo",
        [9002] = "Iron Bangle",
        [9024] = "Revival Earrings",
        [9040] = "Star Bracelet",
        [10001] = "Healing Materia",
        [10002] = "Cleansing Materia",
        [10004] = "Fire Materia",
        [10005] = "Ice Materia",
        [10006] = "Lightning Materia",
        [12002] = "Assess Materia",
        [13012] = "Deadly Dodge",
    };

    public Dictionary<int, string> Learned { get; set; } = new();

    /// <summary>Save-data flag ("offset:bit", see Ff7rChapterReader.ReadFlags) -> guide step name.</summary>
    public Dictionary<string, string> Flags { get; set; } = new();

    static readonly Dictionary<string, string> FlagSeed = new()
    {
        ["C0:29"] = "Rat Problem",
        ["C0:30"] = "Nuisance in the Factory",
        ["C0:31"] = "On the Prowl",
        ["C4:2"] = "Chadley's Report",
    };

    public string? FlagName(string flag) => Flags.TryGetValue(flag, out var n) ? n : FlagSeed.GetValueOrDefault(flag);

    public void LearnFlag(string flag, string name)
    {
        Flags[flag] = name;
        Save();
    }

    public static ItemMap Load()
    {
        try { return JsonSerializer.Deserialize<ItemMap>(File.ReadAllText(File_)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public string? Name(int id) => Learned.TryGetValue(id, out var n) ? n : Seed.GetValueOrDefault(id);

    public void Learn(int id, string name)
    {
        Learned[id] = name;
        Save();
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
