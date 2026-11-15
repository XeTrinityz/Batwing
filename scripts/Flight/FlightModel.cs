using System.Numerics;

namespace Trinity.Batwing;

internal enum Assist
{
    Off,
    Standard,
    Full,
}

// How far mouse aim banks for a turn of a given size.
internal enum Banking
{
    Normal,
    Gentle,
    Sharp,
}

// What the pilot, or an autopilot, asks of the flight model for one step.
internal struct FlightInput
{
    // Where mouse aim wants the flight path to go. Zero means no aim.
    public Vector3 Aim;

    // Direct control on the wings: roll, pitch and yaw, -1 to 1.
    public Vector3 Stick;
    public bool Direct;

    // Direct control: the wings stay at the bank they're left at, and full assist only levels small banks.
    public bool HoldBank;

    // Manual roll on top of mouse aim, -1 to 1.
    public float Roll;

    // How fast to move the throttle, -1 to 1.
    public float Throttle;
    public bool Boost;
    public float Brake;

    // Hover: sideways and forward, -1 to 1. An autopilot may ask for more.
    public Vector2 Move;

    // Hover: climb or sink, -1 to 1. An autopilot may ask for more.
    public float Lift;

    // Hover: the heading to turn to, radians.
    public float Heading;

    // Hover: hold the spot and height it stopped at while the sticks are centred.
    public bool Hold;

    public Assist Assist;

    public Banking Banking;
}

// The Batwing's physics. It knows nothing about the game, so it can be run and tested on its own.
// Unreal units, centimetres and seconds. Body rates are roll, pitch and yaw, positive for right wing
// down, nose up and nose right.
internal sealed class FlightModel
{
    public const float Gravity = 1100f;
    public const float StallSpeed = 2600f;
    public const float StallAngle = 0.30f;
    public const float HoverSpeed = 2600f;
    public const float ClimbSpeed = 1300f;
    public const float GLimit = 9f;

    private const float NegativeGLimit = 3.5f;
    private const float ThrustToWeight = 1.25f;
    private const float BoostThrust = 2.0f;
    private const float InducedDrag = 3.2e-4f;
    private const float SideForce = 2.2e-4f;
    private const float SideDrag = 1.2e-3f;
    private const float BrakeDrag = 2.2e-5f;
    private const float MaxTilt = 0.55f;
    private const float ModeSeconds = 1.3f;
    private const float BoostSeconds = 5.5f;
    private const float RefillSeconds = 9f;
    private const float RefillDelay = 1.2f;
    private const float SubStep = 1f / 120f;

    // Mouse aim. The flight path turns towards the aim, the wings roll the lift round to where the turn
    // needs it, and the nose holds the angle of attack that lift takes.
    private const float PathGain = 1.7f;
    private const float RollGain = 4f;
    private const float RollDamp = 0.3f;
    private const float AlphaGain = 3.5f;
    private const float SlipGain = 3.5f;
    private const float TurnShare = 0.8f;

    // Corrections under the first angle are flat, over the second fully banked. Radians.
    private const float SkidBelow = 0.03f;
    private const float SkidFull = 0.14f;
    private const float SkidMost = 0.1f;

    // From the first angle off the aim big turns lean towards flat, by the second they're flat. Radians.
    private const float FlatFrom = 1.6f;
    private const float FlatFull = 2.4f;

    // The steepest bank mouse aim rolls to, radians.
    private const float MaxAimBank = 1.3f;

    // The bank mouse aim allows for a turn starts at the first figure and grows by the second with how far off
    // the aim is, radians per radian. Normal, gentle and sharp.
    private static readonly (float From, float Per)[] Banks = { (0.55f, 1.6f), (0.35f, 0.9f), (MaxAimBank, 0f) };

    // Under this the wings can't turn the path, so mouse aim points the nose instead.
    private const float PathFrom = StallSpeed * 0.4f;
    private const float PathFull = StallSpeed;

    private const float LevelGain = 2.5f;

    // With the bank held, banks under this still level out, radians.
    private const float HoldFrom = 0.25f;

