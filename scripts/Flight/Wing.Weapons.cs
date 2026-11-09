using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    private const double GunGap = 0.085;
    private const float GunRange = 60000f;
    private const float GunSpread = 0.011f;
    private const int GunFilter = 73;
    private const float HeatPerShot = 0.03f;
    private const float CoolPerSecond = 0.35f;
    private const double OverheatSeconds = 2.4;
    private const float AssistCone = 0.9975f;
    private const float CooledHeat = 0.35f;

    private const float ThugRange = 20000f;
    private const float ThugCone = 0.996f;
    private const double RiotGapFallback = 0.3;
    private const float RiotHeat = 0.05f;
    private const float RiotPush = 600f;
    private static readonly FName Spine = new("Bip01_Spine2");

    private const double CannonGap = 1.1;
    private const double ReloadSoundAfter = 0.55;

    private const float LockCone = 0.966f;
    private const float LockRange = 35000f;
    private const double LockGap = 0.22;
    private const double BarrageCooldown = 3.5;
    private const double MissileStagger = 0.09;
    private const int BarrageFallback = 6;

    private double _gunDue;
    private float _heat;
    private double _overheatUntil;
    private bool _overheated;
    private AkEvent? _cooledSound;
    private AkEvent? _gunLoop;
    private const float GunAhead = 30f, GunApart = 80f, GunBelow = -60f;
    private const float CannonAhead = 120f, CannonBelow = -20f;
    private const float PodAhead = 60f, PodBelow = -30f;

    private int _gunSide;
    private readonly Random _spread = new();

    private double _cannonDue;
    private double _reloadAt;
    private AkEvent? _reloadSound;

    private const float WeakSlack = 250f;
    private const double WeakEvery = 1.0;

    private readonly List<Actor> _foes = new();
    private readonly List<RPawnVillain> _thugs = new();
    private readonly List<(RPawnVillain Thug, RVehicleNPC Ride)> _riders = new();
    private readonly Dictionary<nint, Weak> _weak = new();

    private readonly record struct Weak(RVehicleCar.EAPCWeakSpot Kind, Vector3? Local, double Due);
    private readonly List<Actor> _locks = new();
    private readonly Queue<Actor> _salvo = new();
    private Every _foeScan;
    private bool _locking;
    private bool _lockFull;
    private bool _stacked;
    private double _lockDue;
    private double _salvoDue;
    private double _barrageReady;
    private int _missileSide;
    private bool _noWeaponsSaid;
    private bool _reloadingSaid;

    private Vector3 _aimPoint;
    private Actor? _aimFoe;

    private bool Armed => Opt.Weapons.On;

    public float GunHeat => _heat;

    public bool GunOverheated => _overheated;

    private void ResetWeapons()
    {
        _heat = 0f;
        _overheated = false;
        _overheatUntil = 0;
        _locks.Clear();
        _salvo.Clear();
        _weak.Clear();
        Manned.Clear();
        _locking = false;
        _lockFull = false;
        _noWeaponsSaid = false;
        _reloadingSaid = false;
        _reloadAt = 0;
        _cooledSound = null;
    }

    private void WeaponsOff()
    {
        GunLoop(false);
        _locks.Clear();
        _salvo.Clear();
        _locking = false;
    }

    // What the weapons are told to do this frame. The player's keys fill it, and so can an autopilot.
    private struct Orders
    {
        public bool Gun;
        public bool Cannon;
        public bool Lock;

        // A tap while locking puts one more missile on the aimed target.
        public bool Stack;
        public Vector3 Look;
        public Actor? Target;

        // Whether it may pick thugs, manned vehicles and APCs by itself. The escort only hits those on a strike.
        public bool Free;

        public bool Homing;
    }

    private Vector3 _look = Vector3.UnitX;

    private Orders PlayerOrders(bool listening)
    {
        // The gun edge is read every frame so a click held from before the lock doesn't count as a tap.
        bool gun = listening && (Down(VkLButton) || Pad.Button("x"));
        bool tap = listening && (Pressed(VkLButton) | PadPressed("x"));
        bool lockHeld = listening && (Down(VkRButton) || Pad.Button("a"));
        return new Orders
        {
            Gun = gun && !lockHeld,
            Stack = tap && lockHeld,
            Cannon = listening && (Pressed('Q') | PadPressed("rb")),
            Lock = lockHeld,
            Look = Opt.Direct ? _fm.Forward : AimDir(),
            Free = true,
            Homing = Opt.CannonHoming.On,
        };
    }

    private void WeaponsStep(RPawnPlayer pawn, in Orders orders, float dt)
    {
        if (!Armed || _craft is not { IsValid: true } craft)
        {
            WeaponsOff();
            return;
        }

        double now = Clock.Now;
        if (_foeScan.Elapsed(0.5))
            Safe.Run("batwing foes", FindFoes);
        if (orders.Look.LengthSquared() > 0.01f)
            _look = Vector3.Normalize(orders.Look);
        _free = orders.Free;
        _ordered = orders.Target;
        Aim(craft, orders.Target);
        MissilesLand();

        bool gun = orders.Gun;
        bool cannon = orders.Cannon;
        bool lockHeld = orders.Lock;

        if (_reloadAt > 0 && now >= _reloadAt)
        {
            _reloadAt = 0;
            Sound(_reloadSound);
        }

        if (!gun && !cannon && !lockHeld && _salvo.Count == 0 && _locks.Count == 0)
        {
            GunLoop(false);
            Cool(dt, now);
            return;
        }

        RBatmobileCannon? vulcan = Car.Weapon<RBatmobileCannon>();
        RBatmobileHeavyCannon? heavy = Car.Weapon<RBatmobileHeavyCannon>();
        RBatmobileMissileLauncher? launcher = Car.Weapon<RBatmobileMissileLauncher>();
        if (vulcan == null && heavy == null && launcher == null)
        {
            if (!_noWeaponsSaid && (gun || cannon || lockHeld))
            {
                _noWeaponsSaid = true;
                Readout.Notice(Title, "NO BATMOBILE IN THE CITY // NO WEAPONS", false);
            }

            return;
        }

        Guns(craft, pawn, vulcan, gun, dt, now);
        if (cannon)
            Cannon(heavy, now, orders.Homing);
        Missiles(craft, launcher, lockHeld, orders.Stack, now);
    }

    private void Aim(RBatwing craft, Actor? ordered)
    {
        Vector3 from = _fm.Position;
        Vector3 dir = _look;
        Vector3 nose = from + dir * (_half.X + 100f);
        _aimFoe = null;

        // An ordered target skips the cone search, as long as it's still fair game and in sight.
        if (ordered is { IsValid: true } && Strikeable(ordered) && Sees(craft, nose, ordered))
        {
            _aimFoe = ordered;
            _aimPoint = AimAt(ordered);
            return;
        }

        float best = AssistCone;
        foreach (Actor foe in _foes)
        {
            if (!Foe(foe) || Held(foe))
                continue;
            Vector3 to = foe.Location - from;
            float dist = to.Length();
            if (dist < 1f || dist > GunRange)
                continue;
            float dot = Vector3.Dot(to / dist, dir);
            if (dot > best && Sees(craft, nose, foe))
            {
                best = dot;
                _aimFoe = foe;
            }
        }

        bool rider = false;
        if (_aimFoe == null && _free)
            _aimFoe = AimThug(craft, from, dir, nose, out rider);

        // Riot rounds always lock on, like the Batmobile's, and so does a vehicle picked through the thug in
        // it. Other vehicles only snap with gun assist.
        if (_aimFoe is RPawnVillain || rider || (_aimFoe != null && Opt.GunAssist.On))
        {
            _aimPoint = AimAt(_aimFoe!);
            return;
        }

        Vector3 end = from + dir * GunRange;
        _aimPoint = craft.Trace(out Vector3 hit, out _, end, nose, (Component.ECollisionFilter)GunFilter, Vector3.Zero, out _, 0) != null && hit != Vector3.Zero
            ? hit
            : from + dir * 30000f;
    }

    // Aiming at a thug riding in a vehicle picks the vehicle, never riot rounds through the window.
    private Actor? AimThug(RBatwing craft, Vector3 from, Vector3 dir, Vector3 nose, out bool rider)
    {
        Actor? pick = null;
        rider = false;
        float best = ThugCone;
        foreach (RPawnVillain thug in _thugs)
        {
            if (CanStrike(thug) && Sighted(craft, thug.Location, from, dir, nose, ref best))
            {
                pick = thug;
                rider = false;
            }
        }

        foreach ((RPawnVillain thug, RVehicleNPC ride) in _riders)
        {
            if (Alive(thug) && Foe(ride) && Sighted(craft, thug.Location, from, dir, nose, ref best))
            {
                pick = ride;
                rider = true;
            }
        }

        return pick;
    }

    private static bool Sighted(RBatwing craft, Vector3 at, Vector3 from, Vector3 dir, Vector3 nose, ref float best)
    {
        Vector3 to = at - from;
        float dist = to.Length();
        if (dist < 1f || dist > ThugRange)
            return false;
        float dot = Vector3.Dot(to / dist, dir);
        if (dot <= best || !craft.FastTrace(at, nose, Vector3.Zero, false))
            return false;
        best = dot;
        return true;
    }

    // The vehicle a thug is sitting in or riding on. The foe scan reads every nearby vehicle's seats, and a thug
    // standing on one is based on it.
    private RVehicleNPC? Ride(RPawnVillain thug)
    {
        foreach ((RPawnVillain rider, RVehicleNPC ride) in _riders)
        {
            if (rider.Ptr == thug.Ptr)
                return ride.IsValid ? ride : null;
        }

        return Carried(thug);
    }

    private static RVehicleNPC? Carried(RPawnVillain thug)
    {
        if (thug.Base is RVehicleNPC on && on.IsValid)
            return on;
        if (thug.DrivenVehicle is RVehicleNPC driving && driving.IsValid)
            return driving;
        return null;
    }

    private static bool Strikeable(Actor a) => a is RPawnVillain thug ? CanStrike(thug) : Foe(a);

    // The game flags plain militia cars with an APC style weak spot too, but theirs is the EMP rocket kind. A
    // real APC's weak spots are its wheel arches.
    private static bool Apc(Actor? a) => a switch
    {
        RVehicleNPC { IsMostWantedApc: true } => true,
        RVehicleCar car => car.bHasAPCStyleWeakSpot && car.CurrentWeakSpot != RVehicleCar.EAPCWeakSpot.EAWS_EMPRocket,
        RVehicle v => v.bHasAPCStyleWeakSpot,
        _ => false,
    };

    private Vector3 AimAt(Actor a) => WeakSpot(a) ?? TankWeakSpot(a) ?? a.Location;

    // An APC only takes damage on its current weak spot, which the game moves around. There's no position for
    // it to read, so the APC is asked which points round its hull count as a hit, and the middle of those is
    // the spot. It's kept relative to the hull so it follows the APC between checks.
    private Vector3? WeakSpot(Actor a)
    {
        if (a is not RVehicleCar car || !Apc(car))
            return null;

        RVehicleCar.EAPCWeakSpot kind = car.CurrentWeakSpot;
        if (kind is RVehicleCar.EAPCWeakSpot.EAWS_NoWeakSpot or RVehicleCar.EAPCWeakSpot.EAWS_MAX)
            return null;

        double now = Clock.Now;
        bool known = _weak.TryGetValue(car.Ptr, out Weak w);
        bool fresh = !known || w.Kind != kind;
        if (fresh || now >= w.Due)
        {
            Vector3? found = null;
            Safe.Run("batwing weak spot", () => found = ProbeWeakSpot(car));
            if (fresh)
            {
                Log.Detail(Title + ": " + car.Name + " weak spot " + kind + (found is { } l
                    ? " found at " + Geo.Position(l) + " on the hull."
                    : " not found, aiming at the middle."));
            }

            if (_weak.Count > 8)
                _weak.Clear();
            _weak[car.Ptr] = w = new Weak(kind, found, now + WeakEvery);
        }

        if (w.Local is not { } local)
            return null;
        Geo.Axes(car.Rotation, out Vector3 fwd, out Vector3 right, out Vector3 up);
        return car.Location + fwd * local.X + right * local.Y + up * local.Z;
    }

    private static Vector3? ProbeWeakSpot(RVehicleCar car)
    {
        float half = car.GetVehicleHalfLongAxis();
        float side = car.GetVehicleHalfWidth();
        car.GetComponentsBoundingBox(out GameObject.FBox box);
        if (half < 50f || side < 50f || box.IsValid == 0)
            return null;

        float low = box.Min.Z - car.Location.Z, high = box.Max.Z - car.Location.Z;
        Geo.Axes(car.Rotation, out Vector3 fwd, out Vector3 right, out Vector3 up);
        Vector3 sum = Vector3.Zero;
        int hits = 0;
        const int Long = 8, Wide = 5, Tall = 4;
        for (int i = 0; i < Long; i++)
        {
            float x = -half + 2f * half * i / (Long - 1);
            for (int j = 0; j < Wide; j++)
            {
                float y = -side + 2f * side * j / (Wide - 1);
                for (int k = 0; k < Tall; k++)
                {
                    float z = low + (high - low) * k / (Tall - 1);
                    if (!car.DoesImpactHitWeakSpot(car.Location + fwd * x + right * y + up * z))
                        continue;
                    sum += new Vector3(x, y, z);
                    hits++;
                }
            }
        }

        return hits > 0 ? sum / hits : null;
    }

    // Gun spread scatters rounds round the weak spot. One that lands close counts as on it.
    private Vector3 HitPoint(RVehicleNPC vehicle, Vector3 at) =>
        WeakSpot(vehicle) is { } weak && Vector3.Distance(at, weak) < WeakSlack ? weak : at;

    private void FindFoes()
    {
        _foes.Clear();
        _thugs.Clear();
        _riders.Clear();
        Manned.Clear();
        Vector3 at = _fm.Position;
        float reach = LockRange * LockRange;

        // Seated thugs are often hidden or not ticking, so they're found through the seats, not the other way.
        var cars = new List<RVehicleNPC>();
        var seated = new HashSet<nint>();
        foreach (RVehicleNPC v in World.All<RVehicleNPC>())
        {
            if (!Car.Wreckable(v) || Vector3.DistanceSquared(v.Location, at) >= reach)
                continue;
            cars.Add(v);
            InlineArray<RVehicleNPC.FVehiclePassenger> seats = v.Passengers;
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i].Pawn is RPawnVillain { IsValid: true } p && Alive(p) && seated.Add(p.Ptr))
                {
                    _riders.Add((p, v));
                    Manned.Add(v.Ptr);
                }
            }
        }

        float near = ThugRange * ThugRange;
        foreach (RPawnVillain thug in World.All<RPawnVillain>())
        {
            if (seated.Contains(thug.Ptr) || !CanStrike(thug) || Vector3.DistanceSquared(thug.Location, at) >= near)
                continue;
            if (Carried(thug) is { } ride && Car.Wreckable(ride))
            {
                _riders.Add((thug, ride));
                Manned.Add(ride.Ptr);
            }
            else
            {
                _thugs.Add(thug);
            }
        }

        foreach (RVehicleNPC v in cars)
        {
            if (Foe(v))
                _foes.Add(v);
        }

        foreach (RHelicopterBase h in World.All<RHelicopterBase>())
        {
            if (Foe(h) && Vector3.DistanceSquared(h.Location, at) < reach)
                _foes.Add(h);
        }

        // Car.Wreckable leaves heavy tanks and the fixed drone turrets out of the vehicle list above.
        foreach (RVehicleHeavyTank t in World.All<RVehicleHeavyTank>())
        {
            if (Foe(t) && Vector3.DistanceSquared(t.Location, at) < reach)
                _foes.Add(t);
        }

        foreach (RVehicleStaticTank t in World.All<RVehicleStaticTank>())
        {
            if (Foe(t) && Vector3.DistanceSquared(t.Location, at) < reach)
                _foes.Add(t);
        }

        foreach (RSentryGunBase s in World.All<RSentryGunBase>())
        {
            if (Foe(s) && Vector3.DistanceSquared(s.Location, at) < reach)
                _foes.Add(s);
        }

        foreach (RTurretBase g in World.All<RTurretBase>())
        {
            if (Foe(g) && Vector3.DistanceSquared(g.Location, at) < reach)
                _foes.Add(g);
        }
    }

    private bool _free = true;
    private Actor? _ordered;

    // Manned vehicles and APCs only get hit on a strike order when it isn't free to pick its own targets.
    private bool Held(Actor foe) => !_free && OrderOnly(foe) && (_ordered == null || foe.Ptr != _ordered.Ptr);

    // Militia cars, APCs among them, only on a strike, as do vehicles with a thug aboard. Drone tanks are their
    // own class and stay fair game.
    private static bool OrderOnly(Actor foe) => foe is RVehicleCar || Apc(foe) || Tough(foe) || (foe is RVehicleNPC v && Manned.Contains(v.Ptr));

    // Vehicles with a thug aboard, from the last foe scan. Those are fair game whatever the game calls them.
    private static readonly HashSet<nint> Manned = new();

    private const float TurretSplash = 300f;
    private const int TurretRounds = 30;

    private readonly Dictionary<nint, int> _turretHits = new();

    // Sentry guns the militia set up, security turrets, and the fixed drone turrets out in the city.
    private static bool Turret(Actor? a) => a is RSentryGunBase or RTurretBase or RVehicleStaticTank;

    // Turrets that were finished off. Some don't show it in any flag the strike can read, so it would keep
    // going at them.
    private static readonly HashSet<nint> TakenOut = new();

    // A watchtower that was taken out keeps every turret flag as it was. Its gun is hidden, or the state it's
    // in says so. "Off" alone isn't trusted, a live one may switch off when Batman is far away.
    private static bool WatchtowerOut(RTurretBase t)
    {
        if (t is not RTurret_WatchtowerBase tower)
            return false;
        if (tower.unifiedGunMesh is { IsValid: true } gun && gun.HiddenGame)
            return true;

        string state = tower.GetStateName().ToString() ?? "";
        return state.Contains("Destroy", StringComparison.OrdinalIgnoreCase) || state.Contains("Dead", StringComparison.OrdinalIgnoreCase)
            || state.Contains("TakenOut", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly HashSet<nint> TowersLogged = new();

    // What a watchtower looks like to the strike, once each, so a destroyed one can be told from a live one.
    private static void WatchtowerLog(Actor a)
    {
        if (a is not RTurret_WatchtowerBase tower || !TowersLogged.Add(tower.Ptr))
            return;

        bool active = Safe.Try("watchtower active", tower.IsActive);
        Log.Info(Title + ": watchtower " + tower.Name + " state " + tower.GetStateName() + ", disabled " + tower.bDisabled + ", active " + active
            + ", hidden " + tower.bHidden + ", gun hidden " + (tower.unifiedGunMesh is { IsValid: true } g ? g.HiddenGame.ToString() : "no gun")
            + ", lights " + (tower.lightsInstance is { IsValid: true } l ? (l.bHidden ? "hidden" : "on") : "none") + ", can hurt Batman " + tower.bCanDamagePlayer
            + ", shooting " + tower.bIsShooting + ", taken out by a strike " + TakenOut.Contains(tower.Ptr) + ".");
    }

    // Finished off by a strike or switched off by the game. A turret doesn't drive away, so that's a kill.
    private static bool TurretDown(Actor a) =>
        TakenOut.Contains(a.Ptr) || a is RSentryGunBase { bDisabled: true } || a is RTurretBase { bDisabled: true };

    private static bool TurretUp(Actor a) => !TakenOut.Contains(a.Ptr) && a switch
    {
        RSentryGunBase s => s is { IsValid: true, bDeleteMe: false, bHidden: false, bDisabled: false },
        RTurretBase t => t is { IsValid: true, bDeleteMe: false, bHidden: false, bDisabled: false } && !WatchtowerOut(t),
        RVehicleStaticTank v => v is { IsValid: true, bDeleteMe: false, bHidden: false, DroneHacked: false } && v.Health > 0,
        _ => false,
    };

    // The guns may not be enough on their own for these, so a turret that has soaked up a full burst is
    // finished off the game's own way.
    private const int TankRounds = 60;

    // Plain heavy tanks. Commander tanks have several weak points and boss stages, those are story fights and
    // stay out of it.
    private static bool HeavyTank(Actor? a) => a is RVehicleHeavyTank { IsValid: true } t && t.WeakPoints.Count == 0 && t.BossStages.Count == 0;

    private static bool TankUp(RVehicleHeavyTank t) =>
        !TakenOut.Contains(t.Ptr) && t is { IsValid: true, bDeleteMe: false, bHidden: false, DroneHacked: false } && t.Health > 0;

    private static Vector3? TankWeakSpot(Actor a) =>
        a is RVehicleHeavyTank t && t.WeakPoint is { IsValid: true } wp ? wp.Bounds.Origin : null;

    // The Batmobile heavy cannon's shell damage, the nearest thing to what a hacked tank hits other tanks with.
    private static Class? ShellDamage() =>
        Car.Weapon<RBatmobileHeavyCannon>()?.ProjectileArchetype is { IsValid: true } shell && shell.MyDamageType is { IsValid: true } type ? type : null;

    // Things the guns barely scratch and that get finished off after enough hits.
    private static bool Tough(Actor? a) => Turret(a) || HeavyTank(a);

    private static bool ToughUp(Actor a) => a is RVehicleHeavyTank t ? TankUp(t) : TurretUp(a);

    // Hacked heavy tanks kill each other, so they take damage when it's the right kind. It's put on the rear
    // weak spot as heavy cannon damage, whichever side the round came in from.
    private void TankHurt(RVehicleHeavyTank tank, int damage, Vector3 dir, PlayerController? pc, RPawnPlayer pawn, Class fallback)
    {
        Vector3 weak = TankWeakSpot(tank) ?? tank.Location;
        Class type = ShellDamage() is { } heavy ? heavy : fallback;
        int was = tank.Health;
        tank.TakeDamageFromWeapon(damage, pc!, weak, -dir, dir * 800f, type, default, pawn, false);
        if (!_turretHits.ContainsKey(tank.Ptr))
            Log.Info(Title + ": first hit on heavy tank " + tank.Name + ", health " + was + " to " + tank.Health + ", shielded " + tank.Shielded + ", damage type " + type.Name + ".");
        ToughHit(tank);
    }

    private void ToughHit(Actor turret, int rounds = 1)
    {
        int hits = (_turretHits.TryGetValue(turret.Ptr, out int n) ? n : 0) + rounds;
        if (_turretHits.Count > 16)
            _turretHits.Clear();
        _turretHits[turret.Ptr] = hits;
        if (hits < (turret is RVehicleHeavyTank ? TankRounds : TurretRounds) || !ToughUp(turret))
            return;

        _turretHits.Remove(turret.Ptr);
        switch (turret)
        {
            case RVehicleHeavyTank tank:
                FinishTank(tank);
                return;
            case RSentryGunBase sentry:
                sentry.Explode(true);
                break;
            // Watchtowers have a shield that stops every shot. The game takes them out with this once Batman
            // has dealt with one, and it counts towards the district's watchtowers too.
            case BmSDK.BmScript.RTurret_Watchtower tower:
                tower.WatchtowerTakenOut(false);
                break;
            case RTurretBase gun:
                gun.SetDamaged();
                break;
        }

        bool still = TurretUp(turret);
        if (TakenOut.Count > 64)
            TakenOut.Clear();
        TakenOut.Add(turret.Ptr);
        Log.Info(Title + ": " + turret.Name + " took " + hits + " rounds and was still up, finished it off" + (still ? ", its flags still say it's up." : "."));
    }

    // Shield off and a killing hit on the weak spot. Died only if even that didn't do it.
    private static void FinishTank(RVehicleHeavyTank tank)
    {
        int was = tank.Health;
        Vector3 weak = TankWeakSpot(tank) ?? tank.Location;
        Class type = ShellDamage() is { } heavy ? heavy : RDmgType_BatmobileChainGun.StaticClass();
        PlayerController? pc = World.Pc;
        Safe.Run("heavy tank shield", tank.DisableShield);
        Safe.Run("heavy tank finish", () => tank.TakeDamageFromWeapon(tank.Health + 1, pc!, weak, Vector3.UnitZ, Vector3.Zero, type, default, World.Player!, false));
        bool died = false;
        if (tank.IsValid && tank.Health > 0)
        {
            Safe.Run("heavy tank died", () => tank.Died(pc!, type, weak));
            died = true;
        }

        if (TakenOut.Count > 64)
            TakenOut.Clear();
        TakenOut.Add(tank.Ptr);
        Log.Info(Title + ": heavy tank " + tank.Name + " still up after the barrage, health " + was + ", finished it off" + (died ? " with Died." : " with a hit on the weak spot."));
    }

    private static bool Foe(Actor? a) => a switch
    {
        Actor t when Turret(t) => TurretUp(t),
        RVehicleHeavyTank ht when HeavyTank(ht) => TankUp(ht),
        RVehicleNPC v => Car.Hostile(v) || (Manned.Contains(v.Ptr) && Car.Wreckable(v)),
        RHelicopterBase h => h is { IsValid: true, bDeleteMe: false, bHidden: false, IsFriendly: false, bFriendly: false },
        _ => false,
    };

    private void Guns(RBatwing craft, RPawnPlayer pawn, RBatmobileCannon? vulcan, bool firing, float dt, double now)
    {
        if (_overheated || !firing || vulcan == null)
        {
            GunLoop(false);
            Cool(dt, now);
            return;
        }

        RPawnVillain? thug = _aimFoe as RPawnVillain;
        if (thug != null)
            GunLoop(false);
        else
            GunLoop(true, vulcan);
        if (now < _gunDue)
            return;

        if (thug != null)
        {
            float gap = vulcan.RiotSuppressorFiringInterval;
            _gunDue = now + (gap is > 0.05f and < 2f ? gap : RiotGapFallback);
            RiotRound(craft, pawn, vulcan, thug);
            _heat += RiotHeat;
            _shake = MathF.Max(_shake, 0.2f);
        }
        else
        {
            _gunDue = now + GunGap;
            Round(craft, pawn, vulcan);
            _heat += HeatPerShot;
            _shake = MathF.Max(_shake, 0.12f);
        }

        if (_heat >= 1f)
        {
            _overheated = true;
            _overheatUntil = now + OverheatSeconds;
            _cooledSound = vulcan.ChainGunCooledSound;
            GunLoop(false);
            Sound(vulcan.ChainGunOverheatSound);
            Toast("GUN OVERHEATED");
        }
    }

    private void Cool(float dt, double now)
    {
        if (!_overheated)
        {
            _heat = MathF.Max(0f, _heat - CoolPerSecond * dt);
            return;
        }

        if (now < _overheatUntil)
        {
            _heat = CooledHeat + (1f - CooledHeat) * (float)Math.Clamp((_overheatUntil - now) / OverheatSeconds, 0.0, 1.0);
            return;
        }

        _overheated = false;
        _heat = CooledHeat;
        Sound(_cooledSound);
        _cooledSound = null;
    }

    private void GunLoop(bool on, RBatmobileCannon? vulcan = null)
    {
        if (on)
        {
            if (_gunLoop != null || _craft is not { IsValid: true } craft || vulcan?.FireLoopingEvent is not { IsValid: true } loop)
                return;
            Safe.Run("batwing gun sound", () => craft.AddLoopingAudioEvent(loop));
            _gunLoop = loop;
            return;
        }

        AkEvent? playing = _gunLoop;
        _gunLoop = null;
        if (playing is { IsValid: true } && _craft is { IsValid: true } c)
            Safe.Run("batwing gun sound", () => c.RemoveLoopingAudioEvent(playing));
    }

    private void Round(RBatwing craft, RPawnPlayer pawn, RBatmobileCannon vulcan)
    {
        _gunSide = -_gunSide;
        if (_gunSide == 0)
            _gunSide = 1;
        Vector3 muzzle = WorldPoint(_fm.Position, new Vector3(_seat.X + GunAhead, _gunSide * GunApart, _lf.Z + GunBelow));
        Vector3 dir = Vector3.Normalize(_aimPoint - muzzle);
        dir = Vector3.Normalize(dir + new Vector3(Jitter(), Jitter(), Jitter()) * GunSpread);

        Burst(vulcan.GunFX, muzzle, dir);
        Burst(vulcan.ChainGunTracerSystem, muzzle, dir);
        Actor? hit = craft.Trace(out Vector3 at, out Vector3 n, muzzle + dir * GunRange, muzzle, (Component.ECollisionFilter)GunFilter, Vector3.Zero, out _, 0);
        if (hit == null || at == Vector3.Zero)
            return;

        // A turret is a small target, so rounds landing right by the one being aimed at count as hitting it. On a
        // strike that's the ordered one, flying it yourself it's whatever the aim has settled on.
        Actor? aimed = _ordered is { IsValid: true } o && Turret(o) ? o : _aimFoe is { IsValid: true } f && Turret(f) ? f : null;
        if (aimed != null && hit.Ptr != aimed.Ptr && Vector3.Distance(at, aimed.Location) < TurretSplash)
            hit = aimed;

        int damage = Math.Max(vulcan.ChainGunDamage, 1);
        Safe.Run("batwing gun hit", () => Strike(hit, at, dir, damage, pawn, vulcan));
    }

    private void RiotRound(RBatwing craft, RPawnPlayer pawn, RBatmobileCannon vulcan, RPawnVillain thug)
    {
        _gunSide = _gunSide >= 0 ? -1 : 1;
        Vector3 muzzle = WorldPoint(_fm.Position, new Vector3(_seat.X + GunAhead, _gunSide * GunApart, _lf.Z + GunBelow));
        Vector3 target = thug.Location;
        Vector3 dir = Vector3.Normalize(target - muzzle);

        Burst(vulcan.RiotSuppressorMuzzleFlash ?? vulcan.GunFX, muzzle, dir);
        Burst(vulcan.RiotSuppressorTracerSystem ?? vulcan.ChainGunTracerSystem, muzzle, dir);
        Sound(vulcan.RiotSuppressorIndividualFireEvent);

        // The pawn trace can miss the capsule, so a clear line to the thug counts as a hit.
        float reach = Vector3.Distance(muzzle, target);
        Actor? hit = craft.Trace(out Vector3 at, out _, muzzle + dir * (reach + 200f), muzzle, (Component.ECollisionFilter)GunFilter, Vector3.Zero, out _, 0);
        bool blocked = hit != null && hit.Ptr != thug.Ptr && at != Vector3.Zero && Vector3.Distance(muzzle, at) < reach - 150f;
        if (blocked)
        {
            Burst(vulcan.RiotSuppressorImpactEffect, at, -dir);
            Log.Detail(Title + ": riot round on " + thug.Name + " blocked by " + hit!.Name + ".");
            return;
        }

        Burst(vulcan.RiotSuppressorImpactEffect, target, -dir);
        Sound(vulcan.RiotSuppressorHitSound, thug);
        Safe.Run("batwing riot hit", () => Knock(thug, dir, pawn, vulcan));
    }

    private float Jitter() => (float)(_spread.NextDouble() * 2.0 - 1.0);

    private void Strike(Actor hit, Vector3 at, Vector3 dir, int damage, RPawnPlayer pawn, RBatmobileCannon vulcan)
    {
        PlayerController? pc = World.Pc;
        Class damageType = RDmgType_BatmobileChainGun.StaticClass();

        // A round that finds a thug riding a vehicle goes into the vehicle.
        if (hit is RPawnVillain rider && Ride(rider) is { } ride)
            hit = ride;

        switch (hit)
        {
            case RTankMissile missile when !missile.bDeleteMe && !missile.WasShotOutOfSky:
                missile.TakeDamageFromWeapon(1000, pc!, at, Vector3.UnitZ, Vector3.Zero, damageType, default, pawn, false);
                break;
            case RSentryGunBase sentry when Foe(sentry):
                sentry.TakeDamageFromWeapon(damage, pc!, at, -dir, dir * 800f, damageType, default, pawn, false);
                ToughHit(sentry);
                break;
            case RTurretBase turret when Foe(turret):
                turret.TakeDamage(damage, pc!, at, dir * 800f, damageType, default, pawn);
                ToughHit(turret);
                break;
            case RVehicleHeavyTank tank when HeavyTank(tank) && Foe(tank):
                TankHurt(tank, damage, dir, pc, pawn, damageType);
                break;
            case RVehicleNPC vehicle when Foe(vehicle):
                vehicle.TakeDamageFromWeapon(damage, pc!, HitPoint(vehicle, at), dir * 800f, Vector3.Zero, damageType, default, pawn, false);
                break;
            case RPawnVillain thug when CanStrike(thug):
                Knock(thug, dir, pawn, vulcan);
                break;
        }
    }

    // Built the way the Batmobile's own FireChainGun does it (native, 0x42230). Calling DamagedBy
    // straight with Batman as the attacker did nothing at all.
    private static void Knock(RPawnVillain thug, Vector3 dir, RPawnPlayer pawn, RBatmobileCannon vulcan)
    {
        float momentum = vulcan.ImpactMomentum > 0f ? vulcan.ImpactMomentum : RiotPush;
        Vector3 push = dir * momentum;
        var info = new RPawnCombat.FDamageInfo();
        info.Attacker = vulcan.Owner is { IsValid: true } car ? car : pawn;
        info.BoneName = ImpactBone(vulcan);
        info.Impulse = push;
        info.ForceHitReactionHeading = push;
        // One round puts a thug down. A KO needs a hit bigger than what's left.
        info.DamageAmount = thug.Health + 1;
        info.DamageType = vulcan.DamageType is { IsValid: true } type ? type : RDmgType_BatmobileChainGun.StaticClass();
        info.bWillKnockOverOtherPawns = true;

        int health = thug.Health;
        vulcan.DamagePawnWithChainGun(thug, info);
        Log.Detail(Title + ": riot round on " + thug.Name + " for " + info.DamageAmount + ", health " + health + " to " + thug.Health + ".");
    }

    private static FName ImpactBone(RBatmobileCannon vulcan)
    {
        TArray<FName> bones = vulcan.RiotSuppressorImpactBones;
        return bones.Count > 0 ? bones[Random.Shared.Next(bones.Count)] : Spine;
    }

    // Alive goes first, it checks the thug still exists before anything reads from it.
    private static bool CanStrike(RPawnVillain thug) => Alive(thug) && !thug.bHidden;

    // A hostile thug still up, seen or not.
    private static bool Alive(RPawnVillain thug) =>
        thug is { IsValid: true, bDeleteMe: false, bGoodAsDead: false } && thug.Health > 0 && thug.CombatPawnType switch
        {
            // Brutes are MultiStage and count. Robots and the Riddler mech don't.
            RPawnCombat.ECombatPawnType.ECPT_Robot or RPawnCombat.ECombatPawnType.ECPT_RiddlerMechSuit
                or RPawnCombat.ECombatPawnType.ECPT_Player or RPawnCombat.ECombatPawnType.ECPT_Friendly => false,
            _ => true,
        };

    private void Cannon(RBatmobileHeavyCannon? heavy, double now, bool homingWanted)
    {
        if (heavy?.ProjectileArchetype is not { IsValid: true } shell)
        {
            Toast("NO CANNON FITTED");
            return;
        }

        if (now < _cannonDue)
            return;

        _cannonDue = now + CannonGap;
        Vector3 muzzle = WorldPoint(_fm.Position, _tail + new Vector3(CannonAhead, 0f, CannonBelow));
        Vector3 target = _aimPoint;
        bool homing = homingWanted && _aimFoe is { IsValid: true } and not RPawnVillain;
        Vector3? weak = homing ? WeakSpot(_aimFoe!) : null;
        if (homing && shell.Speed > 0f)
            target = (weak ?? _aimFoe!.Location) + _aimFoe!.Velocity * (Vector3.Distance(muzzle, _aimFoe.Location) / shell.Speed);
        Vector3 dir = Vector3.Normalize(target - muzzle);

        if (heavy.Spawn(shell.Class, heavy, FName.None, muzzle, Rotator.FromDirection(dir), shell, true) is not RProjectile p)
            return;

        // Homing pulls a shell to the middle of the hull, which misses an APC's weak spot. Those go straight.
        if (homing && weak == null && p is RBasicProjectile basic)
        {
            basic.SetTarget(_aimFoe!);
            basic.bLockedOn = true;
        }

        p.Init(dir);
        Burst(heavy.FireFX, muzzle, dir);
        Sound(heavy.FireSound);
        _reloadSound = heavy.ReloadSound;
        _reloadAt = now + ReloadSoundAfter;
        _shake = MathF.Max(_shake, 0.7f);
        _fm.Velocity -= dir * 250f;
        _fm.Rates += new Vector3(0f, 0.25f, 0f);
    }

    private void Missiles(RBatwing craft, RBatmobileMissileLauncher? launcher, bool held, bool stack, double now)
    {
        _locks.RemoveAll(v => !Foe(v));

        if (_salvo.Count > 0 && now >= _salvoDue)
        {
            _salvoDue = now + MissileStagger;
            Actor next = _salvo.Dequeue();
            if (launcher != null && Foe(next))
                Missile(launcher, next);
        }

        if (launcher?.MissileClass is not { IsValid: true })
        {
            if (held && !_locking)
            {
                _locking = true;
                Toast("NO MISSILES FITTED");
            }
            else if (!held)
            {
                _locking = false;
            }

            return;
        }

        if (held && now < _barrageReady && !_locking)
        {
            if (!_reloadingSaid)
            {
                _reloadingSaid = true;
                Toast("MISSILES RELOADING");
            }

            return;
        }

        if (!held)
            _reloadingSaid = false;

        if (held && now >= _barrageReady)
        {
            int most = Math.Clamp(Car.BarrageSize(launcher) is > 0 and int top ? top : BarrageFallback, 1, 16);
            if (_locks.Count < most)
                _lockFull = false;

            if (!_locking)
            {
                _locking = true;
                _lockFull = false;
                _stacked = false;
                _locks.Clear();
                _lockDue = now;
            }

            if (stack && StackTarget(craft) is { } aimed)
            {
                // Once the player starts stacking, auto locks stop spreading the barrage around.
                _stacked = true;
                if (_locks.Count >= most)
                {
                    int spare = _locks.FindLastIndex(l => l.Ptr != aimed.Ptr);
                    if (spare >= 0)
                        _locks.RemoveAt(spare);
                }

                if (_locks.Count < most)
                {
                    _locks.Add(aimed);
                    Sound(launcher.LockOnSound);
                }
            }

            if (!_stacked && _locks.Count < most && now >= _lockDue && NextLock(craft, false) is { } foe)
            {
                _locks.Add(foe);
                _lockDue = now + LockGap;
                Sound(launcher.LockOnSound);
            }

            if (_locks.Count >= most && !_lockFull)
            {
                _lockFull = true;
                Sound(launcher.ConfirmLockOn);
            }

            return;
        }

        if (!_locking)
            return;

        _locking = false;
        if (_locks.Count == 0)
        {
            Sound(launcher.LockOffSound);
            return;
        }

        foreach (Actor foe in _locks)
            _salvo.Enqueue(foe);
        Log.Info(Title + ": missile barrage at " + _locks.Count + (_locks.Count == 1 ? " target." : " targets."));
        _locks.Clear();
        _salvoDue = now;
        _barrageReady = now + BarrageCooldown;
    }

    // The foe under the crosshair, or the last lock if the aim has drifted off everything.
    private Actor? StackTarget(RBatwing craft) =>
        NextLock(craft, true) ?? (_locks.Count > 0 ? _locks[^1] : null);

    private Actor? NextLock(RBatwing craft, bool again)
    {
        Vector3 from = _fm.Position;
        Vector3 dir = _look;
        Actor? best = null;
        float bestDot = LockCone;
        foreach (Actor foe in _foes)
        {
            if (!Foe(foe) || Held(foe) || (!again && _locks.Exists(l => l.Ptr == foe.Ptr)))
                continue;
            Vector3 to = foe.Location - from;
            float dist = to.Length();
            if (dist < 1f || dist > LockRange)
                continue;
            float dot = Vector3.Dot(to / dist, dir);
            if (dot <= bestDot)
                continue;

            if (!Sees(craft, from + dir * (_half.X + 100f), foe))
                continue;

            bestDot = dot;
            best = foe;
        }

        return best;
    }

    // A turret's middle is inside the tower it's on, so a line that stops right by it still counts as seeing it.
    private static bool Sees(RBatwing craft, Vector3 from, Actor target) =>
        Turret(target) ? InView(craft, from, target.Location, TurretSlack) : craft.FastTrace(target.Location, from, Vector3.Zero, false);

    private const float MissileSpeedGuess = 5000f;
    private const int MissileRounds = 12;

    private readonly List<(Actor Turret, double Due)> _missilesInbound = new();

    // Shields stop the real missile, so each one aimed at a turret counts as a burst of rounds when it gets there.
    private void MissilesLand()
    {
        double now = Clock.Now;
        for (int i = _missilesInbound.Count - 1; i >= 0; i--)
        {
            (Actor turret, double due) = _missilesInbound[i];
            if (now < due)
                continue;
            _missilesInbound.RemoveAt(i);
            if (turret.IsValid && ToughUp(turret))
                ToughHit(turret, MissileRounds);
        }
    }

    private void Missile(RBatmobileMissileLauncher launcher, Actor target)
    {
        if (launcher.MissileClass is not { IsValid: true } missile)
            return;

        if (Tough(target) && _missilesInbound.Count < 32)
            _missilesInbound.Add((target, Clock.Now + 0.3 + Vector3.Distance(_fm.Position, target.Location) / MissileSpeedGuess));

        _missileSide = _missileSide >= 0 ? -1 : 1;
        Vector3 from = WorldPoint(_fm.Position, (_missileSide < 0 ? _podL : _podR) + new Vector3(PodAhead, 0f, PodBelow));
        Vector3 dir = Vector3.Normalize(Vector3.Normalize(target.Location - from) + _fm.Forward * 0.6f + _up * 0.25f);
        Burst(launcher.FireFX, from, dir);
        if (launcher.Spawn(missile.Class, launcher, FName.None, from, Rotator.FromDirection(dir), missile, true) is not RProjectile p)
            return;

        if (p is RBatmobileMissile homing)
        {
            homing.SetTarget(target);
            homing.bForceExplodeAtTarget = true;
            homing.ForceExplodeProxDist = 100f;
        }

        p.Init(dir);
    }
}
