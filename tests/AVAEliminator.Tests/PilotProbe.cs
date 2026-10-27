using AVAEliminator.Engine;

namespace AVAEliminator.Tests;

/// <summary>
/// How the computer pilot gets on (set ELIMINATOR_PROBE to run it, for tuning: "1" for one player from the first
/// wave, or "1:5" to start at wave 5; "4" or "4:3" for four as a team, "2v" for two against each other).
/// ELIMINATOR_SEEDS picks the games.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        string? probe = Environment.GetEnvironmentVariable("ELIMINATOR_PROBE");
        if (probe is null) return;
        var parts = probe.Split(':');
        bool versus = parts[0].EndsWith('v');
        int players = int.Parse(parts[0].TrimEnd('v'));
        int from = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        var seeds = (Environment.GetEnvironmentVariable("ELIMINATOR_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse);
        foreach (int seed in seeds)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Harness.TempFolder())) { Silent = true };
            g.Go(from, players, players == 1 ? Mode.Solo : versus ? Mode.Versus : Mode.Team);
            g.Pilot.Reset();
            var deaths = new List<string>();
            var waves = new List<string>();
            var alive = g.Ships.Select(s => s.Alive).ToArray();
            int t = 0, wave = g.Wave;
            double waveAt = 0;
            var doing = new Dictionary<string, int>();
            bool trace = Environment.GetEnvironmentVariable("ELIMINATOR_TRACE") != null;
            for (; t < 60 * 600 && g.State == GameState.Playing; t++)
            {
                var c = g.Ships.Select(s => g.Pilot.Decide(g, s.Index)).ToArray();
                g.Tick(c);
                foreach (var s in g.Ships.Where(s => s.Alive)) doing[g.Pilot.Doing[s.Index]] = doing.GetValueOrDefault(g.Pilot.Doing[s.Index]) + 1;
                if (trace && t % 120 == 0 && g.Ships[0].Alive)
                {
                    var s0 = g.Ships[0];
                    var (lx, ly) = g.Base.ToLocal(s0.X, s0.Y);
                    output.WriteLine($"  t{t / 60} w{g.Wave} {g.Pilot.Doing[0]} at ({s0.X:0},{s0.Y:0}) local ({lx:0},{ly:0}) v ({s0.Vx:0},{s0.Vy:0}) ch {g.Base.ChannelAt(lx, ly)} core {g.Base.CoreHp}");
                }
                foreach (var s in g.Ships)
                {
                    if (alive[s.Index] && !s.Alive) deaths.Add($"{g.Wave}:{s.Cause}({g.Pilot.Doing[s.Index]})");
                    alive[s.Index] = s.Alive;
                }
                if (g.Wave != wave) { waves.Add($"{t / 60.0 - waveAt:0}s[{string.Join(",", doing.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value / 60}"))}]"); doing.Clear(); waveAt = t / 60.0; wave = g.Wave; }
            }
            output.WriteLine($"seed {seed}: {g.State}/{g.Phase} at wave {g.Wave} after {t / 60}s, scores {string.Join(",", g.Ships.Select(s => s.Score))}, waves {string.Join(" ", waves)}");
            output.WriteLine($"   deaths [{string.Join(" ", deaths)}]");
            output.WriteLine($"   now: {string.Join(" | ", g.Ships.Select(s => g.Pilot.Doing[s.Index]))}");
        }
    }
}
