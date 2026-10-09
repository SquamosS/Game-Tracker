using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>
/// FF7R item/materia id -> name used to find the matching guide step. Seeded with ids seen while scanning,
/// extended when you tick a step right after the game gave you an item it did not know yet.
/// </summary>
public class ItemMap
{
    static readonly string File_ = Path.Combine(DataPaths.Data, "ff7r-item-map.json");

    // Ids follow the game's localization keys: accessories 9016 + E_ACC number, materia 10000/11000/12000/13000/14000
    // + M_MAG/M_SUP/M_COM/M_IND/M_SUM number. Names read from the game's own text table (tools/ff7r-scan).
    static readonly Dictionary<int, string> Seed = new()
    {
        [9018] = "Bulletproof Vest",
        [9025] = "Whistlewind Scarf",
        [9027] = "Healing Carcanet",
        [9029] = "Transference Module",
        [9030] = "Spectral Cogwheel",
        [9032] = "Enfeeblement Ring",
        [9035] = "Protective Boots",
        [9038] = "Otherworldly Crystal",
        [10003] = "Revival Materia",
        [10008] = "Poison Materia",
        [10009] = "Binding Materia",
        [10010] = "Time Materia",
        [10011] = "Barrier Materia",
        [11001] = "Magnify Materia",
        [11003] = "Warding Materia",
        [11006] = "Synergy Materia",
        [11007] = "AP Up Materia",
        [12001] = "Steal Materia",
        [12004] = "Chakra Materia",
        [12005] = "Prayer Materia",
        [13001] = "HP Up Materia",
        [13004] = "Luck Up Materia",
        [13005] = "Gil Up Materia",
        [13006] = "EXP Up Materia",
        [13013] = "Parry Materia",
        [13016] = "Provoke Materia",
        [13018] = "Refocus Materia",
        [14002] = "Shiva Materia",
        [14005] = "Bahamut Materia",
        [14008] = "Cactuar Materia",
        [14010] = "Ramuh Materia",

        [257] = "The Prelude",
        [258] = "Barret's Theme",
        [260] = "Hip Hop de Chocobo",
        [9002] = "Iron Bangle",
        [9017] = "Power Wristguards",
        [9024] = "Revival Earrings",
        [9033] = "Crescent Moon Charm",
        [9040] = "Star Bracelet",
        [10001] = "Healing Materia",
        [10002] = "Cleansing Materia",
        [10004] = "Fire Materia",
        [10005] = "Ice Materia",
        [10006] = "Lightning Materia",
        [10007] = "Wind Materia",
        [12002] = "Assess Materia",
        [13002] = "MP Up Materia",
        [13012] = "Deadly Dodge",
        [14003] = "Ifrit",
    };

    public Dictionary<int, string> Learned { get; set; } = new();

    /// <summary>Save-data flag ("offset:bit", see Ff7rChapterReader.ReadFlags) -> guide step name.</summary>
    public Dictionary<string, string> Flags { get; set; } = new();

    static readonly Dictionary<string, string> FlagSeed = new()
    {
        ["4C0:29"] = "Rat Problem",
        ["4C0:30"] = "Nuisance in the Factory",
        ["4C0:31"] = "On the Prowl",
        ["4C4:2"] = "Chadley's Report",
        ["4C4:0"] = "Just Flew in from the Graveyard",
        ["4C4:1"] = "Lost Friends",
        ["E:171907506"] = "Lost Friends",
        ["340:30"] = "Revival Earrings",
        ["340:31"] = "MP Up Materia",
        ["3E4:11"] = "Alone at Last",
        ["54C:28"] = "Shinra Reacts",
        ["3E0:27"] = "Heavenly Dart Player",
        ["95C:8"] = "c4-01-motor-chase",
        ["E:100910228"] = "Rat Problem",
        ["E:167986966"] = "On the Prowl",
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

    public string? Name(int id) =>
        Learned.TryGetValue(id, out var n) ? n : Seed.TryGetValue(id, out n) ? n : Community.Value.GetValueOrDefault(id);

    /// <summary>Full id -> name list shipped in data/ff7r-items.json (from Kingdom Save Editor, see the file).</summary>
    static readonly Lazy<Dictionary<int, string>> Community = new(() =>
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "ff7r-items.json")));
            return doc.RootElement.GetProperty("items").EnumerateObject()
                .ToDictionary(p => int.Parse(p.Name), p => p.Value.GetString() ?? "");
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or FormatException) { return new(); }
    });

    public void Learn(int id, string name)
    {
        Learned[id] = name;
        Save();
    }

    /// <summary>A failed write (file locked) keeps what was learned in memory; the next lesson writes it all again.</summary>
    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            File.WriteAllText(File_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
