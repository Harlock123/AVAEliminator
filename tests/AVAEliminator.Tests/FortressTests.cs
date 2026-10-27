using AVAEliminator.Engine;

namespace AVAEliminator.Tests;

/// <summary>The base: its walls, its channel and core, and things bouncing off it.</summary>
public sealed class FortressTests
{
    public static IEnumerable<object[]> Layouts() => Enumerable.Range(0, Fortress.Layouts.Length).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_base_has_one_channel_from_a_mouth_on_its_edge_to_the_core(int layout)
    {
        var b = new Fortress(layout);
        Assert.True(b.Path.Count >= 3);
        // It starts outside, clear of everything.
        var (ox, oy) = b.Path[0];
        Assert.False(b.SolidAt(ox, oy));
        Assert.Equal(-1, b.ChannelAt(ox, oy));
        // Then a cell at a time, each next to the last, open, and in the channel.
        for (int k = 1; k < b.Path.Count; k++)
        {
            var (x, y) = b.Path[k];
            Assert.False(b.SolidAt(x, y));
            Assert.Equal(k, b.ChannelAt(x, y));
            if (k > 1) Assert.Equal(Fortress.Cell, Math.Abs(x - b.Path[k - 1].X) + Math.Abs(y - b.Path[k - 1].Y), 6);
        }
        Assert.Equal(b.Core, b.Path[^1]);
        // The core is walled in on three sides: only the way it came is open.
        var (cx, cy) = b.Core;
        int open = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Count(d => !b.SolidAt(cx + d.Item1 * Fortress.Cell, cy + d.Item2 * Fortress.Cell));
        Assert.Equal(1, open);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void From_outside_the_core_can_be_seen_only_down_the_channel(int layout)
    {
        var b = new Fortress(layout);
        var (cx, cy) = b.CoreOnScreen;
        int seen = 0;
        for (int i = 0; i < 72; i++)
        {
            double a = i * Math.PI * 2 / 72;
            if (b.Clear(b.X + Math.Cos(a) * (b.Reach + 30), b.Y + Math.Sin(a) * (b.Reach + 30), cx, cy)) seen++;
        }
        // The first base's channel runs straight in, so the core shows down it; the rest wind, and it never shows.
        if (layout == 0) Assert.InRange(seen, 1, 3);
        else Assert.Equal(0, seen);
    }

    [Fact]
    public void The_walls_run_between_solid_and_open_and_join_up_into_long_runs()
    {
        var b = new Fortress(0);
        foreach (var w in b.Walls)
        {
            Assert.True(w.Ax == w.Bx || w.Ay == w.By, "straight across or down");
            double mx = (w.Ax + w.Bx) / 2, my = (w.Ay + w.By) / 2, nx = -(w.By - w.Ay) / w.Length, ny = (w.Bx - w.Ax) / w.Length;
            Assert.NotEqual(b.SolidAt(mx + nx * 3, my + ny * 3), b.SolidAt(mx - nx * 3, my - ny * 3));
        }
        Assert.Contains(b.Walls, w => w.Length >= Fortress.Cell * 7);
    }

    [Fact]
    public void Frames_turn_there_and_back()
    {
        var b = new Fortress(3) { Angle = 0.7 };
        var (wx, wy) = b.ToWorld(80, -40);
        var (lx, ly) = b.ToLocal(wx, wy);
        Assert.Equal(80, lx, 6);
        Assert.Equal(-40, ly, 6);
        Assert.Equal(Math.Sqrt(80 * 80 + 40 * 40), Field.Distance(wx, wy, b.X, b.Y), 6);
    }

    [Fact]
    public void A_ship_flying_into_a_wall_bounces_off_it_and_stays_outside()
    {
        var b = new Fortress(0);
        // At the left side of the base's top half, flying right into it.
        double x = b.X - 225 - Ship.Radius - 2, y = b.Y - 100, vx = 200, vy = 0;
        for (int i = 0; i < 30; i++)
        {
            x += vx * Field.Step;
            b.Collide(ref x, ref y, ref vx, ref vy, Ship.Radius, 0.4);
        }
        Assert.True(vx < 0, "sent back");
        Assert.Equal(-80, vx, 1);
        Assert.True(x <= b.X - 225 - Ship.Radius + 0.01);
    }

    [Fact]
    public void Something_the_turning_base_lands_on_is_pushed_out()
    {
        var b = new Fortress(0);
        double x = b.X + 100, y = b.Y + 100, vx = 0, vy = 0;
        Assert.True(b.SolidAt(100, 100));
        Assert.True(b.Collide(ref x, ref y, ref vx, ref vy, Ship.Radius));
        var (lx, ly) = b.ToLocal(x, y);
        Assert.False(b.SolidAt(lx, ly));
    }

    [Fact]
    public void A_shot_bounces_off_a_wall_like_light_off_a_mirror()
    {
        var b = new Fortress(0);
        // Down and to the right into the top wall of the base, from above.
        double x = b.X - 50, y = b.Y - 175 - 30, vx = 300, vy = 300;
        var (bounces, core) = b.Sweep(ref x, ref y, ref vx, ref vy, 0.2);
        Assert.Equal(1, bounces);
        Assert.False(core);
        Assert.Equal(300, vx, 6);
        Assert.Equal(-300, vy, 6);
        Assert.True(y < b.Y - 175, "back above the wall");
        Assert.Equal(b.X - 50 + 60, x, 6);
    }

    [Fact]
    public void A_shot_down_the_channel_reaches_the_core()
    {
        var b = new Fortress(0);
        var (mx, my) = b.ToWorld(b.Path[0].X, b.Path[0].Y);
        double x = mx, y = my, vx = 560, vy = 0, hits = 0;
        for (int i = 0; i < 60 && hits == 0; i++)
            if (b.Sweep(ref x, ref y, ref vx, ref vy, Field.Step).Core) hits++;
        Assert.Equal(1, hits);
    }

    [Fact]
    public void A_shot_can_ricochet_round_into_the_core()
    {
        // The second base's channel turns a corner: a shot off the wall at the turn goes on to the core.
        var b = new Fortress(1);
        var (mx, my) = b.ToWorld(b.Path[0].X, b.Path[0].Y);
        var (cx, cy) = b.CoreOnScreen;
        Assert.False(b.Clear(mx, my, cx, cy));
        // Up the channel from the mouth, aimed to glance off its far wall towards the core.
        double best = 0;
        for (double a = -Math.PI / 2 - 0.5; a < -Math.PI / 2 + 0.5; a += 0.01)
        {
            double x = mx, y = my, vx = Math.Cos(a) * 560, vy = Math.Sin(a) * 560;
            int bounces = 0;
            for (int i = 0; i < 90; i++)
            {
                var (n, core) = b.Sweep(ref x, ref y, ref vx, ref vy, Field.Step);
                bounces += n;
                if (core) { best = Math.Max(best, bounces); break; }
            }
        }
        Assert.True(best >= 1, "some angle finds the core off a wall");
    }

    [Fact]
    public void The_lines_of_sight_go_across_the_base_not_round_the_edges_of_the_arena()
    {
        var b = new Fortress(2);
        // The shorter way from the right edge to the core is round through the left edge, but the base is in the way.
        var (cx, cy) = b.CoreOnScreen;
        Assert.False(b.Clear(1000, cy, cx, cy));
        Assert.True(b.Clear(1000, 60, 30, 60), "well clear of the base");
    }
}
