using System.Buffers;
using System.IO;
using System.Text.RegularExpressions;

namespace GameTracker;

/// <summary>
/// The markers the game puts on its map, with where they stand: shops, ladders, quest givers, the story objective and,
/// for a side quest under way, what it asks for (Kids on Patrol: one icon per child not found yet; an icon goes away
/// once its child is found). Found 11 Oct 2026 in Ch8 with the scanner (hex on the icon FNames, who, vt).
///
/// One object of vtable module+0x4BF2AB8 (not a named UObject) holds them as a sparse array at +0xD8: data pointer,
/// count (+0xE0), capacity (+0xE4), allocation bits (inline 4 x 32 bits at +0xE8 while the capacity is at most 128, else
/// the pointer at +0xF8), bit count +0x100. Each element is 0x98 bytes: FName at +0x00 (index, number), position at
/// +0x2C (float X, Y, Z). Names: "MapIcon080_qst_q2_Child01" (map 080, quest q2's target "Child01"), "080_SLU5B_q02_11"
/// (quest q02's marker at stage 11), "080_SLU5B_q01_00" (q01's giver), "080_SLU5B_Chapter09_100_00" (the story's).
/// The object is found by the objective search (Ff7rObjective.cs); the array is read in one go every 2 s. Steam 1.0.0.7 only.
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long MarkerOwnerVtableRva = 0x4BF2AB8;
    const int MarkerSize = 0x98;

    static readonly Regex QuestTargetIcon = new(@"^MapIcon(\d{3})_qst_q(\d+)_(\w+)$", RegexOptions.Compiled);
    static readonly Regex SideQuestId = new(@"^(\d{3})_\w+?_q(\d+)$", RegexOptions.Compiled);

    long MarkerOwnerVtable => Version == "Steam 1.0.0.7" && _moduleBase != 0 ? (long)_moduleBase + MarkerOwnerVtableRva : 0;

    /// <summary>The object holding the map markers, from the last objective search; 0 = not found, or not exactly one.</summary>
    long _markerOwner;
    IReadOnlyList<(string Name, GamePosition At)>? _markers;
    DateTime _markersAt;
    readonly Dictionary<(int, int), string> _markerNames = new();
    string _markersLogged = "";

    /// <summary>Every marker on the game's map now (name, where), null when not known.</summary>
    IReadOnlyList<(string Name, GamePosition At)>? ReadMapMarkers()
    {
        if (Version != "Steam 1.0.0.7" || !Attach() || _markerOwner == 0) return null;
        if (DateTime.Now - _markersAt < TimeSpan.FromSeconds(2)) return _markers;
        _markersAt = DateTime.Now;
        long owner = _markerOwner;
        var head = new byte[0x30];
        // Gone (a load freed it): unknown until the next search finds it again.
        if (ReadInt64(owner) != MarkerOwnerVtable || !ReadProcessMemory(_handle, (IntPtr)(owner + 0xD8), head, head.Length, out _))
        {
            _markerOwner = 0;
            return _markers = null;
        }
        long data = BitConverter.ToInt64(head, 0);
        int count = BitConverter.ToInt32(head, 8), capacity = BitConverter.ToInt32(head, 12), bitCount = BitConverter.ToInt32(head, 0x28);
        if (data == 0 || count is <= 0 or > 4096 || capacity < count || bitCount < count) return _markers = null;
        var bits = new byte[(count + 7) / 8];
        if (capacity <= 128) Array.Copy(head, 0x10, bits, 0, Math.Min(bits.Length, 16));
        else if (BitConverter.ToInt64(head, 0x20) is var secondary && (secondary == 0 || !ReadProcessMemory(_handle, (IntPtr)secondary, bits, bits.Length, out _)))
            return _markers = null;
        var buffer = ArrayPool<byte>.Shared.Rent(count * MarkerSize);
        try
        {
            if (!ReadProcessMemory(_handle, (IntPtr)data, buffer, count * MarkerSize, out _)) return _markers = null;
            var found = new List<(string, GamePosition)>();
            for (int i = 0; i < count; i++)
            {
                if ((bits[i >> 3] >> (i & 7) & 1) == 0) continue; // a free slot (an icon taken off the map)
                int at = i * MarkerSize, index = BitConverter.ToInt32(buffer, at), number = BitConverter.ToInt32(buffer, at + 4);
                if (index <= 0 || number < 0) continue;
                if (!_markerNames.TryGetValue((index, number), out var name))
                    _markerNames[(index, number)] = name = FName(index) is { Length: > 0 } text ? number > 0 ? $"{text}_{number - 1}" : text : "";
                var p = new GamePosition(BitConverter.ToSingle(buffer, at + 0x2C), BitConverter.ToSingle(buffer, at + 0x30), BitConverter.ToSingle(buffer, at + 0x34));
                if (name.Length > 0 && float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)) found.Add((name, p));
            }
            LogMarkers(found);
            return _markers = found;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>
    /// The quest page's side quests under way and what the map marks for them: their targets ("MapIcon080_qst_q2_Child01"
    /// for "080_SLU5B_q02": the same map, the same quest number) and their own marker ("080_SLU5B_q02_11").
    /// </summary>
    public IReadOnlyList<QuestMarker>? ReadQuestMarkers()
    {
        if (ReadMapMarkers() is not { } markers) return null;
        // Asked for every step of the chapter: the same answer while the markers and the quest page are the same lists.
        var sides = SideQuests;
        if (ReferenceEquals(_questMarkersFrom.Markers, markers) && ReferenceEquals(_questMarkersFrom.Sides, sides)) return _questMarkersFrom.Result;
        var result = new List<QuestMarker>();
        foreach (var side in sides.Where(s => s.UnderWay && !s.Finished))
        {
            if (SideQuestId.Match(side.Quest) is not { Success: true } id) continue;
            string map = id.Groups[1].Value;
            int number = int.Parse(id.Groups[2].Value);
            foreach (var (name, at) in markers)
                if (QuestTargetIcon.Match(name) is { Success: true } target && target.Groups[1].Value == map && int.Parse(target.Groups[2].Value) == number)
                    result.Add(new QuestMarker(side.Title, target.Groups[3].Value, at));
                else if (name.StartsWith(side.Quest + "_", StringComparison.Ordinal))
                    result.Add(new QuestMarker(side.Title, null, at));
        }
        _questMarkersFrom = (markers, sides, result);
        return result;
    }
    (object? Markers, object? Sides, IReadOnlyList<QuestMarker>? Result) _questMarkersFrom;

    /// <summary>data\ff7r\logs\map-markers.log: the markers each time the set of names changes (for working out the story's).</summary>
    void LogMarkers(List<(string Name, GamePosition At)> markers)
    {
        string names = string.Join("|", markers.Select(m => m.Name).Order(StringComparer.Ordinal));
        if (names == _markersLogged) return;
        _markersLogged = names;
        try
        {
            File.AppendAllText(Path.Combine(DataPaths.GameLogs("ff7r"), "map-markers.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{markers.Count} markers{Environment.NewLine}"
                + string.Concat(markers.Select(m => $"\t{m.Name}\t{m.At.X:0}\t{m.At.Y:0}\t{m.At.Z:0}{Environment.NewLine}")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
