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

    static string FindData()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "overlay", "GameTracker.csproj")))
                return Directory.CreateDirectory(Path.Combine(dir.FullName, "data")).FullName;
        return Directory.CreateDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameTracker")).FullName;
    }
}
