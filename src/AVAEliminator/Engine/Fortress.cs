namespace AVAEliminator.Engine;

/// <summary>A straight piece of the base's wall, in the base's own frame.</summary>
public readonly record struct Wall(double Ax, double Ay, double Bx, double By)
{
    public double Length => Math.Sqrt((Bx - Ax) * (Bx - Ax) + (By - Ay) * (By - Ay));
}

/// <summary>
/// The Eliminator base: a great block of a fortress in the middle of the arena, built on a grid of cells, with one
/// narrow channel winding in from a mouth on its edge to the core. Shots and ships bounce off its walls; the only
/// way to hurt it is to get a shot down the channel into the core. Later bases have longer, more winding channels
/// and turn slowly about their middle. Everything about it is kept in its own frame (the middle at 0, 0) and turned
/// out onto the screen.
/// </summary>
public sealed class Fortress
{
    public const double Cell = 50;
    /// <summary>How close a shot has to come to the middle of the core to hit it.</summary>
    public const double CoreRadius = 15;

    /// <summary>
    /// The bases, in order: '#' solid, '.' the channel, 'C' the core at its end, ' ' outside (the corners cut off).
    /// The channel's mouth is where it meets the outside.
    /// </summary>
    public static readonly string[][] Layouts =
    [
        [
            " ####### ",
            "#########",
            "#########",
            "....C####",
            "#########",
            "#########",
            " ####### ",
        ],
        [
            " ####### ",
            "#########",
            "###.C####",
            "###.#####",
            "###.#####",
            "###.#####",
            " ##.#### ",
        ],
        [
            " ####### ",
            "#########",
            "#C...####",
            "####.####",
            "####.....",
            "#########",
            " ####### ",
        ],
        [
            " ##.#### ",
            "###.#####",
            "###...###",
            "#####.###",
            "##C...###",
            "#########",
            " ####### ",
        ],
        [
            " ####### ",
            "#.....###",
            "#.###.###",
            "..#C#.###",
            "###.#.###",
            "###...###",
            " ####### ",
        ],
        [
            " ####### ",
            "#.......#",
            "#.#####.#",
            "#.#C..#.#",
            "#.###.#.#",
            "#.....#..",
            " ####### ",
        ],
    ];

    public int Cols { get; }
    public int Rows { get; }
    public int Layout { get; }
    private readonly bool[,] _solid;

    /// <summary>The middle, on the screen, and how far it has turned.</summary>
    public double X = Field.CX, Y = Field.CY, Angle;
    /// <summary>How fast it turns (radians a second; the sign is the way round).</summary>
    public double Spin;

    public List<Wall> Walls { get; } = [];
    /// <summary>The way in, in the base's frame: a point outside the mouth, then each cell of the channel to the core.</summary>
    public List<(double X, double Y)> Path { get; } = [];
    public (double X, double Y) Core => Path[^1];
    public int CoreHp, CoreMaxHp = 1;
    /// <summary>A flash when the core is hit.</summary>
    public double Flash;
    /// <summary>Destroyed: the walls have flown apart.</summary>
    public bool Destroyed;
    /// <summary>From the middle to the furthest corner.</summary>
    public double Reach { get; }

