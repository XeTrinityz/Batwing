using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// Batman flying the Batwing, from the cockpit or by remote.
internal sealed partial class Pilot : Phase
{
    private const string HeldKey = "batwing.held";
    private const string KeysOwner = "flight";
    private const float MovedAway = 2500f;
    private const float BailWaitMost = 30f;
    private const float CatchWings = 1500f;

    private readonly Craft _craft;
    private readonly FlightModel _fm;
    private readonly RPawnPlayer _batman;
    private readonly Edges _press = new();
    private readonly Streamer _streamer = new();

    private bool _released;
    private bool _wasTickOff, _wasAnimsPaused, _wasHidden, _wasCollide, _wasBlock;
    private AkEvent? _fallLoop;
    private Vector3 _carriedTo;

    private float _bailWait = -1f;
    private Every _bailCheck;
    private Latch? _latch;

    public bool Remote { get; }

    public bool Waiting => _bailWait >= 0f;

    public Craft Craft => _craft;

    public override string Status => Remote
        ? (_far ? "REMOTE // WEAK SIGNAL" : "REMOTE")
        : _landed ? "LANDED" : Waiting ? "WAITING FOR GROUND" : _craft.Carrying ? "CARRYING BATMOBILE" : _fm.Boosting ? "AFTERBURNER" : _fm.Hover > 0.5f ? "HOVER" : "WINGS";

    public override bool Blink => Waiting;

    public static Pilot? Current => Wing.Now as Pilot;

    public static void Shake(float amount)
    {
        if (Current is { } p)
            p._shake = MathF.Max(p._shake, amount);
    }

    private Pilot(Craft craft, RPawnPlayer batman, bool remote)
    {
        _craft = craft;
        _fm = craft.Fm;
        _batman = batman;
        Remote = remote;
    }

    // On the ground under a hovering Batwing, Batman climbs in.
    public static void Board(Craft craft, RPawnPlayer pawn, Vector3 at, float yawDegrees, bool hover)
    {
        craft.Fm.Place(at, yawDegrees / Geo.Deg, Vector3.Zero);
        craft.Fm.Hover = hover ? 1f : 0f;
        craft.Fm.HoverWanted = hover;
        craft.Fm.Throttle = 0.7f;
        TakeOff(craft, pawn, remote: false);
    }

    // No fly-in. The Batwing appears round Batman, carrying on with his speed if he's gliding.
    public static void BoardNow(RPawnPlayer pawn, Kit kit, bool airborne)
    {
        Vector3 v = pawn.Velocity;
        var flat = new Vector3(v.X, v.Y, 0f);
        bool gliding = airborne && flat.Length() > 1500f;
        float yaw = gliding ? Geo.YawOf(flat) : CameraHook.ViewYaw(pawn);
        Vector3 at = airborne ? pawn.Location : pawn.Location + new Vector3(0f, 0f, Inbound.HoverAbove);
        if (!airborne && !Ground.Clear(pawn, pawn.Location + new Vector3(0f, 0f, 100f), at + new Vector3(0f, 0f, 200f)))
            throw new Refusal("no room above you");

        Craft craft = Craft.Spawn(kit, at, yaw);
        Wing.Use(craft);
        if (gliding)
        {
            Vector3 dir = Vector3.Normalize(new Vector3(v.X, v.Y, MathF.Min(v.Z, 0f) * 0.3f));
            craft.Fm.Place(at, yaw / Geo.Deg, dir * MathF.Max(v.Length(), FlightModel.StallSpeed * 1.5f));
            craft.Fm.Hover = 0f;
            craft.Fm.HoverWanted = false;
            craft.Fm.Throttle = 0.85f;
        }
        else
        {
            craft.Fm.Place(at, yaw / Geo.Deg, airborne ? v * 0.5f : Vector3.Zero);
            craft.Fm.Hover = 1f;
            craft.Fm.HoverWanted = true;
            craft.Fm.Throttle = 0.7f;
        }

        craft.Follow();
        TakeOff(craft, pawn, remote: false);
    }

