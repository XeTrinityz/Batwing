using System.Numerics;

namespace Trinity.Batwing;

// Something holding itself up on jets is never dead still. This only moves the model and the cockpit eye.
// The flight model stays where it is, so steering, aim, collisions and the chase camera don't feel any of it.
internal sealed partial class Wing
{
    private const float DriftBob = 35f;
    private const float DriftRoll = 1.5f / Deg, DriftPitch = 0.9f / Deg, DriftYaw = 0.5f / Deg;
    private const float DriftBuffet = 0.25f / Deg;
    private const float DriftCalm = 300f, DriftGone = 1600f;
    private const float DriftLow = 2500f;
    private const float DriftSpring = 1f, DriftDamp = 0.8f, DriftGain = 1.2f;
    private const float DriftFade = 1.5f;

    private float _driftT, _driftWeight;
    private Vector2 _driftFlat, _driftVel;
    private Vector3 _driftAt;

    // Roll, pitch and yaw in radians, about the craft's own axes.
    private Vector3 _driftTurn;

    private void ResetDrift()
    {
        _driftT = Random.Shared.NextSingle() * 100f;
        _driftWeight = 0f;
        _driftFlat = _driftVel = Vector2.Zero;
        _driftAt = _driftTurn = Vector3.Zero;
    }

    private void DriftStep(float hover, Vector3 velocity, float ground, float dt)
    {
        if (!(dt > 0f))
            return;

        _driftT += dt;

        // Fades out as soon as it's really moving. The lean from the flight model already says enough then.
        float speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y) + MathF.Abs(velocity.Z) * 0.5f;
        float calm = 1f - Ease.Smooth((speed - DriftCalm) / (DriftGone - DriftCalm));
        bool parked = _phase == Phase.Flying && _landed;
        float want = Opt.HoverDrift.On && !parked ? Ease.Smooth((hover - 0.5f) * 2f) * calm : 0f;
        _driftWeight += (want - _driftWeight) * Ease.Follow(DriftFade, dt);

        // Jet wash off the ground stirs it up a bit when it's low.
        float rough = 1f - Ease.Smooth(ground / DriftLow);
        float size = _driftWeight * (1f + 0.5f * rough);
        float t = _driftT;
        float buffet = DriftBuffet * rough * _driftWeight;
        _driftTurn = new Vector3(
            DriftRoll * size * Wander(t, 0.53f, 1.31f, 2.27f) + buffet * MathF.Sin(t * 13.7f),
            DriftPitch * size * Wander(t + 31f, 0.41f, 1.13f, 1.97f) + buffet * 0.6f * MathF.Sin(t * 11.3f + 2f),
            DriftYaw * size * Wander(t + 67f, 0.29f, 0.83f, 1.61f));

        // The tilt tips the jets, so it slides that way and its hold brings it back. That gives the drift
        // the same lag a real hover has instead of moving in step with the sway.
        Vector3 fwd = _fwd, right = _right, up = _up;
        Drifted(ref fwd, ref right, ref up);
        var push = new Vector2(up.X - _up.X, up.Y - _up.Y) * (_fm.Gravity * DriftGain);
        _driftVel += (push - _driftFlat * (DriftSpring * DriftSpring) - _driftVel * (2f * DriftDamp * DriftSpring)) * dt;
        _driftFlat += _driftVel * dt;

        float bob = DriftBob * size * Wander(t + 13f, 1.1f, 2.9f, 0.37f);
        _driftAt = new Vector3(_driftFlat.X, _driftFlat.Y, bob);
    }

    // Layered slow waves at odd ratios so it never visibly repeats. Stays within -1 to 1.
    private static float Wander(float t, float a, float b, float c) =>
        (MathF.Sin(t * a) + 0.6f * MathF.Sin(t * b + 1.7f) + 0.35f * MathF.Sin(t * c + 4.1f)) / 1.95f;

    private void Drifted(ref Vector3 fwd, ref Vector3 right, ref Vector3 up)
    {
        Vector3 spin = fwd * _driftTurn.X + right * _driftTurn.Y + up * _driftTurn.Z;
        float angle = spin.Length();
        if (angle < 1e-6f)
            return;

        Quaternion q = Quaternion.CreateFromAxisAngle(spin / angle, angle);
        fwd = Vector3.Transform(fwd, q);
        right = Vector3.Transform(right, q);
        up = Vector3.Transform(up, q);
    }
}
