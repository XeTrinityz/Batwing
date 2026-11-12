using static Trinity.Batwing.Gfx;

namespace Trinity.Batwing;

// The deck as it shows on the gauntlet's hologram. It is laid out in units, BaseWidth wide on the
// game's own screen. A bigger screen gets more units and smaller ones, so the text shrinks as the
// screen grows and the extra room goes to more rows and columns. The screen is tilted, its edges fade out and
// Batman's hand and forearm cover the bottom of it, the left more than the right. So everything
// sits well inside the edges, the left side stops early, the text is large and pale, and the keys
// are shown next to what they do rather than along the bottom.
internal static class GauntletView
{
    public const float BaseWidth = 400f;

    private const float StripMid = 13f;
    private const float StripH = 26f;
    private const float BodyTop = 34f;
    private const float RowH = 18f;
    private const float TileGap = 3f;

    // How much of the bottom the forearm covers, under the list and under the left half, and how
    // far in from the screen's fading edges everything sits. All in units of the game's own screen.
    private const float ListClear = 24f;
    private const float FocusClear = 58f;
    private const float LeftEdge = 18f;
    private const float RightEdge = 22f;

    private static float _ox, _oy, _font, _width;

    // How many of our units make one unit of the game's own screen.
    private static float _per = 1f;
    private static int _scroll;
    private static Page? _scrollPage;

    private static bool Fx => Opt.Effects.On;

    private const float Gutter = 7f;
    private const float PagesW = 86f;
    private const float StatusW = 108f;
    private const float ListMin = 156f;
    private const float ListMax = 200f;

    // Columns, worked out by Layout for the room this screen has.
    private static bool _pages, _status;
    private static float _focusLeft, _focusW, _listLeft, _listRight, _statusLeft;

    private static float Left => LeftEdge * _per;

    private static float Right => _width - RightEdge * _per;

    private static float FocusW => _focusW;

    private static float ListLeft => _listLeft;

    private static float LabelLeft => _listLeft + 13f;

    private static float PX(float u) => _ox + S(u);

    private static float PY(float u) => _oy + S(u);

    // The font scale for text this many units tall.
    private static float F(float tall) => tall * _font;

    private static float Hair => MathF.Max(1f, S(0.5f));

    // The hologram tints everything blue, so text is drawn close to white to stand out.
    private static Rgba Ink => Rgba.Mix(Palette.Hud, Palette.White, 0.75f);

    private static Rgba Soft => Rgba.Mix(Palette.Hud, Palette.White, 0.4f);

    private static string SelectKey => Pad.InUse ? "A" : "ENTER";

    // Size is how much bigger the screen is than the game's own, and per is how many units make
    // one of the game's own screen.
    public static void Draw(float ox, float oy, float zoom, float width, float height, float size, float per, float anim)
    {
        if (anim <= 0f)
            return;

        Scale = zoom;
        _ox = ox;
        _oy = oy;
        _width = width;
        _per = per;
        _font = Scale / TextH(1f);
        Layout();

        // A projection is never quite steady.
        float flicker = Fx ? 0.95f + 0.05f * MathF.Sin((float)Time * 9f) : 1f;
        Alpha = Ease.Out(anim) * flicker;

        Rect(PX(0f), PY(0f), S(_width), S(height), Palette.Hud, 0.08f);
        Strip();

        // A bigger screen grows as much downwards as up, and that part ends up behind the forearm too.
        float below = height * (size - 1f) / (2f * size);
        float listBottom = height - ListClear * per - below;
        float focusBottom = height - FocusClear * per - below;
        Rect(PX(_listLeft - Gutter), PY(BodyTop), Hair, S(listBottom - BodyTop), Palette.Dim, 0.5f);

        if (_pages)
            Pages(focusBottom);

        if (Deck.Level == 0 || Deck.Selected is not { } selected)
            PageFocus(focusBottom);
        else
            Focus(selected, focusBottom);

        List(listBottom);
        if (_status)
            Status(listBottom);
        Alpha = 1f;
    }

