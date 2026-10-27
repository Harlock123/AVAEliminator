using Avalonia.Input;
using AVAEliminator.Engine;
using static AVAEliminator.Tests.Harness;

namespace AVAEliminator.Tests;

/// <summary>The keys, through the input handler and into the game: one player alone, and two sharing the keyboard.</summary>
public sealed class InputTests
{
    /// <summary>Holds keys down through the handler for half a second, and says what each ship did.</summary>
    private static (double Turned, double Vx, double Vy, int Shots)[] Hold(InputHandler input, int players, params Key[] keys)
    {
        var g = Calm(players: players, mode: players > 1 ? Mode.Team : Mode.Solo);
        for (int i = 0; i < players; i++) Put(g, i, 100 + i * 60, 700, -Math.PI / 2);
        foreach (var k in keys) input.OnKeyDown(k);
        var shots = new int[players];
        var seen = new HashSet<Bullet>();
        for (int t = 0; t < 30; t++)
        {
            g.Tick(input.Players(players));
            foreach (var b in g.Bullets.Where(seen.Add)) shots[b.Owner]++;
        }
        foreach (var k in keys) input.OnKeyUp(k);
        return g.Ships.Select(s => (s.Angle + Math.PI / 2, s.Vx, s.Vy, shots[s.Index])).ToArray();
    }

    [Fact]
    public void Right_turns_clockwise_on_the_screen_and_left_anticlockwise()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Right, Key.D }) Assert.True(Hold(input, 1, key)[0].Turned > 1, $"{key}");
        foreach (var key in new[] { Key.Left, Key.A }) Assert.True(Hold(input, 1, key)[0].Turned < -1, $"{key}");
        // Facing up the screen and turning right, the nose swings to the right: thrust then pushes right.
        var g = Calm();
        var s = Put(g, 0, 100, 700, -Math.PI / 2);
        input.OnKeyDown(Key.Right);
        g.Run(21, input.Players(1));
        input.OnKeyUp(Key.Right);
        input.OnKeyDown(Key.Up);
        g.Run(20, input.Players(1));
        input.OnKeyUp(Key.Up);
        Assert.True(s.Vx > 50);
    }

    [Fact]
    public void Up_thrusts_the_way_the_ship_faces_up_the_screen_when_it_faces_up()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Up, Key.W })
        {
            var r = Hold(input, 1, key)[0];
            Assert.True(r.Vy < -50, $"{key}: up the screen is less y");
            Assert.Equal(0, r.Vx, 6);
        }
        var still = Hold(input, 1, Key.Down)[0];
        Assert.Equal((0.0, 0.0), (still.Vx, still.Vy));
    }

    [Fact]
    public void Alone_every_fire_key_fires_and_holding_one_keeps_firing()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Space, Key.Z, Key.RightCtrl, Key.LeftCtrl, Key.RightShift, Key.LeftShift, Key.F, Key.OemQuestion })
            Assert.True(Hold(input, 1, key)[0].Shots >= 2, $"{key}");
        Assert.Equal(0, Hold(input, 1, Key.X)[0].Shots);
    }

    [Fact]
    public void Two_players_share_the_keyboard_arrows_for_one_and_WAD_for_two()
    {
        using var input = new InputHandler();
        var arrows = Hold(input, 2, Key.Right, Key.Up, Key.RightCtrl);
        Assert.True(arrows[0].Turned > 1 && arrows[0].Shots >= 2);
        Assert.Equal((0.0, 0.0, 0.0, 0), arrows[1]);
        var wasd = Hold(input, 2, Key.A, Key.W, Key.LeftCtrl);
        Assert.True(wasd[1].Turned < -1 && wasd[1].Shots >= 2);
        Assert.Equal((0.0, 0.0, 0.0, 0), wasd[0]);
        // Space is a one-player key: with two it does nothing.
        Assert.All(Hold(input, 2, Key.Space), r => Assert.Equal(0, r.Shots));
    }

    [Fact]
    public void Players_three_and_four_need_gamepads()
    {
        using var input = new InputHandler();
        var r = Hold(input, 4, Key.Right, Key.D, Key.Up, Key.W, Key.RightCtrl, Key.LeftCtrl);
        Assert.True(r[0].Shots > 0 && r[1].Shots > 0);
        Assert.Equal((0.0, 0.0, 0.0, 0), r[2]);
        Assert.Equal((0.0, 0.0, 0.0, 0), r[3]);
    }

    [Fact]
    public void Enter_starts()
    {
        using var input = new InputHandler();
        Assert.False(input.Start);
        input.OnKeyDown(Key.Enter);
        Assert.True(input.Start);
    }
}