    // Hover station keeping, the spring back to the held spot and height.
    private const float HoldSpring = 1.2f;
    private const float HoldPullMost = 600f;
    private const float HoldTakeBelow = 150f;
    private const float HeightSpring = 1.6f;
    private const float HeightPullMost = 400f;

    private static readonly Vector3 WingRates = new(3.6f, 1.45f, 0.6f);
    private static readonly Vector3 HoverRates = new(1.3f, 1.3f, 1.5f);

    // How long the rates take to follow what's asked, roll, pitch and yaw.
    private static readonly Vector3 SpinUp = new(0.12f, 0.18f, 0.22f);

    public float TopSpeed = 9000f;

    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Forward = Vector3.UnitX;
    public Vector3 Right = Vector3.UnitY;
    public Vector3 Up = Vector3.UnitZ;
    public Vector3 Rates;

    public float Throttle = 0.7f;

    // 0 on the wings, 1 in hover, eased between them.
    public float Hover;
    public bool HoverWanted;
    public float Fuel = 1f;

    public bool Boosting { get; private set; }
    public float Airspeed { get; private set; }
    public float Alpha { get; private set; }
    public float Beta { get; private set; }
    public float GLoad { get; private set; } = 1f;
    public bool Stalled { get; private set; }

    // How hard the lift jets work, 1 holds the weight.
    public float Jets { get; private set; }

    // Main engine output for the exhaust and sound, 0 to about 1.6.
    public float Engine { get; private set; }

    // How long it has been slow on the wings with the throttle back. Full assist drops into hover after a while.
    public float SlowFor { get; private set; }

    private float _sinceBoost = 99f;
    private float _thrust;
    private float _turnSide = 1f;
    private Vector3 _pathSpin;
    private Vector3 _lastDir = Vector3.UnitX;
    private Vector3? _holdAt;
    private float? _holdZ;
    private float? _heldBank;

    private float Drag0 => ThrustToWeight * Gravity / (TopSpeed * TopSpeed);

    private static float LiftSlope => Gravity / (StallSpeed * StallSpeed * StallAngle);

    public float Bank() => MathF.Atan2(-Right.Z, Up.Z);

    public float Pitch() => MathF.Asin(Math.Clamp(Forward.Z, -1f, 1f));

    public float Yaw() => MathF.Atan2(Forward.Y, Forward.X);

    public void Step(float dt, in FlightInput input)
    {
        if (!(dt > 0f))
            return;

        dt = MathF.Min(dt, 0.1f);
        int steps = Math.Max(1, (int)MathF.Ceiling(dt / SubStep));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
            Substep(h, input);
    }

    public void Place(Vector3 at, float yaw, Vector3 velocity)
    {
        Position = at;
        Velocity = velocity;
        Forward = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
        Right = new Vector3(-Forward.Y, Forward.X, 0f);
        Up = Vector3.UnitZ;
        Rates = Vector3.Zero;
        _lastDir = velocity.LengthSquared() > 1f ? Vector3.Normalize(velocity) : Forward;
        _pathSpin = Vector3.Zero;
        _heldBank = null;
        Release();
    }

    public void Orient(Vector3 forward, Vector3 right, Vector3 up)
    {
        Forward = forward;
        Right = right;
        Up = up;
        Orthonormalise();
    }

    // Lets go of the held hover spot, for when something else moved it.
    public void Release()
    {
        _holdAt = null;
        _holdZ = null;
    }

    // Bounces off a surface with normal n. Returns how fast it was going into it.
    public float Bump(Vector3 n, float bounce, float friction)
    {
        float into = Vector3.Dot(Velocity, n);
        if (into >= 0f)
            return 0f;

        Vector3 slide = Velocity - n * into;
        Velocity = slide * friction - n * (into * bounce);
        Release();

        float kick = Math.Clamp(-into / 3000f, 0f, 1.5f);
        Rates.Y += Vector3.Dot(n, Up) * kick * 1.5f;
        Rates.Z -= Vector3.Dot(n, Right) * kick * 1.2f;
        Rates.X += (Vector3.Dot(n, Right) >= 0f ? -1f : 1f) * kick * 2f;
        return -into;
    }

