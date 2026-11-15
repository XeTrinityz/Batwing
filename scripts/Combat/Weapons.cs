using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// What the weapons are told to do this frame. The pilot's controls fill it in, and so does the escort.
internal struct Orders
{
    public bool Gun;
    public bool Cannon;
    public bool Lock;

    // A tap while locking puts one more missile on the aimed target.
    public bool Stack;
    public Vector3 Look;
    public Actor? Target;

    // May pick thugs, manned vehicles, APCs and the tough ones by itself. The escort only hits those on a strike.
    public bool Free;
    public bool Homing;
}

// The Batmobile's guns, cannon and missiles, fired from the Batwing. One set per craft.
internal sealed class Weapons
{
    private const double GunGap = 0.085;
    public const float GunRange = 60000f;
    private const float GunSpread = 0.011f;
    private const int GunFilter = 73;
    private const float HeatPerShot = 0.03f;
    private const float CoolPerSecond = 0.35f;
    private const double OverheatSeconds = 2.4;
    private const float CooledHeat = 0.35f;
    private const float AssistCone = 0.9975f;

    private const float ThugRange = 20000f;
    private const float ThugCone = 0.996f;
    private const double RiotGapFallback = 0.3;
    private const float RiotHeat = 0.05f;

    private const double CannonGap = 1.1;
    private const double ReloadSoundAfter = 0.55;

    private const float LockCone = 0.966f;
    public const float LockRange = 35000f;
    private const double LockGap = 0.22;
    private const double BarrageCooldown = 3.5;
    private const double MissileStagger = 0.09;
    private const int BarrageFallback = 6;
    private const float MissileSpeedGuess = 5000f;

    private const float GunAhead = 30f, GunApart = 80f, GunBelow = -60f;
    private const float CannonAhead = 120f, CannonBelow = -20f;
    private const float PodAhead = 60f, PodBelow = -30f;

    // How close a round has to land to a turret or tank to count, and how many it takes to finish one.
    private const float TurretSplash = 300f, TankSplash = 700f;
    private const int TurretRounds = 30, TankRounds = 60, MissileRounds = 12, ShellRounds = 12;
    public const float TurretSlack = 500f;

    // How close spread rounds have to land to an APC's weak spot to count as on it, and how often it's looked for.
    private const float WeakSlack = 250f;
    private const double WeakEvery = 1.0;

    private readonly Craft _craft;
    private readonly Random _spread = new();
    private readonly List<Actor> _locks = new();
    private readonly Queue<Actor> _salvo = new();
    private readonly Dictionary<nint, int> _toughHits = new();
    private readonly Dictionary<nint, Weak> _weak = new();
    private readonly List<(Actor Target, double Due, int Rounds)> _inbound = new();

    private readonly record struct Weak(RVehicleCar.EAPCWeakSpot Kind, Vector3? Local, double Due);

    private Every _scan;
    private double _gunDue, _overheatUntil, _cannonDue, _reloadAt, _lockDue, _salvoDue, _barrageReady;
    private int _gunSide = 1, _missileSide = 1;
    private bool _stacked, _noWeaponsSaid, _reloadingSaid;
    private bool _free = true;
    private Actor? _ordered;
    private AkEvent? _cooledSound, _gunLoop, _reloadSound;

    // Found by the last scan, round the craft.
    public readonly List<Actor> Near = new();
    public readonly List<RPawnVillain> Thugs = new();
    public readonly List<(RPawnVillain Thug, RVehicleNPC Ride)> Riders = new();

    public float Heat { get; private set; }
    public bool Overheated { get; private set; }
    public bool Locking { get; private set; }
    public bool LockFull { get; private set; }
    public IReadOnlyList<Actor> Locks => _locks;
    public int Away => _salvo.Count;
    public Vector3 Look { get; private set; } = Vector3.UnitX;
    public Vector3 AimPoint { get; private set; }
    public Actor? AimFoe { get; private set; }

    public Weapons(Craft craft) => _craft = craft;

