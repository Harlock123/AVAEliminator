namespace AVAEliminator.Engine;

public sealed partial class GameEngine
{
    public const double TurnRate = 4.4, ThrustPower = 380, Drag = 0.65, MaxSpeed = 330;
    public const double BulletSpeed = 560, BulletLife = 1.25, FireGap = 0.17;
    public const int MaxBullets = 4, MaxBounces = 4;
    public const double StartSeconds = 2.4, ClearSeconds = 4.2, RespawnSeconds = 2.5, SafeSeconds = 2.5, EndSeconds = 2.5;
    /// <summary>After this long on one base, it gets angry and fires twice as often.</summary>
    public const double AngerSeconds = 45;
    /// <summary>How soon the base fires again when someone is in its channel.</summary>
    public const double IntruderGap = 0.9;
    public const int ExtraLifeEvery = 10000, StartLives = 3;
    public const int DronePoints = 200, BoltPoints = 50, CorePoints = 100, KillPoints = 500;

    /// <summary>The wave: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public Mode Mode { get; private set; } = Mode.Solo;
    public List<Ship> Ships { get; } = [];
    public Fortress Base { get; private set; } = new(0);
    public List<Bullet> Bullets { get; } = [];
    public List<Drone> Drones { get; } = [];
    public List<Bolt> Bolts { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public Pilot Pilot { get; } = new();
    /// <summary>The player who finished off the last base, and what it was worth.</summary>
    public int Eliminator { get; private set; } = -1;
    public int LastBonus { get; private set; }
    /// <summary>For tests: no eliminators and no drones.</summary>
    internal bool Calm { get; set; }

    private double _boltIn, _droneIn, _endIn = -1, _ricochetSound, _clankSound, _thrustSound;
    private bool _angry;

    /// <summary>The highest score in this game.</summary>
    public int Score => Ships.Count == 0 ? 0 : Ships.Max(s => s.Score);

    public static int BaseBonus(int wave) => 1000 + 500 * wave;
    public static int CoreStrength(int wave) => Math.Min(6, 1 + (wave - 1) / 2);
    /// <summary>How fast the base turns, from the third wave: a little faster each time, one way and then the other.</summary>
    public static double BaseSpin(int wave) => wave < 3 ? 0 : Math.Min(0.2, 0.05 + 0.015 * (wave - 3)) * (wave % 2 == 0 ? 1 : -1);
    public double BoltGap => Math.Max(1.0, 3.4 - 0.25 * (Wave - 1)) * (_angry ? 0.5 : 1);
    public double BoltSpeed => Math.Min(260, 140 + 12 * (Wave - 1));
    public double BoltHuntSpeed => Math.Min(300, 170 + 12 * (Wave - 1));
    public const double DroneShotSpeed = 300;
    public double DroneSpeed => Math.Min(240, 110 + 12 * Wave);
    public double DroneFireGap => Math.Max(1.2, 3.0 - 0.15 * Wave);

    /// <summary>How many drones can be out at once.</summary>
    public int DroneCap => Mode switch
    {
        Mode.Solo => Math.Min(4, 1 + (Wave - 1) / 2),
        Mode.Team => Math.Min(5, Ships.Count / 2 + (Wave - 1) / 2),
        _ => Wave >= 2 ? Math.Min(2, Wave / 2) : 0,
    };

    public static uint PlayerColour(int i) => i switch { 0 => 0xFF6AE8FF, 1 => 0xFFFF6AC8, 2 => 0xFFB8FF5A, _ => 0xFFFFA040 };
    public const uint BaseColour = 0xFF4A7AFF, DroneColour = 0xFFFF4A3A, BoltColour = 0xFFFFF0FF;

    /// <summary>Where each player starts and comes back: a corner each, facing the base.</summary>
    public static (double X, double Y) Home(int i) => i switch
    {
        0 => (110, 690), 1 => (914, 140), 2 => (110, 140), _ => (914, 690),
    };

    private void NewGame(int players, Mode mode)
    {
        _rng = new Random(_seed + _demos * 7919);
        Hum(false);
        Mode = mode;
        Wave = 1;
        Ships.Clear();
        for (int i = 0; i < players; i++)
            Ships.Add(new Ship { Index = i, Lives = StartLives, NextExtra = ExtraLifeEvery });
        Bullets.Clear();
        Drones.Clear();
        Bolts.Clear();
        Wreckage.Clear();
        Base = new Fortress(0);
        _endIn = -1;
        foreach (var s in Ships) Respawn(s);
        SetPhase(Phase.Start);
    }

    /// <summary>A ship back in its corner, still, facing the base, and untouchable for a moment.</summary>
    private void Respawn(Ship s)
    {
        var (x, y) = Home(s.Index);
        s.X = x;
        s.Y = y;
        s.Vx = s.Vy = 0;
        s.Angle = Math.Atan2(Field.CY - y, Field.CX - x);
        s.Alive = true;
        s.Thrusting = false;
        s.Safe = SafeSeconds;
        s.RespawnIn = 0;
    }

    /// <summary>A wave: a new base, a little harder than the last, and everyone back in their corners.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Base = new Fortress(wave - 1) { Spin = BaseSpin(wave) };
        Base.CoreMaxHp = Base.CoreHp = CoreStrength(wave);
        Bullets.Clear();
        Drones.Clear();
        Bolts.Clear();
        _angry = false;
        _boltIn = 1.5;
        _droneIn = 3;
        foreach (var s in Ships)
        {
            if (s.Alive || s.Lives > 0) Respawn(s);
        }
        SetPhase(Phase.Start);
        string hint = wave == 1 ? "FLY INTO THE CHANNEL AND SHOOT THE CORE"
            : Base.Spin != 0 && wave == 3 ? "THE BASE IS TURNING"
            : Base.CoreMaxHp > 1 ? $"THE CORE TAKES {Base.CoreMaxHp} HITS" : "";
        Say($"WAVE {wave}", hint.Length > 0 ? hint : null, StartSeconds + 0.4);
        Play(Sfx.WaveStart);
    }

