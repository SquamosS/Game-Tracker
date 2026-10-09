using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GameTracker;

/// <summary>
/// What Steam knows about a game, read for free: local files (install, update state, cloud save, achievements,
/// artwork, screenshots) and Steam's public, key-less web API (news, players online, price). Every part is
/// optional; anything missing stays null.
/// </summary>
public sealed class SteamInfo
{
    public string? LibraryPath { get; init; }
    public string? InstallPath { get; init; }
    public long? SizeOnDisk { get; init; }
    public string? BuildId { get; init; }
    public bool? UpToDate { get; init; }
    public DateTime? LastUpdated { get; init; }
    public long? Playtime2WeeksMinutes { get; init; }
    public string? CloudState { get; init; }
    public int? AchievementsUnlocked { get; init; }
    public int? AchievementsTotal { get; init; }
    public string? HeroArt { get; init; }
    public string? CoverArt { get; init; }
    public string? LogoArt { get; init; }
    public int ScreenshotCount { get; init; }
    public string? LatestScreenshot { get; init; }

    public string? Account { get; init; }
    public string? PersonaName { get; init; }

    public static string? SteamPath =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string p && Directory.Exists(p) ? p : null;

    /// <summary>The account logged in to the running Steam client (its userdata folder name), or null when Steam
    /// is closed or nobody is logged in. Steam sets ActiveUser to 0 on logout and exit.</summary>
    public static string? ActiveAccount
    {
        get
        {
            const string key = @"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess";
            if (Registry.GetValue(key, "ActiveUser", 0) is not int user || user == 0) return null;
            // A crash can leave ActiveUser set: also require the Steam process to be alive.
            if (Registry.GetValue(key, "pid", 0) is not int pid || pid == 0) return null;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                if (!process.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase)) return null;
            }
            catch (ArgumentException) { return null; }
            return ((uint)user).ToString();
        }
    }

    /// <summary>The Steam profile name of an account, from loginusers.vdf (keyed by SteamID64).</summary>
    static string? Persona(string steam, string account)
    {
        if (Read(Path.Combine(steam, "config", "loginusers.vdf")) is not { } users || !ulong.TryParse(account, out ulong id)) return null;
        var m = Regex.Match(users, $"\"{76561197960265728UL + id}\"\\s*\\{{[^}}]*?\"PersonaName\"\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Reads the local part. <paramref name="screenshotGlob"/> is a game's own screenshot folder and file
    /// pattern relative to its install folder (e.g. "End\Binaries\Win64\ff7remake_*.png"), besides Steam's.</summary>
    public static SteamInfo? Local(int appId, string? screenshotGlob)
    {
        if (SteamPath is not { } steam) return null;

        // Which library holds the game, and its manifest.
        string? library = null, manifest = null;
        foreach (var path in Libraries(steam))
        {
            var file = Path.Combine(path, "steamapps", $"appmanifest_{appId}.acf");
            if (File.Exists(file)) { library = path; manifest = Read(file); break; }
        }
        string? installDir = manifest is null ? null : Value(manifest, "installdir");
        string? install = library is null || installDir is null ? null : Path.Combine(library, "steamapps", "common", installDir);

        // Only the logged-in account: its settings for the game (none when it never played it).
        string? account = ActiveAccount, block = null;
        if (account is not null && Read(Path.Combine(steam, "userdata", account, "config", "localconfig.vdf")) is { } config)
            block = Block(config, appId.ToString());

        // Achievements: unlocked = recorded unlock times; total = achievement bits in the schema.
        int? unlocked = null, total = null;
        string stats = Path.Combine(steam, "appcache", "stats");
        if (Bytes(Path.Combine(stats, $"UserGameStatsSchema_{appId}.bin")) is { } schemaBytes)
        {
            var schema = BinaryKeyValues.Flatten(schemaBytes);
            total = schema.Count(kv => Regex.IsMatch(kv.Key, @"/stats/\d+/bits/\d+/name$"));
            string user = Path.Combine(stats, $"UserGameStats_{account}_{appId}.bin");
            if (account is not null)
                unlocked = !File.Exists(user) ? 0
                    : Bytes(user) is { } userBytes ? BinaryKeyValues.Flatten(userBytes).Count(kv => kv.Key.Contains("/AchievementTimes/")) : null;
        }

        // Steam's own artwork for the game (newer clients keep it in a folder per app).
        string art = Path.Combine(steam, "appcache", "librarycache");
        string? Art(string name) => new[] { Path.Combine(art, appId.ToString(), name), Path.Combine(art, $"{appId}_{name}") }.FirstOrDefault(File.Exists);

        // Screenshots: Steam's (F12) and the game's own folder.
        var shots = new List<FileInfo>();
        void AddShots(string dir, string pattern)
        {
            try { if (Directory.Exists(dir)) shots.AddRange(new DirectoryInfo(dir).GetFiles(pattern)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        if (account is not null)
            AddShots(Path.Combine(steam, "userdata", account, "760", "remote", appId.ToString(), "screenshots"), "*.jpg");
        if (install is not null && screenshotGlob is not null)
            AddShots(Path.Combine(install, Path.GetDirectoryName(screenshotGlob) ?? ""), Path.GetFileName(screenshotGlob));
        var latest = shots.MaxBy(f => f.LastWriteTimeUtc);

        return new SteamInfo
        {
            LibraryPath = library,
            InstallPath = install,
            SizeOnDisk = manifest is null ? null : Number(manifest, "SizeOnDisk"),
            BuildId = manifest is null ? null : Value(manifest, "buildid"),
            UpToDate = manifest is null ? null : Number(manifest, "StateFlags") is long flags ? IsUpToDate(flags) : null,
            LastUpdated = manifest is not null && Number(manifest, "LastUpdated") is long lu and > 0 ? DateTimeOffset.FromUnixTimeSeconds(lu).LocalDateTime : null,
            Account = account,
            PersonaName = account is null ? null : Persona(steam, account),
            Playtime2WeeksMinutes = account is null ? null : block is null ? 0 : Number(block, "Playtime2wks") ?? 0,
            CloudState = block is null ? null : Value(block, "last_sync_state"),
            AchievementsUnlocked = unlocked,
            AchievementsTotal = total,
            HeroArt = Art("library_hero.jpg"),
            CoverArt = Art("library_600x900.jpg"),
            LogoArt = Art("logo.png"),
            ScreenshotCount = shots.Count,
            LatestScreenshot = latest?.FullName,
        };
    }

    /// <summary>
    /// StateFlags is a bit field (Steam's EAppState), so other bits such as 64 = app running may be set too.
    /// Up to date = 4 (fully installed) without 2 (update required), 256 (update running), 512 (update paused)
    /// or 1024 (update started).
    /// </summary>
    static bool IsUpToDate(long flags) => (flags & 4) != 0 && (flags & (2 | 256 | 512 | 1024)) == 0;

    static IEnumerable<string> Libraries(string steam)
    {
        yield return steam;
        if (Read(Path.Combine(steam, "steamapps", "libraryfolders.vdf")) is { } folders)
            foreach (Match m in Regex.Matches(folders, "\"path\"\\s*\"([^\"]+)\""))
            {
                var path = m.Groups[1].Value.Replace(@"\\", @"\");
                if (!path.Equals(steam.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase)) yield return path;
            }
    }

    /// <summary>A file's text, or null when it is missing or Steam holds it locked while writing.</summary>
    static string? Read(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static byte[]? Bytes(string file)
    {
        try { return File.Exists(file) ? File.ReadAllBytes(file) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The text of a "key" { ... } block, nested blocks included: the app's block in localconfig.vdf (the
    /// one holding LastPlayed or Playtime; the same number can also name other blocks). Shared with SteamStats.</summary>
    internal static string? Block(string text, string key)
    {
        foreach (Match m in Regex.Matches(text, $"\"{Regex.Escape(key)}\"\\s*\\{{"))
        {
            int depth = 0;
            for (int i = m.Index + m.Length - 1; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0)
                {
                    string block = text[m.Index..i];
                    if (Regex.IsMatch(block, "\"(LastPlayed|Playtime)\"")) return block;
                    break;
                }
            }
        }
        return null;
    }

    static string? Value(string text, string key) =>
        Regex.Match(text, $"\"{key}\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;

    internal static long? Number(string text, string key) => long.TryParse(Value(text, key), out long n) ? n : null;

    // ---- Web (public, no key) ------------------------------------------------------------------------------

    public sealed record NewsItem(string Title, DateTime Date, string Url);
    public sealed record Online(int? Players, string? Price, int? DiscountPercent, IReadOnlyList<NewsItem> News);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    static readonly Dictionary<int, (DateTime At, Online Data)> Cache = new();

    /// <summary>News, players online and price; cached for 15 minutes. Null when offline.</summary>
    public static async Task<Online?> FetchOnline(int appId)
    {
        if (Cache.TryGetValue(appId, out var cached) && DateTime.Now - cached.At < TimeSpan.FromMinutes(15)) return cached.Data;

        // Each part on its own: one failing (offline store, changed field) does not hide the others.
        var news = await Part("news", async () =>
        {
            var list = new List<NewsItem>();
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appId}&count=3&maxlength=1"));
            foreach (var item in doc.RootElement.GetProperty("appnews").GetProperty("newsitems").EnumerateArray())
                list.Add(new NewsItem(item.GetProperty("title").GetString() ?? "", DateTimeOffset.FromUnixTimeSeconds(item.GetProperty("date").GetInt64()).LocalDateTime, item.GetProperty("url").GetString() ?? ""));
            return list;
        }) ?? [];

        var players = await Part("players", async () =>
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={appId}"));
            return doc.RootElement.GetProperty("response").TryGetProperty("player_count", out var count) ? count.GetInt32() : (int?)null;
        });

        var price = await Part("price", async () =>
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&cc=id&filters=price_overview"));
            if (!doc.RootElement.GetProperty(appId.ToString()).TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("price_overview", out var overview)) return ((string?, int?)?)null;
            return (overview.GetProperty("final_formatted").GetString(), overview.GetProperty("discount_percent").GetInt32());
        });

        if (news.Count == 0 && players is null && price is null) return null;
        var online = new Online(players, price?.Item1, price?.Item2, news);
        Cache[appId] = (DateTime.Now, online);
        return online;
    }

    static async Task<T?> Part<T>(string name, Func<Task<T>> fetch)
    {
        try { return await fetch(); }
        catch (Exception e)
        {
            try { File.AppendAllText(Path.Combine(DataPaths.Logs, "steam-online.log"), $"{DateTime.Now:s} {name}: {e.GetType().Name}: {e.Message}\n"); }
            catch (IOException) { }
            return default;
        }
    }
}

/// <summary>Steam's binary KeyValues (appcache\stats\*.bin), flattened to "/path/to/key" = value.</summary>
static class BinaryKeyValues
{
    public static List<KeyValuePair<string, object>> Flatten(byte[] data)
    {
        var list = new List<KeyValuePair<string, object>>();
        try { Read(data, 0, "", list); } catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException) { }
        return list;
    }

    static int Read(byte[] b, int i, string path, List<KeyValuePair<string, object>> list)
    {
        while (i < b.Length)
        {
            byte type = b[i++];
            if (type == 8) return i;
            int end = Array.IndexOf(b, (byte)0, i);
            string name = path + "/" + Encoding.UTF8.GetString(b, i, end - i);
            i = end + 1;
            switch (type)
            {
                case 0: i = Read(b, i, name, list); break;
                case 1:
                    end = Array.IndexOf(b, (byte)0, i);
                    list.Add(new(name, Encoding.UTF8.GetString(b, i, end - i)));
                    i = end + 1;
                    break;
                case 2: case 6: list.Add(new(name, BitConverter.ToInt32(b, i))); i += 4; break;
                case 3: list.Add(new(name, BitConverter.ToSingle(b, i))); i += 4; break;
                case 7: list.Add(new(name, BitConverter.ToUInt64(b, i))); i += 8; break;
                default: return b.Length; // unknown type: stop rather than misread
            }
        }
        return i;
    }
}
