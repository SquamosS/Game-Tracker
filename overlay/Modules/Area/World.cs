namespace GameTracker;

/// <summary>Distances in the game world (units are centimetres) and how the overlay writes them.</summary>
public static class World
{
    public static double Distance(GamePosition a, GamePosition b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) / 100;
    }

    /// <summary>"12 m": 1 m up close, 5 m to 100 m, 10 m beyond.</summary>
    public static string Metres(double metres)
    {
        double round = metres < 20 ? 1 : metres < 100 ? 5 : 10;
        return $"{Math.Round(metres / round) * round:0} m";
    }
}
