using System.Numerics;
using BmSDK;
using BmSDK.BmGame;

namespace Trinity.Batwing;

// The weapon GTA style's fire button uses.
internal enum Armament
{
    Guns,
    Cannon,
    Missiles,
}

internal sealed partial class Pilot
{
    private const float AimPitchLimit = 80f;
    private const float LookLimit = 75f;
    private const int GamePitchLimit = 15000;
    private const float RecentreAt = 55f, RecentredWithin = 4f;
    private const double RecentreMost = 0.4;

    // Degrees a second at full stick.
    private const float PadTurnRate = 110f, PadPitchRate = 75f, PadLookRate = 150f;

    // How far the pad's aim may lead the flight path, and how soft the end of that leash is.
    private const float PadLeadYaw = 40f, PadLeadPitch = 32f, PadLeashSoft = 0.4f;

    // GTA style. How fast the bumpers turn in hover, and how long the camera waits before it swings back behind.
    private const float GtaHoverTurn = 90f, GtaLookWait = 1.2f;

    // GTA style roll. How much sideways a full pull back ignores, and the share of full roll rate the stick gets.
    private const float GtaRollDead = 0.3f, GtaRollRate = 0.7f;

    // Where mouse aim points, and the free look on top of it.
    private float _aimYaw, _aimPitch, _lookYaw, _lookPitch;
    private bool _haveLast;
    private float _lastYaw, _lastPitch;
    private double _recentreUntil = -1.0;
    private Vector2 _turn, _virtualStick;
    private float _lookIdle;
    private bool _wasDirect;
    private Armament _armament;

    public Armament Armament => _armament;

    private nint _pinCam;
    private bool _pinned;
    private int _minPitch, _maxPitch;

    public Vector3 AimDir => Geo.Dir(_aimYaw, _aimPitch);

    public float AimYaw => _aimYaw;

    // The mouse turns the game's own camera, and that turn is read back from it here, so the game's own
    // mouse sensitivity and invert setting still apply.
    public void Turn(Rotator now, float dt)
    {
        if (!_haveLast)
        {
            _lastYaw = now.Yaw;
            _lastPitch = now.Pitch;
            _haveLast = true;
            _turn = Vector2.Zero;
            return;
        }

        float speed = Opt.MouseAim.Number;
        float yaw = Geo.WrapDegrees(now.Yaw - _lastYaw) * speed;
        float pitch = Geo.WrapDegrees(now.Pitch - _lastPitch) * speed;
        _lastYaw = now.Yaw;
        _lastPitch = now.Pitch;
        if (Recentre(now))
            pitch = 0f;

        _turn = Controls.Listening ? new Vector2(yaw, pitch) : Vector2.Zero;
        if (_turn.LengthSquared() <= 0.0025f)
            return;

        // The game shouldn't see the pad while flying. If the aim stick gets through anyway, that turn is the
        // stick's and is already counted, so it isn't taken as the mouse.
        if (Controls.AimStick != Vector2.Zero)
            _turn = Vector2.Zero;
        else
            Controls.KeyboardUsed();
    }

    // The game camera's pitch is held well inside its limits, so the mouse never runs into them and stops.
    private bool Recentre(Rotator now)
    {
        float pitch = Geo.WrapDegrees(now.Pitch);
        if (_recentreUntil > 0.0)
        {
            if (MathF.Abs(pitch) <= RecentredWithin || Clock.Now >= _recentreUntil)
                _recentreUntil = -1.0;
            return true;
        }

        if (MathF.Abs(pitch) < RecentreAt || World.Pc is not { } pc)
            return false;

        Safe.Run("look recentre", () => pc.SetRotation(new Rotator(0f, pc.Rotation.Yaw, pc.Rotation.Roll)));
        _recentreUntil = Clock.Now + RecentreMost;
        return false;
    }

