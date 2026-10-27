using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAEliminator.Engine;
using Effect = AVAEliminator.Engine.Effect;

namespace AVAEliminator.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAEliminator/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        width = Math.Round(width * 4) / 4;
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Magenta = 0xFFFF5AE0;
    private const double W = Field.Width, H = Field.Height;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private static uint Mix(uint a, uint b, double f)
    {
        f = Math.Clamp(f, 0, 1);
        uint C(int shift) => (uint)((((a >> shift) & 0xFF) * (1 - f) + ((b >> shift) & 0xFF) * f)) << shift;
        return 0xFF000000 | C(16) | C(8) | C(0);
    }

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.2 * alpha), width * 3.4), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    /// <summary>Draws something wherever it shows: near an edge, its other half shows at the far side too, as the arena wraps.</summary>
    private static void Wrapped(double x, double y, double r, Action<double, double> draw)
    {
        draw(x, y);
        bool l = x < r, rt = x > W - r, t = y < r, b = y > H - r;
        if (l) draw(x + W, y);
        if (rt) draw(x - W, y);
        if (t) draw(x, y + H);
        if (b) draw(x, y - H);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000006), new Rect(0, 0, W, H));
        DrawStars(dc);
        if (InGame) DrawArena(dc, g);
        else DrawBase(dc, _show, g.State == GameState.Title ? 0.32 : 0.2, false);
        foreach (var e in g.Effects) DrawEffect(dc, e);
        if (InGame) DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
            Text(dc, "PAUSED", new Point(W / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(W / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(W / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
        {
            string what = g.Mode switch { Mode.Solo => "ONE PLAYER", Mode.Team => $"{g.Ships.Count} PLAYERS AS A TEAM", _ => $"{g.Ships.Count} PLAYERS, VERSUS" };
            Text(dc, $"DEMONSTRATION: {what} - PRESS ENTER OR 1-4 TO PLAY", new Point(W / 2, H - 50), 24, Yellow, center: true);
        }
        if (_toast != null && _toastTime > 0)
        {
            dc.FillRectangle(Brush(0xC0000000), new Rect(W / 2 - 330, H - 128, 660, 40), 8);
            Text(dc, _toast, new Point(W / 2, H - 108), 26, 0xFF6AE8FF, center: true);
        }
    }

    private void DrawStars(DrawingContext dc)
    {
        for (int i = 0; i < 150; i++)
        {
            uint h = Hash(i, 3, 7);
            double x = h % 1024, y = (h >> 10) % 768, tw = 0.3 + 0.35 * Math.Abs(Math.Sin(_clock * (0.6 + (h >> 20) % 5 * 0.3) + i));
            double r = (h >> 6) % 7 == 0 ? 1.4 : 0.8;
            dc.DrawEllipse(Brush(Alpha(0xFFDDE4FF, tw)), null, new Point(x, y), r, r);
        }
    }

    // ───────────────────────────── the arena ─────────────────────────────

    private void DrawArena(DrawingContext dc, GameEngine g)
    {
        if (!g.Base.Destroyed)
        {
            // A new base powers up line by line.
            double alpha = g.Phase == Phase.Start ? Math.Clamp(g.PhaseTime / (GameEngine.StartSeconds * 0.7), 0, 1) : 1;
            DrawBase(dc, g.Base, alpha, true);
        }
        foreach (var d in g.Wreckage)
        {
            double a = Math.Clamp(1 - d.Age / d.Life, 0, 1), c = Math.Cos(d.Angle) * d.Length / 2, s = Math.Sin(d.Angle) * d.Length / 2;
            Glow(dc, new Point(d.X - c, d.Y - s), new Point(d.X + c, d.Y + s), d.Colour, a, 1.8);
        }
        foreach (var b in g.Bolts)
        {
            var (x, y) = g.BoltAt(b);
            DrawBolt(dc, x, y, b.T, 1);
        }
        foreach (var d in g.Drones) Wrapped(d.X, d.Y, 20, (x, y) => DrawDrone(dc, x, y, d.Angle, d.T, 1));
        foreach (var b in g.Bullets)
        {
            double l = Math.Max(1, Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy));
            var tail = new Point(b.X - b.Vx / l * 9, b.Y - b.Vy / l * 9);
            uint col = b.Hostile ? Red : Mix(GameEngine.PlayerColour(b.Owner), 0xFFFFFFFF, 0.55);
            Glow(dc, tail, new Point(b.X, b.Y), col, 1, b.Hostile ? 3 : 2.4);
        }
        foreach (var s in g.Ships)
        {
            if (!s.Alive) continue;
            bool blink = s.Safe > 0 && (int)(_clock * 10) % 2 == 0;
            if (!blink) Wrapped(s.X, s.Y, 24, (x, y) => DrawShip(dc, x, y, s.Angle, GameEngine.PlayerColour(s.Index), s.Thrusting, 1.1));
            if (g.Ships.Count > 1)
                Text(dc, (s.Index + 1).ToString(), new Point(s.X, s.Y - 24), 18, Alpha(GameEngine.PlayerColour(s.Index), 0.8), center: true, shadow: false);
        }
    }

    /// <summary>
    /// The base, in its own frame turned out onto the screen: its solid mass faintly lit, the walls in glowing blue
    /// with a second line just inside them, a trail of dots down the channel, and the core pulsing at the end.
    /// </summary>
    private void DrawBase(DrawingContext dc, Fortress b, double alpha, bool live)
    {
        using var _ = dc.PushTransform(Matrix.CreateRotation(b.Angle) * Matrix.CreateTranslation(b.X, b.Y));
        // The solid part, as one shape so the cells don't show seams between them.
        double h = Fortress.Cell / 2;
        var mass = new StreamGeometry();
        using (var ctx = mass.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);
            for (int r = 0; r < b.Rows; r++)
                for (int c = 0; c < b.Cols; c++)
                {
                    var (x, y) = b.CellCentre(c, r);
                    if (!b.SolidAt(x, y)) continue;
                    ctx.BeginFigure(new Point(x - h, y - h), true);
                    ctx.LineTo(new Point(x + h, y - h));
                    ctx.LineTo(new Point(x + h, y + h));
                    ctx.LineTo(new Point(x - h, y + h));
                    ctx.EndFigure(true);
                }
        }
        dc.DrawGeometry(Brush(Alpha(0xFF1A2A80, 0.33 * alpha)), null, mass);
        uint col = GameEngine.BaseColour;
        foreach (var w in b.Walls)
        {
            // The inner line, a little way into the solid side.
            double l = w.Length, nx = -(w.By - w.Ay) / l, ny = (w.Bx - w.Ax) / l, mx = (w.Ax + w.Bx) / 2, my = (w.Ay + w.By) / 2;
            if (!b.SolidAt(mx + nx * 3, my + ny * 3)) { nx = -nx; ny = -ny; }
            double ux = (w.Bx - w.Ax) / l, uy = (w.By - w.Ay) / l;
            double inA = InnerEnd(b, w.Ax, w.Ay, ux, uy, nx, ny), inB = InnerEnd(b, w.Bx, w.By, -ux, -uy, nx, ny);
            Glow(dc, new Point(w.Ax + nx * 7 + ux * inA, w.Ay + ny * 7 + uy * inA), new Point(w.Bx + nx * 7 - ux * inB, w.By + ny * 7 - uy * inB), 0xFF2A4ACF, 0.7 * alpha, 1.4);
        }
        foreach (var w in b.Walls) Glow(dc, new Point(w.Ax, w.Ay), new Point(w.Bx, w.By), col, alpha, 2.4);
        // The channel: dots running out from the core, the way the eliminators come.
        for (int i = 1; i < b.Path.Count - 1; i++)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(_clock * 6 + i * 1.1);
            dc.DrawEllipse(Brush(Alpha(0xFF8AA8FF, (0.15 + 0.3 * pulse) * alpha)), null, new Point(b.Path[i].X, b.Path[i].Y), 2.2, 2.2);
        }
        DrawCore(dc, b, alpha, live);
    }

    /// <summary>How far to pull an inner line's end in, so it meets the next wall's inner line at the corner.</summary>
    private static double InnerEnd(Fortress b, double x, double y, double ux, double uy, double nx, double ny)
    {
        // Just beyond the end, on the solid side: if it's solid there the solid wraps round the corner, and the inner
        // line runs on past it to meet the next; otherwise the corner points out, and it stops short.
        return b.SolidAt(x - ux * 3 + nx * 3, y - uy * 3 + ny * 3) ? -7 : 7;
    }

    private void DrawCore(DrawingContext dc, Fortress b, double alpha, bool live)
    {
        var (x, y) = b.Core;
        double hp = live ? (double)b.CoreHp / Math.Max(1, b.CoreMaxHp) : 1;
        uint col = b.Flash > 0 ? 0xFFFFFFFF : Mix(0xFFFF3A6A, 0xFFFFE04A, 1 - hp);
        double p = 1 + 0.15 * Math.Sin(_clock * 9);
        var c = new Point(x, y);
        dc.DrawEllipse(Brush(Alpha(col, 0.18 * alpha)), null, c, 20 * p, 20 * p);
        Shape(dc, x, y, _clock * 1.5, p, col, alpha, (13, 0), (0, 13), (-13, 0), (0, -13));
        Shape(dc, x, y, -_clock * 2.2, p * 0.55, Mix(col, 0xFFFFFFFF, 0.5), alpha, (13, 0), (0, 13), (-13, 0), (0, -13));
        dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, alpha)), null, c, 3, 3);
        // How much the core has left: pips around it.
        if (live && b.CoreMaxHp > 1)
            for (int i = 0; i < b.CoreMaxHp; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI * 2 / b.CoreMaxHp;
                dc.DrawEllipse(Brush(Alpha(i < b.CoreHp ? 0xFFFFE04A : 0xFF404060, alpha)), null, new Point(x + Math.Cos(a) * 19, y + Math.Sin(a) * 19), 2, 2);
            }
    }

    private void DrawShip(DrawingContext dc, double x, double y, double angle, uint colour, bool thrust, double k)
    {
        Shape(dc, x, y, angle, k, colour, 1, (15, 0), (-10, -10), (-5, 0), (-10, 10));
        Shape(dc, x, y, angle, k, Mix(colour, 0xFFFFFFFF, 0.3), 0.7, (2, -3), (6, 0), (2, 3));
        if (thrust && (int)(_clock * 24) % 2 == 0)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            Point At(double fx, double fy) => new(x + (fx * c - fy * s) * k, y + (fx * s + fy * c) * k);
            double tip = -19 - Math.Sin(_clock * 40) * 3;
            Glow(dc, At(-7, -4), At(tip, 0), 0xFFFFB04A, 1, 1.8);
            Glow(dc, At(-7, 4), At(tip, 0), 0xFFFFB04A, 1, 1.8);
        }
    }

    /// <summary>A drone: a jagged red dart with a blinking eye.</summary>
    private static void DrawDrone(DrawingContext dc, double x, double y, double angle, double t, double k)
    {
        Shape(dc, x, y, angle, k, GameEngine.DroneColour, 1, (14, 0), (-8, -11), (-3, -4), (-11, 0), (-3, 4), (-8, 11));
        Shape(dc, x, y, angle, k, 0xFFFF9A4A, 0.8, (6, 0), (-2, -4), (-2, 4));
        if ((int)(t * 6) % 2 == 0) dc.DrawEllipse(Brush(Yellow), null, new Point(x + Math.Cos(angle) * 3 * k, y + Math.Sin(angle) * 3 * k), 1.8, 1.8);
    }

    /// <summary>An eliminator: a white-hot ball with spikes of violet light whirling round it.</summary>
    private static void DrawBolt(DrawingContext dc, double x, double y, double t, double k)
    {
        var c = new Point(x, y);
        dc.DrawEllipse(Brush(Alpha(Magenta, 0.25)), null, c, 13 * k, 13 * k);
        for (int i = 0; i < 5; i++)
        {
            double a = t * 9 + i * Math.PI * 2 / 5, l = (9 + 3 * Math.Sin(t * 30 + i)) * k;
            Glow(dc, new Point(x + Math.Cos(a) * 3 * k, y + Math.Sin(a) * 3 * k), new Point(x + Math.Cos(a) * l, y + Math.Sin(a) * l), Magenta, 1, 1.6);
        }
        dc.DrawEllipse(Brush(GameEngine.BoltColour), null, c, 4.5 * k, 4.5 * k);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 20, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 30, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the scores, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        int n = g.Ships.Count;
        for (int i = 0; i < n; i++)
        {
            var s = g.Ships[i];
            uint col = GameEngine.PlayerColour(i);
            double x = n == 1 ? 16 : 16 + i * 256;
            Text(dc, $"{i + 1}UP", new Point(x, 16), 22, s.Out ? Dim : col);
            Text(dc, s.Score.ToString("D6"), new Point(x + 48, 16), 30, s.Out ? Dim : Yellow);
            if (s.Out) { Text(dc, "OUT", new Point(x + 140, 16), 22, Red); continue; }
            int spare = Math.Max(0, s.Lives - (s.Alive ? 1 : 0));
            for (int k = 0; k < Math.Min(spare, 7); k++) DrawShip(dc, x + 10 + k * 22, 44, -Math.PI / 2, col, false, 0.55);
        }
        if (n == 1)
        {
            Text(dc, "HI", new Point(W - 16, 16), 22, Dim, right: true);
            Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(W - 52, 16), 30, Dim, right: true);
        }
        Text(dc, $"WAVE {g.Wave}", new Point(16, H - 18), 24, GameEngine.BaseColour);
        string mode = g.Mode switch { Mode.Solo => "", Mode.Team => "TEAM", _ => "VERSUS" };
        if (n > 1) Text(dc, $"{mode}   HI {Math.Max(g.HighScores.TopScore, g.Score)}", new Point(W - 16, H - 18), 24, Dim, right: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = W / 2, cy = 112;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 14) + 60, g.SubMessage != null ? 100.0 : 66.0);
        dc.FillRectangle(Brush(0xB0060818), new Rect(cx - w / 2, cy - 42, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 10), msg.Length > 14 ? 44 : 54, msg.StartsWith("GAME") ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 32), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2;
        string title = "AVA ELIMINATOR";
        uint[] hues = [0xFF6AE8FF, GameEngine.BaseColour, 0xFFB08AFF, Magenta, 0xFFFFE04A];
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 25 + 25 + i * 50, 92), 104, hues[(i + (int)(_clock * 3)) % hues.Length], center: true);
        Text(dc, "AFTER THE 1981 COLOUR VECTOR ARCADE GAME", new Point(cx, 156), 28, Dim, center: true);
        Text(dc, "FLY INTO THE BASE'S CHANNEL AND SHOOT OUT ITS CORE", new Point(cx, 214), 28, White, center: true);
        Text(dc, "SHOTS BOUNCE OFF ITS WALLS - WATCH FOR THE ELIMINATORS IT SENDS OUT", new Point(cx, 244), 24, Dim, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("ELIMINATOR", $"{GameEngine.BoltPoints}", x => DrawBolt(dc, x, 320, _clock, 1.4)),
            ("DRONE", $"{GameEngine.DronePoints}", x => DrawDrone(dc, x, 320, _clock * 1.3, _clock, 1.5)),
            ("CORE HIT", $"{GameEngine.CorePoints}", x => Shape(dc, x, 320, _clock * 1.5, 1.2, 0xFFFF3A6A, 1, (13, 0), (0, 13), (-13, 0), (0, -13))),
            ("BASE", $"{GameEngine.BaseBonus(1)}+", x => Shape(dc, x, 320, 0, 1, GameEngine.BaseColour, 1, (-22, -16), (22, -16), (22, -4), (4, -4), (4, 4), (22, 4), (22, 16), (-22, 16))),
            ("RIVAL (VERSUS)", $"{GameEngine.KillPoints}", x => DrawShip(dc, x, 320, _clock, 0xFFFF6AC8, true, 1.2)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = cx - 360 + i * 180;
            legend[i].Draw(x);
            Text(dc, legend[i].Name, new Point(x, 362), 22, Dim, center: true);
            Text(dc, legend[i].Points, new Point(x, 386), 24, Yellow, center: true);
        }
        // The choice: how many, and together or against each other.
        string players = g.ChosenPlayers == 1 ? "1 PLAYER" : $"{g.ChosenPlayers} PLAYERS";
        Text(dc, $"<  {players}  >", new Point(cx, 462), 46, GameEngine.PlayerColour(g.ChosenPlayers - 1), center: true);
        if (g.ChosenPlayers == 1) Text(dc, "AGAINST THE BASE AND ITS DRONES", new Point(cx, 506), 28, White, center: true);
        else
        {
            bool team = g.ChosenMulti == Mode.Team;
            Text(dc, "TEAM", new Point(cx - 110, 506), 32, team ? Yellow : Dim, center: true);
            Text(dc, "VERSUS", new Point(cx + 110, 506), 32, team ? Dim : Red, center: true);
            dc.DrawRectangle(null, Pen(team ? Yellow : Red, 2), new Rect(team ? cx - 160 : cx + 50, 488, team ? 100 : 120, 36), 5, 5);
            Text(dc, team ? "TOGETHER AGAINST THE BASE" : "AGAINST THE BASE AND EACH OTHER: SHOTS KILL", new Point(cx, 540), 22, Dim, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1-4 TO PLAY", new Point(cx, 588), 40, Yellow, center: true);
        Text(dc, "LEFT/RIGHT: PLAYERS    UP/DOWN: TEAM OR VERSUS    GAMEPADS: D-PAD, Y, START", new Point(cx, 634), 22, Dim, center: true);
        Text(dc, "P1: ARROWS TURN AND THRUST, RIGHT CTRL FIRES    P2: A D TURN, W THRUSTS, LEFT CTRL FIRES", new Point(cx, 670), 22, Dim, center: true);
        Text(dc, "ALONE, EITHER SET WORKS AND SPACE FIRES    PADS FOR PLAYERS 1-4", new Point(cx, 696), 22, Dim, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA SHIP EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 722), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2;
        Text(dc, "TOP ELIMINATORS", new Point(cx, 120), 68, 0xFF6AE8FF, center: true);
        Text(dc, "RANK NAME    SCORE    WAVE", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER OR 1-4 TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2;
        int p = Math.Max(0, g.EntryPlayer);
        uint col = GameEngine.PlayerColour(p);
        if (g.Ships.Count > 1) Text(dc, $"PLAYER {p + 1}", new Point(cx, 140), 48, col, center: true);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, (p < g.Ships.Count ? g.Ships[p].Score : 0).ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : col, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
