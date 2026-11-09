using static Trinity.Batwing.Gfx;

namespace Trinity.Batwing;

internal static class DeckView
{
    private const float PanelW = 800f;
    private const float PanelH = 360f;
    private const float Bottom = 64f;
    private const float Bezel = 13f;
    private const float HeadH = 32f;
    private const float TabW = 176f;
    private const float SideW = 124f;
    private const float FootH = 74f;
    private const float RowH = 31f;

    private static int _scroll;
    private static Page? _scrollPage;
    private const float Inset = 14f;
    private const float ValueW = 236f;

    private const float TitleScale = 0.64f;
    private const float LabelScale = 0.5f;
    private const float SmallScale = 0.42f;
    private const float HelpScale = 0.48f;
    private const float BrandScale = 0.58f;

    private const float DecodeSeconds = 0.32f;
    private const string Glyphs = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789#%/<>";

    private static readonly (float X, float Y)[] Silhouette =
    {
        (0f, -0.78f), (0.16f, -0.34f), (1f, 0.04f), (0.74f, 0.3f), (0.5f, 0.2f), (0.3f, 0.46f), (0.12f, 0.34f), (0f, 0.6f),
        (-0.12f, 0.34f), (-0.3f, 0.46f), (-0.5f, 0.2f), (-0.74f, 0.3f), (-1f, 0.04f), (-0.16f, -0.34f),
    };

    // Players found the deck too small at 1.0x. The base is baked in here so saved settings pick it up too.
    private const float BaseSize = 1.25f;
    private const float Margin = 16f;

    private static float _size = BaseSize;

    private static float U(float v) => S(v) * _size;

    private static float Sc(float s) => s * _size;

    // Shrinks the deck to fit on narrow or short screens, whatever the size setting asks for.
    private static float FitSize()
    {
        float want = BaseSize * Opt.DeckSize.Number;
        float fitW = (Gfx.W - S(Margin) * 2f) / S(PanelW + Bezel * 2f);
        float fitH = (Gfx.H - S(Bottom) - S(Margin)) / S(PanelH + Bezel * 2f);
        return MathF.Max(0.5f, MathF.Min(want, MathF.Min(fitW, fitH)));
    }

    private static bool Fx => Opt.Effects.On;

    public static string Decode(string text, float age)
    {
        if (!Fx || age >= DecodeSeconds || text.Length == 0)
            return text;

        int resolved = Math.Max(0, (int)(text.Length * (age / DecodeSeconds)));
        char[] c = text.ToCharArray();
        int tick = (int)(Time * 28.0);
        for (int i = resolved; i < c.Length; i++)
        {
            if (c[i] != ' ')
                c[i] = Glyphs[(int)((uint)(i * 7919 + tick * 104729 + text.Length * 31) % (uint)Glyphs.Length)];
        }

        return new string(c);
    }

    public static void Draw(float anim)
    {
        _size = FitSize();
        float ease = Ease.Out(anim);
        Alpha = ease;
        float w = U(PanelW), h = U(PanelH);
        float x = MathF.Round((Gfx.W - w) * 0.5f);
        float y = MathF.Round(Gfx.H - S(Bottom) - h + U(26) * (1f - ease));
        float b = U(Bezel);

        Rect(x - b + U(4), y - b + U(5), w + b * 2f, h + b * 2f, Palette.Black, 0.35f);
        Rect(x - b, y - b, w + b * 2f, h + b * 2f, Palette.Bezel, 1f);
        Outline(x - b, y - b, w + b * 2f, h + b * 2f, Palette.BezelEdge, 0.9f, S(1));
        Shapes.Under = Palette.Bezel;
        float sr = U(2.2f), si = U(6f);
        Shapes.Disc(x - b + si, y - b + si, sr, Palette.BezelEdge, 1f);
        Shapes.Disc(x + w + b - si, y - b + si, sr, Palette.BezelEdge, 1f);
        Shapes.Disc(x - b + si, y + h + b - si, sr, Palette.BezelEdge, 1f);
        Shapes.Disc(x + w + b - si, y + h + b - si, sr, Palette.BezelEdge, 1f);

        Rect(x, y, w, h, Palette.Screen, 1f);
        Shapes.Under = Palette.Screen;
        if (Fx)
        {
            Outline(x, y, w, h, Palette.Hud, 0.28f, S(1));
            Outline(x + S(2), y + S(2), w - S(4), h - S(4), Palette.Hud, 0.07f, S(1));
        }
        else
        {
            Outline(x, y, w, h, Palette.Dim, 0.6f, S(1));
        }

        float head = U(HeadH), foot = U(FootH);
        float top = y + head, bodyH = h - head - foot;
        Header(x, y, w, head);
        Tabs(x, top, bodyH);
        Rows(x + U(TabW), top, w - U(TabW) - U(SideW), bodyH);
        Side(x + w - U(SideW), top, U(SideW), bodyH);
        Footer(x, y + h - foot, w, foot);

        if (Fx)
        {
            float step = MathF.Max(2f, U(3f));
            for (float sy = y + 1f; sy < y + h - 1f; sy += step)
                Rect(x + 1f, sy, w - 2f, 1f, Palette.Black, 0.2f);
        }

        Alpha = 1f;
    }

