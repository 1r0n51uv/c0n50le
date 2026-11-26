using System.Runtime.InteropServices;
using C0n50le.Core.Input;
using Windows.Gaming.Input;

namespace C0n50le.Services;

/// <summary>One poll of every controller: what is held, what was just pressed, and which family was used last.</summary>
public readonly record struct ControllerState(int Held, int Pressed, bool Connected, bool Active, PadFamily Family, string Name);

/// <summary>
/// Reads Xbox pads through XInput (their button bits are the same as <see cref="ControllerButtons"/>) and PlayStation
/// and Switch pads as raw game controllers, mapped by position. Works in the background too, so the return shortcut is
/// heard while a game is in front. Adapted from RON1N Mode.
/// </summary>
public sealed class ControllerReader
{
    private const int XInputMask = 0xF33F;
    private readonly Dictionary<string, int> _previous = [];
    private readonly Dictionary<string, (bool[] Buttons, GameControllerSwitchPosition[] Hat, double[] Axes)> _readings = [];
    private PadFamily _family = PadFamily.Xbox;
    private string _name = "";

    public ControllerState Poll()
    {
        var held = 0;
        var pressed = 0;
        var active = false;
        var seen = new HashSet<string>();
        var xbox = false;
        for (uint slot = 0; slot < 4; slot++)
        {
            if (XInputGetState(slot, out var input) != 0) continue;
            xbox = true;
            var state = (input.Buttons & XInputMask) | Stick(input.LeftX / 32768.0, input.LeftY / 32768.0);
            Read("xbox:" + slot, state, PadFamily.Xbox, "Xbox");
        }
        try
        {
            var pads = RawGameController.RawGameControllers;
            for (var index = 0; index < pads.Count; index++)
            {
                var pad = pads[index];
                try
                {
                    if (pad.HardwareVendorId == 0x045e && xbox) continue;
                    if (pad.HardwareVendorId is not (0x054c or 0x057e)) continue;
                    var id = pad.NonRoamableId;
                    if (!_readings.TryGetValue(id, out var raw))
                        _readings[id] = raw = (new bool[pad.ButtonCount], new GameControllerSwitchPosition[pad.SwitchCount], new double[pad.AxisCount]);
                    pad.GetCurrentReading(raw.Buttons, raw.Hat, raw.Axes);
                    if (raw.Buttons.Count(on => on) > 8) continue;
                    var sony = pad.HardwareVendorId == 0x054c;
                    var state = 0;
                    void Button(int button, int bit)
                    {
                        if (button < raw.Buttons.Length && raw.Buttons[button]) state |= bit;
                    }
                    Button(1, ControllerButtons.Confirm);
                    Button(sony ? 2 : 0, ControllerButtons.Back);
                    Button(sony ? 0 : 3, ControllerButtons.X);
                    Button(sony ? 3 : 2, ControllerButtons.Y);
                    Button(4, ControllerButtons.LeftShoulder);
                    Button(5, ControllerButtons.RightShoulder);
                    Button(8, ControllerButtons.View);
                    Button(9, ControllerButtons.Menu);
                    if (raw.Hat.Length > 0)
                        state |= raw.Hat[0] switch
                        {
                            GameControllerSwitchPosition.Up => ControllerButtons.Up,
                            GameControllerSwitchPosition.Down => ControllerButtons.Down,
                            GameControllerSwitchPosition.Left => ControllerButtons.Left,
                            GameControllerSwitchPosition.Right => ControllerButtons.Right,
                            GameControllerSwitchPosition.UpLeft => ControllerButtons.Up | ControllerButtons.Left,
                            GameControllerSwitchPosition.UpRight => ControllerButtons.Up | ControllerButtons.Right,
                            GameControllerSwitchPosition.DownLeft => ControllerButtons.Down | ControllerButtons.Left,
                            GameControllerSwitchPosition.DownRight => ControllerButtons.Down | ControllerButtons.Right,
                            _ => 0
                        };
                    if (raw.Axes.Length >= 2) state |= Stick(2 * raw.Axes[0] - 1, 1 - 2 * raw.Axes[1]);
                    Read(id, state, sony ? PadFamily.PlayStation : PadFamily.Nintendo, sony ? "PlayStation" : "Nintendo Switch");
                }
                catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException) { }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or InvalidCastException) { }

        foreach (var old in _previous.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _previous.Remove(old);
            _readings.Remove(old);
        }
        if (seen.Count == 0) _name = "";
        return new ControllerState(held, pressed, seen.Count > 0, active, _family, _name);

        void Read(string key, int state, PadFamily family, string name)
        {
            seen.Add(key);
            if (_name.Length == 0) { _family = family; _name = name; }
            if (_previous.TryGetValue(key, out var previous))
            {
                var fresh = state & ~previous;
                pressed |= fresh;
                if (fresh != 0)
                {
                    active = true;
                    _family = family;
                    _name = name;
                }
            }
            held |= state;
            _previous[key] = state;
        }
    }

    private static int Stick(double x, double y) => Math.Abs(x) > Math.Abs(y)
        ? x > .55 ? ControllerButtons.Right : x < -.55 ? ControllerButtons.Left : 0
        : y > .55 ? ControllerButtons.Up : y < -.55 ? ControllerButtons.Down : 0;

    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint slot, out XInputState input);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
}
