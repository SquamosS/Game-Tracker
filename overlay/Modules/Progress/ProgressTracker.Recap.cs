using System.IO;

namespace GameTracker;

/// <summary>The chapter recap: the missables of a chapter that ended the normal way and were never ticked.</summary>
public sealed partial class ProgressTracker
{
    /// <summary>
    /// The recap once it is due: 90 s after the chapter changed, when the inventory (read every 15-60 s) has caught up with
    /// the last pickups; dropped when a save was loaded meanwhile, since the ticks then belong to another save. The missed
    /// ones also go to data\&lt;id&gt;\logs\missed.log, so a Chapter Select can be planned before saving over.
    /// </summary>
    public (int Chapter, List<Objective> Missed)? DueRecap()
    {
        if (PendingRecap is not var (number, since)) return null;
        if (LoadPending || Guide is null) { PendingRecap = null; return null; }
        if (DateTime.Now - since < TimeSpan.FromSeconds(90)) return null;
        PendingRecap = null;
        if (Guide.Chapters.FirstOrDefault(c => c.Number == number) is not { } ended) return null;
        var missed = ended.Objectives.Where(o => o.Missable && !_rules.IsStory(o) && !_rules.IsTrophy(o) && !Progress.Done.Contains(o.Id)).ToList();
        if (missed.Count > 0)
        {
            try
            {
                File.AppendAllText(Path.Combine(_logsDir, "missed.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tchapter {ended.Number}\t{string.Join(" | ", missed.Select(o => $"{o.Id} {o.Name}"))}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return (ended.Number, missed);
    }
}