    // Caught in the air by the escort. Its speed carries on, on the wings if it's fast across the ground.
    public static void CatchBoard(Craft craft, RPawnPlayer flier, Vector3 at, float yawDegrees, Vector3 vel)
    {
        var flat = new Vector3(vel.X, vel.Y, 0f);
        if (flat.Length() > CatchWings)
        {
            Vector3 dir = Vector3.Normalize(new Vector3(vel.X, vel.Y, vel.Z * 0.3f));
            craft.Fm.Place(at, Geo.YawOf(flat) / Geo.Deg, dir * MathF.Max(vel.Length(), FlightModel.StallSpeed * 1.5f));
            craft.Fm.Hover = 0f;
            craft.Fm.HoverWanted = false;
            craft.Fm.Throttle = 0.85f;
        }
        else
        {
            craft.Fm.Place(at, yawDegrees / Geo.Deg, vel);
            craft.Fm.Hover = 1f;
            craft.Fm.HoverWanted = true;
            craft.Fm.Throttle = 0.7f;
        }

        TakeOff(craft, flier, remote: false);
    }

    private static void TakeOff(Craft craft, RPawnPlayer pawn, bool remote)
    {
        var pilot = new Pilot(craft, pawn, remote);
        Wing.Go(pilot);
        pilot.Begin();
    }

    private void Begin()
    {
        if (Remote)
            Tether();
        else
            Hold();

        _craft.StartEngine();
        _fm.Fuel = 1f;
        _fm.TopSpeed = Opt.TopSpeed.Number * Geo.Metre;
        _fm.Release();
        _press.Reset();
        _streamer.Reset();
        StartView();
        HoldKeys();
        _craft.Hatch(close: true);
        Rumble.Play(Buzz.Land);

        if (Remote)
        {
            Wing.Toast("REMOTE CONTROL");
            return;
        }

        Wing.Toast(_fm.HoverWanted ? "ABOARD // HOVER" : "ABOARD");
        Log.Info("Batwing: aboard at " + Geo.Position(_fm.Position) + (_fm.HoverWanted ? ", hovering." : ", flying at " + Fmt.Whole(_fm.Velocity.Length() / Geo.Metre) + " m/s."));
    }

    // Batman rides along hidden and still, moved with the craft so the city streams round him.
    private void Hold()
    {
        RPawnPlayer p = _batman;
        _wasTickOff = p.bTickIsDisabled;
        _wasAnimsPaused = p.Mesh is { IsValid: true } mesh && mesh.bPauseAnims;
        _wasHidden = p.bHidden;
        _wasCollide = p.bCollideActors;
        _wasBlock = p.bBlockActors;
        Shared.SetFlag(HeldKey, true);

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
    }

    private static void Still(RPawnPlayer p)
    {
        if (!p.bTickIsDisabled)
            p.SetTickIsDisabled(true);
        if (p.Mesh is { IsValid: true } mesh && !mesh.bPauseAnims)
            mesh.bPauseAnims = true;
    }

    private void Carry(RPawnPlayer p)
    {
        p.SetLocationIgnoringCollision(_fm.Position);
        p.Velocity = Vector3.Zero;
        _carriedTo = p.Location;
    }

    // Batman as long as he's still the player.
    private RPawnPlayer? Batman() =>
        _batman is { IsValid: true, bDeleteMe: false } p && World.Player is { } now && now.Ptr == p.Ptr ? p : null;

    private void HoldKeys()
    {
        var keys = new List<Keys>();
        foreach ((Act act, Bind b) in Opt.KeyBinds)
        {
            if (act >= Act.Forward && b.Key != Keys.None)
                keys.Add(b.Key);
        }

        GameKeys.Hold(KeysOwner, true, keys.ToArray(), GameKeys.Wheel.Concat(GameKeys.PadNames).ToArray());
    }

