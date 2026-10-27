using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVAEliminator.Engine;

namespace AVAEliminator.Views;

/// <summary>
/// The arcade screen: a fixed 1024×768 picture scaled to fit the window: the arena with the base in the middle and
/// the players' scores along the top. Runs the engine from a 60 Hz timer.
/// </summary>
public sealed partial class GameCanvas : Control
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;

    private readonly DispatcherTimer? _timer;
    private DateTime _last = DateTime.Now;
    private bool _padPausePrev, _padBackPrev, _padUpPrev, _padDownPrev, _padLeftPrev, _padRightPrev, _padAcceptPrev, _padDonePrev, _padModePrev;
    private double _clock;
    /// <summary>A note on the screen for a moment: a setting just changed.</summary>
    private string? _toast;
    private double _toastTime;

    /// <summary>The base turning slowly behind the title and the tables.</summary>
    private readonly Fortress _show = new(5) { Spin = 0.08 };

    public GameEngine Engine { get; }
    public SoundManager Sound { get; }
    public InputHandler Input { get; } = new();

    public GameCanvas(bool silent = false) : this(new GameEngine(Environment.TickCount), new SoundManager(silent), startTimer: true) { }

    /// <summary>For tests: an engine of your own, and no timer (call <see cref="Tick"/>).</summary>
    public GameCanvas(GameEngine engine, SoundManager sound, bool startTimer)
    {
        Engine = engine;
        Sound = sound;
        Engine.Sound += Sound.Play;
        Focusable = true;
        ClipToBounds = true;
        if (startTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer?.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        Sound.Dispose();
        Input.Dispose();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Input.Clear();
    }

    /// <summary>One frame: read the controls, run the game, redraw.</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        Input.Update();
        HandlePad();
        bool inGame = Engine.State is GameState.Playing or GameState.Paused;
        Engine.Update(dt, Input.Players(inGame ? Engine.Ships.Count : 1), Input.Start);
        Cursor = Engine.State == GameState.Playing ? new Cursor(StandardCursorType.None) : null;
        Advance(dt);
    }

    /// <summary>For tests: moves the drawing clock (and the title's base) without running the game.</summary>
    public void Advance(double dt)
    {
        _clock += dt;
        _toastTime -= dt;
        _show.Angle += _show.Spin * dt;
        InvalidateVisual();
    }

    private void Toast(string text)
    {
        _toast = text;
        _toastTime = 2;
    }

    /// <summary>On the title, the attract mode and the tables: the number of players and the mode can be changed.</summary>
    private bool Choosing => Engine.State is GameState.Title or GameState.Attract or GameState.HighScores;

    private void ChoosePlayers(int n)
    {
        Engine.Choose(n);
        Toast(Describe());
    }

    private void ToggleMode()
    {
        Engine.ToggleMulti();
        Toast(Describe());
    }

    private string Describe() => Engine.ChosenPlayers == 1
        ? "1 PLAYER AGAINST THE DRONES"
        : $"{Engine.ChosenPlayers} PLAYERS: " + (Engine.ChosenMulti == Mode.Team ? "TEAM - TOGETHER AGAINST THE BASE" : "VERSUS - SHOTS KILL");

    private void HandlePad()
    {
        bool Edge(bool now, ref bool prev) { bool e = now && !prev; prev = now; return e; }
        if (Edge(Input.PadPause, ref _padPausePrev))
        {
            if (Engine.State == GameState.Playing) Engine.Pause();
            else if (Engine.State == GameState.Paused) Engine.Resume();
        }
        if (Edge(Input.PadBack, ref _padBackPrev))
        {
            if (Engine.State == GameState.Paused) (TopLevel.GetTopLevel(this) as Window)?.Close();
            else if (Engine.State == GameState.Playing) Engine.Pause();
        }
        bool up = Edge(Input.PadUp, ref _padUpPrev), down = Edge(Input.PadDown, ref _padDownPrev);
        bool left = Edge(Input.PadLeft, ref _padLeftPrev), right = Edge(Input.PadRight, ref _padRightPrev);
        bool accept = Edge(Input.PadAccept, ref _padAcceptPrev), done = Edge(Input.PadDone, ref _padDonePrev);
        bool mode = Edge(Input.PadMode, ref _padModePrev);
        if (Choosing)
        {
            if (left) ChoosePlayers(Engine.ChosenPlayers - 1);
            if (right) ChoosePlayers(Engine.ChosenPlayers + 1);
            if (up || down || mode) ToggleMode();
        }
        if (Engine.State == GameState.EnterInitials && Engine.Initials is { } entry)
        {
            if (up) entry.Cycle(1);
            if (down) entry.Cycle(-1);
            if (left) entry.Back();
            if (right) entry.Move(1);
            if (accept) entry.Advance();
            if (done) entry.Finish();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            SaveScreenshot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"AVAEliminator_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"));
            return;
        }
        // While entering initials every key belongs to the entry.
        if (Engine.State == GameState.EnterInitials && Engine.Initials is { } entry)
        {
            InitialsKey(entry, e.Key);
            return;
        }
        if (Choosing && ChooseKey(e.Key)) return;
        switch (e.Key)
        {
            case Key.M:
                Sound.IsMuted = !Sound.IsMuted;
                Toast(Sound.IsMuted ? "SOUND OFF" : "SOUND ON");
                return;
            case Key.Escape:
                if (Engine.State == GameState.Playing) Engine.Pause();
                else if (Engine.State is GameState.Attract or GameState.HighScores) Engine.ReturnToTitle();
                else (TopLevel.GetTopLevel(this) as Window)?.Close();
                return;
            case Key.P:
                if (Engine.State == GameState.Playing) { Engine.Pause(); return; }
                break;
        }
        if (Engine.State == GameState.Paused)
        {
            Engine.Resume();
            return;
        }
        Input.OnKeyDown(e.Key, e.PhysicalKey);
    }

    /// <summary>The title's keys: 1 to 4 start a game for that many; the arrows pick the number and the mode.</summary>
    private bool ChooseKey(Key key)
    {
        switch (key)
        {
            case >= Key.D1 and <= Key.D4:
                Engine.StartGame(key - Key.D1 + 1);
                return true;
            case >= Key.NumPad1 and <= Key.NumPad4:
                Engine.StartGame(key - Key.NumPad1 + 1);
                return true;
            case Key.Left: ChoosePlayers(Engine.ChosenPlayers - 1); return true;
            case Key.Right: ChoosePlayers(Engine.ChosenPlayers + 1); return true;
            case Key.Up or Key.Down or Key.V or Key.T: ToggleMode(); return true;
        }
        return false;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Input.OnKeyUp(e.Key, e.PhysicalKey);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) => Focus();

    private static void InitialsKey(InitialsEntry entry, Key key)
    {
        switch (key)
        {
            case Key.Up: entry.Cycle(1); break;
            case Key.Down: entry.Cycle(-1); break;
            case Key.Left or Key.Back: entry.Back(); break;
            case Key.Right: entry.Move(1); break;
            case Key.Space: entry.Advance(); break;
            case Key.Enter: entry.Finish(); break;
            case Key.OemPeriod or Key.Decimal: entry.Type('.'); break;
            case >= Key.A and <= Key.Z: entry.Type((char)('A' + (key - Key.A))); break;
            case >= Key.D0 and <= Key.D9: entry.Type((char)('0' + (key - Key.D0))); break;
            case >= Key.NumPad0 and <= Key.NumPad9: entry.Type((char)('0' + (key - Key.NumPad0))); break;
        }
    }

    /// <summary>Saves what's on screen as a PNG.</summary>
    public void SaveScreenshot(string path)
    {
        var size = new PixelSize((int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>How the fixed screen fits the window: scale and letterbox offsets.</summary>
    private (double Scale, double X, double Y) Fit()
    {
        double s = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        return (s, (Bounds.Width - ScreenWidth * s) / 2, (Bounds.Height - ScreenHeight * s) / 2);
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var (s, ox, oy) = Fit();
        if (s <= 0) return;
        using (dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy)))
        using (dc.PushClip(new Rect(0, 0, ScreenWidth, ScreenHeight)))
            DrawScreen(dc);
    }
}
