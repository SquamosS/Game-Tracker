using System.Buffers;
using System.Text.RegularExpressions;

namespace GameTracker;

/// <summary>
/// The field objects standing in the level now: chests, pick-ups and props (classes "FA0123_00_TreasureboxSlum_Standard_C",
/// "FA0001_00_Aerithflower_Standard_C"...), with their positions. A chest of the game's tables whose object is not there
/// has not been placed yet (MP Up in Aerith's garden before the Rude fight, Chapter 14's chests during Chapter 8).
///
/// The controlled actor ([[module + 0x53DD150] + 0x60], Ff7rPosition.cs) has the PersistentLevel (vtable module+0x4E6BEC0)
/// as its outer at +0x20; the level's actors are a TArray at +0xA0. Every field object shares one vtable (module+0x4C23E60);
/// its class name is the class's FName at +0x18, its position [actor + 0x160] + 0x1B0 (float X, Y, Z). All of them live in
/// the PersistentLevel (checked 10 Oct 2026 in Ch8: 23 of them, 6 chests). One read of the pointer list and one small read
/// per actor, at most every 3 s. Steam 1.0.0.7 only.
///
/// Quest givers, in the same list: a person of the level carries a struct (vtable module+0x4B24CA0) at +0x820 whose FName
/// at +0xF80 (actor + 0x820 + 0x760) is the game's id for them; a side quest's giver is "oba080_20_qst02_Client" (map 080,
/// quest q02 of the quest page: Ms. Folia for Kids on Patrol), position [actor + 0x160] + 0x1B0 as above. A giver stands
/// in the level only while their quest can be taken or is under way (Damon and q06's old man were missing while their
/// quests were "???"). Found 11 Oct 2026 in Ch8 with the scanner (fnames, hex, base, uobjs). Each actor is asked once
/// while the level's actor list keeps its length.
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long FieldActorVtableRva = 0x4C23E60, LevelVtableRva = 0x4E6BEC0, PersonVtableRva = 0x4B24CA0;

    static readonly Regex QuestClient = new(@"^oba(\d{3})_\d+_qst(\d+)_Client$", RegexOptions.Compiled);

    IReadOnlyList<FieldActor>? _fieldActors;
    DateTime _fieldActorsAt;
    readonly Dictionary<long, string> _fieldClassNames = new();

    /// <summary>Quest givers in the level ("080", "02", where they stand), from the last ReadFieldActors; null = not known.</summary>
    IReadOnlyList<(string Map, string Quest, GamePosition At)>? _questClients;
    /// <summary>Per actor of the level (address -> its vtable, and its "map|quest" when it gives one), kept while the list's length holds.</summary>
    readonly Dictionary<long, (long Vtable, string? Quest)> _personIds = new();
    (long List, int Count) _personList;

    /// <summary>The field objects in the level, or null when they cannot be read (another version, a load).</summary>
    public IReadOnlyList<FieldActor>? ReadFieldActors()
    {
        if (Version != "Steam 1.0.0.7" || !Attach()) return null;
        if (DateTime.Now - _fieldActorsAt < TimeSpan.FromSeconds(3)) return _fieldActors;
        _fieldActorsAt = DateTime.Now;
        _questClients = null;
        long module = (long)_moduleBase;
        long root = ReadInt64(module + PlayerRootRva);
        long actor = root == 0 ? 0 : ReadInt64(root + 0x60);
        long level = actor == 0 ? 0 : ReadInt64(actor + 0x20);
        if (level == 0 || ReadInt64(level) != module + LevelVtableRva) return _fieldActors = null;
        long list = ReadInt64(level + 0xA0);
        int count = ReadInt32(level + 0xA8);
        if (list == 0 || count is <= 0 or > 5000) return _fieldActors = null;
        // Actors came or went (a spawn, another level): ask each one again.
        if (_personList != (list, count)) { _personIds.Clear(); _personList = (list, count); }
        var buffer = ArrayPool<byte>.Shared.Rent(count * 8);
        try
        {
            if (!ReadProcessMemory(_handle, (IntPtr)list, buffer, count * 8, out _)) return _fieldActors = null;
            long vtable = module + FieldActorVtableRva;
            var found = new List<FieldActor>();
            var clients = new List<(string, string, GamePosition)>();
            var xyz = new byte[12];
            for (int i = 0; i < count; i++)
            {
                long a = BitConverter.ToInt64(buffer, i * 8);
                if (a == 0) continue;
                long actorVtable = ReadInt64(a);
                if (actorVtable != vtable)
                {
                    if (QuestOf(a, actorVtable, module) is { } quest && PositionOf(a, xyz) is { } at)
                        clients.Add((quest[..3], quest[4..], at));
                    continue;
                }
                long cls = ReadInt64(a + 0x10);
                if (cls == 0) continue;
                if (!_fieldClassNames.TryGetValue(cls, out var name)) _fieldClassNames[cls] = name = FName(ReadInt32(cls + 0x18));
                if (PositionOf(a, xyz) is { } p) found.Add(new FieldActor(name, p));
            }
            _questClients = clients;
            // None at all is a level still filling up (a load), not a level without chests: unknown.
            return _fieldActors = found.Count > 0 ? found : null;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>"080|02" when the actor gives side quest q02 of map 080, null for anyone (or anything) else.</summary>
    string? QuestOf(long actor, long actorVtable, long module)
    {
        if (_personIds.TryGetValue(actor, out var known) && known.Vtable == actorVtable) return known.Quest;
        string? quest = null;
        if (actorVtable != 0 && ReadInt64(actor + 0x820) == module + PersonVtableRva && ReadInt32(actor + 0xF84) == 0
            && QuestClient.Match(FName(ReadInt32(actor + 0xF80))) is { Success: true } m)
            quest = m.Groups[1].Value + "|" + m.Groups[2].Value;
        _personIds[actor] = (actorVtable, quest);
        return quest;
    }

    /// <summary>Where an actor stands: [actor + 0x160] + 0x1B0 (xyz: a 12-byte buffer for the read).</summary>
    GamePosition? PositionOf(long actor, byte[] xyz)
    {
        long component = ReadInt64(actor + 0x160);
        if (component == 0 || !ReadProcessMemory(_handle, (IntPtr)(component + 0x1B0), xyz, xyz.Length, out _)) return null;
        var p = new GamePosition(BitConverter.ToSingle(xyz, 0), BitConverter.ToSingle(xyz, 4), BitConverter.ToSingle(xyz, 8));
        return float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) ? p : null;
    }

    /// <summary>
    /// Givers of the quest page's side quests that are not taken yet (stage 00), standing in the level now; the quest
    /// page's "080_SLU5B_q02" is the giver "oba080_..._qst02_Client". Null when the level cannot be read.
    /// </summary>
    public IReadOnlyList<QuestGiver>? ReadQuestGivers()
    {
        ReadFieldActors();
        if (_questClients is not { } clients) return null;
        var givers = new List<QuestGiver>();
        foreach (var side in SideQuests)
        {
            if (side.Finished || side.Stage != "00") continue;
            var at = clients.Where(c => side.Quest.StartsWith(c.Map + "_") && side.Quest.EndsWith("_q" + c.Quest)).Select(c => c.At).ToList();
            // Two people named for one quest: which one to walk to is a guess.
            if (at.Count == 1) givers.Add(new QuestGiver(side.Title, at[0]));
        }
        return givers;
    }
}
