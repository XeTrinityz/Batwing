using System.Numerics;

namespace Trinity.Batwing;

internal struct FlightInput
{
    public Vector3 Aim;

    public Vector3 Stick;

    public bool Direct;

    public float Roll;

    public float Throttle;

    public bool Boost;

    public float Brake;

    public Vector2 Move;

    public float Lift;

    public float Heading;

    public bool Assist;
}

internal sealed class FlightModel
{
    public float Gravity = 1100f;

    public float TopSpeed = 9000f;

    public float ThrustToWeight = 1.25f;
    public float BoostThrust = 2.0f;

    public float StallSpeed = 2600f;

    public const float StallAngle = 0.30f;

    public float InducedDrag = 3.2e-4f;
    public float SideForce = 2.2e-4f;
    public float SideDrag = 1.2e-3f;
    public float BrakeDrag = 2.2e-5f;
    public float GLimit = 9f;

    public float NegativeGLimit = 3.5f;

    public float HoverSpeed = 2600f;
    public float ClimbSpeed = 1300f;

    public float MaxTilt = 0.55f;

    public float ModeSeconds = 1.3f;

    public float AimPitchGain = 2.4f;
    public float AimDamping = 0.5f;
    public float AimRollGain = 3f;

    public float AimTurnGain = 0.8f;

    public float MaxAimBank = 1.3f;

    public float BoostSeconds = 5.5f;
    public float RefillSeconds = 9f;

    private static readonly Vector3 WingRates = new(3.6f, 1.45f, 0.6f);
    private static readonly Vector3 HoverRates = new(1.3f, 1.3f, 1.5f);
    private static readonly Vector3 SpinUp = new(0.16f, 0.24f, 0.36f);

    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Forward = Vector3.UnitX;
    public Vector3 Right = Vector3.UnitY;
    public Vector3 Up = Vector3.UnitZ;

    public Vector3 Rates;

    public float Throttle = 0.7f;

    public float Hover;

    public bool HoverWanted;
    public float Fuel = 1f;
    public bool Boosting;

    public float Airspeed { get; private set; }
    public float Alpha { get; private set; }
    public float Beta { get; private set; }
    public float GLoad { get; private set; } = 1f;
    public bool Stalled { get; private set; }

    public float Jets { get; private set; }

    public float Engine { get; private set; }

    public float SlowFor { get; private set; }

    private float _sinceBoost = 99f;
    private float _rollSide = 1f;

    private float Drag0 => ThrustToWeight * Gravity / (TopSpeed * TopSpeed);

    private float LiftSlope => Gravity / (StallSpeed * StallSpeed * StallAngle);

    public void Step(float dt, in FlightInput input)
    {
        if (!(dt > 0f))
            return;

        dt = MathF.Min(dt, 0.1f);
        int steps = Math.Max(1, (int)MathF.Ceiling(dt / (1f / 120f)));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
            Substep(h, input);
    }

    private void Substep(float dt, in FlightInput input)
    {
        float target = HoverWanted ? 1f : 0f;
        Hover = Approach(Hover, target, dt / ModeSeconds);
        float hover = Ease(Hover);
        float wings = 1f - hover;

        Throttle = Math.Clamp(Throttle + input.Throttle * 0.7f * dt, 0f, 1f);
        UpdateFuel(dt, input.Boost && hover < 0.5f);

        Vector3 accel = new(0f, 0f, -Gravity);
        accel += Aero(input.Brake, wings, out Vector3 aero);
        float thrust = ThrustToWeight * Gravity * Throttle * (Boosting ? BoostThrust : 1f);
        accel += Forward * (thrust * wings);
        Engine = Throttle * (Boosting ? 1.6f : 1f) * wings;

        Vector3 rateCmd;
        if (hover > 0.001f)
        {
            accel += HoverPush(dt, input, hover, out Vector3 hoverRates);
            rateCmd = hoverRates;
        }
        else
        {
            Jets = 0f;
            rateCmd = Vector3.Zero;
        }

        if (input.Assist && wings > 0.001f)
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

        Vector3 felt = accel + new Vector3(0f, 0f, Gravity);
        GLoad = Vector3.Dot(felt, Up) / Gravity;

        bool slow = input.Assist && !HoverWanted && Airspeed < StallSpeed && Throttle < 0.3f;
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
        if (_sinceBoost > 1.2f)
            Fuel = MathF.Min(1f, Fuel + dt / RefillSeconds);
    }

