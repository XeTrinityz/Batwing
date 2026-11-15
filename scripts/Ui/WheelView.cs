using static Trinity.Batwing.Gfx;

namespace Trinity.Batwing;

// The command wheel in the middle of the screen. Six segments round a hub that says what the picked one does.
internal static class WheelView
{
    private const float Outer = 190f;
    private const float Inner = 92f;
    private const float Gap = 3f;

    private static float _anim;

    public static bool Showing => _anim > 0f;

    public static void Advance(float dt)
    {
        _anim = Ease.Approach(_anim, Wheel.Open ? 1f : 0f, dt * (Wheel.Open ? 8f : 10f));
    }

    public static void Draw()
    {
        if (_anim <= 0f)
            return;

        float ease = Ease.Out(_anim);
        Alpha = ease;
        float cx = W * 0.5f, cy = H * 0.5f;
        float grow = 0.9f + 0.1f * ease;
        float r1 = S(Outer) * grow, r0 = S(Inner) * grow;
        float slice = 360f / Wheel.Count;
        Rgba under = Shapes.Under;

        Shapes.Under = Palette.Black;
        Shapes.Disc(cx, cy, r1 + S(10f), Palette.Black, 0.45f);
        Shapes.Under = Palette.Screen;
        for (int i = 0; i < Wheel.Count; i++)
        {
            var item = (Wheel.Item)i;
            bool picked = i == Wheel.Selected;
            bool ready = Wheel.Ready(item);

            // Screen angles run clockwise from the right, and the first item sits at the top.
            float mid = -90f + i * slice;
            float a0 = mid - slice * 0.5f + Gap * 0.5f, a1 = mid + slice * 0.5f - Gap * 0.5f;
            Shapes.Arc(cx, cy, r0, r1, a0, a1, Palette.Screen, 0.92f);
            if (picked)
                Shapes.Arc(cx, cy, r0, r1, a0, a1, Palette.Hud, 0.28f);
            Shapes.Arc(cx, cy, r1 - S(picked ? 4f : 2f), r1, a0, a1, picked ? Palette.Hud : Palette.Dim, picked ? 1f : 0.6f);

            float rad = mid / Geo.Deg;
            float lr = (r0 + r1) * 0.5f;
            float lx = cx + MathF.Cos(rad) * lr, ly = cy + MathF.Sin(rad) * lr;
            string label = Wheel.Label(item);
            Rgba ink = picked ? Palette.White : ready ? Palette.Text : Palette.Faint;
            float s = 0.46f;
            string rest = FitLine(label, r1 - r0, s, out string first);
            if (rest.Length == 0)
            {
                TextCentre(lx, ly, label, ink, s);
            }
            else
            {
                TextCentre(lx, ly - TextH(s) * 0.5f, first, ink, s);
                TextCentre(lx, ly + TextH(s) * 0.5f, rest, ink, s);
            }
        }

        Shapes.Disc(cx, cy, r0 - S(6f), Palette.Screen, 0.95f);
        Shapes.Ring(cx, cy, r0 - S(7f), r0 - S(6f), Palette.Dim, 0.8f);
        Shapes.Under = under;

        if (Wheel.Selected >= 0)
        {
            var item = (Wheel.Item)Wheel.Selected;
            TextCentre(cx, cy - S(10f), Wheel.Label(item), Palette.Hud, 0.56f);
            TextCentre(cx, cy + S(16f), Wheel.Ready(item) ? "LET GO TO USE" : "NOT NOW", Palette.Dim, 0.4f);
        }
        else
        {
            Spaced(cx - SpacedW("BATWING", 0.56f, 2f) * 0.5f, cy - S(18f), "BATWING", Palette.Hud, 0.56f, 2f);
            TextCentre(cx, cy + S(12f), Decode.Text(Wing.Status, (float)(Time - Wheel.OpenedAt)), Palette.Dim, 0.4f);
        }

        float hs = 0.42f;
        string hint = "POINT A STICK, LET GO TO USE";
        float hw = Glyphs.Width(PadButton.B, hs) + S(6f) + TextW("CANCEL", hs) + S(18f) + TextW(hint, hs);
        float hx = cx - hw * 0.5f, hy = cy + r1 + S(34f);
        TextMid(hx, hy, hint, Palette.Dim, hs);
        hx += TextW(hint, hs) + S(18f);
        hx += Glyphs.Draw(hx, hy, PadButton.B, hs) + S(6f);
        TextMid(hx, hy, "CANCEL", Palette.Dim, hs);
        Alpha = 1f;
    }
}