    private void StartAim()
    {
        Rotator r = CameraHook.GameView() is { } pov ? pov.Rotation : new Rotator(0f, Geo.YawOf(_fm.Forward), 0f);
        _aimYaw = _fm.HoverWanted ? r.Yaw : Geo.YawOf(_fm.Forward);
        _aimPitch = _fm.HoverWanted ? Math.Clamp(Geo.WrapDegrees(r.Pitch), -25f, 15f) : _fm.Pitch() * Geo.Deg;
        _lookYaw = _lookPitch = 0f;
        _lookIdle = 0f;
        _wasDirect = Opt.Direct;
        _haveLast = false;
        _virtualStick = Vector2.Zero;
    }

    // Moves the aim or the look with the mouse and the aim stick. Returns how far direct control turns.
    private Vector2 Steer(float dt)
    {
        Vector2 turn = _turn;
        _turn = Vector2.Zero;
        Vector2 stick = Controls.AimStick;
        Vector2 rate = new Vector2(PadTurnRate, PadPitchRate) * (Opt.PadAim.Number * dt);
        float lookRate = PadLookRate * Opt.PadAim.Number * dt;

        // In GTA style the aim stick is only the camera. It stays where it's left for a moment, then swings
        // back behind. The mouse still flies, unless it's looking round.
        if (Opt.Gta)
        {
            bool looking = Controls.Down(Act.Look) || _landed;
            Vector2 t = stick * lookRate + (looking ? turn : Vector2.Zero);
            if (t != Vector2.Zero)
            {
                LookBy(t);
                _lookIdle = 0f;
            }
            else
            {
                _lookIdle += dt;
                if (_lookIdle > GtaLookWait)
                    LookBack(dt, 3f);
            }

            return looking ? Vector2.Zero : turn;
        }

        // Parked it shouldn't spin on the spot, so looking round is free look until it lifts off.
        if (Controls.Down(Act.Look) || _landed)
        {
            LookBy(turn + stick * lookRate);
            return Vector2.Zero;
        }

        LookBack(dt, 5f);

        if (Opt.Direct)
            return _fm.HoverWanted ? turn + stick * rate : turn;

        if (stick != Vector2.Zero && !_fm.HoverWanted)
        {
            PadAim(stick * rate);
            turn = Vector2.Zero;
        }
        else
        {
            turn += stick * rate;
        }

        _aimYaw = Geo.WrapDegrees(_aimYaw + turn.X);
        _aimPitch = Math.Clamp(_aimPitch + turn.Y, -AimPitchLimit, AimPitchLimit);
        return Vector2.Zero;
    }

    private void LookBy(Vector2 t)
    {
        _lookYaw = Geo.WrapDegrees(_lookYaw + t.X);
        _lookPitch = Math.Clamp(_lookPitch + t.Y, -LookLimit, LookLimit);
    }

    private void LookBack(float dt, float rate)
    {
        float back = Ease.Follow(rate, MathF.Max(dt, 1f / 60f));
        _lookYaw -= _lookYaw * back;
        _lookPitch -= _lookPitch * back;
    }

    // The stick moves the aim and it stays where it's left, so the view doesn't spring back. It's kept on a
    // leash ahead of the flight path, so holding the stick over turns steadily and the jet catches up once
    // it's let go.
    private void PadAim(Vector2 move)
    {
        Vector3 path = _fm.Velocity.Length() > 500f ? Vector3.Normalize(_fm.Velocity) : _fm.Forward;
        if (path.X * path.X + path.Y * path.Y < 0.01f)
        {
            _aimYaw = Geo.WrapDegrees(_aimYaw + move.X);
            _aimPitch = Math.Clamp(_aimPitch + move.Y, -AimPitchLimit, AimPitchLimit);
            return;
        }

        float reach = Math.Clamp(Opt.PadAim.Number, 0.5f, 2f);
        float yawMove = Leash(move.X, Geo.WrapDegrees(_aimYaw - Geo.YawOf(path)), PadLeadYaw * reach);
        float pitchMove = Leash(move.Y, _aimPitch - Geo.PitchOf(path), PadLeadPitch * reach);
        _aimYaw = Geo.WrapDegrees(_aimYaw + yawMove);
        _aimPitch = Math.Clamp(_aimPitch + pitchMove, -AimPitchLimit, AimPitchLimit);
    }

