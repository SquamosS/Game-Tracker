using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>
/// The trail: where things happened in this playthrough, kept per guide step in data\trail.json so the next one can point
/// the way. A side quest or discovery records where it started (who gave it), where each of its stages was finished (the
/// game's objective text moving on) and where it was done; an item where it was picked up; a story step its stages too.
/// Only the first recording of each spot is kept. Every recording also goes to data\logs\trail.log.
/// </summary>
public partial class MainWindow
{
    public sealed class TrailEntry
    {
        public GuidePoint? Start { get; set; }
        public Dictionary<string, GuidePoint> Stages { get; set; } = [];
        public GuidePoint? Done { get; set; }
    }

    string TrailFile => Path.Combine(_data, "trail.json");
    readonly Dictionary<string, TrailEntry> _trail;

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
    void FollowTrail()
    {
        // Not around a load: the objective, ticks and position then belong to the save being matched, not to a spot.
        if (!_live || !_inGame || _reconcile || _storyMayGoBack || DateTime.Now - _inGameSince < TimeSpan.FromSeconds(20)
            || CurrentChapter is not { } chapter || chapter.Number != _detectedChapter || _herePosition is not { } p || _here is not { } here)
        {
            (_trailDone, _trailStage, _trailStep) = (null, null, null);
            return;
        }
        var spot = new GuidePoint(p.X, p.Y, p.Z, here.Area);
        bool changed = false;

        // Stages: the sub-objective when there is one, else the objective.
        var live = _subObjective ?? _objective;
        string? stage = live is null ? null : live.TitleKey + "|" + live.DescKey;
        // No objective for a moment (a VR battle, a load) is not a stage finished: the same one comes back after it.
        if (stage is not null && stage != _trailStage)
        {
            // A stage is finished when the same quest moves to its next text, not when another quest takes over.
            if (_trailStep is { } before && _trailStage is { } finished && live is not null && finished.StartsWith(live.TitleKey + "|"))
                changed |= Record(before, "stage " + finished, e => e.Stages.TryAdd(finished, spot));
            var step = TrailStepFor(chapter);
            if (step is not null && IsQuestOrEvent(step) && step != _trailStep)
                changed |= Record(step, "start", e => { if (e.Start is not null) return false; e.Start = spot; return true; });
            (_trailStage, _trailStep) = (stage, step);
        }

        // Steps ticked since the last poll: where they were done.
        var done = _progress.Done.ToHashSet();
        if (_trailDone is not null)
        {
            var fresh = done.Except(_trailDone).ToList();
            if (fresh.Count is > 0 and <= 3)
                // Items where they arrived, discoveries as the game finishes them. Not story steps (ticked by flags at
                // the next autosave, wherever that is) nor Ch8's side quests (seen at the next objective search, later).
                foreach (var step in chapter.Objectives.Where(o => fresh.Contains(o.Id) && (IsItem(o) || IsEvent(o)
                    || (IsQuest(o) && IsLiveQuest(o)))))
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
        if (_objective?.Title is not { Length: >= 3 } title) return null;
        return chapter.Objectives.FirstOrDefault(o => IsQuestOrEvent(o) && SameQuest(o, title))
            ?? chapter.Objectives.FirstOrDefault(o => IsStory(o) && NamedAs(o, title));
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
        if (!_trail.TryGetValue(o.Id, out var entry) || CurrentChapter is not { } chapter) return null;
        bool live = TrailStepFor(chapter) == o;
        if (live && (_subObjective ?? _objective) is { } stage && entry.Stages.TryGetValue(stage.TitleKey + "|" + stage.DescKey, out var next))
            return (o.Id + "|" + stage.DescKey, next);
        if (!live && IsQuestOrEvent(o) && entry.Start is { } start) return (o.Id + "|start", start);
        return entry.Done is { } done ? (o.Id + "|done", done) : null;
    }
}
