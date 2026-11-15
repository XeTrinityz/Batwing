using System.Globalization;
using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using static Trinity.Batwing.Gfx;

namespace Trinity.Batwing;

// The flight HUD, the inbound and escort cards and the key hints.
internal static class Hud
{
    private const float TapeH = 230f;
    private const float TapeOff = 330f;
    private const float HeadingW = 420f;
    private const float KeysCardSize = 1.25f;

    // The inbound and escort cards sit where toasts go, so they hold that spot and toasts drop under them.
    private const string CardLane = Shared.OwnerName + ".Card";

    private static float U(float v) => S(v) * Opt.HudSize.Number;

    private static float Sc(float s) => s * Opt.HudSize.Number;

    private static float Thick => MathF.Max(1f, U(1.6f));

    private static bool Minimal => Opt.HudLayout.Index == 1;

    private static void HoldToastSpot(float bottom) => ToastLane.Post(CardLane, 0, bottom);

    public static string Speed(float unitsPerSecond, out string unit)
    {
        float factor;
        switch (Opt.SpeedUnit.Index)
        {
            case 1:
                unit = "KM/H";
                factor = 0.036f;
                break;
            case 2:
                unit = "M/S";
                factor = 0.01f;
                break;
            default:
                unit = "MPH";
                factor = 0.022369363f;
                break;
        }

        return MathF.Round(unitsPerSecond * factor).ToString(CultureInfo.InvariantCulture);
    }

    private static float SpeedFactor => Opt.SpeedUnit.Index switch { 1 => 0.036f, 2 => 0.01f, _ => 0.022369363f };

    private static (float X, float Y, float W, float H) Card(Rgba edge)
    {
        float w = U(320), h = U(46);
        float x = W * 0.5f - w * 0.5f, y = S(118);
        HoldToastSpot(y + h);
        Rect(x, y, w, h, Palette.Screen, 0.85f);
        Rect(x, y, U(3), h, edge, 0.9f);
        return (x, y, w, h);
    }

    public static void Inbound(Inbound inbound)
    {
        if (!Opt.Hud.On)
            return;

        (float x, float y, float w, float h) = Card(Palette.Caution);
        Rect(x, y + h - MathF.Max(1f, U(2)), w * inbound.Progress, MathF.Max(1f, U(2)), Palette.Caution, 0.95f);
        Spaced(x + U(16), y + U(7), "BATWING INBOUND", Palette.Hud, Sc(0.56f), 2f);
        TextRight(x + w - U(16), y + U(17), "ETA " + inbound.Eta.ToString("0.0", CultureInfo.InvariantCulture) + " S", Palette.Text, Sc(0.5f));
        PromptLine(x + U(16), y + h - U(13), ("WAVE OFF", Prompt.Of(Act.Call)));
    }

    // A row of prompts, each one the key or button and what it does.
    private static void PromptLine(float x, float cy, params (string Does, Prompt Key)[] items)
    {
        float s = Sc(0.4f);
        foreach ((string does, Prompt key) in items)
        {
            x += key.Draw(x, cy, s) + U(5);
            TextMid(x, cy, does, Palette.Dim, s);
            x += TextW(does, s) + U(14);
        }
    }

