using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// The checks before the Batwing can come. Each failure says why in words the player sees.
internal static class Require
{
    public static RPawnPlayer OnFoot()
    {
        PlayerController pc = Common();
        if (World.Pawn is RVehicle)
            throw new Refusal("get out of the Batmobile first");
        if (World.Pawn is not RPawnPlayer pawn)
            throw new Refusal("the game is still loading");
        if (CameraHook.PlayerCam() is not { } cam || !CameraHook.OnPlayer(cam, pc))
            throw new Refusal("not during a cutscene");
        if (pawn.Health <= 0)
            throw new Refusal("not while you're down");
        if (!Ground.InCity(pawn.Location))
            throw new Refusal("only out in the city");
        return pawn;
    }

    public static RVehicleBatmobileBase InCar(RVehicleBatmobileBase car)
    {
        Common();
        if (!Ground.InCity(car.Location))
            throw new Refusal("only out in the city");
        return car;
    }

    private static PlayerController Common()
    {
        if (!World.Ready || World.Pc is not { } pc)
            throw new Refusal("the game is still loading");
        if (World.Paused)
            throw new Refusal("not while paused");
        if (World.InCinematic(pc))
            throw new Refusal("not during a cutscene");
        return pc;
    }

    public static void Sky(Pawn pawn)
    {
        if (!Ground.SkyAbove(pawn, pawn.Location, Inbound.SkyCheck))
            throw new Refusal("it needs open sky above you");
    }
}

// The Batwing flying in to hover over Batman, then Batman grappling up or climbing in.
internal sealed class Inbound : Phase
{
    public const float SkyCheck = 4000f;
    public const float HoverAbove = 1300f;
    private const float ArriveSeconds = 5.5f;
    private const float GrappleReach = 450f;
    private const float GrappleMost = 5f;
    private const float HandSeconds = 0.7f;
    private const float HandDrag = 4f;

    private static readonly FName FallingState = new("FallingMode");
    private static readonly FName NoLabel = new("None");
    private static readonly FName[] LineStates = { new("GrappleMovement"), new("SpecialMove"), new("HangMode"), new("LedgeHanging") };

    private readonly Craft _craft;
    private readonly Vector3 _from;
    private Vector3 _at, _last, _vel;
    private float _t, _yaw, _bank;
    private float _grappleT = -1f, _grappleYaw;
    private Vector3 _grappleTo;

    // Taking over from the escort for a pickup. It eases out of the pose and drift it had instead of snapping.
    private float _handT = -1f;
    private Rotator _handFrom;
    private Vector3 _handVel;

    private Inbound(Craft craft, Vector3 from, Vector3 at, float yaw)
    {
        _craft = craft;
        _from = _last = from;
        _at = at;
        _yaw = yaw;
    }

    public Craft Craft => _craft;

    public override string Status => _grappleT >= 0f ? "GRAPPLING" : "INBOUND";

    public override Rgba Lamp => Palette.Caution;

    public override bool Blink => true;

    public float Eta => MathF.Max(0f, ArriveSeconds - _t);

    public float Progress => Math.Clamp(_t / ArriveSeconds, 0f, 1f);

    private static Vector3 HoverPoint(Pawn pawn) => pawn.Location + new Vector3(0f, 0f, HoverAbove);

    public static void Call()
    {
        RPawnPlayer pawn = Require.OnFoot();
        Kit kit = Kit.Load();
        bool airborne = pawn.Physics != Actor.EPhysics.PHYS_Walking && !NearGround(pawn);
        if (!Opt.FlyIn.On || airborne)
        {
            Pilot.BoardNow(pawn, kit, airborne);
            return;
        }

        Require.Sky(pawn);
        Vector3 hover = HoverPoint(pawn);
        float camYaw = CameraHook.ViewYaw(pawn);
        Vector3? start = null;
        foreach (float turn in new[] { 180f, 135f, -135f, 90f, -90f, 45f, -45f, 0f })
        {
            Vector3 flat = Geo.Facing(camYaw + turn);
            Vector3 s = hover + flat * 16000f + new Vector3(0f, 0f, 5000f);
            Vector3 mid = hover + flat * 6000f + new Vector3(0f, 0f, 2600f);
            if (Ground.Clear(pawn, s, mid) && Ground.Clear(pawn, mid, hover + new Vector3(0f, 0f, 2200f)))
            {
                start = s;
                break;
            }
        }

        Vector3 from = start ?? hover + new Vector3(0f, 0f, 9000f);
        float yaw = Geo.YawOf(hover - from);
        Craft craft = Craft.Spawn(kit, from, yaw);
        Wing.Use(craft);
        craft.StartEngine();
        Wing.Go(new Inbound(craft, from, hover, yaw));
        Wing.Voice.Start(kit.Rescue, from);
        Craft.Play(kit.Called, pawn);
        Wing.Toast("INBOUND");
        Log.Info("Batwing: called in from " + Geo.Position(from) + " to hover over " + Geo.Position(hover) + ".");
    }

