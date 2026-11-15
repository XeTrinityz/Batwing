using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Escort
{
    private const float StationRadius = 4000f;
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
    private const float BoostFrom = 40000f;
    private const float ClimbMost = 20000f;
    private const float LostFrom = 40000f;
    private const float StuckSeconds = 4f;
    private const float ChaseMoving = 800f;

    // Extra height tried when nothing at the normal height is clear, lowest first.
    private static readonly float[] Raises = { 0f, 1500f, 3500f, 6000f };

    // Rays tilted up and down as well as flat, so beams that cross above or below it count too.
    private static readonly Vector3[] FeelDirs = BuildFeelDirs();

    private float _climb;
    private float _stationAngle, _attackAngle;
    private float _raise;
    private float _lastGap = float.MaxValue, _stuckFor;
    private bool _blocked;
    private nint _spotFor;
    private Vector3 _avoid;
    private Vector3? _roadSpot;
    private Every _spotCheck, _feel, _ahead;

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

    // Where it wants to be. Off Batman's shoulder, drifting slowly round him. With a target it takes a firing
    // spot off the target instead, kept within reach of Batman.
    private Vector3 Station(Vector3 centre, Actor? target, float dt)
    {
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
                // A road spot is fixed, no use for something driving away. A turret on a tower is better from
                // level with it out to the side than from over the street below it.
                Vector3? found = null;
                if (target is not RHelicopterBase && !Chasing(target) && !Foes.Turret(target))
                    Safe.Run("escort road spot", () => found = RoadSpot(centre, target));
                _roadSpot = found;
                if (found == null)
                    _attackAngle = ClearAngle(centre, target, _attackAngle);
            }
            else
            {
                _stationAngle = ClearAngle(centre, null, _stationAngle);
            }
        }

        if (target != null && _roadSpot is { } road)
            return road + new Vector3(0f, 0f, _climb);
        return SpotFor(centre, target, target != null ? _attackAngle : _stationAngle, _raise);
    }

    // Roads are open sky, so a spot over a road near the target is the best place to shoot from. Tries the road
    // under the target and the roads round it, keeps the ones with a clear line down, and takes the nearest.
    private Vector3? RoadSpot(Vector3 centre, Actor target)
    {
        Vector3 t = target.Location;
        Vector3? best = null;
        float bestDist = float.MaxValue;
        for (int i = -1; i < 8; i++)
        {
            Vector3 probe = i < 0 ? t : t + Geo.Facing(i * 45f) * Standoff;
            Vector3 road = RVehicle.GetNearestRoadLocationStaticScript(probe, false);
            if (road == Vector3.Zero || Geo.FlatDistance(road, probe) > RoadSnap)
                continue;

            Vector3 spot = road + new Vector3(0f, 0f, RoadHeight);
            if (Geo.FlatDistance(spot, centre) > EngageRange + Standoff || !SpotClear(spot, t, RoadRoom, Foes.Turret(target) ? Weapons.TurretSlack : 0f))
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

    // Tries the current angle first, then 30 degrees either side, further and further round, for a spot with
    // room for the wings and a clear line to whoever it watches. Higher spots get fewer angles to keep the
    // traces down. Under a roof there may be no line to him at all, so after that any spot with room for the
    // wings will do. If nothing is clear it keeps moving round.
    private float ClearAngle(Vector3 centre, Actor? target, float angle)
    {
        Vector3 watch = target?.Location ?? centre + new Vector3(0f, 0f, 300f);
        float slack = target != null && Foes.Turret(target) ? Weapons.TurretSlack : 0f;
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (float raise in Raises)
            {
                int tries = raise == 0f ? 12 : 6;
                for (int i = 0; i <= tries; i++)
                {
                    float step = (i + 1) / 2 * SpotStep * (i % 2 == 1 ? 1f : -1f);
                    float a = angle + step;
                    if (!SpotClear(SpotFor(centre, target, a, raise), pass == 0 ? watch : null, SpotRoom, slack))
                        continue;

                    _raise = raise;
                    return a;
                }
            }
        }

        _raise = Raises[^1];
        return angle + SpotStep;
    }

    // A clear line to whoever it watches, nothing through the spot itself, and room round it for the wings.
    private bool SpotClear(Vector3 spot, Vector3? watch, float spare, float slack = 0f)
    {
        Actor tracer = _craft.Actor;
        if (watch is { } w && Ground.Ray(tracer, spot, w, out Vector3 block, out _) && Vector3.Distance(block, w) > slack)
            return false;
        if (_craft.Swept(spot + new Vector3(0f, 0f, 300f), spot - new Vector3(0f, 0f, 300f)))
            return false;

        float room = _craft.Span + spare;
        for (int i = 0; i < 8; i++)
        {
            if (!Ground.Clear(tracer, spot, spot + Geo.Facing(i * 45f) * room))
                return false;
        }

        return true;
    }

    // Short rays all round. Anything they touch pushes it away, harder the closer it is.
    private void Feel()
    {
        Vector3 at = _fm.Position;
        float span = _craft.Span;
        Vector3 push = Vector3.Zero;
        foreach (Vector3 d in FeelDirs)
        {
            Actor? what = _craft.Actor.Trace(out Vector3 hit, out Vector3 face, at + d * (span + AvoidRange), at, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0);
            if (what == null || hit == Vector3.Zero || _craft.IsCargo(what))
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

    private FlightInput Autopilot(Vector3 centre, RPawnPlayer batman, float dt)
    {
        Vector3 at = _fm.Position;

        // Catching an eject or hauling the Batmobile, it goes where that needs and fights nothing.
        Vector3? job = Catching && World.Pawn is RPawnPlayer ? CatchSpot(batman) : HaulSpot();
        Actor? target = _pickup || job != null ? null : Target();

        // A chased vehicle pulls the Batwing away from Batman, so the spot is kept near the vehicle instead.
        bool chase = Chasing(target);
        Vector3 station = job ?? (_pickup ? PickupSpot(centre) : Station(chase ? target!.Location : centre, target, dt));
        float gap = Vector3.Distance(station, at);

        // Going down to a lower spot it gets over it first and then drops straight in. The air above a road is
        // clear, a slanted line down into a street usually isn't.
        Vector3 goal = station.Z < at.Z - 500f && Geo.FlatDistance(station, at) > 1500f ? new Vector3(station.X, station.Y, at.Z) : station;
        Vector3 to = goal - at;
        float toLen = to.Length();
        if (_feel.Elapsed(0.1))
            Feel();

        // Climb only when something sits between it and where it's going, or right in front of it. Looking
        // further along the path made it climb away from targets down on the street.
        if (_ahead.Elapsed(0.1))
        {
            Vector3 path = _fm.Velocity.LengthSquared() > 100f ? Vector3.Normalize(_fm.Velocity) : _fm.Forward;
            float near = MathF.Min(Math.Clamp(_fm.Velocity.Length() * 0.8f, 600f, 6000f), toLen);
            _blocked = (toLen > 300f && _craft.Swept(at, goal)) || _craft.Swept(at, at + path * near);
        }

        // Hauling, a blocked path raises the cruise height a bit at a time instead.
        if (Hauling)
        {
            _climb = 0f;
            if (_blocked && _haul == Haul.Cruise)
                _cruiseZ = MathF.Max(_cruiseZ, at.Z + 1000f * dt * 4f);
        }
        else
        {
            _climb = _blocked ? MathF.Min(_climb + 1500f * dt * 4f, ClimbMost) : MathF.Max(0f, _climb - 800f * dt);
        }

        if (job != null)
            Unstick(Catching ? batman.Location : HaulHome(), gap, dt, Catching || (_haul == Haul.Cruise && Carrying && _blocked), canLose: false);
        else
            Unstick(centre, gap, dt, chase && target!.Velocity.Length() > ChaseMoving, canLose: true);

        // Wings only to catch up, with Batman or a vehicle it's chasing. Never for a target it's fighting or a
        // pickup, since a jet can't stop over a spot. With the car hooked it flies like a helicopter all the way.
        float across = Geo.FlatDistance(station, at);
        bool holdStill = Catching || Carrying || _haul is Haul.Climb or Haul.Lower;
        bool hover = (target != null && !chase) || _pickup || holdStill || (_fm.HoverWanted ? across < WingsFrom : across < HoverWithin);
        if (hover != _fm.HoverWanted)
        {
            _fm.HoverWanted = hover;
            if (hover)
            {
                _craft.HoverSounds();
                _craft.Flare(1.2f);
            }
            else
            {
                _craft.WingSounds();
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
            return new FlightInput { Aim = aim, Assist = Assist.Full, Boost = gap > BoostFrom && !Carrying, Heading = _fm.Yaw() };
        }

        // Faces the target, or the way it's going, or Batman once it's settled. On a job it keeps its heading
        // once it's close, so it doesn't spin round over the car.
        float heading = _fm.Yaw();
        Vector3 face = target != null ? target.Location - at : gap > 1500f ? to : job != null ? Vector3.Zero : centre - at;
        if (_pickup && gap < 1500f)
            heading = _pickupYaw;
        else if (face.X * face.X + face.Y * face.Y > 100f)
            heading = MathF.Atan2(face.Y, face.X);

        var fwd = new Vector3(MathF.Cos(heading), MathF.Sin(heading), 0f);
        var side = new Vector3(-fwd.Y, fwd.X, 0f);
        var flatTo = new Vector3(to.X, to.Y, 0f);

        // Something in the way on the trip across, so it stops and climbs in place until it's clear.
        bool climbOut = _haul == Haul.Cruise && Carrying && _blocked;
        if (climbOut)
            flatTo = Vector3.Zero;

        // The drop spot was already checked for wing room, so walls round it only nudge it there.
        bool straight = _haul is Haul.Climb or Haul.Lower;
        Vector3 want = flatTo * StationGain + new Vector3(_avoid.X, _avoid.Y, 0f) * (AvoidPush * (straight ? 0.2f : 1f));
        float most = FlightModel.HoverSpeed * 1.8f;
        if (want.Length() > most)
            want = Vector3.Normalize(want) * most;
        bool fast = want.Length() > FlightModel.HoverSpeed;
        float scale = FlightModel.HoverSpeed * (fast ? 1.8f : 1f);
        return new FlightInput
        {
            Move = new Vector2(Vector3.Dot(want, side) / scale, Vector3.Dot(want, fwd) / scale),

            // Past -1 the hover model just asks for a faster drop, still capped by its own limits. Going straight
            // up or down with the car the walls round it don't get a say in its height.
            Lift = climbOut ? 1.5f : Math.Clamp(to.Z / 1500f + (straight ? 0f : _avoid.Z), _haul == Haul.Lower ? -3.5f : -2.5f, 1.5f),
            Boost = fast,
            Heading = heading,
            Assist = Assist.Full,
        };
    }

    // It can wedge itself under something or lose Batman entirely. If it stops closing on its spot for a while,
    // it's put back over him.
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
            Log.Info("Escort: stuck at " + Geo.Position(_fm.Position) + " with the Batmobile hooked, cruise height now " + Fmt.Metres(_cruiseZ) + ".");
            return;
        }

        _climb = 0f;
        var to = new Vector3(centre.X, centre.Y, centre.Z + StationHeight + 2000f);
        if (World.Player is { IsValid: true } p && Ground.Ray(p, centre + new Vector3(0f, 0f, 100f), to, out Vector3 roof, out _))
            to = roof - new Vector3(0f, 0f, 400f);
        Log.Info("Escort: " + (lost ? "lost Batman" : "stuck") + " at " + Geo.Position(_fm.Position) + ", back over him at " + Geo.Position(to) + ".");
        _fm.Place(to, _fm.Yaw(), Vector3.Zero);
        _fm.Hover = 1f;
        _fm.HoverWanted = true;
        _craft.Follow();
    }
}
