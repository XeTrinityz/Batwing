using BmSDK.Engine;

namespace Trinity.Batwing;

// Everything the mod draws on the screen, once a frame, in order.
internal static class Overlay
{
    private const float MaxFrame = 0.1f;

    private static FrameTimer _frame;
    private static float _deckAnim;

    // The deck was last shown on the gauntlet, so it fades out there and not on the screen.
    private static bool _onWrist;

    public static float DeckAnim => _deckAnim;

    public static void Draw(Canvas? canvas)
    {
        bool paused = World.Paused;
        if (Deck.IsOpen && (paused || Shared.MenuOwner != Shared.OwnerName))
        {
            Deck.Close();
            _deckAnim = 0f;
        }

        Gfx.Dt = _frame.Step(MaxFrame);
        Gfx.Time += Gfx.Dt;
        Gfx.Frame++;
        Palette.Update();
        Toasts.Advance(Gfx.Dt);
        WheelView.Advance(Gfx.Dt);
        _deckAnim = Ease.Approach(_deckAnim, Deck.IsOpen ? 1f : 0f, Gfx.Dt * (Deck.IsOpen ? 7f : 9f));

        if (Gauntlet.Hosting)
            _onWrist = true;
        else if (Deck.IsOpen || _deckAnim <= 0f)
            _onWrist = false;
        bool flat = !_onWrist && !Gauntlet.Waiting;
        GauntletScreen.Refresh();

        bool hud = !paused && Wing.HasHud;
        if (canvas == null || (_deckAnim <= 0f && !hud && !Toasts.Showing && !WheelView.Showing))
            return;

        if (!Gfx.Begin(canvas, out Font? font))
            return;

        try
        {
            Shapes.Under = Palette.Black;
            if (hud)
                Safe.Run("hud", () => Wing.Draw(canvas));

            Gfx.Alpha = 1f;
            if (_deckAnim > 0f && flat)
                Safe.Run("deck", () => DeckView.Draw(_deckAnim));

            Gfx.Alpha = 1f;
            Safe.Run("wheel", WheelView.Draw);
            Gfx.Alpha = 1f;
            Toasts.Draw();
        }
        finally
        {
            Gfx.Alpha = 1f;
            canvas.Font = font;
        }
    }
}
