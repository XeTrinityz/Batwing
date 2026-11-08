using System.Runtime.InteropServices;
using BmSDK;
using BmSDK.Engine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Trinity.Batwing;

internal static class Pad
{
    private const ushort Up = 0x0001;
    private const ushort Down = 0x0002;
    private const ushort Left = 0x0004;
    private const ushort Right = 0x0008;
    private const ushort Start = 0x0010;
    private const ushort Back = 0x0020;
    private const ushort LS = 0x0040;
    private const ushort RS = 0x0080;
    private const ushort LB = 0x0100;
    private const ushort RB = 0x0200;
    private const ushort A = 0x1000;
    private const ushort B = 0x2000;
    private const ushort X = 0x4000;
    private const ushort Y = 0x8000;

    private const short StickPush = 16000;
    private const short StickNoise = 9000;
    private const byte TriggerNoise = 40;
    private const double ScanSeconds = 2.0;
    private const int RescanFrames = 30;
    private const int FlightRescanFrames = 5;
    private const float OpenHold = 0.35f;

    private static readonly Dictionary<string, ushort> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = A, ["b"] = B, ["x"] = X, ["y"] = Y,
        ["lb"] = LB, ["rb"] = RB, ["ls"] = LS, ["rs"] = RS,
        ["back"] = Back, ["start"] = Start,
        ["up"] = Up, ["down"] = Down, ["left"] = Left, ["right"] = Right,
    };

    private static readonly ushort[] Dirs = { Up, Down, Left, Right };
    private static readonly Keys[] DirKeys = { Keys.Up, Keys.Down, Keys.Left, Keys.Right };

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

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState14(int userIndex, out XInputState state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState910(int userIndex, out XInputState state);

    public static bool Enabled = true;

    public static bool HoldButtons;

    private static string _openText = "";
    private static ushort _openMask;
    private static bool _legacy;
    private static bool _missing;
    private static int _index = -1;
    private static double _nextScan;
    private static uint _packet;
    private static ushort _now, _prev, _raw, _rawPrev;
    private static short _lx, _ly, _rx, _ry;
    private static byte _lt, _rt;
    private static FrameTimer _frame;
    private static long _padUsedAt, _keyUsedAt;
    private static float _held = -1f;
    private static bool _waitRelease;
    private static readonly float[] _repeat = { -1f, -1f, -1f, -1f };

    private static nint _blockedInput, _repairedInput, _buttonsInput;
    private static int _blockFrames, _buttonsFrames;
    private static bool _restorePending;

    private static bool Connected => _index >= 0;

    public static bool InUse => Connected && Enabled && _padUsedAt > _keyUsedAt;

    public static void KeyboardUsed() => _keyUsedAt = Stopwatch.GetTimestamp();

    public static System.Numerics.Vector2 LeftStick => Connected && Enabled ? new(Axis(_lx), Axis(_ly)) : default;

    public static System.Numerics.Vector2 RightStick => Connected && Enabled ? Round(_rx, _ry) : default;

    // Round deadzone so diagonals aim smoothly. The per axis one feels notchy for steering.
    private static System.Numerics.Vector2 Round(short x, short y)
    {
        const float dead = 0.2f;
        var v = new System.Numerics.Vector2(x / 32767f, y / 32767f);
        float size = v.Length();
        if (size <= dead)
            return default;

        float scaled = Math.Min(1f, (size - dead) / (1f - dead));
        return v / size * scaled;
    }

    public static float LeftTrigger => Connected && Enabled ? Trigger(_lt) : 0f;

    public static float RightTrigger => Connected && Enabled ? Trigger(_rt) : 0f;

    public static bool Button(string name) => Connected && Enabled && Names.TryGetValue(name, out ushort mask) && RawHeld(mask);

    private static float Axis(short v)
    {
        const float dead = 7849f, full = 32767f;
        float a = Math.Abs((float)v);
        if (a <= dead)
            return 0f;
        return MathF.Sign(v) * Math.Min(1f, (a - dead) / (full - dead));
    }

    private static float Trigger(byte v) => v <= TriggerNoise ? 0f : Math.Min(1f, (v - TriggerNoise) / (255f - TriggerNoise));

    private static bool Held(ushort mask) => mask != 0 && (_now & mask) == mask;

    private static bool Pressed(ushort mask) => mask != 0 && (_now & mask) == mask && (_prev & mask) != mask;

    private static bool RawHeld(ushort mask) => mask != 0 && (_raw & mask) == mask;

    private static void ReadOpenButton()
    {
        string text = Opt.PadButton.Key;
        if (_openText == text)
            return;

        _openText = text;
        ushort mask = 0;
        if (!text.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                mask |= Names.TryGetValue(part, out ushort bit) ? bit : (ushort)0;
        }

        _openMask = mask;
    }

    private static bool Read(int index, out XInputState state)
    {
        state = default;
        if (_missing)
            return false;

        try
        {
            return (_legacy ? XInputGetState910(index, out state) : XInputGetState14(index, out state)) == 0;
        }
        catch (DllNotFoundException) when (!_legacy)
        {
            _legacy = true;
            Log.Info("Pad: xinput1_4.dll not found, using xinput9_1_0.dll.");
            return Read(index, out state);
        }
        catch (Exception ex)
        {
            _missing = true;
            Log.Info("Pad: XInput not available, controller disabled: " + ex.Message);
            return false;
        }
    }

    private static void Poll()
    {
        _prev = _now;
        _rawPrev = _raw;
        if (_index >= 0)
        {
            if (Read(_index, out XInputState state))
            {
                Take(state);
                return;
            }

            Log.Info("Pad: controller " + _index + " disconnected.");
            _index = -1;
            _now = _raw = 0;
            _lx = _ly = _rx = _ry = 0;
            _lt = _rt = 0;
            _nextScan = 0.0;
        }

        double time = Clock.Now;
        if (time < _nextScan)
            return;

        _nextScan = time + ScanSeconds;
        for (int i = 0; i < 4; i++)
        {
            if (!Read(i, out XInputState state))
                continue;

            _index = i;
            _packet = state.PacketNumber;
            _now = _prev = _raw = _rawPrev = state.Gamepad.Buttons;
            Log.Info("Pad: controller " + i + " connected.");
            return;
        }
    }

    private static void Take(XInputState state)
    {
        XInputGamepad g = state.Gamepad;
        ushort b = g.Buttons;
        _raw = b;
        _lx = g.ThumbLX;
        _ly = g.ThumbLY;
        _rx = g.ThumbRX;
        _ry = g.ThumbRY;
        _lt = g.LeftTrigger;
        _rt = g.RightTrigger;
        if (g.ThumbLY > StickPush)
            b |= Up;
        else if (g.ThumbLY < -StickPush)
            b |= Down;
        if (g.ThumbLX < -StickPush)
            b |= Left;
        else if (g.ThumbLX > StickPush)
            b |= Right;
        _now = b;

        if (state.PacketNumber == _packet)
            return;

        _packet = state.PacketNumber;
        if (g.Buttons != 0 || g.LeftTrigger > TriggerNoise || g.RightTrigger > TriggerNoise ||
            Math.Abs((int)g.ThumbLX) > StickNoise || Math.Abs((int)g.ThumbLY) > StickNoise ||
            Math.Abs((int)g.ThumbRX) > StickNoise || Math.Abs((int)g.ThumbRY) > StickNoise)
            _padUsedAt = Stopwatch.GetTimestamp();
    }

    public static void Frame()
    {
        float dt = _frame.Step(0.1f);

        if (!Enabled)
        {
            if (_blockedInput != 0 || _restorePending)
                Unblock();
            else if (_buttonsInput != 0)
                UnblockButtons();
            _now = _prev = _raw = _rawPrev = 0;
            return;
        }

        ReadOpenButton();
        Poll();
        if (!Connected)
        {
            _held = -1f;
            if (_blockedInput != 0 || _restorePending)
                Unblock();
            return;
        }

        if (!Deck.IsOpen && !Shared.ScreenFree)
        {
            _buttonsInput = 0;
            _blockedInput = 0;
            _restorePending = false;
            _held = -1f;
            _waitRelease = true;
            return;
        }

        if (!Deck.IsOpen)
        {
            if (_restorePending && (_now & ~Start) == 0)
                Unblock();
            if (HoldButtons && !_restorePending)
                BlockButtons();
            else if (_buttonsInput != 0)
                UnblockButtons();
            RepairBindings();
            ClosedFrame(dt);
            return;
        }

        Block();
        DeckFrame(dt);
    }

    private static void ClosedFrame(float dt)
    {
        Array.Fill(_repeat, -1f);
        if (_waitRelease)
        {
            if (!Held(_openMask))
                _waitRelease = false;
            _held = -1f;
            return;
        }

        if (!Held(_openMask))
        {
            _held = -1f;
            return;
        }

        if (_held < 0f)
        {
            if (!Pressed(_openMask))
                return;
            _held = 0f;
        }

        _held += dt;
        if (_held < OpenHold)
            return;

        _held = -1f;
        _waitRelease = true;
        _padUsedAt = Stopwatch.GetTimestamp();
        Deck.Open();
    }

    private static void DeckFrame(float dt)
    {
        if (_waitRelease)
        {
            if (!Held(_openMask))
                _waitRelease = false;
        }
        else if (_openMask != 0 && Pressed(_openMask))
        {
            _waitRelease = true;
            Deck.Close();
            return;
        }

        if (Pressed(A))
            Deck.Press(Keys.Enter, false);
        else if (Pressed(B))
            Deck.Press(Keys.Back, false);
        else if (Pressed(LB))
            Deck.Press(Keys.PageUp, false);
        else if (Pressed(RB))
            Deck.Press(Keys.PageDown, false);
        else if (Pressed(X))
            Deck.Press(Keys.Delete, false);

        if (!Deck.IsOpen)
            return;

        for (int i = 0; i < Dirs.Length; i++)
        {
            if ((_now & Dirs[i]) == 0 || ((_openMask & Dirs[i]) != 0 && _waitRelease))
            {
                _repeat[i] = -1f;
                continue;
            }

            if (_repeat[i] < 0f)
            {
                _repeat[i] = 0f;
                Deck.Press(DirKeys[i], false);
                continue;
            }

            _repeat[i] += dt;
            if (_repeat[i] >= Deck.RepeatDelay)
            {
                _repeat[i] -= Deck.RepeatRate;
                Deck.Press(DirKeys[i], true);
            }

            if (!Deck.IsOpen)
                return;
        }
    }

    public static void DeckClosed()
    {
        Array.Fill(_repeat, -1f);
        if (_blockedInput == 0)
            return;

        if (Connected && (_now & ~Start) != 0)
            _restorePending = true;
        else
            Unblock();
    }

    public static void Shutdown()
    {
        HoldButtons = false;
        Unblock();
    }

    private static bool IsPadBind(string name) =>
        (name.StartsWith("XboxTypeS_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Gamepad_", StringComparison.OrdinalIgnoreCase)) &&
        !name.Equals("XboxTypeS_Start", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<int, bool> _padBindByName = new();

    private static bool IsPadBind(FName name)
    {
        if (name.Number != 0)
            return IsPadBind(name.ToString() ?? "");
        if (!_padBindByName.TryGetValue(name.Index, out bool pad))
            _padBindByName[name.Index] = pad = IsPadBind(name.ToString() ?? "");
        return pad;
    }

    private static PlayerInput? CurrentInput()
    {
        PlayerInput? input = World.Pc?.PlayerInput;
        return input is { IsValid: true } ? input : null;
    }

    private static void Block()
    {
        try
        {
            PlayerInput? input = CurrentInput();
            if (input == null)
                return;
            if (input.Ptr == _blockedInput && ++_blockFrames < RescanFrames)
                return;

            _blockFrames = 0;
            _restorePending = false;
            Park(input, true);
            _blockedInput = input.Ptr;
        }
        catch (Exception ex)
        {
            Log.Throttled("pad block", "Pad: failed to block game controller input: " + ex.Message);
        }
    }

    private static void Unblock()
    {
        _restorePending = false;
        _blockedInput = 0;
        _blockFrames = 0;
        _buttonsInput = 0;
        try
        {
            if (CurrentInput() is { } input)
            {
                Park(input, false);
                _repairedInput = input.Ptr;
            }
        }
        catch (Exception ex)
        {
            Log.Info("Pad: failed to restore game controller input: " + ex.Message);
        }
    }

    private static void BlockButtons()
    {
        try
        {
            PlayerInput? input = CurrentInput();
            if (input == null || (input.Ptr == _buttonsInput && ++_buttonsFrames < FlightRescanFrames))
                return;

            _buttonsFrames = 0;
            Park(input, true);
            _buttonsInput = input.Ptr;
        }
        catch (Exception ex)
        {
            Log.Throttled("pad block", "Pad: failed to block game controller input: " + ex.Message);
        }
    }

    private static void UnblockButtons()
    {
        _buttonsInput = 0;
        _buttonsFrames = 0;
        try
        {
            if (CurrentInput() is { } input)
                Park(input, false);
        }
        catch (Exception ex)
        {
            Log.Info("Pad: failed to restore game controller input: " + ex.Message);
        }
    }

    private static void RepairBindings()
    {
        if (_blockedInput != 0 || _restorePending || _buttonsInput != 0)
            return;

        PlayerInput? input = CurrentInput();
        if (input == null || input.Ptr == _repairedInput)
            return;

        _repairedInput = input.Ptr;
        int restored = Park(input, false);
        if (restored > 0)
            Log.Info("Pad: repaired " + restored + " controller bindings left blocked by a previous session.");
    }

    private static int Park(PlayerInput input, bool park)
    {
        int changed = 0;
        Span<Input.FKeyBind> binds = input.Bindings.AsSpan();
        for (int i = 0; i < binds.Length; i++)
        {
            ref Input.FKeyBind b = ref binds[i];
            if ((b.Control && b.Shift && b.Alt) == park || !IsPadBind(b.Name))
                continue;

            b.Control = park;
            b.Shift = park;
            b.Alt = park;
            changed++;
        }

        return changed;
    }
}
