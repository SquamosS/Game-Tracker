using System.Reflection;

namespace GameTracker;

/// <summary>A point in the game world, in the game's own units (IGameReader.UnitsPerMetre; FF7R: centimetres).</summary>
public sealed record GamePosition(float X, float Y, float Z);

/// <summary>The named area a point lies in (as the game names it), and its floor or district when it has one.</summary>
public sealed record GameLocation(string Area, string? Floor);

/// <summary>
/// One stage of a quest or story objective as the game shows it. Row: the game's own id for this stage (equal rows =
/// same stage); Order: story order of the stage among the game's objectives; TitleKey/DescKey: the game's text keys
/// for title and description (one quest shares its title key across stages); Title/Text: their text, when read;
/// Finished: the stage is the quest's last (cleared); Sub: a sub-objective of another (its title key starts with the
/// other's); Later: left for later ("take a look the next time you're in the area"), not the quest being played.
/// </summary>
public sealed record GameObjective(long Row, int Order, string TitleKey, string DescKey, string? Title, string? Text, bool Finished,
    bool Sub = false, bool Later = false);

/// <summary>
/// One inventory record; Slot is where it lives (keeps holding the same id unless something new is put there), Obtained
/// when it was obtained (Unix seconds; 0 = not known).
/// </summary>
public sealed record OwnedItem(int Id, int Count, uint Obtained, long Slot = 0);

/// <summary>
/// A side quest as the game's quest page lists it: title, the game's quest id and stage, whether it is cleared, whether
/// it is taken and not cleared yet (under way), and the stage's description as the game shows it (null = not read).
/// </summary>
public sealed record SideQuest(string Title, string Quest, string Stage, bool Finished, bool UnderWay = false, string? Text = null);

/// <summary>Someone standing in the loaded level who gives a side quest of the quest page not taken yet: its title (SideQuest.Title), where they stand.</summary>
public sealed record QuestGiver(string Title, GamePosition At);

/// <summary>An object standing in the loaded level (chest, pickup...), by its class name.</summary>
public sealed record FieldActor(string Class, GamePosition At);

/// <summary>
/// A chest (or other pickup point) of the loaded maps: id, where it stands (null = not known), the item ids it holds,
/// and the game's own flag number for it (logs only).
/// </summary>
public sealed record GameChest(string Id, GamePosition? At, int[] Items, int? Flag = null);

/// <summary>
/// What the game is doing: exploring, in a menu, a cutscene or a battle (at most one is true; none = something else).
/// Code: the game's own state number; Detail: every raw value read, for the state log. Equal records = nothing changed.
/// </summary>
public sealed record GameState(bool Exploring, bool Menu, bool Cutscene, bool Battle, int Code, string Detail);

/// <summary>
/// The game's items and flags as guide steps know them: names shipped with the game's folder and learned while playing
/// (an item, flag or objective that arrived right before you ticked a step), and what kind of item an id is. Null = not known.
/// </summary>
public interface IGameNames
{
    string? Name(int id);
    string? FlagName(string flag);
    void Learn(int id, string name);
    void LearnFlag(string flag, string name);

    /// <summary>The guide step (id) learned for an objective's title key, from LearnObjective.</summary>
    string? ObjectiveStep(string titleKey);
    void LearnObjective(string titleKey, string stepId);

    /// <summary>Money: always owned, never a guide step (its name may be part of an item's, "Gil Up").</summary>
    bool IsCurrency(int id);

    /// <summary>Used up (potions...): never a guide step, so never learned as one.</summary>
    bool IsConsumable(int id);

    /// <summary>Whether an unknown item of this id could be what a guide step of this type gives (learning item names).</summary>
    bool FitsStep(int id, string stepType);

    /// <summary>The shorter name a guide may use for the item ("Shiva" for "Shiva Materia"), null when it has none.</summary>
    string? ShortName(string itemName);
}

