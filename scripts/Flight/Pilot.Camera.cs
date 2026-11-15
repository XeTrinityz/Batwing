using System.Numerics;
using BmSDK;
using Pov = BmSDK.GameObject.FTPOV;

namespace Trinity.Batwing;

internal sealed partial class Pilot
{
    private static readonly Vector3 CockpitEye = new(-25f, 0f, 20f);
    private const float CockpitTilt = -14f;
    private const float BlendInSeconds = 1.1f;
    private const double HintSeconds = 9.0;

    private float _blendIn;
    private Pov _lastPov;
    private Vector3 _camOffset, _camLook = Vector3.UnitX;
    private bool _camReady;
    private float _camRoll, _fov, _shake;
    private double _hintUntil;

    public bool ShowKeys => Clock.Now < _hintUntil;

    private void StartView()
    {
        StartAim();
        _camLook = _fm.Forward;
        _camReady = false;
        _blendIn = BlendInSeconds;
        _fov = 0f;
        _shake = 0f;
        _camRoll = 0f;
        _hintUntil = Opt.Hints.On ? Clock.Now + HintSeconds : 0.0;
    }

    private void Decay(float dt) => _shake = MathF.Max(0f, _shake - dt * 1.6f);

    public Pov View(in Pov game, float dt)
    {
        Pov mine = Chase(game, dt);
        if (_blendIn > 0f)
        {
            _blendIn = MathF.Max(0f, _blendIn - MathF.Max(dt, 1f / 60f));
            mine = ChaseCam.Lerp(game, mine, Ease.Smooth(1f - _blendIn / BlendInSeconds));
        }

        _lastPov = mine;
        return mine;
    }

    private Pov Chase(in Pov game, float dt)
    {
        float step = MathF.Max(dt, 1f / 60f);
        Vector3 p = _fm.Position;
        float speed = _fm.Velocity.Length();
        float frac = Math.Clamp(speed / (_fm.TopSpeed * 1.4f), 0f, 1f);
        float wings = 1f - _fm.Hover;

        // The view leans a little with the bank, more in direct control where the bank is all yours. GTA style
        // keeps it nearly level, since its camera swings round freely and a lean looks wrong from the side.
        float leanWant = (Opt.Gta ? 8f : Opt.Direct ? 36f : 14f) * MathF.Sin(_fm.Bank()) * wings;
        _camRoll += (leanWant - _camRoll) * Ease.Follow(5f, step);

        float baseFov = game.FOV > 1f ? game.FOV : 70f;
        float wantFov = baseFov + 12f * frac + (_fm.Boosting ? 6f : 0f);
        _fov = _fov <= 0f ? baseFov : _fov + (wantFov - _fov) * Ease.Follow(2.5f, step);

        Vector3 eye;
        Rotator rot;
        int view = Opt.Camera.Index;
        if (view == 2)
        {
            // The eye sits in the model, so it rides the hover drift with it or the canopy would swim round it.
            Vector3 cf = _craft.Fwd, cr = _craft.Right, cu = _craft.Up;
            _craft.Drift.Turn(ref cf, ref cr, ref cu);
            Vector3 Frame(Vector3 v) => cf * v.X + cr * v.Y + cu * v.Z;
            eye = p + _craft.Drift.Offset + Frame(_craft.Seat + CockpitEye);
            Geo.Axes(new Rotator(CockpitTilt + _lookPitch, _lookYaw, 0f), out Vector3 lx, out Vector3 ly, out Vector3 lz);
            rot = Geo.FromAxes(Frame(lx), Frame(ly), Frame(lz));
        }
        else
        {
            float r = _craft.Span;
            bool far = view == 1;
            float dist = ((far ? 3.4f : 1.9f) * r + 250f) * (1f + 0.18f * frac + (_fm.Boosting ? 0.08f : 0f));
            float height = (far ? 0.7f : 0.34f) * r + 60f;

            Vector3 look;
            if (!Opt.Direct)
            {
                look = Geo.Dir(_aimYaw + _lookYaw, Math.Clamp(_aimPitch + _lookPitch, -85f, 85f));
            }
            else
            {
                Vector3 nose = _fm.Forward;
                Vector3 want = speed > 1500f && wings > 0.5f ? Vector3.Normalize(Vector3.Lerp(nose, _fm.Velocity / speed, 0.4f)) : Geo.Flat(nose);
                _camLook = Vector3.Normalize(Vector3.Lerp(_camLook, want, Ease.Follow(5f, step)));
                look = Geo.Dir(Geo.YawOf(_camLook) + _lookYaw, Math.Clamp(Geo.PitchOf(_camLook) + _lookPitch, -85f, 85f));
            }

            Vector3 target = -look * dist + Vector3.UnitZ * height;
            if (!_camReady)
            {
                _camOffset = target;
                _camReady = true;
            }

            _camOffset = Vector3.Lerp(_camOffset, target, Ease.Follow(8f, step));
            eye = p + _camOffset;

            // Kept out of walls, so the view never ends up inside a building.
            if (_craft.Alive && Ground.Ray(_craft.Actor, p + Vector3.UnitZ * _craft.Half.Z, eye, out Vector3 wall, out Vector3 n))
                eye = wall + Vector3.Normalize(p - eye) * 40f + n * 20f;

            rot = Rotator.FromDirection(p + look * 20000f - eye);
            rot = new Rotator(rot.Pitch, rot.Yaw, _camRoll);
        }

        float a = _shake + (_fm.Boosting ? 0.16f : 0f) + (_fm.Stalled ? 0.25f : 0f) + MathF.Max(0f, _fm.GLoad - 6f) * 0.05f;
        if (a > 0.001f)
        {
            float t = (float)Clock.Now;
            rot = new Rotator(
                rot.Pitch + a * (0.7f * MathF.Sin(t * 37.1f) + 0.4f * MathF.Sin(t * 61.7f + 1.3f)),
                rot.Yaw + a * (0.5f * MathF.Sin(t * 29.3f + 0.7f) + 0.3f * MathF.Sin(t * 53.9f)),
                rot.Roll + a * 0.9f * MathF.Sin(t * 23.7f + 2.1f));
        }

        return new Pov { Location = eye, Rotation = rot, FOV = Math.Clamp(_fov + (view == 2 ? 6f : 0f), 20f, 130f) };
    }
}

internal static class ChaseCam
{
    public static Pov Lerp(in Pov a, in Pov b, float t)
    {
        Rotator ra = a.Rotation, rb = b.Rotation;
        return new Pov
        {
            Location = Vector3.Lerp(a.Location, b.Location, t),
            Rotation = new Rotator(ra.Pitch + Geo.WrapDegrees(rb.Pitch - ra.Pitch) * t, ra.Yaw + Geo.WrapDegrees(rb.Yaw - ra.Yaw) * t, ra.Roll + Geo.WrapDegrees(rb.Roll - ra.Roll) * t),
            FOV = a.FOV + (b.FOV - a.FOV) * t,
        };
    }
}
