using System.Globalization;
using System.Numerics;
using BmSDK;

namespace Trinity.Batwing;

internal static class Geo
{
    public const float Deg = 180f / MathF.PI;

    public static float WrapDegrees(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f)
            degrees -= 360f;
        else if (degrees < -180f)
            degrees += 360f;
        return degrees;
    }

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

    public static string Position(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0}, {1:0}, {2:0}", v.X, v.Y, v.Z);
}

internal static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Whole(float v) => v.ToString("0", Inv);

    public static string Tenths(float v) => v.ToString("0.0", Inv);

    public static string Times(float v) => v.ToString("0.00", Inv) + "x";

    public static string Percent(float v) => Whole(v * 100f) + "%";

    public static string Secs(float v) => Whole(v) + " S";

    public static string Signed(float v) => (v >= 0f ? "+" : "") + Whole(v);
}
