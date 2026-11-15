using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Escort
{
    private const float LatchAbove = 1500f;
    private const float LatchFrom = 900f;
    private const float LatchCarSlow = 1500f;
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
    private const float DownMost = 6f;
    private const float OverSpot = 400f;
    private const float FootHalf = 280f;
    private const float FootSlope = 80f;
    private const float ColumnHeight = 4000f;
    private const float CarColumn = 20000f;
    private const float WingRoom = 0.7f;
    private const float BadNear = 1000f;
    private const float KeepClear = 800f;
    private const float AimReach = 8000f;
    private const float GrappleFrom = 4000f;
    private const float CatchReach = 3000f;
    private const double PickupMost = 12.0;
    private const double CatchSeconds = 15.0;
    private const double EjectWindow = 0.8;
    private const float EjectRise = 400f, EjectThrow = 900f;
    private const float SwoopFrom = 25000f, SwoopSpeed = 4500f;
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
    private bool _delivery, _dismissAfter;
    private RVehicleBatmobileBase? _liftCar;
    private Latch? _latch;
    private readonly List<Vector3> _badSpots = new();
    private readonly Dictionary<string, int> _rejects = new();
    private Vector3 _goal, _goalRough, _liftStart;
    private Vector3? _aimed;
    private float _haulT, _cruiseZ, _lowerWait, _downFor, _downLastZ = float.MaxValue;
    private bool _lowerSaid, _floorSeen, _lined;
    private Every _goalCheck, _floorCheck, _routeCheck;

    private bool _pickup, _pickupHold;
    private Vector3 _pickupAt;
    private float _pickupYaw;
    private double _pickupSince;

    private double _catchUntil = -1.0, _ejectUntil = -1.0;
    private float _ejectFrom;
    private float _swoopT = -1f, _swoopFor;
    private Vector3 _swoopFrom, _swoopLast;
    private Rotator _swoopRot;

    public bool Hauling => _haul != Haul.None;

    public bool Delivery => _delivery;

    private bool Catching => _catchUntil > Clock.Now;

    public bool Picking => _pickup;

    public bool CatchingNow => Catching;

    // Where the Batmobile is going down, while that's worth showing.
    public Vector3? DropSpot => Hauling && (_delivery || _haul is not (Haul.Fetch or Haul.Latch)) ? _goal : null;

    public string HaulStatus => _haul switch
    {
        Haul.Fetch => _delivery ? "FETCHING" : "INBOUND",
        Haul.Latch => "HOOKING",
        Haul.Climb => "LIFTING",
        Haul.Cruise => "EN ROUTE " + Fmt.Range(Geo.FlatDistance(_fm.Position, _goal)),
        Haul.Lower => "SETTING DOWN",
        _ => "",
    };

    // In the Batmobile. The Batwing hooks it and flies it to the waypoint with Batman inside.
    public static void StartRide(RVehicleBatmobileBase car)
    {
        Require.InCar(car);
        (Vector3 at, string what) = Batmobile.Destination() ?? throw new Refusal("set a waypoint on the map first");
        if (Geo.FlatDistance(at, car.Location) < AlreadyThere)
            throw new Refusal("you're already there");

        Escort escort = Wing.Now as Escort ?? Start(car);
        escort._goalRough = at;
        escort._goal = escort.SetDownSpot(at);
        escort.BeginHaul(car, delivery: false);
        Wing.Toast("LIFT TO " + what);
        Log.Info("Lift: to the " + what.ToLowerInvariant() + " at " + Geo.Position(at) + ", setting down at " + Geo.Position(escort._goal) + ".");
    }

    // On foot. The Batwing fetches the Batmobile and puts it down where Batman is looking, or next to him.
    public static void StartDelivery()
    {
        RPawnPlayer pawn = Require.OnFoot();
        if (Batmobile.Current() is not { IsValid: true, bDeleteMe: false } car)
            throw new Refusal("no Batmobile in the city");
        if (car.Driver is { IsValid: true })
            throw new Refusal("the Batmobile is busy");
        if (Vector3.Distance(car.Location, pawn.Location) < AlreadyHere)
            throw new Refusal("the Batmobile is already here");
        if (!Ground.InCity(car.Location))
            throw new Refusal("the Batmobile isn't out in the city");

        Escort escort = Wing.Now as Escort ?? Start(null);
        escort._badSpots.Clear();
        escort._aimed = AimedFloor(pawn);
        escort._goal = escort.DeliverySpot(pawn);
        escort.BeginHaul(car, delivery: true);
        Wing.Toast("FETCHING THE BATMOBILE");
        Log.Info("Lift: fetching the Batmobile from " + Fmt.Metres(Vector3.Distance(car.Location, pawn.Location)) + " away.");
    }

    // The lift key while on escort.
    public void Lift()
    {
        if (!Hauling)
        {
            if (World.Driving is { } car)
                StartRide(car);
            else
                StartDelivery();
            return;
        }

        if (ReAim())
            return;
        if (Carrying)
            SetDownHere();
        else
            CancelHaul("LIFT CANCELLED");
    }

    private void BeginHaul(RVehicleBatmobileBase car, bool delivery)
    {
        EndCombat();
        _pickup = false;
        _catchUntil = -1.0;
        _liftCar = car;
        _delivery = delivery;
        _dismissAfter = false;
        _haul = Haul.Fetch;
        _haulT = 0f;
        _lowerWait = 0f;
        _lowerSaid = false;
        _climb = 0f;
        _stuckFor = 0f;
        _badSpots.Clear();
        _streamer.Reset();
        _goalCheck.Force();
    }

    private void CancelHaul(string why)
    {
        _haul = Haul.None;
        _liftCar = null;
        _latch = null;
        _dismissAfter = false;
        Wing.Toast(why);
    }

    private void FinishHaul()
    {
        bool dismiss = _dismissAfter;
        _haul = Haul.None;
        _liftCar = null;
        _dismissAfter = false;
        if (dismiss)
            End("ESCORT DISMISSED");
    }

    private void SetDownHere()
    {
        if (_craft.Cargo is not { Alive: true } cargo)
            return;

        _goal = cargo.Car.Location - new Vector3(0f, 0f, cargo.Rest);
        BeginLower();
    }

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
        Wing.Toast("SETTING DOWN");
    }

    private float Hang => _craft.Cargo?.Hang ?? (_liftCar is { IsValid: true } car ? Cargo.HangFor(_craft, car) : 300f);

    private float Rest => _craft.Cargo?.Rest ?? 100f;

    // Where the craft should be for this part of the job, or null when it isn't hauling.
    private Vector3? HaulSpot()
    {
        switch (_haul)
        {
            case Haul.Fetch when _liftCar is { IsValid: true } car:
                return car.Location + new Vector3(0f, 0f, Hang + LatchAbove);

            // Straight up to clear every roof on the way, across at that height, then straight down.
            case Haul.Climb:
                return new Vector3(_liftStart.X, _liftStart.Y, _cruiseZ);
            case Haul.Cruise:
                return new Vector3(_goal.X, _goal.Y, _cruiseZ);

            // Holds its height until real ground has loaded under it, lines up over the spot, then comes straight
            // down the clear column. It aims a little under the ground so it keeps coming, and the car's sweep
            // stops it on the floor.
            case Haul.Lower:
                float off = Geo.FlatDistance(_fm.Position, _goal);
                if (off < OverSpot)
                    _lined = true;
                else if (off > OverSpot * 3f)
                    _lined = false;
                return _floorSeen && _lined
                    ? _goal + new Vector3(0f, 0f, Hang + Rest - 200f)
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

    // Before the autopilot each escort frame. False means it moved the craft itself this frame.
    private bool HaulBefore(float dt)
    {
        _haulT += dt;

        // He got into the Batmobile on a delivery, so it's delivered. Hooked, it goes straight down where it is.
        if (_delivery && World.Driving != null)
        {
            if (!Carrying)
            {
                CancelHaul("BATMOBILE DELIVERED");
                return true;
            }

            if (_haul != Haul.Lower)
                SetDownHere();
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
                if (!_delivery && World.Driving == null)
                {
                    CancelHaul("LIFT CANCELLED");
                    return true;
                }

                Vector3 over = car.Location + new Vector3(0f, 0f, Hang + LatchAbove);
                if (Geo.FlatDistance(_fm.Position, over) < LatchFrom && MathF.Abs(_fm.Position.Z - over.Z) < 1200f && car.Velocity.Length() < LatchCarSlow)
                {
                    _latch = Latch.Start(_craft, car);
                    _haul = Haul.Latch;
                    Wing.Toast("HOOKING");
                    return LatchStep(dt);
                }

                return true;
            case Haul.Latch:
                return LatchStep(dt);
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
                if (Geo.FlatDistance(_fm.Position, _goal) < LowerWithin && _fm.Velocity.Length() < 1500f)
                    BeginLower();
                return true;
            default:
                return true;
        }
    }

    private bool LatchStep(float dt)
    {
        if (_latch == null)
        {
            _haul = Haul.None;
            return true;
        }

        if (_latch.Step(_craft, dt))
            return false;

        bool hooked = _latch.Hooked;
        _latch = null;
        if (!hooked)
        {
            CancelHaul("LOST THE BATMOBILE");
            return true;
        }

        _liftStart = _craft.Cargo!.Car.Location;
        _cruiseZ = RouteTop(_liftStart, _goal, MathF.Max(_liftStart.Z, _goal.Z)) + CruiseClear;
        _routeCheck.Force();
        _haul = Haul.Climb;
        _haulT = 0f;
        Wing.Toast(_delivery ? "BATMOBILE HOOKED" : "LIFTING");
        return false;
    }

    // After the craft moved. Puts the car down once it's on the ground.
    private void HaulAfter()
    {
        if (_haul != Haul.Lower || _craft.Cargo is not { Alive: true } cargo || !_floorCheck.Elapsed(0.2))
            return;

        // Only real ground counts, not the low detail stand in a district shows before it has loaded. It's read
        // at the drop off itself and from just over it, so a bridge or sign high over the spot isn't taken for
        // it. Only if that finds nothing, like a map pin's made up height, from the car.
        RVehicleBatmobileBase car = cargo.Car;
        Vector3 floor = default;
        var near = new Vector3(_goal.X, _goal.Y, MathF.Min(car.Location.Z, _goal.Z + 1500f));
        var high = new Vector3(_goal.X, _goal.Y, car.Location.Z);
        bool real = !Streamer.StillLoading(_goal) && Safe.Try("set down floor", () => Ground.Real(car, near, 60000f, out floor) || Ground.Real(car, high, 60000f, out floor));
        if (!real)
        {
            _lowerWait += 0.2f;
            _streamer.Waiting(_fm.Position, _craft.Below.Distance, false);
            if (_lowerWait > 15f && !_lowerSaid)
            {
                _lowerSaid = true;
                Toasts.Notice("NO GROUND HERE YET // PRESS " + Prompt.Of(Act.Lift).Text + " SOMEWHERE ELSE", false);
            }

            return;
        }

        if (!_floorSeen)
            Log.Detail("Lift: ground under the drop off at " + Geo.Position(floor) + ", coming down from " + Fmt.Metres(car.Location.Z - floor.Z) + ".");
        _floorSeen = true;
        _goal = new Vector3(_goal.X, _goal.Y, floor.Z);
        _lowerWait = 0f;
        if (!_lined)
        {
            _downFor = 0f;
            _downLastZ = float.MaxValue;
            return;
        }

        if (car.Location.Z - floor.Z <= cargo.Rest + SetDownSlack)
        {
            cargo.Drop(Vector3.Zero, gently: true);
            _craft.Cargo = null;
            Craft.Play(_craft.Kit.Land, car);
            Rumble.Play(Buzz.Land);
            Wing.Toast(_delivery ? "BATMOBILE DELIVERED" : "SET DOWN");
            FinishHaul();
            return;
        }

        // The ground's there but the car can't get onto it, something sticking up under it. Only counts while
        // it isn't coming down any more.
        float z = car.Location.Z;
        _downFor = z < _downLastZ - 20f ? 0f : _downFor + 0.2f;
        _downLastZ = z;
        if (_downFor > DownMost)
            GiveUpSpot();
    }

    // Close to the drop off, the spot is looked at again now the ground there has loaded.
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

        if (Geo.FlatDistance(_fm.Position, _goalRough) < 40000f)
            _goal = SetDownSpot(_goalRough);
    }

    // It came down but never got the car onto the floor. That spot is ruled out and it climbs to find another.
    private void GiveUpSpot()
    {
        _badSpots.Add(_goal);
        Log.Info("Lift: couldn't set the Batmobile down at " + Geo.Position(_goal) + ", trying somewhere else.");
        _goal = _delivery && World.Pawn is RPawnPlayer batman ? DeliverySpot(batman) : SetDownSpot(_goalRough);
        ClimbAgain();
        Wing.Toast("FINDING ANOTHER SPOT");
    }

    private void ClimbAgain()
    {
        _cruiseZ = MathF.Max(_cruiseZ, _fm.Position.Z + 1500f);
        _haul = Haul.Climb;
        _liftStart = _fm.Position;
        _haulT = 0f;
        _goalCheck.Force();
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
            ClimbAgain();
        Wing.Toast("DROP SPOT MARKED");
        return true;
    }

    // The tallest thing along the way, from the loaded ground under the line and two lines either side of it.
    // Starts from the given floor height, not the craft's, or flying high would keep asking for higher.
    private float RouteTop(Vector3 from, Vector3 to, float floor)
    {
        float top = floor;
        Actor tracer = _craft.Actor;
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
                Actor? what = tracer.Trace(out Vector3 hit, out _, new Vector3(q.X, q.Y, top - 1000f), new Vector3(q.X, q.Y, sky),
                    Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0);
                if (what != null && hit != Vector3.Zero && !_craft.IsCargo(what) && float.IsFinite(hit.Z))
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
            _cruiseZ = want;
    }

    private bool OpenSky(Vector3 spot) => Ground.SkyAbove(_craft.Actor, spot + new Vector3(0f, 0f, 100f), 5800f);

    private bool Reject(string why)
    {
        _rejects[why] = _rejects.TryGetValue(why, out int n) ? n + 1 : 1;
        return false;
    }

    // Room for the whole car, and for the Batwing to come down over it. The four corners have to sit on floor at
    // about the same height.
    private bool SpotFits(Vector3 spot)
    {
        Actor t = _craft.Actor;
        foreach (Vector3 bad in _badSpots)
        {
            if (Geo.FlatDistance(bad, spot) < BadNear)
                return Reject("tried before");
        }

        // All the way up, a sign or walkway 60 m over a street is as much in the way as one at 5 m.
        var box = new Vector3(FootHalf, FootHalf, 100f);
        Actor? hit = t.Trace(out Vector3 at, out _, spot + new Vector3(0f, 0f, box.Z + 40f), spot + new Vector3(0f, 0f, CarColumn),
            Component.ECollisionFilter.COF_TraceWorld, box, out _, 0);
        if (hit != null && at != Vector3.Zero && !_craft.IsCargo(hit))
            return Reject("something over the car");

        // The Batwing is far wider than the car. Only the air it comes down through counts, so a low wall at the
        // edge of a roof doesn't rule the roof out.
        float wing = _craft.Span * WingRoom;
        Actor? wall = t.Trace(out Vector3 wallAt, out _, spot + new Vector3(0f, 0f, Hang + Rest + 200f), spot + new Vector3(0f, 0f, ColumnHeight),
            Component.ECollisionFilter.COF_TraceWorld, new Vector3(wing, wing, _craft.Half.Z), out _, 0);
        if (wall != null && wallAt != Vector3.Zero && !_craft.IsCargo(wall))
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

    // The floor where Batman is looking, if it's close and walkable. That's where he wants the car.
    private static Vector3? AimedFloor(RPawnPlayer batman)
    {
        if (CameraHook.GameView() is not { } pov)
            return null;

        Vector3 eye = pov.Location;
        Vector3 look = Geo.Dir(pov.Rotation.Yaw, pov.Rotation.Pitch);
        return Ground.Ray(batman, eye, eye + look * AimReach, out Vector3 hit, out Vector3 n) && n.Z >= 0.8f ? hit : null;
    }

    // A road near the destination with open sky over it. Roads have a real height, a map pin doesn't.
    private Vector3 SetDownSpot(Vector3 near, Vector3? avoid = null)
    {
        bool Clear(Vector3 p) => avoid is not { } a || Geo.FlatDistance(p, a) > KeepClear;

        Vector3? best = null;
        float bestDist = float.MaxValue;
        for (int i = -1; i < 8; i++)
        {
            Vector3 probe = i < 0 ? near : near + Geo.Facing(i * 45f) * RoadRing;
            Vector3 road = Safe.Get("road", () => RVehicle.GetNearestRoadLocationStaticScript(probe, false), Vector3.Zero);
            if (road == Vector3.Zero || Geo.FlatDistance(road, probe) > RoadSnap || !Clear(road) || !OpenSky(road) || !SpotFits(road))
                continue;

            float d = Geo.FlatDistance(road, near);
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
            dir = dir.LengthSquared() > 1f ? Vector3.Normalize(dir) : Geo.Flat(_fm.Forward);
            at = away + dir * DeliverRing * 1.5f;
        }

        return Ground.Ray(_craft.Actor, at + new Vector3(0f, 0f, 30000f), at - new Vector3(0f, 0f, 30000f), out Vector3 floor, out _) ? floor : at;
    }

    // A delivery keeps its spot while it still fits and Batman hasn't wandered off, so it doesn't jump about.
    private bool DeliveryStillGood(RPawnPlayer batman)
    {
        float away = Geo.FlatDistance(_goal, batman.Location);
        return away > KeepClear && away < DeliverRoad * 3f && SpotFits(_goal);
    }

    // Where he's looking first, if he aimed at a floor. Then his own level all round him, then a road on his
    // level, then any road near him, even down off the roof he's on. Never on top of him.
    private Vector3 DeliverySpot(RPawnPlayer batman)
    {
        _rejects.Clear();
        Vector3 found = PickDelivery(batman, out string how);
        string why = string.Join(", ", _rejects.Select(r => r.Key + " " + r.Value));
        Log.Detail("Lift: delivery spot " + Geo.Position(found) + " (" + how + ", " + Fmt.Metres(Geo.FlatDistance(found, batman.Location)) + " from Batman"
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
        float yaw = CameraHook.ViewYaw(batman);
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

        Vector3 road = Safe.Get("road", () => RVehicle.GetNearestRoadLocationStaticScript(me, false), Vector3.Zero);
        float roadAway = road == Vector3.Zero ? float.MaxValue : Geo.FlatDistance(road, me);
        if (road != Vector3.Zero && roadAway > KeepClear && roadAway < DeliverRoad * 3f && OpenSky(road) && SpotFits(road))
        {
            how = MathF.Abs(road.Z - feet) < 600f ? "road on your level" : "nearest road";
            return road;
        }

        how = "best road search";
        return SetDownSpot(me, me);
    }

    // The aimed spot, then rings round it out to 9 m.
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
                if (Geo.FlatDistance(hit, batman.Location) < KeepClear)
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

    // The call key on escort. In grapple reach with a clear line it stops where it is and he grapples up.
    // Otherwise it comes over him first. Too far to catch him in the air, a normal call takes over.
    public void Board()
    {
        if (_pickup)
            return;
        if (Carrying)
            throw new Refusal("set the Batmobile down first");
        if (World.Pawn is not RPawnPlayer pawn)
            throw new Refusal("get out of the Batmobile first");

        if (Hauling)
        {
            _haul = Haul.None;
            _liftCar = null;
            _latch = null;
        }

        _catchUntil = -1.0;
        bool airborne = pawn.Physics != Actor.EPhysics.PHYS_Walking;
        Vector3 belly = _fm.Position - new Vector3(0f, 0f, _craft.Half.Z + 80f);
        float reach = Vector3.Distance(pawn.Location, belly);
        if (airborne)
        {
            if (reach > CatchReach)
            {
                Wing.Go(null);
                Inbound.Call();
                return;
            }

            EndCombat();
            Pilot.Board(_craft, pawn, _fm.Position, Geo.YawOf(_fm.Forward), hover: true);
            return;
        }

        bool clear = pawn.FastTrace(belly, pawn.Location + new Vector3(0f, 0f, 60f), Vector3.Zero, false);
        _pickupHold = Opt.Grapple.On && reach < GrappleFrom && clear;
        if (!_pickupHold)
            Require.Sky(pawn);

        EndCombat();
        _pickup = true;
        _pickupSince = Clock.Now;
        _pickupAt = _fm.Position;
        _pickupYaw = CameraHook.ViewYaw(pawn) / Geo.Deg;
        Wing.Toast(_pickupHold ? "GRAPPLE IN" : "COMING TO YOU");
    }

    private Vector3 PickupSpot(Vector3 centre) => _pickupHold ? _pickupAt : centre + new Vector3(0f, 0f, Inbound.HoverAbove);

    // Once it's settled and facing the way he's looking, the arrival takes over at its very end, which fires the
    // grapple at the belly and boards him.
    private void PickupCheck(RPawnPlayer batman, Vector3 centre)
    {
        double waited = Clock.Now - _pickupSince;
        if (waited > PickupMost || batman.Physics is not (Actor.EPhysics.PHYS_Walking or Actor.EPhysics.PHYS_Falling))
        {
            _pickup = false;
            Wing.Toast("PICKUP CANCELLED");
            return;
        }

        float yawErr = MathF.Abs(MathF.IEEERemainder(_pickupYaw - _fm.Yaw(), MathF.Tau));
        bool there = Vector3.Distance(_fm.Position, PickupSpot(centre)) < 600f;
        bool settled = _fm.Velocity.Length() < 500f && yawErr < 0.2f;
        if (!there || !(settled || waited > 3.0))
            return;

        _pickup = false;
        Log.Detail("Escort: pickup at " + Fmt.Metres(Vector3.Distance(batman.Location, _fm.Position)) + " from Batman.");
        Inbound.Handover(_craft, _pickupYaw * Geo.Deg);
    }

    // Ejecting from the Batmobile, the Batwing catches Batman in the air. Climbing out of the car leaves him off
    // the ground for a moment too, so it only counts when he's really thrown up.
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

        EndCombat();
        _catchUntil = Clock.Now + CatchSeconds;
        _swoopT = -1f;
        _stuckFor = 0f;

        // The lift stops, the car stays hooked. A missed catch sets it down after.
        _haul = Haul.None;
        _liftCar = null;
        _latch = null;
        _delivery = true;
        Wing.Toast("CATCHING YOU");
        Log.Info("Escort: Batman ejected, catching him" + (Carrying ? " with the Batmobile still hooked." : "."));
    }

    private Vector3 CatchSpot(RPawnPlayer batman) =>
        batman.Location + batman.Velocity * CatchLead + new Vector3(0f, 0f, _craft.Half.Z + 150f);

    // In range it stops flying the normal way and sweeps in under him on a set path, so the catch always lands.
    // Further out the autopilot brings it closer first. True while it's swooping.
    private bool Swoop(RPawnPlayer flier, float dt)
    {
        if (flier.Physics == Actor.EPhysics.PHYS_Walking)
        {
            _catchUntil = -1.0;
            _swoopT = -1f;
            return false;
        }

        Vector3 seat = flier.Location + new Vector3(0f, 0f, _craft.Half.Z + 80f);
        if (_swoopT < 0f)
        {
            float far = Vector3.Distance(_fm.Position, seat);
            if (far > SwoopFrom)
                return false;

            _swoopT = 0f;
            _swoopFor = Math.Clamp(far / SwoopSpeed, 0.8f, 2.5f);
            _swoopFrom = _swoopLast = _fm.Position;
            _swoopRot = Geo.FromAxes(_craft.Fwd, _craft.Right, _craft.Up);
            _craft.Puff();
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
        float yawTo = flat.LengthSquared() > 1f ? Geo.YawOf(flat) : _swoopRot.Yaw;
        float yaw = _swoopRot.Yaw + Geo.WrapDegrees(yawTo - _swoopRot.Yaw) * k;
        Geo.Axes(new Rotator(_swoopRot.Pitch * (1f - k), yaw, _swoopRot.Roll * (1f - k)), out Vector3 fwd, out Vector3 right, out Vector3 up);
        Vector3 vel = (at - _swoopLast) / dt;
        _swoopLast = at;

        _fm.Place(at, yaw / Geo.Deg, vel);
        _fm.Orient(fwd, right, up);
        _fm.Hover = 1f;
        _fm.HoverWanted = true;
        _craft.Place(at, fwd, right, up);
        _craft.Measure();
        _craft.Show(new Look { Hover = 1f, Jets = 0.8f, Velocity = vel, Throttle = 0.7f, G = 1f }, dt);
        if (k < 1f)
            return true;

        _swoopT = -1f;
        _catchUntil = -1.0;
        Log.Info("Escort: caught Batman in the air" + (Carrying ? ", Batmobile still hooked." : "."));
        Pilot.CatchBoard(_craft, flier, at, yaw, vel);
        return true;
    }

    // Close enough under him while he falls is a catch too.
    private void CatchCheck(RPawnPlayer batman)
    {
        if (batman.Physics == Actor.EPhysics.PHYS_Walking)
        {
            _catchUntil = -1.0;
            return;
        }

        Vector3 belly = _fm.Position - new Vector3(0f, 0f, _craft.Half.Z + 80f);
        if (Vector3.Distance(batman.Location, belly) > CatchReach)
            return;

        _catchUntil = -1.0;
        Log.Info("Escort: caught Batman in the air" + (Carrying ? ", Batmobile still hooked." : "."));
        Pilot.CatchBoard(_craft, batman, _fm.Position, Geo.YawOf(_fm.Forward), _fm.Velocity);
    }
}
