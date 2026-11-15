using static Trinity.Batwing.Gfx;

namespace Trinity.Batwing;

// Controller buttons as the player knows them, Xbox or PlayStation, and key caps for the keyboard.
internal static class Glyphs
{
    private static bool PlayStation => Opt.ButtonStyle.Index == 1;

    public static bool Bindable(PadButton b) => b is PadButton.A or PadButton.B or PadButton.X or PadButton.Y or PadButton.LB or PadButton.RB
        or PadButton.LS or PadButton.RS or PadButton.Back or PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right;

    public static string Name(PadButton b) => b switch
    {
        PadButton.None => "NONE",
        PadButton.A => PlayStation ? "CROSS" : "A",
        PadButton.B => PlayStation ? "CIRCLE" : "B",
        PadButton.X => PlayStation ? "SQUARE" : "X",
        PadButton.Y => PlayStation ? "TRIANGLE" : "Y",
        PadButton.LB => PlayStation ? "L1" : "LB",
        PadButton.RB => PlayStation ? "R1" : "RB",
        PadButton.LS => PlayStation ? "L3" : "LS",
        PadButton.RS => PlayStation ? "R3" : "RS",
        PadButton.Back => PlayStation ? "TOUCHPAD" : "VIEW",
        PadButton.Start => PlayStation ? "OPTIONS" : "MENU",
        PadButton.Up => "D-PAD UP",
        PadButton.Down => "D-PAD DOWN",
        PadButton.Left => "D-PAD LEFT",
        PadButton.Right => "D-PAD RIGHT",
        PadButton.LB | PadButton.RB => PlayStation ? "L1 + R1" : "LB + RB",
        PadButton.LS | PadButton.RS => PlayStation ? "L3 + R3" : "LS + RS",
        _ => b.ToString().ToUpperInvariant(),
    };

    public static string LeftStick => PlayStation ? "L" : "LS";
    public static string RightStick => PlayStation ? "R" : "RS";
    public static string LeftTrigger => PlayStation ? "L2" : "LT";
    public static string RightTrigger => PlayStation ? "R2" : "RT";

    // The stick that flies and the one that aims, as laid out.
    public static string FlyStick => Opt.StickLayout.Index == 1 ? RightStick : LeftStick;
    public static string AimStick => Opt.StickLayout.Index == 1 ? LeftStick : RightStick;

    private static Rgba Face(PadButton b) => PlayStation
        ? b switch
        {
            PadButton.A => new Rgba(124, 178, 232),
            PadButton.B => new Rgba(255, 102, 102),
            PadButton.X => new Rgba(230, 140, 210),
            _ => new Rgba(64, 226, 160),
        }
        : b switch
        {
            PadButton.A => new Rgba(100, 220, 90),
            PadButton.B => new Rgba(240, 80, 70),
            PadButton.X => new Rgba(70, 150, 255),
            _ => new Rgba(250, 210, 60),
        };

    // Draws a button with its left edge at x. Returns how wide it was.
    public static float Draw(float x, float cy, PadButton b, float scale, float alpha = 1f)
    {
        float h = TextH(scale) + S(4f) * scale / 0.45f;
        float r = h * 0.5f;
        if (b is PadButton.A or PadButton.B or PadButton.X or PadButton.Y)
        {
            Rgba col = Face(b);
            Rgba under = Shapes.Under;
            Shapes.Disc(x + r, cy, r, Palette.Black, 0.55f * alpha);
            Shapes.Ring(x + r, cy, r - MathF.Max(1f, S(1.4f)), r, col, alpha);
            if (PlayStation)
                Symbol(x + r, cy, r * 0.48f, b, col, alpha);
            else
                TextCentre(x + r, cy, Name(b), col, scale, alpha);
            Shapes.Under = under;
            return h;
        }

        return Cap(x, cy, Name(b), scale, Palette.Text, alpha);
    }