    private static void Header(float x, float y, float w, float head)
    {
        float cy = y + head * 0.5f + U(1);
        float pad = U(Inset);
        float bw = Spaced(x + pad, cy - TextH(Sc(BrandScale)) * 0.5f, "BATWING", Palette.Hud, Sc(BrandScale), 2.4f);
        TextMid(x + pad + bw + U(12), cy + U(1), "FLIGHT DECK", Palette.Dim, Sc(SmallScale));

        float age = (float)(Time - Deck.PageChangedAt);
        string title = Decode(Deck.Current.Title, age);
        TextCentre(x + w * 0.5f, cy, title, Palette.Text, Sc(TitleScale));

        string state = Wing.I.StateLabel;
        Rgba lamp = Wing.I.Lamp;
        float sw = TextRight(x + w - pad, cy + U(1), state, Palette.Dim, Sc(SmallScale));
        float lx = x + w - pad - sw - U(14);
        Shapes.Disc(lx, cy, U(3.5f), lamp, 1f);
        if (Fx && Wing.I.LampBlink)
            Shapes.Ring(lx, cy, U(5f), U(6f), lamp, 0.3f + 0.3f * MathF.Sin((float)Time * 6f));

        Rect(x + pad, y + head - 1f, w - pad * 2f, 1f, Palette.Dim, 0.6f);
    }

    private static void Tabs(float x, float top, float bodyH)
    {
        float pad = U(Inset);
        float rowH = U(38f);
        float ty = top + U(10f);
        for (int i = 0; i < Opt.Pages.Count; i++)
        {
            Page page = Opt.Pages[i];
            bool current = i == Deck.PageIndex;
            bool focus = current && Deck.Level == 0;
            float cy = ty + rowH * 0.5f;

            if (focus)
                Rect(x + U(6), ty + U(3), U(TabW) - U(12), rowH - U(6), Palette.Hud, 0.92f);
            else if (current)
                Rect(x + U(6), ty + U(3), U(TabW) - U(12), rowH - U(6), Palette.Hud, 0.08f);

            Rgba ink = focus ? Palette.Screen : current ? Palette.Hud : Palette.Dim;
            float box = U(9f);
            Outline(x + pad, cy - box * 0.5f, box, box, ink, focus ? 1f : 0.8f, S(1));
            if (current)
                Rect(x + pad + U(2.5f), cy - box * 0.5f + U(2.5f), box - U(5f), box - U(5f), ink, 1f);
            TextMid(x + pad + box + U(10), cy + U(1), page.Title, ink, Sc(LabelScale));
            TextRight(x + U(TabW) - pad, cy + U(1), "0" + (i + 1), focus ? Palette.Screen : Palette.Faint, Sc(SmallScale));
            ty += rowH;
        }

        Rect(x + U(TabW), top + U(6), 1f, bodyH - U(12), Palette.Dim, 0.5f);
    }

