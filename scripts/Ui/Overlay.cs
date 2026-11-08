using BmSDK.Engine;

namespace Trinity.Batwing;

internal static class Overlay
{
    private const float MaxFrame = 0.1f;

    private static FrameTimer _frame;
    private static float _deckAnim;

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
        Readout.Advance(Gfx.Dt);

        float target = Deck.IsOpen ? 1f : 0f;
        float speed = Deck.IsOpen ? 7f : 9f;
        _deckAnim = _deckAnim < target ? Math.Min(target, _deckAnim + Gfx.Dt * speed) : Math.Max(target, _deckAnim - Gfx.Dt * speed);

        bool hud = !paused && Wing.I.HasHud;
        if (canvas == null || (_deckAnim <= 0f && !hud && !Readout.Showing))
            return;

        if (!Gfx.Begin(canvas, out Font? font))
            return;

        try
        {
            if (hud)
            {
                Gfx.Alpha = 1f;
                Shapes.Under = Palette.Black;
                try
                {
                    Wing.I.Draw(canvas);
                }
                catch (Exception ex)
                {
                    Log.Error("hud", "Flight HUD", ex);
                }
            }

            if (_deckAnim > 0f)
                DeckView.Draw(_deckAnim);

            Gfx.Alpha = 1f;
            Readout.Draw();
        }
        finally
        {
            canvas.Font = font;
        }
    }
}
