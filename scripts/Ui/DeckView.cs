using static Trinity.Batwing.Gfx;

namespace Trinity.Batwing;

// The deck on the screen, a cockpit display in a bezel near the bottom.
internal static class DeckView
{
    private const float PanelW = 820f;
    private const float PanelH = 372f;
    private const float Bottom = 64f;
    private const float Bezel = 13f;
    private const float HeadH = 32f;
    private const float TabW = 176f;
    private const float SideW = 128f;
    private const float FootH = 78f;
    private const float RowH = 31f;
    private const float Inset = 14f;
    private const float ValueW = 236f;
    private const float Margin = 16f;

    private const float TitleScale = 0.64f;
    private const float LabelScale = 0.5f;
    private const float SmallScale = 0.42f;
    private const float HelpScale = 0.48f;
    private const float BrandScale = 0.58f;

    // Players found the deck too small at 1.0x. The base is baked in here so saved settings pick it up too.
    private const float BaseSize = 1.25f;

    private static readonly (float X, float Y)[] Silhouette =
    {
        (0f, -0.78f), (0.16f, -0.34f), (1f, 0.04f), (0.74f, 0.3f), (0.5f, 0.2f), (0.3f, 0.46f), (0.12f, 0.34f), (0f, 0.6f),
        (-0.12f, 0.34f), (-0.3f, 0.46f), (-0.5f, 0.2f), (-0.74f, 0.3f), (-1f, 0.04f), (-0.16f, -0.34f),
    };

    private static float _size = BaseSize;
    private static int _scroll;
    private static Page? _scrollPage;

    private static float U(float v) => S(v) * _size;

    private static float Sc(float s) => s * _size;

    private static bool Fx => Opt.Effects.On;

    // Shrinks to fit a narrow or short screen, whatever the size setting asks for.
    private static float FitSize()
    {
        float want = BaseSize * Opt.DeckSize.Number;
        float fitW = (W - S(Margin) * 2f) / S(PanelW + Bezel * 2f);
        float fitH = (H - S(Bottom) - S(Margin)) / S(PanelH + Bezel * 2f);
        return MathF.Max(0.5f, MathF.Min(want, MathF.Min(fitW, fitH)));
    }

    public static void Draw(float anim)
    {
        _size = FitSize();
        float ease = Ease.Out(anim);
        Alpha = ease;
        float w = U(PanelW), h = U(PanelH);
        float x = MathF.Round((W - w) * 0.5f);
        float y = MathF.Round(H - S(Bottom) - h + U(26) * (1f - ease));
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

        Shapes.Under = Palette.Black;
        Alpha = 1f;
    }

    private static void Header(float x, float y, float w, float head)
    {
        float cy = y + head * 0.5f + U(1);
        float pad = U(Inset);
        float bw = Spaced(x + pad, cy - TextH(Sc(BrandScale)) * 0.5f, "BATWING", Palette.Hud, Sc(BrandScale), 2.4f);
        TextMid(x + pad + bw + U(12), cy + U(1), "FLIGHT DECK", Palette.Dim, Sc(SmallScale));

        string title = Decode.Text(Deck.Current.Title, (float)(Time - Deck.PageChangedAt));
        TextCentre(x + w * 0.5f, cy, title, Palette.Text, Sc(TitleScale));

        float sw = TextRight(x + w - pad, cy + U(1), Wing.Status, Palette.Dim, Sc(SmallScale));
        float lx = x + w - pad - sw - U(14);
        Shapes.Disc(lx, cy, U(3.5f), Wing.Lamp, 1f);
        if (Fx && Wing.LampBlink)
            Shapes.Ring(lx, cy, U(5f), U(6f), Wing.Lamp, 0.3f + 0.3f * MathF.Sin((float)Time * 6f));

        Rect(x + pad, y + head - 1f, w - pad * 2f, 1f, Palette.Dim, 0.6f);
    }

