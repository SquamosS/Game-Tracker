using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>
/// The chests of the loaded maps as a guide: which one holds a step's item, whether it is opened (the game's flag, else
/// learned in front of it or inferred from ticks), placed in the level yet, how far, and which ones are left in this area.
/// Keeps the chest logs (data\&lt;id&gt;\chests\, chest-flag.log, field-actors.log).
/// </summary>
public sealed class ChestGuide(IGameReader reader, ProgressTracker tracker, GuideRules rules, AreaTracker area, StepStatus status, bool live, string data, string logs)
{
    readonly IGameReader _reader = reader;
    readonly ProgressTracker _tracker = tracker;
    readonly GuideRules _rules = rules;
    readonly AreaTracker _area = area;
    readonly StepStatus _status = status;
    readonly bool _live = live;
    readonly string _data = data, _logs = logs;
    IGameNames _names => _reader.Names;

    /// <summary>Item name -> the one chest holding it (null: several do), built again when the chest list changes.</summary>
    Dictionary<string, GameChest?> _chestByName = new(StringComparer.OrdinalIgnoreCase);
    IReadOnlyList<GameChest>? _chestsIndexed;

    /// <summary>The area each chest stands in (Ff7rMapArea.cs), once known.</summary>
    readonly Dictionary<GameChest, (string? Area, DateTime When)> _chestArea = [];

    /// <summary>
    /// How far Cloud is from the chest holding this step's item ("12 m", rounded to 1 m up close, 5 m to 100 m, 10 m
    /// beyond), from the game's chest tables (Ff7rTreasure.cs). Only when exactly one chest of the loaded maps holds it,
    /// that chest stands in the area the guide names for the step, and the item was not obtained since the overlay
    /// started (the chest is empty then). Anything else would be a guess: null.
    /// </summary>
    public string? ChestDistance(Objective o)
    {
        if (!_live || _area.Position is not { } p || !_rules.IsItem(o) || GuideRules.AreaOf(o) is not var (stepArea, _)) return null;
        if (!ReferenceEquals(_chestsIndexed, _reader.Chests)) IndexChests();
        if (_chestByName.GetValueOrDefault(o.Name) is not { At: { } at } only || Placed(only) == false) return null;
        // The game's flag when known; else the item arriving this session or a learned opening means the chest is empty.
        if (_reader.ChestOpened(only) ?? (only.Items.Any(_tracker.Obtained.Contains) || _opened.Contains(only.Id))) return null;
        if (ChestArea(only) is not { } area || !area.Equals(stepArea, StringComparison.OrdinalIgnoreCase)) return null;
        return World.Metres(World.Distance(at, p, _reader.UnitsPerMetre));
    }

    void IndexChests()
    {
        _chestsIndexed = _reader.Chests;
        _chestByName = new(StringComparer.OrdinalIgnoreCase);
        _chestArea.Clear();
        foreach (var chest in _chestsIndexed)
            foreach (var name in chest.Items.Select(_names.Name).OfType<string>().Distinct())
                foreach (var key in _names.ShortName(name) is { } shortName ? new[] { name, shortName } : [name])
                    _chestByName[key] = _chestByName.ContainsKey(key) ? null : chest;
    }

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
    public void LogChests()
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
            string folder = Directory.CreateDirectory(Path.Combine(_data, "chests")).FullName;
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

    readonly Dictionary<string, bool> _chestFlagsLogged = [];

