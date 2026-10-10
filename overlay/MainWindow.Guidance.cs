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
        // An alert (missed missables) stays up for its time: a routine notice must not push it away.
        if (!alert && _noticeAlert && _notice is not null) return;
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

    /// <summary>Chapter whose recap waits, and since when.</summary>
    (int Chapter, DateTime Since)? _pendingRecap;

    /// <summary>
    /// Runs the recap 90 s after the chapter changed, when the inventory (read every 15-60 s) has caught up with
    /// the last pickups; dropped when a save was loaded meanwhile, since the ticks then belong to another save.
    /// </summary>
    void FollowRecap()
    {
        if (_pendingRecap is not var (number, since)) return;
        if (_reconcile || _storyMayGoBack || _guide is null) { _pendingRecap = null; return; }
        if (DateTime.Now - since < TimeSpan.FromSeconds(90)) return;
        _pendingRecap = null;
        if (_guide.Chapters.FirstOrDefault(c => c.Number == number) is { } ended) RecapMissed(ended);
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
            Notify(Lang.T($"Chapter {ended.Number} done: no missables missed", $"Chapter {ended.Number} selesai: tidak ada missable yang terlewat"), seconds: 10);
            return;
        }
        string names = string.Join(" · ", missed.Select(o => o.Name));
        Notify(Lang.T($"Missed in Chapter {ended.Number} ({missed.Count}): {names}. Needs a Chapter Select later.",
            $"Terlewat di Chapter {ended.Number} ({missed.Count}): {names}. Perlu Chapter Select nanti."), alert: true, seconds: 60);
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
    List<string>? RouteTo(Ff7rChapterReader.Location here, string area, string? floor)
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
            if (tag == TagHere || AreaOf(step) is not var (area, floor)) continue;
            if (area.Equals(here.Area, StringComparison.OrdinalIgnoreCase)) continue; // same room, other floor: no walk to show
            if (RouteTo(here, area, floor) is not { Count: > 0 } path) continue;
            string way = path.Count <= 4 ? string.Join(" › ", path) : $"{path[0]} › … › {path[^1]} ({path.Count} {Lang.T("rooms", "ruang")})";
            RouteText.Inlines.Add(new System.Windows.Documents.Run("➜ ") { Foreground = Mako, FontWeight = FontWeights.Bold });
            RouteText.Inlines.Add(new System.Windows.Documents.Run(step.Name + ": ") { Foreground = step.Missable ? Danger : Brushes.White, FontWeight = FontWeights.SemiBold });
            RouteText.Inlines.Add(new System.Windows.Documents.Run(way) { Foreground = Muted });
            RouteText.Visibility = Visibility.Visible;
            return;
        }
    }

    // ---- Chest log for auditing the guide -------------------------------------------------------------------------

    static readonly System.Text.RegularExpressions.Regex ChestTable = new(@"^[a-z]+\d+", System.Text.RegularExpressions.RegexOptions.Compiled);

    IReadOnlyList<Ff7rChapterReader.Chest>? _chestsLogged;
    DateTime _chestsLoggedAt;
    bool _chestsLogIncomplete;

    /// <summary>
    /// Keeps data\chests\&lt;table&gt;.tsv (one file per chest table, "obt080") up to date with every chest the game has
    /// loaded: id, position, area and floor (as the overlay names them), item ids and names. Rows are merged by id, so the
    /// files grow over a playthrough into the material for checking the guide's "where" against the game. Written when
    /// the chest list changes, and again (at most every 30 s) while some chest's area was not known yet.
    /// </summary>
    void LogChests()
    {
        var chests = _reader.Chests;
        if (chests.Count == 0) return;
        bool changed = !ReferenceEquals(chests, _chestsLogged);
        if (!changed && !(_chestsLogIncomplete && DateTime.Now - _chestsLoggedAt > TimeSpan.FromSeconds(30))) return;
        _chestsLogged = chests;
        _chestsLoggedAt = DateTime.Now;
        _chestsLogIncomplete = false;
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(DataPaths.Data, "chests")).FullName;
            foreach (var table in chests.GroupBy(c => ChestTable.Match(c.Id).Value))
            {
                string file = Path.Combine(folder, (table.Key.Length > 0 ? table.Key : "other") + ".tsv");
                var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (File.Exists(file))
                    foreach (var line in File.ReadAllLines(file).Skip(1))
                        if (line.Split('	') is { Length: > 1 } cells) rows[cells[0]] = line;
                foreach (var chest in table)
                {
                    var location = chest.At is { } at ? _reader.ReadLocation(at) : null;
                    if (chest.At is not null && location is null) _chestsLogIncomplete = true;
                    // A row that already knows its area keeps it when the area cannot be read now (another map shown).
                    if (location is null && rows.TryGetValue(chest.Id, out var known) && known.Split('	') is { Length: > 4 } k && k[4].Length > 0) continue;
                    string names = string.Join(" + ", chest.Items.Select(id => _itemMap.Name(id) ?? $"#{id}"));
                    rows[chest.Id] = string.Join('	', chest.Id, chest.At is { } p ? $"{p.X:0}" : "", chest.At is { } q ? $"{q.Y:0}" : "", chest.At is { } r ? $"{r.Z:0}" : "",
                        location?.Area ?? "", location?.Floor ?? "", string.Join(",", chest.Items), names);
                }
                string temp = file + ".tmp";
                File.WriteAllLines(temp, rows.Values.Prepend("id	x	y	z	area	floor	item ids	items"));
                File.Move(temp, file, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- Chests opened, learned in front of them ----------------------------------------------------------------

    static readonly string OpenedFile = Path.Combine(DataPaths.Data, "chests", "opened.json");

    /// <summary>
    /// Chests known to be opened (ids, e.g. "obt080_treasure0030"). The game's own "opened" state is not found yet
    /// (research\notes.md), so this is learned: an item the chest holds arrived while Cloud stood within 4 m of it.
    /// Chests opened before that was watched are not in here.
    /// </summary>
    readonly HashSet<string> _opened = LoadOpened();

    static HashSet<string> LoadOpened()
    {
        try { return File.Exists(OpenedFile) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(OpenedFile)) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    DateTime _inGameSince;

    /// <summary>
    /// Items arrived together (ids of the slots that changed): the one chest within 4 m whose contents are exactly those
    /// ids is opened. Not while in a battle or a menu, nor in the first 10 s after a save loaded or a save being matched
    /// (Reconcile): a drop, a reward or a loaded save must not mark a closed chest, since a wrong entry stays.
    /// </summary>
    void ChestOpened(HashSet<int> arrived)
    {
        if (_herePosition is not { } p || arrived.Count == 0 || _reconcile || _storyMayGoBack
            || DateTime.Now - _inGameSince < TimeSpan.FromSeconds(10) || _gameState is { Battle: true } or { Menu: true }) return;
        var near = _reader.Chests.Where(c => c.At is { } at && !_opened.Contains(c.Id) && c.Items.ToHashSet().SetEquals(arrived) && Distance(at, p) <= 4).ToList();
        if (near.Count != 1 || !_opened.Add(near[0].Id)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OpenedFile)!);
            string temp = OpenedFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_opened.OrderBy(id => id, StringComparer.Ordinal)));
            File.Move(temp, OpenedFile, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static double Distance(Ff7rChapterReader.Position a, Ff7rChapterReader.Position b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) / 100;
    }

    /// <summary>"12 m": 1 m up close, 5 m to 100 m, 10 m beyond.</summary>
    static string Metres(double metres)
    {
        double round = metres < 20 ? 1 : metres < 100 ? 5 : 10;
        return $"{Math.Round(metres / round) * round:0} m";
    }

    /// <summary>
    /// The area a chest stands in (Ff7rMapArea.cs), cached once known; null while it cannot be read (asked again after
    /// 30 s, when the area volumes may have loaded).
    /// </summary>
    string? ChestArea(Ff7rChapterReader.Chest chest)
    {
        if (!ReferenceEquals(_chestsIndexed, _reader.Chests)) IndexChests();
        if (_chestArea.TryGetValue(chest, out var known) && (known.Area is not null || DateTime.Now - known.When < TimeSpan.FromSeconds(30))) return known.Area;
        string? area = chest.At is { } at ? _reader.ReadLocation(at)?.Area : null;
        _chestArea[chest] = (area, DateTime.Now);
        return area;
    }

    /// <summary>The step is that item: the same name, or "Shiva" for "Shiva Materia" (not "Turbo Ether" for "Ether").</summary>
    static bool SameItem(Objective step, string name) =>
        step.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        || (name.EndsWith(" Materia") && step.Name.Equals(name[..^8], StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A chest opened before it could be watched: it is the only chest holding each of its items, and each of those is a
    /// guide step already ticked (ticked from the inventory or by you).
    /// </summary>
    bool Collected(Ff7rChapterReader.Chest chest)
    {
        if (_guide is null) return false;
        var names = chest.Items.Distinct().Select(_itemMap.Name).ToList();
        return names.All(name => name is not null && ReferenceEquals(_chestByName.GetValueOrDefault(name), chest)
            && _guide.Chapters.SelectMany(c => c.Objectives).Any(o => _progress.Done.Contains(o.Id) && SameItem(o, name)));
    }

    /// <summary>
    /// The chests of the area you are in that are not known to be opened, nearest first (at most 5): what they hold and
    /// how far. Chests whose contents are a step of the guide show there too (with their distance), so this is mostly
    /// what the guide does not list one by one, like Moogle Medals.
    /// </summary>
    List<(string Contents, string Distance)> ChestsHere()
    {
        if (!_live || _here is not { } here || _herePosition is not { } p) return [];
        return _reader.Chests
            .Where(c => c.At is not null && c.Items.Length > 0 && !_opened.Contains(c.Id) && ChestArea(c) is { } area && area.Equals(here.Area, StringComparison.OrdinalIgnoreCase) && !Collected(c))
            .Select(c => (Chest: c, Metres: Distance(c.At!, p)))
            .OrderBy(x => x.Metres).Take(5)
            .Select(x => (string.Join(" + ", x.Chest.Items.Distinct().Select(id => _itemMap.Name(id) ?? $"#{id}")), Metres(x.Metres))).ToList();
    }
}
