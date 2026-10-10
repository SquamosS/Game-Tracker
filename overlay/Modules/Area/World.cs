namespace GameTracker;

/// <summary>Distances in the game world, in metres (the reader says how many of its units make one), and how the overlay writes them.</summary>
public static class World
{
    /// <summary>Metres between two points given in the game's units (unitsPerMetre: IGameReader.UnitsPerMetre).</summary>
    public static double Distance(GamePosition a, GamePosition b, double unitsPerMetre)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) / unitsPerMetre;
    }

    /// <summary>
    /// Of one quest's map markers, the ones to walk to: its targets (the kids still to find) when it has any, else its own
    /// marker when there is just one (two would be a guess).
    /// </summary>
    public static List<QuestMarker> Marked(IEnumerable<QuestMarker> quest)
    {
        var all = quest.ToList();
        var targets = all.Where(m => m.Target is not null).ToList();
        return targets.Count > 0 ? targets : all.Count == 1 ? all : [];
    }

    /// <summary>"12 m": 1 m up close, 5 m to 100 m, 10 m beyond.</summary>
    public static string Metres(double metres)
    {
        double round = metres < 20 ? 1 : metres < 100 ? 5 : 10;
        return $"{Math.Round(metres / round) * round:0} m";
    }
}