    // A bigger screen gets more columns rather than wider ones. Past a point a wider list only
    // pushes values away from their labels, which reads badly on the tilted screen. The page list
    // comes first, then a status column, and what is left goes to the selected setting's help.
    private static void Layout()
    {
        float x = Left;
        float right = Right;

        _pages = right - x >= 430f;
        if (_pages)
            x += PagesW + Gutter * 2f;

        _status = right - x >= 540f;
        if (_status)
        {
            _statusLeft = right - StatusW;
            right = _statusLeft - Gutter * 2f;
        }

        float rest = right - x;
        float list = Math.Clamp(rest * 0.45f, ListMin, ListMax);
        _focusLeft = x;
        _focusW = MathF.Round(rest - list - Gutter * 2f);
        _listLeft = right - list;
        _listRight = right;
    }

    // Thin font strokes vanish on the hologram, so headline text is drawn twice side by side.
    private static void Bold(float x, float y, string text, Rgba col, float scale)
    {
        Text(x, y, text, col, scale);
        Text(x + MathF.Max(1f, S(0.4f)), y, text, col, scale);
    }

    private static void BoldMid(float x, float cy, string text, Rgba col, float scale) =>
        Bold(x, cy - TextH(scale) * 0.5f, text, col, scale);

    // The name on the left. The page sits boxed on the right with the key that turns it. The
    // box lights up while Up and Down turn pages.
    private static void Strip()
    {
        float cy = PY(StripMid);
        float brand = F(9f);
        Spaced(PX(Left), cy - TextH(brand) * 0.5f, "BATWING", Soft, brand, 2.2f);

        float small = F(7f);
        float key = Chip(PX(Right), cy, Pad.InUse ? "LB RB" : "TAB", Palette.Dim, small, alignRight: true);

        // The box is sized to the real title so the decode effect doesn't make it jump about.
        float title = F(10f);
        string name = Deck.Current.Title;
        string count = (Deck.PageIndex + 1) + "/" + Opt.Pages.Count;
        float boxW = TextW(name, title) + TextW(count, small) + S(22f), boxH = S(18f);
        float bx = PX(Right) - key - S(6f) - boxW, by = cy - boxH * 0.5f;
        bool focus = Deck.Level == 0;
        Rect(bx, by, boxW, boxH, Palette.Hud, focus ? 0.9f : 0.15f);
        Outline(bx, by, boxW, boxH, Palette.Hud, focus ? 1f : 0.7f, Hair);

        Rgba ink = focus ? Palette.Screen : Ink;
        BoldMid(bx + S(8f), cy, DeckView.Decode(name, (float)(Time - Deck.PageChangedAt)), ink, title);
        TextRight(bx + boxW - S(7f), cy, count, focus ? Palette.Screen : Palette.Dim, small);

        Rect(PX(Left), PY(StripH), S(Right - Left), Hair, Palette.Dim, 0.6f);
    }

    // While choosing a page the left half says what is on it, and how the Batwing is doing.
    private static void PageFocus(float bottom)
    {
        float y = BodyTop + 2f;
        Bold(PX(_focusLeft), PY(y), Deck.Current.Title, Ink, F(16f));
        y += 23f;

        float cy = PY(y + 4f);
        float lamp = S(5f);
        Rect(PX(_focusLeft), cy - lamp * 0.5f, lamp, lamp, Wing.I.Lamp);
        TextMid(PX(_focusLeft + 9f), cy, Wing.I.StateLabel, Palette.Dim, F(7.5f));
        y += 14f;

        y = Keys(y, ("UP DOWN", "PAGE"), (SelectKey, "OPEN"));
        Wrapped(Deck.Current.Blurb + " " + Deck.Current.Options.Count + " settings.", y + 6f, bottom);
    }

    private static void Focus(Option o, float bottom)
    {
        float y = Label(o.LabelNow, BodyTop + 2f);
        y = Value(o, y + 6f);
        y = Hint(o, y + 5f);

        string help = Deck.Binding is { } b ? "Press the key for " + b.Label + ". Backspace cancels." : o.Help;
        Wrapped(help, y + 6f, bottom);
    }