    // Pushing the aim further ahead gets heavier over the end of the leash instead of stopping dead. Coming
    // back is never slowed, and an aim already past the end stays put rather than snapping in.
    private static float Leash(float move, float ahead, float lead)
    {
        if (move * ahead <= 0f)
            return move;

        return move * Math.Clamp((lead - MathF.Abs(ahead)) / (lead * PadLeashSoft), 0f, 1f);
    }

    private FlightInput Gather(bool listening, float dt)
    {
        bool direct = Opt.Direct;
        if (direct != _wasDirect)
            Handover(direct);

        Vector2 turn = listening ? Steer(dt) : Vector2.Zero;
        var input = new FlightInput { Assist = Opt.AssistLevel, Banking = Opt.BankLevel, Heading = _aimYaw / Geo.Deg, Hold = Opt.HoverHold.On };
        if (!listening)
        {
            input.Aim = direct ? Vector3.Zero : AimDir;
            return input;
        }

        Vector2 fly = Controls.FlyStick;
        bool hover = _fm.HoverWanted;
        bool gta = Opt.Gta;
        if (gta && !hover)
            return GtaWings(input, turn, dt);

        if (!hover)
        {
            // The fly stick is throttle and roll at once. It snaps to one axis so pushing for throttle doesn't roll.
            if (MathF.Abs(fly.X) < MathF.Abs(fly.Y) * 0.5f)
                fly.X = 0f;
            else if (MathF.Abs(fly.Y) < MathF.Abs(fly.X) * 0.5f)
                fly.Y = 0f;
            fly.X *= 0.35f + 0.65f * fly.X * fly.X;
        }

        float x = Math.Clamp(Controls.Axis(Act.Right, Act.Left) + fly.X, -1f, 1f);
        float y = Math.Clamp(Controls.Axis(Act.Forward, Act.Back) + fly.Y, -1f, 1f);
        float rt = Controls.RightTrigger, lt = Controls.LeftTrigger;

        if (hover)
        {
            input.Move = new Vector2(x, y);
            input.Lift = Math.Clamp(Controls.Axis(Act.Up, Act.Down) + rt - lt, -1f, 1f);
            input.Boost = Controls.Down(Act.Boost);
            if (direct)
                _aimYaw += turn.X;
            if (gta)
                _aimYaw += Controls.Bumpers * GtaHoverTurn * dt;
            _aimYaw = Geo.WrapDegrees(_aimYaw);
            input.Heading = _aimYaw / Geo.Deg;
            return input;
        }

        input.Throttle = y;
        input.Boost = Controls.Down(Act.Boost) || rt > 0.4f;
        input.Brake = MathF.Max(Controls.Down(Act.Down) ? 1f : 0f, lt);
        if (direct)
        {
            Vector2 stick = Vector2.Clamp(MouseStick(turn, dt) + Controls.AimStick, -Vector2.One, Vector2.One);
            input.Direct = true;
            input.Stick = new Vector3(stick.X, stick.Y, x);
        }
        else
        {
            input.Aim = AimDir;
            input.Roll = x;
        }

        return input;
    }

    // GTA style swaps with the keyboard's control mode as the player picks up the pad or the mouse. Mouse aim
    // starts on the flight path so it doesn't swerve to an old aim, and the direct camera starts where mouse aim
    // was looking so the view doesn't jump.
    private void Handover(bool direct)
    {
        _wasDirect = direct;
        _virtualStick = Vector2.Zero;
        if (direct)
        {
            _camLook = Geo.Dir(_aimYaw, _aimPitch);
            return;
        }

        Vector3 path = _fm.Velocity.Length() > 1500f && !_fm.HoverWanted ? Vector3.Normalize(_fm.Velocity) : _fm.Forward;
        _aimYaw = _fm.HoverWanted ? Geo.YawOf(_fm.Forward) : Geo.YawOf(path);
        _aimPitch = _fm.HoverWanted ? Math.Clamp(Geo.PitchOf(_camLook), -25f, 15f) : Math.Clamp(Geo.PitchOf(path), -AimPitchLimit, AimPitchLimit);
    }

