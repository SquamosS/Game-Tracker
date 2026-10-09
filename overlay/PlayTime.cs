using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace GameTracker;

/// <summary>Time a game has been running while the tracker was open, per game id, in data\playtime.json.</summary>
public sealed class PlayTime
{
    public sealed class Entry
    {
        public long Seconds { get; set; }
        public DateTime? LastPlayed { get; set; }
    }

    static readonly string File_ = Path.Combine(DataPaths.Data, "playtime.json");
    readonly Dictionary<string, Entry> _entries;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    DateTime _lastSave = DateTime.Now;

    public static PlayTime Instance { get; } = new();

    PlayTime()
    {
        try { _entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(File_)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _entries = new(); }
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();

    public Entry For(string gameId) => _entries.TryGetValue(gameId, out var e) ? e : new();

    void Tick()
    {
        bool changed = false;
        foreach (var game in GameRegistry.All.Where(g => g.IsRunning))
        {
            if (!_entries.TryGetValue(game.Id, out var entry)) _entries[game.Id] = entry = new();
            entry.Seconds += (long)_timer.Interval.TotalSeconds;
            entry.LastPlayed = DateTime.Now;
            changed = true;
        }
        if (changed && DateTime.Now - _lastSave > TimeSpan.FromMinutes(1)) Save();
    }

    public void Save()
    {
        _lastSave = DateTime.Now;
        try
        {
            File.WriteAllText(File_ + ".tmp", JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(File_ + ".tmp", File_, overwrite: true);
        }
        catch (IOException) { }
    }
}