    // The name as large as fits the half. A long one goes onto two lines. Returns where it ends.
    private static float Label(string label, float top)
    {
        const float largest = 16f;
        const float smallest = 11f;

        float size = largest;
        while (size > smallest && TextW(label, F(size)) > S(FocusW))
            size -= 1f;

        if (TextW(label, F(size)) <= S(FocusW))
        {
            Bold(PX(_focusLeft), PY(top), label, Ink, F(size));
            return top + size + 2f;
        }

        string rest = FitLine(label, S(FocusW), F(smallest), out string first);
        Bold(PX(_focusLeft), PY(top), first, Ink, F(smallest));
        FitLine(rest, S(FocusW), F(smallest), out string second);
        Bold(PX(_focusLeft), PY(top + smallest + 2f), second, Ink, F(smallest));
        return top + smallest * 2f + 4f;
    }

    // Returns where the value and its control end.
    private static float Value(Option o, float top)
    {
        bool flash = Deck.Flashed == o && Time - Deck.FlashAt < 0.45;
        Rgba col = flash ? Palette.White : Palette.Hud;
        bool blink = (int)(Time * 3.0) % 2 == 0;
        float left = PX(_focusLeft);

        switch (o)
        {
            case Switch s:
            {
                float cw = S(40f), ch = S(19f), cy = PY(top + 9.5f);
                Outline(left, cy - ch * 0.5f, cw * 2f, ch, Palette.Dim, 0.8f, Hair);
                Rect((s.On ? left + cw : left) + S(1f), cy - ch * 0.5f + S(1f), cw - S(2f), ch - S(2f), col, 0.9f);
                BoldMid(left + cw * 0.5f - TextW("OFF", F(9f)) * 0.5f, cy, "OFF", s.On ? Palette.Dim : Palette.Screen, F(9f));
                BoldMid(left + cw * 1.5f - TextW("ON", F(9f)) * 0.5f, cy, "ON", s.On ? Palette.Screen : Palette.Dim, F(9f));
                return top + 19f;
            }

            case Dial d:
            {
                Bold(left, PY(top), d.Value, flash ? Palette.White : Ink, F(16f));
                float bw = S(FocusW), by = PY(top + 21f);
                Rect(left, by, bw, S(4f), Palette.Faint, 0.9f);
                Rect(left, by, bw * d.Fraction, S(4f), col);
                for (int k = 0; k <= 4; k++)
                    Rect(left + bw * k / 4f, by - S(3f), Hair, S(10f), Palette.Dim, 0.9f);
                if (!d.IsDefault)
                    Rect(left + bw * d.DefaultFraction - S(1.5f), by + S(8f), S(3f), S(3f), Palette.Dim, 0.9f);
                return top + 30f;
            }

            case Pick p:
            {
                float room = FocusW - 26f;
                float size = 15f;
                while (size > 9f && TextW(p.Value, F(size)) > S(room))
                    size -= 1f;

                float cy = PY(top + 8f);
                BoldMid(left, cy, "<", Palette.Hud, F(11f));
                float end = left + S(13f) + TextW(p.Value, F(size));
                BoldMid(left + S(13f), cy, p.Value, flash ? Palette.White : Ink, F(size));
                BoldMid(end + S(5f), cy, ">", Palette.Hud, F(11f));

                // A dash for each choice, the current one lit.
                float dash = S(10f), gap = S(3f), dy = PY(top + 19f);
                for (int i = 0; i < p.Labels.Length; i++)
                    Rect(left + i * (dash + gap), dy, dash, S(2.5f), i == p.Index ? col : Palette.Faint, 0.9f);
                return top + 22f;
            }

            case Bind b:
            {
                float cy = PY(top + 9f);
                if (Deck.Binding == b)
                {
                    BoldMid(left, cy, Deck.BindRefused ? "THAT KEY IS TAKEN" : "PRESS A KEY", Palette.CautionNow, F(12f));
                    Rect(left, PY(top + 18f), S(FocusW), S(1.5f), Palette.CautionNow, blink ? 0.9f : 0.3f);
                    return top + 19f;
                }

                Box(left, cy, b.Value, col, F(13f), S(8f));
                return top + 19f;
            }

            case Act a:
            {
                float cy = PY(top + 9f);
                if (Deck.Confirming == a)
                {
                    BoldMid(left, cy, "PRESS AGAIN TO CONFIRM", Palette.CautionNow, F(9.5f));
                    Rect(left, PY(top + 18f), S(FocusW) * Deck.ConfirmLeft, S(1.5f), Palette.CautionNow, blink ? 0.9f : 0.5f);
                    return top + 19f;
                }

                float bw = Box(left, cy, SelectKey, col, F(10f), S(9f));
                if (a.Value.Length > 0)
                    TextMid(left + bw + S(8f), cy, a.Value, Soft, F(8f));
                return top + 19f;
            }

            default:
                Bold(left, PY(top), o.Value, Ink, F(14f));
                return top + 14f;
        }
    }

