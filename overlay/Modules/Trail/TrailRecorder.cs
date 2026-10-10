using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>A spot in the game world for a guide step (games/&lt;id&gt;/points.json, the trail): where it is done, in which area.</summary>
/// <summary>A spot in the game world for a guide step (games/<id>/points.json): where it is done, in which area.</summary>
public sealed record GuidePoint(float X, float Y, float Z, string Area, string? Note = null);

public sealed class TrailEntry
{
    public GuidePoint? Start { get; set; }
    public Dictionary<string, GuidePoint> Stages { get; set; } = [];

    public GuidePoint? Done { get; set; }
}

/// <summary>
/// The trail: where things happened in this playthrough, kept per guide step in data\&lt;id&gt;\trail.json so the next one
/// can point the way (see Follow); with the spots recorded by hand (points.json), the distance to a step. Also the item log.
/// </summary>
public sealed class TrailRecorder(IGameReader reader, ProgressTracker tracker, GuideRules rules, AreaTracker area, StepStatus status, bool live, string data, string logs)
{
    readonly IGameReader _reader = reader;
    readonly ProgressTracker _tracker = tracker;
    readonly GuideRules _rules = rules;
    readonly AreaTracker _area = area;
    readonly StepStatus _status = status;
    readonly bool _live = live;
    readonly string _data = data, _logs = logs;
    IGameNames _names => _reader.Names;

    string TrailFile => Path.Combine(_data, "trail.json");
    readonly Dictionary<string, TrailEntry> _trail = [];