    private static void Tabs(float x, float top, float bodyH)
    {
        float pad = U(Inset);
        float rowH = MathF.Min(U(38f), (bodyH - U(14f)) / Opt.Pages.Count);
        float ty = top + U(8f);
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

        // Pages can hold more rows than fit, so it scrolls to keep the selected one in view.
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
            float was = Alpha;
            Alpha = was * (Fx ? Ease.Out(Math.Clamp((age - k * 0.03f) / 0.18f, 0f, 1f)) : 1f);

            if (selected)
            {
                Rect(left - U(6), ry + U(1), right - left + U(12), rowH - U(2), Palette.Hud, 0.09f);
                Rect(left - U(6), ry + U(1), U(2), rowH - U(2), Palette.Hud, 0.9f);
            }

            Rgba labelCol = selected ? Palette.Hud : Palette.Text;
            string label = Decode.Text(o.LabelNow, age - k * 0.03f);
            if (o is Command)
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
            if (!o.IsDefault && o is not Command)
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
        float s = Sc(SmallScale);
        switch (o)
        {
            case Switch sw:
                Rocker(x1 - U(96), cy, sw.On, col);
                break;
            case Pick p:
                TextRight(x1 - U(14), cy + U(1), p.Value, col, Sc(LabelScale));
                TextMid(x1 - U(10), cy + U(1), ">", selected ? Palette.Hud : Palette.Dim, Sc(LabelScale));
                TextRight(x1 - U(18) - TextW(p.Value, Sc(LabelScale)), cy + U(1), "<", selected ? Palette.Hud : Palette.Dim, Sc(LabelScale));
                break;
            case Dial d:
                float bw = x1 - x0 - U(78);
                Rect(x0, cy - U(2), bw, U(4), Palette.Faint, 0.9f);
                Rect(x0, cy - U(2), bw * d.Fraction, U(4), col, 1f);
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
                    TextRight(x1, cy + U(1), Deck.Swapped ?? "PRESS A KEY", Palette.Caution, s, blink ? 1f : 0.45f);
                    break;
                }

                Glyphs.Cap(x1 - Glyphs.CapWidth(b.Value, s), cy, b.Value, s, col);
                break;
            case PadBind pb:
                if (Deck.Binding == pb)
                {
                    TextRight(x1, cy + U(1), Deck.Swapped ?? "PRESS A BUTTON", Palette.Caution, s, blink ? 1f : 0.45f);
                    break;
                }

                Glyphs.Draw(x1 - Glyphs.Width(pb.Button, s), cy, pb.Button, s);
                break;
            case Command a:
                if (Deck.Confirming == a)
                {
                    TextRight(x1, cy + U(1), "PRESS AGAIN TO CONFIRM", Palette.Caution, s, blink ? 1f : 0.5f);
                    Rect(x0, cy + U(10), (x1 - x0) * Deck.ConfirmLeft, U(1.5f), Palette.Caution, 0.8f);
                    break;
                }

                if (a.Value.Length > 0)
                    TextRight(x1, cy + U(1), a.Value, selected ? Palette.Hud : Palette.Dim, s);
                break;
        }
    }

    private static void Rocker(float x, float cy, bool on, Rgba col)
    {
        float cw = U(44), ch = U(18);
        float y = cy - ch * 0.5f;
        Outline(x, y, cw * 2f, ch, Palette.Dim, 0.8f, S(1));
        Rect((on ? x + cw : x) + 1f, y + 1f, cw - 2f, ch - 2f, col, 0.85f);
        TextCentre(x + cw * 0.5f, cy + U(1), "OFF", on ? Palette.Dim : Palette.Screen, Sc(SmallScale));
        TextCentre(x + cw * 1.5f, cy + U(1), "ON", on ? Palette.Screen : Palette.Dim, Sc(SmallScale));
    }

    // While it flies, an attitude ball and readouts. Otherwise the outline of the Batwing and how to call it.
    private static void Side(float x, float top, float w, float bodyH)
    {
        Rect(x, top + U(6), 1f, bodyH - U(12), Palette.Dim, 0.5f);
        float cx = x + w * 0.5f;
        float r = U(40f);
        float cy = top + U(16) + r;
        if (Wing.Live && Wing.Craft is { } craft)
        {
            Attitude(cx, cy, r, craft.Fm.Pitch() * Geo.Deg, craft.Fm.Bank());
            float y = cy + r + U(14);
            string speed = Hud.Speed(craft.Fm.Velocity.Length(), out string unit);
            Line(x, ref y, w, "SPD", speed + " " + unit);
            Line(x, ref y, w, "ALT", craft.Below.Known ? Fmt.Metres(craft.Below.Distance) : "---");
            Line(x, ref y, w, "THR", Fmt.Percent(craft.Fm.Hover > 0.5f ? Math.Clamp(craft.Fm.Jets / 2f, 0f, 1f) : craft.Fm.Throttle));
            Line(x, ref y, w, "HULL", Fmt.Percent(Pilot.Current?.Hull ?? 1f));
            Line(x, ref y, w, "G", Fmt.Tenths(craft.Fm.GLoad));
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
        float ty = cy + r + U(14);
        TextCentre(cx, ty, Decode.Text(Wing.Status, (float)(Time - Deck.OpenedAt)), Palette.Dim, Sc(SmallScale));
        TextCentre(cx, ty + U(20), "CALL", Palette.Faint, Sc(SmallScale));
        Prompt call = Prompt.Of(Act.Call);
        float s = Sc(SmallScale);
        call.Draw(cx - call.Width(s) * 0.5f, ty + U(40), s);
    }

    private static void Line(float x, ref float y, float w, string label, string value)
    {
        float pad = U(10), lh = U(17);
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
        Shapes.FlushLines(Palette.Caution);
        Shapes.Disc(cx, cy, U(1.8f), Palette.Caution, 1f);
        Shapes.Under = under;
    }

    private static void Footer(float x, float y, float w, float foot)
    {
        float pad = U(Inset);
        Rect(x + pad, y, w - pad * 2f, 1f, Palette.Dim, 0.6f);
        float s = Sc(SmallScale);
        float hs = Sc(HelpScale);
        string help = Deck.Binding switch
        {
            Bind b => "PRESS THE KEY FOR " + b.Label + ". BACKSPACE CANCELS. A KEY ALREADY IN USE SWAPS OVER.",
            PadBind p => "PRESS THE BUTTON FOR " + p.Label + ". " + Glyphs.Name(PadButton.Start) + " CANCELS. A BUTTON ALREADY IN USE SWAPS OVER.",
            _ => Deck.Swapped is { } swapped && Time - Deck.FlashAt < 4.0 ? swapped + "." : Deck.Level == 0 ? Deck.Current.Blurb : Deck.Selected?.Help ?? "",
        };
        Wrap(x + pad, y + U(8), help, w - pad * 2f, hs, TextH(hs) + U(2), 2, Palette.Dim);

        float cy = y + foot - U(15);
        var hints = new List<(Prompt Key, string Does)>();
        if (Controls.PadActive)
        {
            hints.Add((Prompt.Keys("", "D-PAD"), Deck.Level == 0 ? "PAGE" : "ROW AND ADJUST"));
            hints.Add((Prompt.Both("", PadButton.A), "SELECT"));
            hints.Add((Prompt.Both("", PadButton.B), Deck.Level == 0 ? "CLOSE" : "PAGES"));
            hints.Add((Prompt.Keys("", Glyphs.Name(PadButton.LB) + " " + Glyphs.Name(PadButton.RB)), "PAGE"));
            hints.Add((Prompt.Both("", PadButton.X), "RESET"));
        }
        else
        {
            hints.Add((Prompt.Keys("UP DOWN", ""), Deck.Level == 0 ? "PAGE" : "ROW"));
            if (Deck.Level == 1)
                hints.Add((Prompt.Keys("LEFT RIGHT", ""), "ADJUST"));
            hints.Add((Prompt.Keys("ENTER", ""), "SELECT"));
            hints.Add((Prompt.Keys("BACKSPACE", ""), Deck.Level == 0 ? "CLOSE" : "PAGES"));
            hints.Add((Prompt.Keys("DEL", ""), "RESET"));
            hints.Add((Prompt.Of(Act.Deck), "CLOSE"));
        }

        float hx = x + pad;
        foreach ((Prompt key, string does) in hints)
        {
            float cw = key.Draw(hx, cy, s);
            TextMid(hx + cw + U(6), cy + U(1), does, Palette.Faint, s);
            hx += cw + U(6) + TextW(does, s) + U(16);
        }

        TextRight(x + w - pad, cy + U(1), "V" + App.Version, Palette.Faint, s);
    }
}
