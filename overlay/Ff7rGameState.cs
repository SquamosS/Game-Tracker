namespace GameTracker;

/// <summary>
/// Whether the game is being played or sits in a menu, from static values in the game module (Steam 1.0.0.7), found
/// by opening and closing the main menu and the map (research\notes.md, "Menu / pause"):
/// module+0x59039B8 is 1 while a menu or the map is open and 0 in play; module+0x5A06764 reads 3 in a menu and 2 in
/// play, and module+0x57E9ABB 3 and 1. The last two may also tell cutscenes and battles apart; they are logged to learn.
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long PausedRva = 0x59039B8, StateRva = 0x5A06764, State2Rva = 0x57E9ABB;

    public record GameState(bool Paused, int State, int State2);

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
