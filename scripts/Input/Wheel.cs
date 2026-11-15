using System.Numerics;

namespace Trinity.Batwing;

// The command wheel on the controller, on the View button. Hold View to open it, point a stick at a command and
// let go to use it. A quick tap of View still opens the map. The game never sees View while the wheel is on, so
// the tap opens the map through the game's own command.
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
    private const float HoldSeconds = 0.25f;
    private const float Pick = 0.55f;
    private const string ViewHold = "view";
    private const string WheelHold = "wheel";
    private static readonly string[] ViewName = { "XboxTypeS_Back" };

    private static float _held = -1f;
    private static bool _waitRelease;

    public static bool Open { get; private set; }

    public static int Selected { get; private set; } = -1;

    public static double OpenedAt { get; private set; }

    private static bool On => Opt.CommandWheel.On;

    // The press that opens the wheel, for the deck to close itself on.
    public static bool ChordPressed => On && Pad.Pressed(PadButton.Back);

    public static string ChordName => On ? "HOLD " + Glyphs.Name(PadButton.Back) : "";

    public static void Frame(float dt)
    {
        // View is the mod's while it's in the world with the screen free. Paused or in another menu it's the game's.
        GameKeys.Hold(ViewHold, On && Hotkeys.Live, Array.Empty<Keys>(), ViewName);

        bool held = On && Pad.Down(PadButton.Back);
        if (Deck.IsOpen)
        {
            Close();
            _held = -1f;
            _waitRelease |= held;
            return;
        }

        if (!held)
        {
            if (Open)
                Run();
            else if (_held >= 0f && !_waitRelease)
                Map();
            _held = -1f;
            _waitRelease = false;
            return;
        }

        // Held from before, like the press that closed the deck.
        if (_waitRelease || !Hotkeys.Live)
        {
            Close();
            _held = -1f;
            _waitRelease = true;
            return;
        }

        if (Open)
        {
            Choose();
            return;
        }

        _held = _held < 0f ? 0f : _held + dt;
        if (_held >= HoldSeconds)
            Show();
    }

    // A tap of View is the game's map, or the upgrades screen when there's one waiting.
    private static void Map()
    {
        if (!Hotkeys.Live || World.Pc is not { } pc)
            return;

        Safe.Run("open map", pc.OpenLevelUpOrMap);
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

    public static string Label(Item item) => item switch
    {
        Item.Call => Wing.CallLabel,
        Item.Remote => Wing.RemoteLabel,
        Item.Escort => Wing.EscortLabel,
        Item.Strike => "STRIKE",
        Item.Lift => Wing.LiftLabel,
        _ => "SETTINGS",
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
        Open = false;
        Selected = -1;
        GameKeys.Release(WheelHold);
    }

    public static void Shutdown()
    {
        Close();
        GameKeys.Release(ViewHold);
        _held = -1f;
        _waitRelease = false;
    }
}
