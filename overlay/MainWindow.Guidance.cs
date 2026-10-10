using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace GameTracker;

/// <summary>
/// What the overlay tells you on its own: a notice for what it just did (ticked, learned, undone), the chapter's
/// missed missables when a chapter ends, and the way to the next step's room through rooms you have walked between.
/// </summary>
public partial class MainWindow
{
    static readonly Brush NoticeOk = Brush("#264ADE80"), NoticeBad = Brush("#26F87171");

    /// <summary>Shows a notice for <paramref name="seconds"/> of visible time (also kept as the status text).</summary>
    void Notify(string text, bool alert = false, int seconds = 5)
    {
        _itemStatus = text;
        // An alert (missed missables) stays up for its time: a routine notice must not push it away.
        if (!alert && _noticeAlert && _notice is not null) return;
        _notice = text;
        _noticeAlert = alert;
        _noticeSeconds = seconds;
        RenderNotice();
    }

    /// <summary>
    /// A chapter just ended the normal way (the next one started): name its missables that were never ticked, on the overlay
    /// for a minute of play (ProgressTracker.DueRecap logs them too).
    /// </summary>
    void FollowRecap()
    {
        if (_tracker.DueRecap() is not var (number, missed)) return;
        if (missed.Count == 0)
        {
            Notify(Lang.T($"Chapter {number} done: no missables missed", $"Chapter {number} selesai: tidak ada missable yang terlewat"), seconds: 10);
            return;
        }
        string names = string.Join(" · ", missed.Select(o => o.Name));
        Notify(Lang.T($"Missed in Chapter {number} ({missed.Count}): {names}. Needs a Chapter Select later.",
            $"Terlewat di Chapter {number} ({missed.Count}): {names}. Perlu Chapter Select nanti."), alert: true, seconds: 60);
    }

    void RenderNotice()
    {
        NoticeBox.Visibility = _notice is null || !_tracker.InGame ? Visibility.Collapsed : Visibility.Visible;
        NoticeText.Text = _notice ?? "";
        NoticeBox.BorderBrush = _noticeAlert ? Danger : Now;
        NoticeBox.Background = _noticeAlert ? NoticeBad : NoticeOk;
    }


    /// <summary>
    /// "➜ Magic Up Materia: Security Ops › Waste Storage": the way to the first open step (missables first) whose room
    /// is not this one, when every room on the way was walked between before. Nothing rather than a guess.
    /// </summary>
    void RenderRoute(Chapter? chapter)
    {
        RouteText.Inlines.Clear();
        RouteText.Visibility = Visibility.Collapsed;
        if (_area.Here is not { } here || chapter is null || !_tracker.InGame || _full) return;
        var objectives = chapter.Objectives;
        int current = CurrentStory is { } story ? Array.IndexOf(objectives, story) : objectives.Length;
        foreach (var (step, tag) in _checklist.OpenSteps(objectives, current))
        {
            if (tag == TagHere || GuideRules.AreaOf(step) is not var (area, floor)) continue;
            if (area.Equals(here.Area, StringComparison.OrdinalIgnoreCase)) continue; // same room, other floor: no walk to show
            if (_area.RouteTo(here, area, floor) is not { Count: > 0 } path) continue;
            string way = path.Count <= 4 ? string.Join(" › ", path) : $"{path[0]} › … › {path[^1]} ({path.Count} {Lang.T("rooms", "ruang")})";
            RouteText.Inlines.Add(new System.Windows.Documents.Run("➜ ") { Foreground = Mako, FontWeight = FontWeights.Bold });
            RouteText.Inlines.Add(new System.Windows.Documents.Run(step.Name + ": ") { Foreground = step.Missable ? Danger : Brushes.White, FontWeight = FontWeights.SemiBold });
            RouteText.Inlines.Add(new System.Windows.Documents.Run(way) { Foreground = Muted });
            RouteText.Visibility = Visibility.Visible;
            return;
        }
    }

    /// <summary>Ctrl+Shift+Alt+P: notes where Cloud stands for points.json (TrailRecorder.RecordSpot).</summary>
    void RecordSpot()
    {
        if (!_live || _area.Position is not { } p)
        {
            Notify(Lang.T("No position to save yet", "Belum ada posisi untuk disimpan"), alert: true);
            return;
        }
        try
        {
            _trails.RecordSpot(p);
            Notify(Lang.T($"Spot saved: {_area.Here?.Area ?? "?"} ({p.X:F0}, {p.Y:F0}, {p.Z:F0})", $"Titik disimpan: {_area.Here?.Area ?? "?"} ({p.X:F0}, {p.Y:F0}, {p.Z:F0})"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notify(Lang.T($"Could not save the spot: {e.Message}", $"Gagal menyimpan titik: {e.Message}"), alert: true);
        }
    }

    /// <summary>How far a step is: its chest (ChestDistance), else its quest giver or recorded spot (PointDistance).</summary>
    string? StepDistance(Objective o) => _chests.ChestDistance(o) ?? _trails.PointDistance(o);

}
