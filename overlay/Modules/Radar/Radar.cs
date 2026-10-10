namespace GameTracker;

/// <summary>What a radar spot is: something the map marks for a side quest under way, a quest giver, the story objective's marker, a chest.</summary>
public enum RadarKind { Quest, Giver, Story, Chest }

/// <summary>A spot for the radar, where it stands in the game's units.</summary>
public sealed record RadarSpot(RadarKind Kind, GamePosition At);

/// <summary>Where a spot lies from the player, in metres: east and north on the game's map (IGameReader.OnMap), and up.</summary>
public readonly record struct RadarOffset(double East, double North, double Up)
{
    public double Flat => Math.Sqrt(East * East + North * North);
}

/// <summary>
/// The spots the radar shows (bottom left, north up): what the game's map marks for the side quests under way (their targets,
/// else their own marker; World.Marked), the givers of side quests not taken yet, the story objective's marker, and the
/// chests of this area not opened yet (ChestGuide). Only what the reader knows: an empty list turns the radar off.
/// </summary>
public sealed class Radar(IGameReader reader, ProgressTracker tracker, StepStatus status, ChestGuide chests)
{
    readonly IGameReader _reader = reader;
    readonly ProgressTracker _tracker = tracker;
    readonly StepStatus _status = status;
    readonly ChestGuide _chests = chests;

    public List<RadarSpot> Spots()
    {
        var spots = new List<RadarSpot>();
        if (!_tracker.InGame) return spots;
        var markers = _reader.ReadQuestMarkers() ?? [];
        foreach (var quest in _status.QuestsUnderWay())
            spots.AddRange(World.Marked(markers.Where(m => Same(m.Title, quest.Title))).Select(m => new RadarSpot(RadarKind.Quest, m.At)));
        spots.AddRange((_reader.ReadQuestGivers() ?? []).Select(g => new RadarSpot(RadarKind.Giver, g.At)));
        if (_tracker.LiveObjective?.Title is { Length: > 0 } live)
            spots.AddRange(markers.Where(m => m.Target is null && Same(m.Title, live)).Select(m => new RadarSpot(RadarKind.Story, m.At)));
        spots.AddRange(_chests.ChestSpotsHere().Select(at => new RadarSpot(RadarKind.Chest, at)));
        return spots;
    }

    static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Where <paramref name="to"/> lies from <paramref name="from"/> on the game's map, in metres.</summary>
    public static RadarOffset Offset(IGameReader reader, GamePosition from, GamePosition to)
    {
        var (e1, n1) = reader.OnMap(from);
        var (e2, n2) = reader.OnMap(to);
        double unit = reader.UnitsPerMetre;
        return new RadarOffset((e2 - e1) / unit, (n2 - n1) / unit, (to.Z - from.Z) / unit);
    }
}
