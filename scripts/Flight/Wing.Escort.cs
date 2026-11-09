using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    private const float StationRadius = 4000f;
    private const float StationHeight = 3000f;
    private const float StationSpin = 0.15f;
    private const float SpotStep = 0.52f;
    private const float SpotRoom = 600f;
    private const float RoadRoom = 200f;
    private const float RoadSnap = 4000f;
    private const float RoadHeight = 2500f;
    private const float AvoidRange = 2000f;
    private const float AvoidPush = 2500f;
    private const float StationGain = 1.2f;
    private const float Standoff = 3500f;
    private const float StandoffHeight = 2000f;
    private const float WingsFrom = 15000f;
    private const float HoverWithin = 8000f;
    private const float CruiseSpeed = 3800f;
    private const float BoostFrom = 40000f;
    private const float ClimbMost = 20000f;
    private const float EngageRange = 15000f;
    private const float LostFrom = 40000f;
    private const float StuckSeconds = 4f;
    private const float GunFront = 0.9f;
    private const float StrikeCone = 0.985f;
    private const float StrikeReach = 12000f;
    private const double StrikeSeconds = 12.0;
    private const double ApcPatience = 30.0;
    private const int MarksMost = 6;
    private const float ChaseReach = 25000f;
    private const float ChaseLeash = 30000f;
    private const float ChaseLead = 0.8f;
    private const float ChaseMoving = 800f;
    private const float HullSlack = 600f;
    private const double CannonEvery = 2.5;
    private const double CannonEveryFlying = 1.5;
    private const double BarrageEvery = 8.0;
    private const double BarrageEveryFlying = 4.0;
    private const double LockHold = 1.4;

    private float _climb;
    private float _stationAngle;
    private float _lastGap, _stuckFor;
    private bool _targetSeen;
    private bool _escortBlocked;
    private float _attackAngle;
    private nint _spotFor;
    private Vector3 _avoid;
    private Vector3? _roadSpot;
    private Every _spotCheck, _feel;

    private const float GrappleFrom = 4000f;
    private const float CatchReach = 3000f;
    private const double PickupMost = 12.0;

    private bool _pickup, _pickupHold;
    private Vector3 _pickupAt;
    private float _pickupYaw;
    private double _pickupSince;

    // Call key on escort. In grapple reach with a clear line it stops where it is and he grapples up. Otherwise
    // it comes over him first. Returns false to fall back to a normal call.
    private bool EscortPickup(RPawnPlayer pawn, bool airborne)
    {
        if (_pickup)
            return true;
        if (Carrying)
            throw new InvalidOperationException("set the Batmobile down first");
        if (Hauling)
        {
            _haul = Haul.None;
            _liftCar = null;
        }

        _catchUntil = -1.0;
        Vector3 belly = _fm.Position - new Vector3(0f, 0f, _half.Z + 80f);
        float reach = Vector3.Distance(pawn.Location, belly);
        if (airborne)
        {
            if (reach > CatchReach)
                return false;

            EndEscortCombat();
            Board(pawn, _fm.Position, MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) * Deg);
            return true;
        }

        bool clear = pawn.FastTrace(belly, pawn.Location + new Vector3(0f, 0f, 60f), Vector3.Zero, false);
        _pickupHold = Opt.Grapple.On && reach < GrappleFrom && clear;
        if (!_pickupHold)
        {
            Vector3 feet = pawn.Location;
            if (Ground.Ray(pawn, feet + new Vector3(0f, 0f, 100f), feet + new Vector3(0f, 0f, SkyCheck), out _, out _))
                throw new InvalidOperationException("it needs open sky above you");
        }

        EndEscortCombat();
        _pickup = true;
        _pickupSince = Clock.Now;
        _pickupAt = _fm.Position;
        _pickupYaw = CameraYaw(pawn) / Deg;
        Toast(_pickupHold ? "GRAPPLE IN" : "COMING TO YOU");
        return true;
    }

    private void EndEscortCombat()
    {
        WeaponsOff();
        _escortFoe = null;
        _strike = _strikeAim = null;
        _queue.Clear();
        _marks.Clear();
        _marking = false;
    }

    private Vector3 PickupSpot(Vector3 centre) =>
        _pickupHold ? _pickupAt : centre + new Vector3(0f, 0f, HoverAbove);

    // Once it's settled and facing the way he's looking, hand over to the arrival code at its very end, which
    // fires the grapple at the belly and boards him.
    private void PickupCheck(RPawnPlayer batman, Vector3 centre)
    {
        double waited = Clock.Now - _pickupSince;
        if (waited > PickupMost || batman.Physics is not (Actor.EPhysics.PHYS_Walking or Actor.EPhysics.PHYS_Falling))
        {
            _pickup = false;
            Toast("PICKUP CANCELLED");
            return;
        }

        float yawErr = MathF.Abs(MathF.IEEERemainder(_pickupYaw - MathF.Atan2(_fm.Forward.Y, _fm.Forward.X), MathF.Tau));
        bool there = Vector3.Distance(_fm.Position, PickupSpot(centre)) < 600f;
        bool settled = _fm.Velocity.Length() < 500f && yawErr < 0.2f;
        if (!there || !(settled || waited > 3.0))
            return;

        _pickup = false;
        _phase = Phase.Arriving;
        _arriveT = ArriveSeconds;
        _from = _arriveAt = _arriveLast = _fm.Position;
        _arriveVel = Vector3.Zero;
        _arriveYaw = _pickupYaw * Deg;
        _arriveBank = 0f;
        _grappleT = -1f;
        _handT = 0f;
        _handFrom = Geo.FromAxes(_fwd, _right, _up);
        _handVel = _fm.Velocity;
        Log.Info(Title + ": escort pickup at " + Geo.Position(_fm.Position) + ", " + Fmt.Whole(Vector3.Distance(batman.Location, _fm.Position) / Metre) + " m from Batman.");
    }

    private Actor? _escortFoe;
    private int _foesNear;
    private Actor? _strike;
    private Actor? _strikeAim;
    private readonly List<Actor> _marks = new();
    private readonly List<Actor> _queue = new();
    private bool _marking;
    private double _strikeUntil;
    private double _nextCannon, _nextBarrage, _lockUntil;
    private Every _escortPick, _escortAhead, _strikeLook;

    public bool Escorting => _phase == Phase.Escort;

    public string EscortLabel => Escorting ? "DISMISS ESCORT" : "ESCORT";

    public void EscortToggle()
    {
        if (Escorting)
        {
            // Never drops the Batmobile from the sky. It sets it down first and leaves after.
            if (Carrying)
            {
                _dismissAfter = true;
                SetDownHere();
                return;
            }

            Safe.Run("batwing escort end", () => EndEscort("ESCORT DISMISSED"));
            return;
        }

        if (_phase != Phase.Off)
        {
            Readout.Notice(Title, _phase == Phase.Flying ? "ALREADY FLYING" : "THE BATWING IS BUSY", false);
            return;
        }

        try
        {
            if (World.Pawn is RVehicleBatmobileBase car)
                StartEscort(RequireCar(car));
            else
                StartEscort();
        }
        catch (Exception ex)
        {
            Log.Info(Title + " escort refused: " + ex.Message);
            Readout.Notice(Title, ex.Message.ToUpperInvariant(), false);
        }
    }

    // The checks Require does for Batman on foot, for when he's driving.
    private static RVehicleBatmobileBase RequireCar(RVehicleBatmobileBase car)
    {
        if (!World.Ready)
            throw new InvalidOperationException("the game is still loading");
        if (World.Paused)
            throw new InvalidOperationException("not while paused");
        if (World.Pc is not { } pc || Hooks.InCinematic(pc))
            throw new InvalidOperationException("not during a cutscene");
        if (!Ground.InCity(car.Location))
            throw new InvalidOperationException("only out in the city");
        return car;
    }

    // Brought in for a lift or from the driver's seat, it comes to the Batmobile instead of Batman on foot.
    private void StartEscort(Pawn? lead = null)
    {
        Pawn pawn = lead ?? Require();
        Kit kit = LoadKit();

        Vector3 over = pawn.Location + new Vector3(0f, 0f, StationHeight + 2000f);
        float camYaw = CameraYaw(pawn);
        Vector3? start = null;
        foreach (float turn in new[] { 180f, 135f, -135f, 90f, -90f, 0f })
        {
            Vector3 s = over + Geo.Facing(camYaw + turn) * 16000f + new Vector3(0f, 0f, 2500f);
            if (!Ground.Ray(pawn, s, over, out _, out _))
            {
                start = s;
                break;
            }
        }

        Vector3 from = start ?? pawn.Location + new Vector3(0f, 0f, 12000f);
        Vector3 toward = Vector3.Normalize(over - from);
        float yaw = MathF.Atan2(toward.Y, toward.X) * Deg;
        SpawnCraft(kit, from, yaw);
        _fm.Place(from, yaw / Deg, toward * CruiseSpeed);
        _fm.Hover = 0f;
        _fm.HoverWanted = false;
        _fm.Throttle = 0.8f;
        _fm.Fuel = 1f;
        _fm.TopSpeed = Opt.TopSpeed.Number * Metre;
        Place(_fm.Position, _fm.Forward, _fm.Right, _fm.Up);

        StartEngine();
        BeginEscort(pawn.Location);
        Sound(kit.Called, pawn);
        Toast("ESCORT INBOUND");
        Log.Info(Title + ": escort from " + Geo.Position(from) + ".");
    }

    private void BeginEscort(Vector3 batman)
    {
        ResetWeapons();
        _climb = 0f;
        _escortBlocked = false;
        _avoid = Vector3.Zero;
        _spotFor = 0;
        _roadSpot = null;
        _pickup = false;
        _stuckFor = 0f;
        _lastGap = float.MaxValue;
        _stationAngle = MathF.Atan2(_fm.Position.Y - batman.Y, _fm.Position.X - batman.X);
        _escortFoe = null;
        _strike = _strikeAim = null;
        _queue.Clear();
        _marks.Clear();
        _marking = false;
        _foesNear = 0;
        _nextCannon = _nextBarrage = _lockUntil = 0.0;
        _escortPick.Force();
        _haul = Haul.None;
        _liftCar = null;
        _catchUntil = -1.0;
        _swoopT = -1f;
        _drove = World.Pawn is RVehicleBatmobileBase;
        _leadLostAt = _cutsceneAt = -1.0;
        _phase = Phase.Escort;
    }

    // Bailing out with escort on bail. The Batwing stays and covers him instead of flying off.
    private bool EscortAfterBail(Vector3 batman)
    {
        if (!Opt.EscortOnBail.On || !Ground.InCity(batman) || _craft is not { IsValid: true })
            return false;

        BeginEscort(batman);
        Log.Info(Title + ": bailed out, Batwing staying on escort.");
        return true;
    }

    private void EndEscort(string why)
    {
        if (!Escorting)
            return;

        EndEscortCombat();
        _pickup = false;
        _haul = Haul.None;
        _liftCar = null;
        _catchUntil = -1.0;
        if (Carrying)
            DropCargo(Vector3.Zero, gently: true);
        Log.Info(Title + ": escort ended, " + why.ToLowerInvariant() + ".");
        Depart(0f);
        Toast(why);
    }

    // Strike key. Hits whatever is nearest the middle of the screen, never a thug picked on its own. A thug in a
    // vehicle means the vehicle. A vehicle gets chased until it's destroyed.
    // Holding it marks everything the crosshair passes over, and the strikes go in the order they were marked.
    public void Strike()
    {
        if (!Escorting || _marking)
            return;
        if (Hauling)
        {
            Readout.Notice(Title, "BUSY WITH THE BATMOBILE", false);
            return;
        }

        _marking = true;
        _marks.Clear();
        Mark();
    }

    private void Mark()
    {
        if (_strikeAim is not { IsValid: true } aim || !Strikeable(aim) || _marks.Count >= MarksMost || _marks.Exists(m => m.Ptr == aim.Ptr))
            return;

        _marks.Add(aim);
        Sound(Car.Weapon<RBatmobileMissileLauncher>()?.LockOnSound);
    }

    // Runs every escort frame. Keeps marking while the key is down, and hands the marks over when it comes up.
    private void Marking()
    {
        if (!_marking)
            return;

        if (Keyboard.IsDown(Opt.StrikeKey.Key))
        {
            Mark();
            return;
        }

        _marking = false;
        _marks.RemoveAll(m => !m.IsValid || !Strikeable(m));
        if (_marks.Count == 0)
        {
            Toast("NO TARGET");
            Safe.Run("batwing strike miss", NoTargetLog);
            return;
        }

        _queue.Clear();
        _queue.AddRange(_marks.Skip(1));
        Actor first = _marks[0];
        int count = _marks.Count;
        _marks.Clear();
        Toast(count > 1 ? "STRIKE // " + count + " TARGETS" : first is RPawnVillain ? "STRIKE" : Turret(first) ? "STRIKE ON TURRET" : HeavyTank(first) ? "STRIKE ON HEAVY TANK" : Apc(first) ? "STRIKE ON APC" : "STRIKE ON VEHICLE");
        if (count > 1)
            Log.Info(Title + ": strike ordered on " + count + " targets.");
        BeginStrike(first);
    }

    private void BeginStrike(Actor aim)
    {
        _strike = aim;
        _strikeUntil = Clock.Now + StrikeSeconds;
        _targetSeen = false;
        Log.Info(Title + ": strike on " + aim.Name + (Apc(aim) ? " (APC)" : "") + " at " + Geo.Position(aim.Location) + ".");
    }

    // One strike is over. Moves on to the next marked target that's still worth hitting.
    private void EndStrike(string why)
    {
        _strike = null;
        while (_queue.Count > 0)
        {
            Actor next = _queue[0];
            _queue.RemoveAt(0);
            if (!next.IsValid || !Strikeable(next))
                continue;

            Toast(why + " // NEXT TARGET");
            BeginStrike(next);
            return;
        }

        Toast(why);
    }

    private Actor? StrikeCandidate(Vector3 batman)
    {
        if (Hooks.PlayerCam() is not { } cam || World.Player is not { } me)
            return null;

        Vector3 eye = cam.CameraCache.POV.Location;
        Vector3 look = Geo.Dir(cam.CameraCache.POV.Rotation.Yaw, cam.CameraCache.POV.Rotation.Pitch);
        Actor? best = null;
        float bestDot = StrikeCone;
        foreach (RPawnVillain thug in World.All<RPawnVillain>())
        {
            // Only thugs you can actually see. The game parks some out of sight, under the streets or in
            // interiors, with their tick off.
            if (!CanStrike(thug) || thug.bTickIsDisabled || Vector3.DistanceSquared(thug.Location, batman) > StrikeReach * StrikeReach
                || !Ahead(eye, look, thug.Location, bestDot, out float dot))
                continue;

            if (Ride(thug) is { } ride)
            {
                if (Foe(ride) && InView(me, eye, thug.Location, HullSlack))
                {
                    bestDot = dot;
                    best = ride;
                }
            }
            else if (InView(me, eye, thug.Location, 0f))
            {
                bestDot = dot;
                best = thug;
            }
        }

        foreach (Actor foe in _foes)
        {
            if (Foe(foe) && Vector3.DistanceSquared(foe.Location, batman) < ChaseReach * ChaseReach
                && Ahead(eye, look, foe.Location, bestDot, out float dot) && InView(me, eye, foe.Location, HullSlack))
            {
                bestDot = dot;
                best = foe;
            }
        }

        return best;
    }

    // Says what the enemy and the vehicle nearest the middle of the screen look like to the strike, so a miss
    // can be traced.
    private void NoTargetLog()
    {
        if (Hooks.PlayerCam() is not { } cam)
            return;

        Vector3 eye = cam.CameraCache.POV.Location;
        Vector3 look = Geo.Dir(cam.CameraCache.POV.Rotation.Yaw, cam.CameraCache.POV.Rotation.Pitch);

        RPawnVillain? enemy = null;
        float enemyDot = 0.9f;
        foreach (RPawnVillain t in World.All<RPawnVillain>())
        {
            if (t is { IsValid: true, bDeleteMe: false } && Ahead(eye, look, t.Location, enemyDot, out float dot))
            {
                enemyDot = dot;
                enemy = t;
            }
        }

        if (enemy != null && World.Player is { } me)
        {
            Log.Info(Title + ": strike skipped " + enemy.Name + " (" + enemy.Class.Name + ", " + enemy.CombatPawnType + "), health " + enemy.Health
                + ", alive " + Alive(enemy) + ", hidden " + enemy.bHidden + ", tick off " + enemy.bTickIsDisabled + ", in view " + InView(me, eye, enemy.Location, 0f)
                + ", " + Fmt.Whole(Vector3.Distance(eye, enemy.Location) / Metre) + " m.");
        }

        RVehicleNPC? best = null;
        float bestDot = 0.9f;
        foreach (RVehicleNPC v in World.All<RVehicleNPC>())
        {
            if (v is { IsValid: true, bDeleteMe: false } && Ahead(eye, look, v.Location, bestDot, out float dot))
            {
                bestDot = dot;
                best = v;
            }
        }

        if (best == null)
        {
            Log.Info(Title + ": strike found no target and no vehicle near the middle of the screen.");
            return;
        }

        var aboard = new List<string>();
        InlineArray<RVehicleNPC.FVehiclePassenger> seats = best.Passengers;
        for (int i = 0; i < seats.Count; i++)
        {
            if (seats[i].Pawn is { IsValid: true } p)
                aboard.Add(p.Name + (p is RPawnVillain t ? (Alive(t) ? "" : " (down)") : " (not a thug)"));
        }

        Log.Info(Title + ": strike found no target. Nearest vehicle " + best.Name + " (" + best.Class.Name + "), "
            + Fmt.Whole(Vector3.Distance(eye, best.Location) / Metre) + " m, " + Fmt.Whole(MathF.Acos(Math.Clamp(bestDot, -1f, 1f)) * Deg) + " deg off centre, "
            + "health " + best.Health + ", civilian " + best.CivilianVehicle + ", hidden " + best.bHidden + ", hacked " + best.DroneHacked
            + ", behaviour " + (best.CurrentBehaviour is { IsValid: true } b ? b.Class.Name : "none") + ", foe " + Foe(best)
            + ", aboard " + (aboard.Count > 0 ? string.Join(", ", aboard) : "nobody") + ".");
    }

    private static bool Ahead(Vector3 eye, Vector3 look, Vector3 at, float least, out float dot)
    {
        Vector3 to = at - eye;
        float dist = to.Length();
        dot = dist < 1f ? -1f : Vector3.Dot(to / dist, look);
        return dot > least;
    }

    // A ray at a vehicle stops on its own hull, so anything that close counts as seeing it.
    private static bool InView(Actor me, Vector3 eye, Vector3 at, float slack) =>
        !Ground.Ray(me, eye, at, out Vector3 hit, out _) || (slack > 0f && Vector3.Distance(eye, hit) > Vector3.Distance(eye, at) - slack);

    // Only something that drives off is chased. A turret stays put, so it gets a firing spot like a thug.
    private bool Chasing(Actor? target) => target != null && _strike != null && target.Ptr == _strike.Ptr && target is not RPawnVillain && !Turret(target);

    // Where to watch a target from. A chased vehicle is led a little so the Batwing keeps up.
    private Vector3 Watched(Actor target) => Chasing(target) ? target.Location + target.Velocity * ChaseLead : target.Location;

    private void EscortStep(PlayerController pc, float dt)
    {
        if (_craft is not { IsValid: true } craft)
        {
            DropCraft();
            _phase = Phase.Off;
            return;
        }

        if (dt <= 0f)
            return;

        // The Batmobile when he's driving, Batman otherwise. Getting in or out of the car can leave the
        // controller without a pawn or Batman for a moment, so only a lasting gap ends it.
        Pawn? lead = World.Pawn;
        RPawnPlayer? batman = World.Player ?? (lead as RVehicleBatmobileBase)?.Driver as RPawnPlayer;
        if (batman is { IsValid: true } && batman.Health <= 0)
        {
            EndEscort("ESCORT ENDED");
            return;
        }

        if (lead is not { IsValid: true } || batman is not { IsValid: true })
        {
            double now = Clock.Now;
            if (_leadLostAt < 0.0)
            {
                _leadLostAt = now;
                Log.Info(Title + ": escort lost track of Batman (pawn " + (lead?.Class.Name.ToString() ?? "none") + ", Batman " + (batman != null ? "yes" : "no") + "), holding.");
            }

            if (now - _leadLostAt > LostGrace)
                EndEscort("ESCORT ENDED");
            return;
        }

        if (_leadLostAt >= 0.0)
        {
            Log.Info(Title + ": escort found Batman again after " + Fmt.Tenths((float)(Clock.Now - _leadLostAt)) + " s, in " + lead.Class.Name + ".");
            _leadLostAt = -1.0;
        }

        if (CutsceneCheck(pc))
            return;

        EjectCheck(lead);
        // Still holding the car with no job, like after a missed catch, so put it down.
        if (Carrying && !Hauling && !Catching)
            SetDownHere();

        Vector3 centre = lead.Location;
        bool busy = Hauling || Catching;
        if (!busy)
        {
            if (_escortPick.Elapsed(0.5))
                Safe.Run("batwing escort targets", () => PickFoe(craft, centre));
            if (_strikeLook.Elapsed(0.1))
            {
                _strikeAim = lead is RPawnPlayer ? StrikeCandidate(centre) : null;
                if (_strikeAim != null)
                    WatchtowerLog(_strikeAim);
            }

            Marking();
            StrikeCheck(centre);
        }

        if (Hauling && !HaulBefore(dt))
            return;
        if (Catching && lead is RPawnPlayer flier && Swoop(flier, dt))
            return;

        FlightInput input = Autopilot(craft, centre, batman, dt);
        _fm.TopSpeed = Opt.TopSpeed.Number * Metre * (Carrying ? CargoSpeed : 1f);
        Vector3 from = _fm.Position;
        _fm.Step(dt, input);
        EscortCollide(craft, from);

        Place(_fm.Position, _fm.Forward, _fm.Right, _fm.Up);
        GroundBelow(_fm.Position, out _ground, out _groundAt, out _groundNormal);
        UpdateEffects(_fm.Hover, _fm.Boosting, _fm.Position, _ground, _groundAt, _groundNormal, _fm.Jets, _fm.Velocity, _fm.Throttle, _fm.GLoad, dt);

        // Batman rides in the car, so the city has to load ahead of the Batwing like it does when he flies it.
        if (Carrying && !_delivery)
        {
            KeepLoading();
            if (_districts.Elapsed(0.3))
                Lead();
        }

        if (Hauling)
        {
            HaulAfter(dt);
            return;
        }

        if (Catching)
        {
            if (lead is RPawnPlayer flying)
                CatchCheck(flying);
            return;
        }

        if (_pickup)
        {
            PickupCheck(batman, centre);
            return;
        }

        Orders orders = EscortOrders();
        Safe.Run("batwing escort weapons", () => WeaponsStep(batman, orders, dt));
    }

    private const double LostGrace = 2.0;
    private const double CutsceneGrace = 1.5;

    private double _leadLostAt = -1.0, _cutsceneAt = -1.0;

    // The cinematic flag can blink on during moves like getting into the Batmobile. Only a real cutscene that
    // keeps it on sends the escort away. True when it ended.
    private bool CutsceneCheck(PlayerController pc)
    {
        double now = Clock.Now;
        if (!Hooks.InCinematic(pc))
        {
            if (_cutsceneAt >= 0.0)
                Log.Info(Title + ": cinematic flag went off after " + Fmt.Tenths((float)(now - _cutsceneAt)) + " s, escort carries on.");
            _cutsceneAt = -1.0;
            return false;
        }

        if (_cutsceneAt < 0.0)
        {
            _cutsceneAt = now;
            string mode = pc is RPlayerControllerBase { ActiveCinematicMode: { IsValid: true } act } ? act.Class.Name.ToString() ?? "?" : "none";
            Log.Info(Title + ": cinematic flag on during escort (bCinematicMode " + pc.bCinematicMode + ", sequence " + mode + ", pawn " + (World.Pawn?.Class.Name.ToString() ?? "none") + ").");
        }

        if (now - _cutsceneAt < CutsceneGrace)
            return false;

        EndEscort("LEFT FOR A CUTSCENE");
        return true;
    }

    private void StrikeCheck(Vector3 centre)
    {
        if (_strike == null)
            return;

        // A thug who got into a vehicle takes the vehicle down with him.
        if (_strike is RPawnVillain { IsValid: true } thug && Alive(thug) && Ride(thug) is { } ride && Foe(ride))
        {
            Log.Info(Title + ": strike target " + thug.Name + " got into " + ride.Name + ", going after the vehicle.");
            _strike = ride;
        }

        if (!_strike.IsValid || !Strikeable(_strike))
        {
            bool down = !_strike.IsValid || _strike.bDeleteMe || _strike is Pawn { Health: <= 0 } || _strike is RPawnVillain || TurretDown(_strike);
            Log.Info(Title + ": strike target " + (down ? "down." : "lost."));
            EndStrike(down ? "TARGET DOWN" : "TARGET LOST");
            return;
        }

        if (_strike is not RPawnVillain)
        {
            if (FlatDistance(_strike.Location, centre) > ChaseLeash)
            {
                Log.Info(Title + ": strike target " + _strike.Name + " got away at " + Geo.Position(_strike.Location) + ".");
                EndStrike("TARGET GOT AWAY");
                return;
            }

            // A vehicle is chased as long as it stays in sight. An APC gets longer to come back into view.
            if (_targetSeen)
                _strikeUntil = Math.Max(_strikeUntil, Clock.Now + (Apc(_strike) || HeavyTank(_strike) ? ApcPatience : StrikeSeconds));
        }

        if (Clock.Now > _strikeUntil)
        {
            Log.Info(Title + ": strike on " + _strike.Name + " at " + Geo.Position(_strike.Location) + " ran out. Batwing at " + Geo.Position(_fm.Position)
                + ", road spot " + (_roadSpot is { } r ? Geo.Position(r) : "none") + ", target " + (_targetSeen ? "in sight" : "not in sight") + ".");
            EndStrike("NO CLEAR SHOT");
        }
    }

    // Picks from the weapons' own foe scan, which runs every half second.
    private void PickFoe(RBatwing craft, Vector3 centre)
    {
        Actor? best = null;
        float bestDist = EngageRange * EngageRange;
        int near = 0;
        // Cars with thugs in them and APCs wait for a strike order.
        foreach (Actor foe in _foes)
        {
            if (!Foe(foe) || OrderOnly(foe))
                continue;
            float d = Vector3.DistanceSquared(foe.Location, centre);
            if (d > EngageRange * EngageRange)
                continue;
            near++;
            if (d < bestDist && craft.FastTrace(foe.Location, _fm.Position, Vector3.Zero, false))
            {
                bestDist = d;
                best = foe;
            }
        }

        _foesNear = near;
        if (best != null && (_escortFoe == null || best.Ptr != _escortFoe.Ptr))
            Log.Detail(Title + ": escort engaging " + best.Name + ".");
        _escortFoe = best;
    }

    private Actor? EscortTarget()
    {
        if (_strike is { IsValid: true } s && Strikeable(s))
            return s;
        return _escortFoe is { IsValid: true } f && Foe(f) ? f : null;
    }

    // Where it wants to be. Hovering off Batman's shoulder, drifting slowly round him. With a target it
    // takes a firing spot off the target instead, kept within reach of Batman.
    private Vector3 Station(RBatwing craft, Vector3 centre, Actor? target, float dt)
    {
        // A missing target compared as never equal, so with nothing to shoot it searched for a spot every frame.
        if ((target?.Ptr ?? 0) != _spotFor)
        {
            _spotFor = target?.Ptr ?? 0;
            _roadSpot = null;
            _raise = 0f;
            if (target != null)
                _attackAngle = MathF.Atan2(_fm.Position.Y - target.Location.Y, _fm.Position.X - target.Location.X);
            _spotCheck.Force();
        }

        if (target == null)
            _stationAngle = (_stationAngle + StationSpin * dt) % MathF.Tau;

        if (_spotCheck.Elapsed(0.5))
        {
            if (target != null)
            {
                // A road spot is fixed, so it's no use for something driving away. A turret up on a tower is
                // better from a spot level with it out to the side than from over the street below it.
                Vector3? found = null;
                if (target is not RHelicopterBase && !Chasing(target) && !Turret(target))
                    Safe.Run("batwing road spot", () => found = RoadSpot(craft, centre, target));
                _roadSpot = found;
                if (found == null)
                    _attackAngle = ClearAngle(craft, centre, target, _attackAngle);
            }
            else
            {
                _stationAngle = ClearAngle(craft, centre, null, _stationAngle);
            }
        }

        if (target != null && _roadSpot is { } road)
            return road + new Vector3(0f, 0f, _climb);
        return SpotFor(centre, target, target != null ? _attackAngle : _stationAngle);
    }

    // Roads are open sky, so a spot over a road near the target is the best place to shoot from. Tries the
    // road under the target and the roads round it, keeps the ones with a clear line down, and takes the nearest.
    private Vector3? RoadSpot(RBatwing craft, Vector3 centre, Actor target)
    {
        Vector3 t = target.Location;
        Vector3? best = null;
        float bestDist = float.MaxValue;
        for (int i = -1; i < 8; i++)
        {
            Vector3 probe = i < 0 ? t : t + Geo.Facing(i * 45f) * Standoff;
            Vector3 road = RVehicle.GetNearestRoadLocationStaticScript(probe, false);
            if (road == Vector3.Zero || FlatDistance(road, probe) > RoadSnap)
                continue;

            Vector3 spot = road + new Vector3(0f, 0f, RoadHeight);
            if (FlatDistance(spot, centre) > EngageRange + Standoff || !SpotClear(craft, spot, t, RoadRoom, Turret(target) ? TurretSlack : 0f))
                continue;

            float d = Vector3.DistanceSquared(spot, _fm.Position);
            if (d < bestDist)
            {
                bestDist = d;
                best = spot;
            }
        }

        return best;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X, y = a.Y - b.Y;
        return MathF.Sqrt(x * x + y * y);
    }

    private Vector3 SpotFor(Vector3 centre, Actor? target, float angle) => SpotFor(centre, target, angle, _raise);

    private Vector3 SpotFor(Vector3 centre, Actor? target, float angle, float raise)
    {
        var dir = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
        if (target == null)
            return new Vector3(centre.X + dir.X * StationRadius, centre.Y + dir.Y * StationRadius, centre.Z + StationHeight + raise + _climb);

        Vector3 t = Watched(target);
        var spot = new Vector3(t.X + dir.X * Standoff, t.Y + dir.Y * Standoff, t.Z + StandoffHeight + raise + _climb);
        var pull = new Vector3(spot.X - centre.X, spot.Y - centre.Y, 0f);
        float reach = pull.Length();
        if (reach > EngageRange)
            spot = new Vector3(centre.X + pull.X / reach * EngageRange, centre.Y + pull.Y / reach * EngageRange, spot.Z);
        return spot;
    }

    // Extra height tried when nothing at the normal height is clear, lowest first.
    private static readonly float[] Raises = { 0f, 1500f, 3500f, 6000f };

    private float _raise;

    // Tries the current angle first, then 30 degrees either side, further and further round, for a spot with
    // room for the wings and a clear line to whoever it's watching. Higher spots get fewer angles to keep the
    // traces down. Under a roof or deep between buildings there may be no line to him at all, so after that
    // any spot with room for the wings will do, as low as it can. If nothing is clear it keeps moving round.
    private float ClearAngle(RBatwing craft, Vector3 centre, Actor? target, float angle)
    {
        Vector3 watch = target?.Location ?? centre + new Vector3(0f, 0f, 300f);
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (float raise in Raises)
            {
                int tries = raise == 0f ? 12 : 6;
                for (int i = 0; i <= tries; i++)
                {
                    float step = (i + 1) / 2 * SpotStep * (i % 2 == 1 ? 1f : -1f);
                    float a = angle + step;
                    if (!SpotClear(craft, SpotFor(centre, target, a, raise), pass == 0 ? watch : null, SpotRoom, target != null && Turret(target) ? TurretSlack : 0f))
                        continue;

                    if (raise != _raise)
                        Log.Detail(Title + ": escort spot " + (raise > 0f ? Fmt.Whole(raise / Metre) + " m higher" : "back to normal height") + (pass == 1 ? ", out of sight of the target." : "."));
                    _raise = raise;
                    return a;
                }
            }
        }

        _raise = Raises[^1];
        return angle + SpotStep;
    }

    // A clear line to whoever it watches, nothing through the spot itself, and room round it for the wings.
    // The box catches thin beams and girders that slip between the rays.
    // A turret sits on or in the tower it's mounted on, so a line that only stops right by it still counts.
    private const float TurretSlack = 500f;

    private bool SpotClear(RBatwing craft, Vector3 spot, Vector3? watch, float spare, float slack = 0f)
    {
        if (watch is { } w && Ground.Ray(craft, spot, w, out Vector3 block, out _) && Vector3.Distance(block, w) > slack)
            return false;
        if (Swept(craft, spot + new Vector3(0f, 0f, 300f), spot - new Vector3(0f, 0f, 300f)))
            return false;

        float room = MathF.Max(_half.X, _half.Y) + spare;
        for (int i = 0; i < 8; i++)
        {
            Vector3 d = Geo.Facing(i * 45f);
            if (Ground.Ray(craft, spot, spot + d * room, out _, out _))
                return false;
        }

        return true;
    }

    // A box about half the wingspan swept along the line, so the wings count, not just the middle.
    private bool Swept(RBatwing craft, Vector3 from, Vector3 to)
    {
        float w = MathF.Max(_half.X, _half.Y) * 0.5f;
        Actor? hit = craft.Trace(out Vector3 at, out _, to, from, Component.ECollisionFilter.COF_TraceWorld, new Vector3(w, w, BoxDepth(_half.Z)), out _, 0);
        return hit != null && at != Vector3.Zero && !IsCargo(hit);
    }

    // With the car hooked, the craft's own boxes stop short of its roof. The car gets its own sweep.
    private float BoxDepth(float want) => Carrying ? MathF.Max(40f, MathF.Min(want, _hang - _cargoTop - 20f)) : want;

    // Rays tilted up and down as well as flat, so beams that cross above or below it count too.
    private static readonly Vector3[] FeelDirs = BuildFeelDirs();

    private static Vector3[] BuildFeelDirs()
    {
        var dirs = new List<Vector3>();
        for (int i = 0; i < 12; i++)
            dirs.Add(Geo.Facing(i * 30f));
        for (int i = 0; i < 8; i++)
        {
            Vector3 flat = Geo.Facing(i * 45f + 22.5f) * 0.82f;
            dirs.Add(flat + new Vector3(0f, 0f, 0.57f));
            dirs.Add(flat - new Vector3(0f, 0f, 0.57f));
        }

        dirs.Add(Vector3.UnitZ);
        return dirs.ToArray();
    }

    // Short rays all round the craft. Anything they touch pushes it away, harder the closer it is.
    private void Feel(RBatwing craft)
    {
        Vector3 at = _fm.Position;
        float span = MathF.Max(_half.X, _half.Y);
        Vector3 push = Vector3.Zero;
        foreach (Vector3 d in FeelDirs)
        {
            Actor? what = craft.Trace(out Vector3 hit, out Vector3 face, at + d * (span + AvoidRange), at, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0);
            if (what == null || hit == Vector3.Zero || IsCargo(what))
                continue;

            // Floor under it is where it lands, not something to shy away from. Pushing off it held the
            // Batwing up off the street when it was setting the car down.
            if (d.Z <= 0f && face.Z > 0.7f)
                continue;
            float near = Math.Clamp(1f - (Vector3.Distance(at, hit) - span) / AvoidRange, 0f, 1f);
            push -= d * near;
        }

        _avoid = push;
    }

    private FlightInput Autopilot(RBatwing craft, Vector3 centre, RPawnPlayer batman, float dt)
    {
        Vector3 at = _fm.Position;

        // Catching an eject or hauling the Batmobile, it goes where that needs and fights nothing.
        Vector3? job = Catching && World.Pawn is RPawnPlayer ? CatchSpot(batman) : HaulSpot();
        Actor? target = _pickup || job != null ? null : EscortTarget();

        // A chased vehicle pulls the Batwing away from Batman, so the spot is kept near the vehicle instead.
        // The strike check ends the chase if it gets too far from him.
        bool chase = Chasing(target);
        Vector3 station = job ?? (_pickup ? PickupSpot(centre) : Station(craft, chase ? target!.Location : centre, target, dt));
        float gap = Vector3.Distance(station, at);

        // Going down to a lower spot, get over it first and then drop straight in. The air above a road is
        // clear, a slanted line down into a street usually isn't.
        Vector3 goal = station.Z < at.Z - 500f && FlatDistance(station, at) > 1500f ? new Vector3(station.X, station.Y, at.Z) : station;
        Vector3 to = goal - at;
        float toLen = to.Length();
        if (_feel.Elapsed(0.1))
            Feel(craft);

        // Climb only when something sits between it and where it's going, or right in front of it. Looking
        // further ahead along the path made it climb away from targets down on the street, since diving
        // towards one points the look-ahead at the road below it.
        if (_escortAhead.Elapsed(0.1))
        {
            Vector3 path = _fm.Velocity.LengthSquared() > 100f ? Vector3.Normalize(_fm.Velocity) : _fm.Forward;
            float near = MathF.Min(Math.Clamp(_fm.Velocity.Length() * 0.8f, 600f, 6000f), toLen);
            Vector3 ahead = at + path * near;
            _escortBlocked = (toLen > 300f && (Swept(craft, at, goal) || CargoSwept(at, goal))) || Swept(craft, at, ahead) || CargoSwept(at, ahead);
        }

        // Hauling, a blocked path raises the cruise height a bit at a time instead. The extra climb tipped the
        // nose up so hard with the car that it went far higher than it needed.
        if (Hauling)
        {
            _climb = 0f;
            if (_escortBlocked && _haul == Haul.Cruise)
                _cruiseZ = MathF.Max(_cruiseZ, at.Z + 1000f * dt * 4f);
        }
        else
        {
            _climb = _escortBlocked ? MathF.Min(_climb + 1500f * dt * 4f, ClimbMost) : MathF.Max(0f, _climb - 800f * dt);
        }
        if (job != null)
            // Stopped to climb over something isn't stuck.
            Unstick(Catching ? batman.Location : HaulHome(), gap, dt, Catching || (_haul == Haul.Cruise && Carrying && _escortBlocked), canLose: false);
        else
            Unstick(centre, gap, dt, chase && target!.Velocity.Length() > ChaseMoving, canLose: true);

        // Wings only to catch up, with Batman or a vehicle it's chasing. Never for a target it's fighting or a
        // pickup, since a jet can't stop over a spot and ends up diving at it. Measured flat so a long drop
        // into a street doesn't count as far.
        float across = FlatDistance(station, at);
        // With the car hooked it flies like a helicopter the whole way, up, across and down. On the wings it
        // was too fast to climb out of the way of buildings that only loaded in as it got close.
        bool holdStill = Catching || Carrying || _haul is Haul.Climb or Haul.Lower;
        bool hover = (target != null && !chase) || _pickup || holdStill || (_fm.HoverWanted ? across < WingsFrom : across < HoverWithin);
        if (hover != _fm.HoverWanted)
        {
            _fm.HoverWanted = hover;
            if (hover)
            {
                HoverSounds();
                Flare(1.2f);
            }
            else
            {
                WingSounds();
                _fm.Throttle = MathF.Max(_fm.Throttle, 0.8f);
            }
        }

        if (!hover)
        {
            Vector3 aim = toLen > 1f ? to / toLen : _fm.Forward;
            if (_climb > 0f)
                aim = Vector3.Normalize(aim + new Vector3(0f, 0f, MathF.Min(_climb / 5000f, 1.5f)));
            if (_avoid.LengthSquared() > 0.01f)
                aim = Vector3.Normalize(aim + _avoid * 1.5f);
            _fm.Throttle = Math.Clamp(0.55f + (CruiseSpeed * 2f - _fm.Airspeed) / 4000f, 0.3f, 1f);
            // No afterburner with the car hooked, the city has to keep up with Batman in it.
            return new FlightInput { Aim = aim, Assist = true, Boost = gap > BoostFrom && !Carrying, Heading = MathF.Atan2(_fm.Forward.Y, _fm.Forward.X) };
        }

        // Face the target when there is one, otherwise the way it's going, or Batman when it's settled. On a
        // job it keeps its heading once it's close, so it doesn't spin round over the car.
        float heading = MathF.Atan2(_fm.Forward.Y, _fm.Forward.X);
        Vector3 face = target != null ? target.Location - at : gap > 1500f ? to : job != null ? Vector3.Zero : centre - at;
        if (_pickup && gap < 1500f)
            heading = _pickupYaw;
        else if (face.X * face.X + face.Y * face.Y > 100f)
            heading = MathF.Atan2(face.Y, face.X);

        var fwd = new Vector3(MathF.Cos(heading), MathF.Sin(heading), 0f);
        var side = new Vector3(-fwd.Y, fwd.X, 0f);
        var flatTo = new Vector3(to.X, to.Y, 0f);

        // Something in the way on the trip across, so it stops and climbs in place until the way is clear.
        bool climbOut = _haul == Haul.Cruise && Carrying && _escortBlocked;
        if (climbOut)
            flatTo = Vector3.Zero;

        // The drop spot was already checked for wing room, so walls round it only nudge it there.
        bool straight = _haul is Haul.Climb or Haul.Lower;
        float shy = straight ? 0.2f : 1f;
        Vector3 want = flatTo * StationGain + new Vector3(_avoid.X, _avoid.Y, 0f) * (AvoidPush * shy);
        float most = _fm.HoverSpeed * 1.8f;
        if (want.Length() > most)
            want = Vector3.Normalize(want) * most;
        bool fast = want.Length() > _fm.HoverSpeed;
        float scale = _fm.HoverSpeed * (fast ? 1.8f : 1f);
        return new FlightInput
        {
            Move = new Vector2(Vector3.Dot(want, side) / scale, Vector3.Dot(want, fwd) / scale),
            // Past -1 the hover model just asks for a faster drop, still capped by its own acceleration limit.
            // Going straight up or down with the car the walls round it don't get a say in its height, and it
            // comes down faster from high up.
            Lift = climbOut ? 1.5f : Math.Clamp(to.Z / 1500f + (straight ? 0f : _avoid.Z), _haul == Haul.Lower ? -3.5f : -2.5f, 1.5f),
            Boost = fast,
            Heading = heading,
            Assist = true,
        };
    }

    // It can wedge itself under something or lose Batman entirely. If it stops closing on its spot for a
    // while, put it back above him.
    private void Unstick(Vector3 centre, float gap, float dt, bool trailing, bool canLose)
    {
        bool lost = canLose && Vector3.DistanceSquared(_fm.Position, centre) > LostFrom * LostFrom;
        // Falling behind something driving away isn't being stuck.
        bool stuck = !trailing && gap > 3000f && gap > _lastGap - 30f * dt;
        _lastGap = gap;
        _stuckFor = lost || stuck ? _stuckFor + dt : MathF.Max(0f, _stuckFor - dt);
        if (_stuckFor < StuckSeconds)
            return;

        _stuckFor = 0f;

        // Never jumps with the Batmobile hooked. That once dropped Batman over a district that hadn't loaded.
        // It goes up instead and lets the climb and avoidance work it free.
        if (Carrying)
        {
            _cruiseZ = MathF.Max(_cruiseZ, _fm.Position.Z + 1500f);
            Log.Info(Title + ": stuck at " + Geo.Position(_fm.Position) + " with the Batmobile hooked, cruise height now " + Fmt.Whole(_cruiseZ / Metre) + ".");
            return;
        }

        _climb = 0f;
        Vector3 to = new Vector3(centre.X, centre.Y, centre.Z + StationHeight + 2000f);
        if (World.Player is { IsValid: true } p && Ground.Ray(p, centre + new Vector3(0f, 0f, 100f), to, out Vector3 roof, out _))
            to = roof - new Vector3(0f, 0f, 400f);
        Log.Info(Title + ": escort " + (lost ? "lost Batman" : "stuck") + " at " + Geo.Position(_fm.Position) + ", back over him at " + Geo.Position(to) + ".");
        _fm.Place(to, MathF.Atan2(_fm.Forward.Y, _fm.Forward.X), Vector3.Zero);
        _fm.Hover = 1f;
        _fm.HoverWanted = true;
        Place(_fm.Position, _fm.Forward, _fm.Right, _fm.Up);
    }

    // No damage on escort. It just bounces off what it clips and climbs.
    private void EscortCollide(RBatwing craft, Vector3 from)
    {
        Vector3 to = _fm.Position;
        if (Vector3.DistanceSquared(from, to) < 0.01f)
            return;

        if (Carrying)
        {
            bool flat = CargoCollide(from, to, hurts: false);
            if (_fm.Position != to)
            {
                if (!flat)
                    _climb = MathF.Min(_climb + 2500f, ClimbMost);
                to = _fm.Position;
            }
        }

        float body = Math.Clamp(MathF.Min(_half.X, _half.Y) * 0.45f, 150f, 420f);
        var extent = new Vector3(body, body, BoxDepth(_half.Z * 0.8f));
        Actor? hit = craft.Trace(out Vector3 at, out Vector3 n, to, from, Component.ECollisionFilter.COF_TraceWorld, extent, out _, 0);
        if (hit == null || at == Vector3.Zero || n.LengthSquared() < 0.5f || IsCargo(hit))
            return;

        _fm.Position = at + n * 20f;
        float into = Vector3.Dot(_fm.Velocity, n);
        if (into < 0f)
            _fm.Velocity -= n * into * 1.3f;
        _climb = MathF.Min(_climb + 2500f, ClimbMost);
        Burst(_kit?.Impact, at, n);
        if (_scrapeSound.Elapsed(0.25))
            Sound(_kit?.Scrape);
    }

    private Orders EscortOrders()
    {
        var orders = new Orders { Look = _fm.Forward, Free = false, Homing = true };
        if (EscortTarget() is not { } target)
            return orders;

        Vector3 to = target.Location - _fm.Position;
        float dist = to.Length();
        if (dist < 1f)
            return orders;

        // Hovering it only turns flat, so "in front" means facing it, whatever the height difference.
        Vector3 dir = to / dist;
        float front = Vector3.Dot(Flat(dir), Flat(_fm.Forward));
        Vector3 nose = _fm.Position + dir * (_half.X + 100f);
        _targetSeen = _craft is { IsValid: true } craft && Sees(craft, nose, target);
        orders.Look = dir;
        orders.Target = target;
        orders.Gun = _targetSeen && front > GunFront && dist < GunRange * 0.5f;
        if (!_targetSeen)
            return orders;
        if (target is RPawnVillain)
            return orders;

        // Guns don't bring down anything flying, so those get the cannon more often and missiles even alone.
        // So does a vehicle Batman ordered a strike on, with the whole barrage on it, and a struck turret, which
        // mostly falls to the guns but gets the full show anyway.
        bool flying = target is RVehicleFlying or RHelicopterBase;
        bool ordered = Chasing(target) || Turret(target);
        bool heavy = flying || ordered;
        double now = Clock.Now;
        if (front > 0.9f && now >= _nextCannon && Car.Weapon<RBatmobileHeavyCannon>()?.ProjectileArchetype is { IsValid: true })
        {
            orders.Cannon = true;
            _nextCannon = now + (heavy ? CannonEveryFlying : CannonEvery);
        }

        if ((heavy || _foesNear >= 2) && front > 0.8f && now >= _nextBarrage && Car.Weapon<RBatmobileMissileLauncher>()?.MissileClass is { IsValid: true })
        {
            _lockUntil = now + LockHold;
            _nextBarrage = now + (heavy ? BarrageEveryFlying : BarrageEvery);
        }

        orders.Lock = now < _lockUntil;
        orders.Stack = orders.Lock && ordered;
        return orders;
    }
}
