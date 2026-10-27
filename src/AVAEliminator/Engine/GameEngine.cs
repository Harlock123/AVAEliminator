namespace AVAEliminator.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>Where a wave has got to.</summary>
public enum Phase
{
    /// <summary>The wave's number, while the new base powers up: it doesn't fire yet, and no drones come.</summary>
    Start,
    /// <summary>The fight.</summary>
    Play,
    /// <summary>The base eliminated: its walls flying apart, the bonus, then the next wave.</summary>
    Clear,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, Ricochet, Thrust, Clank, Bolt, BoltDown, DroneFire, DroneDown, ShipDown, CoreHit, BaseBlast, WaveStart, ExtraLife,
    /// <summary>The base's hum, looping while it's alive.</summary>
    Hum, HumStop,
    TitleTune, StartTune, GameOver, MusicStop,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and each player's controls; it runs
/// in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always plays the same game.
/// </summary>
public sealed partial class GameEngine
{
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;
    public const double GameOverSeconds = 4;
    public const int MaxPlayers = 4;

    private Random _rng;
    private readonly int _seed;
    private double _accumulator;
    private bool _startPrev, _titleTunePending = true, _hum;
    private int _demos;
    /// <summary>The players still to enter their initials, best first.</summary>
    private readonly Queue<Ship> _toEnter = new();
    private int _lastPlace = -1;

    public GameEngine(int seed = 1981, HighScoreTable? table = null)
    {
        _seed = seed;
        _rng = new Random(seed);
        HighScores = table ?? HighScoreTable.Load();
        NewGame(1, Mode.Solo);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    /// <summary>The player entering initials (0 to 3).</summary>
    public int EntryPlayer { get; private set; } = -1;
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }

    /// <summary>How many will play the next game (chosen on the title), and whether two or more play as a team or against each other.</summary>
    public int ChosenPlayers { get; private set; } = 1;
    public Mode ChosenMulti { get; private set; } = Mode.Team;

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        // The demonstration plays in silence, as most arcade machines' did.
        if (!Silent && State != GameState.Attract) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    /// <summary>Picks how many will play (1 to 4).</summary>
    public void Choose(int players) => ChosenPlayers = Math.Clamp(players, 1, MaxPlayers);

    /// <summary>Two or more players: together against the base, or against each other too.</summary>
    public void ToggleMulti() => ChosenMulti = ChosenMulti == Mode.Team ? Mode.Versus : Mode.Team;

    /// <summary>The mode a game for so many players is played in.</summary>
    public Mode ModeFor(int players) => players <= 1 ? Mode.Solo : ChosenMulti;

    public void Update(double dt, IReadOnlyList<Controls> controls, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(controls, start);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(IReadOnlyList<Controls> controls, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                if (startPressed) StartGame(ChosenPlayers);
                else if (StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Ships.Select(s => Pilot.Decide(this, s.Index)).ToArray());
                if (startPressed) { StartGame(ChosenPlayers); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, controls);
                break;
            case GameState.GameOver:
                if (StateTime > GameOverSeconds)
                {
                    _toEnter.Clear();
                    foreach (var s in Ships.OrderByDescending(s => s.Score)) _toEnter.Enqueue(s);
                    _lastPlace = -1;
                    NextEntry();
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    var ship = Ships[EntryPlayer];
                    _lastPlace = HighScores.Insert(done.Initials, ship.Score, Wave);
                    HighScores.Save();
                    NextEntry();
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame(ChosenPlayers);
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>The next player with a score good enough for the table enters initials; with none left, the table.</summary>
    private void NextEntry()
    {
        while (_toEnter.Count > 0)
        {
            var s = _toEnter.Dequeue();
            if (!HighScores.Qualifies(s.Score)) continue;
            State = GameState.EnterInitials;
            StateTime = 0;
            EntryPlayer = s.Index;
            Initials = new InitialsEntry(HighScores.LastInitials);
            return;
        }
        EntryPlayer = -1;
        Initials = null;
        ShowHighScores(_lastPlace);
    }

    /// <summary>A new game for one to four players (two or more in the mode chosen on the title).</summary>
    public void StartGame(int players, Mode? mode = null)
    {
        players = Math.Clamp(players, 1, MaxPlayers);
        ChosenPlayers = players;
        NewGame(players, mode ?? ModeFor(players));
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Play(Sfx.MusicStop);
        Play(Sfx.StartTune);
        BeginWave(1);
    }

    /// <summary>The demonstration takes turns: one ship against the drones, a team of four, then two against each other.</summary>
    private void StartAttract()
    {
        Play(Sfx.MusicStop);
        var (players, mode) = (_demos++ % 3) switch { 0 => (1, Mode.Solo), 1 => (4, Mode.Team), _ => (2, Mode.Versus) };
        NewGame(players, mode);
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
        BeginWave(1 + _rng.Next(3));
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _titleTunePending = true;
        Hum(false);
        Play(Sfx.MusicStop);
        Effects.Clear();
        Message = SubMessage = null;
        NewGame(1, Mode.Solo);
    }

    private void ShowHighScores(int place)
    {
        Hum(false);
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Hum(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Hum(true);
    }

    private void GameOver()
    {
        Hum(false);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Hum(bool on)
    {
        if (on == _hum) return;
        _hum = on;
        if (on && State == GameState.Attract) { _hum = false; return; }
        Play(on ? Sfx.Hum : Sfx.HumStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}
