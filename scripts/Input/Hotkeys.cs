namespace Trinity.Batwing;

// The mod's own keys on the keyboard. They're kept from the game, which has debug commands bound to some of
// the defaults.
internal static class Hotkeys
{
    private const string Owner = "hotkeys";

    private static readonly Act[] Global = { Act.Deck, Act.Call, Act.Remote, Act.Escort, Act.Strike, Act.Lift };
    private static readonly Keys[] _own = new Keys[Global.Length];

    // Hotkeys and the command wheel work on the same terms.
    public static bool Live => Shared.ScreenFree && World.InGame && !World.Paused;

    public static bool StrikeHeld => (Live && Keyboard.IsDown(Controls.Key(Act.Strike))) || Wheel.StrikeHeld;

    public static void Pressed(Keys key)
    {
        Controls.KeyPressed(key);

        if (Deck.IsOpen)
        {
            Deck.Press(key, false);
            return;
        }

        // Before the screen check, so the deck can say another menu has the screen.
        if (key == Controls.Key(Act.Deck))
        {
            Deck.Toggle();
            return;
        }

        if (!Live)
            return;

        if (key == Controls.Key(Act.Call))
            Wing.CallPressed();
        else if (key == Controls.Key(Act.Remote))
            Wing.RemotePressed();
        else if (key == Controls.Key(Act.Escort))
            Wing.EscortPressed();
        else if (key == Controls.Key(Act.Strike))
            Wing.StrikePressed();
        else if (key == Controls.Key(Act.Lift))
            Wing.LiftPressed();
    }

    public static void Frame()
    {
        // With Alt down the key is the game's, or Alt and F4 would stop closing it.
        if (!World.InGame || Keyboard.IsDown(Keys.Menu))
        {
            GameKeys.Release(Owner);
            return;
        }

        bool changed = !GameKeys.Holds(Owner);
        for (int i = 0; i < Global.Length; i++)
        {
            // Escape and Alt stay the game's, or there's no pausing and no Alt and F4.
            Keys key = Controls.Key(Global[i]);
            if (key is Keys.Escape or Keys.Menu)
                key = Keys.None;
            changed |= _own[i] != key;
            _own[i] = key;
        }

        if (changed)
            GameKeys.Hold(Owner, _own.ToArray());
    }
}