    private Vector3 Aero(float brake, float wings, out Vector3 lift)
    {
        lift = Vector3.Zero;
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

        float a = MathF.Abs(Alpha);
        float cl = a <= StallAngle ? a : StallAngle * MathF.Max(0.25f, 1f - (a - StallAngle) * 2.2f);
        Stalled = a > StallAngle * 1.1f && speed > 300f && wings > 0.5f;
        Vector3 liftDir = Across(Up, dir);
        lift = liftDir * (LiftSlope * q * cl * MathF.Sign(Alpha));

        float b = Math.Clamp(Beta, -0.6f, 0.6f);
        Vector3 sideDir = Across(Right, dir);
        lift -= sideDir * (SideForce * q * MathF.Sin(b));

        lift *= 1f - 0.7f * (1f - wings);

        float up = Vector3.Dot(lift, Up);
        float limit = (up >= 0f ? GLimit : NegativeGLimit) * Gravity;
        float size = lift.Length();
        if (size > limit)
            lift *= limit / size;

        float sa = MathF.Sin(MathF.Min(a, 1.2f));
        float sb = MathF.Sin(MathF.Abs(b));
        float cd = Drag0 + InducedDrag * sa * sa + SideDrag * sb * sb + BrakeDrag * Math.Clamp(brake, 0f, 1f);
        Vector3 drag = -dir * (q * cd);
        return lift + drag;
    }

    private static Vector3 Across(Vector3 axis, Vector3 dir)
    {
        Vector3 v = axis - dir * Vector3.Dot(axis, dir);
        float l = v.Length();
        return l > 1e-4f ? v / l : Vector3.Zero;
    }

    private Vector3 AimRates(in FlightInput input)
    {
        Vector3 aim = input.Aim;
        float bank = Bank();
        if (aim.LengthSquared() < 0.5f)
            return Clamp(new Vector3(-bank * 2.5f + input.Roll * WingRates.X, 0f, 0f), WingRates);

        float dx = Vector3.Dot(aim, Forward);
        float dy = Vector3.Dot(aim, Right);
        float dz = Vector3.Dot(aim, Up);

        float flying = Math.Clamp((Airspeed - StallSpeed * 0.5f) / StallSpeed, 0f, 1f);
        float alpha = Math.Clamp(Alpha, -StallAngle, StallAngle) * flying;
        float beta = Math.Clamp(Beta, -0.3f, 0.3f) * flying;
        float q = AirflowPull();
        float pitch = AimPitchGain * (MathF.Atan2(dz, dx) + alpha) - AimDamping * Rates.Y + 5f * q * Alpha * SpinUp.Y;
        if (pitch < 0f)
            pitch *= 0.6f;
        float yaw = 1.4f * (MathF.Atan2(dy, dx) - beta) - AimDamping * Rates.Z - 4f * q * Beta * SpinUp.Z;

        if (MathF.Abs(dy) > 0.02f)
            _rollSide = MathF.Sign(dy);
        Vector3 heading = Airspeed > 500f ? Velocity : Forward;
        float roll;
        if (dz + 0.8f < 0f)
        {
            float lateral = MathF.Abs(dy) < 0.02f ? 0.02f * _rollSide : dy;
            roll = AimRollGain * MathF.Atan2(lateral, dz + 0.8f);
        }
        else
        {
            float turnErr = FlatAngle(heading, aim);
            float speed = MathF.Max(Airspeed, StallSpeed);
            float mostRate = GLimit * 0.75f * Gravity / speed;
            float turnRate = Math.Clamp(turnErr * AimTurnGain, -mostRate, mostRate);
            float wantBank = Math.Clamp(MathF.Atan(turnRate * speed / Gravity), -MaxAimBank, MaxAimBank);
            roll = AimRollGain * WrapPi(wantBank - bank) - 0.25f * Rates.X;
        }

        // Blend toward manual roll so a light touch on a stick doesn't throw away the auto bank.
        float manual = Math.Clamp((MathF.Abs(input.Roll) - 0.1f) / 0.4f, 0f, 1f);
        roll += (input.Roll * WingRates.X - roll) * manual;

        return Limit(Clamp(new Vector3(roll, pitch, yaw), WingRates), input.Assist);
    }

    private Vector3 StickRates(in FlightInput input)
    {
        Vector3 rates = input.Stick * WingRates;
        if (input.Assist && MathF.Abs(input.Stick.X) < 0.05f)
            rates.X = -Bank() * 2f;
        return Limit(Clamp(rates, WingRates), input.Assist);
    }

    private Vector3 Limit(Vector3 rates, bool assist)
    {
        if (!assist)
            return rates;

        float edge = StallAngle * 0.8f;
        if (rates.Y > 0f && Alpha > edge)
            rates.Y *= Math.Clamp((StallAngle - Alpha) / (StallAngle - edge), 0f, 1f);
        else if (rates.Y < 0f && Alpha < -edge)
            rates.Y *= Math.Clamp((StallAngle + Alpha) / (StallAngle - edge), 0f, 1f);
        return rates;
    }

