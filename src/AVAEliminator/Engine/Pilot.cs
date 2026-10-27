namespace AVAEliminator.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests), one for each ship. It shoots what threatens it
/// first: an eliminator or a drone it can see and that's near, or (in a versus game) a rival close by. Otherwise it
/// makes for the core: it heads for the furthest point along the channel it can see with room for the ship, round
/// the outside of the base if the mouth is on the far side, slowing right down in the channel. As soon as it can
/// see the core it turns to face it and fires.
/// </summary>
public sealed class Pilot
{
    private readonly object?[] _target = new object?[GameEngine.MaxPlayers];
    private readonly int[] _tick = new int[GameEngine.MaxPlayers];

    /// <summary>What each one is up to, for the probe.</summary>
    public string[] Doing { get; } = Enumerable.Repeat("", GameEngine.MaxPlayers).ToArray();

    /// <summary>How near a threat must be before it turns to deal with it.</summary>
    public const double ThreatRange = 330, DroneRange = 460;
    /// <summary>How fast it goes in the open, and in the channel.</summary>
    public const double Cruise = 230, Creep = 85;

    public void Reset()
    {
        Array.Clear(_target);
        Array.Clear(_tick);
    }

    public Controls Decide(GameEngine g, int i)
    {
        var s = g.Ships[i];
        if (!s.Alive || g.Phase == Phase.Clear) { Doing[i] = "waiting"; return default; }
        _tick[i]++;
        if (Incoming(g, s) is { } dodge) { Doing[i] = "dodging"; return dodge; }
        // It sees a new threat only every few frames, like anyone would.
        if (_tick[i] % 5 == 1 || !Exists(g, _target[i])) _target[i] = Threat(g, s);
        if (_target[i] is { } t && Exists(g, t))
        {
            var (tx, ty, tvx, tvy, r) = Where(g, t);
            return Shoot(g, s, i, tx, ty, tvx, tvy, r, t is Ship ? "duelling" : t is Bolt ? "shooting an eliminator" : "shooting a drone");
        }
        return ForTheCore(g, s, i);
    }

    private static bool Exists(GameEngine g, object? t) => t switch
    {
        Drone d => !d.Dead && g.Drones.Contains(d),
        Bolt b => !b.Dead && g.Bolts.Contains(b),
        Ship o => o.Alive,
        _ => false,
    };

    private static (double X, double Y, double Vx, double Vy, double R) Where(GameEngine g, object t)
    {
        switch (t)
        {
            case Drone d: return (d.X, d.Y, d.Vx, d.Vy, Drone.Radius);
            case Bolt b:
            {
                var (x, y) = g.BoltAt(b);
                var (vx, vy) = b.Free ? (b.Vx, b.Vy) : g.Base.TurnOut(b.Vx * g.BoltSpeed, b.Vy * g.BoltSpeed);
                return (x, y, vx, vy, Bolt.Radius);
            }
            case Ship o: return (o.X, o.Y, o.Vx, o.Vy, Ship.Radius);
            default: return default;
        }
    }

    /// <summary>The nearest thing it can see that's a danger: eliminators first, then drones, then rivals.</summary>
    private static object? Threat(GameEngine g, Ship s)
    {
        object? best = null;
        double cost = double.MaxValue;
        void Consider(object t, double weight, double range)
        {
            var (x, y, _, _, _) = Where(g, t);
            double d = Field.Distance(s.X, s.Y, x, y);
            if (d > range || d * weight >= cost || !g.Base.Clear(s.X, s.Y, x, y, 2)) return;
            cost = d * weight;
            best = t;
        }
        foreach (var b in g.Bolts) Consider(b, 0.6, ThreatRange);
        foreach (var d in g.Drones) Consider(d, 1, DroneRange);
        if (g.Mode == Mode.Versus)
            foreach (var o in g.Ships)
                if (o != s && o.Alive && o.Safe <= 0) Consider(o, 1.3, 260);
        return best;
    }

    /// <summary>
    /// A shot about to hit it (a drone's, or a rival's): it gets out of the way, across the shot's line, thrusting if
    /// it faces either way across and turning to the nearer if not.
    /// </summary>
    private static Controls? Incoming(GameEngine g, Ship s)
    {
        if (s.Safe > 0.3) return null;
        foreach (var b in g.Bullets)
        {
            if (b.Owner == s.Index || (!b.Hostile && g.Mode != Mode.Versus)) continue;
            double px = Field.Dx(s.X, b.X), py = Field.Dy(s.Y, b.Y), rvx = b.Vx - s.Vx, rvy = b.Vy - s.Vy, vv = rvx * rvx + rvy * rvy;
            if (vv < 1) continue;
            double t = -(px * rvx + py * rvy) / vv;
            if (t is < 0 or > 0.55) continue;
            double cx = px + rvx * t, cy = py + rvy * t;
            if (cx * cx + cy * cy > 22 * 22) continue;
            // Away from where it will pass.
            double away = cx * cx + cy * cy < 1 ? Math.Atan2(rvx, -rvy) : Math.Atan2(-cy, -cx);
            double d1 = Field.Turn(s.Angle, away);
            if (!g.Base.Clear(s.X, s.Y, s.X + Math.Cos(away) * 40, s.Y + Math.Sin(away) * 40, Ship.Radius)) return null;
            return new Controls(Math.Abs(d1) < 0.1 ? 0 : Math.Sign(d1), Math.Abs(d1) < 0.9);
        }
        return null;
    }

