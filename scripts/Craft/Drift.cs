using System.Numerics;

namespace Trinity.Batwing;

// Something holding itself up on jets is never dead still. This only moves the model and the cockpit eye.
// The flight model stays where it is, so steering, aim, collisions and the chase camera feel none of it.
internal sealed class Drift
{
    private const float Bob = 35f;
    private const float RollMost = 1.5f / Geo.Deg, PitchMost = 0.9f / Geo.Deg, YawMost = 0.5f / Geo.Deg;
    private const float Buffet = 0.25f / Geo.Deg;
    private const float Calm = 300f, Gone = 1600f;
    private const float LowFrom = 2500f;
    private const float Spring = 1f, Damp = 0.8f, Gain = 1.2f;
    private const float FadeRate = 1.5f;

    private float _t, _weight;
    private Vector2 _flat, _vel;

    // Roll, pitch and yaw in radians, about the craft's own axes.
    private Vector3 _turn;

    // Set while the craft sits parked on the ground, which stops the drift.
    public bool Parked;

    public Vector3 Offset { get; private set; }

    public void Reset()
    {
        _t = Random.Shared.NextSingle() * 100f;
        _weight = 0f;
        _flat = _vel = Vector2.Zero;
        Offset = _turn = Vector3.Zero;
        Parked = false;
    }

    public void Step(Craft craft, float hover, Vector3 velocity, float ground, float dt)
    {
        if (!(dt > 0f))
            return;

        _t += dt;

        // Fades out as soon as it's really moving. The lean from the flight model says enough then.
        float speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y) + MathF.Abs(velocity.Z) * 0.5f;
        float calm = 1f - Ease.Smooth((speed - Calm) / (Gone - Calm));
        float want = Opt.HoverDrift.On && !Parked ? Ease.Smooth((hover - 0.5f) * 2f) * calm : 0f;
        _weight += (want - _weight) * Ease.Follow(FadeRate, dt);

        // Jet wash off the ground stirs it up a bit when it's low.
        float rough = 1f - Ease.Smooth(ground / LowFrom);
        float size = _weight * (1f + 0.5f * rough);
        float buffet = Buffet * rough * _weight;
        _turn = new Vector3(
            RollMost * size * Wander(_t, 0.53f, 1.31f, 2.27f) + buffet * MathF.Sin(_t * 13.7f),
            PitchMost * size * Wander(_t + 31f, 0.41f, 1.13f, 1.97f) + buffet * 0.6f * MathF.Sin(_t * 11.3f + 2f),
            YawMost * size * Wander(_t + 67f, 0.29f, 0.83f, 1.61f));

        // The tilt tips the jets, so it slides that way and its hold brings it back. That gives the drift the
        // lag a real hover has instead of moving in step with the sway.
        Vector3 fwd = craft.Fwd, right = craft.Right, up = craft.Up;
        Turn(ref fwd, ref right, ref up);
        var push = new Vector2(up.X - craft.Up.X, up.Y - craft.Up.Y) * (FlightModel.Gravity * Gain);
        _vel += (push - _flat * (Spring * Spring) - _vel * (2f * Damp * Spring)) * dt;
        _flat += _vel * dt;

        Offset = new Vector3(_flat.X, _flat.Y, Bob * size * Wander(_t + 13f, 1.1f, 2.9f, 0.37f));
    }

    // Layered slow waves at odd ratios so it never visibly repeats. Stays within -1 to 1.
    private static float Wander(float t, float a, float b, float c) =>
        (MathF.Sin(t * a) + 0.6f * MathF.Sin(t * b + 1.7f) + 0.35f * MathF.Sin(t * c + 4.1f)) / 1.95f;

    public void Turn(ref Vector3 fwd, ref Vector3 right, ref Vector3 up)
    {
        Vector3 spin = fwd * _turn.X + right * _turn.Y + up * _turn.Z;
        float angle = spin.Length();
        if (angle < 1e-6f)
            return;

        Quaternion q = Quaternion.CreateFromAxisAngle(spin / angle, angle);
        fwd = Vector3.Transform(fwd, q);
        right = Vector3.Transform(right, q);
        up = Vector3.Transform(up, q);
    }
}