    public override void Step(PlayerController pc, float dt)
    {
        if (!_craft.Alive)
        {
            Wing.Stop("THE BATWING WAS LOST", false);
            return;
        }

        if (Batman() is not { } pawn)
        {
            Wing.Stop("ENDED: BATMAN CHANGED", false);
            return;
        }

        if (World.InCinematic(pc))
        {
            Release(pawn, Remote ? null : BailSpot());
            Departing.From(_craft, 0.5f);
            Toasts.Notice("LEFT FOR A CUTSCENE", true);
            return;
        }

        if (pawn.Health <= 0)
        {
            if (Remote)
                EndRemote("BATMAN IS DOWN");
            else
                Crash(-_fm.Velocity, "BATMAN IS DOWN");
            return;
        }

        if (Remote)
        {
            if (!RemoteChecks(pawn, dt))
                return;
        }
        else if (Vector3.DistanceSquared(pawn.Location, _carriedTo) > MovedAway * MovedAway)
        {
            Log.Info("Batwing: the game moved Batman out of the craft (" + Geo.Position(pawn.Location) + "), letting him go.");
            Release(pawn, null);
            Departing.From(_craft, 0.5f);
            Toasts.Notice("LEFT THE BATWING", true);
            return;
        }

        Pin();
        HoldKeys();
        if (!Remote)
            Still(pawn);

        bool listening = Controls.Listening;
        if (listening)
        {
            Buttons();
            if (Wing.Now != this)
                return;
        }

        if (dt <= 0f)
        {
            if (!Remote)
                Carry(pawn);
            return;
        }

        // Dropping onto the Batmobile to hook it takes the controls for a moment.
        if (_latch != null)
        {
            if (_latch.Step(_craft, dt))
            {
                if (!Remote)
                    Carry(pawn);
                Decay(dt);
                return;
            }

            if (_latch.Hooked)
                Wing.Toast("BATMOBILE HOOKED");
            _latch = null;
        }

        FlightInput input = Gather(listening, dt);
        _fm.TopSpeed = Opt.TopSpeed.Number * Geo.Metre * (_craft.Carrying ? Escort.CargoSpeed : 1f);

        if (Opt.AssistLevel == Assist.Full && _fm.SlowFor > 0.8f && !_fm.HoverWanted)
            SetHover(true, "HOVER");

        Vector3 from = _fm.Position;
        if (_landed)
            Parked(input, dt);
        else
            _fm.Step(dt, input);

        if (!Collide(from))
            return;

        TakingFire();
        if (Wing.Now != this)
            return;

        BailWaiting(dt);
        if (Wing.Now != this)
            return;

        Bounds(dt);
        _craft.Follow();
        if (!Remote)
            Carry(pawn);
        _craft.Measure();
        GroundCheck(pawn, dt);
        Warnings();

        // Leading the districts towards the craft on remote would pull the loaded city away from Batman.
        if (!Remote)
            _streamer.Step(_fm.Position, _fm.Velocity);

        Look look = _craft.Flying();
        if (_landed)
            look.Jets = 0.3f;
        _craft.Drift.Parked = _landed;
        _craft.Show(look, dt);
        Orders orders = PlayerOrders(listening);
        Safe.Run("pilot weapons", () => _craft.Weapons.Step(pawn, orders, dt));
        Decay(dt);
    }

    private void Buttons()
    {
        if (_press.Take(Act.Mode))
            SetHover(!_fm.HoverWanted, null);

        if (_press.Take(Act.Bail))
        {
            Exit();
            return;
        }

        if (_press.Take(Act.Camera))
        {
            Opt.Camera.Step(1);
            Settings.MarkDirty();
            Wing.Toast("CAMERA: " + Opt.Camera.Value);
        }

        if (_press.Take(Act.Hints))
            _hintUntil = Clock.Now < _hintUntil ? 0.0 : double.MaxValue;

        // The keyboard lift key comes in through the hotkeys like the mod's other keys.
        if (_press.Take(Act.Lift) && Controls.PadActive)
            Wing.LiftPressed();
    }