    private static void Rows(float x, float top, float w, float bodyH)
    {
        float pad = U(Inset);
        float left = x + pad;
        float right = x + w - pad;
        float rowH = U(RowH);
        float ry = top + U(8f);
        float age = (float)(Time - Deck.PageChangedAt);
        List<Option> rows = Deck.Current.Options;

        // Pages can hold more rows than fit, so scroll to keep the selected one in view.
        int fit = Math.Max(1, (int)((bodyH - U(8f)) / rowH));
        if (!ReferenceEquals(_scrollPage, Deck.Current))
        {
            _scrollPage = Deck.Current;
            _scroll = 0;
        }

        if (Deck.Level == 1)
        {
            if (Deck.RowIndex < _scroll)
                _scroll = Deck.RowIndex;
            else if (Deck.RowIndex >= _scroll + fit)
                _scroll = Deck.RowIndex - fit + 1;
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, rows.Count - fit));
        if (rows.Count > fit)
        {
            float track = fit * rowH;
            float thumb = MathF.Max(U(12f), track * fit / rows.Count);
            float at = (track - thumb) * _scroll / (rows.Count - fit);
            Rect(x + w - U(4f), ry, U(2f), track, Palette.Dim, 0.35f);
            Rect(x + w - U(4f), ry + at, U(2f), thumb, Palette.Hud, 0.9f);
        }