    Dictionary<string, TrailEntry> LoadTrail()
    {
        try { return File.Exists(TrailFile) ? JsonSerializer.Deserialize<Dictionary<string, TrailEntry>>(File.ReadAllText(TrailFile)) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>The live objective and sub-objective stage last seen ("title key|description key"), and the steps done then.</summary>
    string? _trailStage;
    Objective? _trailStep;
    HashSet<string>? _trailDone;

    /// <summary>
    /// Once a poll, while playing: records a stage finished when the live objective's description moves on, a quest
    /// started when its first stage shows, and steps ticked since the last poll (a handful at most: more is a loaded save).
    /// </summary>
    public void Follow()
    {
        // Not around a load: the objective, ticks and position then belong to the save being matched, not to a spot.
        if (!_live || !_tracker.InGame || _tracker.LoadPending || DateTime.Now - _tracker.InGameSince < TimeSpan.FromSeconds(20)
            || _tracker.CurrentChapter is not { } chapter || chapter.Number != _tracker.DetectedChapter || _area.Position is not { } p || _area.Here is not { } here)
        {
            (_trailDone, _trailStage, _trailStep) = (null, null, null);
            return;
        }
        var spot = new GuidePoint(p.X, p.Y, p.Z, here.Area);
        bool changed = false;

        // Stages: the sub-objective when there is one, else the objective.
        var live = _tracker.LiveSubObjective ?? _tracker.LiveObjective;
        string? stage = live is null ? null : live.TitleKey + "|" + live.DescKey;
        // No objective for a moment (a VR battle, a load) is not a stage finished: the same one comes back after it.
        if (stage is not null && stage != _trailStage)
        {
            // A stage is finished when the same quest moves to its next text, not when another quest takes over.
            if (_trailStep is { } before && _trailStage is { } finished && live is not null && finished.StartsWith(live.TitleKey + "|"))
                changed |= Record(before, "stage " + finished, e => e.Stages.TryAdd(finished, spot));
            var step = TrailStepFor(chapter);
            if (step is not null && _rules.IsQuestOrEvent(step) && step != _trailStep)
                changed |= Record(step, "start", e => { if (e.Start is not null) return false; e.Start = spot; return true; });
            (_trailStage, _trailStep) = (stage, step);
        }

        // Steps ticked since the last poll: where they were done.
        var done = _tracker.Progress.Done.ToHashSet();
        if (_trailDone is not null)
        {
            var fresh = done.Except(_trailDone).ToList();
            if (fresh.Count is > 0 and <= 3)
                // Items where they arrived, discoveries as the game finishes them. Not story steps (ticked by flags at
                // the next autosave, wherever that is) nor Ch8's side quests (seen at the next objective search, later).
                foreach (var step in chapter.Objectives.Where(o => fresh.Contains(o.Id) && (_rules.IsItem(o) || _rules.IsEvent(o)
                    || (_rules.IsQuest(o) && _status.IsLiveQuest(o)))))
                    changed |= Record(step, "done", e => { if (e.Done is not null) return false; e.Done = spot; return true; });
        }
        _trailDone = done;
        if (changed) SaveTrail();

        bool Record(Objective step, string what, Func<TrailEntry, bool> put)
        {
            if (!_trail.TryGetValue(step.Id, out var entry)) _trail[step.Id] = entry = new TrailEntry();
            if (!put(entry)) return false;
            try
            {
                File.AppendAllText(Path.Combine(_logs, "trail.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tch {chapter.Number}\t{step.Id}\t{what}\t{here.Area}\t{here.Floor}\t{p.X:0}\t{p.Y:0}\t{p.Z:0}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return true;
        }
    }

    /// <summary>The guide step the live objective belongs to: a side quest or discovery by its title, else the story step.</summary>
    Objective? TrailStepFor(Chapter chapter)
    {
        if (_tracker.LiveObjective?.Title is not { Length: >= 3 } title) return null;
        return chapter.Objectives.FirstOrDefault(o => _rules.IsQuestOrEvent(o) && GuideRules.SameQuest(o, title))
            ?? chapter.Objectives.FirstOrDefault(o => _rules.IsStory(o) && GuideRules.NamedAs(o, title));
    }

    void SaveTrail()
    {
        try
        {
            Directory.CreateDirectory(_data);
            string temp = TrailFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_trail, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, TrailFile, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Where to point for a step, from the trail of an earlier playthrough: for the live quest the spot its current stage was
    /// finished at; for a quest not started yet where it was given; for anything else where it was done. Key names the spot
    /// (its area is checked once per key).
    /// </summary>
    (string Key, GuidePoint Point)? TrailSpot(Objective o)
    {
        if (!_trail.TryGetValue(o.Id, out var entry) || _tracker.CurrentChapter is not { } chapter) return null;
        bool live = TrailStepFor(chapter) == o;
        if (live && (_tracker.LiveSubObjective ?? _tracker.LiveObjective) is { } stage && entry.Stages.TryGetValue(stage.TitleKey + "|" + stage.DescKey, out var next))
            return (o.Id + "|" + stage.DescKey, next);
        if (!live && _rules.IsQuestOrEvent(o) && entry.Start is { } start) return (o.Id + "|start", start);
        return entry.Done is { } done ? (o.Id + "|done", done) : null;
    }

    /// <summary>
    /// Writes data\logs\items.log for items handed over in play (not a loaded save): time, chapter, id, name, count now,
    /// area, floor, where Cloud stands, the game state (battle, exploring, menu, cutscene) and the live objective. Where an item was
    /// picked up is a spot for points.json on the next playthrough.
    /// </summary>
    public void LogItems(IEnumerable<OwnedItem> items)
    {
        var p = _area.Position;
        var lines = items.Select(o => string.Join('\t', $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}", _tracker.DetectedChapter, o.Id, _names.Name(o.Id) ?? $"#{o.Id}", o.Count,
            _area.Here?.Area ?? "", _area.Here?.Floor ?? "", p is null ? "" : $"{p.X:0}", p is null ? "" : $"{p.Y:0}", p is null ? "" : $"{p.Z:0}",
            _reader.ReadGameState() switch { { Battle: true } => "battle", { Exploring: true } => "exploring", { Menu: true } => "menu", { Cutscene: true } => "cutscene", { } g => $"state {g.Code}", null => "" }, _tracker.LiveObjective?.Title ?? "")).ToList();
        if (lines.Count == 0) return;
        try
        {
            string file = Path.Combine(_logs, "items.log");
            if (!File.Exists(file)) File.WriteAllText(file, "time\tchapter\tid\tname\tcount\tarea\tfloor\tx\ty\tz\tstate\tobjective" + Environment.NewLine);
            File.AppendAllLines(file, lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Guide step id -> its spot, recorded standing there (position.log); loaded with the guide.</summary>
    public Dictionary<string, GuidePoint> Points { get; set; } = [];
    readonly Dictionary<string, (string? Area, DateTime When)> _pointArea = [];

    public static Dictionary<string, GuidePoint> LoadPoints(string file)
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
    /// How far Cloud is from the step's recorded spot. Only while the area volumes of the loaded map name that spot's
    /// area as recorded (asked again every 30 s): the same coordinates in another map would be a guess.
    /// </summary>
    public string? PointDistance(Objective o)
    {
        if (!_live || _area.Position is not { } p) return null;
        // The trail of an earlier playthrough first (MainWindow.Trail.cs), then a spot recorded by hand.
        if ((TrailSpot(o) ?? (Points.TryGetValue(o.Id, out var fixedPoint) ? (o.Id, fixedPoint) : null)) is not var (key, point)) return null;
        // Asked again after 30 s, or 5 s while unknown (the map's area volumes may still be loading).
        if (!_pointArea.TryGetValue(key, out var known) || DateTime.Now - known.When >= TimeSpan.FromSeconds(known.Area is null ? 5 : 30))
            _pointArea[key] = known = (_reader.ReadLocation(new GamePosition(point.X, point.Y, point.Z))?.Area, DateTime.Now);
        if (known.Area is null || !known.Area.Equals(point.Area, StringComparison.OrdinalIgnoreCase)) return null;
        return World.Metres(World.Distance(new GamePosition(point.X, point.Y, point.Z), p));
    }

    /// <summary>Reads the trail: once, when the overlay starts.</summary>
    public void Load()
    {
        foreach (var (id, entry) in LoadTrail()) _trail[id] = entry;
    }

    /// <summary>
    /// Ctrl+Shift+Alt+P: notes where Cloud stands (area, floor, X Y Z, the game's live objective) in data\&lt;id&gt;\points-recorded.tsv,
    /// to be named and moved into points.json for a guide step. Throws when the file cannot be written.
    /// </summary>
    public void RecordSpot(GamePosition p)
    {
        string file = Path.Combine(_data, "points-recorded.tsv");
        if (!File.Exists(file)) File.WriteAllText(file, "time\tchapter\tarea\tfloor\tx\ty\tz\tobjective" + Environment.NewLine);
        File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{_tracker.DetectedChapter}\t{_area.Here?.Area}\t{_area.Here?.Floor}\t{p.X:F0}\t{p.Y:F0}\t{p.Z:F0}\t{_tracker.LiveObjective?.Title}{Environment.NewLine}");
    }
}