    // The escort is already close, so it hands over at the very end of an arrival, where the grapple fires.
    public static void Handover(Craft craft, float yawDegrees)
    {
        var inbound = new Inbound(craft, craft.Position, craft.Position, yawDegrees)
        {
            _t = ArriveSeconds,
            _handT = 0f,
            _handFrom = Geo.FromAxes(craft.Fwd, craft.Right, craft.Up),
            _handVel = craft.Fm.Velocity,
        };
        Wing.Go(inbound);
    }

    // Some ground moves aren't walking. Boarding in place there spawns the craft in the floor and wrecks it.
    private static bool NearGround(RPawnPlayer pawn) =>
        !Ground.Clear(pawn, pawn.Location, pawn.Location - new Vector3(0f, 0f, Ground.Lift(pawn) + 400f));

    public override void Step(PlayerController pc, float dt)
    {
        if (!_craft.Alive)
        {
            Wing.Go(null);
            return;
        }

        if (World.Pawn is not RPawnPlayer pawn || pawn.Health <= 0 || World.InCinematic(pc))
        {
            Departing.From(_craft, 0f);
            return;
        }

        if (dt <= 0f)
            return;

        _t += dt;
        float t = Math.Clamp(_t / ArriveSeconds, 0f, 1f);
        if (_handT >= 0f)
        {
            _at += _handVel * dt;
            _handVel *= MathF.Exp(-HandDrag * dt);
        }
        else if (_grappleT < 0f)
        {
            _at = Vector3.Lerp(_at, HoverPoint(pawn), Ease.Follow(3f, dt));
        }

        // A curve in from the side that drops onto the hover point from above.
        Vector3 p0 = _from;
        Vector3 p1 = _from + new Vector3(_at.X - _from.X, _at.Y - _from.Y, 0f) * 0.55f;
        Vector3 p2 = _at + new Vector3(0f, 0f, 2200f);
        Vector3 p3 = _at;
        float e = 1f - MathF.Pow(1f - t, 2.4f);
        float u = 1f - e;
        Vector3 at = u * u * u * p0 + 3f * u * u * e * p1 + 3f * u * e * e * p2 + e * e * e * p3;

        Vector3 vel = (at - _last) / dt;
        _last = at;
        _vel = Vector3.Lerp(_vel, vel, Ease.Follow(8f, dt));
        float speed = _vel.Length();

        float settle = Ease.Smooth((t - 0.5f) / 0.4f);
        if (settle > 0.05f && !_craft.Flaring && t < 0.8f)
        {
            _craft.Flare(1.6f);
            _craft.HoverSounds();
        }

        float pathYaw = speed > 200f ? Geo.YawOf(_vel) : _yaw;
        float camYaw = _grappleT >= 0f ? _grappleYaw : CameraHook.ViewYaw(pawn);
        float yawRate = Geo.WrapDegrees(pathYaw - _yaw) / dt;
        _yaw += Geo.WrapDegrees(pathYaw - _yaw) * Ease.Follow(6f, dt);
        float bankWant = Math.Clamp(MathF.Atan(yawRate / Geo.Deg * speed / FlightModel.Gravity) * Geo.Deg, -40f, 40f);
        _bank += (bankWant - _bank) * Ease.Follow(4f, dt);

        float yaw = _yaw + Geo.WrapDegrees(camYaw - _yaw) * settle;
        float climb = speed > 200f ? Math.Clamp(Geo.PitchOf(_vel) * 0.5f, -15f, 12f) : 0f;
        float pitch = climb * (1f - settle) + 9f * MathF.Sin(MathF.PI * settle);
        float roll = _bank * (1f - settle);
        if (_handT >= 0f)
        {
            _handT += dt;
            float k = Ease.Smooth(_handT / HandSeconds);
            yaw = _handFrom.Yaw + Geo.WrapDegrees(yaw - _handFrom.Yaw) * k;
            pitch = _handFrom.Pitch + Geo.WrapDegrees(pitch - _handFrom.Pitch) * k;
            roll = _handFrom.Roll + Geo.WrapDegrees(roll - _handFrom.Roll) * k;
            if (k >= 1f)
                _handT = -1f;
        }

        Geo.Axes(new Rotator(pitch, yaw, roll), out Vector3 fwd, out Vector3 right, out Vector3 up);
        _craft.Fm.Place(at, yaw / Geo.Deg, _vel);
        _craft.Fm.Orient(fwd, right, up);
        _craft.Place(at, fwd, right, up);
        _craft.Measure();
        _craft.Show(new Look { Hover = settle, Jets = 0.4f + settle, Velocity = _vel, Throttle = 0.6f, G = 1f }, dt);

        if (t < 1f)
            return;

        Wing.Voice.End(_craft.Kit.RescueStop);
        if (_grappleT >= 0f)
        {
            Grappling(pawn, at, yaw, dt);
            return;
        }

        if (Opt.Grapple.On && pawn.Physics == Actor.EPhysics.PHYS_Walking && Grapple(pawn, pc, at))
            return;

        Pilot.Board(_craft, pawn, at, yaw, hover: true);
    }

