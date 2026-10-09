using System.IO;

namespace GameTracker;

/// <summary>
/// Play time and last played as Steam records them, read from Steam's own files
/// (Steam\userdata\&lt;account&gt;\config\localconfig.vdf: "Playtime" in minutes, "LastPlayed" and the last launch and
/// exit as Unix times). Steam adds a session's minutes when the game exits, so a running session is added on top.
/// </summary>
public static class SteamStats
{
    public sealed record Stats(long Seconds, DateTime? LastPlayed);

    /// <summary>The numbers as read from the file; the running session is added per call, as time moves on.</summary>
    sealed record Record(long Minutes, long? Launch, long? Exit, DateTime? LastPlayed);

    // localconfig.vdf is large: parse it again only when it is rewritten or another account's file is meant.
    static readonly Dictionary<int, (string File, DateTime Written, Record? Data)> Cache = new();

    /// <summary>The logged-in account's record; null when Steam is not logged in, zero when that account never played.
    /// <paramref name="running"/> = the game's process is alive, so the session since its launch counts.</summary>
    public static Stats? For(int appId, bool running)
    {
        if (SteamInfo.SteamPath is not { } steam || SteamInfo.ActiveAccount is not { } account) return null;
        string file = Path.Combine(steam, "userdata", account, "config", "localconfig.vdf");
        if (!File.Exists(file)) return new Stats(0, null);

        DateTime written = File.GetLastWriteTimeUtc(file);
        if (!Cache.TryGetValue(appId, out var c) || c.File != file || c.Written != written)
        {
            // A failed read (Steam holds the file while writing) is not cached: try again next time.
            if (!Read(file, appId, out var data)) return c.File == file && c.Data is { } old ? Total(old, running) : new Stats(0, null);
            Cache[appId] = c = (file, written, data);
        }
        return c.Data is { } record ? Total(record, running) : new Stats(0, null);
    }

    static Stats Total(Record r, bool running)
    {
        long seconds = r.Minutes * 60;
        // Running now: the session since the last launch is not in Playtime yet. Only while the process is really
        // alive: after a crash of the game or Steam, lastexit is never written and the session would grow forever.
        if (running && r.Launch is long launch && launch > (r.Exit ?? 0))
            seconds += Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - launch);
        return new Stats(seconds, r.LastPlayed);
    }

    /// <summary>False when the file could not be read; <paramref name="data"/> is null when the app has no record.</summary>
    static bool Read(string file, int appId, out Record? data)
    {
        data = null;
        string text;
        try { text = File.ReadAllText(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        // The app's block: "1462040" { ... } with nested blocks inside.
        if (SteamInfo.Block(text, appId.ToString()) is { } block && SteamInfo.Number(block, "Playtime") is long minutes)
            data = new Record(minutes, SteamInfo.Number(block, "lastlaunch"), SteamInfo.Number(block, "lastexit"),
                SteamInfo.Number(block, "LastPlayed") is long lp and > 0 ? DateTimeOffset.FromUnixTimeSeconds(lp).LocalDateTime : null);
        return true;
    }
}