    private void Substep(float dt, in FlightInput input)
    {
        // A bank held from before hover or another control mode is stale.
        if (!input.HoldBank || HoverWanted)
            _heldBank = null;

        Hover = Approach(Hover, HoverWanted ? 1f : 0f, dt / ModeSeconds);
        float hover = Smooth(Hover);
        float wings = 1f - hover;

        Throttle = Math.Clamp(Throttle + input.Throttle * 0.7f * dt, 0f, 1f);
        UpdateFuel(dt, input.Boost && hover < 0.5f);

        Vector3 accel = new(0f, 0f, -Gravity);
        accel += Aero(input.Brake, wings);
        float thrust = ThrustToWeight * Gravity * Throttle * (Boosting ? BoostThrust : 1f);
        _thrust = thrust * wings;
        accel += Forward * _thrust;
        Engine = Throttle * (Boosting ? 1.6f : 1f) * wings;

        Vector3 rateCmd = Vector3.Zero;
        Jets = 0f;
        if (hover > 0.001f)
            accel += HoverPush(input, hover, out rateCmd);
        else
            Release();

        // Below stall speed with assist on, the lift jets take some of the weight so it sinks instead of dropping.
        if (input.Assist != Assist.Off && wings > 0.001f)
        {
            float support = Math.Clamp(1f - Airspeed / (StallSpeed * 1.15f), 0f, 1f);
            support *= support * 0.85f;
            accel.Z += Gravity * support * wings;
            Jets = MathF.Max(Jets, support);
        }

        if (wings > 0.001f)
        {
            Vector3 wingRates = input.Direct ? StickRates(input) : AimRates(input);
            rateCmd = hover > 0.001f ? Vector3.Lerp(rateCmd, wingRates, wings) : wingRates;
        }

        Turn(dt, rateCmd, wings);

        Velocity += accel * dt;
        Position += Velocity * dt;

        // How fast the flight path itself is turning, for the stall protection.
        float moving = Velocity.Length();
        if (moving > 1f)
        {
            Vector3 dir = Velocity / moving;
            _pathSpin = Vector3.Cross(_lastDir, dir) / dt;
            _lastDir = dir;
        }

        Vector3 felt = accel + new Vector3(0f, 0f, Gravity);
        GLoad = Vector3.Dot(felt, Up) / Gravity;

        bool slow = input.Assist == Assist.Full && !HoverWanted && Airspeed < StallSpeed && Throttle < 0.3f;
        SlowFor = slow ? SlowFor + dt : 0f;
    }

    private void UpdateFuel(float dt, bool wanted)
    {
        Boosting = wanted && Fuel > 0f;
        if (Boosting)
        {
            Fuel = MathF.Max(0f, Fuel - dt / BoostSeconds);
            _sinceBoost = 0f;
            return;
        }

        _sinceBoost += dt;
        if (_sinceBoost > RefillDelay)
            Fuel = MathF.Min(1f, Fuel + dt / RefillSeconds);
    }

    private Vector3 Aero(float brake, float wings)
    {
        float speed = Velocity.Length();
        Airspeed = speed;
        if (speed < 1f)
        {
            Alpha = Beta = 0f;
            Stalled = false;
            return Vector3.Zero;
        }

        Vector3 dir = Velocity / speed;
        float along = Vector3.Dot(Velocity, Forward);
        Alpha = MathF.Atan2(-Vector3.Dot(Velocity, Up), along);
        Beta = MathF.Atan2(Vector3.Dot(Velocity, Right), along);
        float q = speed * speed;

        // Lift grows with the angle up to the stall, then falls away.
        float a = MathF.Abs(Alpha);
        float cl = a <= StallAngle ? a : StallAngle * MathF.Max(0.25f, 1f - (a - StallAngle) * 2.2f);
        Stalled = a > StallAngle * 1.1f && speed > 300f && wings > 0.5f;
        Vector3 lift = Across(Up, dir) * (LiftSlope * q * cl * MathF.Sign(Alpha));

        float b = Math.Clamp(Beta, -0.6f, 0.6f);
        lift -= Across(Right, dir) * (SideForce * q * MathF.Sin(b));
        lift *= 1f - 0.7f * (1f - wings);

        float up = Vector3.Dot(lift, Up);
        float limit = (up >= 0f ? GLimit : NegativeGLimit) * Gravity;
        float size = lift.Length();
        if (size > limit)
            lift *= limit / size;

        float sa = MathF.Sin(MathF.Min(a, 1.2f));
        float sb = MathF.Sin(MathF.Abs(b));
        float cd = Drag0 + InducedDrag * sa * sa + SideDrag * sb * sb + BrakeDrag * Math.Clamp(brake, 0f, 1f);
        return lift - dir * (q * cd);
    }

