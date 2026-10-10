using System.IO;
using System.Text.Json;

namespace GameTracker;

public record Detect(string[]? Ocr);

/// <summary>One step of the linear guide. Warning marks a point of no return: what to finish before doing this step.
/// Progress is the story-progress counter value at which this story step starts, when known. Needs lists the steps
/// the warning is about: once they are all done, the warning has nothing left to say. Optional: on the way and
/// also sold in shops, so shown as a reminder only, never as missable. Hard: only in Hard mode (left out otherwise).
/// Where and Warning are the Indonesian originals (rules read only the area Where starts with; the rest is in columns
/// below); WhereEn and WarningEn are their English versions, shown in English (the original when there is none). After: the step that
/// makes this one available (Ch8 side quests open with "Requests for the Mercenary"); until it is done the step stays
/// out of the "in this area" banner, unless the game shows it as the live quest. Revisit: the step that lets you come
/// back for this one once its part of the story is behind you (Station Way opens again with the Ch8 hub); until then it
/// is not shown as left behind. RewardOf: the side quest or discovery (id) whose reward this step is: nothing to find
/// until it is done. Auto: handed over on its own, no searching: "chapter" (at the end of the chapter; for a trophy, the
/// chapter's completion trophy), "boss" (after a boss) or "yes". GameTitle: the game's own name for the quest when the
/// guide names it otherwise ("Discovery: X" is the game's "X"). Closes/ClosesEn: what closes behind the point of no
/// return, the warning's "Setelah ..." / "After ..." sentence.</summary>
public record Objective(string Id, string Type, string Name, string Where, bool Missable, Detect? Detect, string? Warning = null, int? Progress = null, string[]? Needs = null, bool Optional = false, bool Hard = false,
    string? WhereEn = null, string? WarningEn = null, string? After = null, string? Revisit = null,
    string? RewardOf = null, string? Auto = null, string? GameTitle = null, string? Closes = null, string? ClosesEn = null)
{
    public string ShownWhere => !Lang.Indonesian && WhereEn is not null ? WhereEn : Where;
    public string? ShownWarning => !Lang.Indonesian && WarningEn is not null ? WarningEn : Warning;
    public string? ShownCloses => Lang.Indonesian ? Closes : ClosesEn;
}

/// <summary>
/// A kind of guide step, defined per game in game.json "stepTypes" (key = the guide's "type"). Role tells the overlay's
/// rules what it is: "story" (the main line; its steps split the chapter into phases), "quest" (side quest, matched with
/// the game's quest page), "event" (discovery or other event), "item" (handed over by the game: ticked from the
/// inventory), "trophy" (not counted). En: the English name shown (the key otherwise). Icon and Color (#RRGGBB): the
/// compact marker. NeverLost: an item that cannot be sold or used up, so a loaded save without it has not got it yet.
/// Unique: one per playthrough, so owning it means the step is done wherever the guide lists it.
/// </summary>
public sealed record StepType(string? Role = null, string? En = null, string? Icon = null, string? Color = null, bool NeverLost = false, bool Unique = false);

public record Chapter(int Number, string Title, string? PointOfNoReturn, Objective[] Objectives);

public record Guide(string Game, string Status, string[] Notes, Chapter[] Chapters)
{
    /// <summary>The guide for one mode: outside Hard mode its Hard-only steps are left out everywhere.</summary>
    public Guide ForMode(bool hard) => hard ? this
        : this with { Chapters = Chapters.Select(c => c with { Objectives = c.Objectives.Where(o => !o.Hard).ToArray() }).ToArray() };

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static Guide Load(string path) =>
        JsonSerializer.Deserialize<Guide>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException(Lang.T($"Empty guide: {path}", $"Panduan kosong: {path}"));
}
