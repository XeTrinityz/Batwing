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

    public static PadButton Button(Act a) => Opt.PadBinds.TryGetValue(a, out PadBind? b) ? b.Button : PadButton.None;

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
            foreach ((Act act, PadBind b) in Opt.PadBinds)
            {
                if (b.Button == pressed)
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

    // The stick that aims and steers, with the response and invert the player picked.
    public static Vector2 AimStick
    {
        get
        {
            if (!Listening)
                return Vector2.Zero;

            Vector2 v = Shape(Swapped ? Pad.LeftRaw : Pad.RightRaw, Opt.Deadzone.Number, Opt.PadResponse.Index switch { 1 => 1f, 2 => 2.6f, _ => 0f });
            return Opt.PadInvert.On ? v with { Y = -v.Y } : v;
        }
    }

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