    private Vector3 From => _craft.Fm.Position;

    private Vector3 Nose => From + Look * (_craft.Half.X + 100f);

    public void Off()
    {
        GunLoop(false);
        _locks.Clear();
        _salvo.Clear();
        Locking = false;
    }

    public void Step(RPawnPlayer batman, in Orders orders, float dt)
    {
        if (!Opt.Weapons.On || !_craft.Alive)
        {
            Off();
            return;
        }

        double now = Clock.Now;
        if (_scan.Elapsed(0.5))
            Safe.Run("weapons scan", Scan);
        if (orders.Look.LengthSquared() > 0.01f)
            Look = Vector3.Normalize(orders.Look);
        _free = orders.Free;
        _ordered = orders.Target;
        Aim(orders.Target);
        Arrivals();

        if (_reloadAt > 0 && now >= _reloadAt)
        {
            _reloadAt = 0;
            _craft.Sound(_reloadSound);
        }

        if (!orders.Gun && !orders.Cannon && !orders.Lock && _salvo.Count == 0 && _locks.Count == 0)
        {
            GunLoop(false);
            Cool(dt, now);
            return;
        }

        RBatmobileCannon? vulcan = Batmobile.Weapon<RBatmobileCannon>();
        RBatmobileHeavyCannon? heavy = Batmobile.Weapon<RBatmobileHeavyCannon>();
        RBatmobileMissileLauncher? launcher = Batmobile.Weapon<RBatmobileMissileLauncher>();
        if (vulcan == null && heavy == null && launcher == null)
        {
            if (!_noWeaponsSaid && (orders.Gun || orders.Cannon || orders.Lock))
            {
                _noWeaponsSaid = true;
                Toasts.Notice("NO BATMOBILE IN THE CITY // NO WEAPONS", false);
            }

            return;
        }

        Guns(batman, vulcan, orders.Gun, dt, now);
        if (orders.Cannon)
            Cannon(heavy, now, orders.Homing);
        Missiles(launcher, orders.Lock, orders.Stack, now);
    }

