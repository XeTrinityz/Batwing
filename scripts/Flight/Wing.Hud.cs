using System.Globalization;
using System.Numerics;
using BmSDK.BmGame;
using BmSDK.Engine;
using static Trinity.Batwing.Gfx;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    private const float TapeH = 230f;
    private const float TapeOff = 330f;
    private const float HeadingW = 420f;

    private static float U(float v) => S(v) * Opt.HudSize.Number;

    private static float Sc(float s) => s * Opt.HudSize.Number;

    private static float Thick => MathF.Max(1f, U(1.6f));

    // The inbound and escort cards sit where toasts go, so they hold that spot and toasts drop under them.
    private const string CardLane = Shared.OwnerName + ".Card";

    private static void HoldToastSpot(float bottom) => ToastLane.Post(CardLane, 0, bottom);

    public void Draw(Canvas c)
    {
        if (_phase == Phase.Arriving)
        {
            Inbound();
            return;
        }

        if (_phase == Phase.Escort)
        {
            EscortHud(c);
            return;
        }

        if (_phase != Phase.Flying)
            return;

        if (Opt.Hud.On)
        {
            WorldView.Begin(c);
            Markers();
            PitchLadder();
            WeaponMarks();
            SpeedTape();
            HeightTape();
            HeadingTape();
            Status();
            Alerts();
            CargoMark();
        }

        if (Clock.Now < _hintUntil)
            KeysCard();
    }

    // Brackets the Batmobile when it's under the craft and close enough to hook.
    private void CargoMark()
    {
        if (Carrying || _haul == Haul.Latch || !WorldView.Ready || Hookable(out _) is not { } car)
            return;

        Brackets(car.Location, U(16f), Palette.Hud, Thick);
        if (WorldView.ToScreen(car.Location, out Vector2 p))
            TextMid(p.X + U(22f), p.Y, (Pad.InUse ? "D-PAD DOWN" : Opt.LiftKey.Value) + " HOOK", Palette.Hud, Sc(0.4f));
    }

    private void Inbound()
    {
        float cx = Gfx.W * 0.5f;
        float y = S(118);
        float w = U(300), h = U(44);
        float x = cx - w * 0.5f;
        HoldToastSpot(y + h);
        Rect(x, y, w, h, Palette.Screen, 0.85f);
        Rect(x, y, U(3), h, Palette.CautionNow, 0.9f);
        Rect(x, y + h - MathF.Max(1f, U(2)), w * EtaFraction, MathF.Max(1f, U(2)), Palette.CautionNow, 0.95f);
        Spaced(x + U(16), y + U(7), "BATWING INBOUND", Palette.Hud, Sc(0.56f), 2f);
        TextRight(x + w - U(16), y + U(16), "ETA " + Eta.ToString("0.0", CultureInfo.InvariantCulture) + " S", Palette.Text, Sc(0.5f));
        TextMid(x + U(16), y + h - U(13), "WAVE OFF: " + (Pad.InUse ? "CALL AGAIN" : Opt.CallKey.Value), Palette.Dim, Sc(0.4f));
    }

    private void EscortHud(Canvas c)
    {
        if (!Opt.Hud.On)
            return;

        float cx = Gfx.W * 0.5f;
        float y = S(118);
        float w = U(300), h = U(44);
        float x = cx - w * 0.5f;
        HoldToastSpot(y + h);
        Rect(x, y, w, h, Palette.Screen, 0.85f);
        Rect(x, y, U(3), h, Palette.Hud, 0.9f);
        if (Hauling)
        {
            Spaced(x + U(16), y + U(7), _delivery ? "BATMOBILE DELIVERY" : "BATMOBILE LIFT", Palette.Hud, Sc(0.56f), 2f);
            TextRight(x + w - U(16), y + U(16), HaulState(), Palette.Text, Sc(0.5f));
            string undo = _delivery ? "MARK SPOT: " : Carrying ? "SET DOWN: " : "CANCEL: ";
            TextMid(x + U(16), y + h - U(13), undo + Opt.LiftKey.Value + "  DISMISS: " + Opt.EscortKey.Value, Palette.Dim, Sc(0.4f));
        }
        else
        {
            Spaced(x + U(16), y + U(7), "BATWING ESCORT", Palette.Hud, Sc(0.56f), 2f);
            string state = Catching ? "CATCHING" : _pickup ? "PICKUP" : _strike != null ? (Chasing(_strike) ? "CHASING" : "STRIKING") : EscortTarget() != null ? "ENGAGING" : "ON WATCH";
            TextRight(x + w - U(16), y + U(16), state, _strike != null ? Palette.CautionNow : Palette.Text, Sc(0.5f));
            string keys = World.Pawn is RVehicleBatmobileBase
                ? "LIFT: " + Opt.LiftKey.Value + "  DISMISS: " + Opt.EscortKey.Value
                : "STRIKE: " + Opt.StrikeKey.Value + "  BOARD: " + Opt.CallKey.Value + "  DISMISS: " + Opt.EscortKey.Value;
            TextMid(x + U(16), y + h - U(13), keys, Palette.Dim, Sc(0.4f));
        }

        WorldView.Begin(c);
        if (!WorldView.Ready)
            return;

        // Where the Batmobile is going down.
        if (Hauling && (_delivery || (_haul != Haul.Fetch && _haul != Haul.Latch)))
        {
            Brackets(_goal, U(16f), Palette.Warn, Thick);
            if (WorldView.ToScreen(_goal, out Vector2 g))
                TextMid(g.X + U(22f), g.Y, Fmt.Whole(FlatDistance(_fm.Position, _goal) / Metre) + " M", Palette.Warn, Sc(0.4f));
        }

        float t = Thick;
        if (_strike is { IsValid: true } strike)
            Brackets(strike.Location, U(14f), Palette.CautionNow, t);

        // Marked while the key is held, then the ones still waiting their turn, numbered in strike order.
        List<Actor> numbered = _marking ? _marks : _queue;
        for (int i = 0; i < numbered.Count; i++)
        {
            if (numbered[i] is not { IsValid: true } m)
                continue;
            Brackets(m.Location, U(12f), Palette.Warn, t);
            if (WorldView.ToScreen(m.Location, out Vector2 p))
                TextMid(p.X + U(20f), p.Y, (i + (_marking ? 1 : 2)).ToString(), Palette.Warn, Sc(0.44f));
        }

        if ((_strike == null || _marking) && _strikeAim is { IsValid: true } aim && !_marks.Exists(m => m.Ptr == aim.Ptr))
        {
            Brackets(aim.Location, U(aim is RPawnVillain ? 10f : 16f), Palette.Dim, t);
            string what = aim is RPawnVillain ? "" : Turret(aim) ? " TURRET" : HeavyTank(aim) ? " HEAVY TANK" : Apc(aim) ? " APC" : " VEHICLE";
            if (WorldView.ToScreen(aim.Location, out Vector2 p))
                TextMid(p.X + U(aim is RPawnVillain ? 16f : 22f), p.Y, Opt.StrikeKey.Value + what, Palette.Dim, Sc(0.4f));
        }

        if (_escortFoe is { IsValid: true } foe && Foe(foe))
            Brackets(foe.Location, U(14f), Palette.Text, t);

        string away = World.Pawn is { IsValid: true } me ? Fmt.Whole(Vector3.Distance(me.Location, _fm.Position) / Metre) + " M" : "";
        PointerBounds(y + h, out float left, out float right, out float top, out float bottom);
        if (WorldView.ToScreen(_fm.Position, out Vector2 bw) && bw.X >= left && bw.X <= right && bw.Y >= top && bw.Y <= bottom)
        {
            float r = U(7f);
            Shapes.QueueLine(bw.X, bw.Y - r, bw.X + r, bw.Y, t);
            Shapes.QueueLine(bw.X + r, bw.Y, bw.X, bw.Y + r, t);
            Shapes.QueueLine(bw.X, bw.Y + r, bw.X - r, bw.Y, t);
            Shapes.QueueLine(bw.X - r, bw.Y, bw.X, bw.Y - r, t);
            Shapes.FlushLines(Palette.Hud);
            if (away.Length > 0)
                TextMid(bw.X + U(14f), bw.Y, away, Palette.Hud, Sc(0.4f));
        }
        else
        {
            EscortPointer(left, right, top, bottom, away);
        }
    }

    // The pointer keeps clear of the screen edge and stays under the escort card.
    private static void PointerBounds(float card, out float left, out float right, out float top, out float bottom)
    {
        float m = U(56f);
        left = m;
        right = Gfx.W - m;
        top = MathF.Max(m, card + U(28f));
        bottom = Gfx.H - m;
    }

    // Off screen or behind the camera, an arrow on the edge shows which way to turn to find it.
    private void EscortPointer(float left, float right, float top, float bottom, string away)
    {
        Vector3 v = _fm.Position - WorldView.Eye;
        var d = new Vector2(Vector3.Dot(v, WorldView.Right), -Vector3.Dot(v, WorldView.Up));
        d = d.LengthSquared() > 1e-4f ? Vector2.Normalize(d) : new Vector2(0f, 1f);

        float cx = Gfx.W * 0.5f, cy = Gfx.H * 0.5f;
        float sx = d.X > 1e-4f ? (right - cx) / d.X : d.X < -1e-4f ? (left - cx) / d.X : float.MaxValue;
        float sy = d.Y > 1e-4f ? (bottom - cy) / d.Y : d.Y < -1e-4f ? (top - cy) / d.Y : float.MaxValue;
        Vector2 at = new Vector2(cx, cy) + d * MathF.Max(0f, MathF.Min(sx, sy));

        float s = U(11f);
        var n = new Vector2(-d.Y, d.X);
        Vector2 back = at - d * (s * 0.6f);
        Vector2 tip = at + d * s, b1 = back + n * (s * 0.8f), b2 = back - n * (s * 0.8f);
        float o = U(3f);
        Vector2 ot = at + d * (s + o * 1.6f), o1 = back - d * o + n * (s * 0.8f + o * 1.4f), o2 = back - d * o - n * (s * 0.8f + o * 1.4f);
        Shapes.Triangle(ot.X, ot.Y, o1.X, o1.Y, o2.X, o2.Y, Palette.Screen, 0.7f);
        Shapes.Triangle(tip.X, tip.Y, b1.X, b1.Y, b2.X, b2.Y, Palette.Hud, 0.95f);

        if (away.Length == 0)
            return;

        // Sits on the inside of the arrow, pushed far enough along that the text never covers it.
        float scale = Sc(0.4f);
        float reach = s * 0.6f + U(8f) + MathF.Abs(d.X) * TextW(away, scale) * 0.5f + MathF.Abs(d.Y) * TextH(scale) * 0.5f;
        TextCentre(at.X - d.X * reach, at.Y - d.Y * reach, away, Palette.Hud, scale);
    }

    private void Markers()
    {
        if (!WorldView.Ready)
            return;

        Vector3 p = _fm.Position;
        float t = Thick;

        if (!Opt.Direct && WorldView.ToScreen(p + AimDir() * 30000f, out Vector2 aim))
        {
            float r = U(17f);
            Shapes.Ring(aim.X, aim.Y, r - t, r, Palette.Hud, 1f);
            Shapes.QueueLine(aim.X - r - U(6), aim.Y, aim.X - r + U(1), aim.Y, t);
            Shapes.QueueLine(aim.X + r - U(1), aim.Y, aim.X + r + U(6), aim.Y, t);
            Shapes.QueueLine(aim.X, aim.Y - r - U(6), aim.X, aim.Y - r + U(1), t);
            Shapes.FlushLines(Palette.Hud);
        }

        if (_fm.Hover > 0.5f)
        {
            if (WorldView.ToScreen(p + _fm.Forward * 30000f, out Vector2 h))
            {
                float s = U(9f);
                Shapes.QueueLine(h.X - s, h.Y, h.X + s, h.Y, t);
                Shapes.QueueLine(h.X, h.Y - s, h.X, h.Y + s, t);
                Shapes.FlushLines(Palette.Text);
            }

            return;
        }

        if (WorldView.ToScreen(p + _fm.Forward * 30000f, out Vector2 nose))
        {
            float s = U(8f);
            Shapes.QueueLine(nose.X - s * 1.8f, nose.Y, nose.X - s * 0.6f, nose.Y, t);
            Shapes.QueueLine(nose.X + s * 0.6f, nose.Y, nose.X + s * 1.8f, nose.Y, t);
            Shapes.QueueLine(nose.X, nose.Y - s * 0.6f, nose.X, nose.Y - s * 1.5f, t);
            Shapes.FlushLines(Palette.Text);
        }

        float speed = _fm.Velocity.Length();
        if (speed > 500f && WorldView.ToScreen(p + _fm.Velocity / speed * 30000f, out Vector2 path))
        {
            float r = U(6.5f);
            Rgba col = _impactAt < 2.5f ? Palette.Warn : Palette.Hud;
            Shapes.Ring(path.X, path.Y, r - t, r, col, 1f);
            Shapes.QueueLine(path.X - r * 2.8f, path.Y, path.X - r, path.Y, t);
            Shapes.QueueLine(path.X + r, path.Y, path.X + r * 2.8f, path.Y, t);
            Shapes.QueueLine(path.X, path.Y - r, path.X, path.Y - r * 2.2f, t);
            Shapes.FlushLines(col);
        }
    }

    private void PitchLadder()
    {
        if (!WorldView.Ready || _fm.Hover > 0.5f)
            return;

        float yaw = MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) * Deg;
        float t = MathF.Max(1f, U(1.2f));
        float gap = U(44f);
        float s = Sc(0.4f);
        var labels = new List<(float X, float Y, string Text)>();
        for (int p = -40; p <= 40; p += 10)
        {
            if (!WorldView.DirToScreen(Geo.Dir(yaw, p), out Vector2 at) || at.Y < 0f || at.Y > Gfx.H)
                continue;

            float half = p == 0 ? U(150f) : U(56f);
            Shapes.QueueLine(at.X - gap - half, at.Y, at.X - gap, at.Y, t);
            Shapes.QueueLine(at.X + gap, at.Y, at.X + gap + half, at.Y, t);
            if (p != 0)
            {
                float tip = p > 0 ? U(6f) : -U(6f);
                Shapes.QueueLine(at.X - gap, at.Y, at.X - gap, at.Y + tip, t);
                Shapes.QueueLine(at.X + gap, at.Y, at.X + gap, at.Y + tip, t);
                labels.Add((at.X + gap + half + U(6), at.Y, Fmt.Signed(p)));
            }
        }

        Shapes.FlushLines(Palette.Dim);
        foreach ((float x, float y, string text) in labels)
            TextMid(x, y, text, Palette.Dim, s);
    }

    private void SpeedTape()
    {
        float x = Gfx.W * 0.5f - U(TapeOff);
        float cy = Gfx.H * 0.5f;
        float h = U(TapeH);
        string number = Speed(_fm.Velocity.Length(), out string unit);
        float speed = float.Parse(number, CultureInfo.InvariantCulture);
        Tape(x, cy, h, speed, 10f, 60f, false, Palette.Hud, out _);
        Box(x, cy, number, true);
        Text(x - U(8) - TextW(unit, Sc(0.4f)), cy - h * 0.5f - U(20), unit, Palette.Dim, Sc(0.4f));

        float stall = _fm.StallSpeed * SpeedFactor();
        float sy = cy + (speed - stall) * (h / 60f);
        if (sy > cy - h * 0.5f && sy < cy + h * 0.5f)
            Rect(x - U(26), sy - U(1), U(10), U(2), Palette.CautionNow, 0.9f);
    }

    private void HeightTape()
    {
        float x = Gfx.W * 0.5f + U(TapeOff);
        float cy = Gfx.H * 0.5f;
        float h = U(TapeH);
        bool known = _ground < GroundReach;
        float alt = known ? MathF.Max(0f, _ground) / Metre : 0f;
        Rgba col = known && _ground < 1500f && _fm.Hover < 0.5f ? Palette.CautionNow : Palette.Hud;
        Tape(x, cy, h, alt, 10f, 60f, true, col, out _);
        Box(x, cy, known ? Fmt.Whole(alt) : "---", false);
        Text(x + U(8), cy - h * 0.5f - U(20), "M AGL", Palette.Dim, Sc(0.4f));
    }

    private static void Tape(float x, float cy, float h, float value, float step, float span, bool rightSide, Rgba col, out float pxPerUnit)
    {
        pxPerUnit = h / span;
        float t = MathF.Max(1f, U(1.2f));
        float side = rightSide ? 1f : -1f;
        float top = cy - h * 0.5f, bottom = cy + h * 0.5f;
        Shapes.QueueLine(x, top, x, bottom, t);
        float first = MathF.Floor((value - span * 0.5f) / step) * step;
        var labels = new List<(float Y, string Text)>();
        for (float v = first; v <= value + span * 0.5f + step; v += step)
        {
            if (v < 0f)
                continue;
            float y = cy - (v - value) * pxPerUnit;
            if (y < top || y > bottom)
                continue;
            bool major = MathF.Abs(MathF.IEEERemainder(v, step * 2f)) < 0.01f;
            float len = major ? U(12f) : U(6f);
            Shapes.QueueLine(x, y, x + side * len, y, t);
            if (major && MathF.Abs(y - cy) > U(16))
                labels.Add((y, Fmt.Whole(v)));
        }

        Shapes.FlushLines(col);
        float s = Sc(0.4f);
        foreach ((float y, string text) in labels)
        {
            if (rightSide)
                TextMid(x + U(16), y, text, Palette.Dim, s);
            else
                TextRight(x - U(16), y, text, Palette.Dim, s);
        }
    }

    private static void Box(float x, float cy, string text, bool leftOfTape)
    {
        float s = Sc(0.68f);
        float w = TextW(text, s) + U(18), h = TextH(s) + U(8);
        float bx = leftOfTape ? x - U(16) - w : x + U(16);
        Rect(bx, cy - h * 0.5f, w, h, Palette.Screen, 0.8f);
        Outline(bx, cy - h * 0.5f, w, h, Palette.Hud, 0.9f, U(1.2f));
        TextCentre(bx + w * 0.5f, cy, text, Palette.Hud, s);
        float px = leftOfTape ? bx + w : bx;
        float dir = leftOfTape ? 1f : -1f;
        Shapes.Triangle(px, cy - U(5), px, cy + U(5), px + dir * U(7), cy, Palette.Hud, 1f);
    }

    private void HeadingTape()
    {
        float cx = Gfx.W * 0.5f;
        float y = Gfx.H * 0.5f - U(238f);
        float w = U(HeadingW);
        const float span = 90f;
        float pxPerDeg = w / span;
        Vector3 v = _fm.Velocity;
        bool moving = v.X * v.X + v.Y * v.Y > 250000f;
        float heading = Compass(moving ? MathF.Atan2(v.Y, v.X) * Deg : MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) * Deg);
        float t = MathF.Max(1f, U(1.2f));
        Shapes.QueueLine(cx - w * 0.5f, y, cx + w * 0.5f, y, t);
        var labels = new List<(float X, string Text)>();
        float first = MathF.Floor((heading - span * 0.5f) / 5f) * 5f;
        for (float d = first; d <= heading + span * 0.5f + 5f; d += 5f)
        {
            float x = cx + (d - heading) * pxPerDeg;
            if (x < cx - w * 0.5f || x > cx + w * 0.5f)
                continue;
            float deg = ((d % 360f) + 360f) % 360f;
            bool major = MathF.Abs(MathF.IEEERemainder(deg, 10f)) < 0.01f;
            Shapes.QueueLine(x, y, x, y + (major ? U(10f) : U(5f)), t);
            if (major && MathF.Abs(x - cx) > U(14))
                labels.Add((x, HeadingText(deg)));
        }

        Shapes.FlushLines(Palette.Hud);
        float s = Sc(0.4f);
        foreach ((float x, string text) in labels)
            TextCentre(x, y + U(20), text, Palette.Dim, s);

        if (!Opt.Direct)
        {
            float aim = Geo.WrapDegrees(Compass(_aimYaw) - heading);
            if (MathF.Abs(aim) < span * 0.5f)
            {
                float ax = cx + aim * pxPerDeg;
                Outline(ax - U(4), y - U(9), U(8), U(6), Palette.Hud, 0.9f, U(1.2f));
            }
        }

        string head = HeadingText(heading);
        float bs = Sc(0.6f);
        float bw = TextW(head, bs) + U(16), bh = TextH(bs) + U(6);
        Rect(cx - bw * 0.5f, y - bh - U(8), bw, bh, Palette.Screen, 0.8f);
        Outline(cx - bw * 0.5f, y - bh - U(8), bw, bh, Palette.Hud, 0.9f, U(1.2f));
        TextCentre(cx, y - bh * 0.5f - U(8), head, Palette.Hud, bs);
        Shapes.Triangle(cx - U(5), y - U(8), cx + U(5), y - U(8), cx, y - U(1), Palette.Hud, 1f);
    }

    private static float Compass(float yaw) => ((yaw % 360f) + 360f) % 360f;

    private static string HeadingText(float deg) => ((int)MathF.Round(((deg % 360f) + 360f) % 360f) % 360).ToString("000", CultureInfo.InvariantCulture);

    private static float SpeedFactor() => Opt.SpeedUnit.Index switch { 1 => 0.036f, 2 => 0.01f, _ => 0.022369363f };

    private void Status()
    {
        bool hover = _fm.Hover > 0.5f;
        float cx = Gfx.W * 0.5f;
        float y = Gfx.H * 0.5f + U(210f);
        float s = Sc(0.4f);

        string mode = _landed ? "LANDED" : _fm.Boosting ? "AFTERBURNER" : hover ? "HOVER" : "WINGS";
        Rgba modeCol = _fm.Boosting ? Palette.CautionNow : Palette.Hud;
        float mw = SpacedW(mode, Sc(0.5f), 2f);
        Spaced(cx - mw * 0.5f, y, mode, modeCol, Sc(0.5f), 2f);
        y += TextH(Sc(0.5f)) + U(10);

        int meters = 2 + (Opt.Crashes.On ? 1 : 0) + (Armed ? 1 : 0);
        float mwid = U(96f), gap = U(18f);
        float total = meters * mwid + (meters - 1) * gap;
        float mx = cx - total * 0.5f;
        float level = hover ? Math.Clamp(_fm.Jets / 2f, 0f, 1f) : _fm.Throttle;
        Meter(mx, y, mwid, hover ? "JETS" : "THR", level, Palette.Hud, false);
        mx += mwid + gap;
        Meter(mx, y, mwid, "A/B", _fm.Fuel, _fm.Boosting ? Palette.CautionNow : Palette.Hud, false);
        mx += mwid + gap;
        if (Opt.Crashes.On)
        {
            Meter(mx, y, mwid, "HULL", Math.Clamp(_hull, 0f, 1f), _hull < 0.3f ? Palette.Warn : Palette.Hud, true);
            mx += mwid + gap;
        }

        if (Armed)
        {
            Rgba heat = _overheated ? Palette.Warn : Rgba.Mix(Palette.Hud, Palette.Warn, Math.Clamp((_heat - 0.4f) / 0.6f, 0f, 1f));
            Meter(mx, y, mwid, _overheated ? "GUN HOT" : "GUN", _heat, heat, false);
        }

        y += U(26);
        string attitude = "PITCH " + Fmt.Signed(_fm.Pitch() * Deg) + "   BANK " + Fmt.Signed(_fm.Bank() * Deg) + "   G " + _fm.GLoad.ToString("0.0", CultureInfo.InvariantCulture);
        TextCentre(cx, y + TextH(s) * 0.5f, attitude, Palette.Dim, s);
    }

    private static void Meter(float x, float y, float w, string label, float frac, Rgba col, bool lowBlinks)
    {
        frac = float.IsFinite(frac) ? Math.Clamp(frac, 0f, 1f) : 0f;
        float s = Sc(0.4f);
        Text(x, y, label, Palette.Dim, s);
        TextRight(x + w, y + TextH(s) * 0.5f, Fmt.Whole(frac * 100f), Palette.Text, s);
        float by = y + TextH(s) + U(3);
        float bh = U(4);
        bool blink = lowBlinks && frac < 0.3f && (int)(Clock.Now * 4.0) % 2 == 0;
        Rect(x, by, w, bh, Palette.Faint, 0.9f);
        Rect(x, by, w * frac, bh, col, blink ? 0.4f : 1f);
        Rect(x - 1f, by - U(2), MathF.Max(1f, U(1)), bh + U(4), Palette.Dim, 0.9f);
        Rect(x + w, by - U(2), MathF.Max(1f, U(1)), bh + U(4), Palette.Dim, 0.9f);
    }

    private void Alerts()
    {
        string? text = null;
        Rgba col = Palette.Warn;
        if (_bailWait >= 0f)
        {
            text = "WAITING FOR THE CITY TO LOAD // " + (Pad.InUse ? "B" : "E") + " TO JUMP NOW";
            col = Palette.CautionNow;
        }
        else if (_impactAt < 2.5f)
            text = "PULL UP";
        else if (RemoteFar)
        {
            text = _outFor > 0f ? "SIGNAL LOST IN " + Math.Max(1, (int)MathF.Ceiling(LostAfter - _outFor)) : "WEAK SIGNAL // TURN BACK";
            col = Palette.CautionNow;
        }
        else if (_fm.Stalled)
            text = "STALL";
        else if (_outside)
        {
            text = _outsideFor > 3f ? "TURNING BACK TO GOTHAM" : "LEAVING GOTHAM";
            col = Palette.CautionNow;
        }
        else if (Opt.Crashes.On && _hull < 0.25f)
            text = "HULL CRITICAL";
        else if (Clock.Now < _underFireUntil)
        {
            text = "TAKING FIRE";
            col = Palette.CautionNow;
        }
        else if (_noSolidFor > 1.5f)
        {
            text = _loadingBelow ? "GROUND STILL LOADING" : "NOTHING SOLID BELOW";
            col = Palette.CautionNow;
        }

        if (text == null || (int)(Clock.Now * 3.0) % 2 == 1)
            return;

        float s = Sc(0.9f);
        float cx = Gfx.W * 0.5f, cy = Gfx.H * 0.5f - U(150f);
        float w = TextW(text, s) + U(28), h = TextH(s) + U(10);
        Outline(cx - w * 0.5f, cy - h * 0.5f, w, h, col, 0.9f, U(1.5f));
        TextCentre(cx, cy, text, col, s);
    }

    private void WeaponMarks()
    {
        if (!Armed || !WorldView.Ready)
            return;

        float t = Thick;
        if (_aimFoe is RPawnVillain { IsValid: true } thug)
        {
            Brackets(thug.Location, U(11f), Palette.CautionNow, t);
            if (WorldView.ToScreen(thug.Location, out Vector2 p))
                TextMid(p.X + U(18f), p.Y, "RIOT ROUNDS", Palette.CautionNow, Sc(0.4f));
        }
        else if (_aimFoe is { IsValid: true } aimed && !_locks.Exists(l => l.Ptr == aimed.Ptr))
        {
            Brackets(aimed.Location, U(14f), Palette.Text, t);
        }

        if (_aimFoe is { IsValid: true } apc && Apc(apc) && WeakSpot(apc) is { } weak)
        {
            Brackets(weak, U(8f), Palette.CautionNow, t);
            if (WorldView.ToScreen(weak, out Vector2 p))
                TextMid(p.X + U(14f), p.Y, "WEAK SPOT", Palette.CautionNow, Sc(0.4f));
        }

        Rgba lockCol = _lockFull ? Palette.Warn : Palette.Hud;
        for (int i = 0; i < _locks.Count; i++)
        {
            Actor foe = _locks[i];
            if (!foe.IsValid || _locks.FindIndex(l => l.Ptr == foe.Ptr) != i)
                continue;

            Brackets(foe.Location, U(20f), lockCol, t);
            int stacked = 0;
            foreach (Actor l in _locks)
            {
                if (l.Ptr == foe.Ptr)
                    stacked++;
            }

            if (stacked > 1 && WorldView.ToScreen(foe.Location, out Vector2 p))
                TextMid(p.X + U(32f), p.Y, "x" + stacked, lockCol, Sc(0.44f));
        }

        if ((_locking || _salvo.Count > 0) && WorldView.ToScreen(_fm.Position + _look * 30000f, out Vector2 aim))
        {
            string count = _locking ? _locks.Count + " LOCKED" : _salvo.Count + " AWAY";
            TextMid(aim.X + U(24f), aim.Y, count, _lockFull ? Palette.Warn : Palette.Hud, Sc(0.44f));
        }
    }

    private static void Brackets(Vector3 world, float r, Rgba col, float thick)
    {
        if (!WorldView.ToScreen(world, out Vector2 p))
            return;

        float l = r * 0.45f;
        Shapes.QueueLine(p.X - r, p.Y - r, p.X - r + l, p.Y - r, thick);
        Shapes.QueueLine(p.X - r, p.Y - r, p.X - r, p.Y - r + l, thick);
        Shapes.QueueLine(p.X + r, p.Y - r, p.X + r - l, p.Y - r, thick);
        Shapes.QueueLine(p.X + r, p.Y - r, p.X + r, p.Y - r + l, thick);
        Shapes.QueueLine(p.X - r, p.Y + r, p.X - r + l, p.Y + r, thick);
        Shapes.QueueLine(p.X - r, p.Y + r, p.X - r, p.Y + r - l, thick);
        Shapes.QueueLine(p.X + r, p.Y + r, p.X + r - l, p.Y + r, thick);
        Shapes.QueueLine(p.X + r, p.Y + r, p.X + r, p.Y + r - l, thick);
        Shapes.FlushLines(col);
    }

    private void KeysCard()
    {
        bool pad = Pad.InUse;
        bool hover = _fm.HoverWanted;
        bool direct = Opt.Direct;
        var keys = new List<(string, string)>();
        if (pad)
        {
            keys.Add(("RS", direct && !hover ? "STICK" : hover ? "TURN / AIM" : "STEER"));
            keys.Add(("LS", hover ? "FLY" : direct ? "THROTTLE / RUDDER" : "THROTTLE / ROLL"));
            keys.Add(("RT / LT", hover ? "UP / DOWN" : "AFTERBURNER / BRAKE"));
            if (hover)
                keys.Add(("LS CLICK", "FAST"));
            keys.Add(("Y", hover ? "WINGS" : "HOVER"));
            if (Armed)
            {
                keys.Add(("X", "GUNS"));
                keys.Add(("RB", "CANNON"));
                keys.Add(("A", "HOLD: LOCK // LET GO: MISSILES"));
                keys.Add(("A + X", "EXTRA MISSILE ON TARGET"));
            }

            keys.Add(("LB", "LOOK AROUND"));
            keys.Add(("D-PAD UP", "CAMERA"));
            keys.Add(("D-PAD DOWN", Carrying ? "DROP BATMOBILE" : "HOOK BATMOBILE"));
            keys.Add(("B", _remote ? "END REMOTE" : "BAIL OUT"));
        }
        else
        {
            keys.Add(("MOUSE", direct && !hover ? "STICK" : "AIM"));
            if (hover)
            {
                keys.Add(("WASD", "FLY"));
                keys.Add(("SPACE / CTRL", "UP / DOWN"));
                keys.Add(("SHIFT", "FAST"));
            }
            else
            {
                keys.Add(("W / S", "THROTTLE"));
                keys.Add(("A / D", direct ? "RUDDER" : "ROLL"));
                keys.Add(("SHIFT", "AFTERBURNER"));
                keys.Add(("CTRL", "AIR BRAKE"));
            }

            keys.Add(("F", hover ? "WINGS" : "HOVER"));
            if (Armed)
            {
                keys.Add(("LEFT MOUSE", "GUNS"));
                keys.Add(("Q", "CANNON"));
                keys.Add(("RIGHT MOUSE", "HOLD: LOCK // LET GO: MISSILES"));
                keys.Add(("RIGHT + LEFT MOUSE", "EXTRA MISSILE ON TARGET"));
            }

            keys.Add(("MIDDLE MOUSE", "LOOK AROUND"));
            keys.Add(("C", "CAMERA"));
            keys.Add((Opt.LiftKey.Value, Carrying ? "DROP BATMOBILE" : "HOOK BATMOBILE"));
            keys.Add(("E", _remote ? "END REMOTE" : "BAIL OUT"));
            keys.Add(("H", "HIDE THESE KEYS"));
        }

        float s = Sc(0.42f);
        float lh = TextH(s) + U(9);
        float keyW = 0f;
        foreach ((string key, _) in keys)
            keyW = MathF.Max(keyW, TextW(key, s) + U(12));
        float w = U(320f), pad2 = U(14f);
        float h = U(30f) + keys.Count * lh + pad2;
        float x = S(28f), y = Gfx.H - S(150f) - h;

        Rect(x, y, w, h, Palette.Screen, 0.82f);
        Rect(x, y, U(3), h, Palette.Hud, 0.9f);
        Spaced(x + pad2, y + U(8), "FLIGHT KEYS", Palette.Hud, Sc(0.46f), 2f);
        TextRight(x + w - pad2, y + U(8) + TextH(Sc(0.46f)) * 0.5f, hover ? "HOVER" : "WINGS", Palette.Dim, s);
        float cy = y + U(30f) + lh * 0.5f;
        foreach ((string key, string action) in keys)
        {
            Outline(x + pad2, cy - lh * 0.5f + U(2), keyW, lh - U(4), Palette.Dim, 0.7f, U(1));
            TextCentre(x + pad2 + keyW * 0.5f, cy, key, Palette.Text, s);
            TextMid(x + pad2 + keyW + U(10), cy, action, Palette.Dim, s);
            cy += lh;
        }
    }
}