    public static void Escort(Escort escort, Canvas c)
    {
        if (!Opt.Hud.On)
            return;

        (float x, float y, float w, float h) = Card(Palette.Hud);
        bool driving = World.Driving != null;
        if (escort.Hauling)
        {
            Spaced(x + U(16), y + U(7), escort.Delivery ? "BATMOBILE DELIVERY" : "BATMOBILE LIFT", Palette.Hud, Sc(0.56f), 2f);
            TextRight(x + w - U(16), y + U(17), escort.HaulStatus, Palette.Text, Sc(0.5f));
            string undo = escort.Delivery ? "MARK SPOT" : escort.Carrying ? "SET DOWN" : "CANCEL";
            PromptLine(x + U(16), y + h - U(13), (undo, Prompt.Of(Act.Lift)), ("DISMISS", Prompt.Of(Act.Escort)));
        }
        else
        {
            Spaced(x + U(16), y + U(7), "BATWING ESCORT", Palette.Hud, Sc(0.56f), 2f);
            string state = escort.CatchingNow ? "CATCHING" : escort.Picking ? "PICKUP" : escort.Striking ? "STRIKING" : escort.Engaging != null ? "ENGAGING" : "ON WATCH";
            TextRight(x + w - U(16), y + U(17), state, escort.Striking ? Palette.Caution : Palette.Text, Sc(0.5f));
            if (driving)
                PromptLine(x + U(16), y + h - U(13), ("LIFT", Prompt.Of(Act.Lift)), ("DISMISS", Prompt.Of(Act.Escort)));
            else
                PromptLine(x + U(16), y + h - U(13), ("STRIKE", Prompt.Of(Act.Strike)), ("BOARD", Prompt.Of(Act.Call)), ("DISMISS", Prompt.Of(Act.Escort)));
        }

        WorldView.Begin(c);
        if (!WorldView.Ready)
            return;

        if (escort.DropSpot is { } goal)
        {
            Brackets(goal, U(16f), Palette.Warn, Thick);
            Tag(goal, Fmt.Range(Geo.FlatDistance(escort.Craft.Position, goal)), Palette.Warn, U(22f));
        }

        float t = Thick;
        if (escort.StrikeTarget is { } strike)
            Brackets(strike.Location, U(14f), Palette.Caution, t);

        // Marked while the key is held, then the ones still waiting their turn, numbered in strike order.
        IReadOnlyList<Actor> numbered = escort.Numbered;
        for (int i = 0; i < numbered.Count; i++)
        {
            if (numbered[i] is not { IsValid: true } m)
                continue;
            Brackets(m.Location, U(12f), Palette.Warn, t);
            Tag(m.Location, (i + (escort.Sweeping ? 1 : 2)).ToString(CultureInfo.InvariantCulture), Palette.Warn, U(20f));
        }

        if ((!escort.Striking || escort.Sweeping) && escort.StrikeAim is { } aim && !numbered.Any(m => m.Ptr == aim.Ptr))
        {
            bool thug = aim is RPawnVillain;
            Brackets(aim.Location, U(thug ? 10f : 16f), Palette.Dim, t);
            Tag(aim.Location, Prompt.Of(Act.Strike).Text + (thug ? "" : " " + Foes.Kind(aim)), Palette.Dim, U(thug ? 16f : 22f));
        }

        if (escort.Engaging is { } foe)
            Brackets(foe.Location, U(14f), Palette.Text, t);

        string away = World.Pawn is { IsValid: true } me ? Fmt.Range(Vector3.Distance(me.Location, escort.Craft.Position)) : "";
        Pointer(escort.Craft.Position, y + h, away, Palette.Hud, diamond: true);
        Nav(y + h);
    }

    public static void Flight(Pilot pilot, Canvas c)
    {
        if (Opt.Hud.On)
        {
            WorldView.Begin(c);
            Markers(pilot);
            if (!Minimal)
            {
                PitchLadder(pilot);
                HeadingTape(pilot);
            }

            WeaponMarks(pilot);
            if (Minimal)
                Readouts(pilot);
            else
            {
                SpeedTape(pilot);
                HeightTape(pilot);
            }

            Status(pilot);
            Alerts(pilot);
            CargoMark(pilot);
            Nav(S(60));
        }

        if (pilot.ShowKeys)
            KeysCard(pilot);
    }

    // The map waypoint or mission marker, bracketed with its range, or an arrow at the edge when off screen.
    private static void Nav(float below)
    {
        if (!Opt.Nav.On || !WorldView.Ready || Batmobile.Destination() is not { } dest)
            return;

        float range = Vector3.Distance(WorldView.Eye, dest.At);
        Rgba col = Palette.Caution;
        string label = dest.What + " " + Fmt.Range(range);
        if (WorldView.ToScreen(dest.At, out Vector2 p) && p.X > 0f && p.X < W && p.Y > 0f && p.Y < H)
        {
            float r = U(7f);
            Shapes.QueueLine(p.X, p.Y - r, p.X + r, p.Y, Thick);
            Shapes.QueueLine(p.X + r, p.Y, p.X, p.Y + r, Thick);
            Shapes.QueueLine(p.X, p.Y + r, p.X - r, p.Y, Thick);
            Shapes.QueueLine(p.X - r, p.Y, p.X, p.Y - r, Thick);
            Shapes.FlushLines(col);
            TextCentre(p.X, p.Y - r - U(12f), label, col, Sc(0.4f));
            return;
        }

        Pointer(dest.At, below, label, col, diamond: false);
    }

    private static void Tag(Vector3 at, string text, Rgba col, float off)
    {
        if (WorldView.ToScreen(at, out Vector2 p))
            TextMid(p.X + off, p.Y, text, col, Sc(0.42f));
    }

    // Brackets the Batmobile when it's under the craft and close enough to hook.
    private static void CargoMark(Pilot pilot)
    {
        if (pilot.Craft.Carrying || pilot.Hooking || !WorldView.Ready || pilot.Hookable(out _) is not { } car)
            return;

        Brackets(car.Location, U(16f), Palette.Hud, Thick);
        Tag(car.Location, Prompt.Of(Act.Lift).Text + " HOOK", Palette.Hud, U(22f));
    }

