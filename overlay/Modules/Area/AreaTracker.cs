using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>
/// Where you are: the area and floor from the game's own area volumes, the position, and the rooms you have walked
/// between (data\&lt;id&gt;\area-links.json), the only connections a route to a step trusts. Logs area changes and positions.
/// </summary>
public sealed class AreaTracker(IGameReader reader, ProgressTracker tracker, string data, string logs)
{
    readonly IGameReader _reader = reader;
    readonly ProgressTracker _tracker = tracker;
    readonly string _data = data, _logs = logs;

    /// <summary>The area you are in (the game's own name), null when not known.</summary>
    public GameLocation? Here { get; private set; }
    /// <summary>Names the area you are in from the game's own area volumes (Ff7rMapArea.cs).</summary>
    public bool Follow(GamePosition? p)
    {
        var here = p is not null ? _reader.ReadLocation(p) : null;
        var (lastHere, lastPosition) = (Here, Position);
        Position = p;
        if (here == Here) return false;
        LearnLink(lastHere, lastPosition, here, p);
        Here = here;
        // Every change of area, with the position, in data\logs\area.log: to check maps whose areas read differently.
        try
        {
            File.AppendAllText(Path.Combine(_logs, "area.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tch {_tracker.DetectedChapter}\t{here?.Area ?? "-"}\t{here?.Floor}\t{p?.X:F0}\t{p?.Y:F0}\t{p?.Z:F0}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return true;
    }

    /// <summary>Where the controlled character stands, read once a poll.</summary>
    public GamePosition? Position { get; private set; }
    GamePosition? _loggedPosition;

    /// <summary>
    /// Appends the controlled character's position to data\logs\position.log whenever it moved 2 m or more, with the
    /// chapter and the live objective: samples for naming the location later (not shown on the overlay yet).
    /// </summary>
    public void LogPosition(GamePosition? position)
    {
        if (position is not { } p) return;
        if (_loggedPosition is { } last && World.Distance(p, last, _reader.UnitsPerMetre) < 2) return;
        _loggedPosition = p;
        try
        {
            File.AppendAllText(Path.Combine(_logs, "position.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tch {_tracker.DetectedChapter}\t{p.X:F0}\t{p.Y:F0}\t{p.Z:F0}\t{_tracker.LiveObjective?.Title}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Whether the step is in the area you are in now (and on its floor, when the guide names one).</summary>
    public bool IsHere(Objective o) => Here is { } here && GuideRules.AreaOf(o) is var (area, floor)
        && area.Equals(here.Area, StringComparison.OrdinalIgnoreCase)
        && (floor is null || (here.Floor ?? "").Split(' ', '-').Contains(floor));
    string LinksFile => Path.Combine(_data, "area-links.json");

    /// <summary>
    /// Pairs of rooms ("floor|area") you walked straight from one into the other: the only connections the route
    /// trusts. Room boxes that merely touch can be split by a wall; a walk between them cannot.
    /// </summary>
    readonly HashSet<string> _links = [];
    HashSet<string> LoadLinks()
    {
        try { return File.Exists(LinksFile) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(LinksFile)) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>The rooms next to each room, built from _links when first needed after a change.</summary>
    Dictionary<string, List<string>>? _neighbours;
    Dictionary<string, List<string>> Neighbours()
    {
        var neighbours = new Dictionary<string, List<string>>();
        foreach (var link in _links)
        {
            var ends = link.Split('\n');
            if (ends.Length != 2) continue;
            (neighbours.TryGetValue(ends[0], out var a) ? a : neighbours[ends[0]] = []).Add(ends[1]);
            (neighbours.TryGetValue(ends[1], out var b) ? b : neighbours[ends[1]] = []).Add(ends[0]);
        }
        return neighbours;
    }

    static string Room(GameLocation l) => $"{l.Floor}|{l.Area}";
    static string LinkKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}\n{b}" : $"{b}\n{a}";

    /// <summary>
    /// Remembers a walk from one room into another between two polls (a second apart). A jump of more than 15 m in
    /// that second is not a walk (a load, a cutscene moving you), so it teaches nothing.
    /// </summary>
    void LearnLink(GameLocation? from, GamePosition? fromAt, GameLocation? to, GamePosition? toAt)
    {
        if (from is null || to is null || fromAt is null || toAt is null || from == to) return;
        if (World.Distance(fromAt, toAt, _reader.UnitsPerMetre) > 15) return;
        if (!_links.Add(LinkKey(Room(from), Room(to)))) return;
        _neighbours = null;
        try
        {
            string temp = LinksFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_links.OrderBy(l => l, StringComparer.Ordinal)));
            File.Move(temp, LinksFile, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The rooms to walk through from here to a room called <paramref name="area"/> (on floor hint
    /// <paramref name="floor"/> when given), the target included; null when no walked route is known.
    /// </summary>
    public List<string>? RouteTo(GameLocation here, string area, string? floor)
    {
        var neighbours = _neighbours ??= Neighbours();
        bool IsTarget(string room)
        {
            int bar = room.IndexOf('|');
            return room[(bar + 1)..].Equals(area, StringComparison.OrdinalIgnoreCase)
                && (floor is null || room[..bar].Split(' ', '-').Contains(floor));
        }
        string start = Room(here);
        var cameFrom = new Dictionary<string, string> { [start] = "" };
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            string room = queue.Dequeue();
            if (room != start && IsTarget(room))
            {
                var path = new List<string>();
                for (string r = room; r != start; r = cameFrom[r]) path.Add(r[(r.IndexOf('|') + 1)..]);
                path.Reverse();
                return path;
            }
            foreach (string next in neighbours.GetValueOrDefault(room) ?? [])
                if (cameFrom.TryAdd(next, room)) queue.Enqueue(next);
        }
        return null;
    }

    /// <summary>Reads the rooms walked between: once, when the overlay starts.</summary>
    public void Load() => _links.UnionWith(LoadLinks());
}
