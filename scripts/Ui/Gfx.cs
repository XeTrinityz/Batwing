using BmSDK.Engine;
using FLinearColor = BmSDK.GameObject.FLinearColor;

namespace Trinity.Batwing;

// Drawing on the game's canvas. Sizes are in 1080p pixels and scaled to the screen.
internal static class Gfx
{
    private const int MaxCachedWidths = 4096;
    private const string FontPath = "BmFonts.EngineSmallFont";
    private const string WhitePath = "EngineResources.WhiteSquareTexture";

    private static readonly float[] Linear = BuildLinear();
    private static readonly Dictionary<(string, float), float> _widths = new();
    private static readonly Dictionary<float, float> _heights = new();
    private static Texture2D? _white;
    private static Font? _font;

    public static Canvas C = null!;
    public static float Scale = 1f;
    public static float Alpha = 1f;
    public static float Dt;
    public static double Time;
    public static int Frame;

    public static float W => C.ClipX;

    public static float H => C.ClipY;

    public static float S(float v) => v * Scale;

    public static Texture2D? WhiteTexture => _white is { IsValid: true } ? _white : null;

    public static bool Begin(Canvas canvas, out Font? previous)
    {
        previous = null;
        if (_white is not { IsValid: true })
            _white = Game.FindObject<Texture2D>(WhitePath);
        if (_font is not { IsValid: true })
            _font = Game.FindObject<Font>(FontPath);
        if (_white == null)
            return false;

        C = canvas;
        Scale = Math.Clamp(canvas.ClipY / 1080f, 0.75f, 2.5f);
        Alpha = 1f;
        previous = canvas.Font;
        if (_font != null)
            canvas.Font = _font;
        return true;
    }

    public static void Rect(float x, float y, float w, float h, Rgba col, float alpha = 1f)
    {
        if (w <= 0f || h <= 0f)
            return;

        C.SetPos(MathF.Round(x), MathF.Round(y), 0f);
        var color = new FLinearColor { R = Linear[col.R], G = Linear[col.G], B = Linear[col.B], A = col.A / 255f * alpha * Alpha };
        C.DrawTile(_white!, MathF.Round(w), MathF.Round(h), 0f, 0f, 32f, 32f, color, false, EngineTypes.EBlendMode.BLEND_Translucent);
    }

    public static void Outline(float x, float y, float w, float h, Rgba col, float alpha, float t = 1f)
    {
        t = MathF.Max(1f, MathF.Round(t));
        Rect(x, y, w, t, col, alpha);
        Rect(x, y + h - t, w, t, col, alpha);
        Rect(x, y + t, t, h - t * 2f, col, alpha);
        Rect(x + w - t, y + t, t, h - t * 2f, col, alpha);
    }

    // Corner ticks round a box, lighter than a full outline.
    public static void Corners(float x, float y, float w, float h, float len, Rgba col, float alpha, float t = 1f)
    {
        t = MathF.Max(1f, MathF.Round(t));
        Rect(x, y, len, t, col, alpha);
        Rect(x, y, t, len, col, alpha);
        Rect(x + w - len, y, len, t, col, alpha);
        Rect(x + w - t, y, t, len, col, alpha);
        Rect(x, y + h - t, len, t, col, alpha);
        Rect(x, y + h - len, t, len, col, alpha);
        Rect(x + w - len, y + h - t, len, t, col, alpha);
        Rect(x + w - t, y + h - len, t, len, col, alpha);
    }

    public static float TextW(string text, float scale)
    {
        if (_widths.Count >= MaxCachedWidths)
            _widths.Clear();

        float size = scale * Scale;
        if (_widths.TryGetValue((text, size), out float w))
            return w;

        C.TextSize(text, out w, out _, size, size);
        _widths[(text, size)] = w;
        return w;
    }

    public static float TextH(float scale)
    {
        float size = scale * Scale;
        if (_heights.TryGetValue(size, out float h))
            return h;

        C.TextSize("AQ", out _, out h, size, size);
        _heights[size] = h;
        return h;
    }

    public static void Text(float x, float y, string text, Rgba col, float scale, float alpha = 1f)
    {
        C.SetDrawColor(col.R, col.G, col.B, (byte)Math.Clamp(col.A * alpha * Alpha, 0f, 255f));
        C.SetPos(MathF.Round(x), MathF.Round(y), 0f);
        float size = scale * Scale;
        C.DrawText(text, false, size, size, out _);
    }

    public static void TextMid(float x, float cy, string text, Rgba col, float scale, float alpha = 1f) =>
        Text(x, cy - TextH(scale) * 0.5f, text, col, scale, alpha);

