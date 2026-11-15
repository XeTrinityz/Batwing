using System.Globalization;
using System.Numerics;
using BmSDK;

namespace Trinity.Batwing;

internal static class Geo
{
    public const float Deg = 180f / MathF.PI;
    public const float Metre = 100f;

    public static float WrapDegrees(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f)
            degrees -= 360f;
        else if (degrees < -180f)
            degrees += 360f;
        return degrees;
    }

    public static float Compass(float yaw) => ((yaw % 360f) + 360f) % 360f;

    public static Vector3 Facing(float yawDegrees)
    {
        float r = yawDegrees / Deg;
        return new Vector3(MathF.Cos(r), MathF.Sin(r), 0f);
    }

    public static Vector3 Dir(float yaw, float pitch)
    {
        float y = yaw / Deg, p = pitch / Deg;
        return new Vector3(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), MathF.Sin(p));
    }

    public static float YawOf(Vector3 v) => MathF.Atan2(v.Y, v.X) * Deg;

    public static float PitchOf(Vector3 v)
    {
        float l = v.Length();
        return l < 1e-4f ? 0f : MathF.Asin(Math.Clamp(v.Z / l, -1f, 1f)) * Deg;
    }

    public static Vector3 Flat(Vector3 v)
    {
        var f = new Vector3(v.X, v.Y, 0f);
        return f.LengthSquared() > 1e-4f ? Vector3.Normalize(f) : Vector3.UnitX;
    }

    public static float FlatDistance(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X, y = a.Y - b.Y;
        return MathF.Sqrt(x * x + y * y);
    }

    public static void Axes(Rotator rot, out Vector3 fwd, out Vector3 right, out Vector3 up)
    {
        float d = MathF.PI / 180f;
        float sp = MathF.Sin(rot.Pitch * d), cp = MathF.Cos(rot.Pitch * d);
        float sy = MathF.Sin(rot.Yaw * d), cy = MathF.Cos(rot.Yaw * d);
        float sr = MathF.Sin(rot.Roll * d), cr = MathF.Cos(rot.Roll * d);
        fwd = new Vector3(cp * cy, cp * sy, sp);
        right = new Vector3(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp);
        up = new Vector3(-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp);
    }

    public static Rotator FromAxes(Vector3 x, Vector3 y, Vector3 z)
    {
        float pitch = MathF.Atan2(x.Z, MathF.Sqrt(x.X * x.X + x.Y * x.Y));
        float yaw = MathF.Atan2(x.Y, x.X);
        var flatY = new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0f);
        float roll = MathF.Atan2(Vector3.Dot(z, flatY), Vector3.Dot(y, flatY));
        return new Rotator(pitch * Deg, yaw * Deg, roll * Deg);
    }

    // A point in a frame's own axes, out in the world.
    public static Vector3 Out(Vector3 at, Vector3 fwd, Vector3 right, Vector3 up, Vector3 local) =>
        at + fwd * local.X + right * local.Y + up * local.Z;

    // A world point in a frame's own axes.
    public static Vector3 In(Vector3 at, Vector3 fwd, Vector3 right, Vector3 up, Vector3 world)
    {
        Vector3 d = world - at;
        return new Vector3(Vector3.Dot(d, fwd), Vector3.Dot(d, right), Vector3.Dot(d, up));
    }

    public static Vector3 Spin(Vector3 v, Vector3 axis, float radians) =>
        axis.LengthSquared() < 1e-8f ? v : Vector3.Transform(v, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians));

    public static string Position(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0}, {1:0}, {2:0}", v.X, v.Y, v.Z);
}

internal static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Whole(float v) => v.ToString("0", Inv);

    public static string Tenths(float v) => v.ToString("0.0", Inv);

    public static string Times(float v) => v.ToString("0.00", Inv) + "x";

    public static string Percent(float v) => Whole(v * 100f) + "%";

    public static string Signed(float v) => (v >= 0f ? "+" : "") + Whole(v);

    public static string Metres(float units) => Whole(units / Geo.Metre) + " M";

    // Distance for the HUD, metres up close and kilometres further out.
    public static string Range(float units)
    {
        float m = units / Geo.Metre;
        return m < 1000f ? Whole(m) + " M" : (m / 1000f).ToString(m < 10000f ? "0.0" : "0", Inv) + " KM";
    }
}
