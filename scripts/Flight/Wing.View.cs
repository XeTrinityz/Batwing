using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using Pov = BmSDK.GameObject.FTPOV;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    private static readonly Vector3 CockpitEye = new(-25f, 0f, 20f);
    private const float CockpitTilt = -14f;

    private const float RecentreAt = 55f, RecentredWithin = 4f;
    private const double RecentreMost = 0.4;
    private double _recentreUntil = -1.0;

    private const float BlendInSeconds = 1.1f;
    private const float BlendOutSeconds = 0.8f;
    private const double HintSeconds = 9.0;
    private const float AimPitchLimit = 80f;
    private const float LookLimit = 75f;
    private const int GamePitchLimit = 15000;

    private float _aimYaw, _aimPitch, _lookYaw, _lookPitch;
    private bool _haveLast;
    private float _lastYaw, _lastPitch;

    private float _blendIn, _blendOut;
    private Pov _blendFrom, _lastPov;
    private Vector3 _camOffset, _camLook = Vector3.UnitX;
    private bool _camReady;
    private float _camRoll;
    private float _fov;
    private float _shake;
    private double _hintUntil;

    private nint _pinCam;
    private bool _pinned;
    private int _minPitch, _maxPitch;

    public bool View(R3rdPersonCamera cam, PlayerController pc, in Pov game, ref Pov pov, float dt)
    {
        if (_phase == Phase.Off && _blendOut <= 0f && _voice == null)
        {
            _haveLast = false;
            return false;
        }

        try
        {
            dt = World.Paused ? 0f : Math.Clamp(dt, 0f, 0.1f);
            VoiceStep(dt);
            Vector2 turn = Turned(game.Rotation);
            switch (_phase)
            {
                case Phase.Arriving:
                    ArriveStep(cam, pc, dt);
                    break;
                case Phase.Flying:
                    FlyStep(cam, pc, Steer(turn, dt), dt);
                    break;
                case Phase.Leaving:
                    LeaveStep(dt);
                    break;
                case Phase.Escort:
                    EscortStep(pc, dt);
                    break;
            }

            if (_phase == Phase.Flying)
            {
                Pov mine = Chase(game, dt);
                if (_blendIn > 0f)
                {
                    _blendIn = MathF.Max(0f, _blendIn - MathF.Max(dt, 1f / 60f));
                    mine = Lerp(game, mine, Ease.Smooth(1f - _blendIn / BlendInSeconds));
                }

                pov = mine;
                _lastPov = mine;
                return true;
            }

            return BlendBack(game, ref pov, dt);
        }
        catch (Exception ex)
        {
            Fail(ex);
            return false;
        }
    }

    private Vector2 Turned(Rotator now)
    {
        bool listening = Listening;
        if (!_haveLast)
        {
            _lastYaw = now.Yaw;
            _lastPitch = now.Pitch;
            _haveLast = true;
            return Vector2.Zero;
        }

        float yaw = Geo.WrapDegrees(now.Yaw - _lastYaw) * Opt.AimSpeed.Number;
        float pitch = Geo.WrapDegrees(now.Pitch - _lastPitch) * Opt.AimSpeed.Number;
        _lastYaw = now.Yaw;
        _lastPitch = now.Pitch;
        if (Recentre(now))
            pitch = 0f;
        return listening ? new Vector2(yaw, pitch) : Vector2.Zero;
    }

    private bool Recentre(Rotator now)
    {
        float pitch = Geo.WrapDegrees(now.Pitch);
        if (_recentreUntil > 0.0)
        {
            if (MathF.Abs(pitch) <= RecentredWithin || Clock.Now >= _recentreUntil)
                _recentreUntil = -1.0;
            return true;
        }

        if (_phase != Phase.Flying || MathF.Abs(pitch) < RecentreAt || World.Pc is not { } pc)
            return false;

        Safe.Run("batwing look recentre", () => pc.SetRotation(new Rotator(0f, pc.Rotation.Yaw, pc.Rotation.Roll)));
        _recentreUntil = Clock.Now + RecentreMost;
        return false;
    }

    private Vector2 Steer(Vector2 turn, float dt)
    {
        // The pad's camera binds are parked while flying, so any real turn here is the mouse.
        if (turn.LengthSquared() > 0.0025f)
            Pad.KeyboardUsed();

        bool pad = Pad.InUse && Listening;
        Vector2 rs = pad ? PadStick() : Vector2.Zero;
        Vector2 rate = new Vector2(PadTurnRate, PadPitchRate) * (Opt.AimSpeed.Number * dt);

        bool look = !Shared.AnyMenuOpen && (Down(VkMButton) || Pad.Button("lb"));
        if (look)
        {
            Vector2 t = turn + rs * (PadLookRate * Opt.AimSpeed.Number * dt);
            _lookYaw = Geo.WrapDegrees(_lookYaw + t.X);
            _lookPitch = Math.Clamp(_lookPitch + t.Y, -LookLimit, LookLimit);
            return Vector2.Zero;
        }

        float back = Ease.Follow(5f, MathF.Max(dt, 1f / 60f));
        _lookYaw -= _lookYaw * back;
        _lookPitch -= _lookPitch * back;

        if (Opt.Direct)
            return _fm.HoverWanted ? turn + rs * rate : turn;

        if (pad && !_fm.HoverWanted)
        {
            PadAim(rs * rate);
            return Vector2.Zero;
        }

        Vector2 move = turn + rs * rate;
        _aimYaw = Geo.WrapDegrees(_aimYaw + move.X);
        _aimPitch = Math.Clamp(_aimPitch + move.Y, -AimPitchLimit, AimPitchLimit);
        return Vector2.Zero;
    }

    private const float PadTurnRate = 110f, PadPitchRate = 75f, PadLookRate = 150f;
    private const float PadLeadYaw = 40f, PadLeadPitch = 32f;

    private static bool Listening => !Shared.AnyMenuOpen && Native.GameInFront() && !World.Paused;

    private static Vector2 PadStick()
    {
        Vector2 rs = Pad.RightStick;
        float size = rs.Length();
        if (size <= 0f)
            return Vector2.Zero;

        // Soft centre for fine corrections, full rate at the edge.
        rs *= 0.35f + 0.65f * size * size;
        if (Opt.PadInvert.On)
            rs.Y = -rs.Y;
        return rs;
    }

    // The stick moves the aim and it stays where you leave it, so the view doesn't spring back. It's kept
    // on a leash ahead of the flight path, so holding the stick over turns steadily and the jet catches up
    // with the aim once you let go.
    private void PadAim(Vector2 move)
    {
        _aimYaw = Geo.WrapDegrees(_aimYaw + move.X);
        _aimPitch = Math.Clamp(_aimPitch + move.Y, -AimPitchLimit, AimPitchLimit);

        Vector3 path = _fm.Velocity.Length() > 500f ? Vector3.Normalize(_fm.Velocity) : _fm.Forward;
        if (path.X * path.X + path.Y * path.Y < 0.01f)
            return;

        float pathYaw = MathF.Atan2(path.Y, path.X) * Deg;
        float pathPitch = MathF.Asin(Math.Clamp(path.Z, -1f, 1f)) * Deg;
        float reach = Math.Clamp(Opt.AimSpeed.Number, 0.5f, 2f);
        float yawLead = PadLeadYaw * reach, pitchLead = PadLeadPitch * reach;

        _aimYaw = Geo.WrapDegrees(pathYaw + Math.Clamp(Geo.WrapDegrees(_aimYaw - pathYaw), -yawLead, yawLead));
        _aimPitch = Math.Clamp(Math.Clamp(_aimPitch, pathPitch - pitchLead, pathPitch + pitchLead), -AimPitchLimit, AimPitchLimit);
    }

    private Vector3 AimDir() => Geo.Dir(_aimYaw, _aimPitch);

    private void StartView()
    {
        Rotator r = Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Rotation : new Rotator(0f, MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) * Deg, 0f);
        _aimYaw = _fm.HoverWanted ? r.Yaw : MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) * Deg;
        _aimPitch = _fm.HoverWanted ? Math.Clamp(Geo.WrapDegrees(r.Pitch), -25f, 15f) : _fm.Pitch() * Deg;
        _lookYaw = _lookPitch = 0f;
        _camLook = _fm.Forward;
        _camReady = false;
        _haveLast = false;
        _blendIn = BlendInSeconds;
        _blendOut = 0f;
        _fov = 0f;
        _shake = 0f;
        _camRoll = 0f;
        _hintUntil = Opt.Hints.On ? Clock.Now + HintSeconds : 0.0;
    }

    private void EndView()
    {
        _blendFrom = _lastPov;
        _blendOut = _lastPov.FOV > 1f ? BlendOutSeconds : 0f;
        _blendIn = 0f;
    }

    private bool BlendBack(in Pov game, ref Pov pov, float dt)
    {
        if (_blendOut <= 0f)
            return false;

        _blendOut = MathF.Max(0f, _blendOut - MathF.Max(dt, 1f / 60f));
        pov = Lerp(_blendFrom, game, Ease.Smooth(1f - _blendOut / BlendOutSeconds));
        return true;
    }

    private Pov Chase(in Pov game, float dt)
    {
        Vector3 p = _fm.Position;
        float speed = _fm.Velocity.Length();
        float frac = Math.Clamp(speed / (_fm.TopSpeed * 1.4f), 0f, 1f);
        float wings = 1f - _fm.Hover;
        float leanMost = Opt.Direct ? 36f : 14f;
        float leanWant = leanMost * MathF.Sin(_fm.Bank()) * wings;
        _camRoll += (leanWant - _camRoll) * Ease.Follow(5f, MathF.Max(dt, 1f / 60f));

        float baseFov = game.FOV > 1f ? game.FOV : 70f;
        float wantFov = baseFov + 12f * frac + (_fm.Boosting ? 6f : 0f);
        _fov = _fov <= 0f ? baseFov : _fov + (wantFov - _fov) * Ease.Follow(2.5f, MathF.Max(dt, 1f / 60f));

        Vector3 eye;
        Rotator rot;
        int view = Opt.Camera.Index;
        if (view == 2)
        {
            eye = WorldPoint(p, _seat + CockpitEye);
            Geo.Axes(new Rotator(CockpitTilt + _lookPitch, _lookYaw, 0f), out Vector3 lx, out Vector3 ly, out Vector3 lz);
            Vector3 Frame(Vector3 v) => _fwd * v.X + _right * v.Y + _up * v.Z;
            rot = Geo.FromAxes(Frame(lx), Frame(ly), Frame(lz));
        }
        else
        {
            float r = MathF.Max(_half.X, _half.Y);
            bool far = view == 1;
            float dist = (far ? 3.4f : 1.9f) * r + 250f;
            float height = (far ? 0.7f : 0.34f) * r + 60f;
            dist *= 1f + 0.18f * frac + (_fm.Boosting ? 0.08f : 0f);

            Vector3 look;
            if (!Opt.Direct)
            {
                look = Geo.Dir(_aimYaw + _lookYaw, Math.Clamp(_aimPitch + _lookPitch, -85f, 85f));
            }
            else
            {
                Vector3 nose = _fm.Forward;
                Vector3 want = speed > 1500f && wings > 0.5f ? Vector3.Normalize(Vector3.Lerp(nose, _fm.Velocity / speed, 0.4f)) : Flat(nose);
                _camLook = Vector3.Normalize(Vector3.Lerp(_camLook, want, Ease.Follow(5f, MathF.Max(dt, 1f / 60f))));
                float yaw = MathF.Atan2(_camLook.Y, _camLook.X) * Deg;
                float pitch = MathF.Asin(Math.Clamp(_camLook.Z, -1f, 1f)) * Deg;
                look = Geo.Dir(yaw + _lookYaw, Math.Clamp(pitch + _lookPitch, -85f, 85f));
            }

            Vector3 target = -look * dist + Vector3.UnitZ * height;
            if (!_camReady)
            {
                _camOffset = target;
                _camReady = true;
            }

            _camOffset = Vector3.Lerp(_camOffset, target, Ease.Follow(8f, MathF.Max(dt, 1f / 60f)));
            eye = p + _camOffset;
            if (_craft is { IsValid: true } craft && Ground.Ray(craft, p + Vector3.UnitZ * _half.Z, eye, out Vector3 wall, out Vector3 n))
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
                rot.Roll + a * (0.9f * MathF.Sin(t * 23.7f + 2.1f)));
        }

        return new Pov { Location = eye, Rotation = rot, FOV = Math.Clamp(_fov + (view == 2 ? 6f : 0f), 20f, 130f) };
    }

    private static Vector3 Flat(Vector3 v)
    {
        var f = new Vector3(v.X, v.Y, 0f);
        return f.LengthSquared() > 1e-4f ? Vector3.Normalize(f) : Vector3.UnitX;
    }

    private void Shake(float dt) => _shake = MathF.Max(0f, _shake - dt * 1.6f);

    private static Pov Lerp(in Pov a, in Pov b, float t)
    {
        Rotator ra = a.Rotation, rb = b.Rotation;
        return new Pov
        {
            Location = Vector3.Lerp(a.Location, b.Location, t),
            Rotation = new Rotator(ra.Pitch + Geo.WrapDegrees(rb.Pitch - ra.Pitch) * t, ra.Yaw + Geo.WrapDegrees(rb.Yaw - ra.Yaw) * t, ra.Roll + Geo.WrapDegrees(rb.Roll - ra.Roll) * t),
            FOV = a.FOV + (b.FOV - a.FOV) * t,
        };
    }

    private void Pin(R3rdPersonCamera cam)
    {
        if (!_pinned || _pinCam != cam.Ptr)
        {
            _pinned = true;
            _pinCam = cam.Ptr;
            _minPitch = cam.MinPitch;
            _maxPitch = cam.maxPitch;
        }

        if (cam.MinPitch != -GamePitchLimit)
            cam.MinPitch = -GamePitchLimit;
        if (cam.maxPitch != GamePitchLimit)
            cam.maxPitch = GamePitchLimit;
    }

    private void Unpin()
    {
        if (!_pinned)
            return;

        _pinned = false;
        Safe.Run("batwing camera pitch", () =>
        {
            if (Hooks.PlayerCam() is not { } cam || cam.Ptr != _pinCam)
                return;
            if (cam.MinPitch == -GamePitchLimit)
                cam.MinPitch = _minPitch;
            if (cam.maxPitch == GamePitchLimit)
                cam.maxPitch = _maxPitch;
        });
    }
}
