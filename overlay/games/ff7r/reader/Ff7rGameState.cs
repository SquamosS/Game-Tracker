namespace GameTracker;

/// <summary>
/// Whether the game is being played, sits in a menu or shows a cutscene, from static values in the game module
/// (Steam 1.0.0.7; research\notes.md, "Menu / pause"). module+0x57E9ABB tells them apart: 1 exploring, 0 in battle,
/// 3 in any menu (main menu, map, a cutscene's pause menu, a battle's command menu), 5 during a cutscene. module+0x5A06764 (2 in the world, 3 in a menu) and
/// module+0x59039B8 (1 in menus and cutscenes, but 0 in a cutscene's pause menu) are read and logged too.
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long PausedRva = 0x59039B8, StateRva = 0x5A06764, State2Rva = 0x57E9ABB;

    /// <summary>The menu/play state, or null on another game version or while not attached.</summary>
    public GameState? ReadGameState()
    {
        if (!Attach() || Version != "Steam 1.0.0.7") return null;
        long m = (long)_moduleBase;
        var b = new byte[1];
        int Byte(long rva) => ReadProcessMemory(_handle, (IntPtr)(m + rva), b, 1, out _) ? b[0] : -1;
        bool paused = Byte(PausedRva) == 1;
        int state = Byte(StateRva), state2 = Byte(State2Rva);
        return new GameState(state2 == 1, state2 == 3, state2 == 5, state2 == 0, state2, $"paused {(paused ? 1 : 0)}\tstate {state}\tstate2 {state2}");
    }
}
