namespace AVAEliminator.Engine;

/// <summary>One player's controls: turning (-1 left, 1 right), thrust and fire.</summary>
public readonly record struct Controls(int Turn = 0, bool Thrust = false, bool Fire = false)
{
    /// <summary>Two sets of controls together: the first one's turn wins if it's turning at all.</summary>
    public static Controls operator |(Controls a, Controls b) => new(a.Turn != 0 ? a.Turn : b.Turn, a.Thrust || b.Thrust, a.Fire || b.Fire);
}

/// <summary>How the players play: one against the computer's drones, or two to four together or against each other.</summary>
public enum Mode
{
    /// <summary>One player against the base and the drones.</summary>
    Solo,
    /// <summary>Two to four players together: their shots pass through each other.</summary>
    Team,
    /// <summary>Two to four players against each other as well as the base: shots kill, 500 a time.</summary>
    Versus,
}

/// <summary>The arena: the whole screen, wrapping round at the edges, with the base in the middle.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The middle of the arena, a little below the screen's, clear of the scores.</summary>
    public const double CX = Width / 2, CY = 416;

    /// <summary>The shortest way from one coordinate to another, round the wrap if that's shorter.</summary>
    public static double Delta(double from, double to, double size)
    {
        double d = to - from;
        if (d > size / 2) d -= size;
        else if (d < -size / 2) d += size;
        return d;
    }

    public static double Dx(double from, double to) => Delta(from, to, Width);
    public static double Dy(double from, double to) => Delta(from, to, Height);
    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt(Dx(ax, bx) * Dx(ax, bx) + Dy(ay, by) * Dy(ay, by));

    public static double WrapX(double x) => (x % Width + Width) % Width;
    public static double WrapY(double y) => (y % Height + Height) % Height;

    /// <summary>The difference between two angles, from -π to π.</summary>
    public static double Turn(double from, double to) => Math.IEEERemainder(to - from, Math.PI * 2);
}

/// <summary>A player's ship: turned, thrust and fired, Asteroids-style, drifting with momentum.</summary>
public sealed class Ship
{
    public const double Radius = 11;

    /// <summary>0 to 3: player 1 to 4.</summary>
    public int Index;
    public double X, Y, Vx, Vy, Angle = -Math.PI / 2;
    public bool Alive, Thrusting;
    /// <summary>Lives left, counting the one in play.</summary>
    public int Lives;
    public int Score, Kills;
    /// <summary>Untouchable for a moment after coming back.</summary>
    public double Safe;
    public double FireIn, RespawnIn;
    public int NextExtra;
    /// <summary>What hit it last.</summary>
    public string Cause = "";

    /// <summary>Out of lives, and out of the game.</summary>
    public bool Out => !Alive && Lives <= 0;
}

/// <summary>A shot: a player's (it bounces off the base's walls) or a drone's.</summary>
public sealed class Bullet
{
    public double X, Y, Vx, Vy, Life;
    /// <summary>The player who fired it, or -1 for a drone.</summary>
    public int Owner;
    public int Bounces;
    public bool Hostile => Owner < 0;
}

/// <summary>A drone: one of the computer's ships, hunting the players and shooting at them.</summary>
public sealed class Drone
{
    public const double Radius = 12;
    public double X, Y, Vx, Vy, Angle, FireIn = 1.5, T;
    public bool Dead;
}

/// <summary>
/// An eliminator: the base's deadly shot. It runs out along the channel from the core, leaves by the mouth, and
/// then hunts the nearest ship until it burns out. It can be shot down.
/// </summary>
public sealed class Bolt
{
    public const double Radius = 8;
    /// <summary>Where it is: in the base's own frame while in the channel, on the screen once it's out.</summary>
    public double X, Y, Vx, Vy, T;
    /// <summary>The point on the channel it is heading for (the core is the last; the mouth the first); -1 once it's out.</summary>
    public int Leg;
    public double Life = 4.5;
    public bool Dead;
    public bool Free => Leg < 0;
}

/// <summary>A broken-off line, spinning away.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Angle, Spin, Length, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