    // The keys that work on this row, under its control. Returns where they end.
    private static float Hint(Option o, float top)
    {
        if (Deck.Binding != null)
            return Keys(top, ("BACKSPACE", "CANCEL"));
        if (Deck.Confirming == o)
            return Keys(top, (SelectKey, "CONFIRM"));

        string adjust = Pad.InUse ? "D-PAD" : "LEFT RIGHT";
        string reset = Pad.InUse ? "X" : "DEL";
        bool resettable = !o.IsDefault && o is not Act;
        return o switch
        {
            Switch => resettable ? Keys(top, (SelectKey, "SWITCH"), (reset, "RESET")) : Keys(top, (SelectKey, "SWITCH")),
            Dial or Pick => resettable ? Keys(top, (adjust, "CHANGE"), (reset, "RESET")) : Keys(top, (adjust, "CHANGE")),
            Bind => resettable ? Keys(top, (SelectKey, "REBIND"), (reset, "RESET")) : Keys(top, (SelectKey, "REBIND")),
            _ => top - 5f,
        };
    }

    // Key names in small boxes, each followed by what it does.
    private static float Keys(float top, params (string Key, string Does)[] keys)
    {
        float s = F(7f);
        float cy = PY(top + 5f);
        float x = PX(_focusLeft);
        foreach ((string key, string does) in keys)
        {
            x += Chip(x, cy, key, Palette.Dim, s, alignRight: false) + S(4f);
            TextMid(x, cy, does, Palette.Dim, s);
            x += TextW(does, s) + S(10f);
        }

        return top + 10f;
    }

    private static float Chip(float x, float cy, string text, Rgba col, float scale, bool alignRight)
    {
        float pad = S(3f);
        float w = TextW(text, scale) + pad * 2f, h = TextH(scale) + S(2f);
        if (alignRight)
            x -= w;
        Rect(x, cy - h * 0.5f, w, h, col, 0.15f);
        Outline(x, cy - h * 0.5f, w, h, col, 0.7f, Hair);
        TextMid(x + pad, cy, text, Soft, scale);
        return w;
    }

    // Text in a framed box, its left edge at x. Returns how wide the box is.
    private static float Box(float x, float cy, string text, Rgba col, float scale, float pad)
    {
        float w = TextW(text, scale) + pad * 2f;
        float h = TextH(scale) + pad * 0.6f;
        Rect(x, cy - h * 0.5f, w, h, col, 0.18f);
        Outline(x, cy - h * 0.5f, w, h, col, 0.85f, Hair);
        BoldMid(x + pad, cy, text, Ink, scale);
        return w;
    }

    // Wraps text down the left half until it runs out of room.
    private static void Wrapped(string text, float top, float bottom)
    {
        const float size = 8.5f;
        const float lineH = 11.5f;
        float scale = F(size);
        string rest = text;
        float y = top;
        while (rest.Length > 0 && y + size <= bottom)
        {
            rest = FitLine(rest, S(FocusW), scale, out string line);
            if (rest.Length > 0 && y + lineH + size > bottom)
                line = line.TrimEnd('.', ',') + "...";
            Text(PX(_focusLeft), PY(y), line, Soft, scale);
            y += lineH;
        }
    }

