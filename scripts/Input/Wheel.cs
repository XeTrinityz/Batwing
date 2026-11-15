using System.Numerics;

namespace Trinity.Batwing;

// The command wheel on the controller. Hold the wheel buttons, point a stick at a command and let go. A face
// button while the wheel buttons are held is a shortcut that skips the wheel.
internal static class Wheel
{
    public enum Item
    {
        Call,
        Remote,
        Escort,
        Strike,
        Lift,
        Deck,
    }

    public const int Count = 6;
    private const float OpenAfter = 0.18f;
    private const float Pick = 0.55f;
    private const string ShortcutHold = "wheel shortcuts";
    private const string WheelHold = "wheel";

    private static readonly string[] ShortcutNames = { "XboxTypeS_A", "XboxTypeS_B", "XboxTypeS_X", "XboxTypeS_Y", "XboxTypeS_DPad_Down" };

    private static float _held = -1f;
    private static bool _shortcut, _waitRelease;

    public static bool Open { get; private set; }

    public static int Selected { get; private set; } = -1;

    public static double OpenedAt { get; private set; }

    // While the wheel buttons and X are held the escort keeps marking strike targets.
    public static bool StrikeHeld => _shortcut && Pad.Down(PadButton.X);

    public static PadButton Chord => Opt.MenuChord.Index switch
    {
        0 => PadButton.LB | PadButton.RB,
        1 => PadButton.LS | PadButton.RS,
        _ => PadButton.None,
    };

    public static void Frame(float dt)
    {
        PadButton chord = Chord;
        bool held = chord != PadButton.None && Pad.Down(chord);

        if (Deck.IsOpen)
        {
            Close();
            _waitRelease |= held;
            return;
        }

        if (!held)
        {
            if (Open)
                Run();
            _waitRelease = false;
            Rest();
            return;
        }

        // Held from before, like the press that closed the deck.
        if (_waitRelease || !Hotkeys.Live)
        {
            Close();
            _waitRelease = true;
            return;
        }

        if (Open)
        {
            Choose();
            return;
        }

        if (_held < 0f && !_shortcut)
            _held = 0f;

        // On foot or in the car the face buttons under the wheel are shortcuts, and the game doesn't get them.
        // Flying they're the flight controls, so there it's the wheel only.
        GameKeys.Hold(ShortcutHold, !Wing.Piloting, Array.Empty<Keys>(), ShortcutNames);
        if (!Wing.Piloting && Shortcut())
        {
            _shortcut = true;
            _held = -1f;
            return;
        }

        if (_shortcut)
            return;

        _held += dt;
        if (_held >= OpenAfter)
            Show();
    }

    private static void Show()
    {
        Open = true;
        Selected = -1;
        OpenedAt = Gfx.Time;
        GameKeys.Hold(WheelHold, true, Array.Empty<Keys>(), GameKeys.PadNames);
    }

    private static void Choose()
    {
        if (Pad.Pressed(PadButton.B))
        {
            Close();
            _waitRelease = true;
            return;
        }

        Vector2 v = Pad.RightRaw.Length() > Pad.LeftRaw.Length() ? Pad.RightRaw : Pad.LeftRaw;
        if (v.Length() > Pick)
        {
            float angle = MathF.Atan2(v.Y, v.X) * Geo.Deg;
            int index = (int)MathF.Round(Geo.WrapDegrees(90f - angle) / (360f / Count));
            Selected = ((index % Count) + Count) % Count;
        }

        if (Pad.Pressed(PadButton.A) && Selected >= 0)
        {
            Run();
            _waitRelease = true;
        }
    }

    private static void Run()
    {
        int picked = Selected;
        Close();
        if (picked < 0)
            return;

        switch ((Item)picked)
        {
            case Item.Call:
                Wing.CallPressed();
                break;
            case Item.Remote:
                Wing.RemotePressed();
                break;
            case Item.Escort:
                Wing.EscortPressed();
                break;
            case Item.Strike:
                Wing.StrikePressed();
                break;
            case Item.Lift:
                Wing.LiftPressed();
                break;
            case Item.Deck:
                Deck.Open();
                break;
        }
    }

    private static bool Shortcut()
    {
        if (Pad.Pressed(PadButton.Y))
            Wing.CallPressed();
        else if (Pad.Pressed(PadButton.B))
            Wing.EscortPressed();
        else if (Pad.Pressed(PadButton.A))
            Wing.RemotePressed();
        else if (Pad.Pressed(PadButton.X))
            Wing.StrikePressed();
        else if (Pad.Pressed(PadButton.Down))
            Wing.LiftPressed();
        else
            return false;

        return true;
    }

    public static string Label(Item item) => item switch
    {
        Item.Call => Wing.CallLabel,
        Item.Remote => Wing.RemoteLabel,
        Item.Escort => Wing.EscortLabel,
        Item.Strike => "STRIKE",
        Item.Lift => Wing.LiftLabel,
        _ => "SETTINGS",
    };

    public static string Shortcut(Item item) => item switch
    {
        Item.Call => Glyphs.Name(PadButton.Y),
        Item.Remote => Glyphs.Name(PadButton.A),
        Item.Escort => Glyphs.Name(PadButton.B),
        Item.Strike => Glyphs.Name(PadButton.X),
        Item.Lift => Glyphs.Name(PadButton.Down),
        _ => "",
    };

    // Whether a command makes sense right now. The wheel still shows it, dimmed.
    public static bool Ready(Item item) => item switch
    {
        Item.Strike => Wing.Escorting,
        Item.Remote => !Wing.Busy || Wing.Remote,
        _ => true,
    };

    private static void Close()
    {
        if (Open)
            Open = false;
        GameKeys.Release(WheelHold);
        Selected = -1;
    }

    private static void Rest()
    {
        Close();
        _held = -1f;
        _shortcut = false;
        GameKeys.Release(ShortcutHold);
    }

    public static void Shutdown()
    {
        Rest();
        _waitRelease = false;
    }
}
