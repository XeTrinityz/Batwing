using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Pilot
{
    private const float CrashSpeed = 2600f;
    private const float LandSpeed = 700f;
    private const float HullPoints = 400f;
    private const float OutsideMargin = 30000f;
    private const float CeilingAbove = 30000f;

    private float _hull = 1f;
    private bool _landed;
    private float _impactIn = 99f;
    private Every _lookAhead, _solidCheck, _scrape;
    private bool _solidBelow = true, _loadingBelow;
    private float _noSolidFor, _outsideFor;
    private bool _away;
    private Every _cityCheck;
    private float _shotDamage;
    private Vector3 _shotFrom;
    private double _underFireUntil;

    public float Hull => Math.Clamp(_hull, 0f, 1f);

    public bool Landed => _landed;

    // Seconds until it hits something on its current path, 99 when nothing is close.
    public float ImpactIn => _impactIn;

    public bool UnderFire => Clock.Now < _underFireUntil;

    public bool Outside => _outsideFor > 0f;

    public bool TurningBack => _outsideFor > 3f;

    public bool NothingBelow => _noSolidFor > 1.5f;

    public bool LoadingBelow => _loadingBelow;

    public bool Absorbs(RPawnPlayer pawn, int damage, Class? type, Vector3 at)
    {
        if (Remote || damage <= 0 || pawn.Ptr != _batman.Ptr)
            return false;
        if (type is { IsValid: true } && type.Name.ToString().Contains("KillZone", StringComparison.OrdinalIgnoreCase))
            return false;

        _shotDamage += damage;
        _shotFrom = at;
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
        Rumble.Play(hard > 0.5f ? Buzz.Hit : Buzz.Bump);

        Vector3 away = _fm.Position - _shotFrom;
        if (_shotFrom.LengthSquared() < 1f || away.LengthSquared() < 1f)
            away = -_fm.Forward;
        away = Vector3.Normalize(away);
        Craft.Burst(_craft.Kit.Impact, _fm.Position - away * (_craft.Half.X * 0.4f) + _craft.Up * (_craft.Half.Z * 0.5f), away);
        if (_scrape.Elapsed(0.25))
            _craft.Sound(hard > 0.5f ? _craft.Kit.Hit : _craft.Kit.Scrape);

        if (!Opt.Crashes.On)
            return;

        _hull -= damage / HullPoints;
        Log.Detail("Batwing: took " + Fmt.Whole(damage) + " damage, hull " + Fmt.Percent(_hull) + ".");
        if (_hull <= 0f)
            Crash(away, "SHOT DOWN");
    }

    // Sitting on the ground, it stays put until it's told to lift off.
    private void Parked(in FlightInput input, float dt)
    {
        _fm.Velocity = Vector3.Zero;
        _fm.Rates = Vector3.Zero;
        bool liftOff = input.Lift > 0.1f || !_fm.HoverWanted || input.Move.LengthSquared() > 0.04f;
        if (!liftOff)
            return;

        _landed = false;
        _fm.Velocity = new Vector3(0f, 0f, 300f);
        _craft.Flare(0.5f);
        _craft.Puff();
        _fm.Step(dt, input);
    }

    // False when it crashed this step.
    private bool Collide(Vector3 from)
    {
        Vector3 to = _fm.Position;
        if (!_craft.Alive || Vector3.DistanceSquared(from, to) < 0.01f)
            return true;

        if (_craft.Cargo is { Alive: true } cargo && cargo.Collide(_craft, from, to, out float carInto, out bool carFlat))
        {
            if (!carFlat && carInto > 400f && _craft.Cargo != null)
                Hit(_craft.Cargo.Car.Location, Vector3.UnitZ, carInto);
            else if (carInto > 200f && _scrape.Elapsed(0.25))
                _craft.Sound(_craft.Kit.Scrape);
            if (Wing.Now != this)
                return false;
            to = _fm.Position;
        }

        float body = Math.Clamp(MathF.Min(_craft.Half.X, _craft.Half.Y) * 0.45f, 150f, 420f);
        var extent = new Vector3(body, body, _craft.BoxDepth(_craft.Half.Z * 0.8f));
        Actor? hit = _craft.Actor.Trace(out Vector3 at, out Vector3 n, to, from, Component.ECollisionFilter.COF_TraceWorld, extent, out _, 0);
        if (hit != null && at != Vector3.Zero && n.LengthSquared() > 0.5f && !_craft.IsCargo(hit))
        {
            float into = -Vector3.Dot(_fm.Velocity, n);
            bool gentle = n.Z > 0.7f && into < LandSpeed && (_fm.Hover > 0.5f || _fm.Velocity.Length() < LandSpeed * 2f);
            _fm.Position = at + n * 4f;
            if (gentle && !_craft.Carrying)
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
            if (Wing.Now != this)
                return false;
        }

        // The wingtips are a long way out from the body box, so they get a line each.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 tip = _craft.Right * (side * _craft.Half.Y * 0.9f);
            if (_craft.Actor.Trace(out Vector3 tipAt, out Vector3 tipN, to + tip, from + tip, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0) == null
                || tipAt == Vector3.Zero)
                continue;

            _fm.Position += tipN * 60f;
            _fm.Velocity -= tipN * MathF.Min(Vector3.Dot(_fm.Velocity, tipN), 0f) * 1.2f;
            _fm.Rates += new Vector3(side * 1.6f, 0f, -side * 0.4f);
            _fm.Release();
            _shake = MathF.Max(_shake, 0.5f);
            if (Opt.Crashes.On)
                _hull -= MathF.Min(0.3f / 60f, 0.03f);
            Craft.Burst(_craft.Kit.Impact, tipAt, tipN);
            if (_scrape.Elapsed(0.25))
            {
                _craft.Sound(_craft.Kit.Scrape);
                Rumble.Play(Buzz.Bump);
            }
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
        Craft.Burst(_craft.Kit.Impact, at, n);
        _craft.Sound(hard > 0.3f ? _craft.Kit.Hit : _craft.Kit.Scrape);
        Rumble.Play(hard > 0.3f ? Buzz.Hit : Buzz.Bump);
        Log.Detail("Batwing: hit at " + Fmt.Whole(impact / Geo.Metre) + " m/s, hull " + Fmt.Percent(_hull) + ".");
        if (Opt.Crashes.On && _hull <= 0f)
            Crash(n, "WRECKED");
    }

    private void Touchdown()
    {
        if (_landed)
            return;

        _landed = true;
        float yaw = _fm.Yaw();
        _fm.Place(_fm.Position, yaw, Vector3.Zero);

        // Aim goes back on the nose so it doesn't turn on lift off. Mouse aim moves the gap into the look so
        // the view stays put, direct already has its camera on the nose.
        if (!Opt.Direct)
            _lookYaw = Geo.WrapDegrees(_lookYaw + _aimYaw - yaw * Geo.Deg);
        _aimYaw = yaw * Geo.Deg;
        _fm.HoverWanted = true;
        _fm.Hover = 1f;
        _shake = MathF.Max(_shake, 0.25f);
        _craft.Flare(0.5f);
        _craft.Puff();
        _craft.Sound(_craft.Kit.Land);
        Rumble.Play(Buzz.Land);
        Wing.Toast("LANDED");
    }

    private void Crash(Vector3 away, string why)
    {
        Vector3 at = _fm.Position;
        Vector3 off = away.LengthSquared() > 0.01f ? Vector3.Normalize(away) : Vector3.UnitZ;
        Craft.Burst(_craft.Kit.Explosion, at, Vector3.UnitZ);
        Craft.Burst(_craft.Kit.Impact, at, off);
        if (_craft.Cargo is { Alive: true } cargo)
        {
            cargo.Drop(_fm.Velocity, gently: false);
            _craft.Cargo = null;
        }

        RPawnPlayer? pawn = Batman();
        Vector3 fling = off * 900f + new Vector3(0f, 0f, 1500f) + _fm.Velocity * 0.15f;
        Release(pawn, new Drop(at + off * (_craft.Half.Z + 200f) + new Vector3(0f, 0f, 250f), fling, false));
        Actor? ear = pawn is { IsValid: true } ? pawn : World.Pawn;
        Craft.Play(_craft.Kit.Boom, ear);
        Craft.Play(_craft.Kit.Wreck, ear);
        Rumble.Play(Buzz.Crash);
        Log.Info("Batwing: " + why.ToLowerInvariant() + " at " + Geo.Position(at) + ", " + Fmt.Whole(_fm.Velocity.Length() / Geo.Metre) + " m/s.");
        Wing.Stop(why, false);
    }

    // Solid ground under it, for the warning that bailing out here would drop Batman into nothing.
    private void GroundCheck(RPawnPlayer pawn, float dt)
    {
        if (!Remote && _solidCheck.Elapsed(0.5))
        {
            _solidBelow = Safe.Try("solid ground", () => Ground.Real(pawn, _fm.Position, 60000f, out _), fallback: true);
            _loadingBelow = !_solidBelow && Streamer.StillLoading(_fm.Position);
        }

        _noSolidFor = Remote || _solidBelow ? 0f : _noSolidFor + dt;
    }

    // Looks ahead along the flight path for the pull up warning.
    private void Warnings()
    {
        if (!_lookAhead.Elapsed(0.12))
            return;

        _impactIn = 99f;
        float speed = _fm.Velocity.Length();
        if (_fm.HoverWanted || speed < 2500f || !_craft.Alive)
            return;

        float reach = MathF.Min(speed * 4f, 40000f);
        Vector3 dir = _fm.Velocity / speed;
        if (Ground.Ray(_craft.Actor, _fm.Position, _fm.Position + dir * reach, out Vector3 hit, out _))
            _impactIn = Vector3.Distance(_fm.Position, hit) / speed;
    }

    // Too far out from Gotham it's turned back, and too high it's held down. Nothing out there is loaded.
    private void Bounds(float dt)
    {
        Vector3 at = _fm.Position;
        if (_cityCheck.Elapsed(0.3))
            _away = Districts.Under(at) == null && Districts.Outside(at, OutsideMargin);

        if (_away)
        {
            _outsideFor += dt;
            if (_outsideFor > 3f)
            {
                Vector3 back = Districts.Middle - at;
                back.Z = 0f;
                if (back.LengthSquared() > 1f)
                    _fm.Velocity += Vector3.Normalize(back) * (900f * dt * MathF.Min(_outsideFor - 2f, 3f));
            }
        }
        else
        {
            _outsideFor = 0f;
        }

        float over = at.Z - (Districts.High.Z + CeilingAbove);
        if (over > 0f)
            _fm.Velocity -= new Vector3(0f, 0f, MathF.Min(over * 0.5f, 2500f) * dt);
    }
}