    private Vector3 AimRates(in FlightInput input)
    {
        Vector3 rates;
        if (input.Aim.LengthSquared() < 0.5f)
        {
            rates = new Vector3(Level(input.Assist), 0f, 0f);
        }
        else
        {
            Vector3 aim = Vector3.Normalize(input.Aim);
            float path = Math.Clamp((Airspeed - PathFrom) / (PathFull - PathFrom), 0f, 1f);
            Vector3 nose = NoseRates(aim);
            rates = path <= 0f ? nose : Vector3.Lerp(nose, PathRates(aim, input.Assist, input.Banking), Smooth(path));
        }

        // Leaning on the roll keys takes over the roll bit by bit, so a light touch keeps the auto bank.
        float manual = Math.Clamp((MathF.Abs(input.Roll) - 0.1f) / 0.4f, 0f, 1f);
        rates.X += (input.Roll * WingRates.X - rates.X) * manual;
        return Limit(Clamp(rates, WingRates), input.Assist);
    }

    // Points the nose at the aim, for when it's too slow to turn on its wings.
    private Vector3 NoseRates(Vector3 aim)
    {
        float dx = Vector3.Dot(aim, Forward);
        float dy = Vector3.Dot(aim, Right);
        float dz = Vector3.Dot(aim, Up);
        return new Vector3(-Bank() * LevelGain, 2.2f * MathF.Atan2(dz, dx), 1.6f * MathF.Atan2(dy, dx));
    }

