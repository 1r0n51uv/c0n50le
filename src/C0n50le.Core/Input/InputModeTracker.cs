namespace C0n50le.Core.Input;

/// <summary>How the home is being used right now. The layout follows it.</summary>
public enum InputMode
{
    /// <summary>Mouse and keyboard: buttons on screen, hover effects, the pointer visible.</summary>
    MouseKeyboard,
    /// <summary>A controller: focus highlighted, the button hints at the bottom, the pointer hidden.</summary>
    Controller
}

/// <summary>
/// Switches the input mode by itself: touching the controller (a button or the stick past its dead zone) moves to
/// Controller; a real mouse move, a click, the wheel or a key moves to MouseKeyboard. A mouse that only twitches a few
/// pixels (a desk bumped, the pointer being parked) doesn't count.
/// </summary>
public sealed class InputModeTracker
{
    /// <summary>The distance, in pixels, the mouse has to travel within <see cref="MouseWindow"/> to count.</summary>
    public const double MouseTravel = 24;
    public static readonly TimeSpan MouseWindow = TimeSpan.FromMilliseconds(400);

    private double _travel;
    private DateTimeOffset _travelStart;
    private (double X, double Y)? _lastPoint;

    public InputModeTracker(InputMode initial = InputMode.MouseKeyboard) => Mode = initial;

    public InputMode Mode { get; private set; }

    public event Action<InputMode>? Changed;

    /// <summary>Any controller activity: a button or a stick past the dead zone.</summary>
    public void ControllerUsed() => Set(InputMode.Controller);

    public void KeyPressed() => Set(InputMode.MouseKeyboard);

    public void MouseClicked() => Set(InputMode.MouseKeyboard);

    public void MouseWheel() => Set(InputMode.MouseKeyboard);

    public void MouseMoved(double x, double y, DateTimeOffset now)
    {
        if (_lastPoint is not { } last)
        {
            _lastPoint = (x, y);
            _travelStart = now;
            _travel = 0;
            return;
        }
        _lastPoint = (x, y);
        if (now - _travelStart > MouseWindow)
        {
            _travelStart = now;
            _travel = 0;
        }
        _travel += Math.Sqrt((x - last.X) * (x - last.X) + (y - last.Y) * (y - last.Y));
        if (_travel >= MouseTravel)
        {
            _travel = 0;
            Set(InputMode.MouseKeyboard);
        }
    }

    private void Set(InputMode mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        _travel = 0;
        Changed?.Invoke(mode);
    }
}

/// <summary>
/// Turns held directions into repeated moves, like a console menu: one move on the press, then after a pause a move
/// every <see cref="Interval"/> while it stays held.
/// </summary>
public sealed class RepeatGate
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(420);
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(120);

    private int _held;
    private DateTimeOffset _next;

    /// <summary>The directions to move on this poll, given what is held now.</summary>
    public int Moves(int held, DateTimeOffset now)
    {
        held &= ControllerButtons.Directions;
        if (held == 0)
        {
            _held = 0;
            return 0;
        }
        if (held != _held)
        {
            var fresh = held & ~_held;
            _held = held;
            _next = now + Delay;
            return fresh != 0 ? fresh : held;
        }
        if (now < _next) return 0;
        _next = now + Interval;
        return held;
    }
}
