using System.IO;
using System.Text.Json;

namespace GameTracker;

/// <summary>
/// The language the tracker speaks: English (the default) or Indonesian, switched with the EN/IN switch on the overlay
/// (or Ctrl+Shift+L / Ctrl+Shift+Alt+L) and kept in data\settings.json for every game.
/// </summary>
public static class Lang
{
    static readonly string SettingsFile = Path.Combine(DataPaths.Data, "settings.json");

    public static bool Indonesian { get; private set; } = Load();

    /// <summary>Raised on the UI thread after a switch, so open windows redraw their text.</summary>
    public static event Action? Changed;

    /// <summary>The text in the current language.</summary>
    public static string T(string en, string id) => Indonesian ? id : en;

    public static void Set(bool indonesian)
    {
        if (indonesian == Indonesian) return;
        Indonesian = indonesian;
        try
        {
            string temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Settings(indonesian ? "id" : "en")));
            File.Move(temp, SettingsFile, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    static bool Load()
    {
        try { return File.Exists(SettingsFile) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile))?.Language == "id"; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }

    record Settings(string Language);
}
