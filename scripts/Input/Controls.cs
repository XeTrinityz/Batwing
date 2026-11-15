using System.Numerics;

namespace Trinity.Batwing;

internal enum Act
{
    Call,
    Remote,
    Escort,
    Strike,
    Lift,
    Deck,
    Forward,
    Back,
    Left,
    Right,
    Up,
    Down,
    Boost,
    Mode,
    Bail,
    Gun,
    Cannon,
    Lock,
    Look,
    Camera,
    Hints,
    Fire,
    Weapon,
}

// Every control the mod reads, by what it does rather than which key or button it's on. Presses are counted
// as they happen, so whatever reads them later never misses one or sees one twice, however its own updates
// fall against the frames.
internal static class Controls
{
    private static readonly int[] _presses = new int[Enum.GetValues<Act>().Length];
    private static double _padAt = -1.0, _keysAt;

    // The last thing used was the pad, so prompts show pad buttons.
    public static bool PadActive => Pad.Connected && _padAt > _keysAt;

    // Flight controls only count while nothing else has the input.
    public static bool Listening => World.InGame && !World.Paused && !Shared.AnyMenuOpen && !Wheel.Open && Native.GameInFront;

    public static Keys Key(Act a) => Opt.KeyBinds.TryGetValue(a, out Bind? b) ? b.Key : Keys.None;

    // The button an action is on in the fly style in use. A row from the other style has none, and in GTA
    // style the bumpers yaw, so an old bind left on one of them is ignored.
    public static PadButton Button(Act a)
    {
        if (!Opt.PadBinds.TryGetValue(a, out PadBind? b) || !b.Active)
            return PadButton.None;
        return Opt.Gta && b.Button is PadButton.LB or PadButton.RB ? PadButton.None : b.Button;
    }

    public static void KeyboardUsed() => _keysAt = Clock.Now;

    public static void KeyPressed(Keys key)
    {
        KeyboardUsed();
        if (!Listening)
            return;

        foreach ((Act act, Bind b) in Opt.KeyBinds)
        {
            if (b.Key == key)
                _presses[(int)act]++;
        }
    }

    public static void Frame()
    {
        Pad.Poll();
        if (Pad.Busy)
            _padAt = Clock.Now;
        if (!Listening)
            return;

        foreach (PadButton pressed in Pad.NewPresses())
        {
            foreach (Act act in Opt.PadBinds.Keys)
            {
                if (Button(act) == pressed)
                    _presses[(int)act]++;
            }
        }
    }

    public static bool Down(Act a) => Listening && (GameKeys.Down(Key(a)) || Pad.Down(Button(a)));

    public static int Presses(Act a) => _presses[(int)a];

    public static float Axis(Act plus, Act minus) => (Down(plus) ? 1f : 0f) - (Down(minus) ? 1f : 0f);

    private static bool Swapped => Opt.StickLayout.Index == 1;

    // The stick that flies: throttle and roll on the wings, moving in hover. Even response.
    public static Vector2 FlyStick => Listening ? Shape(Swapped ? Pad.RightRaw : Pad.LeftRaw, Opt.Deadzone.Number, 1f) : Vector2.Zero;

    private static float Response => Opt.PadResponse.Index switch { 1 => 1f, 2 => 2.6f, _ => 0f };

    // The stick that aims and steers, with the response and invert the player picked. In GTA style it only
    // moves the camera, and the invert is on the stick that flies.
    public static Vector2 AimStick
    {
        get
        {
            if (!Listening)
                return Vector2.Zero;

            Vector2 v = Shape(Swapped ? Pad.LeftRaw : Pad.RightRaw, Opt.Deadzone.Number, Response);
            return Opt.PadInvert.On && !Opt.Gta ? v with { Y = -v.Y } : v;
        }
    }

    // GTA style flies on this stick, roll and pitch, with pull back to climb unless pitch is inverted.
    public static Vector2 GtaStick
    {
        get
        {
            if (!Listening)
                return Vector2.Zero;

            Vector2 v = Shape(Swapped ? Pad.RightRaw : Pad.LeftRaw, Opt.Deadzone.Number, Response);
            return Opt.PadInvert.On ? v : v with { Y = -v.Y };
        }
    }

    // GTA style yaws on the bumpers, right is positive.
    public static float Bumpers => Listening && Opt.Gta ? (Pad.Down(PadButton.RB) ? 1f : 0f) - (Pad.Down(PadButton.LB) ? 1f : 0f) : 0f;

    public static float RightTrigger => Listening ? Pad.RightTrigger : 0f;

    public static float LeftTrigger => Listening ? Pad.LeftTrigger : 0f;

    // A round deadzone, so diagonals stay smooth, with the rest of the throw stretched back to full. A power of
    // 0 means the smooth curve: soft round the middle for fine corrections, full rate at the edge.
    public static Vector2 Shape(Vector2 raw, float dead, float power)
    {
        float size = raw.Length();
        if (size <= dead || size < 1e-4f)
            return Vector2.Zero;

        float t = Math.Min(1f, (size - dead) / (1f - dead));
        t = power <= 0f ? t * (0.35f + 0.65f * t * t) : MathF.Pow(t, power);
        return raw / size * t;
    }
}

// Remembers which presses of each control one reader has already seen.
internal sealed class Edges
{
    private readonly int[] _seen = new int[Enum.GetValues<Act>().Length];

    // Forgets anything pressed before now, like the key that started the flight.
    public void Reset()
    {
        foreach (Act a in Enum.GetValues<Act>())
            _seen[(int)a] = Controls.Presses(a);
    }

    public bool Take(Act a)
    {
        int now = Controls.Presses(a);
        if (now == _seen[(int)a])
            return false;

        _seen[(int)a] = now;
        return true;
    }
}