    private void Simulate(double dt, IReadOnlyList<Controls> controls)
    {
        MoveDebris(dt);
        PhaseTime += dt;
        _ricochetSound -= dt;
        _clankSound -= dt;
        _thrustSound -= dt;
        Base.Flash = Math.Max(0, Base.Flash - dt);
        if (!Base.Destroyed) Base.Angle += Base.Spin * dt;
        for (int i = 0; i < Ships.Count; i++) Fly(dt, Ships[i], i < controls.Count ? controls[i] : default);
        MoveBullets(dt);
        Comebacks(dt);
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime >= StartSeconds)
                {
                    SetPhase(Phase.Play);
                    Hum(true);
                }
                break;
            case Phase.Play:
                if (!_angry && PhaseTime >= AngerSeconds)
                {
                    _angry = true;
                    Say("THE BASE IS ANGRY!", "ITS ELIMINATORS COME TWICE AS FAST", 2.5);
                }
                BaseFire(dt);
                Reinforce(dt);
                MoveBolts(dt);
                MoveDrones(dt);
                Hits();
                break;
            case Phase.Clear:
                MoveDrones(dt);
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        // Everyone out: a moment, then the game is over.
        if (_endIn < 0 && Ships.All(s => s.Out))
        {
            _endIn = EndSeconds;
            Hum(false);
            Say("GAME OVER", null, EndSeconds);
        }
        if (_endIn >= 0 && (_endIn -= dt) < 0) GameOver();
    }

    // ───────────────────────────── the ships ─────────────────────────────

    private void Fly(double dt, Ship s, Controls c)
    {
        if (!s.Alive) return;
        s.Safe = Math.Max(0, s.Safe - dt);
        s.Angle += c.Turn * TurnRate * dt;
        s.Thrusting = c.Thrust;
        if (c.Thrust)
        {
            s.Vx += Math.Cos(s.Angle) * ThrustPower * dt;
            s.Vy += Math.Sin(s.Angle) * ThrustPower * dt;
            if (_thrustSound <= 0) { _thrustSound = 0.22; Play(Sfx.Thrust); }
        }
        s.Vx *= 1 - Drag * dt;
        s.Vy *= 1 - Drag * dt;
        double v = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        if (v > MaxSpeed) { s.Vx *= MaxSpeed / v; s.Vy *= MaxSpeed / v; }
        s.X = Field.WrapX(s.X + s.Vx * dt);
        s.Y = Field.WrapY(s.Y + s.Vy * dt);
        double before = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        if (Base.Collide(ref s.X, ref s.Y, ref s.Vx, ref s.Vy, Ship.Radius, 0.4) && before > 70 && _clankSound <= 0)
        {
            _clankSound = 0.2;
            Play(Sfx.Clank);
        }
        // Ships push each other apart.
        foreach (var o in Ships)
        {
            if (o.Index <= s.Index || !o.Alive) continue;
            double dx = Field.Dx(s.X, o.X), dy = Field.Dy(s.Y, o.Y), d = Math.Sqrt(dx * dx + dy * dy);
            if (d >= Ship.Radius * 2 || d < 1e-6) continue;
            double nx = dx / d, ny = dy / d, push = (Ship.Radius * 2 - d) / 2;
            s.X = Field.WrapX(s.X - nx * push); s.Y = Field.WrapY(s.Y - ny * push);
            o.X = Field.WrapX(o.X + nx * push); o.Y = Field.WrapY(o.Y + ny * push);
            double rel = (o.Vx - s.Vx) * nx + (o.Vy - s.Vy) * ny;
            if (rel < 0)
            {
                s.Vx += rel * nx; s.Vy += rel * ny;
                o.Vx -= rel * nx; o.Vy -= rel * ny;
            }
        }
        s.FireIn -= dt;
        if (c.Fire && s.FireIn <= 0 && Bullets.Count(b => b.Owner == s.Index) < MaxBullets)
        {
            s.FireIn = FireGap;
            double fx = Math.Cos(s.Angle), fy = Math.Sin(s.Angle);
            Bullets.Add(new Bullet
            {
                X = Field.WrapX(s.X + fx * 14), Y = Field.WrapY(s.Y + fy * 14), Vx = fx * BulletSpeed + s.Vx * 0.5, Vy = fy * BulletSpeed + s.Vy * 0.5,
                Life = BulletLife, Owner = s.Index,
            });
            Play(Sfx.Fire);
        }
    }

    /// <summary>Ships that were hit come back in their corners, once it's clear there.</summary>
    private void Comebacks(double dt)
    {
        foreach (var s in Ships)
        {
            if (s.Alive || s.Lives <= 0) continue;
            s.RespawnIn -= dt;
            if (s.RespawnIn > 0) continue;
            var (x, y) = Home(s.Index);
            bool clear = !Drones.Any(d => Field.Distance(d.X, d.Y, x, y) < 140)
                && !Bolts.Any(b => b.Free && Field.Distance(b.X, b.Y, x, y) < 160);
            if (clear || s.RespawnIn < -4) Respawn(s);
        }
    }

    private void Kill(Ship s, string cause)
    {
        s.Alive = false;
        s.Thrusting = false;
        s.Lives--;
        s.Cause = cause;
        s.RespawnIn = RespawnSeconds;
        Shatter(s.X, s.Y, PlayerColour(s.Index), 16, 1.6);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = s.X, Y = s.Y, Life = 0.7, Size = 2, Colour = 0xFFFFFFFF });
        Play(Sfx.ShipDown);
        if (s.Lives <= 0 && Ships.Count > 1 && Ships.Any(o => !o.Out))
            Effects.Add(new Effect { Kind = EffectKind.Word, X = s.X, Y = s.Y - 20, Text = $"PLAYER {s.Index + 1} OUT", Life = 1.8, Colour = PlayerColour(s.Index) });
    }

    // ───────────────────────────── shots ─────────────────────────────

    private void MoveBullets(double dt)
    {
        bool ricochet = false;
        var coreHits = new List<int>();
        foreach (var b in Bullets)
        {
            b.Life -= dt;
            if (b.Life <= 0) continue;
            var (bounces, core) = Base.Sweep(ref b.X, ref b.Y, ref b.Vx, ref b.Vy, dt);
            if (bounces > 0)
            {
                b.Bounces += bounces;
                ricochet = true;
                if (b.Bounces > MaxBounces) b.Life = 0;
            }
            if (core && !b.Hostile && Phase == Phase.Play && !Base.Destroyed)
            {
                b.Life = 0;
                coreHits.Add(b.Owner);
            }
        }
        Bullets.RemoveAll(b => b.Life <= 0);
        foreach (int owner in coreHits)
            if (!Base.Destroyed) CoreHit(owner);
        if (ricochet && _ricochetSound <= 0) { _ricochetSound = 0.08; Play(Sfx.Ricochet); }
    }

    // ───────────────────────────── the base ─────────────────────────────

    /// <summary>
    /// The base sends out an eliminator now and then, one at a time down the channel: the next only once the last
    /// is out (or shot), and sooner if someone is in the channel.
    /// </summary>
    private void BaseFire(double dt)
    {
        if (Calm || Base.Destroyed || !Ships.Any(s => s.Alive)) return;
        _boltIn -= dt;
        if (Bolts.Any(b => !b.Free)) return;
        bool intruder = Ships.Any(s => s.Alive && Base.ChannelAt(Base.ToLocal(s.X, s.Y).X, Base.ToLocal(s.X, s.Y).Y) > 0);
        if (intruder && _boltIn > IntruderGap) _boltIn = IntruderGap;
        if (_boltIn > 0) return;
        _boltIn = BoltGap * (0.8 + _rng.NextDouble() * 0.4);
        var (cx, cy) = Base.Core;
        Bolts.Add(new Bolt { X = cx, Y = cy, Leg = Base.Path.Count - 2 });
        Play(Sfx.Bolt);
    }

    /// <summary>
    /// Eliminators run out along the channel (in the base's own frame, so they follow it round as it turns), then
    /// out of the mouth and after the nearest ship, bouncing off the walls, until they burn out.
    /// </summary>
    private void MoveBolts(double dt)
    {
        foreach (var b in Bolts)
        {
            b.T += dt;
            if (!b.Free)
            {
                double go = BoltSpeed * dt;
                while (go > 0 && b.Leg >= 0)
                {
                    var (tx, ty) = Base.Path[b.Leg];
                    double dx = tx - b.X, dy = ty - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > go)
                    {
                        b.X += dx / d * go;
                        b.Y += dy / d * go;
                        b.Vx = dx / d;
                        b.Vy = dy / d;
                        go = 0;
                    }
                    else
                    {
                        b.X = tx;
                        b.Y = ty;
                        go -= d;
                        b.Leg--;
                    }
                }
                if (b.Free)
                {
                    // Out of the mouth: onto the screen, going the way the channel pointed.
                    var (m1x, m1y) = Base.Path[1];
                    var (m0x, m0y) = Base.Path[0];
                    double l = Math.Sqrt((m0x - m1x) * (m0x - m1x) + (m0y - m1y) * (m0y - m1y));
                    var (vx, vy) = Base.TurnOut((m0x - m1x) / l * BoltHuntSpeed, (m0y - m1y) / l * BoltHuntSpeed);
                    (b.X, b.Y) = Base.ToWorld(b.X, b.Y);
                    (b.Vx, b.Vy) = (vx, vy);
                }
                continue;
            }
            b.Life -= dt;
            if (b.Life <= 0) { b.Dead = true; continue; }
            // After the nearest ship, turning towards it only so fast.
            var target = Ships.Where(s => s.Alive).MinBy(s => Field.Distance(b.X, b.Y, s.X, s.Y));
            if (target != null)
            {
                double want = Math.Atan2(Field.Dy(b.Y, target.Y), Field.Dx(b.X, target.X)), now = Math.Atan2(b.Vy, b.Vx);
                double turn = Math.Clamp(Field.Turn(now, want), -1.5 * dt, 1.5 * dt);
                double a = now + turn;
                b.Vx = Math.Cos(a) * BoltHuntSpeed;
                b.Vy = Math.Sin(a) * BoltHuntSpeed;
            }
            Base.Sweep(ref b.X, ref b.Y, ref b.Vx, ref b.Vy, dt);
        }
        Bolts.RemoveAll(b => b.Dead);
    }

    /// <summary>Where an eliminator is on the screen.</summary>
    public (double X, double Y) BoltAt(Bolt b) => b.Free ? (b.X, b.Y) : Base.ToWorld(b.X, b.Y);

    private void CoreHit(int owner)
    {
        Base.CoreHp--;
        Base.Flash = 0.35;
        var (cx, cy) = Base.CoreOnScreen;
        Award(owner, CorePoints, cx, cy);
        Play(Sfx.CoreHit);
        if (Base.CoreHp <= 0) Eliminate(owner);
    }

    /// <summary>The core shot out: the base flies apart, and whoever did it gets the bonus.</summary>
    private void Eliminate(int owner)
    {
        Base.Destroyed = true;
        Eliminator = owner;
        LastBonus = BaseBonus(Wave);
        var (cx, cy) = Base.CoreOnScreen;
        Award(owner, LastBonus, cx, cy - 30);
        foreach (var w in Base.Walls)
        {
            // Each wall breaks into pieces a cell long, flung out from the middle.
            int pieces = Math.Max(1, (int)Math.Round(w.Length / Fortress.Cell));
            for (int i = 0; i < pieces; i++)
            {
                double f0 = (double)i / pieces, f1 = (i + 1.0) / pieces;
                double lx = w.Ax + (w.Bx - w.Ax) * (f0 + f1) / 2, ly = w.Ay + (w.By - w.Ay) * (f0 + f1) / 2;
                var (x, y) = Base.ToWorld(lx, ly);
                double d = Math.Max(20, Math.Sqrt(lx * lx + ly * ly));
                var (ox, oy) = Base.TurnOut(lx / d, ly / d);
                double v = 60 + _rng.NextDouble() * 160;
                Wreckage.Add(new Debris
                {
                    X = x, Y = y, Vx = ox * v, Vy = oy * v, Angle = Base.Angle + Math.Atan2(w.By - w.Ay, w.Bx - w.Ax),
                    Spin = (_rng.NextDouble() - 0.5) * 5, Length = w.Length / pieces, Life = 2.2 + _rng.NextDouble() * 1.5, Colour = BaseColour,
                });
            }
        }
        Shatter(cx, cy, 0xFFFFFFFF, 30, 2.2);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = cx, Y = cy, Life = 1.4, Size = 7, Colour = 0xFFFFE04A });
        foreach (var b in Bolts)
        {
            var (x, y) = BoltAt(b);
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Life = 0.5, Size = 1, Colour = BoltColour });
        }
        Bolts.Clear();
        foreach (var d in Drones) DroneDown(d, -1);
        Drones.Clear();
        Bullets.RemoveAll(b => b.Hostile);
        Hum(false);
        Play(Sfx.BaseBlast);
        string who = Ships.Count > 1 ? $"PLAYER {owner + 1}  " : "";
        Say("BASE ELIMINATED!", $"{who}BONUS {LastBonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    // ───────────────────────────── the drones ─────────────────────────────

    /// <summary>Drones come in from the edges of the screen, away from the ships, up to the wave's number at once.</summary>
    private void Reinforce(double dt)
    {
        if (Calm || Drones.Count >= DroneCap) return;
        if ((_droneIn -= dt) > 0) return;
        _droneIn = Math.Max(2.5, 5 - 0.25 * Wave) * (0.8 + _rng.NextDouble() * 0.4);
        for (int tries = 0; tries < 20; tries++)
        {
            double x, y;
            if (_rng.Next(2) == 0) { x = _rng.NextDouble() * Field.Width; y = _rng.Next(2) == 0 ? 2 : Field.Height - 2; }
            else { x = _rng.Next(2) == 0 ? 2 : Field.Width - 2; y = _rng.NextDouble() * Field.Height; }
            if (Ships.Any(s => s.Alive && Field.Distance(x, y, s.X, s.Y) < 320)) continue;
            double a = Math.Atan2(Field.CY - y, Field.CX - x);
            Drones.Add(new Drone { X = x, Y = y, Vx = Math.Cos(a) * 60, Vy = Math.Sin(a) * 60, Angle = a, FireIn = 1.5 + _rng.NextDouble() });
            return;
        }
    }

    /// <summary>A point on a ring round the base, part of the way round from one place towards another: the way past it.</summary>
    public (double X, double Y) RoundTheBase(double fromX, double fromY, double toX, double toY)
    {
        double from = Math.Atan2(Field.Dy(Base.Y, fromY), Field.Dx(Base.X, fromX)), to = Math.Atan2(Field.Dy(Base.Y, toY), Field.Dx(Base.X, toX));
        double step = Math.Clamp(Field.Turn(from, to), -0.8, 0.8), r = Base.Reach + 55;
        return (Field.WrapX(Base.X + Math.Cos(from + step) * r), Field.WrapY(Base.Y + Math.Sin(from + step) * r));
    }

    /// <summary>Drones close on the nearest ship (round the base if it's in the way) and shoot when they can see it.</summary>
    private void MoveDrones(double dt)
    {
        foreach (var d in Drones)
        {
            d.T += dt;
            var target = Ships.Where(s => s.Alive).MinBy(s => Field.Distance(d.X, d.Y, s.X, s.Y));
            double tx = Field.CX, ty = Field.CY - 300;
            bool sees = false;
            if (target != null)
            {
                (tx, ty) = (target.X, target.Y);
                sees = Base.Clear(d.X, d.Y, tx, ty, Drone.Radius);
                if (!sees) (tx, ty) = RoundTheBase(d.X, d.Y, tx, ty);
            }
            double dx = Field.Dx(d.X, tx), dy = Field.Dy(d.Y, ty), dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            // They hang back a little once close, circling, rather than ramming straight in.
            double speed = DroneSpeed * (sees && dist < 160 ? 0.55 : 1);
            double wx = dx / dist * speed, wy = dy / dist * speed;
            if (sees && dist < 160) { wx += -dy / dist * speed * 0.6; wy += dx / dist * speed * 0.6; }
            d.Vx += (wx - d.Vx) * 1.8 * dt;
            d.Vy += (wy - d.Vy) * 1.8 * dt;
            d.X = Field.WrapX(d.X + d.Vx * dt);
            d.Y = Field.WrapY(d.Y + d.Vy * dt);
            Base.Collide(ref d.X, ref d.Y, ref d.Vx, ref d.Vy, Drone.Radius, 0.6);
            if (Math.Abs(d.Vx) + Math.Abs(d.Vy) > 1) d.Angle = Math.Atan2(d.Vy, d.Vx);
            if (Phase != Phase.Play || target == null || Calm) continue;
            d.FireIn -= dt;
            if (d.FireIn <= 0 && sees && dist < 420)
            {
                d.FireIn = DroneFireGap * (0.7 + _rng.NextDouble() * 0.6);
                double spread = Math.Max(0.12, 0.35 - 0.025 * Wave);
                double a = Math.Atan2(dy, dx) + (_rng.NextDouble() - 0.5) * spread;
                Bullets.Add(new Bullet { X = d.X, Y = d.Y, Vx = Math.Cos(a) * DroneShotSpeed, Vy = Math.Sin(a) * DroneShotSpeed, Life = 1.5, Owner = -1 });
                Play(Sfx.DroneFire);
            }
        }
        Drones.RemoveAll(d => d.Dead);
    }

    private void DroneDown(Drone d, int by)
    {
        d.Dead = true;
        if (by >= 0) Award(by, DronePoints, d.X, d.Y);
        Shatter(d.X, d.Y, DroneColour, 10, 1.1);
        Play(Sfx.DroneDown);
    }

    // ───────────────────────────── hits ─────────────────────────────

    private void Hits()
    {
        foreach (var b in Bullets)
        {
            if (b.Life <= 0) continue;
            if (!b.Hostile)
            {
                var d = Drones.FirstOrDefault(d => !d.Dead && Swept(b, d.X, d.Y, d.Vx, d.Vy) < Drone.Radius + 3);
                if (d != null) { b.Life = 0; DroneDown(d, b.Owner); continue; }
                var bolt = Bolts.FirstOrDefault(o => !o.Dead && Swept(b, BoltAt(o).X, BoltAt(o).Y, o.Free ? o.Vx : 0, o.Free ? o.Vy : 0) < Bolt.Radius + 4);
                if (bolt != null)
                {
                    b.Life = 0;
                    bolt.Dead = true;
                    var (x, y) = BoltAt(bolt);
                    Award(b.Owner, BoltPoints, x, y);
                    Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Life = 0.4, Size = 1, Colour = BoltColour });
                    Play(Sfx.BoltDown);
                    continue;
                }
            }
            foreach (var s in Ships)
            {
                if (!s.Alive || s.Safe > 0 || s.Index == b.Owner) continue;
                // Team-mates' shots pass through each other; in a versus game they kill.
                if (!b.Hostile && Mode != Mode.Versus) continue;
                if (Swept(b, s.X, s.Y, s.Vx, s.Vy) > Ship.Radius + 2) continue;
                b.Life = 0;
                if (b.Hostile) Kill(s, "a drone's shot");
                else
                {
                    Kill(s, $"player {b.Owner + 1}");
                    Ships[b.Owner].Kills++;
                    Award(b.Owner, KillPoints, s.X, s.Y);
                }
                break;
            }
        }
        Bullets.RemoveAll(b => b.Life <= 0);
        Bolts.RemoveAll(b => b.Dead);
        Drones.RemoveAll(d => d.Dead);
        foreach (var s in Ships)
        {
            if (!s.Alive || s.Safe > 0) continue;
            var bolt = Bolts.FirstOrDefault(b => Dist(BoltAt(b), s.X, s.Y) < Bolt.Radius + Ship.Radius - 2);
            if (bolt != null)
            {
                bolt.Dead = true;
                Kill(s, "an eliminator");
                continue;
            }
            var drone = Drones.FirstOrDefault(d => !d.Dead && Field.Distance(d.X, d.Y, s.X, s.Y) < Drone.Radius + Ship.Radius - 2);
            if (drone != null)
            {
                DroneDown(drone, -1);
                Kill(s, "a drone");
            }
        }
        Bolts.RemoveAll(b => b.Dead);
        Drones.RemoveAll(d => d.Dead);
    }

    private static double Dist((double X, double Y) p, double x, double y) => Field.Distance(p.X, p.Y, x, y);

    /// <summary>
    /// How near a shot came to something over the last step, the two moving as they do: shots are quick enough to
    /// jump right over a small thing in one step.
    /// </summary>
    private static double Swept(Bullet b, double x, double y, double vx, double vy)
    {
        double px = Field.Dx(b.X, x), py = Field.Dy(b.Y, y);
        return Fortress.NearSegment(px, py, -(b.Vx - vx) * Field.Step, -(b.Vy - vy) * Field.Step, 0, 0);
    }

    private void Award(int player, int points, double x, double y)
    {
        if (player < 0 || player >= Ships.Count) return;
        var s = Ships[player];
        s.Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 12, Text = points.ToString(), Life = 0.8, Colour = PlayerColour(player) });
        while (s.Score >= s.NextExtra)
        {
            s.NextExtra += ExtraLifeEvery;
            s.Lives++;
            Play(Sfx.ExtraLife);
            Effects.Add(new Effect { Kind = EffectKind.Word, X = s.X, Y = s.Y - 30, Text = "EXTRA SHIP", Life = 1.5, Colour = PlayerColour(player) });
        }
    }

    private void Shatter(double x, double y, uint colour, int n, double life)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 170;
            Wreckage.Add(new Debris
            {
                X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Angle = _rng.NextDouble() * 6, Spin = (_rng.NextDouble() - 0.5) * 12,
                Length = 6 + _rng.NextDouble() * 12, Life = life * (0.5 + _rng.NextDouble() * 0.5), Colour = colour,
            });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Vx *= 1 - 0.4 * dt;
            d.Vy *= 1 - 0.4 * dt;
            d.Angle += d.Spin * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, params Controls[] c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks);

    /// <summary>Straight into a wave's fight, past its number, with nobody untouchable.</summary>
    internal void Go(int wave = 1, int players = 1, Mode? mode = null)
    {
        StartGame(players, mode);
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        foreach (var s in Ships) s.Safe = 0;
        Message = SubMessage = null;
    }

    internal Drone PutDrone(double x, double y)
    {
        var d = new Drone { X = x, Y = y, FireIn = 99 };
        Drones.Add(d);
        return d;
    }

    /// <summary>An eliminator at the core, about to run out along the channel.</summary>
    internal Bolt PutBolt()
    {
        var (cx, cy) = Base.Core;
        var b = new Bolt { X = cx, Y = cy, Leg = Base.Path.Count - 2 };
        Bolts.Add(b);
        return b;
    }

    internal Bolt PutFreeBolt(double x, double y, double vx, double vy)
    {
        var b = new Bolt { X = x, Y = y, Vx = vx, Vy = vy, Leg = -1 };
        Bolts.Add(b);
        return b;
    }

    internal void SetPhaseForTest(Phase p) => SetPhase(p);
    internal void Anger() => PhaseTime = AngerSeconds;
}