    private void SetHover(bool hover, string? say)
    {
        if (hover == _fm.HoverWanted)
            return;

        _fm.HoverWanted = hover;
        if (hover)
        {
            if (Opt.Direct)
                _aimYaw = Geo.YawOf(_fm.Forward);
            _craft.HoverSounds();
            _craft.Flare(1.2f);
        }
        else
        {
            _fm.Throttle = MathF.Max(_fm.Throttle, 0.8f);
            _landed = false;
            _craft.WingSounds();
            if (Waiting)
            {
                _bailWait = -1f;
                Wing.Toast("WINGS // BAIL OUT CANCELLED");
                return;
            }
        }

        Wing.Toast(say ?? (hover ? "HOVER" : "WINGS"));
    }

    // The call or bail button.
    public void Exit()
    {
        if (Remote)
            EndRemote("REMOTE OFF");
        else if (Waiting)
            Bail("BAILED OUT");
        else
            Bailout();
    }

    private void Bailout()
    {
        if (GroundLoaded())
        {
            Bail("BAILED OUT");
            return;
        }

        string? below = Districts.Under(_fm.Position);
        if (!Streamer.StillLoading(_fm.Position))
        {
            Log.Info("Batwing: no solid ground under " + Geo.Position(_fm.Position) + " (over " + (below ?? "no district") + "), staying aboard.");
            Toasts.Notice("NOTHING SOLID BELOW // FLY OVER THE STREETS TO GET OUT", false);
            return;
        }

        _bailWait = 0f;
        _bailCheck.Force();
        if (!_fm.HoverWanted)
            SetHover(true, "HOVER");

        Log.Info("Batwing: bail out waiting for the ground to load (over " + (_streamer.Cell ?? "no district") + ").");
        _streamer.Waiting(_fm.Position, _craft.Below.Distance, true);
        Wing.Toast("WAITING FOR THE GROUND");
    }