        for (int k = 0; k < fit && _scroll + k < rows.Count; k++)
        {
            int j = _scroll + k;
            Option o = rows[j];
            bool selected = Deck.Level == 1 && j == Deck.RowIndex;
            float cy = ry + rowH * 0.5f;
            float cascade = Fx ? Ease.Out(Math.Clamp((age - k * 0.03f) / 0.18f, 0f, 1f)) : 1f;
            float was = Alpha;
            Alpha = was * cascade;

            if (selected)
            {
                Rect(left - U(6), ry + U(1), right - left + U(12), rowH - U(2), Palette.Hud, 0.09f);
                Rect(left - U(6), ry + U(1), U(2), rowH - U(2), Palette.Hud, 0.9f);
            }

            Rgba labelCol = selected ? Palette.Hud : Palette.Text;
            string label = Decode(o.LabelNow, age - k * 0.03f);
            if (o is Act)
            {
                float bw = TextW(label, Sc(LabelScale)) + U(18);
                Outline(left + U(6), cy - U(10), bw, U(20), labelCol, selected ? 0.9f : 0.45f, S(1));
                TextMid(left + U(15), cy + U(1), label, labelCol, Sc(LabelScale));
            }
            else
            {
                TextMid(left + U(10), cy + U(1), label, labelCol, Sc(LabelScale));
            }

            Value(o, right - U(ValueW), right, cy, selected);
            if (!o.IsDefault && o is not Act)
                Rect(right + U(5), cy - U(1.5f), U(3), U(3), Palette.Dim, 0.9f);

            Alpha = was;
            ry += rowH;
        }
    }

    private static void Value(Option o, float x0, float x1, float cy, bool selected)
    {
        bool flash = Deck.Flashed == o && Time - Deck.FlashAt < 0.45;
        Rgba col = flash ? Palette.White : selected ? Palette.Hud : Palette.Text;
        bool blink = (int)(Time * 3.0) % 2 == 0;
        switch (o)
        {
            case Switch s:
                Rocker(x1 - U(96), cy, s.On, col);
                break;
            case Pick p:
                TextRight(x1 - U(14), cy + U(1), p.Value, col, Sc(LabelScale));
                TextMid(x1 - U(10), cy + U(1), ">", selected ? Palette.Hud : Palette.Dim, Sc(LabelScale));
                float pw = TextW(p.Value, Sc(LabelScale));
                TextRight(x1 - U(18) - pw, cy + U(1), "<", selected ? Palette.Hud : Palette.Dim, Sc(LabelScale));
                break;
            case Dial d:
                float bw = x1 - x0 - U(78);
                float by = cy - U(2);
                Rect(x0, by, bw, U(4), Palette.Faint, 0.9f);
                Rect(x0, by, bw * d.Fraction, U(4), col, 1f);
                for (int k = 0; k <= 4; k++)
                    Rect(MathF.Round(x0 + bw * k / 4f) - 0.5f, cy - U(6), 1f, U(12), Palette.Dim, 0.9f);
                Rect(MathF.Round(x0 + bw * d.Fraction) - U(1), cy - U(7), U(2), U(14), col, 1f);
                if (!d.IsDefault)
                    Rect(MathF.Round(x0 + bw * d.DefaultFraction) - U(1.5f), cy + U(9), U(3), U(3), Palette.Dim, 0.9f);
                TextRight(x1, cy + U(1), d.Value, col, Sc(LabelScale));
                break;
            case Bind b:
                if (Deck.Binding == b)
                {
                    string prompt = Deck.BindRefused ? "THAT KEY IS TAKEN" : "PRESS A KEY";
                    TextRight(x1, cy + U(1), prompt, Palette.CautionNow, Sc(SmallScale), blink ? 1f : 0.45f);
                    break;
                }

                Chip(x1, cy, b.Value, col, true);
                break;
            case Act a:
                if (Deck.Confirming == a)
                {
                    TextRight(x1, cy + U(1), "PRESS AGAIN TO CONFIRM", Palette.CautionNow, Sc(SmallScale), blink ? 1f : 0.5f);
                    Rect(x0, cy + U(10), (x1 - x0) * Deck.ConfirmLeft, U(1.5f), Palette.CautionNow, 0.8f);
                    break;
                }

                string note = a.Value;
                if (note.Length > 0)
                    TextRight(x1, cy + U(1), note, selected ? Palette.Hud : Palette.Dim, Sc(SmallScale));
                break;
        }
    }

    private static void Rocker(float x, float cy, bool on, Rgba col)
    {
        float cw = U(44), ch = U(18);
        float y = cy - ch * 0.5f;
        Outline(x, y, cw * 2f, ch, Palette.Dim, 0.8f, S(1));
        float lit = on ? x + cw : x;
        Rect(lit + 1f, y + 1f, cw - 2f, ch - 2f, col, 0.85f);
        TextCentre(x + cw * 0.5f, cy + U(1), "OFF", on ? Palette.Dim : Palette.Screen, Sc(SmallScale));
        TextCentre(x + cw * 1.5f, cy + U(1), "ON", on ? Palette.Screen : Palette.Dim, Sc(SmallScale));
    }

    public static float Chip(float right, float cy, string text, Rgba col, bool alignRight)
    {
        float s = Sc(SmallScale);
        float w = TextW(text, s) + U(12);
        float h = TextH(s) + U(5);
        float x = alignRight ? right - w : right;
        Rect(x, cy - h * 0.5f, w, h, col, 0.1f);
        Outline(x, cy - h * 0.5f, w, h, col, 0.55f, S(1));
        TextMid(x + U(6), cy + U(1), text, col, s);
        return w;
    }

    private static void Side(float x, float top, float w, float bodyH)
    {
        Rect(x, top + U(6), 1f, bodyH - U(12), Palette.Dim, 0.5f);
        float cx = x + w * 0.5f;
        float r = U(40f);
        float cy = top + U(16) + r;
        if (Wing.I.CraftLive)
        {
            Attitude(cx, cy, r, Wing.I.PitchDegrees, Wing.I.BankRadians);
            float ty = cy + r + U(14);
            Readouts(x, ty, w);
            return;
        }

        float sw = r * 0.95f;
        for (int i = 0; i < Silhouette.Length; i++)
        {
            (float ax, float ay) = Silhouette[i];
            (float bx, float by) = Silhouette[(i + 1) % Silhouette.Length];
            Shapes.QueueLine(cx + ax * sw, cy + ay * sw * 0.9f, cx + bx * sw, cy + by * sw * 0.9f, MathF.Max(1f, U(1.4f)));
        }

        Shapes.FlushLines(Palette.Dim);
        Shapes.Disc(cx, cy - sw * 0.2f, U(2f), Palette.Hud, 1f);
        float ty2 = cy + r + U(14);
        string standby = Wing.I.StateLabel;
        TextCentre(cx, ty2, Decode(standby, (float)(Time - Deck.OpenedAt)), Palette.Dim, Sc(SmallScale));
        TextCentre(cx, ty2 + U(20), "CALL", Palette.Faint, Sc(SmallScale));
        Chip(cx - TextW(Opt.CallKey.Value, Sc(SmallScale)) * 0.5f - U(6), ty2 + U(38), Opt.CallKey.Value, Palette.Dim, false);
    }

    private static void Readouts(float x, float y, float w)
    {
        float pad = U(10);
        float lh = U(17);
        Line(x, ref y, w, pad, lh, "SPD", Wing.I.SpeedText);
        Line(x, ref y, w, pad, lh, "ALT", Wing.I.AltitudeText);
        Line(x, ref y, w, pad, lh, "THR", Fmt.Percent(Wing.I.ThrottleFraction));
        Line(x, ref y, w, pad, lh, "HULL", Fmt.Percent(Wing.I.HullFraction));
        Line(x, ref y, w, pad, lh, "G", Fmt.Tenths(Wing.I.GLoad));
    }

    private static void Line(float x, ref float y, float w, float pad, float lh, string label, string value)
    {
        TextMid(x + pad, y + lh * 0.5f, label, Palette.Faint, Sc(SmallScale));
        TextRight(x + w - pad, y + lh * 0.5f, value, Palette.Text, Sc(SmallScale));
        y += lh;
    }

    public static void Attitude(float cx, float cy, float r, float pitchDeg, float bankRad)
    {
        Rgba under = Shapes.Under;
        Shapes.Disc(cx, cy, r + U(2), Palette.Dim, 0.7f);
        Shapes.Disc(cx, cy, r, Palette.Faint, 0.3f);

        float hx = MathF.Cos(-bankRad), hy = MathF.Sin(-bankRad);
        float dx = -hy, dy = hx;
        float k = r / 45f;
        float off = Math.Clamp(pitchDeg * k, -r * 0.95f, r * 0.95f);

        float phi = MathF.Atan2(dy, dx) * Geo.Deg;
        float alpha = MathF.Acos(Math.Clamp(off / r, -1f, 1f)) * Geo.Deg;
        Rgba ground = Palette.Faint.Dim(0.5f);
        Shapes.Arc(cx, cy, 0f, r, phi - alpha, phi + alpha, ground, 0.9f);
        float half = MathF.Sqrt(MathF.Max(0f, r * r - off * off));
        float ex = cx + dx * off, ey = cy + dy * off;
        float ax = ex + hx * half, ay = ey + hy * half, bx = ex - hx * half, by = ey - hy * half;
        if (off > 0.5f)
            Shapes.Triangle(cx, cy, ax, ay, bx, by, Palette.Faint, 0.3f);
        else if (off < -0.5f)
            Shapes.Triangle(cx, cy, ax, ay, bx, by, ground, 0.9f);

        Shapes.QueueLine(ax, ay, bx, by, MathF.Max(1f, U(1.5f)));
        for (int p = -30; p <= 30; p += 10)
        {
            if (p == 0)
                continue;
            float o = off - p * k;
            if (MathF.Abs(o) > r * 0.85f)
                continue;
            float len = (p % 20 == 0 ? 0.42f : 0.26f) * r;
            float px = cx + dx * o, py = cy + dy * o;
            Shapes.QueueLine(px - hx * len, py - hy * len, px + hx * len, py + hy * len, MathF.Max(1f, U(1f)));
        }

        Shapes.FlushLines(Palette.Text);

        for (int b = -60; b <= 60; b += 30)
        {
            float a = (-90f + b) / Geo.Deg;
            float r0 = r + U(3), r1 = r + U(b == 0 ? 8 : 6);
            Shapes.QueueLine(cx + MathF.Cos(a) * r0, cy + MathF.Sin(a) * r0, cx + MathF.Cos(a) * r1, cy + MathF.Sin(a) * r1, MathF.Max(1f, U(1f)));
        }

        Shapes.FlushLines(Palette.Dim);
        float pa = (-90f - bankRad * Geo.Deg) / Geo.Deg;
        Shapes.Triangle(cx + MathF.Cos(pa) * (r - U(2)), cy + MathF.Sin(pa) * (r - U(2)),
            cx + MathF.Cos(pa - 0.08f) * (r - U(9)), cy + MathF.Sin(pa - 0.08f) * (r - U(9)),
            cx + MathF.Cos(pa + 0.08f) * (r - U(9)), cy + MathF.Sin(pa + 0.08f) * (r - U(9)), Palette.Hud, 1f);

        float t = MathF.Max(1.5f, U(2f));
        Shapes.QueueLine(cx - r * 0.72f, cy, cx - r * 0.22f, cy, t);
        Shapes.QueueLine(cx + r * 0.22f, cy, cx + r * 0.72f, cy, t);
        Shapes.QueueLine(cx - r * 0.22f, cy, cx - r * 0.22f, cy + r * 0.12f, t);
        Shapes.QueueLine(cx + r * 0.22f, cy, cx + r * 0.22f, cy + r * 0.12f, t);
        Shapes.FlushLines(Palette.CautionNow);
        Shapes.Disc(cx, cy, U(1.8f), Palette.CautionNow, 1f);
        Shapes.Under = under;
    }

    private static void Footer(float x, float y, float w, float foot)
    {
        float pad = U(Inset);
        Rect(x + pad, y, w - pad * 2f, 1f, Palette.Dim, 0.6f);
        float s = Sc(SmallScale);
        float hs = Sc(HelpScale);
        float ty = y + U(8);
        string help = Deck.Binding is { } b
            ? "PRESS THE KEY FOR " + b.Label + ". BACKSPACE CANCELS. THE DECK'S OWN KEYS AND THE MOUSE BUTTONS CAN'T BE USED."
            : Deck.Level == 0 ? Deck.Current.Blurb : Deck.Selected?.Help ?? "";
        float width = w - pad * 2f;
        string rest = FitLine(help, width, hs, out string line1);
        Text(x + pad, ty, line1, Palette.Dim, hs);
        if (rest.Length > 0)
        {
            string more = FitLine(rest, width, hs, out string line2);
            if (more.Length > 0 && line2.Length > 3)
                line2 = line2.Substring(0, line2.Length - 3) + "...";
            Text(x + pad, ty + TextH(hs) + U(2), line2, Palette.Dim, hs);
        }

        float cy = y + foot - U(14);
        var hints = new List<(string, string)>();
        if (Pad.InUse)
        {
            hints.Add(("D-PAD", Deck.Level == 0 ? "PAGE" : "ROW / ADJUST"));
            hints.Add(("A", "SELECT"));
            hints.Add(("B", Deck.Level == 0 ? "CLOSE" : "PAGES"));
            hints.Add(("LB RB", "PAGE"));
            hints.Add(("X", "RESET"));
        }
        else
        {
            hints.Add(("UP DOWN", Deck.Level == 0 ? "PAGE" : "ROW"));
            if (Deck.Level == 1)
                hints.Add(("LEFT RIGHT", "ADJUST"));
            hints.Add(("ENTER", "SELECT"));
            hints.Add(("BACKSPACE", Deck.Level == 0 ? "CLOSE" : "PAGES"));
            hints.Add(("DEL", "RESET"));
            hints.Add((Opt.DeckKey.Value, "CLOSE"));
        }

        float hx = x + pad;
        foreach ((string key, string action) in hints)
        {
            float cw = Chip(hx, cy, key, Palette.Dim, false);
            TextMid(hx + cw + U(6), cy + U(1), action, Palette.Faint, s);
            hx += cw + U(6) + TextW(action, s) + U(16);
        }

        TextRight(x + w - pad, cy + U(1), "V" + App.Version, Palette.Faint, s);
    }
}