    private Vector3 PathRates(Vector3 aim, Assist assist, Banking banking)
    {
        float speed = MathF.Max(Airspeed, 1f);
        Vector3 d = Velocity / speed;
        float cos = Math.Clamp(Vector3.Dot(d, aim), -1f, 1f);
        float err = MathF.Acos(cos);
        Vector3 n = aim - d * cos;
        float nl = n.Length();
        n = nl > 1e-4f ? n / nl : Vector3.Zero;

        // Far off the aim the shortest way round can be a loop, and with the aim right behind it flips
        // between loops and turns. A flat turn is what's wanted, so big turns lean that way.
        float flatten = Smooth((err - FlatFrom) / (FlatFull - FlatFrom)) * (1f - MathF.Abs(aim.Z));
        if (flatten > 0f && Flat(d) is { } heading && Flat(aim) is { } toward)
        {
            float cross = heading.X * toward.Y - heading.Y * toward.X;
            if (MathF.Abs(cross) > 0.02f)
                _turnSide = MathF.Sign(cross);
            Vector3 level = new Vector3(-heading.Y, heading.X, 0f) * _turnSide;
            level -= d * Vector3.Dot(level, d);
            if (level.LengthSquared() > 1e-4f)
                n = Vector3.Normalize(Vector3.Lerp(n, Vector3.Normalize(level), flatten));
        }

        // How fast the path should turn, no more than the wings can pull at this speed.
        float most = GLimit * TurnShare * Gravity / MathF.Max(speed, StallSpeed);
        float turn = MathF.Min(err * PathGain, most);

        Vector3 want = n * (speed * turn);
        var g = new Vector3(0f, 0f, -Gravity);
        Vector3 hold = -(g - d * Vector3.Dot(d, g));

        // The rudder takes part of the turn by yawing the nose into it. Small corrections are all rudder, the way
        // you'd line up a shot. Bigger turns bank, but only as far as suits the size of the turn, and the rudder
        // makes up the rest so they still come round quickly.
        (float from, float per) = Banks[(int)banking];
        float bankMost = Math.Clamp(from + err * per, from, MaxAimBank);
        float limit = Gravity * MathF.Tan(bankMost);
        Vector3 side = Right - d * Vector3.Dot(Right, d);
        float sideLength = side.Length();
        Vector3 flatSide = Vector3.Cross(Vector3.UnitZ, d);
        float wantBeta = 0f;
        Vector3 rudder = Vector3.Zero;
        if (sideLength > 1e-3f)
        {
            side /= sideLength;
            float flat = 1f - Smooth((err - SkidBelow) / (SkidFull - SkidBelow));
            float skid = Vector3.Dot(want, side) * flat;
            if (flatSide.LengthSquared() > 0.01f)
            {
                flatSide = Vector3.Normalize(flatSide);
                float lateral = Vector3.Dot(want - side * skid, flatSide);
                if (MathF.Abs(lateral) > limit)
                    skid += (lateral - MathF.Sign(lateral) * limit) * Vector3.Dot(flatSide, side);
            }

            wantBeta = Math.Clamp(-skid / (SideForce * speed * speed), -SkidMost, SkidMost);
            rudder = side * (-wantBeta * SideForce * speed * speed);
        }

        // The lift does the rest of the turn and holds it up against gravity. Whatever the rudder pushes up or
        // down when banked is taken back out, so turns hold their height.
        Vector3 lift = want - rudder + hold;
        if (flatSide.LengthSquared() > 0.5f)
        {
            float lateral = Vector3.Dot(lift, flatSide);
            if (MathF.Abs(lateral) > limit)
                lift -= flatSide * (lateral - MathF.Sign(lateral) * limit);
        }

        // The path turns as fast as the lift and rudder together take it.
        Vector3 spinNow = Vector3.Cross(d, lift - hold + rudder) / speed;
        Vector3 across = lift - Forward * Vector3.Dot(lift, Forward);
        float size = across.Length();
        float roll = MathF.Atan2(Vector3.Dot(across, Right), Vector3.Dot(across, Up));

        // Lift wanted below the horizon means pushing the nose down, never rolling over to pull. The push is held
        // to what the wings take by the angle of attack limit, so a steep dive just takes a moment longer. Lift that
        // is only under the wings because they're banked, like turning back out of a turn, means rolling across.
        float sign = 1f;
        if (MathF.Abs(roll) > MathF.PI * 0.5f && lift.Z < 0f)
        {
            roll -= MathF.PI * MathF.Sign(roll);
            sign = -1f;
        }

        // Mouse aim never banks past the most a flat turn needs. Upside down, like over the top of a loop, it
        // rolls back upright.
        float bank = Bank();
        roll = Math.Clamp(bank + roll, -bankMost, bankMost) - bank;

        // Only pulls as hard as the wings are rolled round to where the lift has to go.
        // The engine pushes along the nose, so with the nose up it gives some lift as well.
        float lined = MathF.Max(0f, MathF.Cos(roll));
        float wantAlpha = sign * size * lined / (LiftSlope * speed * speed + _thrust);
        float top = assist == Assist.Off ? StallAngle * 1.6f : StallAngle * 0.9f;
        wantAlpha = Math.Clamp(wantAlpha, -top * 0.7f, top);

        // The body turns with the path, as far as the wings are rolled in to turn it, plus what it takes to fix
        // the roll, the angle of attack and the slip. The airflow's own pull on the nose is cancelled out, or
        // the nose would settle a little off where it's asked.
        Vector3 spin = spinNow * lined;
        float pull = AirflowPull();
        float p = -Vector3.Dot(spin, Forward) + RollGain * roll - RollDamp * Rates.X;
        float q = -Vector3.Dot(spin, Right) + AlphaGain * (wantAlpha - Alpha) + 5f * pull * Alpha * SpinUp.Y;
        float r = Vector3.Dot(spin, Up) + SlipGain * (Beta - wantBeta) - 4f * pull * Beta * SpinUp.Z;
        return new Vector3(p, q, r);
    }

