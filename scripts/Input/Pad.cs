using System.Numerics;
using System.Runtime.InteropServices;

namespace Trinity.Batwing;

[Flags]
internal enum PadButton : ushort
{
    None = 0,
    Up = 0x0001,
    Down = 0x0002,
    Left = 0x0004,
    Right = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LS = 0x0040,
    RS = 0x0080,
    LB = 0x0100,
    RB = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

// The controller, read through XInput once a draw. The game reads its pads through the same library.
internal static class Pad
{
    private const short StickNoise = 9000;
    private const byte TriggerNoise = 40;
    private const double ScanSeconds = 0.5;
    private const float MenuPush = 0.5f;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput9_1_0.dll")]
    private static extern int XInputGetState(int userIndex, out XInputState state);

    private static bool _missing;
    private static int _index = -1;
    private static int _scanSlot;
    private static Every _scan;
    private static XInputGamepad _now, _was;
    private static ushort _menuNow, _menuWas;

    public static bool Connected => _index >= 0 && Opt.Controller.On;

    public static PadButton Held => Connected ? (PadButton)_now.Buttons : PadButton.None;

    public static bool Down(PadButton b) => b != PadButton.None && (Held & b) == b;

    public static bool Pressed(PadButton b) => Connected && b != PadButton.None && ((PadButton)_now.Buttons & b) == b && ((PadButton)_was.Buttons & b) != b;

    // Buttons with the sticks counted as a d-pad, for moving round menus.
    public static bool MenuPressed(PadButton b) => Connected && (_menuNow & (ushort)b) == (ushort)b && (_menuWas & (ushort)b) != (ushort)b;

    public static bool MenuDown(PadButton b) => Connected && (_menuNow & (ushort)b) == (ushort)b;

    // Raw sticks, -1 to 1 with no deadzone. Controls shapes them.
    public static Vector2 LeftRaw => Connected ? new Vector2(_now.ThumbLX / 32767f, _now.ThumbLY / 32767f) : Vector2.Zero;

    public static Vector2 RightRaw => Connected ? new Vector2(_now.ThumbRX / 32767f, _now.ThumbRY / 32767f) : Vector2.Zero;

    public static float LeftTrigger => Connected ? Trigger(_now.LeftTrigger) : 0f;

    public static float RightTrigger => Connected ? Trigger(_now.RightTrigger) : 0f;

    // Something on the pad is being used right now.
    public static bool Busy { get; private set; }

    private static float Trigger(byte v) => v <= TriggerNoise ? 0f : Math.Min(1f, (v - TriggerNoise) / (255f - TriggerNoise));

    public static void Poll()
    {
        _was = _now;
        _menuWas = _menuNow;
        if (!Opt.Controller.On)
        {
            Quiet();
            return;
        }

        if (_index >= 0)
        {
            if (Read(_index, out XInputState state))
            {
                Take(state.Gamepad);
                return;
            }

            Log.Info("Pad: controller " + _index + " disconnected.");
            _index = -1;
            _scanSlot = 0;
            _scan.Force();
            Quiet();
            Toasts.Show("CONTROLLER DISCONNECTED", false);
        }

        // Asking about an empty slot is slow, so it's one slot each half second. The game looks the same way.
        if (!_scan.Elapsed(ScanSeconds))
            return;

        int slot = _scanSlot;
        _scanSlot = (_scanSlot + 1) % 4;
        if (!Read(slot, out XInputState found))
            return;

        _index = slot;
        Take(found.Gamepad);

        // Whatever is held as it connects isn't a press.
        _was = _now;
        _menuWas = _menuNow;
        Log.Info("Pad: controller " + slot + " connected.");
    }

    private static bool Read(int index, out XInputState state)
    {
        state = default;
        if (_missing)
            return false;

        try
        {
            return XInputGetState(index, out state) == 0;
        }
        catch (Exception ex)
        {
            _missing = true;
            Log.Info("Pad: XInput isn't available, controller support is off: " + ex.Message);
            return false;
        }
    }

    private static void Take(XInputGamepad g)
    {
        // XInput answers for the whole desktop. The game only reads the pad while its own window has the focus.
        if (!Native.GameInFront)
        {
            Quiet();
            return;
        }

        _now = g;
        ushort menu = g.Buttons;
        if (g.ThumbLY > 32767 * MenuPush)
            menu |= (ushort)PadButton.Up;
        else if (g.ThumbLY < -32767 * MenuPush)
            menu |= (ushort)PadButton.Down;
        if (g.ThumbLX < -32767 * MenuPush)
            menu |= (ushort)PadButton.Left;
        else if (g.ThumbLX > 32767 * MenuPush)
            menu |= (ushort)PadButton.Right;
        _menuNow = menu;

        // Anything held counts on every poll, not just when the state changes. A stick held still at full tilt
        // sends no new packets.
        Busy = g.Buttons != 0 || g.LeftTrigger > TriggerNoise || g.RightTrigger > TriggerNoise
            || Math.Abs((int)g.ThumbLX) > StickNoise || Math.Abs((int)g.ThumbLY) > StickNoise
            || Math.Abs((int)g.ThumbRX) > StickNoise || Math.Abs((int)g.ThumbRY) > StickNoise;
    }

    private static void Quiet()
    {
        _now = default;
        _menuNow = 0;
        Busy = false;
    }

    // Every button newly down this poll, one at a time.
    public static IEnumerable<PadButton> NewPresses()
    {
        if (!Connected)
            yield break;

        ushort fresh = (ushort)(_now.Buttons & ~_was.Buttons);
        for (int bit = 0; bit < 16 && fresh != 0; bit++)
        {
            ushort mask = (ushort)(1 << bit);
            if ((fresh & mask) != 0)
                yield return (PadButton)mask;
        }
    }
}