    public float Bank() => MathF.Atan2(-Right.Z, Up.Z);

    public float Pitch() => MathF.Asin(Math.Clamp(Forward.Z, -1f, 1f));

    private Vector3 HoverPush(float dt, in FlightInput input, float hover, out Vector3 rates)
    {
        var face = new Vector3(MathF.Cos(input.Heading), MathF.Sin(input.Heading), 0f);
        var side = new Vector3(-face.Y, face.X, 0f);
        float speed = HoverSpeed * (input.Boost ? 1.8f : 1f);
        Vector3 wantFlat = (face * input.Move.Y + side * input.Move.X) * speed;
        var flat = new Vector3(Velocity.X, Velocity.Y, 0f);

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

        float climb = Math.Clamp((input.Lift * ClimbSpeed - Velocity.Z) * 2.6f, -0.8f * Gravity, 0.9f * Gravity);
        float holdUp = Gravity + climb;

        Vector3 wantUp = Vector3.Normalize(lean + new Vector3(0f, 0f, Gravity));
        float turn = WrapPi(input.Heading - MathF.Atan2(Forward.Y, Forward.X));
        Vector3 w = Vector3.Cross(Up, wantUp) * 3.4f + Vector3.UnitZ * (turn * 2.4f);
        var want = new Vector3(-Vector3.Dot(w, Forward), -Vector3.Dot(w, Right), Vector3.Dot(w, Up));
        rates = Clamp(want - Rates * 0.3f, HoverRates);

        float upright = MathF.Max(Up.Z, 0.45f);
        float push = holdUp / upright;
        Jets = push / Gravity;
        return (Up * push + vectored) * hover;
    }

    private static float FlatAngle(Vector3 from, Vector3 to)
    {
        if (from.X * from.X + from.Y * from.Y < 1e-6f || to.X * to.X + to.Y * to.Y < 1e-6f)
            return 0f;
        return WrapPi(MathF.Atan2(to.Y, to.X) - MathF.Atan2(from.Y, from.X));
    }

    private static float WrapPi(float a)
    {
        a %= MathF.Tau;
        return a > MathF.PI ? a - MathF.Tau : a < -MathF.PI ? a + MathF.Tau : a;
    }

    private void Turn(float dt, Vector3 cmd, float wings)
    {
        float authority = Math.Clamp((Airspeed - StallSpeed * 0.35f) / (StallSpeed * 0.9f), 0.2f, 1f);
        float give = wings * authority + (1f - wings);
        cmd *= give;

        Rates.X += (cmd.X - Rates.X) * Follow(dt, SpinUp.X);
        Rates.Y += (cmd.Y - Rates.Y) * Follow(dt, SpinUp.Y);
        Rates.Z += (cmd.Z - Rates.Z) * Follow(dt, SpinUp.Z);

        float q = AirflowPull();
        Rates.Y += -Alpha * 5f * q * wings * dt;
        Rates.Z += Beta * 4f * q * wings * dt;

        Vector3 omega = Up * Rates.Z - Right * Rates.Y - Forward * Rates.X;
        float angle = omega.Length() * dt;
        if (angle > 1e-6f)
        {
            Quaternion turn = Quaternion.CreateFromAxisAngle(omega / omega.Length(), angle);
            Forward = Vector3.Transform(Forward, turn);
            Right = Vector3.Transform(Right, turn);
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

    public void Place(Vector3 at, float yaw, Vector3 velocity)
    {
        Position = at;
        Velocity = velocity;
        Forward = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
        Right = new Vector3(-Forward.Y, Forward.X, 0f);
        Up = Vector3.UnitZ;
        Rates = Vector3.Zero;
    }

    public float Bump(Vector3 n, float bounce, float friction)
    {
        float into = Vector3.Dot(Velocity, n);
        if (into >= 0f)
            return 0f;

        Vector3 slide = Velocity - n * into;
        Velocity = slide * friction - n * (into * bounce);

        float kick = Math.Clamp(-into / 3000f, 0f, 1.5f);
        Rates.Y += Vector3.Dot(n, Up) * kick * 1.5f;
        Rates.Z -= Vector3.Dot(n, Right) * kick * 1.2f;
        Rates.X += (Vector3.Dot(n, Right) >= 0f ? -1f : 1f) * kick * 2f;
        return -into;
    }

    private static float Follow(float dt, float seconds) => 1f - MathF.Exp(-dt / MathF.Max(seconds, 1e-3f));

    private static float Approach(float v, float target, float step) => v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);

    private static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Vector3 Clamp(Vector3 v, Vector3 max) =>
        new(Math.Clamp(v.X, -max.X, max.X), Math.Clamp(v.Y, -max.Y, max.Y), Math.Clamp(v.Z, -max.Z, max.Z));
}
