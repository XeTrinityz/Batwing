using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    private const string CargoKey = "batwing.cargo";

    // A little under zero. The model bounds still run a touch above the car's roof, and the two never collide.
    private const float HangGap = -20f;
    private const float LatchSeconds = 1.4f;
    private const float LatchAbove = 1500f;
    private const float LatchFrom = 900f;
    private const float LatchCarSlow = 1500f;
    private const float HookAcross = 1500f;
    private const float HookBelow = 3500f;
    private const float HookSlow = 1200f;
    private const float CruiseClear = 4000f;
    private const float RouteStep = 5000f;
    private const float RouteWide = 2000f;
    private const float LowerWithin = 1200f;
    private const float SetDownSlack = 90f;
    private const float RoadRing = 3000f;
    private const float AlreadyThere = 5000f;
    private const float DeliverRing = 1000f;
    private const float DeliverRoad = 4000f;
    private const float AlreadyHere = 3000f;
    private const float CargoSpeed = 0.75f;
    private const float LowDrop = 250f;
    private const float DropHurtSpeed = 1500f;
    private const double DropWatch = 10.0;
    private const double CatchSeconds = 15.0;
    private const float SwoopFrom = 25000f;
    private const float SwoopSpeed = 4500f;
    private const float CatchLead = 0.5f;

    private enum Haul
    {
        None,
        Fetch,
        Latch,
        Climb,
        Cruise,
        Lower,
    }

    private Haul _haul;
    private bool _delivery;
    private bool _dismissAfter;
    private RVehicleBatmobileBase? _liftCar;
    private RVehicleBatmobileBase? _cargo;
    private float _hang = 300f;
    private float _cargoTop = 150f;
    private readonly List<Vector3> _badSpots = new();
    private RVehicleBatmobileBase? _settling;
    private double _settleAt;
    private float _settleZ;
    private float _rest = 100f;
    private Vector3 _cargoBox = new(160f, 160f, 110f);
    private Vector3 _goal, _goalRough;
    private string _goalWhat = "";
    private Vector3 _liftStart;
    private float _haulT;
    private Vector3 _latchFrom;
    private Rotator _latchRot;
    private float _lowerWait;
    private bool _lowerSaid;
    private bool _floorSeen;
    private float _cruiseZ;
    private Every _goalCheck, _floorCheck, _routeCheck;

    private RVehicleBatmobileBase? _falling;
    private double _fallSince;
    private float _fallFastest;


    private bool _drove;
    private double _catchUntil = -1.0;

    public bool Carrying => _cargo is { IsValid: true };

    private bool Hauling => _haul != Haul.None;

    private bool Catching => _catchUntil > Clock.Now;

    public string LiftLabel => _phase switch
    {
        Phase.Flying => Carrying ? "DROP BATMOBILE" : "HOOK BATMOBILE",
        Phase.Escort when Hauling => Carrying ? "SET DOWN HERE" : "CANCEL LIFT",
        _ => World.Pawn is RVehicleBatmobileBase ? "LIFT TO WAYPOINT" : "BRING BATMOBILE",
    };

    public string LiftNote => _phase == Phase.Escort && Hauling ? HaulState() : Carrying ? "CARRYING" : "";

    private string HaulState() => _haul switch
    {
        Haul.Fetch => _delivery ? "FETCHING" : "INBOUND",
        Haul.Latch => "HOOKING",
        Haul.Climb => "LIFTING",
        Haul.Cruise => "EN ROUTE " + Fmt.Tenths(FlatDistance(_fm.Position, _goal) / 100000f) + " KM",
        Haul.Lower => "SETTING DOWN",
        _ => "",
    };

    // Lift key and the deck action. What it does depends on where Batman is.
    public void LiftAction()
    {
        try
        {
            LiftPress();
        }
        catch (Exception ex)
        {
            Log.Info(Title + " lift refused: " + ex.Message);
            Readout.Notice(Title, ex.Message.ToUpperInvariant(), false);
        }
    }

    private void LiftPress()
    {
        if (!World.Ready)
            throw new InvalidOperationException("the game is still loading");
        if (World.Paused)
            throw new InvalidOperationException("not while paused");

        switch (_phase)
        {
            case Phase.Flying:
                HookOrDrop();
                return;
            case Phase.Arriving or Phase.Leaving:
                throw new InvalidOperationException("the Batwing is busy");
            case Phase.Escort when Hauling:
                if (ReAim())
                    return;
                if (Carrying)
                    SetDownHere();
                else
                    CancelHaul("LIFT CANCELLED");
                return;
        }

        switch (World.Pawn)
        {
            case RVehicleBatmobileBase car:
                StartRide(car);
                break;
            case RPawnPlayer:
                StartDelivery();
                break;
            default:
                throw new InvalidOperationException("the game is still loading");
        }
    }

    // In the Batmobile. The Batwing hooks it and flies it to the waypoint with Batman inside.
    private void StartRide(RVehicleBatmobileBase car)
    {
        if (World.Pc is not { } pc || Hooks.InCinematic(pc))
            throw new InvalidOperationException("not during a cutscene");
        if (!Ground.InCity(car.Location))
            throw new InvalidOperationException("only out in the city");

        (Vector3 at, string what) = Destination();
        if (FlatDistance(at, car.Location) < AlreadyThere)
            throw new InvalidOperationException("you're already there");

        if (_phase == Phase.Off)
            StartEscort(car);

        _goalRough = at;
        _goalWhat = what;
        _goal = SetDownSpot(at);
        BeginHaul(car, delivery: false);
        Toast("LIFT TO " + what);
        Log.Info(Title + ": lifting the Batmobile to the " + what.ToLowerInvariant() + " at " + Geo.Position(at) + ", setting down at " + Geo.Position(_goal) + ".");
    }

    // On foot. The Batwing fetches the Batmobile and puts it down next to Batman.
    private void StartDelivery()
    {
        RPawnPlayer pawn = Require();
        if (Car.Current() is not { IsValid: true, bDeleteMe: false } car)
            throw new InvalidOperationException("no Batmobile in the city");
        if (car.Driver is { IsValid: true })
            throw new InvalidOperationException("the Batmobile is busy");
        if (Vector3.Distance(car.Location, pawn.Location) < AlreadyHere)
            throw new InvalidOperationException("the Batmobile is already here");
        if (!Ground.InCity(car.Location))
            throw new InvalidOperationException("the Batmobile isn't out in the city");

        if (_phase == Phase.Off)
            StartEscort();

        _goalWhat = "YOU";
        _badSpots.Clear();
        _aimed = AimedFloor(pawn);
        _goal = DeliverySpot(pawn);
        BeginHaul(car, delivery: true);
        Toast("FETCHING THE BATMOBILE");
        Log.Info(Title + ": fetching the Batmobile from " + Geo.Position(car.Location) + ", " + Fmt.Whole(Vector3.Distance(car.Location, pawn.Location) / Metre) + " m from Batman.");
    }

    private static (Vector3 At, string What) Destination()
    {
        RPersistentData? pd = null;
        Safe.Run("batwing waypoint", () => pd = Game.GetPersistentData());
        if (pd is { IsValid: true } && Safe.Try("batwing waypoint on", pd.GetCustomWaypointEnabled))
            return (pd.GetCustomWaypoint(true), "WAYPOINT");

        if (World.Info is { } info && Objective(info) is { } objective)
            return (objective, "OBJECTIVE");

        throw new InvalidOperationException("set a waypoint on the map first");
    }

    private static Vector3? Objective(RGameInfo info)
    {
        var markers = info.ObjectiveMarkers;
        int picked = info.HighestPriorityNonCustomObjectiveMarker;
        if (picked >= 0 && picked < markers.Count && Usable(markers[picked]))
            return markers[picked].Location;

        RObjectiveMarker? best = null;
        foreach (RObjectiveMarker m in markers)
        {
            if (Usable(m) && (best == null || m.Priority > best.Priority))
                best = m;
        }

        return best?.Location;
    }

    private static bool Usable(RObjectiveMarker? m) => m is { IsValid: true, bDeleteMe: false, IsMarkerActive: true, IsCustomWaypoint: false };

    private void BeginHaul(RVehicleBatmobileBase car, bool delivery)
    {
        EndEscortCombat();
        _pickup = false;
        _catchUntil = -1.0;
        _liftCar = car;
        _delivery = delivery;
        _dismissAfter = false;
        _haul = Haul.Fetch;
        _haulT = 0f;
        _lowerWait = 0f;
        _lowerSaid = false;
        _cell = null;
        _climb = 0f;
        _stuckFor = 0f;
        _badSpots.Clear();
        _hang = CargoHang(car);
        _goalCheck.Force();
    }

    private void CancelHaul(string why)
    {
        _haul = Haul.None;
        _liftCar = null;
        _dismissAfter = false;
        Toast(why);
    }

    private void FinishHaul()
    {
        bool dismiss = _dismissAfter;
        _haul = Haul.None;
        _liftCar = null;
        _dismissAfter = false;
        if (dismiss)
            EndEscort("ESCORT DISMISSED");
    }

    private void SetDownHere()
    {
        if (_cargo is not { IsValid: true } car)
            return;

        _goal = new Vector3(car.Location.X, car.Location.Y, car.Location.Z - _rest);
        BeginLower();
    }

    private const float DownMost = 6f;

    private float _downFor, _downLastZ = float.MaxValue;
    private bool _lined;

    private void BeginLower()
    {
        _haul = Haul.Lower;
        _lined = false;
        _haulT = 0f;
        _downFor = 0f;
        _downLastZ = float.MaxValue;
        _lowerWait = 0f;
        _lowerSaid = false;
        _floorSeen = false;
        _floorCheck.Force();
        Toast("SETTING DOWN");
    }

    // Where the craft should be for this part of the job, or null when it isn't hauling.
    private Vector3? HaulSpot()
    {
        switch (_haul)
        {
            case Haul.Fetch when _liftCar is { IsValid: true } car:
                return car.Location + new Vector3(0f, 0f, _hang + LatchAbove);
            // Straight up to clear every roof on the way, across at that height, then straight down.
            case Haul.Climb:
                return new Vector3(_liftStart.X, _liftStart.Y, _cruiseZ);
            case Haul.Cruise:
                return new Vector3(_goal.X, _goal.Y, _cruiseZ);
            // Holds its height until real ground has loaded under it. Then it aims a little under the ground so
            // it keeps coming down, and the car's sweep stops it on the floor.
            // It lines up over the spot first and only then comes straight down the clear column. Once lined up
            // it keeps coming down unless it drifts well off, so a small nudge doesn't stop it every time.
            case Haul.Lower:
                float off = FlatDistance(_fm.Position, _goal);
                if (off < OverSpot)
                    _lined = true;
                else if (off > OverSpot * 3f)
                    _lined = false;
                return _floorSeen && _lined
                    ? _goal + new Vector3(0f, 0f, _hang + _rest - 200f)
                    : new Vector3(_goal.X, _goal.Y, _fm.Position.Z);
            default:
                return null;
        }
    }

    // Where it goes back to if it gets stuck while hauling.
    private Vector3 HaulHome() => _haul switch
    {
        Haul.Fetch when _liftCar is { IsValid: true } car => car.Location,
        Haul.Climb => _liftStart,
        _ => _goal,
    };

    // Runs before the autopilot each escort frame. False means it placed the craft itself this frame.
    private bool HaulBefore(float dt)
    {
        _haulT += dt;

        // He got into the Batmobile on a delivery, so it's delivered. Hooked, it goes straight down where it is.
        if (_delivery && World.Pawn is RVehicleBatmobileBase)
        {
            if (!Carrying)
            {
                CancelHaul("BATMOBILE DELIVERED");
                return true;
            }

            if (_haul != Haul.Lower)
            {
                Log.Info(Title + ": Batman got into the Batmobile while it was hooked, setting it down here.");
                SetDownHere();
            }
        }

        switch (_haul)
        {
            case Haul.Fetch:
                if (_liftCar is not { IsValid: true, bDeleteMe: false } car || (car.Driver is { IsValid: true } && _delivery))
                {
                    CancelHaul("LOST THE BATMOBILE");
                    return true;
                }

                // He got out before it was hooked, so there's nobody to take anywhere.
                if (!_delivery && World.Pawn is not RVehicleBatmobileBase)
                {
                    CancelHaul("LIFT CANCELLED");
                    return true;
                }

                Vector3 over = car.Location + new Vector3(0f, 0f, _hang + LatchAbove);
                if (FlatDistance(_fm.Position, over) < LatchFrom && MathF.Abs(_fm.Position.Z - over.Z) < 1200f && car.Velocity.Length() < LatchCarSlow)
                    StartLatch(car);
                return _haul != Haul.Latch || !LatchStep(dt);
            case Haul.Latch:
                return !LatchStep(dt);
            case Haul.Climb:
                RaiseCruise();
                if (_fm.Position.Z > _cruiseZ - 600f || _haulT > 25f)
                {
                    _haul = Haul.Cruise;
                    _haulT = 0f;
                }

                return true;
            case Haul.Cruise:
                RefreshGoal();
                RaiseCruise();
                if (FlatDistance(_fm.Position, _goal) < LowerWithin && _fm.Velocity.Length() < 1500f)
                    BeginLower();

                return true;
            default:
                return true;
        }
    }

    // After the craft moved. Sets the car down once it's on the ground.
    private void HaulAfter(float dt)
    {
        if (_haul != Haul.Lower || _cargo is not { IsValid: true } car)
            return;

        if (!_floorCheck.Elapsed(0.2))
            return;

        // Only real ground counts, not the low detail stand in a district shows before it's loaded, and not
        // while the district under it is still streaming in.
        // The floor is read at the drop off itself, not under the car, so passing over a bridge or a roof on the
        // way doesn't become the new target.
        // It looks down from just over the planned spot, not from the car, so a sign or a bridge high over the
        // spot isn't taken for it. Only if that finds nothing, like a map pin's made up height, from the car.
        Vector3 floor = default;
        var near = new Vector3(_goal.X, _goal.Y, MathF.Min(car.Location.Z, _goal.Z + 1500f));
        var high = new Vector3(_goal.X, _goal.Y, car.Location.Z);
        bool real = !StillLoading() && Safe.Try("batwing set down floor", () => Ground.Real(car, near, 60000f, out floor) || Ground.Real(car, high, 60000f, out floor));
        if (real)
        {
            if (!_floorSeen)
                Log.Info(Title + ": ground under the drop off at " + Geo.Position(floor) + ", coming down from " + Fmt.Whole((car.Location.Z - floor.Z) / Metre) + " m.");
            _floorSeen = true;
            _goal = new Vector3(_goal.X, _goal.Y, floor.Z);
            _lowerWait = 0f;

            Log.Throttled("batwing lowering", Title + ": lowering, car " + Fmt.Tenths((car.Location.Z - floor.Z - _rest) / Metre) + " m off the ground, "
                + Fmt.Tenths(FlatDistance(car.Location, _goal) / Metre) + " m off the spot, " + (_lined ? "coming down" : "lining up") + ".");

            // Still lining up over the spot.
            if (!_lined)
            {
                _downFor = 0f;
                _downLastZ = float.MaxValue;
                return;
            }

            if (car.Location.Z - floor.Z <= _rest + SetDownSlack)
            {
                DropCargo(Vector3.Zero, gently: true);
                Sound(_kit?.Land, car);
                Toast(_delivery ? "BATMOBILE DELIVERED" : "SET DOWN");
                Log.Info(Title + ": Batmobile set down at " + Geo.Position(car.Location) + ".");
                FinishHaul();
                return;
            }

            // Ground's there but the car can't get onto it, like something sticking up under it. Only counts
            // while it isn't coming down any more, a long way down is fine.
            float z = car.Location.Z;
            _downFor = z < _downLastZ - 20f ? 0f : _downFor + 0.2f;
            _downLastZ = z;
            if (_downFor > DownMost)
                GiveUpSpot();
            return;
        }

        // No floor under it yet. The ground may still be loading.
        _lowerWait += 0.2f;
        WaitLog(_cell, false);
        if (_lowerWait > 15f && !_lowerSaid)
        {
            _lowerSaid = true;
            Readout.Notice(Title, "NO GROUND HERE YET // PRESS " + Opt.LiftKey.Value + " SOMEWHERE ELSE", false);
        }
    }

    // Close to the drop off, the spot is checked again now the ground there has loaded.
    private void RefreshGoal()
    {
        if (!_goalCheck.Elapsed(1.0))
            return;

        if (_delivery)
        {
            if (World.Pawn is RPawnPlayer batman && !DeliveryStillGood(batman))
                _goal = DeliverySpot(batman);
            return;
        }

        if (FlatDistance(_fm.Position, _goalRough) < 40000f)
            _goal = SetDownSpot(_goalRough);
    }

    private void StartLatch(RVehicleBatmobileBase car)
    {
        _liftCar = car;
        _haul = Haul.Latch;
        _haulT = 0f;
        _hang = CargoHang(car);
        _latchFrom = _fm.Position;
        _latchRot = Geo.FromAxes(_fwd, _right, _up);
        Puff();
        Toast("HOOKING");
    }

    // Drops onto the car, turning to match it, and hooks it at the bottom. True while it's still going.
    private bool LatchStep(float dt)
    {
        if (_liftCar is not { IsValid: true, bDeleteMe: false } car)
        {
            _haul = Haul.None;
            return false;
        }

        if (dt <= 0f)
            return true;

        // The escort counts the time in HaulBefore.
        if (_phase == Phase.Flying)
            _haulT += dt;
        float k = Ease.Smooth(_haulT / LatchSeconds);

        Vector3 top = car.Location + new Vector3(0f, 0f, _hang);
        Vector3 at = Vector3.Lerp(_latchFrom, top, k);
        float yaw = _latchRot.Yaw + Geo.WrapDegrees(car.Rotation.Yaw - _latchRot.Yaw) * k;
        var turn = new Rotator(_latchRot.Pitch * (1f - k), yaw, _latchRot.Roll * (1f - k));
        Geo.Axes(turn, out Vector3 fwd, out Vector3 right, out Vector3 up);
        _fm.Place(at, yaw / Deg, Vector3.Zero);
        _fm.Forward = fwd;
        _fm.Right = right;
        _fm.Up = up;
        _fm.Hover = 1f;
        _fm.HoverWanted = true;
        Place(at, fwd, right, up);
        GroundBelow(at, out _ground, out _groundAt, out _groundNormal);
        UpdateEffects(1f, false, at, _ground, _groundAt, _groundNormal, 0.6f, Vector3.Zero, 0.5f, 1f, dt);

        if (k < 1f)
            return true;

        Hook(car);
        return false;
    }

    private void Hook(RVehicleBatmobileBase car)
    {
        _rest = RestHeight(car);
        if (car.Mesh is { IsValid: true } mesh)
        {
            Vector3 box = mesh.Bounds.BoxExtent;
            float side = Math.Clamp(MathF.Min(box.X, box.Y) * 0.8f, 80f, 220f);
            _cargoBox = new Vector3(side, side, Math.Clamp(box.Z * 0.8f, 60f, 160f));
        }

        // Its collision stays on. Turning it off and back on left the car frozen in the air after letting go,
        // so the craft's own traces skip the car instead.
        Safe.Run("batwing hook", () =>
        {
            car.SetPhysics(Actor.EPhysics.PHYS_None, false);
            car.Velocity = Vector3.Zero;
            car.AngularVelocity = Vector3.Zero;
        });

        _cargo = car;
        Shared.Set(CargoKey, true);
        _liftStart = car.Location;
        _cruiseZ = RouteTop(car.Location, _goal, MathF.Max(car.Location.Z, _goal.Z)) + CruiseClear;
        _routeCheck.Force();
        Sound(_kit?.Land, car);
        Log.Info(Title + ": hooked the Batmobile at " + Geo.Position(car.Location) + ", hanging " + Fmt.Whole(_hang) + " below, resting " + Fmt.Whole(_rest) + " off the ground.");

        if (_phase == Phase.Flying)
        {
            _haul = Haul.None;
            _liftCar = null;
            Toast("BATMOBILE HOOKED");
            return;
        }

        _haul = Haul.Climb;
        _haulT = 0f;
        Toast(_delivery ? "BATMOBILE HOOKED" : "LIFTING");
    }

    // Keeps the hooked car under the belly. Runs from Place, so every phase carries it.
    private void HoldCargo(Vector3 at, Vector3 fwd, Vector3 right, Vector3 up)
    {
        if (_cargo == null)
            return;

        if (_cargo is not { IsValid: true, bDeleteMe: false } car)
        {
            _cargo = null;
            Shared.Set(CargoKey, false);
            return;
        }

        Vector3 spot = at - up * _hang;
        Rotator rot = Geo.FromAxes(fwd, right, up);
        Safe.Run("batwing cargo", () =>
        {
            // Something like an eject can turn its physics back on.
            if (car.Physics != Actor.EPhysics.PHYS_None)
                car.SetPhysics(Actor.EPhysics.PHYS_None, false);
            car.SetLocation(spot);
            car.SetRotation(rot);
            car.Velocity = Vector3.Zero;
            if (car.Mesh is { IsValid: true } mesh)
            {
                mesh.SetRBPosition(spot);
                mesh.SetRBRotation(rot);
            }
        });
    }

    // Lets go of the car. Gently puts it on the ground under it, standing still. Otherwise it falls with the
    // speed it had.
    private void DropCargo(Vector3 velocity, bool gently)
    {
        RVehicleBatmobileBase? car = _cargo;
        _cargo = null;
        Shared.Set(CargoKey, false);
        if (car is not { IsValid: true, bDeleteMe: false })
            return;

        Vector3 at = car.Location;
        Rotator rot = car.Rotation;
        if (gently)
        {
            if (Floor(car, 60000f, out Vector3 floor))
                at = new Vector3(at.X, at.Y, floor.Z + _rest + 10f);
            rot = new Rotator(0f, rot.Yaw, 0f);
            velocity = Vector3.Zero;
        }

        Safe.Run("batwing cargo release", () =>
        {
            car.SetPhysics(Actor.EPhysics.PHYS_None, false);
            car.SetLocation(at);
            car.SetRotation(rot);
            car.Velocity = velocity;
            car.AngularVelocity = Vector3.Zero;
            if (car.Mesh is { IsValid: true } mesh)
            {
                mesh.SetRBPosition(at);
                mesh.SetRBRotation(rot);
                mesh.SetRBLinearVelocity(velocity);
                mesh.SetRBAngularVelocity(Vector3.Zero);
                mesh.WakeRigidBody();
            }

            car.SetPhysics(Actor.EPhysics.PHYS_RigidBody, false);
            if (gently)
                car.TeleportCleanup(at, rot);
            else if (car.Mesh is { IsValid: true } moving)
                moving.SetRBLinearVelocity(velocity);
        });

        if (!gently)
        {
            _falling = car;
            _fallSince = Clock.Now;
            _fallFastest = 0f;
        }

        _settling = car;
        _settleAt = Clock.Now + 1.5;
        _settleZ = at.Z;
        Log.Info(Title + ": let go of the Batmobile at " + Geo.Position(at) + (gently ? ", set down." : ", dropped at " + Fmt.Whole(velocity.Length() / Metre) + " m/s."));
    }

    // Pilot side. Hover over the parked Batmobile to hook it, press again to drop it.
    private void HookOrDrop()
    {
        if (_haul == Haul.Latch)
            return;

        if (_cargo is { IsValid: true } held)
        {
            bool low = Floor(held, _rest + LowDrop, out _);
            DropCargo(low ? Vector3.Zero : _fm.Velocity, low);
            Toast(low ? "BATMOBILE SET DOWN" : "BATMOBILE DROPPED");
            return;
        }

        RVehicleBatmobileBase car = Hookable(out string? why) ?? throw new InvalidOperationException(why ?? "no Batmobile in the city");
        StartLatch(car);
    }

    private RVehicleBatmobileBase? Hookable(out string? why)
    {
        why = null;
        if (Car.Current() is not { IsValid: true, bDeleteMe: false } car)
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
        float across = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (across > HookAcross || -d.Z > HookBelow || -d.Z < _half.Z)
        {
            why = "hover right over the Batmobile to hook it, it's " + Fmt.Whole(d.Length() / Metre) + " m away";
            return null;
        }

        if (!_fm.HoverWanted || _fm.Velocity.Length() > HookSlow)
        {
            why = "slow to a hover over it first";
            return null;
        }

        return car;
    }

    // The car's box swept along with the craft. Moves the craft back if the car hit something. True when the car
    // landed on something flat.
    private bool CargoCollide(Vector3 from, Vector3 to, bool hurts)
    {
        if (_cargo is not { IsValid: true } car || Vector3.DistanceSquared(from, to) < 0.01f)
            return false;

        Vector3 drop = _up * _hang;
        Actor? hit = car.Trace(out Vector3 at, out Vector3 n, to - drop, from - drop, Component.ECollisionFilter.COF_TraceWorld, _cargoBox, out _, 0);
        if (hit == null || at == Vector3.Zero || n.LengthSquared() < 0.5f)
            return false;

        _fm.Position = at + n * 4f + drop;
        float into = -Vector3.Dot(_fm.Velocity, n);
        if (into > 0f)
            _fm.Velocity += n * into * 1.1f;

        bool flat = n.Z > 0.7f;
        if (hurts && !flat && into > 400f)
            Hit(at, n, into);
        else if (_scrapeSound.Elapsed(0.25) && into > 200f)
            Sound(_kit?.Scrape);
        return flat;
    }

    // Box sweep for the hanging car, so climbing and avoiding count it too.
    private bool CargoSwept(Vector3 from, Vector3 to)
    {
        if (_cargo is not { IsValid: true } car)
            return false;

        Vector3 drop = new(0f, 0f, _hang);
        Actor? hit = car.Trace(out Vector3 at, out _, to - drop, from - drop, Component.ECollisionFilter.COF_TraceWorld, _cargoBox, out _, 0);
        return hit != null && at != Vector3.Zero;
    }

    // Craft centre to car origin when hooked. From the belly down to the car's roof plus a small gap.
    private float CargoHang(RVehicleBatmobileBase car)
    {
        float roof = CarRoof(car, out string how);
        float belly = BellyDepth();
        _cargoTop = roof;
        float hang = belly + roof + HangGap;
        Log.Info(Title + ": hang " + Fmt.Whole(hang) + ", belly " + Fmt.Whole(belly) + " under the Batwing's middle, car roof " + Fmt.Whole(roof) + " over its middle (" + how + ").");
        return hang;
    }

    // The top of the car model in the car's own frame, from the model's own bounds. The world bounds the
    // engine keeps are padded and left a big gap, and rays straight down don't hit the car at all.
    private static float CarRoof(RVehicleBatmobileBase car, out string how)
    {
        if (car.Mesh is { IsValid: true } mesh && mesh.SkeletalMesh is { IsValid: true } skin)
        {
            ref GameObject.FBoxSphereBounds b = ref skin.Bounds;
            Geo.Axes(mesh.Rotation, out Vector3 x, out Vector3 y, out Vector3 z);
            Vector3 scale = mesh.Scale3D * (mesh.Scale > 0f ? mesh.Scale : 1f);
            float top = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Vector3 c = (b.Origin + b.BoxExtent * sign) * scale;
                Vector3 p = x * c.X + y * c.Y + z * c.Z + mesh.Translation;
                top = MathF.Max(top, p.Z);
            }

            if (float.IsFinite(top))
            {
                how = "model";
                return Math.Clamp(top, 40f, 300f);
            }
        }

        how = "guess";
        return 120f;
    }

    // Measured once from the Batwing model when it's fitted.
    private float BellyDepth() => _belly;

    // How high the car's origin sits off the ground when it's parked.
    private static float RestHeight(RVehicleBatmobileBase car)
    {
        if (Floor(car, 1500f, out Vector3 floor))
        {
            float rest = car.Location.Z - floor.Z;
            if (rest is > 20f and < 400f)
                return rest;
        }

        return Math.Clamp(car.GetCollisionHeight(), 60f, 200f);
    }

    private static bool Floor(RVehicleBatmobileBase car, float reach, out Vector3 floor)
    {
        Vector3 at = car.Location;
        return Ground.Ray(car, at, at - new Vector3(0f, 0f, reach), out floor, out _);
    }

    private Actor? Tracer() => _craft is { IsValid: true } craft ? craft : World.Pawn;

    private bool OpenSky(Vector3 spot) =>
        Tracer() is not { } t || !Ground.Ray(t, spot + new Vector3(0f, 0f, 200f), spot + new Vector3(0f, 0f, 6000f), out _, out _);

    private const float FootHalf = 280f;
    private const float ColumnHeight = 4000f;
    private const float CarColumn = 20000f;
    private const float WingRoom = 0.7f;
    private const float OverSpot = 400f;
    private const float FootSlope = 80f;
    private const float BadNear = 1000f;

    // Why the last spot was turned down, counted per search for the log.
    private readonly Dictionary<string, int> _rejects = new();

    private bool Reject(string why)
    {
        _rejects[why] = _rejects.TryGetValue(why, out int n) ? n + 1 : 1;
        return false;
    }

    // Room for the whole car. A car sized box dropped down the column over the spot catches spires, railings
    // and anything else poking up. The four corners have to sit on floor at about the same height.
    private bool SpotFits(Vector3 spot)
    {
        if (Tracer() is not { } t)
            return true;

        foreach (Vector3 bad in _badSpots)
        {
            if (FlatDistance(bad, spot) < BadNear)
                return Reject("tried before");
        }

        var box = new Vector3(FootHalf, FootHalf, 100f);
        // All the way up, a sign or walkway 60 m over a street is as much in the way as one at 5 m.
        Actor? hit = t.Trace(out Vector3 at, out _, spot + new Vector3(0f, 0f, box.Z + 40f), spot + new Vector3(0f, 0f, CarColumn),
            Component.ECollisionFilter.COF_TraceWorld, box, out _, 0);
        if (hit != null && at != Vector3.Zero && !IsCargo(hit))
            return Reject("something over the car (" + hit.Class.Name + ")");

        // The Batwing has to get down there too, and it's far wider than the car. A gap between towers that
        // fits the car can still catch its wings. Only the air it comes down through counts, so a low wall or
        // an air vent at the edge of a roof doesn't rule the roof out.
        float wing = MathF.Max(_half.X, _half.Y) * WingRoom;
        Actor? wall = t.Trace(out Vector3 wallAt, out _, spot + new Vector3(0f, 0f, _hang + _rest + 200f), spot + new Vector3(0f, 0f, ColumnHeight),
            Component.ECollisionFilter.COF_TraceWorld, new Vector3(wing, wing, _half.Z), out _, 0);
        if (wall != null && wallAt != Vector3.Zero && !IsCargo(wall))
            return Reject("no room for the wings");

        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = spot + new Vector3(i < 2 ? FootHalf : -FootHalf, i % 2 == 0 ? FootHalf : -FootHalf, 0f);
            if (!Ground.Ray(t, corner + new Vector3(0f, 0f, 300f), corner - new Vector3(0f, 0f, 300f), out Vector3 floor, out Vector3 n)
                || n.Z < 0.8f || MathF.Abs(floor.Z - spot.Z) > FootSlope)
                return Reject("not flat");
        }

        return true;
    }

    private const float AimReach = 8000f;

    private Vector3? _aimed;

    // The floor where Batman's looking, if it's close and walkable. That's where he wants the car.
    private static Vector3? AimedFloor(RPawnPlayer batman)
    {
        if (Hooks.PlayerCam() is not { } cam)
            return null;

        Vector3 eye = cam.CameraCache.POV.Location;
        Vector3 look = Geo.Dir(cam.CameraCache.POV.Rotation.Yaw, cam.CameraCache.POV.Rotation.Pitch);
        if (!Ground.Ray(batman, eye, eye + look * AimReach, out Vector3 hit, out Vector3 n) || n.Z < 0.8f)
            return null;

        return hit;
    }

    // The spot itself, then rings round it out to 9 m.
    private Vector3? SpotNear(RPawnPlayer batman, Vector3 around)
    {
        foreach (float r in new[] { 0f, 300f, 600f, 900f })
        {
            int dirs = r == 0f ? 1 : 8;
            for (int i = 0; i < dirs; i++)
            {
                Vector3 probe = around + Geo.Facing(i * 45f) * r;
                if (!Ground.Ray(batman, probe + new Vector3(0f, 0f, 1500f), probe - new Vector3(0f, 0f, 1500f), out Vector3 hit, out Vector3 n))
                    continue;
                if (n.Z < 0.8f || MathF.Abs(hit.Z - around.Z) > 400f)
                    continue;
                if (FlatDistance(hit, batman.Location) < KeepClear)
                {
                    Reject("too close to Batman");
                    continue;
                }

                if (SpotFits(hit))
                    return hit;
            }
        }

        return null;
    }

    private bool IsCargo(Actor? a) => a != null && _cargo is { IsValid: true } car && a.Ptr == car.Ptr;

    // The tallest thing along the way, from the loaded ground under the line and two lines either side of it.
    // Starts from the given floor height, not the craft's, or flying high would keep asking for higher.
    private float RouteTop(Vector3 from, Vector3 to, float floor)
    {
        float top = floor;
        if (Tracer() is not { } t)
            return top;

        var flat = new Vector3(to.X - from.X, to.Y - from.Y, 0f);
        float length = flat.Length();
        Vector3 side = length > 1f ? new Vector3(-flat.Y, flat.X, 0f) / length * RouteWide : Vector3.Zero;
        int steps = Math.Clamp((int)(length / RouteStep), 1, 40);
        float sky = top + 60000f;
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = from + flat * (i / (float)steps);
            for (int s = -1; s <= 1; s++)
            {
                Vector3 q = p + side * s;
                Actor? what = t.Trace(out Vector3 hit, out _, new Vector3(q.X, q.Y, top - 1000f), new Vector3(q.X, q.Y, sky),
                    Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0);
                if (what != null && hit != Vector3.Zero && !IsCargo(what) && float.IsFinite(hit.Z))
                    top = MathF.Max(top, hit.Z);
            }
        }

        return top;
    }

    // More of the city loads in on the way, so the route is looked at again. It only ever goes up.
    private void RaiseCruise()
    {
        if (!_routeCheck.Elapsed(1.0))
            return;

        float want = RouteTop(_fm.Position, _goal, _goal.Z) + CruiseClear;
        if (want > _cruiseZ + 100f)
        {
            Log.Detail(Title + ": cruise height up " + Fmt.Whole((want - _cruiseZ) / Metre) + " m for what's ahead.");
            _cruiseZ = want;
        }
    }

    private const float KeepClear = 800f;

    // A road near the destination with open sky over it. Roads have a real height, a map pin doesn't. Nothing
    // within KeepClear of the avoid point counts, that's where Batman is standing.
    private Vector3 SetDownSpot(Vector3 near, Vector3? avoid = null)
    {
        bool Clear(Vector3 p) => avoid is not { } a || FlatDistance(p, a) > KeepClear;

        Vector3? best = null;
        float bestDist = float.MaxValue;
        for (int i = -1; i < 8; i++)
        {
            Vector3 probe = i < 0 ? near : near + Geo.Facing(i * 45f) * RoadRing;
            Vector3 road = Vector3.Zero;
            Safe.Run("batwing road", () => road = RVehicle.GetNearestRoadLocationStaticScript(probe, false));
            if (road == Vector3.Zero || FlatDistance(road, probe) > RoadSnap || !Clear(road) || !OpenSky(road) || !SpotFits(road))
                continue;

            float d = FlatDistance(road, near);
            if (d < bestDist)
            {
                bestDist = d;
                best = road;
            }
        }

        if (best is { } found)
            return found;

        // No road fits. Straight down at the spot, or off to one side of Batman.
        Vector3 at = near;
        if (avoid is { } away && !Clear(near))
        {
            var dir = new Vector3(near.X - away.X, near.Y - away.Y, 0f);
            dir = dir.LengthSquared() > 1f ? Vector3.Normalize(dir) : Geo.Facing(MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) * Deg);
            at = away + dir * DeliverRing * 1.5f;
        }

        if (Tracer() is { } t && Ground.Ray(t, at + new Vector3(0f, 0f, 30000f), at - new Vector3(0f, 0f, 30000f), out Vector3 floor, out _))
            return floor;
        return at;
    }

    // The delivery keeps the spot it has while it still fits and Batman hasn't wandered off from it, so the
    // target doesn't jump about every second.
    private bool DeliveryStillGood(RPawnPlayer batman)
    {
        float away = FlatDistance(_goal, batman.Location);
        return away > KeepClear && away < DeliverRoad * 3f && SpotFits(_goal);
    }

    // Next to Batman, never on him. A road close by on his level first, then open flat ground round him with
    // room for the whole car, then the nearest road even if it's down off the roof he's on.
    // Where he's looking first, if he aimed at a floor. Then his own level all round him, then a road on his
    // level, then any road near him, even down off the roof he's on.
    private Vector3 DeliverySpot(RPawnPlayer batman)
    {
        _rejects.Clear();
        Vector3 found = PickDelivery(batman, out string how);
        string why = string.Join(", ", _rejects.Select(r => r.Key + " " + r.Value));
        Log.Info(Title + ": delivery spot " + Geo.Position(found) + " (" + how + ", " + Fmt.Whole(FlatDistance(found, batman.Location) / Metre) + " m from Batman"
            + (why.Length > 0 ? ", turned down: " + why : "") + ").");
        return found;
    }

    private Vector3 PickDelivery(RPawnPlayer batman, out string how)
    {
        if (_aimed is { } aimed && SpotNear(batman, aimed) is { } there)
        {
            how = "where you aimed";
            return there;
        }

        Vector3 me = batman.Location;
        float feet = me.Z - Ground.Lift(batman) + 40f;
        float yaw = CameraYaw(batman);
        foreach (float ring in new[] { 900f, 1300f, 1800f, 2400f })
        {
            for (int i = 0; i < 12; i++)
            {
                float turn = (i + 1) / 2 * 30f * (i % 2 == 1 ? 1f : -1f);
                Vector3 probe = me + Geo.Facing(yaw + turn) * ring;
                if (Ground.Ray(batman, probe + new Vector3(0f, 0f, 1500f), probe - new Vector3(0f, 0f, 1500f), out Vector3 hit, out Vector3 n)
                    && n.Z > 0.8f && MathF.Abs(hit.Z - feet) < 600f && SpotFits(hit))
                {
                    how = "your level";
                    return hit;
                }
            }
        }

        Vector3 road = Vector3.Zero;
        Safe.Run("batwing road", () => road = RVehicle.GetNearestRoadLocationStaticScript(me, false));
        float roadAway = road == Vector3.Zero ? float.MaxValue : FlatDistance(road, me);
        if (road != Vector3.Zero && roadAway > KeepClear && roadAway < DeliverRoad * 3f && OpenSky(road) && SpotFits(road))
        {
            how = MathF.Abs(road.Z - feet) < 600f ? "road on your level" : "nearest road";
            return road;
        }

        how = "best road search";
        return SetDownSpot(me, me);
    }

    // Pressing the lift key again on a delivery moves the drop to where he's looking.
    private bool ReAim()
    {
        if (!_delivery || World.Pawn is not RPawnPlayer batman || AimedFloor(batman) is not { } aimed)
            return false;

        _aimed = aimed;
        _badSpots.Clear();
        _goal = DeliverySpot(batman);
        _goalCheck.Force();
        if (Carrying && _haul == Haul.Lower)
        {
            _cruiseZ = MathF.Max(_cruiseZ, _fm.Position.Z);
            _haul = Haul.Climb;
            _liftStart = _fm.Position;
            _haulT = 0f;
        }

        Toast("DROP SPOT MARKED");
        return true;
    }

    // It came down but never got the car onto the floor, so something's in the way. That spot is ruled out and
    // it climbs back up to find another.
    private void GiveUpSpot()
    {
        _badSpots.Add(_goal);
        Log.Info(Title + ": couldn't set the Batmobile down at " + Geo.Position(_goal) + ", trying somewhere else.");
        _goal = _delivery && World.Pawn is RPawnPlayer batman ? DeliverySpot(batman) : SetDownSpot(_goalRough);
        _cruiseZ = MathF.Max(_cruiseZ, _fm.Position.Z + 1500f);
        _haul = Haul.Climb;
        _liftStart = _fm.Position;
        _haulT = 0f;
        _goalCheck.Force();
        Toast("FINDING ANOTHER SPOT");
    }

    // Checks the car a moment after letting go. If it's still frozen, physics is forced back on.
    private void SettleStep()
    {
        if (_settling is not { IsValid: true, bDeleteMe: false } car)
        {
            _settling = null;
            return;
        }

        if (Clock.Now < _settleAt)
            return;

        _settling = null;
        Actor.EPhysics physics = car.Physics;
        Log.Info(Title + ": Batmobile after letting go, physics " + physics + ", moved " + Fmt.Whole(car.Location.Z - _settleZ) + " up and down, speed " + Fmt.Whole(car.Velocity.Length()) + ".");
        if (physics == Actor.EPhysics.PHYS_RigidBody)
            return;

        Log.Info(Title + ": the Batmobile was left frozen, turning its physics back on.");
        car.SetPhysics(Actor.EPhysics.PHYS_RigidBody, false);
        if (car.Mesh is { IsValid: true } mesh)
            mesh.WakeRigidBody();
    }

    // A hard landing from a drop wrecks vehicles and knocks down thugs where it comes down.
    private void FallStep()
    {
        if (_falling is not { IsValid: true, bDeleteMe: false } car)
        {
            _falling = null;
            return;
        }

        double t = Clock.Now - _fallSince;
        float down = -car.Velocity.Z;
        _fallFastest = MathF.Max(_fallFastest, down);
        bool near = t > 0.15 && Floor(car, _rest + 150f, out _);
        bool stopped = t > 0.4 && _fallFastest > 600f && down < _fallFastest * 0.3f;
        if (!near && !stopped)
        {
            if (t > DropWatch)
                _falling = null;
            return;
        }

        _falling = null;
        if (_fallFastest < DropHurtSpeed || !Opt.DropDamage.On)
            return;

        Safe.Run("batwing drop impact", () => Impact(car, _fallFastest));
    }

    private void Impact(RVehicleBatmobileBase car, float speed)
    {
        Vector3 at = car.Location;
        float radius = Math.Clamp(500f + speed * 0.4f, 800f, 2500f);
        float inner = radius * 0.4f;
        if (World.Pc is not { } pc || World.Player is not { IsValid: true } batman)
            return;

        int hits = 0;

        foreach (RVehicleNPC v in World.All<RVehicleNPC>())
        {
            if (!Foe(v))
                continue;
            float d = Vector3.Distance(v.Location, at);
            if (d > radius)
                continue;

            int damage = d < inner ? v.Health + 1 : Math.Max(1, (int)(v.Health * 0.5f));
            Vector3 push = Vector3.Normalize(v.Location - at + new Vector3(0f, 0f, 100f)) * 1500f;
            v.TakeDamageFromWeapon(damage, pc, v.Location, push, Vector3.Zero, RDmgType_BatmobileChainGun.StaticClass(), default, batman, false);
            hits++;
        }

        if (Car.Weapon<RBatmobileCannon>() is { IsValid: true } vulcan)
        {
            foreach (RPawnVillain thug in World.All<RPawnVillain>())
            {
                if (!CanStrike(thug) || Vector3.Distance(thug.Location, at) > radius)
                    continue;
                Vector3 away = thug.Location - at;
                away.Z = 0f;
                Knock(thug, away.LengthSquared() > 1f ? Vector3.Normalize(away) : Vector3.UnitX, batman, vulcan);
                hits++;
            }
        }

        Burst(_kit?.Impact, at, Vector3.UnitZ);
        Sound(_kit?.Hit, car);
        Log.Info(Title + ": Batmobile came down at " + Fmt.Whole(speed / Metre) + " m/s at " + Geo.Position(at) + ", hit " + hits + ".");
        if (hits > 0)
            Readout.Toast(Title, "BATMOBILE DROP // " + hits + " HIT", true);
    }

    private const double EjectWindow = 0.8;
    private const float EjectRise = 400f, EjectThrow = 900f;

    private double _ejectUntil = -1.0;
    private float _ejectFrom;

    // On escort, an eject from the Batmobile has the Batwing catch Batman in the air. Climbing out of the car
    // also leaves him off the ground for a moment, so it watches briefly after he gets out and only counts it
    // when he's really thrown up into the air.
    private void EjectCheck(Pawn lead)
    {
        bool driving = lead is RVehicleBatmobileBase;
        if (_drove && !driving && lead is RPawnPlayer left)
        {
            _ejectUntil = Clock.Now + EjectWindow;
            _ejectFrom = left.Location.Z;
        }

        _drove = driving;
        if (_ejectUntil < 0.0 || lead is not RPawnPlayer p)
            return;
        if (Clock.Now > _ejectUntil || p.Physics == Actor.EPhysics.PHYS_Walking)
        {
            _ejectUntil = -1.0;
            return;
        }

        if (p.Location.Z - _ejectFrom < EjectRise && p.Velocity.Z < EjectThrow)
            return;

        _ejectUntil = -1.0;
        if (!Opt.CatchEject.On || _pickup)
            return;

        EndEscortCombat();
        _catchUntil = Clock.Now + CatchSeconds;
        _swoopT = -1f;
        _stuckFor = 0f;
        // The lift stops, the car stays hooked. A missed catch sets it down after.
        _haul = Haul.None;
        _liftCar = null;
        _delivery = true;
        Toast("CATCHING YOU");
        Log.Info(Title + ": Batman ejected, catching him" + (Carrying ? " with the Batmobile still hooked." : "."));
    }

    private float _swoopT = -1f, _swoopFor;
    private Vector3 _swoopFrom, _swoopLast;
    private Rotator _swoopRot;

    // Once it's in range it stops flying the normal way and sweeps in under him on a set path, so the catch
    // always lands. Further out the autopilot brings it closer first. True while it's swooping.
    private bool Swoop(RPawnPlayer flier, float dt)
    {
        if (flier.Physics == Actor.EPhysics.PHYS_Walking)
        {
            _catchUntil = -1.0;
            _swoopT = -1f;
            Log.Info(Title + ": Batman landed before the catch.");
            return false;
        }

        Vector3 seat = flier.Location + new Vector3(0f, 0f, _half.Z + 80f);
        if (_swoopT < 0f)
        {
            float far = Vector3.Distance(_fm.Position, seat);
            if (far > SwoopFrom)
                return false;

            _swoopT = 0f;
            _swoopFor = Math.Clamp(far / SwoopSpeed, 0.8f, 2.5f);
            _swoopFrom = _swoopLast = _fm.Position;
            _swoopRot = Geo.FromAxes(_fwd, _right, _up);
            Puff();
            Log.Info(Title + ": swooping in to catch Batman, " + Fmt.Whole(far / Metre) + " m out.");
        }

        if (dt <= 0f)
            return true;

        _swoopT += dt;
        float k = Ease.Smooth(_swoopT / _swoopFor);
        Vector3 at = Vector3.Lerp(_swoopFrom, seat, k);

        // Turns to the way he's flying, or along its own path if he's barely moving.
        Vector3 v = flier.Velocity;
        Vector3 path = at - _swoopLast;
        Vector3 flat = new Vector3(v.X, v.Y, 0f).LengthSquared() > 90000f ? new Vector3(v.X, v.Y, 0f) : new Vector3(path.X, path.Y, 0f);
        float yawTo = flat.LengthSquared() > 1f ? MathF.Atan2(flat.Y, flat.X) * Deg : _swoopRot.Yaw;
        float yaw = _swoopRot.Yaw + Geo.WrapDegrees(yawTo - _swoopRot.Yaw) * k;
        Geo.Axes(new Rotator(_swoopRot.Pitch * (1f - k), yaw, _swoopRot.Roll * (1f - k)), out Vector3 fwd, out Vector3 right, out Vector3 up);
        Vector3 vel = (at - _swoopLast) / dt;
        _swoopLast = at;

        _fm.Place(at, yaw / Deg, vel);
        _fm.Forward = fwd;
        _fm.Right = right;
        _fm.Up = up;
        _fm.Hover = 1f;
        _fm.HoverWanted = true;
        Place(at, fwd, right, up);
        GroundBelow(at, out _ground, out _groundAt, out _groundNormal);
        UpdateEffects(1f, false, at, _ground, _groundAt, _groundNormal, 0.8f, vel, 0.7f, 1f, dt);

        if (k < 1f)
            return true;

        _swoopT = -1f;
        _catchUntil = -1.0;
        _haul = Haul.None;
        _liftCar = null;
        Log.Info(Title + ": caught Batman in the air" + (Carrying ? ", Batmobile still hooked." : "."));
        CatchBoard(flier, at, yaw, vel);
        return true;
    }

    private const float CatchWings = 1500f;

    // Boards him without killing the speed. The swoop ends moving with him, so that speed carries on. Fast
    // across the ground it comes out flying on its wings that way, slow it stays in hover and drifts to a stop.
    private void CatchBoard(RPawnPlayer flier, Vector3 at, float yawDegrees, Vector3 vel)
    {
        var flat = new Vector3(vel.X, vel.Y, 0f);
        if (flat.Length() > CatchWings)
        {
            float yaw = MathF.Atan2(flat.Y, flat.X);
            Vector3 dir = Vector3.Normalize(new Vector3(vel.X, vel.Y, vel.Z * 0.3f));
            _fm.Place(at, yaw, dir * MathF.Max(vel.Length(), _fm.StallSpeed * 1.5f));
            _fm.Hover = 0f;
            _fm.HoverWanted = false;
            _fm.Throttle = 0.85f;
        }
        else
        {
            _fm.Place(at, yawDegrees / Deg, vel);
            _fm.Hover = 1f;
            _fm.HoverWanted = true;
            _fm.Throttle = 0.7f;
        }

        TakeOff(flier);
    }

    private Vector3 CatchSpot(RPawnPlayer batman) =>
        batman.Location + batman.Velocity * CatchLead + new Vector3(0f, 0f, _half.Z + 150f);

    // True when it boarded him.
    private bool CatchCheck(RPawnPlayer batman)
    {
        if (batman.Physics == Actor.EPhysics.PHYS_Walking)
        {
            _catchUntil = -1.0;
            return false;
        }

        Vector3 belly = _fm.Position - new Vector3(0f, 0f, _half.Z + 80f);
        if (Vector3.Distance(batman.Location, belly) > CatchReach)
            return false;

        _catchUntil = -1.0;
        _haul = Haul.None;
        _liftCar = null;
        Log.Info(Title + ": caught Batman in the air" + (Carrying ? ", Batmobile still hooked." : "."));
        CatchBoard(batman, _fm.Position, MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) * Deg, _fm.Velocity);
        return true;
    }

    // A script reload mid carry leaves the car frozen in the air.
    private static void RecoverCargo()
    {
        if (!Shared.GetValue(CargoKey, false))
            return;

        Shared.Set(CargoKey, false);
        if (Car.Current() is not { IsValid: true } car || car.Physics != Actor.EPhysics.PHYS_None)
            return;

        Log.Info(Title + ": scripts reloaded mid carry, letting the Batmobile go.");
        car.SetPhysics(Actor.EPhysics.PHYS_RigidBody, false);
        if (car.Mesh is { IsValid: true } mesh)
            mesh.WakeRigidBody();
    }
}
