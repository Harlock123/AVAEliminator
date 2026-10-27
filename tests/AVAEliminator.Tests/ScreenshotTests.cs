using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAEliminator.Engine;
using AVAEliminator.Views;

namespace AVAEliminator.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set ELIMINATOR_SHOTS to a folder to
/// save the README's four there as PNGs (and ELIMINATOR_ALL_SHOTS as well for every one).
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Harness.TempFolder())) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The pictures kept in Images for the README; the rest are drawn (to see that they draw) but not saved.</summary>
    private static readonly string[] Kept = ["team", "channel", "title", "eliminated"];

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("ELIMINATOR_SHOTS");
        if (string.IsNullOrEmpty(dir) || !(Kept.Contains(name) || Environment.GetEnvironmentVariable("ELIMINATOR_ALL_SHOTS") != null)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAEliminator-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilots play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            // Kept alive for the pictures: they're of the game, not of the pilots' luck.
            foreach (var s in g.Ships) s.Lives = Math.Max(s.Lives, 3);
            g.Tick(g.Ships.Select(s => g.Pilot.Decide(g, s.Index)).ToArray());
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Play(string name, int wave, int players, Mode mode, Func<GameEngine, bool> moment, int seconds = 150)
    {
        var (w, c, g) = Open();
        g.Go(wave, players, mode);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static bool InChannel(GameEngine g, Ship s)
    {
        var (lx, ly) = g.Base.ToLocal(s.X, s.Y);
        return s.Alive && g.Base.ChannelAt(lx, ly) > 1;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        for (int i = 0; i < 60 * 3; i++) c.Advance(1 / 60.0);
        Shot(w, c, "title");
        g.Choose(4);
        g.ToggleMulti();
        Shot(w, c, "title-versus");
        g.HighScores.Insert("ACE", 61250, 9);
        g.HighScores.Insert("AVA", 48800, 7);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 60 && g.State != GameState.HighScores; i++) g.Idle(1);
        c.Advance(1);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void A_wave_starting()
    {
        var (w, c, g) = Open();
        g.StartGame(1);
        g.Idle(110);
        c.Advance(0.7);
        Shot(w, c, "get-ready");
    }

    [AvaloniaFact]
    public void Into_the_channel() => Play("channel", 5, 1, Mode.Solo,
        g => g.Phase == Phase.Play && InChannel(g, g.Ships[0]) && g.Base.ChannelAt(g.Base.ToLocal(g.Ships[0].X, g.Ships[0].Y).X, g.Base.ToLocal(g.Ships[0].X, g.Ships[0].Y).Y) > 5
             && g.Bolts.Count >= 1 && g.Drones.Count >= 1 && g.Bullets.Any(b => !b.Hostile));

    [AvaloniaFact]
    public void A_team_of_four() => Play("team", 4, 4, Mode.Team,
        g => g.Phase == Phase.Play && g.Ships.All(s => s.Alive && s.Safe <= 0) && g.Ships.Count(s => InChannel(g, s)) >= 1 && g.Bolts.Count >= 1 && g.Drones.Count >= 1 && g.Bullets.Count >= 4);

    [AvaloniaFact]
    public void Versus() => Play("versus", 3, 2, Mode.Versus,
        g => g.Phase == Phase.Play && g.Ships.All(s => s.Alive && s.Safe <= 0) && g.Bolts.Count >= 1 && g.Bullets.Count(b => !b.Hostile) >= 3 && g.PhaseTime > 3);

    [AvaloniaFact]
    public void The_base_eliminated() => Play("eliminated", 4, 1, Mode.Solo, g => g.Phase == Phase.Clear && g.PhaseTime > 0.35, 200);
}
