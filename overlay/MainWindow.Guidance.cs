using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace GameTracker;

/// <summary>
/// What the overlay tells you on its own: a notice for what it just did (ticked, learned, undone), the chapter's
/// missed missables when a chapter ends, and the way to the next step's room through rooms you have walked between.
/// </summary>
public partial class MainWindow
{
    static readonly Brush NoticeOk = Brush("#264ADE80"), NoticeBad = Brush("#26F87171");

    /// <summary>Shows a notice for <paramref name="seconds"/> of visible time (also kept as the status text).</summary>
    void Notify(string text, bool alert = false, int seconds = 5)
    {
        _itemStatus = text;
        _notice = text;
        _noticeAlert = alert;
        _noticeSeconds = seconds;
        RenderNotice();
    }

    void RenderNotice()
    {
        NoticeBox.Visibility = _notice is null || !_inGame ? Visibility.Collapsed : Visibility.Visible;
        NoticeText.Text = _notice ?? "";
        NoticeBox.BorderBrush = _noticeAlert ? Danger : Now;
        NoticeBox.Background = _noticeAlert ? NoticeBad : NoticeOk;
    }

    /// <summary>
    /// A chapter just ended the normal way (the next one started): name its missables that were never ticked, on the
    /// overlay for a minute of play and in data\logs\missed.log, so a Chapter Select can be planned before saving over.
    /// </summary>
    void RecapMissed(Chapter ended)
    {
        var missed = ended.Objectives.Where(o => o.Missable && o.Type is not ("cerita" or "trofi") && !_progress.Done.Contains(o.Id)).ToList();
        if (missed.Count == 0)
        {
            Notify($"Chapter {ended.Number} selesai: tidak ada missable yang terlewat", seconds: 10);
            return;
        }
        Notify($"Terlewat di Chapter {ended.Number} ({missed.Count}): {string.Join(" · ", missed.Select(o => o.Name))}. Perlu Chapter Select nanti.", alert: true, seconds: 60);
        try
        {
            File.AppendAllText(Path.Combine(DataPaths.Logs, "missed.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tchapter {ended.Number}\t{string.Join(" | ", missed.Select(o => $"{o.Id} {o.Name}"))}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- Rooms walked between -------------------------------------------------------------------------------------

    static readonly string LinksFile = Path.Combine(DataPaths.Data, "area-links.json");

    /// <summary>
    /// Pairs of rooms ("floor|area") you walked straight from one into the other: the only connections the route
    /// trusts. Room boxes that merely touch can be split by a wall; a walk between them cannot.
    /// </summary>
    readonly HashSet<string> _links = LoadLinks();

    static HashSet<string> LoadLinks()
    {
        try { return File.Exists(LinksFile) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(LinksFile)) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    static string Room(Ff7rChapterReader.Location l) => $"{l.Floor}|{l.Area}";

    static string LinkKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}\n{b}" : $"{b}\n{a}";

    /// <summary>
    /// Remembers a walk from one room into another between two polls (a second apart). A jump of more than 15 m in
    /// that second is not a walk (a load, a cutscene moving you), so it teaches nothing.
    /// </summary>
    void LearnLink(Ff7rChapterReader.Location? from, Ff7rChapterReader.Position? fromAt, Ff7rChapterReader.Location? to, Ff7rChapterReader.Position? toAt)
    {
        if (from is null || to is null || fromAt is null || toAt is null || from == to) return;
        float dx = toAt.X - fromAt.X, dy = toAt.Y - fromAt.Y, dz = toAt.Z - fromAt.Z;
        if (dx * dx + dy * dy + dz * dz > 1500f * 1500f) return;
        if (!_links.Add(LinkKey(Room(from), Room(to)))) return;
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
    List<string>? RouteTo(Ff7rChapterReader.Location here, string area, string? floor)
    {
        var neighbours = new Dictionary<string, List<string>>();
        foreach (var link in _links)
        {
            var ends = link.Split('\n');
            if (ends.Length != 2) continue;
            (neighbours.TryGetValue(ends[0], out var a) ? a : neighbours[ends[0]] = []).Add(ends[1]);
            (neighbours.TryGetValue(ends[1], out var b) ? b : neighbours[ends[1]] = []).Add(ends[0]);
        }
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

    /// <summary>
    /// "➜ Magic Up Materia: Security Ops › Waste Storage": the way to the first open step (missables first) whose room
    /// is not this one, when every room on the way was walked between before. Nothing rather than a guess.
    /// </summary>
    void RenderRoute(Chapter? chapter)
    {
        RouteText.Inlines.Clear();
        RouteText.Visibility = Visibility.Collapsed;
        if (_here is not { } here || chapter is null || !_inGame || _full) return;
        var objectives = chapter.Objectives;
        int current = CurrentStory is { } story ? Array.IndexOf(objectives, story) : objectives.Length;
        foreach (var (step, tag) in OpenSteps(objectives, current))
        {
            if (tag == "DI SINI" || AreaOf(step) is not var (area, floor)) continue;
            if (area.Equals(here.Area, StringComparison.OrdinalIgnoreCase)) continue; // same room, other floor: no walk to show
            if (RouteTo(here, area, floor) is not { Count: > 0 } path) continue;
            string way = path.Count <= 4 ? string.Join(" › ", path) : $"{path[0]} › … › {path[^1]} ({path.Count} ruang)";
            RouteText.Inlines.Add(new System.Windows.Documents.Run("➜ ") { Foreground = Mako, FontWeight = FontWeights.Bold });
            RouteText.Inlines.Add(new System.Windows.Documents.Run(step.Name + ": ") { Foreground = step.Missable ? Danger : Brushes.White, FontWeight = FontWeights.SemiBold });
            RouteText.Inlines.Add(new System.Windows.Documents.Run(way) { Foreground = Muted });
            RouteText.Visibility = Visibility.Visible;
            return;
        }
    }
}