    private Vector3 StickRates(in FlightInput input)
    {
        Vector3 rates = input.Stick * WingRates;
        if (input.HoldBank)
        {
            // The roll eases over from the stick to the bank hold, so a thumb resting near the middle doesn't
            // flick between the two.
            float hold = HeldRoll(input.Assist, MathF.Abs(input.Stick.X) < 0.02f);
            rates.X += (hold - rates.X) * (1f - Smooth(MathF.Abs(input.Stick.X) / 0.12f));
        }
        else if (input.Assist == Assist.Full && MathF.Abs(input.Stick.X) < 0.05f)
        {
            rates.X = Level(input.Assist);
        }

        // Assist keeps the turn coordinated, so the rudder isn't needed to stop it skidding.
        if (input.Assist != Assist.Off && MathF.Abs(input.Stick.Z) < 0.05f)
            rates.Z += SlipGain * Beta * Math.Clamp((Airspeed - PathFrom) / (PathFull - PathFrom), 0f, 1f);

        return Limit(Clamp(rates, WingRates), input.Assist);
    }

    private float Level(Assist assist) => assist == Assist.Full ? -Bank() * LevelGain : 0f;

    // Keeps the bank the stick left it at. Pulling round in a bank tilts the wings against the horizon, so a
    // roll rate of nothing would still let the bank creep. Full assist levels small banks. Near the vertical the
    // bank reads half a turn out, so it isn't held there or a loop would roll over at the top.
    private float HeldRoll(Assist assist, bool free)
    {
        if (!free || assist == Assist.Off || MathF.Abs(Forward.Z) > 0.85f)
        {
            _heldBank = null;
            return 0f;
        }

        float bank = Bank();
        _heldBank ??= bank;
        float want = assist == Assist.Full && MathF.Abs(_heldBank.Value) < HoldFrom ? 0f : _heldBank.Value;
        return WrapPi(want - bank) * LevelGain;
    }

    // Stall protection. The pitch is held to what keeps the angle of attack under the stall, and past it
    // the nose is pushed back down, since a falling path raises the angle of attack with no pitch at all.
    private Vector3 Limit(Vector3 rates, Assist assist)
    {
        if (assist == Assist.Off || Airspeed < 300f)
            return rates;

        float edge = StallAngle * 0.85f;
        float follow = -Vector3.Dot(_pathSpin, Right);
        float most = follow + AlphaGain * (edge - Alpha);
        float least = follow + AlphaGain * (-edge * 0.7f - Alpha);
        rates.Y = Math.Clamp(rates.Y, MathF.Min(least, most), MathF.Max(least, most));
        return rates;
    }

    private Vector3 HoverPush(in FlightInput input, float hover, out Vector3 rates)
    {
        var face = new Vector3(MathF.Cos(input.Heading), MathF.Sin(input.Heading), 0f);
        var side = new Vector3(-face.Y, face.X, 0f);
        float speed = HoverSpeed * (input.Boost ? 1.8f : 1f);
        Vector3 wantFlat = (face * input.Move.Y + side * input.Move.X) * speed;
        var flat = new Vector3(Velocity.X, Velocity.Y, 0f);

        // With the sticks centred it takes the spot it came to rest on, and holds it against any drift.
        bool still = input.Hold && input.Move.LengthSquared() < 1e-4f;
        if (!still)
            _holdAt = null;
        else if (_holdAt == null && flat.Length() < HoldTakeBelow)
            _holdAt = Position;
        if (_holdAt is { } spot)
        {
            var back = new Vector3(spot.X - Position.X, spot.Y - Position.Y, 0f) * HoldSpring;
            float pull = back.Length();
            wantFlat += pull > HoldPullMost ? back * (HoldPullMost / pull) : back;
        }

        // Leans into the way it wants to go. Past the most it can lean, braking gets help from the jets.
        Vector3 lean = (wantFlat - flat) * 1.7f;
        float tiltMax = Gravity * MathF.Tan(MaxTilt);
        Vector3 vectored = Vector3.Zero;
        float leanSize = lean.Length();
        if (leanSize > tiltMax)
        {
            bool braking = Vector3.Dot(lean, flat) < 0f;
            float most = flat.Length() > HoverSpeed * 1.2f ? 1.4f : 0.5f;
            float extra = braking ? MathF.Min(leanSize - tiltMax, most * Gravity) : 0f;
            vectored = lean / leanSize * extra;
            lean *= tiltMax / leanSize;
        }

        float wantVz = input.Lift * ClimbSpeed;
        bool level = input.Hold && MathF.Abs(input.Lift) < 0.05f;
        if (!level)
            _holdZ = null;
        else if (_holdZ == null && MathF.Abs(Velocity.Z) < HoldTakeBelow)
            _holdZ = Position.Z;
        if (_holdZ is { } height)
            wantVz += Math.Clamp((height - Position.Z) * HeightSpring, -HeightPullMost, HeightPullMost);

        float climb = Math.Clamp((wantVz - Velocity.Z) * 3.2f, -0.95f * Gravity, Gravity);
        float holdUp = Gravity + climb;

        Vector3 wantUp = Vector3.Normalize(lean + new Vector3(0f, 0f, Gravity));
        float turn = WrapPi(input.Heading - Yaw());
        Vector3 w = Vector3.Cross(Up, wantUp) * 3.4f + Vector3.UnitZ * (turn * 2.4f);
        var want = new Vector3(-Vector3.Dot(w, Forward), -Vector3.Dot(w, Right), Vector3.Dot(w, Up));
        rates = Clamp(want - Rates * 0.3f, HoverRates);

        float upright = MathF.Max(Up.Z, 0.45f);
        float push = holdUp / upright;
        Jets = push / Gravity;
        return (Up * push + vectored) * hover;
    }