    // Seated thugs are often hidden or not ticking, so they're found through the seats, not the other way.
    private void Scan()
    {
        Near.Clear();
        Thugs.Clear();
        Riders.Clear();
        Foes.Manned.Clear();
        Vector3 at = From;
        float reach = LockRange * LockRange;

        var cars = new List<RVehicleNPC>();
        var seated = new HashSet<nint>();
        foreach (RVehicleNPC v in World.All<RVehicleNPC>())
        {
            if (!Foes.Wreckable(v) || Vector3.DistanceSquared(v.Location, at) >= reach)
                continue;
            cars.Add(v);
            InlineArray<RVehicleNPC.FVehiclePassenger> seats = v.Passengers;
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i].Pawn is RPawnVillain { IsValid: true } p && Foes.Alive(p) && seated.Add(p.Ptr))
                {
                    Riders.Add((p, v));
                    Foes.Manned.Add(v.Ptr);
                }
            }
        }

        float near = ThugRange * ThugRange;
        foreach (RPawnVillain thug in World.All<RPawnVillain>())
        {
            if (seated.Contains(thug.Ptr) || !Foes.CanStrike(thug) || Vector3.DistanceSquared(thug.Location, at) >= near)
                continue;
            if (Foes.Carried(thug) is { } ride && Foes.Wreckable(ride))
            {
                Riders.Add((thug, ride));
                Foes.Manned.Add(ride.Ptr);
            }
            else
            {
                Thugs.Add(thug);
            }
        }

        foreach (RVehicleNPC v in cars)
        {
            if (Foes.Is(v))
                Near.Add(v);
        }

        AddNear<RHelicopterBase>(at, reach);
        AddNear<RVehicleHeavyTank>(at, reach);
        AddNear<RVehicleStaticTank>(at, reach);
        AddNear<RSentryGunBase>(at, reach);
        AddNear<RTurretBase>(at, reach);
    }

    private void AddNear<T>(Vector3 at, float reach) where T : Actor
    {
        foreach (T a in World.All<T>())
        {
            if (Foes.Is(a) && Vector3.DistanceSquared(a.Location, at) < reach)
                Near.Add(a);
        }
    }

    // The vehicle a thug is in, from the scan or what he's standing on.
    public RVehicleNPC? Ride(RPawnVillain thug)
    {
        foreach ((RPawnVillain rider, RVehicleNPC ride) in Riders)
        {
            if (rider.Ptr == thug.Ptr)
                return ride.IsValid ? ride : null;
        }

        return Foes.Carried(thug);
    }

    // Manned vehicles, APCs and the tough ones wait for a strike order when it isn't free to pick for itself.
    public bool Held(Actor foe) => !_free && Foes.OrderOnly(foe) && (_ordered == null || foe.Ptr != _ordered.Ptr);

    // A turret's middle is inside the tower it's on, so a line that stops right by it still counts as seeing it.
    public bool Sees(Vector3 from, Actor target) =>
        Foes.Turret(target) ? Sight.Clear(_craft.Actor, from, target.Location, TurretSlack) : _craft.Actor.FastTrace(target.Location, from, Vector3.Zero, false);

    private void Aim(Actor? ordered)
    {
        Vector3 from = From;
        Vector3 dir = Look;
        AimFoe = null;

        // An ordered target skips the cone search, as long as it's still fair game and in sight.
        if (ordered is { IsValid: true } && Foes.Strikeable(ordered) && Sees(Nose, ordered))
        {
            AimFoe = ordered;
            AimPoint = AimAt(ordered);
            return;
        }

        float best = AssistCone;
        foreach (Actor foe in Near)
        {
            if (!Foes.Is(foe) || Held(foe))
                continue;
            Vector3 to = foe.Location - from;
            float dist = to.Length();
            if (dist < 1f || dist > GunRange)
                continue;
            float dot = Vector3.Dot(to / dist, dir);
            if (dot > best && Sees(Nose, foe))
            {
                best = dot;
                AimFoe = foe;
            }
        }

        bool rider = false;
        if (AimFoe == null && _free)
            AimFoe = AimThug(from, dir, out rider);

        // Riot rounds always lock on, like the Batmobile's, and so does a vehicle picked through the thug in
        // it. Other targets only snap with gun assist.
        if (AimFoe is RPawnVillain || rider || (AimFoe != null && Opt.GunAssist.On))
        {
            AimPoint = AimAt(AimFoe!);
            return;
        }

        Vector3 end = from + dir * GunRange;
        AimPoint = _craft.Actor.Trace(out Vector3 hit, out _, end, Nose, (Component.ECollisionFilter)GunFilter, Vector3.Zero, out _, 0) != null && hit != Vector3.Zero
            ? hit
            : from + dir * 30000f;
    }

    // Aiming at a thug riding in a vehicle picks the vehicle, never riot rounds through the window.
    private Actor? AimThug(Vector3 from, Vector3 dir, out bool rider)
    {
        Actor? pick = null;
        rider = false;
        float best = ThugCone;
        foreach (RPawnVillain thug in Thugs)
        {
            if (Foes.CanStrike(thug) && Sighted(thug.Location, from, dir, ref best))
            {
                pick = thug;
                rider = false;
            }
        }

        foreach ((RPawnVillain thug, RVehicleNPC ride) in Riders)
        {
            if (Foes.Alive(thug) && Foes.Is(ride) && Sighted(thug.Location, from, dir, ref best))
            {
                pick = ride;
                rider = true;
            }
        }

        return pick;
    }

    private bool Sighted(Vector3 at, Vector3 from, Vector3 dir, ref float best)
    {
        Vector3 to = at - from;
        float dist = to.Length();
        if (dist < 1f || dist > ThugRange)
            return false;
        float dot = Vector3.Dot(to / dist, dir);
        if (dot <= best || !_craft.Actor.FastTrace(at, Nose, Vector3.Zero, false))
            return false;
        best = dot;
        return true;
    }

    public Vector3 AimAt(Actor a) => WeakSpot(a) ?? Foes.TankWeakSpot(a) ?? a.Location;

    // An APC only takes damage on its current weak spot, which the game moves around. There's no position
    // to read, so the APC is asked which points round its hull count as a hit, and the middle of those is the
    // spot. It's kept relative to the hull so it follows the APC between checks.
    public Vector3? WeakSpot(Actor a)
    {
        if (a is not RVehicleCar car || !Foes.Apc(car))
            return null;

        RVehicleCar.EAPCWeakSpot kind = car.CurrentWeakSpot;
        if (kind is RVehicleCar.EAPCWeakSpot.EAWS_NoWeakSpot or RVehicleCar.EAPCWeakSpot.EAWS_MAX)
            return null;

        double now = Clock.Now;
        bool known = _weak.TryGetValue(car.Ptr, out Weak w);
        if (!known || w.Kind != kind || now >= w.Due)
        {
            Vector3? found = Safe.Get("apc weak spot", () => ProbeWeakSpot(car), null);
            if (!known || w.Kind != kind)
                Log.Detail("Guns: " + car.Name + " weak spot " + kind + (found is { } l ? " at " + Geo.Position(l) + " on the hull." : " not found, aiming at the middle."));
            if (_weak.Count > 8)
                _weak.Clear();
            _weak[car.Ptr] = w = new Weak(kind, found, now + WeakEvery);
        }

        if (w.Local is not { } local)
            return null;
        Geo.Axes(car.Rotation, out Vector3 fwd, out Vector3 right, out Vector3 up);
        return Geo.Out(car.Location, fwd, right, up, local);
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

    private void Guns(RPawnPlayer batman, RBatmobileCannon? vulcan, bool firing, float dt, double now)
    {
        if (Overheated || !firing || vulcan == null)
        {
            GunLoop(false);
            Cool(dt, now);
            return;
        }

        RPawnVillain? thug = AimFoe as RPawnVillain;
        GunLoop(thug == null, vulcan);
        if (now < _gunDue)
            return;

        if (thug != null)
        {
            float gap = vulcan.RiotSuppressorFiringInterval;
            _gunDue = now + (gap is > 0.05f and < 2f ? gap : RiotGapFallback);
            RiotRound(batman, vulcan, thug);
            Heat += RiotHeat;
            Pilot.Shake(0.2f);
            Rumble.Play(Buzz.Riot);
        }
        else
        {
            _gunDue = now + GunGap;
            Round(batman, vulcan);
            Heat += HeatPerShot;
            Pilot.Shake(0.12f);
            Rumble.Play(Buzz.Gun);
        }

        if (Heat < 1f)
            return;

        Overheated = true;
        _overheatUntil = now + OverheatSeconds;
        _cooledSound = vulcan.ChainGunCooledSound;
        GunLoop(false);
        _craft.Sound(vulcan.ChainGunOverheatSound);
        Toasts.Show("GUN OVERHEATED", false);
    }

    private void Cool(float dt, double now)
    {
        if (!Overheated)
        {
            Heat = MathF.Max(0f, Heat - CoolPerSecond * dt);
            return;
        }

        if (now < _overheatUntil)
        {
            Heat = CooledHeat + (1f - CooledHeat) * (float)Math.Clamp((_overheatUntil - now) / OverheatSeconds, 0.0, 1.0);
            return;
        }

        Overheated = false;
        Heat = CooledHeat;
        _craft.Sound(_cooledSound);
        _cooledSound = null;
    }

    private void GunLoop(bool on, RBatmobileCannon? vulcan = null)
    {
        if (on)
        {
            if (_gunLoop != null || !_craft.Alive || vulcan?.FireLoopingEvent is not { IsValid: true } loop)
                return;
            Safe.Run("gun sound", () => _craft.Actor.AddLoopingAudioEvent(loop));
            _gunLoop = loop;
            return;
        }

        AkEvent? playing = _gunLoop;
        _gunLoop = null;
        if (playing is { IsValid: true } && _craft.Alive)
            Safe.Run("gun sound", () => _craft.Actor.RemoveLoopingAudioEvent(playing));
    }

    private Vector3 Muzzle()
    {
        _gunSide = -_gunSide;
        return _craft.WorldPoint(new Vector3(_craft.Seat.X + GunAhead, _gunSide * GunApart, _craft.Jets.Z + GunBelow));
    }

    private void Round(RPawnPlayer batman, RBatmobileCannon vulcan)
    {
        Vector3 muzzle = Muzzle();
        Vector3 dir = Vector3.Normalize(AimPoint - muzzle);
        dir = Vector3.Normalize(dir + new Vector3(Jitter(), Jitter(), Jitter()) * GunSpread);

        Craft.Burst(vulcan.GunFX, muzzle, dir);
        Craft.Burst(vulcan.ChainGunTracerSystem, muzzle, dir);
        Actor? hit = _craft.Actor.Trace(out Vector3 at, out _, muzzle + dir * GunRange, muzzle, (Component.ECollisionFilter)GunFilter, Vector3.Zero, out _, 0);
        if (hit == null || at == Vector3.Zero)
            return;

        // A turret sits inside its tower and a tank's rounds often land on its shield or the road by it, so
        // the trace names something else. Anything that lands close counts as a hit on it.
        if (hit is not RTankMissile and not RPawnVillain && !Foes.Is(hit) && ToughNear(at) is { } near)
            hit = near;

        int damage = Math.Max(vulcan.ChainGunDamage, 1);
        Safe.Run("gun hit", () => Hit(hit, at, dir, damage, batman, vulcan));
    }

    private void RiotRound(RPawnPlayer batman, RBatmobileCannon vulcan, RPawnVillain thug)
    {
        Vector3 muzzle = Muzzle();
        Vector3 target = thug.Location;
        Vector3 dir = Vector3.Normalize(target - muzzle);

        Craft.Burst(vulcan.RiotSuppressorMuzzleFlash ?? vulcan.GunFX, muzzle, dir);
        Craft.Burst(vulcan.RiotSuppressorTracerSystem ?? vulcan.ChainGunTracerSystem, muzzle, dir);
        _craft.Sound(vulcan.RiotSuppressorIndividualFireEvent);

        // The pawn trace can miss the capsule, so a clear line to the thug counts as a hit.
        float reach = Vector3.Distance(muzzle, target);
        Actor? hit = _craft.Actor.Trace(out Vector3 at, out _, muzzle + dir * (reach + 200f), muzzle, (Component.ECollisionFilter)GunFilter, Vector3.Zero, out _, 0);
        if (hit != null && hit.Ptr != thug.Ptr && at != Vector3.Zero && Vector3.Distance(muzzle, at) < reach - 150f)
        {
            Craft.Burst(vulcan.RiotSuppressorImpactEffect, at, -dir);
            return;
        }

        Craft.Burst(vulcan.RiotSuppressorImpactEffect, target, -dir);
        Craft.Play(vulcan.RiotSuppressorHitSound, thug);
        Safe.Run("riot hit", () => Foes.Knock(thug, dir, batman, vulcan));
    }

    private float Jitter() => (float)(_spread.NextDouble() * 2.0 - 1.0);

    private void Hit(Actor hit, Vector3 at, Vector3 dir, int damage, RPawnPlayer batman, RBatmobileCannon vulcan)
    {
        PlayerController? pc = World.Pc;
        Class type = RDmgType_BatmobileChainGun.StaticClass();

        // A round that finds a thug riding a vehicle goes into the vehicle.
        if (hit is RPawnVillain rider && Ride(rider) is { } ride)
            hit = ride;

        switch (hit)
        {
            case RTankMissile missile when !missile.bDeleteMe && !missile.WasShotOutOfSky:
                missile.TakeDamageFromWeapon(1000, pc!, at, Vector3.UnitZ, Vector3.Zero, type, default, batman, false);
                break;
            case RSentryGunBase sentry when Foes.Is(sentry):
                sentry.TakeDamageFromWeapon(damage, pc!, at, -dir, dir * 800f, type, default, batman, false);
                ToughHit(sentry);
                break;
            case RTurretBase turret when Foes.Is(turret):
                turret.TakeDamage(damage, pc!, at, dir * 800f, type, default, batman);
                ToughHit(turret);
                break;
            case RVehicleHeavyTank tank when Foes.HeavyTank(tank) && Foes.Is(tank):
                TankHurt(tank, damage, dir, pc, batman, type);
                break;
            case RVehicleNPC vehicle when Foes.Is(vehicle):
                vehicle.TakeDamageFromWeapon(damage, pc!, HitPoint(vehicle, at), dir * 800f, Vector3.Zero, type, default, batman, false);
                break;
            case RPawnVillain thug when Foes.CanStrike(thug):
                Foes.Knock(thug, dir, batman, vulcan);
                break;
        }
    }

    // Spread scatters rounds round the weak spot. One that lands close counts as on it.
    private Vector3 HitPoint(RVehicleNPC vehicle, Vector3 at) =>
        WeakSpot(vehicle) is { } weak && Vector3.Distance(at, weak) < WeakSlack ? weak : at;

    // Hacked heavy tanks kill each other, so they take damage of the right kind. It goes on the rear weak
    // spot as heavy cannon damage, whichever side the round came in from.
    private void TankHurt(RVehicleHeavyTank tank, int damage, Vector3 dir, PlayerController? pc, RPawnPlayer batman, Class fallback)
    {
        Vector3 weak = Foes.TankWeakSpot(tank) ?? tank.Location;
        Class type = Foes.ShellDamage();
        int was = tank.Health;
        tank.TakeDamageFromWeapon(damage, pc!, weak, -dir, dir * 800f, type, default, batman, false);
        if (!_toughHits.ContainsKey(tank.Ptr))
            Log.Detail("Guns: first hit on heavy tank " + tank.Name + ", health " + was + " to " + tank.Health + ", shielded " + tank.Shielded + ".");
        ToughHit(tank);
    }

    private void ToughHit(Actor tough, int rounds = 1)
    {
        int hits = (_toughHits.TryGetValue(tough.Ptr, out int n) ? n : 0) + rounds;
        if (_toughHits.Count > 16)
            _toughHits.Clear();
        _toughHits[tough.Ptr] = hits;
        if (hits < (tough is RVehicleHeavyTank ? TankRounds : TurretRounds) || !Foes.ToughUp(tough))
            return;

        _toughHits.Remove(tough.Ptr);
        Safe.Run("finish tough", () => Foes.Finish(tough, hits));
    }

    // The nearest turret or tank to where a round landed, if it's close enough to count. On a strike the
    // ordered target wins.
    private Actor? ToughNear(Vector3 at)
    {
        if (_ordered is { IsValid: true } o && Foes.Tough(o) && Foes.ToughUp(o) && Vector3.Distance(at, o.Location) < Splash(o))
            return o;

        Actor? best = null;
        float bestDist = float.MaxValue;
        foreach (Actor foe in Near)
        {
            if (!Foes.Tough(foe) || !Foes.ToughUp(foe))
                continue;
            float d = Vector3.Distance(at, foe.Location);
            if (d < Splash(foe) && d < bestDist)
            {
                bestDist = d;
                best = foe;
            }
        }

        return best;
    }

    private static float Splash(Actor a) => a is RVehicleHeavyTank ? TankSplash : TurretSplash;

    private void Cannon(RBatmobileHeavyCannon? heavy, double now, bool homingWanted)
    {
        if (heavy?.ProjectileArchetype is not { IsValid: true } shell)
        {
            Toasts.Show("NO CANNON FITTED", false);
            return;
        }

        if (now < _cannonDue)
            return;

        _cannonDue = now + CannonGap;
        Vector3 muzzle = _craft.WorldPoint(_craft.Tail + new Vector3(CannonAhead, 0f, CannonBelow));
        Vector3 target = AimPoint;
        Actor? foe = AimFoe is { IsValid: true } and not RPawnVillain ? AimFoe : null;
        bool homing = homingWanted && foe != null;
        Vector3? weak = homing ? WeakSpot(foe!) : null;
        if (homing && shell.Speed > 0f)
            target = (weak ?? foe!.Location) + foe!.Velocity * (Vector3.Distance(muzzle, foe.Location) / shell.Speed);
        Vector3 dir = Vector3.Normalize(target - muzzle);

        if (heavy.Spawn(shell.Class, heavy, FName.None, muzzle, Rotator.FromDirection(dir), shell, true) is not RProjectile p)
            return;

        // Homing pulls a shell to the middle of the hull, which misses an APC's weak spot. Those go straight.
        if (homing && weak == null && p is RBasicProjectile basic)
        {
            basic.SetTarget(foe!);
            basic.bLockedOn = true;
        }

        p.Init(dir);
        Safe.Run("shell credit", () => ShellCredit(muzzle, dir, homing ? foe : null, shell.Speed));
        Craft.Burst(heavy.FireFX, muzzle, dir);
        _craft.Sound(heavy.FireSound);
        _reloadSound = heavy.ReloadSound;
        _reloadAt = now + ReloadSoundAfter;
        Pilot.Shake(0.7f);
        Rumble.Play(Buzz.Cannon);
        _craft.Fm.Velocity -= dir * 250f;
        _craft.Fm.Rates += new Vector3(0f, 0.25f, 0f);
    }

    private void Missiles(RBatmobileMissileLauncher? launcher, bool held, bool stack, double now)
    {
        _locks.RemoveAll(v => !Foes.Is(v));

        if (_salvo.Count > 0 && now >= _salvoDue)
        {
            _salvoDue = now + MissileStagger;
            Actor next = _salvo.Dequeue();
            if (launcher != null && Foes.Is(next))
                Missile(launcher, next);
        }

        if (launcher?.MissileClass is not { IsValid: true })
        {
            if (held && !Locking)
            {
                Locking = true;
                Toasts.Show("NO MISSILES FITTED", false);
            }
            else if (!held)
            {
                Locking = false;
            }

            return;
        }

        if (held && now < _barrageReady && !Locking)
        {
            if (!_reloadingSaid)
            {
                _reloadingSaid = true;
                Toasts.Show("MISSILES RELOADING", false);
            }

            return;
        }

        if (!held)
            _reloadingSaid = false;

        if (held && now >= _barrageReady)
        {
            int most = Math.Clamp(Batmobile.BarrageSize(launcher) is > 0 and int top ? top : BarrageFallback, 1, 16);
            if (_locks.Count < most)
                LockFull = false;

            if (!Locking)
            {
                Locking = true;
                LockFull = false;
                _stacked = false;
                _locks.Clear();
                _lockDue = now;
            }

            if (stack && (NextLock(true) ?? (_locks.Count > 0 ? _locks[^1] : null)) is { } aimed)
            {
                // Once the player starts stacking, auto locks stop spreading the barrage round.
                _stacked = true;
                if (_locks.Count >= most && _locks.FindLastIndex(l => l.Ptr != aimed.Ptr) is >= 0 and var spare)
                    _locks.RemoveAt(spare);

                if (_locks.Count < most)
                {
                    _locks.Add(aimed);
                    _craft.Sound(launcher.LockOnSound);
                }
            }

            if (!_stacked && _locks.Count < most && now >= _lockDue && NextLock(false) is { } foe)
            {
                _locks.Add(foe);
                _lockDue = now + LockGap;
                _craft.Sound(launcher.LockOnSound);
            }

            if (_locks.Count >= most && !LockFull)
            {
                LockFull = true;
                _craft.Sound(launcher.ConfirmLockOn);
            }

            return;
        }

        if (!Locking)
            return;

        Locking = false;
        if (_locks.Count == 0)
        {
            _craft.Sound(launcher.LockOffSound);
            return;
        }

        foreach (Actor foe in _locks)
            _salvo.Enqueue(foe);
        Log.Detail("Missiles: barrage at " + _locks.Count + (_locks.Count == 1 ? " target." : " targets."));
        _locks.Clear();
        _salvoDue = now;
        _barrageReady = now + BarrageCooldown;
    }

    private Actor? NextLock(bool again)
    {
        Vector3 from = From;
        Actor? best = null;
        float bestDot = LockCone;
        foreach (Actor foe in Near)
        {
            if (!Foes.Is(foe) || Held(foe) || (!again && _locks.Exists(l => l.Ptr == foe.Ptr)))
                continue;
            Vector3 to = foe.Location - from;
            float dist = to.Length();
            if (dist < 1f || dist > LockRange)
                continue;
            float dot = Vector3.Dot(to / dist, Look);
            if (dot <= bestDot || !Sees(Nose, foe))
                continue;

            bestDot = dot;
            best = foe;
        }

        return best;
    }

    private void Missile(RBatmobileMissileLauncher launcher, Actor target)
    {
        if (launcher.MissileClass is not { IsValid: true } missile)
            return;

        if (Foes.Tough(target))
            Inbound(target, Vector3.Distance(From, target.Location), MissileSpeedGuess, MissileRounds);

        _missileSide = -_missileSide;
        Vector3 from = _craft.WorldPoint((_missileSide < 0 ? _craft.PodLeft : _craft.PodRight) + new Vector3(PodAhead, 0f, PodBelow));
        Vector3 dir = Vector3.Normalize(Vector3.Normalize(target.Location - from) + _craft.Fm.Forward * 0.6f + _craft.Up * 0.25f);
        Craft.Burst(launcher.FireFX, from, dir);
        Rumble.Play(Buzz.Missile);
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

    // Shields stop the real missiles and shells, so each one aimed at a turret or tank counts as a burst of
    // rounds when it gets there.
    private void Inbound(Actor target, float dist, float speed, int rounds)
    {
        if (_inbound.Count < 32)
            _inbound.Add((target, Clock.Now + 0.3 + dist / Math.Max(speed, 1000f), rounds));
    }

    private void Arrivals()
    {
        double now = Clock.Now;
        for (int i = _inbound.Count - 1; i >= 0; i--)
        {
            (Actor target, double due, int rounds) = _inbound[i];
            if (now < due)
                continue;
            _inbound.RemoveAt(i);
            if (target.IsValid && Foes.ToughUp(target))
                ToughHit(target, rounds);
        }
    }

    // A homing shell goes where it's locked. A straight one goes where the line from the muzzle lands.
    private void ShellCredit(Vector3 muzzle, Vector3 dir, Actor? homing, float speed)
    {
        Actor? target = homing is { IsValid: true } && Foes.Tough(homing) && Foes.ToughUp(homing) ? homing : null;
        Vector3 at = target?.Location ?? Vector3.Zero;
        if (target == null)
        {
            Actor? hit = _craft.Actor.Trace(out at, out _, muzzle + dir * GunRange, muzzle, (Component.ECollisionFilter)GunFilter, Vector3.Zero, out _, 0);
            if (hit == null || at == Vector3.Zero)
                return;
            target = Foes.Tough(hit) && Foes.Is(hit) ? hit : ToughNear(at);
        }

        if (target != null)
            Inbound(target, Vector3.Distance(muzzle, at), speed, ShellRounds);
    }
}

internal static class Sight
{
    // A ray at a vehicle or turret stops on its own hull or tower, so anything that close counts as seeing it.
    public static bool Clear(Actor tracer, Vector3 eye, Vector3 at, float slack) =>
        !Ground.Ray(tracer, eye, at, out Vector3 hit, out _) || (slack > 0f && Vector3.Distance(eye, hit) > Vector3.Distance(eye, at) - slack);
}
