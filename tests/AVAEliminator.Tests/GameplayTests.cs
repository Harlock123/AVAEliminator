using AVAEliminator.Engine;
using static AVAEliminator.Tests.Harness;

namespace AVAEliminator.Tests;

/// <summary>The rules: the ships, the base and its eliminators, the drones, lives, the modes and the machine.</summary>
public sealed class GameplayTests
{
    private static readonly Controls Nothing = default;

    // ───────────────────────────── the ships ─────────────────────────────

    [Fact]
    public void A_ship_turns_thrusts_drifts_and_has_a_top_speed()
    {
        var g = Calm();
        var s = Put(g, 0, 100, 100, 0);
        g.Run(30, new Controls(Turn: 1));
        Assert.Equal(GameEngine.TurnRate * 0.5, s.Angle, 2);
        Put(g, 0, 100, 100, 0);
        g.Run(30, new Controls(Thrust: true));
        Assert.True(s.Vx > 150 && Math.Abs(s.Vy) < 1e-6, "pushed the way it faces");
        double v = s.Vx, x = s.X;
        g.Run(30);
        Assert.True(s.Vx < v && s.Vx > v * 0.6, "it drifts on, slowing");
        Assert.True(s.X > x + 50);
        g.Run(400, new Controls(Thrust: true));
        Assert.True(Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy) <= GameEngine.MaxSpeed + 1e-6);
    }

    [Fact]
    public void The_arena_wraps_round_at_the_edges()
    {
        var g = Calm();
        var s = Put(g, 0, Field.Width - 2, 60, 0);
        s.Vx = 200;
        g.Run(3);
        Assert.InRange(s.X, 0, 20);
        Put(g, 0, 300, 2, 0).Vy = -200;
        g.Run(3);
        Assert.InRange(s.Y, Field.Height - 20, Field.Height);
    }

    [Fact]
    public void Shots_come_from_the_nose_four_at_a_time_and_fade_out()
    {
        var g = Calm();
        var s = Put(g, 0, 100, 700, -Math.PI / 2);
        g.Run(1, new Controls(Fire: true));
        var b = Assert.Single(g.Bullets);
        Assert.Equal(100, b.X, 1);
        Assert.True(b.Y < 700 - 10 && b.Vy < -500);
        g.Run(90, new Controls(Fire: true));
        Assert.Equal(GameEngine.MaxBullets, g.Bullets.Count);
        g.Run(100);
        Assert.Empty(g.Bullets);
        Assert.Equal(-Math.PI / 2, s.Angle);
    }

    [Fact]
    public void A_ship_bumping_the_base_bounces_off_unharmed()
    {
        var g = Calm();
        var s = Put(g, 0, g.Base.X - 225 - 30, g.Base.Y - 100, 0);
        s.Vx = 250;
        g.Run(40);
        Assert.True(s.Alive);
        Assert.True(s.Vx < 0);
        Assert.Equal(GameEngine.StartLives, s.Lives);
    }

    // ───────────────────────────── the base ─────────────────────────────

    /// <summary>Puts a ship at the mouth facing down the channel of the first base, and fires.</summary>
    private static Ship AtTheMouth(GameEngine g, int i = 0)
    {
        var (mx, my) = Channel(g, 1);
        var (cx, cy) = g.Base.CoreOnScreen;
        return Put(g, i, mx, my, Heading(mx, my, cx, cy));
    }

    [Fact]
    public void A_shot_into_the_core_eliminates_the_first_base_for_the_bonus()
    {
        var g = Calm();
        var s = AtTheMouth(g);
        g.Run(1, new Controls(Fire: true));
        g.Run(30);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.True(g.Base.Destroyed);
        Assert.Equal(0, g.Eliminator);
        Assert.Equal(GameEngine.CorePoints + GameEngine.BaseBonus(1), s.Score);
        Assert.True(g.Wreckage.Count > g.Base.Walls.Count, "the walls fly apart");
        Assert.Equal("BASE ELIMINATED!", g.Message);
    }

    [Fact]
    public void Then_the_next_wave_brings_a_new_harder_base_and_everyone_back_to_their_corners()
    {
        var g = Calm();
        AtTheMouth(g);
        g.Run(1, new Controls(Fire: true));
        g.Run(30 + (int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(2, g.Wave);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.False(g.Base.Destroyed);
        Assert.Equal(1, g.Base.Layout);
        Assert.Equal(GameEngine.Home(0), (g.Ships[0].X, g.Ships[0].Y));
        Assert.True(g.Ships[0].Safe > 0);
        Assert.Equal("WAVE 2", g.Message);
    }

    [Fact]
    public void Later_bases_are_harder_longer_channels_tougher_cores_and_they_turn()
    {
        Assert.True(new Fortress(5).Path.Count > new Fortress(0).Path.Count + 10);
        Assert.Equal(1, GameEngine.CoreStrength(1));
        Assert.True(GameEngine.CoreStrength(5) > GameEngine.CoreStrength(2));
        Assert.Equal(0, GameEngine.BaseSpin(1));
        Assert.Equal(0, GameEngine.BaseSpin(2));
        Assert.NotEqual(0, GameEngine.BaseSpin(3));
        Assert.True(Math.Sign(GameEngine.BaseSpin(3)) != Math.Sign(GameEngine.BaseSpin(4)), "one way, then the other");
        Assert.True(Math.Abs(GameEngine.BaseSpin(9)) > Math.Abs(GameEngine.BaseSpin(3)));
        var g = Calm(wave: 7);
        Assert.Equal(0, g.Base.Layout);
        Assert.Equal(GameEngine.CoreStrength(7), g.Base.CoreHp);
        double a = g.Base.Angle;
        g.Run(60);
        Assert.NotEqual(a, g.Base.Angle);
    }

    [Fact]
    public void A_tougher_core_takes_more_hits_and_each_is_worth_points()
    {
        var g = Calm(wave: 3);
        g.Base.Spin = 0;
        Assert.Equal(2, g.Base.CoreHp);
        var s = Put(g, 0, 0, 0);
        // Straight down the last straight of the channel into the core.
        var (px, py) = Channel(g, g.Base.Path.Count - 3);
        var (cx, cy) = g.Base.CoreOnScreen;
        Put(g, 0, px, py, Heading(px, py, cx, cy));
        g.Run(1, new Controls(Fire: true));
        g.Run(20);
        Assert.Equal(1, g.Base.CoreHp);
        Assert.Equal(GameEngine.CorePoints, s.Score);
        Assert.Equal(Phase.Play, g.Phase);
        g.Run(1, new Controls(Fire: true));
        g.Run(20);
        Assert.Equal(Phase.Clear, g.Phase);
    }

    [Fact]
    public void Shots_bounce_off_the_base_and_cannot_hurt_it_from_outside()
    {
        var g = Calm();
        var s = Put(g, 0, g.Base.X, g.Base.Y - 260, Math.PI / 2);
        g.Run(1, new Controls(Fire: true));
        var b = Assert.Single(g.Bullets);
        g.Run(25);
        Assert.True(b.Vy < 0, "back the way it came");
        Assert.True(b.Bounces >= 1);
        Assert.Equal(1, g.Base.CoreHp);
        Assert.Equal(0, s.Score);
    }

    [Fact]
    public void An_eliminator_runs_out_along_the_channel_then_hunts_the_nearest_ship()
    {
        var g = Calm();
        var s = Put(g, 0, 150, 150);
        s.Safe = 100;
        var bolt = g.PutBolt();
        Assert.False(bolt.Free);
        int steps = 0;
        while (!bolt.Free && steps++ < 600) g.Run(1);
        Assert.True(bolt.Free);
        // It came out of the mouth, on the left.
        var (ox, oy) = Channel(g, 0);
        Assert.True(Field.Distance(bolt.X, bolt.Y, ox, oy) < 10);
        Assert.True(bolt.Vx < 0, "out of the mouth the way the channel points");
        double before = Field.Distance(bolt.X, bolt.Y, s.X, s.Y);
        g.Run(60);
        Assert.True(Field.Distance(bolt.X, bolt.Y, s.X, s.Y) < before - 100, "after the ship");
        // And it burns out.
        g.Run((int)(5 * 60));
        Assert.DoesNotContain(bolt, g.Bolts);
    }

    [Fact]
    public void An_eliminator_is_deadly_but_can_be_shot_down()
    {
        var g = Calm();
        var s = Put(g, 0, 300, 600);
        g.PutFreeBolt(300, 640, 0, -200);
        g.Run(20);
        Assert.False(s.Alive);
        Assert.Equal("an eliminator", s.Cause);
        Assert.Equal(GameEngine.StartLives - 1, s.Lives);

        g = Calm();
        s = Put(g, 0, 150, 700, -Math.PI / 2);
        var bolt = g.PutFreeBolt(150, 450, 0, 170);
        g.Run(1, new Controls(Fire: true));
        g.Run(30);
        Assert.DoesNotContain(bolt, g.Bolts);
        Assert.True(s.Alive);
        Assert.Equal(GameEngine.BoltPoints, s.Score);
    }

    [Fact]
    public void The_base_fires_now_and_then_and_sooner_at_a_ship_in_its_channel()
    {
        var g = Game();
        g.Go();
        g.Ships[0].Safe = 1000;
        var seen = new HashSet<Bolt>();
        for (int i = 0; i < 60 * 20; i++)
        {
            g.Run(1);
            seen.UnionWith(g.Bolts);
        }
        // About every three seconds.
        Assert.InRange(seen.Count, 5, 9);

        var a = Game();
        a.Go();
        var s = AtTheMouth(a);
        s.Safe = 1000;
        a.Bolts.Clear();
        a.Run(60);
        Assert.NotEmpty(a.Bolts);
    }

    [Fact]
    public void Left_too_long_the_base_gets_angry_and_fires_twice_as_often()
    {
        var g = Game();
        g.Go();
        double calm = g.BoltGap;
        g.Anger();
        g.Run(1);
        Assert.Equal(calm / 2, g.BoltGap, 6);
        Assert.Equal("THE BASE IS ANGRY!", g.Message);
    }

    // ───────────────────────────── the drones ─────────────────────────────

    [Fact]
    public void Drones_come_in_from_the_edges_up_to_the_waves_number_and_hunt_the_ships()
    {
        var g = Game();
        g.Go(wave: 4);
        foreach (var s in g.Ships) s.Safe = 1000;
        g.Run(60 * 30);
        Assert.Equal(g.DroneCap, g.Drones.Count);
        Assert.Equal(2, g.DroneCap);
        var g1 = Game();
        g1.Go();
        Assert.Equal(1, g1.DroneCap);
        var v = Game();
        v.Go(players: 2, mode: Mode.Versus);
        Assert.Equal(0, v.DroneCap);

        var h = Calm();
        var ship = Put(h, 0, 150, 150);
        ship.Safe = 1000;
        var d = h.PutDrone(150, 450);
        h.Run(60);
        Assert.True(Field.Distance(d.X, d.Y, ship.X, ship.Y) < 300 - 50);
    }

    [Fact]
    public void A_drone_is_shot_down_for_200_and_one_that_rams_a_ship_takes_it_with_it()
    {
        var g = Calm();
        var s = Put(g, 0, 150, 600, -Math.PI / 2);
        var d = g.PutDrone(150, 400);
        g.Run(1, new Controls(Fire: true));
        g.Run(25);
        Assert.DoesNotContain(d, g.Drones);
        Assert.Equal(GameEngine.DronePoints, s.Score);

        g = Calm();
        s = Put(g, 0, 150, 600);
        g.PutDrone(150, 620);
        g.Run(10);
        Assert.False(s.Alive);
        Assert.Empty(g.Drones);
        Assert.Equal("a drone", s.Cause);
    }

    [Fact]
    public void A_drones_shot_kills()
    {
        var g = Calm();
        var s = Put(g, 0, 150, 600);
        g.Bullets.Add(new Bullet { X = 150, Y = 500, Vy = 320, Life = 1.5, Owner = -1 });
        g.Run(30);
        Assert.False(s.Alive);
        Assert.Equal("a drone's shot", s.Cause);
    }

    // ───────────────────────────── lives ─────────────────────────────

    [Fact]
    public void A_ship_hit_comes_back_in_its_corner_untouchable_for_a_moment()
    {
        var g = Calm();
        var s = Put(g, 0, 300, 600);
        g.PutFreeBolt(300, 610, 0, 0);
        g.Run(1);
        Assert.False(s.Alive);
        g.Run((int)(GameEngine.RespawnSeconds * 60) + 2);
        Assert.True(s.Alive);
        Assert.Equal(GameEngine.Home(0), (s.X, s.Y));
        Assert.True(s.Safe > 2);
        g.PutFreeBolt(s.X, s.Y, 0, 0);
        g.Run(2);
        Assert.True(s.Alive, "untouchable");
    }

    [Fact]
    public void The_last_life_lost_the_game_is_over_and_a_good_score_enters_initials()
    {
        var g = Calm();
        var s = g.Ships[0];
        s.Lives = 1;
        s.Score = 99999;
        Put(g, 0, 300, 600);
        g.PutFreeBolt(300, 610, 0, 0);
        g.Run(1);
        Assert.True(s.Out);
        g.Run((int)(GameEngine.EndSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(GameEngine.GameOverSeconds * 60) + 2);
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(0, g.EntryPlayer);
        foreach (char c in "LON") g.Initials!.Type(c);
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("LON", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
        Assert.Equal(0, g.NewEntryIndex);
    }

    [Fact]
    public void With_several_players_each_good_score_enters_initials_in_turn_best_first()
    {
        var g = Calm(players: 3, mode: Mode.Team);
        g.Ships[0].Score = 50000;
        g.Ships[1].Score = 70000;
        g.Ships[2].Score = 10;
        foreach (var s in g.Ships)
        {
            s.Lives = 1;
            s.Alive = false;
            s.Lives = 0;
        }
        g.Run((int)((GameEngine.EndSeconds + GameEngine.GameOverSeconds) * 60) + 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(1, g.EntryPlayer);
        g.Initials!.Finish();
        g.Run(1);
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(0, g.EntryPlayer);
        g.Initials!.Finish();
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void A_game_goes_on_while_anyone_is_left()
    {
        var g = Calm(players: 2, mode: Mode.Team);
        g.Ships[0].Lives = 0;
        g.Ships[0].Alive = false;
        g.Run(60 * 10);
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Ships[0].Out);
        Assert.True(g.Ships[1].Alive);
    }

    [Fact]
    public void An_extra_ship_every_10000()
    {
        var g = Calm();
        var s = g.Ships[0];
        s.Score = GameEngine.ExtraLifeEvery - 50;
        var bolt = g.PutFreeBolt(150, 450, 0, 170);
        Put(g, 0, 150, 700, -Math.PI / 2);
        g.Run(1, new Controls(Fire: true));
        g.Run(30);
        Assert.DoesNotContain(bolt, g.Bolts);
        Assert.Equal(GameEngine.StartLives + 1, s.Lives);
        Assert.Equal(2 * GameEngine.ExtraLifeEvery, s.NextExtra);
    }

    // ───────────────────────────── the modes ─────────────────────────────

    [Fact]
    public void Each_player_starts_in_a_corner_of_their_own_facing_the_base()
    {
        var g = Game();
        g.StartGame(4, Mode.Team);
        Assert.Equal(4, g.Ships.Count);
        Assert.Equal(4, g.Ships.Select(s => (s.X, s.Y)).Distinct().Count());
        foreach (var s in g.Ships)
        {
            Assert.True(Field.Distance(s.X, s.Y, Field.CX, Field.CY) > g.Base.Reach + 100);
            Assert.Equal(Heading(s.X, s.Y, Field.CX, Field.CY), s.Angle, 6);
        }
    }

    [Fact]
    public void In_a_team_shots_pass_through_team_mates()
    {
        var g = Calm(players: 2, mode: Mode.Team);
        Put(g, 0, 150, 650, -Math.PI / 2);
        var mate = Put(g, 1, 150, 450);
        g.Run(1, new Controls(Fire: true));
        g.Run(30);
        Assert.True(mate.Alive);
    }

    [Fact]
    public void In_versus_shots_kill_rivals_for_500()
    {
        var g = Calm(players: 2, mode: Mode.Versus);
        var me = Put(g, 0, 150, 650, -Math.PI / 2);
        var rival = Put(g, 1, 150, 450);
        g.Run(1, new Controls(Fire: true));
        g.Run(30);
        Assert.False(rival.Alive);
        Assert.Equal("player 1", rival.Cause);
        Assert.Equal(GameEngine.KillPoints, me.Score);
        Assert.Equal(1, me.Kills);
    }

    [Fact]
    public void Whoever_shoots_out_the_core_gets_the_bonus()
    {
        var g = Calm(players: 2, mode: Mode.Versus);
        Put(g, 0, 150, 150);
        var p2 = AtTheMouth(g, 1);
        g.Run(1, Nothing, new Controls(Fire: true));
        g.Run(30);
        Assert.Equal(1, g.Eliminator);
        Assert.Equal(GameEngine.CorePoints + GameEngine.BaseBonus(1), p2.Score);
        Assert.Equal(0, g.Ships[0].Score);
        Assert.Equal("PLAYER 2  BONUS " + GameEngine.BaseBonus(1), g.SubMessage);
    }

    [Fact]
    public void Ships_push_each_other_apart()
    {
        var g = Calm(players: 2, mode: Mode.Team);
        var a = Put(g, 0, 150, 650);
        var b = Put(g, 1, 158, 650);
        g.Run(2);
        Assert.True(Field.Distance(a.X, a.Y, b.X, b.Y) >= Ship.Radius * 2 - 0.5);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void Title_then_demonstrations_taking_turns_then_the_table()
    {
        var g = Game();
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        Assert.Equal(Mode.Solo, g.Mode);
        while (g.State == GameState.Attract) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal((Mode.Team, 4), (g.Mode, g.Ships.Count));
        while (g.State == GameState.Attract) g.Idle(1);
        g.Idle((int)((GameEngine.HighScoreSeconds + GameEngine.TitleIdleSeconds) * 60) + 4);
        Assert.Equal((Mode.Versus, 2), (g.Mode, g.Ships.Count));
    }

    [Fact]
    public void Start_plays_the_chosen_number_of_players_in_the_chosen_mode()
    {
        var g = Game();
        g.Tick([Nothing], start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((Mode.Solo, 1), (g.Mode, g.Ships.Count));
        g.ReturnToTitle();
        g.Choose(3);
        g.Tick([Nothing], start: false);
        g.Tick([Nothing], start: true);
        Assert.Equal((Mode.Team, 3), (g.Mode, g.Ships.Count));
        g.ReturnToTitle();
        g.ToggleMulti();
        g.Choose(9);
        Assert.Equal(4, g.ChosenPlayers);
        g.StartGame(2);
        Assert.Equal((Mode.Versus, 2), (g.Mode, g.Ships.Count));
        g.ReturnToTitle();
        g.StartGame(1);
        Assert.Equal(Mode.Solo, g.Mode);
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Game();
        g.Go();
        var s = g.Ships[0];
        s.Vx = 100;
        g.Pause();
        double x = s.X;
        g.Run(60);
        Assert.Equal(GameState.Paused, g.State);
        Assert.Equal(x, s.X);
        g.Resume();
        g.Run(1);
        Assert.NotEqual(x, s.X);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play(int seed)
        {
            var g = Game(seed);
            g.Go(wave: 3, players: 2, mode: Mode.Team);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Ships.Select(s => g.Pilot.Decide(g, s.Index)).ToArray());
            return string.Join(",", g.Ships.Select(s => $"{s.Score}/{s.X:0.000}/{s.Lives}")) + $" w{g.Wave} d{g.Drones.Count} b{g.Bolts.Count}";
        }
        Assert.Equal(Play(5), Play(5));
    }

    [Fact]
    public void The_computer_pilot_clears_the_first_waves()
    {
        var g = Game(3);
        g.Go();
        g.Pilot.Reset();
        for (int i = 0; i < 60 * 120 && g.Wave < 4 && g.State == GameState.Playing; i++) g.Tick([g.Pilot.Decide(g, 0)]);
        Assert.True(g.Wave >= 4, $"reached wave {g.Wave}");
        Assert.True(g.Ships[0].Score >= GameEngine.BaseBonus(1) + GameEngine.BaseBonus(2) + GameEngine.BaseBonus(3));
    }

    [Fact]
    public void A_team_of_computer_pilots_clears_a_turning_base()
    {
        var g = Game(4);
        g.Go(wave: 4, players: 4, mode: Mode.Team);
        g.Pilot.Reset();
        for (int i = 0; i < 60 * 120 && g.Wave < 5 && g.State == GameState.Playing; i++) g.Tick(g.Ships.Select(s => g.Pilot.Decide(g, s.Index)).ToArray());
        Assert.Equal(5, g.Wave);
    }

    [Fact]
    public void Every_sound_has_its_file()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        foreach (var s in Enum.GetValues<Sfx>().Where(s => s is not (Sfx.HumStop or Sfx.MusicStop)))
            Assert.True(File.Exists(Path.Combine(folder, s + ".wav")), $"{s}.wav");
    }

    [Fact]
    public void The_high_score_table_is_kept_between_games()
    {
        string folder = TempFolder();
        var t = HighScoreTable.Load(folder);
        t.Insert("ELI", 88888, 6);
        t.Save();
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("ELI", 88888, 6), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Assert.Equal("ELI", again.LastInitials);
    }
}