    // Every setting on the page. The screen is tilted enough that a value can look like it sits
    // on the row below, so each row is a tile of its own with a gap round it. The list scrolls
    // to keep the selected one in view.
    private static void List(float bottom)
    {
        List<Option> rows = Deck.Current.Options;
        float top = BodyTop;
        int fit = Math.Max(1, (int)((bottom - top) / RowH));
        if (!ReferenceEquals(_scrollPage, Deck.Current))
        {
            _scrollPage = Deck.Current;
            _scroll = 0;
        }

        if (Deck.Level == 1)
        {
            if (Deck.RowIndex < _scroll + 1)
                _scroll = Deck.RowIndex - 1;
            else if (Deck.RowIndex > _scroll + fit - 2)
                _scroll = Deck.RowIndex - fit + 2;
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, rows.Count - fit));
        float age = (float)(Time - Deck.PageChangedAt);
        float label = F(8.5f), value = F(8f);
        float right = PX(_listRight - 3f);
        float y = top;
        for (int k = 0; k < fit && _scroll + k < rows.Count; k++)
        {
            int j = _scroll + k;
            Option o = rows[j];
            bool selected = Deck.Level == 1 && j == Deck.RowIndex;
            float cy = PY(y + RowH * 0.5f);

            // Rows come in one after another when the page turns.
            float was = Alpha;
            Alpha = was * (Fx ? Ease.Out(Math.Clamp((age - k * 0.03f) / 0.18f, 0f, 1f)) : 1f);

            float tileTop = PY(y + TileGap * 0.5f), tileH = S(RowH - TileGap);
            Rect(PX(ListLeft), tileTop, S(_listRight - ListLeft), tileH, Palette.Hud, selected ? 0.3f : 0.1f);
            if (selected)
                Rect(PX(ListLeft), tileTop, S(2.5f), tileH, Palette.Hud, 1f);

            if (!o.IsDefault && o is not Act)
                Rect(PX(ListLeft + 6f), cy - S(1.5f), S(3f), S(3f), Palette.Hud);

            (string text, Rgba tone) = RowValue(o, selected);
            float valueW = text.Length > 0 ? TextRight(right, cy, text, tone, value) : 0f;

            string name = o.LabelNow;
            float room = right - valueW - S(6f) - PX(LabelLeft);
            if (TextW(name, label) > room)
            {
                FitLine(name, room, label, out string cut);
                name = cut.Length < name.Length ? cut + ".." : cut;
            }

            if (selected)
                BoldMid(PX(LabelLeft), cy, name, Palette.White, label);
            else
                TextMid(PX(LabelLeft), cy, name, Soft, label);

            Alpha = was;
            y += RowH;
        }