    // A word in a rounded key cap, for keys, sticks and triggers. Returns how wide it was.
    public static float Cap(float x, float cy, string text, float scale, Rgba col, float alpha = 1f)
    {
        float pad = S(6f) * scale / 0.45f;
        float w = TextW(text, scale) + pad * 2f;
        float h = TextH(scale) + S(4f) * scale / 0.45f;
        Rect(x, cy - h * 0.5f, w, h, Palette.Black, 0.45f * alpha);
        Outline(x, cy - h * 0.5f, w, h, col, 0.75f * alpha, S(1f));
        TextMid(x + pad, cy, text, col, scale, alpha);
        return w;
    }

    public static float Width(PadButton b, float scale)
    {
        float h = TextH(scale) + S(4f) * scale / 0.45f;
        return b is PadButton.A or PadButton.B or PadButton.X or PadButton.Y ? h : TextW(Name(b), scale) + S(6f) * scale / 0.45f * 2f;
    }

    public static float CapWidth(string text, float scale) => TextW(text, scale) + S(6f) * scale / 0.45f * 2f;

    private static void Symbol(float cx, float cy, float r, PadButton b, Rgba col, float alpha)
    {
        float t = MathF.Max(1f, r * 0.28f);
        switch (b)
        {
            case PadButton.A:
                Shapes.QueueLine(cx - r, cy - r, cx + r, cy + r, t);
                Shapes.QueueLine(cx - r, cy + r, cx + r, cy - r, t);
                Shapes.FlushLines(col with { A = (byte)(255 * alpha) });
                break;
            case PadButton.B:
                Shapes.Ring(cx, cy, r - t, r, col, alpha);
                break;
            case PadButton.X:
                Shapes.QueueLine(cx - r, cy - r, cx + r, cy - r, t);
                Shapes.QueueLine(cx + r, cy - r, cx + r, cy + r, t);
                Shapes.QueueLine(cx + r, cy + r, cx - r, cy + r, t);
                Shapes.QueueLine(cx - r, cy + r, cx - r, cy - r, t);
                Shapes.FlushLines(col with { A = (byte)(255 * alpha) });
                break;
            default:
                Shapes.QueueLine(cx, cy - r, cx + r, cy + r * 0.75f, t);
                Shapes.QueueLine(cx + r, cy + r * 0.75f, cx - r, cy + r * 0.75f, t);
                Shapes.QueueLine(cx - r, cy + r * 0.75f, cx, cy - r, t);
                Shapes.FlushLines(col with { A = (byte)(255 * alpha) });
                break;
        }
    }
}

// A prompt is a key on the keyboard or a button on the pad, whichever is in use.
internal readonly record struct Prompt(string Key, PadButton Button, string Pad)
{
    public static Prompt Of(Act act) => new(Bind.KeyName(Controls.Key(act)), Controls.Button(act), "");

    public static Prompt Keys(string key, string pad) => new(key, PadButton.None, pad);

    public static Prompt Both(string key, PadButton button) => new(key, button, "");

    // Draws the prompt for the device in use. Returns how wide it was.
    public float Draw(float x, float cy, float scale, float alpha = 1f)
    {
        if (!Controls.PadActive)
            return Glyphs.Cap(x, cy, Key, scale, Palette.Text, alpha);
        if (Button != PadButton.None)
            return Glyphs.Draw(x, cy, Button, scale, alpha);
        return Glyphs.Cap(x, cy, Pad.Length > 0 ? Pad : Key, scale, Palette.Text, alpha);
    }

    public float Width(float scale)
    {
        if (!Controls.PadActive)
            return Glyphs.CapWidth(Key, scale);
        if (Button != PadButton.None)
            return Glyphs.Width(Button, scale);
        return Glyphs.CapWidth(Pad.Length > 0 ? Pad : Key, scale);
    }

    public string Text => !Controls.PadActive ? Key : Button != PadButton.None ? Glyphs.Name(Button) : Pad.Length > 0 ? Pad : Key;
}
