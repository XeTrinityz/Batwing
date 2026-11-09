namespace Trinity.Batwing;

internal static class KeyInput
{
    public static void Pressed(Keys key)
    {
        Pad.KeyboardUsed();

        if (Deck.IsOpen)
        {
            Deck.Press(key, false);
            return;
        }

        if (!Shared.ScreenFree)
            return;

        if (key == Opt.DeckKey.Key)
        {
            Deck.Toggle();
            return;
        }

        if (!World.InGame || World.Paused)
            return;

        if (key == Opt.CallKey.Key)
            Wing.I.Toggle();
        else if (key == Opt.RemoteKey.Key)
            Wing.I.RemoteToggle();
        else if (key == Opt.EscortKey.Key)
            Wing.I.EscortToggle();
        else if (key == Opt.StrikeKey.Key)
            Wing.I.Strike();
        else if (key == Opt.LiftKey.Key)
            Wing.I.LiftAction();
    }
}