        // Arrows at the ends that have more past them.
        float bob = Fx ? 0.75f + 0.75f * MathF.Sin((float)Time * 4f) : 0f;
        float middle = (ListLeft + _listRight) * 0.5f;
        if (_scroll > 0)
            Arrow(middle, top - 5f - bob, down: false);
        if (_scroll + fit < rows.Count)
            Arrow(middle, top + fit * RowH + 1f + bob, down: true);
    }

    private static (string, Rgba) RowValue(Option o, bool selected)
    {
        Rgba tone = selected ? Palette.White : Ink;
        return o switch
        {
            Switch s => (s.Value, s.On ? tone : Palette.Dim),
            Bind b when Deck.Binding == b => ("PRESS A KEY", Palette.CautionNow),
            Act a when Deck.Confirming == a => ("CONFIRM", Palette.CautionNow),
            Act a => (a.Value, Palette.Dim),
            _ => (o.Value, tone),
        };
    }

    // Every page, the current one lit, as the flat deck has them down its side.
    private static void Pages(float bottom)
    {
        const float rowH = 18f;
        float x = PX(Left), w = S(PagesW);
        float y = BodyTop;
        for (int i = 0; i < Opt.Pages.Count && y + rowH <= bottom; i++)
        {
            bool current = i == Deck.PageIndex;
            bool focus = current && Deck.Level == 0;
            float cy = PY(y + rowH * 0.5f);
            if (current)
            {
                Rect(x, PY(y + 1.5f), w, S(rowH - 3f), Palette.Hud, focus ? 0.9f : 0.22f);
                Rect(x, PY(y + 1.5f), S(2.5f), S(rowH - 3f), Palette.Hud, 1f);
            }

            string title = Opt.Pages[i].Title;
            if (current)
                BoldMid(x + S(7f), cy, title, focus ? Palette.Screen : Palette.White, F(8f));
            else
                TextMid(x + S(7f), cy, title, Soft, F(8f));
            TextRight(x + w - S(4f), cy, (i + 1).ToString("00"), focus ? Palette.Screen : Palette.Dim, F(6.5f));
            y += rowH;
        }

        Rect(PX(Left + PagesW + Gutter), PY(BodyTop), Hair, S(bottom - BodyTop), Palette.Dim, 0.5f);
    }

    // How the Batwing is doing. While it flies, its readouts. Otherwise the keys that call on it.
    private static void Status(float bottom)
    {
        const float lineH = 12f;
        float x = PX(_statusLeft), right = PX(_statusLeft + StatusW);
        Rect(PX(_statusLeft - Gutter), PY(BodyTop), Hair, S(bottom - BodyTop), Palette.Dim, 0.5f);

        float y = BodyTop + 2f;
        Text(x, PY(y), "STATUS", Palette.Dim, F(7f));
        y += 11f;

        float lamp = S(5f);
        Rect(x, PY(y + 5f) - lamp * 0.5f, lamp, lamp, Wing.I.Lamp);
        string rest = FitLine(Wing.I.StateLabel, S(StatusW - 9f), F(7.5f), out string line);
        BoldMid(x + S(9f), PY(y + 5f), line, Ink, F(7.5f));
        if (rest.Length > 0)
        {
            y += 10f;
            FitLine(rest, S(StatusW - 9f), F(7.5f), out line);
            BoldMid(x + S(9f), PY(y + 5f), line, Ink, F(7.5f));
        }

        y += 18f;
        bool flying = Wing.I.CraftLive;
        (string, string)[] lines = flying
            ? new[]
            {
                ("SPEED", Wing.I.SpeedText),
                ("ALTITUDE", Wing.I.AltitudeText),
                ("THROTTLE", Fmt.Percent(Wing.I.ThrottleFraction)),
                ("HULL", Fmt.Percent(Wing.I.HullFraction)),
                ("G", Fmt.Tenths(Wing.I.GLoad)),
            }
            : new[]
            {
                ("CALL", Opt.CallKey.Value),
                ("REMOTE", Opt.RemoteKey.Value),
                ("ESCORT", Opt.EscortKey.Value),
                ("STRIKE", Opt.StrikeKey.Value),
                ("LIFT", Opt.LiftKey.Value),
            };

        Text(x, PY(y), flying ? "FLIGHT" : "KEYS", Palette.Dim, F(7f));
        y += 11f;
        foreach ((string name, string value) in lines)
        {
            if (y + lineH > bottom)
                break;

            float cy = PY(y + lineH * 0.5f);
            TextMid(x, cy, name, Soft, F(7.5f));
            if (flying)
                TextRight(right, cy, value, Ink, F(7.5f));
            else
                Chip(right, cy, value, Palette.Dim, F(7f), alignRight: true);
            y += lineH;
        }
    }

    // An arrow head made of boxes, so it fades like everything else.
    private static void Arrow(float middle, float y, bool down)
    {
        const int rows = 4;
        for (int i = 0; i < rows; i++)
        {
            float half = 0.5f + (down ? rows - 1 - i : i);
            Rect(PX(middle - half), PY(y + i), S(2f * half), MathF.Max(1f, S(1f)), Palette.Hud);
        }
    }
}
