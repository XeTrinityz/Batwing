using Stopwatch = System.Diagnostics.Stopwatch;

namespace Trinity.Batwing;

internal static class Clock
{
    private static readonly long Origin = Stopwatch.GetTimestamp();
    private static float _world = -1f;

    public static double Now => Stopwatch.GetElapsedTime(Origin).TotalSeconds;

    // Seconds the game world has run for. It stands still while the game is paused and runs slow in
    // slow motion, so a deadline on it can't run out behind the pause menu.
    public static double Game { get; private set; }

    // Fed the world's own clock. A new level starts that clock again, so only steps forward count.
    public static void Follow(float worldSeconds)
    {
        if (_world >= 0f && worldSeconds > _world)
            Game += worldSeconds - _world;
        _world = worldSeconds;
    }

    public static void Unfollow() => _world = -1f;
}

internal struct Every
{
    private double _due;

    public bool Elapsed(double seconds)
    {
        double now = Clock.Now;
        if (now < _due)
            return false;

        _due = now + seconds;
        return true;
    }

    public void Force() => _due = 0.0;
}

internal struct FrameTimer
{
    private long _last;

    public float Step(float max)
    {
        long now = Stopwatch.GetTimestamp();
        float dt = _last == 0 ? 0f : Math.Min((float)Stopwatch.GetElapsedTime(_last, now).TotalSeconds, max);
        _last = now;
        return dt;
    }
}

internal static class Ease
{
    // The share of the way to a target to move this step, for a smooth follow at the given rate.
    public static float Follow(float rate, float dt) => 1f - MathF.Exp(-rate * dt);

    public static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float In(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t;
    }

    public static float Out(float t) => 1f - MathF.Pow(1f - Math.Clamp(t, 0f, 1f), 3f);

    public static float Approach(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);
}