    // Something the camera may not see. On screen it gets a marker, off it an arrow on the edge.
    private static void Pointer(Vector3 at, float below, string text, Rgba col, bool diamond)
    {
        float m = U(56f);
        float left = m, right = W - m, top = MathF.Max(m, below + U(28f)), bottom = H - m;
        float t = Thick;
        if (WorldView.ToScreen(at, out Vector2 p) && p.X >= left && p.X <= right && p.Y >= top && p.Y <= bottom)
        {
            if (!diamond)
                return;

            float r = U(7f);
            Shapes.QueueLine(p.X, p.Y - r, p.X + r, p.Y, t);
            Shapes.QueueLine(p.X + r, p.Y, p.X, p.Y + r, t);
            Shapes.QueueLine(p.X, p.Y + r, p.X - r, p.Y, t);
            Shapes.QueueLine(p.X - r, p.Y, p.X, p.Y - r, t);
            Shapes.FlushLines(col);
            if (text.Length > 0)
                TextMid(p.X + U(14f), p.Y, text, col, Sc(0.4f));
            return;
        }

        Vector3 v = at - WorldView.Eye;
        var d = new Vector2(Vector3.Dot(v, WorldView.Right), -Vector3.Dot(v, WorldView.Up));
        d = d.LengthSquared() > 1e-4f ? Vector2.Normalize(d) : new Vector2(0f, 1f);

        float cx = W * 0.5f, cy = H * 0.5f;
        float sx = d.X > 1e-4f ? (right - cx) / d.X : d.X < -1e-4f ? (left - cx) / d.X : float.MaxValue;
        float sy = d.Y > 1e-4f ? (bottom - cy) / d.Y : d.Y < -1e-4f ? (top - cy) / d.Y : float.MaxValue;
        Vector2 edge = new Vector2(cx, cy) + d * MathF.Max(0f, MathF.Min(sx, sy));

        float s = U(11f);
        var n = new Vector2(-d.Y, d.X);
        Vector2 back = edge - d * (s * 0.6f);
        Vector2 tip = edge + d * s, b1 = back + n * (s * 0.8f), b2 = back - n * (s * 0.8f);
        float o = U(3f);
        Vector2 ot = edge + d * (s + o * 1.6f), o1 = back - d * o + n * (s * 0.8f + o * 1.4f), o2 = back - d * o - n * (s * 0.8f + o * 1.4f);
        Shapes.Triangle(ot.X, ot.Y, o1.X, o1.Y, o2.X, o2.Y, Palette.Screen, 0.7f);
        Shapes.Triangle(tip.X, tip.Y, b1.X, b1.Y, b2.X, b2.Y, col, 0.95f);
        if (text.Length == 0)
            return;

        // Sits on the inside of the arrow, far enough along that the text never covers it.
        float scale = Sc(0.4f);
        float reach = s * 0.6f + U(8f) + MathF.Abs(d.X) * TextW(text, scale) * 0.5f + MathF.Abs(d.Y) * TextH(scale) * 0.5f;
        TextCentre(edge.X - d.X * reach, edge.Y - d.Y * reach, text, col, scale);
    }

    private static void Markers(Pilot pilot)
    {
        if (!WorldView.Ready)
            return;

        FlightModel fm = pilot.Craft.Fm;
        Vector3 p = fm.Position;
        float t = Thick;
        if (!Opt.Direct && WorldView.ToScreen(p + pilot.AimDir * 30000f, out Vector2 aim))
        {
            float r = U(17f);
            Shapes.Ring(aim.X, aim.Y, r - t, r, Palette.Hud, 1f);
            Shapes.QueueLine(aim.X - r - U(6), aim.Y, aim.X - r + U(1), aim.Y, t);
            Shapes.QueueLine(aim.X + r - U(1), aim.Y, aim.X + r + U(6), aim.Y, t);
            Shapes.QueueLine(aim.X, aim.Y - r - U(6), aim.X, aim.Y - r + U(1), t);
            Shapes.FlushLines(Palette.Hud);
        }

        if (fm.Hover > 0.5f)
        {
            if (WorldView.ToScreen(p + fm.Forward * 30000f, out Vector2 h))
            {
                float s = U(9f);
                Shapes.QueueLine(h.X - s, h.Y, h.X + s, h.Y, t);
                Shapes.QueueLine(h.X, h.Y - s, h.X, h.Y + s, t);
                Shapes.FlushLines(Palette.Text);
            }

            return;
        }

        if (WorldView.ToScreen(p + fm.Forward * 30000f, out Vector2 nose))
        {
            float s = U(8f);
            Shapes.QueueLine(nose.X - s * 1.8f, nose.Y, nose.X - s * 0.6f, nose.Y, t);
            Shapes.QueueLine(nose.X + s * 0.6f, nose.Y, nose.X + s * 1.8f, nose.Y, t);
            Shapes.QueueLine(nose.X, nose.Y - s * 0.6f, nose.X, nose.Y - s * 1.5f, t);
            Shapes.FlushLines(Palette.Text);
        }

        // The flight path marker, where it's really going. With mouse aim the ring and this line up when settled.
        float speed = fm.Velocity.Length();
        if (speed > 500f && WorldView.ToScreen(p + fm.Velocity / speed * 30000f, out Vector2 path))
        {
            float r = U(6.5f);
            Rgba col = pilot.ImpactIn < 2.5f ? Palette.Warn : Palette.Hud;
            Shapes.Ring(path.X, path.Y, r - t, r, col, 1f);
            Shapes.QueueLine(path.X - r * 2.8f, path.Y, path.X - r, path.Y, t);
            Shapes.QueueLine(path.X + r, path.Y, path.X + r * 2.8f, path.Y, t);
            Shapes.QueueLine(path.X, path.Y - r, path.X, path.Y - r * 2.2f, t);
            Shapes.FlushLines(col);
        }
    }

