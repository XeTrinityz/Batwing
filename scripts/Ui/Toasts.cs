using static Trinity.Batwing.Gfx;

namespace Trinity.Batwing;

// Short messages at the top of the screen. A notice holds longer and a toast doesn't cut it short.
internal static class Toasts
{
    private const string Title = "BATWING";
    private const float ToastSeconds = 1.8f;
    private const float NoticeSeconds = 3.4f;
    private const float FadeIn = 0.12f, FadeOut = 0.3f;

    private static float _left, _length = 1f;
    private static double _noticeUntil;
    private static string _text = "";
    private static bool _good;
    private static long _started;
    private static float _top = -1f;

    public static bool Showing => _left > 0f;

    public static void Show(string text, bool good)
    {
        if (_noticeUntil <= Clock.Now)
            Put(text, good, ToastSeconds);
    }

    public static void Notice(string text, bool good)
    {
        _noticeUntil = Clock.Now + NoticeSeconds;
        Put(text, good, NoticeSeconds);
    }

    private static void Put(string text, bool good, float seconds)
    {
        if (_left <= 0f)
        {
            _started = ToastLane.Now;
            _top = -1f;
        }

        _text = text;
        _good = good;
        _left = seconds;
        _length = seconds;
    }

    public static void Advance(float dt)
    {
        _left = Math.Max(0f, _left - dt);
        if (_left <= 0f)
            ToastLane.Clear(Shared.OwnerName);
    }

    public static void Draw()
    {
        if (_left <= 0f)
            return;

        float age = _length - _left;
        Alpha = Ease.Out(Math.Min(1f, _left / FadeOut) * Math.Min(1f, age / FadeIn));
        Rgba col = _good ? Palette.Hud : Palette.Caution;
        const float ts = 0.56f, ss = 0.48f;
        string text = Decode.Text(_text, age);
        float tw = SpacedW(Title, ts, 2f);
        float mw = TextW(_text, ss);
        float pad = S(18);
        float w = pad + tw + S(22) + mw + pad;
        float h = S(34);
        float x = MathF.Round((W - w) * 0.5f);

        // Slides rather than jumps when a toast above it goes away.
        float want = ToastLane.Top(Shared.OwnerName, _started, S(118), S(8));
        _top = _top < 0f ? want : _top + (want - _top) * Math.Min(1f, Dt * 12f);
        float y = MathF.Round(_top - S(8) * (1f - Alpha));
        ToastLane.Post(Shared.OwnerName, _started, y + h);

        Rect(x, y, w, h, Palette.Screen, 0.9f);
        Rect(x, y + h - MathF.Max(1f, S(2)), w * Math.Clamp(_left / _length, 0f, 1f), MathF.Max(1f, S(2)), col, 0.9f);
        Rect(x, y, MathF.Max(1f, S(3)), h, col, 0.9f);
        Spaced(x + pad, y + (h - TextH(ts)) * 0.5f + S(1), Title, col, ts, 2f);
        Rect(x + pad + tw + S(10), y + S(9), 1f, h - S(18), Palette.Dim, 0.8f);
        TextMid(x + pad + tw + S(22), y + h * 0.5f + S(1), text, Palette.Text, ss);
        Alpha = 1f;
    }
}

// Text that types itself in with a scramble, for headings as they change.
internal static class Decode
{
    private const float Seconds = 0.32f;
    private const string Glyphs = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789#%/<>";

    public static string Text(string text, float age)
    {
        if (!Opt.Effects.On || age >= Seconds || text.Length == 0)
            return text;

        int resolved = Math.Max(0, (int)(text.Length * (age / Seconds)));
        char[] c = text.ToCharArray();
        int tick = (int)(Gfx.Time * 28.0);
        for (int i = resolved; i < c.Length; i++)
        {
            if (c[i] != ' ')
                c[i] = Glyphs[(int)((uint)(i * 7919 + tick * 104729 + text.Length * 31) % (uint)Glyphs.Length)];
        }

        return new string(c);
    }
}
