using System.Numerics;
using BmSDK;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal static class WorldView
{
    private const float ProbeAhead = 1000f;
    private const float ProbeSide = 250f;
    private const float NearPlane = 10f;

    private static float _cx, _cy, _kx, _ky;
    private static bool _reported;
    private static int _frame = -1;

    public static bool Ready { get; private set; }

    public static Vector3 Eye { get; private set; }
    public static Vector3 Forward { get; private set; }
    public static Vector3 Right { get; private set; }
    public static Vector3 Up { get; private set; }
    public static Rotator Rotation { get; private set; }

    public static float Width => _cx * 2f;
    public static float Height => _cy * 2f;

    public static void Begin(Canvas canvas)
    {
        if (Ready && _frame == Gfx.Frame)
            return;
        _frame = Gfx.Frame;

        Ready = false;
        if (World.Pc is not { } pc || !ReadCamera(pc))
            return;

        _cx = canvas.ClipX * 0.5f;
        _cy = canvas.ClipY * 0.5f;
        if (_cx <= 0f || _cy <= 0f)
            return;

        float ratio = ProbeSide / ProbeAhead;
        Vector3 s = canvas.Project(Eye + Forward * ProbeAhead + Right * ProbeSide + Up * ProbeSide);
        float kx = (s.X - _cx) / ratio;
        float ky = (_cy - s.Y) / ratio;
        if (float.IsFinite(kx) && float.IsFinite(ky) && kx > 1f && ky > 1f && kx < _cx * 50f && ky < _cx * 50f)
        {
            _kx = kx;
            _ky = ky;
        }
        else
        {
            float fov = Math.Clamp(pc.GetFOVAngle(), 5f, 170f);
            _kx = _ky = _cx / MathF.Tan(fov * 0.5f * MathF.PI / 180f);
            if (!_reported)
            {
                _reported = true;
                Log.Info("WorldView: Canvas.Project gave " + s + ", projecting from the FOV (" + fov + ") instead.");
            }
        }

        Ready = true;
    }

    private static bool ReadCamera(PlayerController pc)
    {
        pc.GetPlayerViewPoint(out Vector3 eye, out Rotator rot);
        Eye = eye;
        Rotation = rot;
        Geo.Axes(rot, out Vector3 f, out Vector3 r, out Vector3 u);
        Forward = f;
        Right = r;
        Up = u;
        return float.IsFinite(f.X);
    }

    public static bool ToScreen(Vector3 world, out Vector2 screen)
    {
        Vector3 v = world - Eye;
        float z = Vector3.Dot(v, Forward);
        if (!Ready || z < NearPlane)
        {
            screen = default;
            return false;
        }

        screen = new Vector2(_cx + _kx * Vector3.Dot(v, Right) / z, _cy - _ky * Vector3.Dot(v, Up) / z);
        return true;
    }

    public static bool DirToScreen(Vector3 dir, out Vector2 screen) => ToScreen(Eye + dir * 100000f, out screen);
}
