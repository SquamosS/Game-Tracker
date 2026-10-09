namespace GameTracker;

/// <summary>
/// Whether the game is being played, sits in a menu or shows a cutscene, from static values in the game module
/// (Steam 1.0.0.7; research\notes.md, "Menu / pause"). module+0x57E9ABB tells them apart: 1 in play, 3 in any menu
/// (main menu, map, a cutscene's pause menu), 5 during a cutscene. module+0x5A06764 (2 in the world, 3 in a menu) and
/// module+0x59039B8 (1 in menus and cutscenes, but 0 in a cutscene's pause menu) are read and logged too.
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long PausedRva = 0x59039B8, StateRva = 0x5A06764, State2Rva = 0x57E9ABB;

    public record GameState(bool Paused, int State, int State2)
    {
        public bool Menu => State2 == 3;
        public bool Cutscene => State2 == 5;
    }

    /// <summary>The menu/play state, or null on another game version or while not attached.</summary>
    public GameState? ReadGameState()
    {
        if (!Attach() || Version != "Steam 1.0.0.7") return null;
        long m = (long)_moduleBase;
        var b = new byte[1];
        int Byte(long rva) => ReadProcessMemory(_handle, (IntPtr)(m + rva), b, 1, out _) ? b[0] : -1;
        return new GameState(Byte(PausedRva) == 1, Byte(StateRva), Byte(State2Rva));
    }
}
