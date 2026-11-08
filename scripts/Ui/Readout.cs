using static Trinity.Batwing.Gfx;

namespace Trinity.Batwing;

internal static class Readout
{
    private const float ToastSeconds = 1.8f;
    private const float NoticeSeconds = 3.2f;

    private static float _left;
    private static float _length = 1f;
    private static double _noticeDue;
    private static string _title = "";
    private static string _text = "";
    private static bool _good;

    public static void Toast(string title, string text, bool good)
    {
        if (_noticeDue <= Clock.Now)
            Show(title, text, good, ToastSeconds);
    }

    public static void Notice(string title, string text, bool good)
    {
        _noticeDue = Clock.Now + NoticeSeconds;
        Show(title, text, good, NoticeSeconds);
    }

    private static void Show(string title, string text, bool good, float seconds)
    {
        _title = title;
        _text = text;
        _good = good;
        _left = seconds;
        _length = seconds;
    }

    public static bool Showing => _left > 0f;

    public static void Advance(float dt) => _left = Math.Max(0f, _left - dt);

    public static void Draw()
    {
        if (_left <= 0f)
            return;

        float age = _length - _left;
        Alpha = Ease.Out(Math.Min(1f, _left / 0.3f) * Math.Min(1f, age / 0.12f));
        Rgba col = _good ? Palette.Hud : Palette.CautionNow;
        float ts = 0.56f, ss = 0.46f;
        string text = DeckView.Decode(_text, age);
        float tw = SpacedW(_title, ts, 2f);
        float mw = TextW(_text, ss);
        float pad = S(18);
        float w = pad + tw + S(22) + mw + pad;
        float h = S(34);
        float x = MathF.Round((Gfx.W - w) * 0.5f);
        float y = MathF.Round(S(118) - S(8) * (1f - Alpha));

        Rect(x, y, w, h, Palette.Screen, 0.9f);
        Rect(x, y + h - MathF.Max(1f, S(2)), w, MathF.Max(1f, S(2)), col, 0.9f);
        Rect(x, y, MathF.Max(1f, S(3)), h, col, 0.9f);
        Spaced(x + pad, y + (h - TextH(ts)) * 0.5f + S(1), _title, col, ts, 2f);
        Rect(x + pad + tw + S(10), y + S(9), 1f, h - S(18), Palette.Dim, 0.8f);
        TextMid(x + pad + tw + S(22), y + h * 0.5f + S(1), text, Palette.Text, ss);
        Alpha = 1f;
    }
}