    private void Turn(float dt, Vector3 cmd, float wings)
    {
        // Slow on the wings there's little air over the control surfaces, so the turn rates drop off.
        float authority = Math.Clamp((Airspeed - StallSpeed * 0.35f) / (StallSpeed * 0.9f), 0.2f, 1f);
        cmd *= wings * authority + (1f - wings);

        Rates.X += (cmd.X - Rates.X) * Follow(dt, SpinUp.X);
        Rates.Y += (cmd.Y - Rates.Y) * Follow(dt, SpinUp.Y);
        Rates.Z += (cmd.Z - Rates.Z) * Follow(dt, SpinUp.Z);

        // The airflow turns the nose back into it, harder the faster it goes.
        float q = AirflowPull();
        Rates.Y += -Alpha * 5f * q * wings * dt;
        Rates.Z += Beta * 4f * q * wings * dt;

        Vector3 omega = Up * Rates.Z - Right * Rates.Y - Forward * Rates.X;
        float angle = omega.Length() * dt;
        if (angle > 1e-6f)
        {
            Quaternion spin = Quaternion.CreateFromAxisAngle(omega / omega.Length(), angle);
            Forward = Vector3.Transform(Forward, spin);
            Right = Vector3.Transform(Right, spin);
        }

        Orthonormalise();
    }

    private float AirflowPull() => Math.Clamp(Airspeed * Airspeed / (TopSpeed * TopSpeed), 0f, 1.5f) + (Stalled ? 0.35f : 0f);

    private void Orthonormalise()
    {
        Forward = Vector3.Normalize(Forward);
        Right = Vector3.Normalize(Right - Forward * Vector3.Dot(Right, Forward));
        Up = Vector3.Cross(Forward, Right);
    }

    private static Vector3? Flat(Vector3 v)
    {
        var f = new Vector3(v.X, v.Y, 0f);
        float l = f.Length();
        return l > 1e-3f ? f / l : null;
    }

    private static Vector3 Across(Vector3 axis, Vector3 dir)
    {
        Vector3 v = axis - dir * Vector3.Dot(axis, dir);
        float l = v.Length();
        return l > 1e-4f ? v / l : Vector3.Zero;
    }

    public static float WrapPi(float a)
    {
        a %= MathF.Tau;
        return a > MathF.PI ? a - MathF.Tau : a < -MathF.PI ? a + MathF.Tau : a;
    }

    private static float Follow(float dt, float seconds) => 1f - MathF.Exp(-dt / MathF.Max(seconds, 1e-3f));

    private static float Approach(float v, float target, float step) => v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Vector3 Clamp(Vector3 v, Vector3 max) =>
        new(Math.Clamp(v.X, -max.X, max.X), Math.Clamp(v.Y, -max.Y, max.Y), Math.Clamp(v.Z, -max.Z, max.Z));
}