    private void BailWaiting(float dt)
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
            _streamer.Waiting(_fm.Position, _craft.Below.Distance, true);
            Log.Info("Batwing: the ground still hasn't loaded after " + Fmt.Whole(_bailWait) + " s, staying aboard.");
            _bailWait = -1f;
            Toasts.Notice("THE GROUND HERE ISN'T LOADING // STAYING ABOARD", false);
            return;
        }

        if (!_bailCheck.Elapsed(0.25))
            return;

        bool ready = GroundLoaded();
        _streamer.Waiting(_fm.Position, _craft.Below.Distance, false);
        if (!ready && _bailWait > 2f && !Streamer.StillLoading(_fm.Position))
        {
            Log.Info("Batwing: the district loaded but there's no ground under " + Geo.Position(_fm.Position) + ", staying aboard.");
            _bailWait = -1f;
            Toasts.Notice("NOTHING SOLID BELOW // FLY OVER THE STREETS TO GET OUT", false);
            return;
        }

        if (ready)
        {
            Log.Info("Batwing: the ground loaded after " + Fmt.Tenths(_bailWait) + " s.");
            Bail("BAILED OUT");
        }
    }

    private bool GroundLoaded()
    {
        if (Batman() is not { } p)
            return true;

        _streamer.Lead(_fm.Position, Vector3.Zero);
        return Ground.Real(p, _fm.Position, 60000f, out _);
    }

    private void Bail(string why)
    {
        _bailWait = -1f;
        if (_craft.Cargo is { Alive: true } held)
        {
            bool low = held.Low(Escort.LowDrop);
            held.Drop(low ? Vector3.Zero : _fm.Velocity, low);
            _craft.Cargo = null;
        }

        RPawnPlayer? pawn = Batman();
        Release(pawn, BailSpot());
        if (pawn != null && Escort.AfterBail(_craft, pawn.Location))
        {
            Wing.Toast(why + " // ON ESCORT");
            return;
        }

        _craft.Hatch(close: false);
        Departing.From(_craft, 1.2f);
        Wing.Toast(why);
    }

    private readonly record struct Drop(Vector3 At, Vector3 Velocity, bool OnGround);

    // Straight down onto the ground when low and slow, otherwise out of the belly with the craft's speed.
    // Going out of the top put Batman on the hull as it climbed away, and he fell through it.
    private Drop BailSpot()
    {
        Vector3 at = _fm.Position;
        if ((_landed || _fm.Hover > 0.5f) && _craft.Below.Distance < 1800f && Batman() is { } p
            && Ground.Ray(p, at - new Vector3(0f, 0f, _craft.Half.Z), at - new Vector3(0f, 0f, 3000f), out Vector3 floor, out _))
            return new Drop(floor + new Vector3(0f, 0f, Ground.Lift(p)), Vector3.Zero, true);

        Vector3 v = _fm.Velocity;
        var fall = new Vector3(v.X * 0.7f, v.Y * 0.7f, MathF.Min(v.Z * 0.5f, v.Z) - 400f);
        if (Batman() is not { } b)
            return new Drop(at - new Vector3(0f, 0f, _craft.Depth + 300f), fall, false);

        float lift = Ground.Lift(b);
        Vector3 belly = at - new Vector3(0f, 0f, _craft.Depth);
        Vector3 below = belly - new Vector3(0f, 0f, lift + 150f);
        Actor tracer = _craft.Alive ? _craft.Actor : b;
        if (!Ground.Ray(tracer, belly, below - new Vector3(0f, 0f, lift), out Vector3 hit, out Vector3 n))
            return new Drop(below, fall, false);

        return n.Z > 0.6f
            ? new Drop(hit + new Vector3(0f, 0f, lift), Vector3.Zero, true)
            : new Drop(hit + n * lift, fall, false);
    }

    // Lets Batman go, put back as he was. Only the first call does anything.
    private void Release(RPawnPlayer? p, Drop? drop)
    {
        if (_released)
            return;

        _released = true;
        if (Remote)
            Untether();
        Shared.SetFlag(HeldKey, false);
        _craft.Weapons.Off();
        GameKeys.Release(KeysOwner);
        Unpin();
        Wing.BlendOut(_lastPov);
        if (Remote)
            return;

        p ??= _batman is { IsValid: true, bDeleteMe: false } still ? still : null;
        if (p == null)
            return;

        Inbound.EndGrapple();
        float yaw = Geo.YawOf(_fm.Forward);
        RPawnPlayer pawn = p;
        Safe.Run("batwing release", () =>
        {
            if (pawn.bHidden != _wasHidden)
                pawn.SetHidden(_wasHidden);
            pawn.SetCollision(_wasCollide, _wasBlock, pawn.bIgnoreEncroachers);
            if (pawn.bTickIsDisabled != _wasTickOff)
                pawn.SetTickIsDisabled(_wasTickOff);
            if (pawn.Mesh is { IsValid: true } mesh && mesh.bPauseAnims != _wasAnimsPaused)
                mesh.bPauseAnims = _wasAnimsPaused;
            if (_fallLoop is { IsValid: true } loop)
                pawn.FallLoopSound = loop;
            _fallLoop = null;

            if (drop is { } d)
            {
                pawn.SetLocationIgnoringCollision(d.At);
                pawn.SetRotation(new Rotator(0f, yaw, 0f));
            }

            pawn.SetPhysics(Actor.EPhysics.PHYS_Falling, false);
            pawn.Velocity = drop?.Velocity ?? Vector3.Zero;
            if (drop is { OnGround: true })
                Ground.Settle(pawn);
        });
    }

    public override void Leave() => Release(Batman(), null);

    // A script reload mid flight leaves Batman hidden and frozen, and on remote his legs taken.
    public static void Recover()
    {
        RecoverRemote();
        if (!Shared.Flag(HeldKey))
            return;

        Shared.SetFlag(HeldKey, false);
        if (World.Player is not { IsValid: true } pawn)
            return;

        Log.Info("Batwing: scripts reloaded mid flight, putting Batman back.");
        if (pawn.bHidden)
            pawn.SetHidden(false);
        if (pawn.bTickIsDisabled)
            pawn.SetTickIsDisabled(false);
        if (pawn.Mesh is { IsValid: true } mesh)
            mesh.bPauseAnims = false;
        pawn.SetCollision(true, true, pawn.bIgnoreEncroachers);
        pawn.SetPhysics(Actor.EPhysics.PHYS_Falling, false);
    }

    // Pilot side of the Batmobile lift. Hover over the parked car to hook it, press again to drop it.
    public void HookOrDrop()
    {
        if (_latch != null)
            return;

        if (_craft.Cargo is { Alive: true } held)
        {
            bool low = held.Low(Escort.LowDrop);
            held.Drop(low ? Vector3.Zero : _fm.Velocity, low);
            _craft.Cargo = null;
            Wing.Toast(low ? "BATMOBILE SET DOWN" : "BATMOBILE DROPPED");
            return;
        }

        RVehicleBatmobileBase car = Hookable(out string? why) ?? throw new Refusal(why ?? "no Batmobile in the city");
        _latch = Latch.Start(_craft, car);
        Wing.Toast("HOOKING");
    }

    private const float HookAcross = 1500f;
    private const float HookBelow = 3500f;
    private const float HookSlow = 1200f;

    public RVehicleBatmobileBase? Hookable(out string? why)
    {
        why = null;
        if (Batmobile.Current() is not { IsValid: true, bDeleteMe: false } car)
        {
            why = "no Batmobile in the city";
            return null;
        }

        if (car.Driver is { IsValid: true })
        {
            why = "the Batmobile is busy";
            return null;
        }

        Vector3 d = car.Location - _fm.Position;
        if (MathF.Sqrt(d.X * d.X + d.Y * d.Y) > HookAcross || -d.Z > HookBelow || -d.Z < _craft.Half.Z)
        {
            why = "hover right over the Batmobile to hook it, it's " + Fmt.Metres(d.Length()) + " away";
            return null;
        }

        if (!_fm.HoverWanted || _fm.Velocity.Length() > HookSlow)
        {
            why = "slow to a hover over it first";
            return null;
        }

        return car;
    }

    public bool Hooking => _latch != null;

    public override void Draw(Canvas canvas) => Hud.Flight(this, canvas);
}