    /// <summary>Turns to lead the target and fires once it's lined up; backs off if it's right on top.</summary>
    private Controls Shoot(GameEngine g, Ship s, int i, double tx, double ty, double tvx, double tvy, double r, string what)
    {
        double dx = Field.Dx(s.X, tx), dy = Field.Dy(s.Y, ty);
        double time = Math.Sqrt(dx * dx + dy * dy) / GameEngine.BulletSpeed;
        for (int k = 0; k < 3; k++)
        {
            double lx = dx + (tvx - s.Vx * 0.5) * time, ly = dy + (tvy - s.Vy * 0.5) * time;
            time = Math.Sqrt(lx * lx + ly * ly) / GameEngine.BulletSpeed;
        }
        dx += (tvx - s.Vx * 0.5) * time;
        dy += (tvy - s.Vy * 0.5) * time;
        double dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy)), diff = Field.Turn(s.Angle, Math.Atan2(dy, dx));
        int turn = Math.Abs(diff) < 0.03 ? 0 : Math.Sign(diff);
        bool fire = Math.Abs(diff) < Math.Atan2(r * 0.9, dist) + 0.03;
        Doing[i] = what;
        return new Controls(turn, false, fire);
    }

    /// <summary>Into the channel and at the core.</summary>
    private Controls ForTheCore(GameEngine g, Ship s, int i)
    {
        var b = g.Base;
        var (cx, cy) = b.Towards(s.X, s.Y, b.Core.X, b.Core.Y);
        double cdist = Math.Sqrt(cx * cx + cy * cy);
        var (wx, wy) = b.CoreOnScreen;
        if (cdist < GameEngine.BulletSpeed * GameEngine.BulletLife * 0.8 && b.Clear(s.X, s.Y, wx, wy, 3))
        {
            // The core in sight: face it and fire.
            double diff = Field.Turn(s.Angle, Math.Atan2(cy, cx));
            double speed = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
            bool lined = Math.Abs(diff) < Math.Atan2(Fortress.CoreRadius * 0.7, cdist) + 0.01;
            Doing[i] = "firing at the core";
            // Too fast towards the walls: a touch of thrust against it, if facing that way anyway.
            bool brake = speed > 140 && Math.Abs(Field.Turn(s.Angle, Math.Atan2(-s.Vy, -s.Vx))) < 0.4;
            return new Controls(Math.Abs(diff) < 0.02 ? 0 : Math.Sign(diff), brake, lined);
        }
        // The furthest point along the way in that it can see with room to fly.
        (double X, double Y)? way = null;
        bool inside = false;
        var (lx, ly) = b.ToLocal(s.X, s.Y);
        for (int k = b.Path.Count - 1; k >= 0; k--)
        {
            var (px, py) = b.ToWorld(b.Path[k].X, b.Path[k].Y);
            if (!b.Clear(s.X, s.Y, px, py, Ship.Radius + 2)) continue;
            way = b.Towards(s.X, s.Y, b.Path[k].X, b.Path[k].Y);
            inside = k > 0 || b.ChannelAt(lx, ly) > 0;
            break;
        }
        string what = inside ? "in the channel" : "making for the mouth";
        if (way == null)
        {
            var (ax, ay) = b.ToWorld(b.Path[0].X, b.Path[0].Y);
            var (rx, ry) = g.RoundTheBase(s.X, s.Y, ax, ay);
            way = (Field.Dx(s.X, rx), Field.Dy(s.Y, ry));
            what = "round the base";
        }
        Doing[i] = what;
        return Steer(s, way.Value.X, way.Value.Y, inside ? Creep : Cruise, cx, cy);
    }

    /// <summary>
    /// Flies along a way (dx, dy) at up to a speed, slowing as it gets to the end: turns to whichever way it needs to
    /// push and thrusts once it faces it; when it's going the right way it turns to face along (faceX, faceY), ready
    /// to fire.
    /// </summary>
    private static Controls Steer(Ship s, double dx, double dy, double cruise, double faceX, double faceY)
    {
        double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double want = Math.Min(cruise, d * 1.1 + 20);
        double ex = dx / d * want - s.Vx, ey = dy / d * want - s.Vy;
        double err = Math.Sqrt(ex * ex + ey * ey);
        if (err < 28)
        {
            double face = Field.Turn(s.Angle, Math.Atan2(faceY, faceX));
            return new Controls(Math.Abs(face) < 0.05 ? 0 : Math.Sign(face));
        }
        double diff = Field.Turn(s.Angle, Math.Atan2(ey, ex));
        return new Controls(Math.Abs(diff) < 0.05 ? 0 : Math.Sign(diff), Math.Abs(diff) < 0.4);
    }
}
