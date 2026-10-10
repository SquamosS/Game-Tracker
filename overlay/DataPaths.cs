using System.IO;

namespace GameTracker;

/// <summary>
/// Where the tracker keeps its files: the "data" folder next to "overlay" in the project folder (progress, learned
/// maps) and "data\logs" (diagnostics), so nothing lands on the system drive. Falls back to %APPDATA%\GameTracker
/// when the exe runs outside the project.
/// </summary>
public static class DataPaths
{
    public static string Data { get; } = FindData();
    /// <summary>The project folder (the one holding "overlay" and "data"), or the exe's folder outside the project.</summary>
    public static string Root { get; } = Directory.GetParent(Data) is { } parent && Directory.Exists(Path.Combine(parent.FullName, "overlay"))
        ? parent.FullName : AppContext.BaseDirectory;
    public static string Logs { get; } = Directory.CreateDirectory(Path.Combine(Data, "logs")).FullName;

    /// <summary>
    /// One game's own folder data\&lt;id&gt;\ (progress, trail, chests...), so games never mix their files. Shared files
    /// (settings, play time, crash log) stay in data\. No game: data\ itself.
    /// </summary>
    public static string Game(string? id) => id is null ? Data : Directory.CreateDirectory(Path.Combine(Data, id)).FullName;

    /// <summary>One game's logs: data\&lt;id&gt;\logs\.</summary>
    public static string GameLogs(string? id) => id is null ? Logs : Directory.CreateDirectory(Path.Combine(Game(id), "logs")).FullName;

    /// <summary>
    /// Moves a file or folder of the old shared layout (data\&lt;relative&gt;) into a game's folder, once: never over one that
    /// is already there (then the old one stays where it is). A failed move is tried again at the next start.
    /// </summary>
    public static void MoveOld(string gameDir, string relative)
    {
        string from = Path.Combine(Data, relative), to = Path.Combine(gameDir, relative);
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (File.Exists(from) && !File.Exists(to))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
            }
            else if (Directory.Exists(from) && !Directory.Exists(to))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                Directory.Move(from, to);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static string FindData()
    {
        // Tests point the data folder elsewhere, so they can never touch the real progress.
        if (Environment.GetEnvironmentVariable("GAMETRACKER_DATA") is { Length: > 0 } other) return Directory.CreateDirectory(other).FullName;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "overlay", "GameTracker.csproj")))
                return Directory.CreateDirectory(Path.Combine(dir.FullName, "data")).FullName;
        return Directory.CreateDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameTracker")).FullName;
    }
}