/// <summary>
/// Everything the shared overlay asks a game's live reader (games\&lt;id&gt;\reader\, picked by game.json "reader").
/// A reader may fill only part of it: null (or an empty list) means "not known", and the feature that needs it stays
/// off. Read only, from the UI thread once a second: each call must be cheap (big scans on a worker thread).
/// A new reader derives from GameReaderBase and overrides what it knows. A member added here later gets a default
/// "not known" body (and a virtual one in GameReaderBase), so the readers already written need no change.
/// </summary>
public interface IGameReader : IDisposable
{
    /// <summary>The game build once attached ("Steam 1.0.0.7"), null while the game is not running.</summary>
    string? Version { get; }

    /// <summary>Set when the game runs but cannot be read (unknown version, no access).</summary>
    string? Problem { get; }

    /// <summary>How many of the game's position units make a metre (Unreal: 100, centimetres). Default: 1, metres.</summary>
    double UnitsPerMetre => 1;

    /// <summary>The chapter being played, null outside a game (title screen, menus, between chapters).</summary>
    int? ReadChapter();

    GameState? ReadGameState();

    GamePosition? ReadPosition();

    /// <summary>The area a point lies in (the player's, a chest's...).</summary>
    GameLocation? ReadLocation(GamePosition p);

    /// <summary>Objects standing in the loaded level now.</summary>
    IReadOnlyList<FieldActor>? ReadFieldActors();

    /// <summary>Who gives the side quests the quest page lists and that are not taken yet, standing in the level now. Null = not known.</summary>
    IReadOnlyList<QuestGiver>? ReadQuestGivers() => null;

    IGameNames Names { get; }

    /// <summary>Every inventory record (empty slots too, id 0: a new item may go there), null while not found yet.</summary>
    List<OwnedItem>? ReadOwned();

    /// <summary>Ids owned by the save just loaded (changedSlots: the slots that changed at the load). Null = not known (yet).</summary>
    HashSet<int>? ReadLiveOwnedIds(IReadOnlyCollection<long> changedSlots);

    /// <summary>How often to look again for the inventory (shorter while an item is expected).</summary>
    TimeSpan ListRefresh { get; set; }

    /// <summary>Look for the inventory again right away (the objective changed: rewards often come with it).</summary>
    void RefreshListsSoon();

    /// <summary>Completion flags of the save being played, as text keys the names map (IGameNames.FlagName) knows.</summary>
    HashSet<string>? ReadFlags();

    /// <summary>The live story objective of the chapter, null when not known.</summary>
    GameObjective? ReadObjective(int chapter);

    /// <summary>The objective the game started last (its own order), null when it cannot tell (then the guide order decides).</summary>
    GameObjective? NewestObjective();

    /// <summary>The live sub-objective of an objective ("Find Stamp" › "Train Yard Security"), if any.</summary>
    GameObjective? SubObjectiveOf(GameObjective objective);

    /// <summary>Every objective stage some entry of the game points at, from the last ReadObjective.</summary>
    IReadOnlyList<GameObjective> Candidates { get; }

    /// <summary>Candidates with where they came from: the game's entry address and its parent (pick order, choice log).</summary>
    IReadOnlyList<(GameObjective Objective, long Slot, long Parent)> CandidateSlots { get; }

    /// <summary>The side quests the game's quest page lists now (empty = none, or not read since the last load).</summary>
    IReadOnlyList<SideQuest> SideQuests { get; }

    /// <summary>A save was loaded: drop the side quests read from the one before.</summary>
    void ForgetSideQuests();

    /// <summary>The chests of the maps loaded now.</summary>
    IReadOnlyList<GameChest> Chests { get; }

    /// <summary>Whether the save being played has opened the chest; null = not known.</summary>
    bool? ChestOpened(GameChest chest);

    /// <summary>
    /// Whether the chest stands in the level now as far as quests go: a quest's pick-up kept with the chests shows only
    /// while that quest is live (live: the objective the overlay follows). Plain chests always do.
    /// </summary>
    bool ChestShown(GameChest chest, GameObjective? live);

    /// <summary>A save was loaded: drop what was read from the one before (asked again).</summary>
    void ForgetChestCopy();
}

