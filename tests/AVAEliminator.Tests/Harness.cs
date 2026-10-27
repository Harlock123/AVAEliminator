using AVAEliminator.Engine;

namespace AVAEliminator.Tests;

/// <summary>Games set up for the tests, with anything saved kept in a temporary folder.</summary>
internal static class Harness
{
    public static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaeliminator-tests-" + Guid.NewGuid().ToString("N"));

    public static GameEngine Game(int seed = 1) => new(seed, HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>A game in a wave's fight, with no eliminators or drones unless sent for.</summary>
    public static GameEngine Calm(int wave = 1, int players = 1, Mode? mode = null)
    {
        var g = Game();
        g.Go(wave, players, mode);
        g.Calm = true;
        return g;
    }

    /// <summary>A ship put somewhere, still, facing a way.</summary>
    public static Ship Put(GameEngine g, int i, double x, double y, double angle = 0)
    {
        var s = g.Ships[i];
        (s.X, s.Y, s.Vx, s.Vy, s.Angle, s.Safe) = (x, y, 0, 0, angle, 0);
        return s;
    }

    /// <summary>Where a point of the base's channel is on the screen.</summary>
    public static (double X, double Y) Channel(GameEngine g, int k) => g.Base.ToWorld(g.Base.Path[k].X, g.Base.Path[k].Y);

    public static double Heading(double fx, double fy, double tx, double ty) => Math.Atan2(ty - fy, tx - fx);
}