// Dropping onto the Batmobile, turning to match it, and hooking it at the bottom.
internal sealed class Latch
{
    private const float Seconds = 1.4f;

    private readonly RVehicleBatmobileBase _car;
    private readonly Vector3 _from;
    private readonly Rotator _rot;
    private readonly float _hang;
    private float _t;

    public bool Hooked { get; private set; }

    public RVehicleBatmobileBase Car => _car;

    private Latch(RVehicleBatmobileBase car, Vector3 from, Rotator rot, float hang)
    {
        _car = car;
        _from = from;
        _rot = rot;
        _hang = hang;
    }

    public static Latch Start(Craft craft, RVehicleBatmobileBase car)
    {
        craft.Puff();
        return new Latch(car, craft.Position, Geo.FromAxes(craft.Fwd, craft.Right, craft.Up), Cargo.HangFor(craft, car));
    }

    // True while it's still going down.
    public bool Step(Craft craft, float dt)
    {
        if (_car is not { IsValid: true, bDeleteMe: false })
            return false;
        if (dt <= 0f)
            return true;

        _t += dt;
        float k = Ease.Smooth(_t / Seconds);
        Vector3 top = _car.Location + new Vector3(0f, 0f, _hang);
        Vector3 at = Vector3.Lerp(_from, top, k);
        float yaw = _rot.Yaw + Geo.WrapDegrees(_car.Rotation.Yaw - _rot.Yaw) * k;
        Geo.Axes(new Rotator(_rot.Pitch * (1f - k), yaw, _rot.Roll * (1f - k)), out Vector3 fwd, out Vector3 right, out Vector3 up);
        craft.Fm.Place(at, yaw / Geo.Deg, Vector3.Zero);
        craft.Fm.Orient(fwd, right, up);
        craft.Fm.Hover = 1f;
        craft.Fm.HoverWanted = true;
        craft.Place(at, fwd, right, up);
        craft.Measure();
        craft.Show(new Look { Hover = 1f, Jets = 0.6f, Throttle = 0.5f, G = 1f }, dt);
        if (k < 1f)
            return true;

        craft.Cargo = Cargo.Hook(craft, _car);
        Hooked = true;
        return false;
    }
}