/// <summary>Marks a game's reader with the id game.json names in "reader" (one class per id).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GameReaderAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

public static class GameReaders
{
    /// <summary>The reader game.json names, or null when there is none (the overlay is then ticked by hand).</summary>
    public static IGameReader? Create(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var type = typeof(GameReaders).Assembly.GetTypes().FirstOrDefault(t => typeof(IGameReader).IsAssignableFrom(t) && !t.IsAbstract
            && t.GetCustomAttribute<GameReaderAttribute>()?.Id.Equals(id, StringComparison.OrdinalIgnoreCase) == true);
        return type is null ? null : (IGameReader?)Activator.CreateInstance(type);
    }
}

/// <summary>
/// Base for a new game's names (see GameReaderBase): every answer "not known" until overridden. A reader that knows no
/// names uses GameNamesBase.None.
/// </summary>
public class GameNamesBase : IGameNames
{
    public static readonly IGameNames None = new GameNamesBase();

    public virtual string? Name(int id) => null;
    public virtual string? FlagName(string flag) => null;
    public virtual void Learn(int id, string name) { }
    public virtual void LearnFlag(string flag, string name) { }
    public virtual string? ObjectiveStep(string titleKey) => null;
    public virtual void LearnObjective(string titleKey, string stepId) { }
    public virtual bool IsCurrency(int id) => false;
    public virtual bool IsConsumable(int id) => false;
    public virtual bool FitsStep(int id, string stepType) => false;
    public virtual string? ShortName(string itemName) => null;
}

/// <summary>
/// Base for a new game's reader (any engine: Unreal, Unity...): every answer is "not known" (null, empty, nothing done)
/// until the reader overrides it, so it fills in only what it can read and every other feature stays off for that game.
/// "override" makes the compiler check each answer's name and type. (FF7R implements IGameReader directly.)
/// Rule for growing the interface: a new member of IGameReader gets a default "not known" body there and a virtual one
/// here, so existing readers (FF7R) keep compiling and working unchanged.
/// </summary>
public abstract class GameReaderBase : IGameReader
{
    public virtual string? Version => null;
    public virtual string? Problem => null;
    public virtual double UnitsPerMetre => 1;
    public virtual int? ReadChapter() => null;
    public virtual GameState? ReadGameState() => null;
    public virtual GamePosition? ReadPosition() => null;
    public virtual GameLocation? ReadLocation(GamePosition p) => null;
    public virtual IReadOnlyList<FieldActor>? ReadFieldActors() => null;
    public virtual IReadOnlyList<QuestGiver>? ReadQuestGivers() => null;
    public virtual IGameNames Names => GameNamesBase.None;
    public virtual List<OwnedItem>? ReadOwned() => null;
    public virtual HashSet<int>? ReadLiveOwnedIds(IReadOnlyCollection<long> changedSlots) => null;
    public virtual TimeSpan ListRefresh { get; set; }
    public virtual void RefreshListsSoon() { }
    public virtual HashSet<string>? ReadFlags() => null;
    public virtual GameObjective? ReadObjective(int chapter) => null;
    public virtual GameObjective? NewestObjective() => null;
    public virtual GameObjective? SubObjectiveOf(GameObjective objective) => null;
    public virtual IReadOnlyList<GameObjective> Candidates => [];
    public virtual IReadOnlyList<(GameObjective Objective, long Slot, long Parent)> CandidateSlots => [];
    public virtual IReadOnlyList<SideQuest> SideQuests => [];
    public virtual void ForgetSideQuests() { }
    public virtual IReadOnlyList<GameChest> Chests => [];
    public virtual bool? ChestOpened(GameChest chest) => null;
    public virtual bool ChestShown(GameChest chest, GameObjective? live) => true;
    public virtual void ForgetChestCopy() { }
    public virtual void Dispose() { }
}

/// <summary>A game without a live reader: nothing is known, every feature that needs the game stays off.</summary>
public sealed class NoGameReader : GameReaderBase;
