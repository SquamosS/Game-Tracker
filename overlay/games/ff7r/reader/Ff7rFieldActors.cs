using System.Buffers;

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
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long FieldActorVtableRva = 0x4C23E60, LevelVtableRva = 0x4E6BEC0;

    public sealed record FieldActor(string Class, Position At);

    IReadOnlyList<FieldActor>? _fieldActors;
    DateTime _fieldActorsAt;
    readonly Dictionary<long, string> _fieldClassNames = new();

    /// <summary>The field objects in the level, or null when they cannot be read (another version, a load).</summary>
    public IReadOnlyList<FieldActor>? ReadFieldActors()
    {
        if (Version != "Steam 1.0.0.7" || !Attach()) return null;
        if (DateTime.Now - _fieldActorsAt < TimeSpan.FromSeconds(3)) return _fieldActors;
        _fieldActorsAt = DateTime.Now;
        long module = (long)_moduleBase;
        long root = ReadInt64(module + PlayerRootRva);
        long actor = root == 0 ? 0 : ReadInt64(root + 0x60);
        long level = actor == 0 ? 0 : ReadInt64(actor + 0x20);
        if (level == 0 || ReadInt64(level) != module + LevelVtableRva) return _fieldActors = null;
        long list = ReadInt64(level + 0xA0);
        int count = ReadInt32(level + 0xA8);
        if (list == 0 || count is <= 0 or > 20000) return _fieldActors = null;
        var buffer = ArrayPool<byte>.Shared.Rent(count * 8);
        try
        {
            if (!ReadProcessMemory(_handle, (IntPtr)list, buffer, count * 8, out _)) return _fieldActors = null;
            long vtable = module + FieldActorVtableRva;
            var found = new List<FieldActor>();
            var xyz = new byte[12];
            for (int i = 0; i < count; i++)
            {
                long a = BitConverter.ToInt64(buffer, i * 8);
                if (a == 0 || ReadInt64(a) != vtable) continue;
                long cls = ReadInt64(a + 0x10);
                if (!_fieldClassNames.TryGetValue(cls, out var name)) _fieldClassNames[cls] = name = FName(ReadInt32(cls + 0x18));
                long component = ReadInt64(a + 0x160);
                if (component == 0 || !ReadProcessMemory(_handle, (IntPtr)(component + 0x1B0), xyz, xyz.Length, out _)) continue;
                var p = new Position(BitConverter.ToSingle(xyz, 0), BitConverter.ToSingle(xyz, 4), BitConverter.ToSingle(xyz, 8));
                if (float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)) found.Add(new FieldActor(name, p));
            }
            return _fieldActors = found;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