    public static float TextRight(float right, float cy, string text, Rgba col, float scale, float alpha = 1f)
    {
        float w = TextW(text, scale);
        TextMid(right - w, cy, text, col, scale, alpha);
        return w;
    }

    public static void TextCentre(float cx, float cy, string text, Rgba col, float scale, float alpha = 1f) =>
        TextMid(cx - TextW(text, scale) * 0.5f, cy, text, col, scale, alpha);

    // Letter spaced text for titles.
    public static float Spaced(float x, float y, string text, Rgba col, float scale, float spacing, float alpha = 1f)
    {
        float start = x;
        foreach (char ch in text)
        {
            string letter = ch.ToString();
            Text(x, y, letter, col, scale, alpha);
            x += TextW(letter, scale) + S(spacing);
        }

        return x - start - S(spacing);
    }

    public static float SpacedW(string text, float scale, float spacing)
    {
        float w = 0f;
        foreach (char ch in text)
            w += TextW(ch.ToString(), scale) + S(spacing);
        return w - S(spacing);
    }

    // As much of the text as fits the width on one line, breaking at a space. Returns the rest.
    public static string FitLine(string text, float width, float scale, out string line)
    {
        if (TextW(text, scale) <= width)
        {
            line = text;
            return "";
        }

        int cut = text.Length;
        while (cut > 0)
        {
            int space = text.LastIndexOf(' ', cut - 1);
            if (space <= 0)
                break;

            cut = space;
            if (TextW(text[..cut], scale) <= width)
                break;
        }

        line = text[..cut];
        return text[cut..].TrimStart();
    }

    // Text wrapped into the width, at most so many lines. The last line gets dots if it ran out.
    public static float Wrap(float x, float y, string text, float width, float scale, float lineH, int most, Rgba col, float alpha = 1f)
    {
        string rest = text;
        int lines = 0;
        while (rest.Length > 0 && lines < most)
        {
            rest = FitLine(rest, width, scale, out string line);
            if (rest.Length > 0 && lines == most - 1)
                line = line.TrimEnd('.', ',') + "...";
            Text(x, y, line, col, scale, alpha);
            y += lineH;
            lines++;
        }

        return y;
    }

    public static float Follow(float value, float target, float rate) =>
        value < 0f ? target : value + (target - value) * Ease.Follow(rate, Dt);

    private static float[] BuildLinear()
    {
        var table = new float[256];
        for (int i = 0; i < table.Length; i++)
            table[i] = MathF.Pow(i / 255f, 2.2f);
        return table;
    }
}

internal readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba Mix(Rgba a, Rgba b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgba((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
    }

    public Rgba Dim(float f) => new((byte)(R * f), (byte)(G * f), (byte)(B * f), A);
}

internal static class Palette
{
    private static readonly Rgba[] Phosphors =
    {
        new(96, 204, 244),
        new(118, 255, 136),
        new(255, 186, 56),
        new(232, 240, 236),
    };

    private static readonly Rgba ScreenBase = new(5, 7, 8, 236);
    private const float ScreenTint = 0.04f;

    public static Rgba Hud { get; private set; } = Phosphors[0];
    public static Rgba Text { get; private set; } = Phosphors[0].Dim(0.86f);
    public static Rgba Dim { get; private set; } = Phosphors[0].Dim(0.55f);
    public static Rgba Faint { get; private set; } = Phosphors[0].Dim(0.3f);
    public static Rgba Screen { get; private set; } = Rgba.Mix(ScreenBase, Phosphors[0] with { A = 236 }, ScreenTint);

    public static readonly Rgba Warn = new(255, 84, 70);
    public static readonly Rgba Bezel = new(22, 25, 27, 250);
    public static readonly Rgba BezelEdge = new(58, 64, 68);
    public static readonly Rgba Black = new(0, 0, 0);
    public static readonly Rgba White = new(255, 255, 255);

    // Amber, except on an amber HUD where it wouldn't stand out.
    public static Rgba Caution => Opt.HudColour.Index == 2 ? new Rgba(240, 244, 240) : new Rgba(255, 186, 56);

    private static int _index = -1;

    public static void Update()
    {
        int index = Math.Clamp(Opt.HudColour.Index, 0, Phosphors.Length - 1);
        if (index == _index)
            return;

        _index = index;
        Rgba p = Phosphors[index];
        Hud = p;
        Text = p.Dim(0.86f);
        Dim = p.Dim(0.55f);
        Faint = p.Dim(0.3f);
        Screen = Rgba.Mix(ScreenBase, p with { A = 236 }, ScreenTint);
    }
}