    // Fires Batman's grapple at the belly, as if at a grapple point.
    private bool Grapple(RPawnPlayer pawn, PlayerController pc, Vector3 at)
    {
        if (pc is not RPlayerController rpc || pawn.GrappleGun is not { IsValid: true } gun || gun.GrappleFireMove is not { IsValid: true } move)
            return false;

        Vector3 to = at - _craft.Up * (_craft.Half.Z + 80f);
        var flat = new Vector3(pawn.Location.X - to.X, pawn.Location.Y - to.Y, 0f);
        var loc = new REnvironmentCheckTicker.FEnvironmentSpecialMoveLocator
        {
            Location = to,
            Normal = flat.LengthSquared() > 1f ? Vector3.Normalize(flat) : -_craft.Fwd,
        };

        bool fired = Safe.Try("batwing grapple", () =>
        {
            gun.ActiveGrappleTarget = to;
            gun.ActiveOutwardNormal = loc.Normal;
            gun.ActiveHidePoint = null!;
            gun.ActiveSwingGrapple = false;
            RGrapplePoint.FGrapplePointInfo info = gun.ActiveGrapplePoint;
            info.bValid = true;
            info.OutwardNormal = loc.Normal;
            info.PointA = to;
            info.PointB = to;
            info.SourceGrapplePoint = null!;
            info.VantagePointGrapplePoint = null!;
            gun.ActiveGrapplePoint = info;
            move.TriggerSpecialMove(rpc, loc, false);
            return true;
        });
        if (!fired)
            return false;

        _grappleT = 0f;
        _grappleTo = to;
        _grappleYaw = CameraHook.ViewYaw(pawn);
        Wing.Toast("GRAPPLE");
        Log.Detail("Batwing: grapple fired at the belly, " + Fmt.Metres(Vector3.Distance(pawn.Location, to)) + " up.");
        return true;
    }

    private void Grappling(RPawnPlayer pawn, Vector3 at, float yaw, float dt)
    {
        _grappleT += dt;
        float away = Vector3.Distance(pawn.Location, _grappleTo);
        bool standing = _grappleT > 1f && pawn.Physics == Actor.EPhysics.PHYS_Walking;
        bool late = _grappleT >= GrappleMost;
        if (away > GrappleReach && !standing && !late)
            return;
        if (!late && pawn.CurrentSpecialMove is RSpecialMoveInstance_FireGrapple { IsValid: true })
            return;

        if (standing || late)
            Log.Info("Batwing: the grapple didn't take (" + Fmt.Metres(away) + " away after " + Fmt.Whole(_grappleT) + " s), boarding anyway.");
        _grappleT = -1f;
        EndGrapple();
        Pilot.Board(_craft, pawn, at, yaw, hover: true);
    }

    // Batman can still be on the rope or hanging when he's taken aboard, which would leave him stuck in that move.
    public static void EndGrapple()
    {
        if (World.Pc is not { } pc)
            return;

        Safe.Run("batwing grapple end", () =>
        {
            foreach (FName state in LineStates)
            {
                if (!pc.IsInState(state, true))
                    continue;

                Log.Detail("Batwing: leaving the " + state + " state.");
                pc.GotoState(FallingState, NoLabel, false, false);
                return;
            }
        });
    }

    public override void Leave() => Wing.Voice.Fade();

    public override void Draw(Canvas canvas) => Hud.Inbound(this);
}
