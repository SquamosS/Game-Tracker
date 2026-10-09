using System.IO;
using System.Text.Json;

namespace GameTracker;

public record Detect(string[]? Ocr);

/// <summary>One step of the linear guide. Warning marks a point of no return: what to finish before doing this step.
/// Progress is the story-progress counter value at which this story step starts, when known. Needs lists the steps
/// the warning is about: once they are all done, the warning has nothing left to say. Optional: on the way and
/// also sold in shops, so shown as a reminder only, never as missable. Hard: only in Hard mode (left out otherwise).</summary>
public record Objective(string Id, string Type, string Name, string Where, bool Missable, Detect? Detect, string? Warning = null, int? Progress = null, string[]? Needs = null, bool Optional = false, bool Hard = false);

public record Chapter(int Number, string Title, string? PointOfNoReturn, Objective[] Objectives);

public record Guide(string Game, string Status, string[] Notes, Chapter[] Chapters)
{
    /// <summary>The guide for one mode: outside Hard mode its Hard-only steps are left out everywhere.</summary>
    public Guide ForMode(bool hard) => hard ? this
        : this with { Chapters = Chapters.Select(c => c with { Objectives = c.Objectives.Where(o => !o.Hard).ToArray() }).ToArray() };

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static Guide Load(string path) =>
        JsonSerializer.Deserialize<Guide>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Panduan kosong: {path}");
}
