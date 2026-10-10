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

    static string Room(GameLocation l) => $"{l.Floor}|{l.Area}";

    static string LinkKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}\n{b}" : $"{b}\n{a}";

    /// <summary>
    /// Remembers a walk from one room into another between two polls (a second apart). A jump of more than 15 m in
    /// that second is not a walk (a load, a cutscene moving you), so it teaches nothing.
    /// </summary>
    void LearnLink(GameLocation? from, GamePosition? fromAt, GameLocation? to, GamePosition? toAt)
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
    List<string>? RouteTo(GameLocation here, string area, string? floor)
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

    IReadOnlyList<GameChest>? _chestsLogged;
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
                    string names = string.Join(" + ", chest.Items.Select(id => _names.Name(id) ?? $"#{id}"));
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

    /// <summary>
    /// Writes data\logs\items.log for items handed over in play (not a loaded save): time, chapter, id, name, count now,
    /// area, floor, where Cloud stands, the game state (battle, exploring, menu, cutscene) and the live objective. Where an item was
    /// picked up is a spot for points.json on the next playthrough.
    /// </summary>
    void LogItems(IEnumerable<OwnedItem> items)
    {
        var p = _herePosition;
        var lines = items.Select(o => string.Join('\t', $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}", _detectedChapter, o.Id, _names.Name(o.Id) ?? $"#{o.Id}", o.Count,
            _here?.Area ?? "", _here?.Floor ?? "", p is null ? "" : $"{p.X:0}", p is null ? "" : $"{p.Y:0}", p is null ? "" : $"{p.Z:0}",
            _reader.ReadGameState() switch { { Battle: true } => "battle", { Exploring: true } => "exploring", { Menu: true } => "menu", { Cutscene: true } => "cutscene", { } g => $"state {g.Code}", null => "" }, _objective?.Title ?? "")).ToList();
        if (lines.Count == 0) return;
        try
        {
            string file = Path.Combine(DataPaths.Logs, "items.log");
            if (!File.Exists(file)) File.WriteAllText(file, "time\tchapter\tid\tname\tcount\tarea\tfloor\tx\ty\tz\tstate\tobjective" + Environment.NewLine);
            File.AppendAllLines(file, lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    readonly Dictionary<string, bool> _chestFlagsLogged = [];

    /// <summary>
    /// Writes data\logs\chest-flag.log whenever the game's opened flag of a loaded chest changes (or is first read), with
    /// where Cloud stands: the check that the flag (new on 10 Oct 2026) keeps meaning "opened" on other maps.
    /// </summary>
    void LogChestFlags()
    {
        var lines = new List<string>();
        foreach (var chest in _reader.Chests)
        {
            // Unknown for a moment (the live copy not told apart yet) is not a change worth a line.
            if (_reader.ChestOpened(chest) is not { } open || (_chestFlagsLogged.TryGetValue(chest.Id, out var was) && was == open)) continue;
            _chestFlagsLogged[chest.Id] = open;
            string names = string.Join(" + ", chest.Items.Select(id => _names.Name(id) ?? $"#{id}"));
            string distance = chest.At is { } at && _herePosition is { } p ? Metres(Distance(at, p)) : "";
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{chest.Id}\tflag {(chest.Flag is { } f ? $"0x{f:X}" : "?")}\t{(open ? "opened" : "closed")}\t{distance}\t{names}");
        }
        if (lines.Count == 0) return;
        try { File.AppendAllLines(Path.Combine(DataPaths.Logs, "chest-flag.log"), lines); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- Chests opened, learned in front of them ----------------------------------------------------------------

    static readonly string OpenedFile = Path.Combine(DataPaths.Data, "chests", "opened.json");

    /// <summary>
    /// Chests learned to be opened (ids, e.g. "obt080_treasure0030"): an item the chest holds arrived while Cloud stood
    /// within 4 m of it. Only a fallback now: the game's own flag (IGameReader.ChestOpened) wins when it is known.
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

    static double Distance(GamePosition a, GamePosition b)
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
    string? ChestArea(GameChest chest)
    {
        if (!ReferenceEquals(_chestsIndexed, _reader.Chests)) IndexChests();
        if (_chestArea.TryGetValue(chest, out var known) && (known.Area is not null || DateTime.Now - known.When < TimeSpan.FromSeconds(30))) return known.Area;
        string? area = chest.At is { } at ? _reader.ReadLocation(at)?.Area : null;
        _chestArea[chest] = (area, DateTime.Now);
        return area;
    }

    /// <summary>The step is that item: the same name, or its short name ("Shiva" for "Shiva Materia"; not "Turbo Ether" for "Ether").</summary>
    bool SameItem(Objective step, string name) =>
        step.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        || (_names.ShortName(name) is { } shortName && step.Name.Equals(shortName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A chest opened before it could be watched: it is the only chest holding each of its items, and each of those is a
    /// guide step already ticked (ticked from the inventory or by you).
    /// </summary>
    bool Collected(GameChest chest)
    {
        if (_guide is null) return false;
        var names = chest.Items.Distinct().Select(_names.Name).ToList();
        return names.All(name => name is not null && ReferenceEquals(_chestByName.GetValueOrDefault(name), chest)
            && _guide.Chapters.SelectMany(c => c.Objectives).Any(o => _progress.Done.Contains(o.Id) && SameItem(o, name)));
    }

    /// <summary>A spot in the game world for a guide step (games/<id>/points.json): where it is done, in which area.</summary>
    public sealed record GuidePoint(float X, float Y, float Z, string Area, string? Note = null);

    /// <summary>Guide step id -> its spot, recorded standing there (position.log); loaded with the guide.</summary>
    Dictionary<string, GuidePoint> _points = [];
    readonly Dictionary<string, (string? Area, DateTime When)> _pointArea = [];

    static Dictionary<string, GuidePoint> LoadPoints(string file)
    {
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, GuidePoint>>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>
    /// Ctrl+Shift+Alt+P: notes where Cloud stands (area, floor, X Y Z, the game's live objective) in data\points-recorded.tsv,
    /// to be named and moved into points.json for a guide step.
    /// </summary>
    void RecordSpot()
    {
        if (!_live || _herePosition is not { } p)
        {
            Notify(Lang.T("No position to save yet", "Belum ada posisi untuk disimpan"), alert: true);
            return;
        }
        try
        {
            string file = Path.Combine(DataPaths.Data, "points-recorded.tsv");
            if (!File.Exists(file)) File.WriteAllText(file, "time\tchapter\tarea\tfloor\tx\ty\tz\tobjective" + Environment.NewLine);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{_detectedChapter}\t{_here?.Area}\t{_here?.Floor}\t{p.X:F0}\t{p.Y:F0}\t{p.Z:F0}\t{_objective?.Title}{Environment.NewLine}");
            Notify(Lang.T($"Spot saved: {_here?.Area ?? "?"} ({p.X:F0}, {p.Y:F0}, {p.Z:F0})", $"Titik disimpan: {_here?.Area ?? "?"} ({p.X:F0}, {p.Y:F0}, {p.Z:F0})"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notify(Lang.T($"Could not save the spot: {e.Message}", $"Gagal menyimpan titik: {e.Message}"), alert: true);
        }
    }

    /// <summary>How far a step is: its chest (ChestDistance), else its recorded spot (PointDistance).</summary>
    string? StepDistance(Objective o) => ChestDistance(o) ?? PointDistance(o);

    /// <summary>
    /// How far Cloud is from the step's recorded spot. Only while the area volumes of the loaded map name that spot's
    /// area as recorded (asked again every 30 s): the same coordinates in another map would be a guess.
    /// </summary>
    string? PointDistance(Objective o)
    {
        if (!_live || _herePosition is not { } p) return null;
        // The trail of an earlier playthrough first (MainWindow.Trail.cs), then a spot recorded by hand.
        if ((TrailSpot(o) ?? (_points.TryGetValue(o.Id, out var fixedPoint) ? (o.Id, fixedPoint) : null)) is not var (key, point)) return null;
        // Asked again after 30 s, or 5 s while unknown (the map's area volumes may still be loading).
        if (!_pointArea.TryGetValue(key, out var known) || DateTime.Now - known.When >= TimeSpan.FromSeconds(known.Area is null ? 5 : 30))
            _pointArea[key] = known = (_reader.ReadLocation(new GamePosition(point.X, point.Y, point.Z))?.Area, DateTime.Now);
        if (known.Area is null || !known.Area.Equals(point.Area, StringComparison.OrdinalIgnoreCase)) return null;
        return Metres(Distance(new GamePosition(point.X, point.Y, point.Z), p));
    }

    /// <summary>
    /// Whether the chest's object stands in the level now (a field object within 1.5 m of its point, Ff7rFieldActors.cs):
    /// a chest the game has not placed yet cannot be opened. Placed is not reachable, though (Aerith's house holds the
    /// Ch13 Mythical Amulet chest during Ch8, locked): it only hides, the guide's order still decides. Null when the level
    /// cannot be read.
    /// </summary>
    bool? Placed(GameChest chest)
    {
        if (chest.At is not { } at || _reader.ReadFieldActors() is not { } actors) return null;
        return actors.Any(a => Distance(a.At, at) <= 1.5);
    }

    /// <summary>The placed state of the one chest holding this step's item; null when no single chest holds it.</summary>
    bool? ChestPlaced(Objective o)
    {
        if (!ItemTypes.Contains(o.Type)) return null;
        if (!ReferenceEquals(_chestsIndexed, _reader.Chests)) IndexChests();
        return _chestByName.GetValueOrDefault(o.Name) is { } chest ? Placed(chest) : null;
    }

    readonly List<FieldActor> _fieldLogged = [];

    /// <summary>
    /// Writes data\logs\field-actors.log when a field object appears where none of its class was seen before this
    /// session (3 m apart; things carried around by people are left out): to learn the classes of pick-ups as they
    /// appear (the MP Up materia of The Language of Flowers).
    /// </summary>
    void LogFieldActors()
    {
        if (_reader.ReadFieldActors() is not { } actors) return;
        var fresh = actors.Where(a => !a.Class.StartsWith("WE") && (a.At.X != 0 || a.At.Y != 0)
            && !_fieldLogged.Any(b => b.Class == a.Class && Distance(a.At, b.At) <= 3)).ToList();
        if (fresh.Count == 0) return;
        _fieldLogged.AddRange(fresh);
        var lines = fresh.Select(a => $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{_here?.Area}\t{a.Class}\t{a.At.X:0}\t{a.At.Y:0}\t{a.At.Z:0}");
        try { File.AppendAllLines(Path.Combine(DataPaths.Logs, "field-actors.log"), lines); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The chest holds a step of this chapter that is not open yet (MP Up in Aerith's garden comes with The Language of
    /// Flowers, after the Rude fight): not listed before its time.
    /// </summary>
    bool ForLater(GameChest chest) => CurrentChapter is { } chapter && chest.Items.Select(_names.Name).OfType<string>()
        .Any(name => chapter.Objectives.Any(o => SameItem(o, name) && !_progress.Done.Contains(o.Id) && NotYet(o, chapter)));

    /// <summary>
    /// Opened: the game's own flag when it can be read; else learned (opened.json) or inferred from ticked steps (Collected).
    /// </summary>
    bool Opened(GameChest chest) => _reader.ChestOpened(chest) ?? (_opened.Contains(chest.Id) || Collected(chest));

    /// <summary>
    /// The chests of the area you are in that are not known to be opened, nearest first (at most 5): what they hold and
    /// how far. Chests whose contents are a step of the guide show there too (with their distance), so this is mostly
    /// what the guide does not list one by one, like Moogle Medals.
    /// </summary>
    List<(string Contents, string Distance)> ChestsHere()
    {
        if (!_live || _here is not { } here || _herePosition is not { } p) return [];
        return _reader.Chests
            .Where(c => c.At is not null && c.Items.Length > 0 && _reader.ChestShown(c, _objective) && !Opened(c) && Placed(c) != false && !ForLater(c) && ChestArea(c) is { } area && area.Equals(here.Area, StringComparison.OrdinalIgnoreCase))
            .Select(c => (Chest: c, Metres: Distance(c.At!, p)))
            .OrderBy(x => x.Metres).Take(5)
            .Select(x => (string.Join(" + ", x.Chest.Items.Distinct().Select(id => _names.Name(id) ?? $"#{id}")), Metres(x.Metres))).ToList();
    }
}
