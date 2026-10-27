using Avalonia.Input;

namespace AVAEliminator.Engine;

/// <summary>
/// The keyboard and up to four gamepads boiled down to each player's controls: turn, thrust and fire. With one
/// person playing every key works for them. With two or more the keyboard is split: player 1 has the arrows and
/// Right Ctrl, Right Shift, / or NumPad 0 to fire; player 2 has A and D to turn, W to thrust, and Left Ctrl, Left
/// Shift or F to fire. Gamepads go to players 1 to 4 in turn.
/// </summary>
public sealed class InputHandler : IDisposable
{
    public const int Pads = GameEngine.MaxPlayers;

    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager[] _pads = Enumerable.Range(0, Pads).Select(i => new ControllerManager(i)).ToArray();

    /// <summary>
    /// A key goes down. The key it was pressed as is kept against the physical key, because the release can be named
    /// differently: with the keyboard option shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock,
    /// and a Shift that was never let go would keep its player firing for good.
    /// </summary>
    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up: the key it was pressed as is let go too, whatever the release is called.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    /// <summary>Let go of everything (the window lost the focus).</summary>
    public void Clear()
    {
        _held.Clear();
        _pressedAs.Clear();
    }

    public void Update()
    {
        ControllerManager.PollEvents();
        foreach (var p in _pads) p.Update();
    }

    private static readonly Key[] ArrowLeft = [Key.Left, Key.NumPad4], ArrowRight = [Key.Right, Key.NumPad6], ArrowThrust = [Key.Up, Key.NumPad8];
    private static readonly Key[] ArrowFire = [Key.RightCtrl, Key.RightShift, Key.OemQuestion, Key.NumPad0];
    private static readonly Key[] WasdLeft = [Key.A], WasdRight = [Key.D], WasdThrust = [Key.W];
    private static readonly Key[] WasdFire = [Key.LeftCtrl, Key.LeftShift, Key.F];
    /// <summary>Fire for one player alone: Space and Z too.</summary>
    private static readonly Key[] SoloFire = [Key.Space, Key.Z];

    private Controls Keys(Key[] left, Key[] right, Key[] thrust, Key[] fire) =>
        new((Any(right) ? 1 : 0) - (Any(left) ? 1 : 0), Any(thrust), Any(fire));

    private Controls Arrows => Keys(ArrowLeft, ArrowRight, ArrowThrust, ArrowFire);
    private Controls Wasd => Keys(WasdLeft, WasdRight, WasdThrust, WasdFire);

    private Controls Pad(int i)
    {
        var p = _pads[i];
        int turn = p.DPadLeft || p.LeftStickX < -0.4f ? -1 : p.DPadRight || p.LeftStickX > 0.4f ? 1 : 0;
        bool thrust = p.DPadUp || p.LeftStickY < -0.5f || p.ButtonB || p.LeftTrigger > 0.5f || p.LeftBumper;
        bool fire = p.ButtonA || p.ButtonX || p.RightTrigger > 0.5f || p.RightBumper;
        return new Controls(turn, thrust, fire);
    }

    /// <summary>Player <paramref name="player"/>'s controls (0 to 3), with <paramref name="humans"/> people playing.</summary>
    public Controls Player(int player, int humans)
    {
        var pad = Pad(player);
        // Keys first, so a half-pushed stick can't override them; the pad fills in what the keys leave.
        return player switch
        {
            0 when humans <= 1 => Arrows | Wasd | new Controls(0, false, Any(SoloFire)) | pad,
            0 => Arrows | pad,
            1 => Wasd | pad,
            _ => pad,
        };
    }

    /// <summary>Every player's controls, in order.</summary>
    public Controls[] Players(int humans)
    {
        int n = Math.Clamp(humans, 1, Pads);
        var all = new Controls[n];
        for (int i = 0; i < n; i++) all[i] = Player(i, humans);
        return all;
    }

    /// <summary>Enter (or Start on any pad).</summary>
    public bool Start => Any(Key.Enter) || _pads.Any(p => p.ButtonStart);

    /// <summary>How many gamepads are plugged in.</summary>
    public int PadsConnected => _pads.Count(p => p.IsConnected);

    public bool PadUp => _pads.Any(p => p.DPadUp || p.LeftStickY < -0.5f);
    public bool PadDown => _pads.Any(p => p.DPadDown || p.LeftStickY > 0.5f);
    public bool PadLeft => _pads.Any(p => p.DPadLeft || p.LeftStickX < -0.5f);
    public bool PadRight => _pads.Any(p => p.DPadRight || p.LeftStickX > 0.5f);
    public bool PadAccept => _pads.Any(p => p.ButtonA || p.ButtonX || p.ButtonB);
    public bool PadDone => _pads.Any(p => p.ButtonStart);
    public bool PadPause => _pads.Any(p => p.ButtonStart);
    public bool PadBack => _pads.Any(p => p.ButtonBack);
    /// <summary>Y on any pad: team or versus, on the title.</summary>
    public bool PadMode => _pads.Any(p => p.ButtonY);

    public void Dispose()
    {
        foreach (var p in _pads) p.Dispose();
    }
}
