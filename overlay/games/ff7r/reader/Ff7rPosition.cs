namespace GameTracker;

/// <summary>
/// Where the controlled character stands, in the game's world units (cm), read-only from memory.
///
/// The controlled character's actor is [[module + 0x53DD150] + 0x60]. Every character has a position object (one
/// class, about a dozen instances: party, NPCs, enemies) whose owner actor is at +0x20 and whose feet position is a
/// float X, Y, Z at +0x160; the player's is the one owned by that actor. The objects are picked up by their vtable
/// during the objective search. Found and checked across a battle, a room change and a game restart on Steam 1.0.0.7
/// (research\notes.md, "Posisi pemain"); other versions read no position.
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long PlayerRootRva = 0x53DD150, PositionVtableRva = 0x4DD7148;

    long PositionVtable => Version == "Steam 1.0.0.7" ? (long)_moduleBase + PositionVtableRva : 0;

    /// <summary>Position objects seen at the last objective search, and the one owned by the controlled actor.</summary>
    List<long> _positionObjects = new();
    long _playerPosition;
    DateTime _positionSearch;


    /// <summary>The controlled character's position, or null when unknown (another version, a menu, not found yet).</summary>
    public GamePosition? ReadPosition()
    {
        if (!Attach() || PositionVtable == 0) return null;
        long root = ReadInt64((long)_moduleBase + PlayerRootRva);
        long actor = root == 0 ? 0 : ReadInt64(root + 0x60);
        if (actor == 0) return null;
        if (!OwnedBy(_playerPosition, actor))
        {
            // Another character taken over, or the objects were made anew (a load): pick the one owned by this actor.
            _playerPosition = _positionObjects.FirstOrDefault(o => OwnedBy(o, actor));
            if (_playerPosition == 0)
            {
                // Not among the objects of the last search: look again, at most every minute.
                if (DateTime.Now - _positionSearch > TimeSpan.FromMinutes(1))
                {
                    _positionSearch = DateTime.Now;
                    _objectiveSearch ??= Scan(SafeFindObjectives);
                }
                return null;
            }
        }
        var b = new byte[12];
        if (!ReadProcessMemory(_handle, (IntPtr)(_playerPosition + 0x160), b, b.Length, out _)) return null;
        var p = new GamePosition(BitConverter.ToSingle(b, 0), BitConverter.ToSingle(b, 4), BitConverter.ToSingle(b, 8));
        return float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) ? p : null;
    }

    bool OwnedBy(long o, long actor) => o != 0 && ReadInt64(o) == PositionVtable && ReadInt64(o + 0x20) == actor;

    /// <summary>
    /// Unreal's world seen from above (Z up, left-handed): +X east, +Y south, so the map's north is -Y. Assumed 11 Oct 2026
    /// from the engine's axes; to be checked against the game's map screen (radar).
    /// </summary>
    public (double East, double North) OnMap(GamePosition p) => (p.X, -p.Y);

    readonly byte[] _rotation = new byte[16];

    /// <summary>
    /// The way the controlled character faces: the rotation (quaternion x, y, z, w) at +0x1A0 of its position object, just
    /// before the copy of its position (+0x1B0), as a heading on the map (OnMap). Null before ReadPosition found the object,
    /// or when the four floats are not a rotation.
    /// </summary>
    public double? ReadHeading()
    {
        if (_playerPosition == 0 || !Attach() || !ReadProcessMemory(_handle, (IntPtr)(_playerPosition + 0x1A0), _rotation, _rotation.Length, out _)) return null;
        double x = BitConverter.ToSingle(_rotation, 0), y = BitConverter.ToSingle(_rotation, 4), z = BitConverter.ToSingle(_rotation, 8), w = BitConverter.ToSingle(_rotation, 12);
        double norm = x * x + y * y + z * z + w * w;
        if (!double.IsFinite(norm) || Math.Abs(norm - 1) > 0.01) return null;
        double yaw = Math.Atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
        var (east, north) = OnMap(new GamePosition((float)Math.Cos(yaw), (float)Math.Sin(yaw), 0));
        double heading = Math.Atan2(east, north) * 180 / Math.PI;
        return heading < 0 ? heading + 360 : heading;
    }
}
