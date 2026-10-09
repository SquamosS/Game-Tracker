using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GameTracker;

/// <summary>
/// Play time and last played as Steam records them, read from Steam's own files
/// (Steam\userdata\&lt;account&gt;\config\localconfig.vdf: "Playtime" in minutes, "LastPlayed" and the last launch and
/// exit as Unix times). Steam adds a session's minutes when the game exits, so a running session is added on top.
/// </summary>
public static class SteamStats
{
    public sealed record Stats(long Seconds, DateTime? LastPlayed);

    public static Stats? For(int appId)
    {
        string? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steam is null || !Directory.Exists(Path.Combine(steam, "userdata"))) return null;
        Stats? best = null;
        foreach (var account in Directory.GetDirectories(Path.Combine(steam, "userdata")))
        {
            string file = Path.Combine(account, "config", "localconfig.vdf");
            if (!File.Exists(file) || Read(file, appId) is not { } stats) continue;
            if (best is null || (stats.LastPlayed ?? DateTime.MinValue) > (best.LastPlayed ?? DateTime.MinValue)) best = stats;
        }
        return best;
    }

    static Stats? Read(string file, int appId)
    {
        string text;
        try { text = File.ReadAllText(file); }
        catch (IOException) { return null; }
        // The app's block: "1462040" { ... } with nested blocks inside; take it up to its matching brace.
        foreach (Match m in Regex.Matches(text, $"\"{appId}\"\\s*\\{{"))
        {
            int depth = 0, end = m.Index + m.Length - 1;
            for (int i = end; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) { end = i; break; }
            }
            string block = text[m.Index..end];
            long? Number(string key) =>
                Regex.Match(block, $"\"{key}\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase) is { Success: true } n ? long.Parse(n.Groups[1].Value) : null;
            if (Number("Playtime") is not long minutes) continue;
            long seconds = minutes * 60;
            // Running now: the session since the last launch is not in Playtime yet.
            if (Number("lastlaunch") is long launch && launch > (Number("lastexit") ?? 0))
                seconds += Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - launch);
            DateTime? last = Number("LastPlayed") is long lp and > 0 ? DateTimeOffset.FromUnixTimeSeconds(lp).LocalDateTime : null;
            return new Stats(seconds, last);
        }
        return null;
    }
}