    /// <summary>
    /// Writes data\logs\chest-flag.log whenever the game's opened flag of a loaded chest changes (or is first read), with
    /// where Cloud stands: the check that the flag (new on 10 Oct 2026) keeps meaning "opened" on other maps.
    /// </summary>
    public void LogChestFlags()
    {
        var lines = new List<string>();
        foreach (var chest in _reader.Chests)
        {
            // Unknown for a moment (the live copy not told apart yet) is not a change worth a line.
            if (_reader.ChestOpened(chest) is not { } open || (_chestFlagsLogged.TryGetValue(chest.Id, out var was) && was == open)) continue;
            _chestFlagsLogged[chest.Id] = open;
            string names = string.Join(" + ", chest.Items.Select(id => _names.Name(id) ?? $"#{id}"));
            string distance = chest.At is { } at && _area.Position is { } p ? World.Metres(World.Distance(at, p, _reader.UnitsPerMetre)) : "";
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{chest.Id}\tflag {(chest.Flag is { } f ? $"0x{f:X}" : "?")}\t{(open ? "opened" : "closed")}\t{distance}\t{names}");
        }
        if (lines.Count == 0) return;
        try { File.AppendAllLines(Path.Combine(_logs, "chest-flag.log"), lines); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    string OpenedFile => Path.Combine(_data, "chests", "opened.json");

    /// <summary>
    /// Chests learned to be opened (ids, e.g. "obt080_treasure0030"): an item the chest holds arrived while Cloud stood
    /// within 4 m of it. Only a fallback now: the game's own flag (IGameReader.ChestOpened) wins when it is known.
    /// </summary>
    readonly HashSet<string> _opened = [];
    HashSet<string> LoadOpened()
    {
        try { return File.Exists(OpenedFile) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(OpenedFile)) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>
    /// Items arrived together (ids of the slots that changed): the one chest within 4 m whose contents are exactly those
    /// ids is opened. Not while in a battle or a menu, nor in the first 10 s after a save loaded or a save being matched
    /// (Reconcile): a drop, a reward or a loaded save must not mark a closed chest, since a wrong entry stays.
    /// </summary>
    public void LearnOpened(HashSet<int> arrived, GameState? state)
    {
        if (_area.Position is not { } p || arrived.Count == 0 || _tracker.LoadPending
            || DateTime.Now - _tracker.InGameSince < TimeSpan.FromSeconds(10) || state is { Battle: true } or { Menu: true }) return;
        var near = _reader.Chests.Where(c => c.At is { } at && !_opened.Contains(c.Id) && c.Items.ToHashSet().SetEquals(arrived) && World.Distance(at, p, _reader.UnitsPerMetre) <= 4).ToList();
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

    /// <summary>
    /// A chest opened before it could be watched: it is the only chest holding each of its items, and each of those is a
    /// guide step already ticked (ticked from the inventory or by you).
    /// </summary>
    bool Collected(GameChest chest)
    {
        if (_tracker.Guide is null) return false;
        var names = chest.Items.Distinct().Select(_names.Name).ToList();
        return names.All(name => name is not null && ReferenceEquals(_chestByName.GetValueOrDefault(name), chest)
            && _tracker.Guide.Chapters.SelectMany(c => c.Objectives).Any(o => _tracker.Progress.Done.Contains(o.Id) && _rules.SameItem(o, name)));
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
        return actors.Any(a => World.Distance(a.At, at, _reader.UnitsPerMetre) <= 1.5);
    }

    /// <summary>The placed state of the one chest holding this step's item; null when no single chest holds it.</summary>
    public bool? ChestPlaced(Objective o)
    {
        if (!_rules.IsItem(o)) return null;
        if (!ReferenceEquals(_chestsIndexed, _reader.Chests)) IndexChests();
        return _chestByName.GetValueOrDefault(o.Name) is { } chest ? Placed(chest) : null;
    }

    readonly List<FieldActor> _fieldLogged = [];

    /// <summary>
    /// Writes data\logs\field-actors.log when a field object appears where none of its class was seen before this
    /// session (3 m apart; things carried around by people are left out): to learn the classes of pick-ups as they
    /// appear (the MP Up materia of The Language of Flowers).
    /// </summary>
    public void LogFieldActors()
    {
        if (_reader.ReadFieldActors() is not { } actors) return;
        var fresh = actors.Where(a => !a.Class.StartsWith("WE") && (a.At.X != 0 || a.At.Y != 0)
            && !_fieldLogged.Any(b => b.Class == a.Class && World.Distance(a.At, b.At, _reader.UnitsPerMetre) <= 3)).ToList();
        if (fresh.Count == 0) return;
        _fieldLogged.AddRange(fresh);
        var lines = fresh.Select(a => $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{_area.Here?.Area}\t{a.Class}\t{a.At.X:0}\t{a.At.Y:0}\t{a.At.Z:0}");
        try { File.AppendAllLines(Path.Combine(_logs, "field-actors.log"), lines); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The chest holds a step of this chapter that is not open yet (MP Up in Aerith's garden comes with The Language of
    /// Flowers, after the Rude fight): not listed before its time.
    /// </summary>
    bool ForLater(GameChest chest) => _tracker.CurrentChapter is { } chapter && chest.Items.Select(_names.Name).OfType<string>()
        .Any(name => chapter.Objectives.Any(o => _rules.SameItem(o, name) && !_tracker.Progress.Done.Contains(o.Id) && _status.NotYet(o, chapter)));
    /// <summary>
    /// Opened: the game's own flag when it can be read; else learned (opened.json) or inferred from ticked steps (Collected).
    /// </summary>
    bool Opened(GameChest chest) => _reader.ChestOpened(chest) ?? (_opened.Contains(chest.Id) || Collected(chest));

    /// <summary>
    /// The chests of the area you are in that are not known to be opened, nearest first (at most 5): what they hold and
    /// how far. Chests whose contents are a step of the guide show there too (with their distance), so this is mostly
    /// what the guide does not list one by one, like Moogle Medals.
    /// </summary>
    public List<(string Contents, string Distance)> ChestsHere()
    {
        if (!_live || _area.Here is not { } here || _area.Position is not { } p) return [];
        return _reader.Chests
            .Where(c => c.At is not null && c.Items.Length > 0 && _reader.ChestShown(c, _tracker.LiveObjective) && !Opened(c) && Placed(c) != false && !ForLater(c) && ChestArea(c) is { } area && area.Equals(here.Area, StringComparison.OrdinalIgnoreCase))
            .Select(c => (Chest: c, Metres: World.Distance(c.At!, p, _reader.UnitsPerMetre)))
            .OrderBy(x => x.Metres).Take(5)
            .Select(x => (string.Join(" + ", x.Chest.Items.Distinct().Select(id => _names.Name(id) ?? $"#{id}")), World.Metres(x.Metres))).ToList();
    }

    /// <summary>Reads the chests learned to be opened: once, when the overlay starts.</summary>
    public void Load() => _opened.UnionWith(LoadOpened());
}
