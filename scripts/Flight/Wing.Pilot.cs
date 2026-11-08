using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    private const int VkShift = 0x10, VkControl = 0x11, VkSpace = 0x20, VkLButton = 0x01, VkRButton = 0x02, VkMButton = 0x04;

    private static readonly int[] FlyKeys = { 'W', 'A', 'S', 'D', 'F', 'E', 'C', 'H', 'Q', VkSpace, VkControl, VkShift };

    private const float CrashSpeed = 2600f;

    private const float LandSpeed = 700f;

    private const float GroundReach = 30000f;

    private RPawnPlayer? _held;
    private bool _wasTickOff, _wasAnimsPaused, _wasHidden, _wasCollide, _wasBlock;
    private AkEvent? _fallLoop;
    private bool _muted;

    private readonly bool[] _was = new bool[256];
    private float _hull = 1f;
    private bool _landed;
    private float _ground = GroundReach;
    private Vector3 _groundAt, _groundNormal = Vector3.UnitZ;
    private float _impactAt = 99f;
    private Every _lookAhead;
    private Every _districts;
    private string? _cell;
    private bool _outside;
    private float _outsideFor;
    private Every _scrapeSound;
    private Vector2 _virtualStick;

    private void Board(RPawnPlayer pawn, Vector3 at, float yawDegrees)
    {
        _fm.Place(at, yawDegrees / Deg, Vector3.Zero);
        _fm.Hover = 1f;
        _fm.HoverWanted = true;
        _fm.Throttle = 0.7f;
        TakeOff(pawn);
    }

    private void BoardNow(RPawnPlayer pawn, Kit kit, bool airborne)
    {
        Vector3 v = pawn.Velocity;
        var flat = new Vector3(v.X, v.Y, 0f);
        bool flying = airborne && flat.Length() > 1500f;
        float yaw = flying ? MathF.Atan2(flat.Y, flat.X) * Deg : CameraYaw(pawn);
        Vector3 at = airborne ? pawn.Location : HoverPoint(pawn);
        if (!airborne && Ground.Ray(pawn, pawn.Location + new Vector3(0f, 0f, 100f), at + new Vector3(0f, 0f, _half.Z), out _, out _))
            throw new InvalidOperationException("no room above you");

        SpawnCraft(kit, at, yaw);
        if (flying)
        {
            Vector3 dir = Vector3.Normalize(new Vector3(v.X, v.Y, MathF.Min(v.Z, 0f) * 0.3f));
            _fm.Place(at, yaw / Deg, dir * MathF.Max(v.Length(), _fm.StallSpeed * 1.5f));
            _fm.Hover = 0f;
            _fm.HoverWanted = false;
            _fm.Throttle = 0.85f;
        }
        else
        {
            _fm.Place(at, yaw / Deg, airborne ? v * 0.5f : Vector3.Zero);
            _fm.Hover = 1f;
            _fm.HoverWanted = true;
            _fm.Throttle = 0.7f;
        }

        Place(_fm.Position, _fm.Forward, _fm.Right, _fm.Up);
        TakeOff(pawn);
    }

    private static float CameraYaw(Pawn pawn) => Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Rotation.Yaw : pawn.Rotation.Yaw;

    private void TakeOff(RPawnPlayer pawn)
    {
        if (_remote)
            Tether(pawn);
        else
            Hold(pawn);
        StartEngine();
        FadeVoice();
        _phase = Phase.Flying;
        _hull = 1f;
        _landed = false;
        _cell = null;
        _outside = false;
        _outsideFor = 0f;
        _impactAt = 99f;
        _bailWait = -1f;
        _solidBelow = true;
        _loadingBelow = false;
        _noSolidFor = 0f;
        _shotDamage = 0f;
        _underFireUntil = 0.0;
        _fm.Fuel = 1f;
        _fm.TopSpeed = Opt.TopSpeed.Number * Metre;
        ResetWeapons();
        _virtualStick = Vector2.Zero;
        StartView();

        foreach (int vk in FlyKeys)
            _was[vk] = true;
        _padWas.Clear();

        Hatch(close: true);
        if (_remote)
        {
            Toast("REMOTE CONTROL");
            return;
        }

        Toast(_fm.HoverWanted ? "ABOARD // HOVER" : "ABOARD");
        Log.Info(Title + ": aboard at " + Geo.Position(_fm.Position) + (_fm.HoverWanted ? ", hovering." : ", flying at " + Fmt.Whole(_fm.Velocity.Length() / Metre) + " m/s."));
    }

    private void Hold(RPawnPlayer p)
    {
        _held = p;
        _wasTickOff = p.bTickIsDisabled;
        _wasAnimsPaused = p.Mesh is { IsValid: true } mesh && mesh.bPauseAnims;
        _wasHidden = p.bHidden;
        _wasCollide = p.bCollideActors;
        _wasBlock = p.bBlockActors;
        Shared.Set(HeldKey, true);

        if (p.FallLoopSound is { IsValid: true } loop)
        {
            _fallLoop = loop;
            Safe.Run("fall loop", () => p.RemoveLoopingAudioEvent(loop));
            p.FallLoopSound = null!;
        }

        p.SetPhysics(Actor.EPhysics.PHYS_None, false);
        p.Velocity = Vector3.Zero;
        p.SetCollision(false, false, p.bIgnoreEncroachers);
        p.SetHidden(true);
        Still(p);
        Carry(p);
        MuteKeys();
    }

    private static void Still(RPawnPlayer p)
    {
        if (!p.bTickIsDisabled)
            p.SetTickIsDisabled(true);
        if (p.Mesh is { IsValid: true } mesh && !mesh.bPauseAnims)
            mesh.bPauseAnims = true;
    }

    private const float MovedAway = 2500f;

    private Vector3 _carriedTo;

    private void Carry(RPawnPlayer p)
    {
        p.SetLocationIgnoringCollision(_fm.Position);
        p.Velocity = Vector3.Zero;
        _carriedTo = p.Location;
    }

    private RPawnPlayer? HeldPawn() =>
        _held is { IsValid: true } p && !p.bDeleteMe && World.Player is { } now && now.Ptr == p.Ptr ? p : null;

    private readonly record struct Drop(Vector3 At, Vector3 Velocity, bool OnGround);

    private Drop Bail()
    {
        Vector3 at = _fm.Position;
        if ((_landed || _fm.Hover > 0.5f) && _ground < 1800f && HeldPawn() is { } p
            && Ground.Ray(p, at - new Vector3(0f, 0f, _half.Z), at - new Vector3(0f, 0f, 3000f), out Vector3 floor, out _))
        {
            return new Drop(floor + new Vector3(0f, 0f, Ground.Lift(p)), Vector3.Zero, true);
        }

        return Underneath(at);
    }

    // Drop out of the belly. Going out the top put Batman on the hull as it climbed away and he fell through it.
    private Drop Underneath(Vector3 at)
    {
        Vector3 v = _fm.Velocity;
        float vz = MathF.Min(v.Z * 0.5f, v.Z) - 400f;
        var fall = new Vector3(v.X * 0.7f, v.Y * 0.7f, vz);
        if (HeldPawn() is not { } p)
            return new Drop(at - new Vector3(0f, 0f, HullDepth() + 300f), fall, false);

        float lift = Ground.Lift(p);
        Vector3 belly = at - new Vector3(0f, 0f, HullDepth());
        Vector3 below = belly - new Vector3(0f, 0f, lift + 150f);
        Actor tracer = _craft is { IsValid: true } craft ? craft : p;
        if (!Ground.Ray(tracer, belly, below - new Vector3(0f, 0f, lift), out Vector3 hit, out Vector3 n))
            return new Drop(below, fall, false);

        return n.Z > 0.6f
            ? new Drop(hit + new Vector3(0f, 0f, lift), Vector3.Zero, true)
            : new Drop(hit + n * lift, fall, false);
    }

    // Half the hull's height in world terms, so a banked craft drops him clear of the low wingtip.
    private float HullDepth() =>
        MathF.Abs(_fwd.Z) * _half.X + MathF.Abs(_right.Z) * _half.Y + MathF.Abs(_up.Z) * _half.Z;

    private void LetGo(RPawnPlayer? p, Drop? drop)
    {
        bool remote = _remote;
        if (remote)
            Untether();
        Shared.Set(HeldKey, false);
        WeaponsOff();
        Unmute();
        Unpin();
        Pad.HoldButtons = false;
        EndView();
        // Batman was never inside on remote control, so there's nothing to restore or move.
        if (remote)
            return;

        p ??= _held is { IsValid: true } still && !still.bDeleteMe ? still : null;
        _held = null;
        if (p is not { IsValid: true })
            return;

        EndGrapple();
        Safe.Run("batwing release", () =>
        {
            if (p.bHidden != _wasHidden)
                p.SetHidden(_wasHidden);
            p.SetCollision(_wasCollide, _wasBlock, p.bIgnoreEncroachers);
            if (p.bTickIsDisabled != _wasTickOff)
                p.SetTickIsDisabled(_wasTickOff);
            if (p.Mesh is { IsValid: true } mesh && mesh.bPauseAnims != _wasAnimsPaused)
                mesh.bPauseAnims = _wasAnimsPaused;
            if (_fallLoop is { IsValid: true } loop)
                p.FallLoopSound = loop;
            _fallLoop = null;

            float yaw = MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) * Deg;
            if (drop is { } d)
            {
                p.SetLocationIgnoringCollision(d.At);
                p.SetRotation(new Rotator(0f, yaw, 0f));
            }

            p.SetPhysics(Actor.EPhysics.PHYS_Falling, false);
            p.Velocity = drop?.Velocity ?? Vector3.Zero;
            if (drop is { OnGround: true })
                Ground.Settle(p);
        });
    }

    private void MuteKeys()
    {
        if (_muted)
            return;

        _muted = true;
        GameKeys.Hold("flight", GameKeys.FlightKeys, GameKeys.FlightNames);
    }

    private void Unmute()
    {
        if (!_muted)
            return;

        _muted = false;
        Safe.Run("batwing keys", () => GameKeys.Release("flight"));
    }

    private readonly HashSet<string> _padWas = new();

    private static bool Down(int vk) => Keyboard.IsDown((Keys)vk);

    private bool Pressed(int vk)
    {
        bool down = Down(vk);
        bool was = _was[vk];
        _was[vk] = down;
        return down && !was;
    }

    private bool PadPressed(string name)
    {
        bool down = Pad.Button(name);
        bool was = _padWas.Contains(name);
        if (down)
            _padWas.Add(name);
        else
            _padWas.Remove(name);
        return down && !was;
    }

    private static float Axis(int plus, int minus) => (Down(plus) ? 1f : 0f) - (Down(minus) ? 1f : 0f);

    private FlightInput Gather(bool listening, Vector2 turn)
    {
        var input = new FlightInput { Assist = Opt.Assist.On, Heading = _aimYaw / Deg };
        bool direct = Opt.Direct;
        if (!listening)
        {
            input.Aim = direct ? Vector3.Zero : AimDir();
            input.Direct = direct;
            return input;
        }

        Vector2 ls = Pad.LeftStick;
        bool hover = _fm.HoverWanted;
        if (!hover)
        {
            // LS is throttle and roll at once. Snap to one axis so pushing for throttle doesn't roll the jet.
            if (MathF.Abs(ls.X) < MathF.Abs(ls.Y) * 0.5f)
                ls.X = 0f;
            else if (MathF.Abs(ls.Y) < MathF.Abs(ls.X) * 0.5f)
                ls.Y = 0f;
            ls.X *= 0.35f + 0.65f * ls.X * ls.X;
        }

        float keysX = Axis('D', 'A'), keysY = Axis('W', 'S');
        float x = Math.Clamp(keysX + ls.X, -1f, 1f);
        float y = Math.Clamp(keysY + ls.Y, -1f, 1f);
        float rt = Pad.RightTrigger, lt = Pad.LeftTrigger;

        if (hover)
        {
            input.Move = new Vector2(x, y);
            input.Lift = Math.Clamp(Axis(VkSpace, VkControl) + rt - lt, -1f, 1f);
            input.Boost = Down(VkShift) || Pad.Button("ls");
            input.Heading = _aimYaw / Deg;
            if (direct)
            {
                _aimYaw += turn.X;
                input.Heading = _aimYaw / Deg;
            }
        }
        else
        {
            input.Throttle = y;
            input.Boost = Down(VkShift) || rt > 0.4f;
            input.Brake = MathF.Max(Down(VkControl) ? 1f : 0f, lt);
            if (direct)
            {
                _virtualStick += new Vector2(turn.X, turn.Y) * 0.09f;
                _virtualStick *= MathF.Exp(-6f * _frameDt);
                _virtualStick = Vector2.Clamp(_virtualStick, -Vector2.One, Vector2.One);
                Vector2 stick = Vector2.Clamp(_virtualStick + (Pad.InUse ? PadStick() : Vector2.Zero), -Vector2.One, Vector2.One);
                input.Direct = true;
                input.Stick = new Vector3(stick.X, stick.Y, x);
            }
            else
            {
                input.Aim = AimDir();
                input.Roll = x;
            }
        }

        return input;
    }

    private void Buttons()
    {
        if (Pressed('F') | PadPressed("y"))
            SwitchMode();

        if (Pressed('E') | PadPressed("b"))
        {
            if (_remote)
                EndRemote("REMOTE OFF");
            else if (_bailWait >= 0f)
                Exit("BAILED OUT");
            else
                Bailout();
            return;
        }

        if (Pressed('C') | PadPressed("up"))
        {
            Opt.Camera.Step(1);
            Settings.MarkDirty();
            Readout.Toast(Title, "CAMERA: " + Opt.Camera.Value, true);
        }

        if (Pressed('H'))
            _hintUntil = Clock.Now < _hintUntil ? 0.0 : double.MaxValue;
    }

    private const float BailWaitMost = 30f;

    private float _bailWait = -1f;

    private Every _bailCheck;
    private bool _solidBelow = true;
    private float _noSolidFor;
    private Every _solidCheck;
    private bool _loadingBelow;

    private void Bailout()
    {
        if (GroundLoaded())
        {
            Exit("BAILED OUT");
            return;
        }

        string? below = Districts.Under(_fm.Position);
        if (!StillLoading())
        {
            Log.Info(Title + ": no solid ground under " + Geo.Position(_fm.Position) + " (over " + (below ?? "no district")
                + (below != null ? ", loaded" : "") + "), staying aboard.");
            Readout.Notice(Title, "NOTHING SOLID BELOW // FLY OVER THE STREETS TO GET OUT", false);
            return;
        }

        _bailWait = 0f;
        _bailCheck.Force();
        if (!_fm.HoverWanted)
        {
            _fm.HoverWanted = true;
            HoverSounds();
            Flare(1.2f);
        }

        Log.Info(Title + ": bail out waiting for ground to load (over " + (_cell ?? "no district") + ").");
        WaitLog(_cell, true);
        Toast("WAITING FOR THE GROUND");
    }

    private void BailWaitStep(float dt)
    {
        if (_bailWait < 0f)
            return;

        _bailWait += dt;
        if (!_fm.HoverWanted)
        {
            _bailWait = -1f;
            return;
        }

        if (_bailWait >= BailWaitMost)
        {
            WaitLog(_cell, true);
            Log.Info(Title + ": ground still not loaded after " + Fmt.Secs(_bailWait) + ", staying aboard.");
            _bailWait = -1f;
            Readout.Notice(Title, "THE GROUND HERE ISN'T LOADING // STAYING ABOARD", false);
            return;
        }

        if (!_bailCheck.Elapsed(0.25))
            return;

        bool ready = GroundLoaded();
        WaitLog(_cell, false);

        if (!ready && _bailWait > 2f && !StillLoading())
        {
            Log.Info(Title + ": district loaded but no ground under " + Geo.Position(_fm.Position) + ", staying aboard.");
            _bailWait = -1f;
            Readout.Notice(Title, "NOTHING SOLID BELOW // FLY OVER THE STREETS TO GET OUT", false);
            return;
        }

        if (ready)
        {
            Log.Info(Title + ": ground loaded after " + Fmt.Tenths(_bailWait) + " s.");
            Exit("BAILED OUT");
        }
    }

    private bool StillLoading()
    {
        bool around = World.Ri is { } ri && Safe.Try("levels around player", () => ri.AreLevelsLoadedAroundPlayer(0f), fallback: true);
        if (!around)
            return true;

        return Districts.Under(_fm.Position) is { } cell && !Safe.Try("district shown", () => Shown(cell), fallback: true);
    }

    private bool GroundLoaded()
    {
        if (HeldPawn() is not { } p)
            return true;

        Lead();

        return Ground.Real(p, _fm.Position, 60000f, out _);
    }

    private const float HullPoints = 400f;

    private float _shotDamage;
    private Vector3 _shotFrom;
    private double _underFireUntil;

    public bool Absorbs(RPawnPlayer pawn, int damage, Class? type, Vector3 hitLocation)
    {
        if (_phase != Phase.Flying || damage <= 0 || _held is not { IsValid: true } held || held.Ptr != pawn.Ptr)
            return false;
        if (type is { IsValid: true } && (type.Name.ToString() ?? "").Contains("KillZone", StringComparison.OrdinalIgnoreCase))
            return false;

        _shotDamage += damage;
        _shotFrom = hitLocation;
        return true;
    }

    private void TakingFire()
    {
        if (_shotDamage <= 0f)
            return;

        float damage = _shotDamage;
        _shotDamage = 0f;
        float hard = Math.Clamp(damage / 60f, 0.15f, 1f);
        _shake = MathF.Max(_shake, 0.3f + hard * 0.6f);
        _underFireUntil = Clock.Now + 1.5;

        Vector3 away = _fm.Position - _shotFrom;
        if (_shotFrom.LengthSquared() < 1f || away.LengthSquared() < 1f)
            away = -_fm.Forward;
        away = Vector3.Normalize(away);
        Burst(_kit?.Impact, _fm.Position - away * (_half.X * 0.4f) + _up * (_half.Z * 0.5f), away);
        if (_scrapeSound.Elapsed(0.25))
            Sound(hard > 0.5f ? _kit?.Hit : _kit?.Scrape);

        if (!Opt.Crashes.On)
            return;

        _hull -= damage / HullPoints;
        Log.Detail(Title + ": took " + Fmt.Whole(damage) + " damage, hull " + Fmt.Percent(_hull) + ".");
        if (_hull <= 0f)
            Crash(away, "SHOT DOWN");
    }

    private void SwitchMode()
    {
        _fm.HoverWanted = !_fm.HoverWanted;
        if (!_fm.HoverWanted)
        {
            _fm.Throttle = MathF.Max(_fm.Throttle, 0.8f);
            _landed = false;
            if (_bailWait >= 0f)
            {
                _bailWait = -1f;
                Toast("WINGS // BAIL-OUT CANCELLED");
                WingSounds();
                return;
            }
        }
        else if (Opt.Direct)
        {
            _aimYaw = MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) * Deg;
        }

        if (_fm.HoverWanted)
        {
            HoverSounds();
            Flare(1.2f);
        }
        else
        {
            WingSounds();
        }

        Toast(_fm.HoverWanted ? "HOVER" : "WINGS");
    }

    private float _frameDt;

    private void FlyStep(R3rdPersonCamera cam, PlayerController pc, Vector2 turn, float dt)
    {
        bool remote = _remote;
        RPawnPlayer? pawn = remote ? RemotePawn() : HeldPawn();
        if (pawn == null)
        {
            Stop(silent: false);
            _ended = "ENDED: BATMAN CHANGED";
            _endedGood = false;
            return;
        }

        if (Hooks.InCinematic(pc))
        {
            LetGo(pawn, remote ? null : Bail());
            Depart(0.5f);
            _ended = "LEFT FOR A CUTSCENE";
            _endedGood = true;
            return;
        }

        if (pawn.Health <= 0)
        {
            if (remote)
                EndRemote("BATMAN IS DOWN");
            else
                Crash(-_fm.Velocity, "BATMAN IS DOWN");
            return;
        }

        if (remote)
        {
            if (!RemoteChecks(pawn, dt))
                return;
        }
        else if (Vector3.DistanceSquared(pawn.Location, _carriedTo) > MovedAway * MovedAway)
        {
            Log.Info(Title + ": game moved Batman out of the craft (" + Geo.Position(pawn.Location) + "), releasing him.");
            LetGo(pawn, null);
            Depart(0.5f);
            _ended = "LEFT THE BATWING";
            _endedGood = true;
            return;
        }

        Pin(cam);
        MuteKeys();
        if (_reassert.Elapsed(0.5))
            GameKeys.Reassert();
        if (!remote)
            Still(pawn);

        _frameDt = dt;
        bool listening = Listening;
        if (listening)
        {
            Buttons();
            if (_phase != Phase.Flying)
                return;
        }
        else
        {
            foreach (int vk in FlyKeys)
                _was[vk] = true;
        }

        if (dt <= 0f)
        {
            if (!remote)
                Carry(pawn);
            return;
        }

        FlightInput input = Gather(listening, turn);
        _fm.TopSpeed = Opt.TopSpeed.Number * Metre;

        if (Opt.Assist.On && _fm.SlowFor > 0.8f && !_fm.HoverWanted)
        {
            _fm.HoverWanted = true;
            HoverSounds();
            Flare(1.2f);
            Toast("HOVER");
        }

        Vector3 from = _fm.Position;
        if (_landed)
            Parked(input, dt);
        else
            _fm.Step(dt, input);

        if (!Collide(pawn, from, _fm.Position))
            return;

        TakingFire();
        if (_phase != Phase.Flying)
            return;

        BailWaitStep(dt);
        if (_phase != Phase.Flying)
            return;

        Ceiling(dt);
        Place(_fm.Position, _fm.Forward, _fm.Right, _fm.Up);
        if (!remote)
            Carry(pawn);
        GroundBelow(_fm.Position, out _ground, out _groundAt, out _groundNormal);
        // The solid ground warning is only there for bailing out, which remote control doesn't do.
        if (!remote && _solidCheck.Elapsed(0.5))
        {
            _solidBelow = Safe.Try("batwing solid ground", () => Ground.Real(pawn, _fm.Position, 60000f, out _), fallback: true);
            _loadingBelow = !_solidBelow && StillLoading();
        }

        _noSolidFor = remote || _solidBelow ? 0f : _noSolidFor + dt;
        Warnings();
        // Leading districts towards the craft would pull the loaded area away from Batman.
        if (!remote)
            Stream(dt);
        UpdateEffects(_fm.Hover, _fm.Boosting, _fm.Position, _ground, _groundAt, _groundNormal, _landed ? 0.3f : _fm.Jets, _fm.Velocity, _fm.Throttle, _fm.GLoad, dt);
        Orders orders = PlayerOrders(listening);
        Safe.Run("batwing weapons", () => WeaponsStep(pawn, orders, dt));
        Shake(dt);
    }

    private void Parked(in FlightInput input, float dt)
    {
        _fm.Velocity = Vector3.Zero;
        _fm.Rates = Vector3.Zero;
        bool liftOff = input.Lift > 0.1f || !_fm.HoverWanted || input.Move.LengthSquared() > 0.04f;
        if (!liftOff)
        {
            float want = input.Heading;
            float have = MathF.Atan2(_fm.Forward.Y, _fm.Forward.X);
            float d = MathF.IEEERemainder(want - have, MathF.Tau);
            float yaw = have + d * Ease.Follow(2.5f, dt);
            Vector3 up = _fm.Up;
            var fwd = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
            fwd = Vector3.Normalize(fwd - up * Vector3.Dot(fwd, up));
            _fm.Forward = fwd;
            _fm.Right = Vector3.Cross(up, fwd);
            return;
        }

        _landed = false;
        _fm.Velocity = new Vector3(0f, 0f, 300f);
        Flare(0.5f);
        Puff();
        _fm.Step(dt, input);
    }

    private bool Collide(RPawnPlayer pawn, Vector3 from, Vector3 to)
    {
        if (_craft is not { IsValid: true } craft || Vector3.DistanceSquared(from, to) < 0.01f)
            return true;

        float body = Math.Clamp(MathF.Min(_half.X, _half.Y) * 0.45f, 150f, 420f);
        var extent = new Vector3(body, body, _half.Z * 0.8f);
        Actor? hit = craft.Trace(out Vector3 at, out Vector3 n, to, from, Component.ECollisionFilter.COF_TraceWorld, extent, out _, 0);
        if (hit != null && at != Vector3.Zero && n.LengthSquared() > 0.5f)
        {
            float into = -Vector3.Dot(_fm.Velocity, n);
            bool gentle = n.Z > 0.7f && into < LandSpeed && (_fm.Hover > 0.5f || _fm.Velocity.Length() < LandSpeed * 2f);
            _fm.Position = at + n * 4f;
            if (gentle)
            {
                Touchdown();
                return true;
            }

            float impact = _fm.Bump(n, 0.25f, 0.85f);
            if (Opt.Crashes.On && impact > CrashSpeed)
            {
                Crash(n, "WRECKED");
                return false;
            }

            Hit(at, n, impact);
            if (_phase != Phase.Flying)
                return false;
        }

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 tip = _right * (side * _half.Y * 0.9f);
            if (craft.Trace(out Vector3 tipAt, out Vector3 tipN, to + tip, from + tip, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0) == null
                || tipAt == Vector3.Zero)
                continue;

            _fm.Position += tipN * 60f;
            _fm.Velocity -= tipN * MathF.Min(Vector3.Dot(_fm.Velocity, tipN), 0f) * 1.2f;
            _fm.Rates += new Vector3(side * 1.6f, 0f, -side * 0.4f);
            _shake = MathF.Max(_shake, 0.5f);
            if (Opt.Crashes.On)
                _hull -= MathF.Min(0.3f * _frameDt, 0.03f);
            Burst(_kit?.Impact, tipAt, tipN);
            if (_scrapeSound.Elapsed(0.25))
                Sound(_kit?.Scrape);
        }

        if (Opt.Crashes.On && _hull <= 0f)
        {
            Crash(-_fm.Forward, "WRECKED");
            return false;
        }

        return true;
    }

    private void Hit(Vector3 at, Vector3 n, float impact)
    {
        float hard = Math.Clamp((impact - 400f) / (CrashSpeed - 400f), 0f, 1f);
        if (Opt.Crashes.On)
            _hull -= 0.08f + hard * 0.45f;
        _shake = MathF.Max(_shake, 0.4f + hard * 1.2f);
        Burst(_kit?.Impact, at, n);
        Sound(hard > 0.3f ? _kit?.Hit : _kit?.Scrape);
        Log.Detail(Title + ": hit at " + Fmt.Whole(impact / Metre) + " m/s, hull " + Fmt.Percent(_hull) + ".");
    }

    private void Touchdown()
    {
        if (_landed)
            return;

        _landed = true;
        _fm.Velocity = Vector3.Zero;
        _fm.Rates = Vector3.Zero;
        float yaw = MathF.Atan2(_fm.Forward.Y, _fm.Forward.X);
        _fm.Place(_fm.Position, yaw, Vector3.Zero);
        _fm.HoverWanted = true;
        _fm.Hover = 1f;
        _shake = MathF.Max(_shake, 0.25f);
        Flare(0.5f);
        Puff();
        Sound(_kit?.Land);
        Toast("LANDED");
    }

    private void Crash(Vector3 away, string why)
    {
        Vector3 at = _fm.Position;
        Vector3 off = away.LengthSquared() > 0.01f ? Vector3.Normalize(away) : Vector3.UnitZ;
        Burst(_kit?.Explosion, at, Vector3.UnitZ);
        Burst(_kit?.Impact, at, off);
        RPawnPlayer? pawn = HeldPawn();
        Vector3 fling = off * 900f + new Vector3(0f, 0f, 1500f) + _fm.Velocity * 0.15f;
        LetGo(pawn, new Drop(at + off * (_half.Z + 200f) + new Vector3(0f, 0f, 250f), fling, false));
        DropCraft();
        Sound(_kit?.Boom);
        Sound(_kit?.Wreck);
        _phase = Phase.Off;
        _ended = why;
        _endedGood = false;
        Log.Info(Title + ": " + why.ToLowerInvariant() + " at " + Geo.Position(at) + ", " + Fmt.Whole(_fm.Velocity.Length() / Metre) + " m/s.");
    }

    private void GroundBelow(Vector3 at, out float distance, out Vector3 point, out Vector3 normal)
    {
        distance = GroundReach;
        point = at - new Vector3(0f, 0f, GroundReach);
        normal = Vector3.UnitZ;
        if (_craft is not { IsValid: true } craft)
            return;

        Vector3 bottom = at - new Vector3(0f, 0f, _half.Z * 0.6f);
        if (Ground.Ray(craft, bottom, bottom - new Vector3(0f, 0f, GroundReach), out Vector3 hit, out Vector3 n))
        {
            distance = bottom.Z - hit.Z;
            point = hit;
            normal = n;
        }
    }

    private void Warnings()
    {
        if (!_lookAhead.Elapsed(0.12))
            return;

        _impactAt = 99f;
        float speed = _fm.Velocity.Length();
        if (_fm.HoverWanted || speed < 2500f || _craft is not { IsValid: true } craft)
            return;

        float reach = MathF.Min(speed * 4f, 40000f);
        Vector3 dir = _fm.Velocity / speed;
        if (Ground.Ray(craft, _fm.Position, _fm.Position + dir * reach, out Vector3 hit, out _))
            _impactAt = Vector3.Distance(_fm.Position, hit) / speed;
    }

    private void Stream(float dt)
    {
        if (_outside)
        {
            _outsideFor += dt;
            if (_outsideFor > 3f && Districts.Outline(out Vector3 min, out Vector3 max))
            {
                Vector3 mid = (min + max) * 0.5f;
                var back = new Vector3(mid.X - _fm.Position.X, mid.Y - _fm.Position.Y, 0f);
                if (back.LengthSquared() > 1f)
                    _fm.Velocity += Vector3.Normalize(back) * (900f * dt * MathF.Min(_outsideFor - 2f, 3f));
            }
        }
        else
        {
            _outsideFor = 0f;
        }

        KeepLoading();
        if (!_districts.Elapsed(0.3))
            return;

        TrackDistrict();
    }

    private void TrackDistrict()
    {
        Lead();
        if (Districts.Under(_fm.Position) != null)
        {
            _outside = false;
            return;
        }

        if (!Districts.Outline(out Vector3 lo, out Vector3 hi))
        {
            _outside = false;
            return;
        }

        // Wide margin since the district map only knows what we've flown over so far.
        const float margin = 30000f;
        Vector3 at = _fm.Position;
        _outside = at.X < lo.X - margin || at.X > hi.X + margin || at.Y < lo.Y - margin || at.Y > hi.Y + margin;
    }

    private void Ceiling(float dt)
    {
        if (!Districts.Outline(out _, out Vector3 max))
            return;

        float over = _fm.Position.Z - (max.Z + 30000f);
        if (over > 0f)
            _fm.Velocity -= new Vector3(0f, 0f, MathF.Min(over * 0.5f, 2500f) * dt);
    }
}
