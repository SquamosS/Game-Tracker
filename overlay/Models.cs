using System.IO;
using System.Text.Json;

namespace GameTracker;

public record Detect(string[]? Ocr);

public record Objective(string Id, string Type, string Name, string Where, bool Missable, Detect? Detect);

public record Chapter(int Number, string Title, string? PointOfNoReturn, Objective[] Objectives);

public record Guide(string Game, string Status, string[] Notes, Chapter[] Chapters)
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static Guide Load(string path) =>
        JsonSerializer.Deserialize<Guide>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Panduan kosong: {path}");
}