    private static void PitchLadder(Pilot pilot)
    {
        FlightModel fm = pilot.Craft.Fm;
        if (!WorldView.Ready || fm.Hover > 0.5f)
            return;

        float yaw = Geo.YawOf(fm.Forward);
        float t = MathF.Max(1f, U(1.2f));
        float gap = U(44f);
        float s = Sc(0.4f);
        var labels = new List<(float X, float Y, string Text)>();
        for (int p = -40; p <= 40; p += 10)
        {
            if (!WorldView.DirToScreen(Geo.Dir(yaw, p), out Vector2 at) || at.Y < 0f || at.Y > H)
                continue;

            float half = p == 0 ? U(150f) : U(56f);
            Shapes.QueueLine(at.X - gap - half, at.Y, at.X - gap, at.Y, t);
            Shapes.QueueLine(at.X + gap, at.Y, at.X + gap + half, at.Y, t);
            if (p == 0)
                continue;

            float tip = p > 0 ? U(6f) : -U(6f);
            Shapes.QueueLine(at.X - gap, at.Y, at.X - gap, at.Y + tip, t);
            Shapes.QueueLine(at.X + gap, at.Y, at.X + gap, at.Y + tip, t);
            labels.Add((at.X + gap + half + U(6), at.Y, Fmt.Signed(p)));
        }

        Shapes.FlushLines(Palette.Dim);
        foreach ((float x, float y, string text) in labels)
            TextMid(x, y, text, Palette.Dim, s);
    }

    private static void SpeedTape(Pilot pilot)
    {
        float x = W * 0.5f - U(TapeOff);
        float cy = H * 0.5f;
        float h = U(TapeH);
        string number = Speed(pilot.Craft.Fm.Velocity.Length(), out string unit);
        float speed = float.Parse(number, CultureInfo.InvariantCulture);
        Tape(x, cy, h, speed, 10f, 60f, false, Palette.Hud);
        Box(x, cy, number, true);
        Text(x - U(8) - TextW(unit, Sc(0.4f)), cy - h * 0.5f - U(20), unit, Palette.Dim, Sc(0.4f));

        float stall = FlightModel.StallSpeed * SpeedFactor;
        float sy = cy + (speed - stall) * (h / 60f);
        if (sy > cy - h * 0.5f && sy < cy + h * 0.5f)
            Rect(x - U(26), sy - U(1), U(10), U(2), Palette.Caution, 0.9f);
    }

    private static void HeightTape(Pilot pilot)
    {
        float x = W * 0.5f + U(TapeOff);
        float cy = H * 0.5f;
        float h = U(TapeH);
        Probe below = pilot.Craft.Below;
        float alt = below.Known ? MathF.Max(0f, below.Distance) / Geo.Metre : 0f;
        Rgba col = below.Known && below.Distance < 1500f && pilot.Craft.Fm.Hover < 0.5f ? Palette.Caution : Palette.Hud;
        Tape(x, cy, h, alt, 10f, 60f, true, col);
        Box(x, cy, below.Known ? Fmt.Whole(alt) : "---", false);
        Text(x + U(8), cy - h * 0.5f - U(20), "M AGL", Palette.Dim, Sc(0.4f));
    }

