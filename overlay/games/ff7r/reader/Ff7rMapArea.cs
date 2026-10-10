using System.Text.RegularExpressions;

namespace GameTracker;

/// <summary>
/// The area you are in, as the game's map names it ("Mako Reactor 5 - B7" · "B7 Facilities"), from the game's own
/// area volumes.
///
/// Each area is an EndNaviMapVolume actor in the level (vtable module+0x4C1C358), named like
/// "Navi070_Layer07_060_017_010": map 070, layer 07, part 060. Its area name is the localization text of
/// "$navi070_name_part007_600" (part × 10) and its floor that of "$navi070_name_layer007". The actor's root
/// BrushComponent (+0x160) holds its bounds at +0x160: centre X, Y, Z then half-size X, Y, Z. Names come from the
/// FNamePool (blocks at module+0x5981310; index = block &lt;&lt; 16 | offset / 2; entries start with a 2-byte header,
/// length &lt;&lt; 6 | wide). The volumes are picked up by their vtable during the objective search. Found on Steam 1.0.0.7
/// (research\notes.md, "Database batas area").
///
/// Not every map names its volumes that way (Sector 5 Slums, map 080: "EndNaviMapVolume9"). The same numbers are in
/// the actor too: layer at +0x3B0, part / 10 at +0x3B4, and the map is the first three digits of the name of the
/// World that owns the volume's level (actor +0x20 = Level, Level +0x20 = World,
/// "080-SLU5B_State__SLU5B_10_Level_NaviMap_Layer01"). Checked in Ch8: layer 1, part 3 = "Rooftops".
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long NaviVolumeVtableRva = 0x4C1C358, NamePoolBlocksRva = 0x5981310;

    long NaviVolumeVtable => Version == "Steam 1.0.0.7" ? (long)_moduleBase + NaviVolumeVtableRva : 0;

    /// <summary>"$navi..." key -> English text, from the objective search's pass over the localization table.</summary>
    Dictionary<string, string> _naviTexts = new();
    /// <summary>Volume actors seen at the last objective search, and those resolved to names and bounds.</summary>
    List<long> _naviVolumes = new();
    List<long> _resolvedFrom = new();
    List<Volume> _volumes = new();
    DateTime _volumeSearch;

    sealed record Volume(long Actor, string Area, string? Floor, float CX, float CY, float CZ, float EX, float EY, float EZ)
    {
        public bool Contains(Position p) => Math.Abs(p.X - CX) <= EX && Math.Abs(p.Y - CY) <= EY && Math.Abs(p.Z - CZ) <= EZ;
        public float Size => EX * EY * EZ;
    }

    public record Location(string Area, string? Floor);

    static readonly Regex VolumeName = new(@"^Navi(\d{3})_Layer(\d{2})_(\d{3})_\d{3}_\d{3}$", RegexOptions.Compiled),
        WorldMap = new(@"^(\d{3})-", RegexOptions.Compiled);

    /// <summary>
    /// The area whose volume holds the position (the smallest when volumes overlap), or null outside every volume, on
    /// another game version, or before the volumes and names were read.
    /// </summary>
    public Location? ReadLocation(Position p)
    {
        if (!Attach() || NaviVolumeVtable == 0) return null;
        if (!ReferenceEquals(_resolvedFrom, _naviVolumes) && _naviTexts.Count > 0) ResolveVolumes();
        // Volumes go when another map loads: look for the new ones (at most every 30 s).
        if (_volumes.Count == 0 || ReadInt64(_volumes[0].Actor) != NaviVolumeVtable)
        {
            if (DateTime.Now - _volumeSearch > TimeSpan.FromSeconds(30))
            {
                _volumeSearch = DateTime.Now;
                _objectiveSearch ??= Scan(SafeFindObjectives);
            }
            return null;
        }
        return _volumes.Where(v => v.Contains(p)).OrderBy(v => v.Size).Select(v => new Location(v.Area, v.Floor)).FirstOrDefault();
    }

    void ResolveVolumes()
    {
        var volumes = new List<Volume>();
        foreach (long actor in _naviVolumes)
        {
            if (ReadInt64(actor) != NaviVolumeVtable || AreaNumbers(actor) is not var (map, layer, part)) continue;
            if (_naviTexts.GetValueOrDefault($"$navi{map}_name_part{layer}_{part:000}") is not { } area) continue;
            long brush = ReadInt64(actor + 0x160);
            var b = new byte[24];
            if (brush == 0 || !ReadProcessMemory(_handle, (IntPtr)(brush + 0x160), b, b.Length, out _)) continue;
            var f = Enumerable.Range(0, 6).Select(i => BitConverter.ToSingle(b, i * 4)).ToArray();
            if (!f.All(float.IsFinite) || f[3] <= 0 || f[4] <= 0 || f[5] <= 0) continue;
            // Some maps have no floor names (an empty or unreadable text): then the area stands alone.
            string? floor = _naviTexts.GetValueOrDefault($"$navi{map}_name_layer{layer}");
            if (floor is not null && (floor.Trim().Length == 0 || floor.Any(c => c < ' ' || c > '\u024F'))) floor = null;
            volumes.Add(new Volume(actor, area, floor, f[0], f[1], f[2], f[3], f[4], f[5]));
        }
        _volumes = volumes;
        _resolvedFrom = _naviVolumes;
    }

    /// <summary>
    /// A volume's map ("080"), layer ("001") and part key number (30 for part 3): from its name when it is
    /// "Navi070_Layer07_060_...", otherwise from its fields and the map number in its World's name.
    /// </summary>
    (string Map, string Layer, int Part)? AreaNumbers(long actor)
    {
        if (VolumeName.Match(FName(ReadInt32(actor + 0x18))) is { Success: true } m)
            return (m.Groups[1].Value, "0" + m.Groups[2].Value, int.Parse(m.Groups[3].Value) * 10);
        long level = ReadInt64(actor + 0x20), world = level == 0 ? 0 : ReadInt64(level + 0x20);
        if (world == 0 || WorldMap.Match(FName(ReadInt32(world + 0x18))) is not { Success: true } w) return null;
        int layer = ReadInt32(actor + 0x3B0), part = ReadInt32(actor + 0x3B4);
        if (layer is <= 0 or > 99 || part is <= 0 or > 99) return null;
        return (w.Groups[1].Value, layer.ToString("000"), part * 10);
    }

    /// <summary>An FName's text from the game's FNamePool, or "" when it cannot be read.</summary>
    string FName(int index)
    {
        long block = ReadInt64((long)_moduleBase + NamePoolBlocksRva + 8L * (index >> 16));
        if (block == 0) return "";
        long entry = block + 2L * (index & 0xFFFF);
        var header = new byte[2];
        if (!ReadProcessMemory(_handle, (IntPtr)entry, header, 2, out _)) return "";
        int h = BitConverter.ToUInt16(header), length = h >> 6;
        if (length is <= 0 or > 256 || (h & 1) != 0) return ""; // area volume names are short and not wide
        var text = new byte[length];
        return ReadProcessMemory(_handle, (IntPtr)(entry + 2), text, length, out _) ? System.Text.Encoding.ASCII.GetString(text) : "";
    }
}