    // GTA style on the wings. The fly stick rolls and pitches, the triggers move the throttle, the bumpers yaw
    // and the afterburner is its own button. The keys work as they do in direct control.
    private FlightInput GtaWings(FlightInput input, Vector2 turn, float dt)
    {
        float rt = Controls.RightTrigger, lt = Controls.LeftTrigger;

        // A thumb pulling back drifts sideways, so the harder the pull the more roll it takes to count. Full
        // roll is also slower than direct control's, since the stick does pitch as well.
        Vector2 pad = Controls.GtaStick;
        float dead = GtaRollDead * MathF.Abs(pad.Y);
        pad.X = MathF.Sign(pad.X) * MathF.Max(0f, MathF.Abs(pad.X) - dead) / (1f - dead) * GtaRollRate;
        Vector2 stick = Vector2.Clamp(MouseStick(turn, dt) + pad, -Vector2.One, Vector2.One);
        float yaw = Math.Clamp(Controls.Axis(Act.Right, Act.Left) + Controls.Bumpers, -1f, 1f);
        input.Throttle = Math.Clamp(Controls.Axis(Act.Forward, Act.Back) + rt - lt, -1f, 1f);
        input.Boost = Controls.Down(Act.Boost);
        input.Brake = MathF.Max(Controls.Down(Act.Down) ? 1f : 0f, lt);
        input.Direct = true;
        input.HoldBank = true;
        input.Stick = new Vector3(stick.X, stick.Y, yaw);
        return input;
    }

    // The mouse nudges a stick that springs back to the middle. The pad's stick is the stick.
    private Vector2 MouseStick(Vector2 turn, float dt)
    {
        _virtualStick += turn * 0.09f;
        _virtualStick *= MathF.Exp(-6f * dt);
        _virtualStick = Vector2.Clamp(_virtualStick, -Vector2.One, Vector2.One);
        return _virtualStick;
    }

    private Orders PlayerOrders(bool listening)
    {
        // The gun press is read every frame so a click held from before the lock doesn't count as a tap.
        bool tap = _press.Take(Act.Gun);
        bool cannon = _press.Take(Act.Cannon);
        bool lockHeld = listening && Controls.Down(Act.Lock);
        bool gun = listening && Controls.Down(Act.Gun);

        // GTA style's one fire button uses whichever weapon is picked. The switch tapped while locking missiles
        // puts another one on, like tapping the guns does.
        bool fired = _press.Take(Act.Fire);
        bool switched = _press.Take(Act.Weapon);
        if (listening && Opt.Gta)
        {
            bool fire = Controls.Down(Act.Fire);
            if (switched && fire && _armament == Armament.Missiles)
                tap = true;
            else if (switched)
                NextWeapon();

            gun |= fire && _armament == Armament.Guns;
            cannon |= fired && _armament == Armament.Cannon;
            lockHeld |= fire && _armament == Armament.Missiles;
        }

        return new Orders
        {
            Gun = gun && !lockHeld,
            Stack = listening && tap && lockHeld,
            Cannon = listening && cannon,
            Lock = lockHeld,
            Look = Opt.Direct ? _fm.Forward : AimDir,
            Free = true,
            Homing = Opt.CannonHoming.On,
        };
    }

    private void NextWeapon()
    {
        if (!Opt.Weapons.On)
            return;

        _armament = (Armament)(((int)_armament + 1) % 3);
        Wing.Toast(WeaponName(_armament));
    }

    public static string WeaponName(Armament a) => a switch
    {
        Armament.Cannon => "CANNON",
        Armament.Missiles => "MISSILES",
        _ => "GUNS",
    };

    // The game camera's pitch limits are opened right up so the mouse can aim anywhere.
    private void Pin()
    {
        if (CameraHook.PlayerCam() is not { } cam)
            return;

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
        Safe.Run("camera pitch", () =>
        {
            if (CameraHook.PlayerCam() is not { } cam || cam.Ptr != _pinCam)
                return;
            if (cam.MinPitch == -GamePitchLimit)
                cam.MinPitch = _minPitch;
            if (cam.maxPitch == GamePitchLimit)
                cam.maxPitch = _maxPitch;
        });
    }
}