    public Fortress(int layout)
    {
        Layout = (layout % Layouts.Length + Layouts.Length) % Layouts.Length;
        var map = Layouts[Layout];
        Rows = map.Length;
        Cols = map[0].Length;
        _solid = new bool[Cols, Rows];
        (int C, int R) core = (0, 0);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                _solid[c, r] = map[r][c] == '#';
                if (map[r][c] == 'C') core = (c, r);
            }
        BuildWalls();
        BuildPath(map, core);
        Reach = Math.Sqrt(Cols * Cols + Rows * Rows) * Cell / 2;
        CoreHp = CoreMaxHp;
    }

    /// <summary>The middle of a cell, in the base's frame.</summary>
    public (double X, double Y) CellCentre(int c, int r) => ((c + 0.5 - Cols / 2.0) * Cell, (r + 0.5 - Rows / 2.0) * Cell);

    private bool SolidCell(int c, int r) => c >= 0 && r >= 0 && c < Cols && r < Rows && _solid[c, r];

    /// <summary>Whether a point in the base's frame is inside the solid part.</summary>
    public bool SolidAt(double lx, double ly)
    {
        int c = (int)Math.Floor(lx / Cell + Cols / 2.0), r = (int)Math.Floor(ly / Cell + Rows / 2.0);
        return SolidCell(c, r);
    }

    /// <summary>The walls: every edge between a solid cell and an open one, joined into long straight runs with the solid on one side.</summary>
    private void BuildWalls()
    {
        double x0 = -Cols / 2.0 * Cell, y0 = -Rows / 2.0 * Cell;
        // Across: the line above each row (0 to Rows). A run is broken where the solid side changes over.
        for (int line = 0; line <= Rows; line++)
        {
            int start = -1;
            bool side = false;
            for (int c = 0; c <= Cols; c++)
            {
                bool above = SolidCell(c, line - 1), edge = c < Cols && above != SolidCell(c, line);
                if (start >= 0 && (!edge || above != side))
                {
                    Walls.Add(new Wall(x0 + start * Cell, y0 + line * Cell, x0 + c * Cell, y0 + line * Cell));
                    start = -1;
                }
                if (edge && start < 0) { start = c; side = above; }
            }
        }
        // Down: the line left of each column.
        for (int line = 0; line <= Cols; line++)
        {
            int start = -1;
            bool side = false;
            for (int r = 0; r <= Rows; r++)
            {
                bool left = SolidCell(line - 1, r), edge = r < Rows && left != SolidCell(line, r);
                if (start >= 0 && (!edge || left != side))
                {
                    Walls.Add(new Wall(x0 + line * Cell, y0 + start * Cell, x0 + line * Cell, y0 + r * Cell));
                    start = -1;
                }
                if (edge && start < 0) { start = r; side = left; }
            }
        }
    }

    /// <summary>The channel, followed back from the core to where it meets the outside.</summary>
    private void BuildPath(string[] map, (int C, int R) core)
    {
        bool Open(int c, int r) => c >= 0 && r >= 0 && c < Cols && r < Rows && map[r][c] is '.' or 'C';
        bool Outside(int c, int r) => c < 0 || r < 0 || c >= Cols || r >= Rows || map[r][c] == ' ';
        (int, int)[] dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        var cells = new List<(int C, int R)> { core };
        var seen = new HashSet<(int, int)> { core };
        (int Dx, int Dy) outward = (0, 0);
        while (true)
        {
            var (c, r) = cells[^1];
            var exit = dirs.FirstOrDefault(d => cells.Count > 1 && Outside(c + d.Item1, r + d.Item2));
            if (exit != default) { outward = exit; break; }
            var next = dirs.Select(d => (c + d.Item1, r + d.Item2)).FirstOrDefault(n => Open(n.Item1, n.Item2) && !seen.Contains(n));
            if (next == default) throw new InvalidOperationException($"Base {Layout} has a channel that goes nowhere.");
            cells.Add(next);
            seen.Add(next);
        }
        cells.Reverse();
        var (mx, my) = CellCentre(cells[0].C, cells[0].R);
        Path.Add((mx + outward.Dx * Cell * 1.3, my + outward.Dy * Cell * 1.3));
        foreach (var (c, r) in cells) Path.Add(CellCentre(c, r));
    }

    /// <summary>Which point of the channel a point in the base's frame is at (1 the mouth, the last the core), or -1 if it isn't in it.</summary>
    public int ChannelAt(double lx, double ly)
    {
        for (int i = 1; i < Path.Count; i++)
            if (Math.Abs(lx - Path[i].X) <= Cell / 2 && Math.Abs(ly - Path[i].Y) <= Cell / 2) return i;
        return -1;
    }

    // ───────────────────────────── frames ─────────────────────────────

    public (double X, double Y) ToWorld(double lx, double ly)
    {
        double c = Math.Cos(Angle), s = Math.Sin(Angle);
        return (Field.WrapX(X + lx * c - ly * s), Field.WrapY(Y + lx * s + ly * c));
    }

    public (double X, double Y) ToLocal(double wx, double wy)
    {
        double c = Math.Cos(Angle), s = Math.Sin(Angle), dx = Field.Dx(X, wx), dy = Field.Dy(Y, wy);
        return (dx * c + dy * s, -dx * s + dy * c);
    }

    public (double X, double Y) TurnOut(double lx, double ly)
    {
        double c = Math.Cos(Angle), s = Math.Sin(Angle);
        return (lx * c - ly * s, lx * s + ly * c);
    }

    public (double X, double Y) TurnIn(double wx, double wy)
    {
        double c = Math.Cos(Angle), s = Math.Sin(Angle);
        return (wx * c + wy * s, -wx * s + wy * c);
    }

    public (double X, double Y) CoreOnScreen => ToWorld(Core.X, Core.Y);

    /// <summary>
    /// The way from a point on the screen to a point in the base's frame, on the screen, measured across the base
    /// rather than round the edges of the arena.
    /// </summary>
    public (double X, double Y) Towards(double wx, double wy, double lx, double ly)
    {
        var (px, py) = ToLocal(wx, wy);
        return TurnOut(lx - px, ly - py);
    }

    // ───────────────────────────── collisions ─────────────────────────────

    private static (double X, double Y) Closest(Wall w, double px, double py)
    {
        double ex = w.Bx - w.Ax, ey = w.By - w.Ay, l2 = ex * ex + ey * ey;
        double t = l2 <= 0 ? 0 : Math.Clamp(((px - w.Ax) * ex + (py - w.Ay) * ey) / l2, 0, 1);
        return (w.Ax + ex * t, w.Ay + ey * t);
    }

    /// <summary>
    /// Keeps something round (a ship, a drone) out of the walls: pushed back out, its speed into the wall reversed
    /// and cut by <paramref name="bounce"/>. True if it touched.
    /// </summary>
    public bool Collide(ref double x, ref double y, ref double vx, ref double vy, double r, double bounce = 0.5)
    {
        if (Destroyed) return false;
        var (px, py) = ToLocal(x, y);
        if (px * px + py * py > (Reach + r + 2) * (Reach + r + 2)) return false;
        var (lvx, lvy) = TurnIn(vx, vy);
        bool hit = false;
        if (SolidAt(px, py))
        {
            // Right inside (the base turned onto it): out by the nearest wall.
            var (bx, by) = Walls.Select(w => Closest(w, px, py)).MinBy(q => (q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py));
            double dx = bx - px, dy = by - py, d = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
            px = bx + dx / d * (r + 0.5);
            py = by + dy / d * (r + 0.5);
            hit = true;
        }
        for (int pass = 0; pass < 3; pass++)
        {
            bool any = false;
            foreach (var w in Walls)
            {
                var (qx, qy) = Closest(w, px, py);
                double dx = px - qx, dy = py - qy, d2 = dx * dx + dy * dy;
                if (d2 >= r * r) continue;
                double d = Math.Sqrt(d2);
                double nx, ny;
                if (d < 1e-6) { nx = -(w.By - w.Ay) / w.Length; ny = (w.Bx - w.Ax) / w.Length; }
                else { nx = dx / d; ny = dy / d; }
                px = qx + nx * r;
                py = qy + ny * r;
                double vn = lvx * nx + lvy * ny;
                if (vn < 0)
                {
                    lvx -= (1 + bounce) * vn * nx;
                    lvy -= (1 + bounce) * vn * ny;
                }
                any = hit = true;
            }
            if (!any) break;
        }
        if (!hit) return false;
        (x, y) = ToWorld(px, py);
        (vx, vy) = TurnOut(lvx, lvy);
        return true;
    }

    /// <summary>Where two lines cross, as a fraction along the first (from a, going d), if they do.</summary>
    private static double? Cross(double ax, double ay, double dx, double dy, Wall w)
    {
        double ex = w.Bx - w.Ax, ey = w.By - w.Ay;
        double den = dx * ey - dy * ex;
        if (Math.Abs(den) < 1e-9) return null;
        double fx = w.Ax - ax, fy = w.Ay - ay;
        double t = (fx * ey - fy * ex) / den, u = (fx * dy - fy * dx) / den;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? t : null;
    }

    /// <summary>
    /// Moves a point (a shot) on for <paramref name="dt"/>, bouncing it off the walls it meets on the way. Returns how
    /// many it bounced off, and whether it went into the core.
    /// </summary>
    public (int Bounces, bool Core) Sweep(ref double x, ref double y, ref double vx, ref double vy, double dt)
    {
        if (Destroyed)
        {
            x = Field.WrapX(x + vx * dt);
            y = Field.WrapY(y + vy * dt);
            return (0, false);
        }
        var (px, py) = ToLocal(x, y);
        double step = Math.Sqrt(vx * vx + vy * vy) * dt;
        if (px * px + py * py > (Reach + step + 4) * (Reach + step + 4))
        {
            x = Field.WrapX(x + vx * dt);
            y = Field.WrapY(y + vy * dt);
            return (0, false);
        }
        var (lvx, lvy) = TurnIn(vx, vy);
        double dx = lvx * dt, dy = lvy * dt;
        int bounces = 0;
        bool core = false;
        Wall? last = null;
        for (int i = 0; i < 4; i++)
        {
            if (!core && NearSegment(Core.X, Core.Y, px, py, px + dx, py + dy) < CoreRadius) core = true;
            double best = 2;
            Wall? hit = null;
            foreach (var w in Walls)
            {
                if (last == w) continue;
                if (Cross(px, py, dx, dy, w) is { } t && t < best) { best = t; hit = w; }
            }
            if (hit is not { } h)
            {
                px += dx;
                py += dy;
                break;
            }
            px += dx * best;
            py += dy * best;
            double nx = -(h.By - h.Ay) / h.Length, ny = (h.Bx - h.Ax) / h.Length;
            double rest = 1 - best;
            double vn = lvx * nx + lvy * ny;
            lvx -= 2 * vn * nx;
            lvy -= 2 * vn * ny;
            double dn = dx * nx + dy * ny;
            dx = (dx - 2 * dn * nx) * rest;
            dy = (dy - 2 * dn * ny) * rest;
            last = h;
            bounces++;
        }
        (x, y) = ToWorld(px, py);
        (vx, vy) = TurnOut(lvx, lvy);
        return (bounces, core);
    }

    /// <summary>How near a point comes to a line between two others.</summary>
    public static double NearSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var (qx, qy) = Closest(new Wall(ax, ay, bx, by), px, py);
        return Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
    }

    /// <summary>
    /// Whether the way from one point on the screen to another is clear of the walls; with a
    /// <paramref name="width"/>, a band that wide either side of it must be clear too.
    /// </summary>
    public bool Clear(double ax, double ay, double bx, double by, double width = 0)
    {
        if (Destroyed) return true;
        // Both ends measured from the middle of the base: the straight line across it, not round the edges.
        var (px, py) = ToLocal(ax, ay);
        var (qx, qy) = ToLocal(bx, by);
        double ldx = qx - px, ldy = qy - py;
        double l = Math.Max(1e-6, Math.Sqrt(ldx * ldx + ldy * ldy));
        double nx = -ldy / l * width, ny = ldx / l * width;
        foreach (var w in Walls)
        {
            if (Cross(px, py, ldx, ldy, w) != null) return false;
            if (width > 0 && (Cross(px + nx, py + ny, ldx, ldy, w) != null || Cross(px - nx, py - ny, ldx, ldy, w) != null)) return false;
        }
        return true;
    }
}