    // Speed and height as two plain boxes either side of the middle, for the minimal layout.
    private static void Readouts(Pilot pilot)
    {
        float cy = H * 0.5f + U(70f);
        string speed = Speed(pilot.Craft.Fm.Velocity.Length(), out string unit);
        Probe below = pilot.Craft.Below;
        float s = Sc(0.6f), small = Sc(0.38f);
        TextRight(W * 0.5f - U(120f), cy, speed, Palette.Hud, s);
        TextMid(W * 0.5f - U(114f), cy + U(2), unit, Palette.Dim, small);
        Text(W * 0.5f + U(120f), cy - TextH(s) * 0.5f, below.Known ? Fmt.Whole(below.Distance / Geo.Metre) : "---", Palette.Hud, s);
        TextMid(W * 0.5f + U(126f) + TextW(below.Known ? Fmt.Whole(below.Distance / Geo.Metre) : "---", s), cy + U(2), "M", Palette.Dim, small);
    }

    private static void Tape(float x, float cy, float h, float value, float step, float span, bool rightSide, Rgba col)
    {
        float per = h / span;
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
            float y = cy - (v - value) * per;
            if (y < top || y > bottom)
                continue;
            bool major = MathF.Abs(MathF.IEEERemainder(v, step * 2f)) < 0.01f;
            Shapes.QueueLine(x, y, x + side * (major ? U(12f) : U(6f)), y, t);
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

    private static void HeadingTape(Pilot pilot)
    {
        FlightModel fm = pilot.Craft.Fm;
        float cx = W * 0.5f;
        float y = H * 0.5f - U(238f);
        float w = U(HeadingW);
        const float span = 90f;
        float per = w / span;
        Vector3 v = fm.Velocity;
        float heading = Geo.Compass(v.X * v.X + v.Y * v.Y > 250000f ? Geo.YawOf(v) : Geo.YawOf(fm.Forward));
        float t = MathF.Max(1f, U(1.2f));
        Shapes.QueueLine(cx - w * 0.5f, y, cx + w * 0.5f, y, t);
        var labels = new List<(float X, string Text)>();
        float first = MathF.Floor((heading - span * 0.5f) / 5f) * 5f;
        for (float d = first; d <= heading + span * 0.5f + 5f; d += 5f)
        {
            float x = cx + (d - heading) * per;
            if (x < cx - w * 0.5f || x > cx + w * 0.5f)
                continue;
            float deg = Geo.Compass(d);
            bool major = MathF.Abs(MathF.IEEERemainder(deg, 10f)) < 0.01f;
            Shapes.QueueLine(x, y, x, y + (major ? U(10f) : U(5f)), t);
            if (major && MathF.Abs(x - cx) > U(14))
                labels.Add((x, HeadingText(deg)));
        }

        Shapes.FlushLines(Palette.Hud);
        float s = Sc(0.4f);
        foreach ((float x, string text) in labels)
            TextCentre(x, y + U(20), text, Palette.Dim, s);

        // The aim on the tape, and the waypoint as a caret.
        if (!Opt.Direct)
            Caret(cx, y, per, span, heading, pilot.AimYaw, false, Palette.Hud);
        if (Opt.Nav.On && Batmobile.Destination() is { } dest)
            Caret(cx, y, per, span, heading, Geo.YawOf(dest.At - fm.Position), true, Palette.Caution);

        string head = HeadingText(heading);
        float bs = Sc(0.6f);
        float bw = TextW(head, bs) + U(16), bh = TextH(bs) + U(6);
        Rect(cx - bw * 0.5f, y - bh - U(8), bw, bh, Palette.Screen, 0.8f);
        Outline(cx - bw * 0.5f, y - bh - U(8), bw, bh, Palette.Hud, 0.9f, U(1.2f));
        TextCentre(cx, y - bh * 0.5f - U(8), head, Palette.Hud, bs);
        Shapes.Triangle(cx - U(5), y - U(8), cx + U(5), y - U(8), cx, y - U(1), Palette.Hud, 1f);
    }

    private static void Caret(float cx, float y, float per, float span, float heading, float yaw, bool pin, Rgba col)
    {
        float off = Geo.WrapDegrees(Geo.Compass(yaw) - heading);
        float ax = cx + Math.Clamp(off, -span * 0.5f, span * 0.5f) * per;
        if (pin)
            Shapes.Triangle(ax - U(5), y + U(14), ax + U(5), y + U(14), ax, y + U(6), col, MathF.Abs(off) > span * 0.5f ? 0.5f : 1f);
        else if (MathF.Abs(off) < span * 0.5f)
            Outline(ax - U(4), y - U(9), U(8), U(6), col, 0.9f, U(1.2f));
    }

    private static string HeadingText(float deg) => ((int)MathF.Round(Geo.Compass(deg)) % 360).ToString("000", CultureInfo.InvariantCulture);

    private static void Status(Pilot pilot)
    {
        FlightModel fm = pilot.Craft.Fm;
        Weapons guns = pilot.Craft.Weapons;
        bool hover = fm.Hover > 0.5f;
        float cx = W * 0.5f;
        float y = H * 0.5f + U(Minimal ? 150f : 210f);
        float s = Sc(0.4f);

        string mode = pilot.Landed ? "LANDED" : fm.Boosting ? "AFTERBURNER" : hover ? "HOVER" : "WINGS";
        Rgba modeCol = fm.Boosting ? Palette.Caution : Palette.Hud;
        float mw = SpacedW(mode, Sc(0.5f), 2f);
        Spaced(cx - mw * 0.5f, y, mode, modeCol, Sc(0.5f), 2f);
        y += TextH(Sc(0.5f)) + U(10);

        bool armed = Opt.Weapons.On;
        int meters = 2 + (Opt.Crashes.On ? 1 : 0) + (armed ? 1 : 0);
        float mwid = U(96f), gap = U(18f);
        float mx = cx - (meters * mwid + (meters - 1) * gap) * 0.5f;
        Meter(mx, y, mwid, hover ? "JETS" : "THR", hover ? Math.Clamp(fm.Jets / 2f, 0f, 1f) : fm.Throttle, Palette.Hud, false);
        mx += mwid + gap;
        Meter(mx, y, mwid, "A/B", fm.Fuel, fm.Boosting ? Palette.Caution : Palette.Hud, false);
        mx += mwid + gap;
        if (Opt.Crashes.On)
        {
            Meter(mx, y, mwid, "HULL", pilot.Hull, pilot.Hull < 0.3f ? Palette.Warn : Palette.Hud, true);
            mx += mwid + gap;
        }

        if (armed)
        {
            Rgba heat = guns.Overheated ? Palette.Warn : Rgba.Mix(Palette.Hud, Palette.Warn, Math.Clamp((guns.Heat - 0.4f) / 0.6f, 0f, 1f));
            Meter(mx, y, mwid, guns.Overheated ? "GUN HOT" : "GUN", guns.Heat, heat, false);
        }

        // GTA style fires one weapon at a time, so the pad player needs to see which.
        if (armed && Opt.Gta)
        {
            y += U(26);
            string weapon = "WEAPON  " + Pilot.WeaponName(pilot.Armament);
            float ww = SpacedW(weapon, s, 2f);
            Spaced(cx - ww * 0.5f, y, weapon, Palette.Hud, s, 2f);
        }

        if (Minimal)
            return;

        y += U(26);
        string attitude = "PITCH " + Fmt.Signed(fm.Pitch() * Geo.Deg) + "   BANK " + Fmt.Signed(fm.Bank() * Geo.Deg) + "   G " + fm.GLoad.ToString("0.0", CultureInfo.InvariantCulture);
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

    private static void Alerts(Pilot pilot)
    {
        string? text = null;
        Rgba col = Palette.Warn;
        FlightModel fm = pilot.Craft.Fm;
        if (pilot.Waiting)
        {
            text = "WAITING FOR THE CITY TO LOAD // " + Prompt.Of(Act.Bail).Text + " TO JUMP NOW";
            col = Palette.Caution;
        }
        else if (pilot.ImpactIn < 2.5f)
        {
            text = "PULL UP";
        }
        else if (pilot.WeakSignal)
        {
            text = pilot.SignalLeft >= 0f ? "SIGNAL LOST IN " + Math.Max(1, (int)MathF.Ceiling(pilot.SignalLeft)) : "WEAK SIGNAL // TURN BACK";
            col = Palette.Caution;
        }
        else if (fm.Stalled)
        {
            text = "STALL";
        }
        else if (pilot.Outside)
        {
            text = pilot.TurningBack ? "TURNING BACK TO GOTHAM" : "LEAVING GOTHAM";
            col = Palette.Caution;
        }
        else if (Opt.Crashes.On && pilot.Hull < 0.25f)
        {
            text = "HULL CRITICAL";
        }
        else if (pilot.UnderFire)
        {
            text = "TAKING FIRE";
            col = Palette.Caution;
        }
        else if (pilot.NothingBelow)
        {
            text = pilot.LoadingBelow ? "GROUND STILL LOADING" : "NOTHING SOLID BELOW";
            col = Palette.Caution;
        }

        if (text == null || (int)(Clock.Now * 3.0) % 2 == 1)
            return;

        float s = Sc(0.9f);
        float cx = W * 0.5f, cy = H * 0.5f - U(150f);
        float w = TextW(text, s) + U(28), h = TextH(s) + U(10);
        Outline(cx - w * 0.5f, cy - h * 0.5f, w, h, col, 0.9f, U(1.5f));
        TextCentre(cx, cy, text, col, s);
    }

    private static void WeaponMarks(Pilot pilot)
    {
        if (!Opt.Weapons.On || !WorldView.Ready)
            return;

        Weapons guns = pilot.Craft.Weapons;
        float t = Thick;
        if (guns.AimFoe is RPawnVillain { IsValid: true } thug)
        {
            Brackets(thug.Location, U(11f), Palette.Caution, t);
            Tag(thug.Location, "RIOT ROUNDS", Palette.Caution, U(18f));
        }
        else if (guns.AimFoe is { IsValid: true } aimed && !guns.Locks.Any(l => l.Ptr == aimed.Ptr))
        {
            Brackets(aimed.Location, U(14f), Palette.Text, t);
        }

        if (guns.AimFoe is { IsValid: true } apc && Foes.Apc(apc) && guns.WeakSpot(apc) is { } weak)
        {
            Brackets(weak, U(8f), Palette.Caution, t);
            Tag(weak, "WEAK SPOT", Palette.Caution, U(14f));
        }

        Rgba lockCol = guns.LockFull ? Palette.Warn : Palette.Hud;
        var counted = new Dictionary<nint, int>();
        foreach (Actor l in guns.Locks)
            counted[l.Ptr] = counted.TryGetValue(l.Ptr, out int n) ? n + 1 : 1;

        var drawn = new HashSet<nint>();
        foreach (Actor foe in guns.Locks)
        {
            if (!foe.IsValid || !drawn.Add(foe.Ptr))
                continue;
            Brackets(foe.Location, U(20f), lockCol, t);
            if (counted[foe.Ptr] > 1)
                Tag(foe.Location, "x" + counted[foe.Ptr], lockCol, U(32f));
        }

        if ((guns.Locking || guns.Away > 0) && WorldView.ToScreen(pilot.Craft.Position + guns.Look * 30000f, out Vector2 aim))
            TextMid(aim.X + U(24f), aim.Y, guns.Locking ? guns.Locks.Count + " LOCKED" : guns.Away + " AWAY", lockCol, Sc(0.44f));
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

    // The flight controls for whichever device is in use, drawn with the real keys and buttons.
    private static void KeysCard(Pilot pilot)
    {
        bool pad = Controls.PadActive;
        bool hover = pilot.Craft.Fm.HoverWanted;
        bool direct = Opt.Direct;
        bool armed = Opt.Weapons.On;
        bool gta = pad && Opt.Gta;
        var rows = new List<(Prompt Key, string Does)>();
        if (gta)
        {
            rows.Add((Prompt.Keys("", Glyphs.FlyStick), hover ? "FLY" : "ROLL AND PITCH"));
            rows.Add((Prompt.Keys("", Glyphs.Name(PadButton.LB) + " " + Glyphs.Name(PadButton.RB)), hover ? "TURN" : "YAW"));
            rows.Add((Prompt.Keys("", Glyphs.RightTrigger + " " + Glyphs.LeftTrigger), hover ? "UP AND DOWN" : "THROTTLE AND BRAKE"));
            rows.Add((Prompt.Keys("", Glyphs.AimStick), "CAMERA"));
            rows.Add((Prompt.Of(Act.Boost), hover ? "FAST" : "AFTERBURNER"));
        }
        else if (pad)
        {
            rows.Add((Prompt.Keys("", Glyphs.AimStick), direct && !hover ? "STICK" : hover ? "TURN AND AIM" : "STEER"));
            rows.Add((Prompt.Keys("", Glyphs.FlyStick), hover ? "FLY" : direct ? "THROTTLE AND RUDDER" : "THROTTLE AND ROLL"));
            rows.Add((Prompt.Keys("", Glyphs.RightTrigger + " " + Glyphs.LeftTrigger), hover ? "UP AND DOWN" : "AFTERBURNER AND BRAKE"));
            if (hover)
                rows.Add((Prompt.Of(Act.Boost), "FAST"));
        }
        else
        {
            rows.Add((Prompt.Keys("MOUSE", ""), direct && !hover ? "STICK" : "AIM"));
            string wasd = Bind.KeyName(Controls.Key(Act.Forward)) + Bind.KeyName(Controls.Key(Act.Left)) + Bind.KeyName(Controls.Key(Act.Back)) + Bind.KeyName(Controls.Key(Act.Right));
            if (hover)
            {
                rows.Add((Prompt.Keys(wasd.Length <= 4 ? wasd : "MOVE KEYS", ""), "FLY"));
                rows.Add((Prompt.Keys(Bind.KeyName(Controls.Key(Act.Up)) + " " + Bind.KeyName(Controls.Key(Act.Down)), ""), "UP AND DOWN"));
                rows.Add((Prompt.Of(Act.Boost), "FAST"));
            }
            else
            {
                rows.Add((Prompt.Keys(Bind.KeyName(Controls.Key(Act.Forward)) + " " + Bind.KeyName(Controls.Key(Act.Back)), ""), "THROTTLE"));
                rows.Add((Prompt.Keys(Bind.KeyName(Controls.Key(Act.Left)) + " " + Bind.KeyName(Controls.Key(Act.Right)), ""), direct ? "RUDDER" : "ROLL"));
                rows.Add((Prompt.Of(Act.Boost), "AFTERBURNER"));
                rows.Add((Prompt.Of(Act.Down), "AIR BRAKE"));
            }
        }

        rows.Add((Prompt.Of(Act.Mode), hover ? "WINGS" : "HOVER"));
        if (armed && gta)
        {
            rows.Add((Prompt.Of(Act.Fire), "FIRE " + Pilot.WeaponName(pilot.Armament)));
            rows.Add((Prompt.Of(Act.Weapon), "SWITCH WEAPON"));
        }
        else if (armed)
        {
            rows.Add((Prompt.Of(Act.Gun), "GUNS"));
            rows.Add((Prompt.Of(Act.Cannon), "CANNON"));
            rows.Add((Prompt.Of(Act.Lock), "HOLD TO LOCK, LET GO FOR MISSILES"));
        }

        if (!gta)
            rows.Add((Prompt.Of(Act.Look), "LOOK AROUND"));
        rows.Add((Prompt.Of(Act.Camera), "CAMERA"));
        rows.Add((Prompt.Of(Act.Lift), pilot.Craft.Carrying ? "DROP BATMOBILE" : "HOOK BATMOBILE"));
        rows.Add((Prompt.Of(Act.Bail), pilot.Remote ? "END REMOTE" : "BAIL OUT"));
        if (pad && Wheel.ChordName.Length > 0)
            rows.Add((Prompt.Keys("", Wheel.ChordName), "COMMAND WHEEL"));
        if (!pad)
            rows.Add((Prompt.Of(Act.Hints), "HIDE THESE KEYS"));

        // Players found these too small to read, so the card runs bigger than the rest of the HUD. It still
        // shrinks if a long list wouldn't fit.
        string mode = hover ? "HOVER" : "WINGS";
        float k = KeysCardSize;
        float s = 0f, ts = 0f, lh = 0f, keyW = 0f, pad2 = 0f, w = 0f, h = 0f;
        float u(float v) => U(v) * k;

        void Measure()
        {
            s = Sc(0.42f) * k;
            ts = Sc(0.46f) * k;
            lh = TextH(s) + u(10);
            pad2 = u(14f);
            keyW = 0f;
            float actW = 0f;
            foreach ((Prompt key, string does) in rows)
            {
                keyW = MathF.Max(keyW, key.Width(s));
                actW = MathF.Max(actW, TextW(does, s));
            }

            float titleW = SpacedW("FLIGHT KEYS", ts, 2f) + u(16) + TextW(mode, s);
            w = pad2 * 2f + MathF.Max(keyW + u(10) + actW, titleW);
            h = u(30f) + rows.Count * lh + pad2;
        }

        Measure();
        float fit = MathF.Min((H - S(150f) - S(16f)) / h, (W - S(28f) * 2f) / w);
        if (fit < 1f)
        {
            k *= fit;
            Measure();
        }

        float x = S(28f), y = H - S(150f) - h;
        Rect(x, y, w, h, Palette.Screen, 0.82f);
        Rect(x, y, u(3), h, Palette.Hud, 0.9f);
        Spaced(x + pad2, y + u(8), "FLIGHT KEYS", Palette.Hud, ts, 2f);
        TextRight(x + w - pad2, y + u(8) + TextH(ts) * 0.5f, mode, Palette.Dim, s);
        Shapes.Under = Palette.Screen;
        float cy = y + u(30f) + lh * 0.5f;
        foreach ((Prompt key, string does) in rows)
        {
            float kw = key.Width(s);
            key.Draw(x + pad2 + (keyW - kw) * 0.5f, cy, s);
            TextMid(x + pad2 + keyW + u(10), cy, does, Palette.Dim, s);
            cy += lh;
        }

        Shapes.Under = Palette.Black;
    }
}
