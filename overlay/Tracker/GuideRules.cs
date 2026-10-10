using System.Text.RegularExpressions;

namespace GameTracker;

/// <summary>
/// The guide's rules that need neither the game nor a window: what a step is (its type's role from game.json
/// "stepTypes"), how the game's names match steps, which steps are rewards, which area a step is in. Same input, same
/// answer: tested in tools\tracker-tests without the overlay running.
/// </summary>
public sealed class GuideRules(IReadOnlyDictionary<string, StepType> types, IGameNames names)
{
    static readonly StepType NoType = new();

    public StepType TypeOf(Objective o) => types.GetValueOrDefault(o.Type) ?? NoType;
    public bool IsStory(Objective o) => TypeOf(o).Role == "story";
    public bool IsTrophy(Objective o) => TypeOf(o).Role == "trophy";
    public bool IsQuest(Objective o) => TypeOf(o).Role == "quest";
    public bool IsEvent(Objective o) => TypeOf(o).Role == "event";
    public bool IsQuestOrEvent(Objective o) => TypeOf(o).Role is "quest" or "event";
    /// <summary>An item the game hands over: ticked from the inventory.</summary>
    public bool IsItem(Objective o) => TypeOf(o).Role == "item";

    /// <summary>The item's name, or its short name ("Shiva" for "Shiva Materia", IGameNames.ShortName), is in the step's.</summary>
    public bool Matches(Objective step, string name) =>
        step.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
        || (names.ShortName(name) is { } shortName && step.Name.Contains(shortName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The step is that item: the same name, or its short name ("Shiva" for "Shiva Materia"; not "Turbo Ether" for "Ether").</summary>
    public bool SameItem(Objective step, string name) =>
        step.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        || (names.ShortName(name) is { } shortName && step.Name.Equals(shortName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Story steps carry the game's quest names; one step may cover several ("A / B").</summary>
    public static bool NamedAs(Objective step, string title) =>
        step.Name.Split(" / ").Any(part => part.Trim().Equals(title, StringComparison.OrdinalIgnoreCase));

    /// <summary>The game's name for the quest is the step's (GameTitle when the guide names it otherwise: "Discovery: X" is "X").</summary>
    public static bool SameQuest(Objective step, string title) =>
        (step.GameTitle ?? step.Name).Trim().StartsWith(title, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The side quest or discovery of the chapter whose reward this step is (guide column rewardOf: Nail Bat is the reward
    /// of Kids on Patrol). Nothing to find in the area until the quest is done, and the quest itself shows there already.
    /// </summary>
    public static Objective? RewardOf(Objective o, Chapter chapter) =>
        o.RewardOf is { } id ? chapter.Objectives.FirstOrDefault(q => q.Id == id && q != o) : null;

    /// <summary>
    /// Items handed over automatically need no searching (guide column auto): "REWARD BOSS" (after a boss), "REWARD
    /// CHAPTER" (at the end of the chapter) or "REWARD" (otherwise automatic). Null for anything you have to find yourself.
    /// </summary>
    public string? RewardTag(Objective o) => IsStory(o) || IsTrophy(o) || o.Optional ? null : o.Auto switch
    {
        null => null,
        "chapter" => "REWARD CHAPTER",
        "boss" => "REWARD BOSS",
        _ => "REWARD",
    };

    /// <summary>The trophy that comes with finishing the chapter (auto "chapter").</summary>
    public static bool ChapterEndTrophy(Objective o) => o.Auto == "chapter";

    /// <summary>
    /// The area a step's "where" starts with, as the guide writes it: "Connecting Passageway (B5): ..." gives the area
    /// and the floor hint B5; null when the text does not start with an area.
    /// </summary>
    public static (string Area, string? Floor)? AreaOf(Objective o)
    {
        var m = AreaPattern.Match(o.Where);
        if (!m.Success) return null;
        var floor = FloorPattern.Match(m.Groups[2].Value);
        return (m.Groups[1].Value.Trim(), floor.Success ? floor.Value : null);
    }

    static readonly Regex AreaPattern = new(@"^([^:(]{3,60}?)\s*(?:\(([^)]*)\))?\s*:", RegexOptions.Compiled),
        FloorPattern = new(@"\bB\d+\b", RegexOptions.Compiled);
}
