using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Escort
{
    private const float EngageRange = 15000f;
    private const float StrikeCone = 0.985f;
    private const float StrikeReach = 12000f;
    private const double StrikeSeconds = 12.0;
    private const double ToughPatience = 30.0;
    private const int MarksMost = 6;
    private const float ChaseReach = 25000f;
    private const float ChaseLeash = 30000f;
    private const float ChaseLead = 0.8f;
    private const float HullSlack = 600f;
    private const float GunFront = 0.9f;
    private const double CannonEvery = 2.5, CannonEveryHeavy = 1.5;
    private const double BarrageEvery = 8.0, BarrageEveryHeavy = 4.0;
    private const double LockHold = 1.4;

    private Actor? _escortFoe;
    private int _foesNear;
    private Actor? _strike;
    private Actor? _strikeAim;
    private readonly List<Actor> _marks = new();
    private readonly List<Actor> _queue = new();
    private bool _marking;
    private bool _targetSeen;
    private double _strikeUntil;
    private double _nextCannon, _nextBarrage, _lockUntil;
    private Every _escortPick, _strikeLook;

    public Actor? StrikeTarget => _strike is { IsValid: true } s ? s : null;

    public Actor? StrikeAim => _strikeAim is { IsValid: true } a ? a : null;

    public Actor? Engaging => _escortFoe is { IsValid: true } f && Foes.Is(f) ? f : null;

    public IReadOnlyList<Actor> Numbered => _marking ? _marks : _queue;

    public bool Sweeping => _marking;

    public bool Striking => _strike != null;

    // Hits whatever is nearest the middle of the screen. A thug in a vehicle means the vehicle, and a vehicle
    // gets chased until it's destroyed. Holding the key marks everything the crosshair passes over, and the
    // strikes go in the order they were marked.
    public void Strike()
    {
        if (_marking)
            return;
        if (Hauling)
            throw new Refusal("busy with the Batmobile");

        _marking = true;
        _marks.Clear();
        Mark();
    }

    private void Mark()
    {
        if (_strikeAim is not { IsValid: true } aim || !Foes.Strikeable(aim) || _marks.Count >= MarksMost || _marks.Exists(m => m.Ptr == aim.Ptr))
            return;

        _marks.Add(aim);
        _craft.Sound(Batmobile.Weapon<RBatmobileMissileLauncher>()?.LockOnSound);
    }

    // Keeps marking while the key is down, and hands the marks over when it comes up.
    private void Marking()
    {
        if (!_marking)
            return;

        if (Hotkeys.StrikeHeld)
        {
            Mark();
            return;
        }

        _marking = false;
        _marks.RemoveAll(m => !m.IsValid || !Foes.Strikeable(m));
        if (_marks.Count == 0)
        {
            Wing.Toast("NO TARGET");
            return;
        }

        _queue.Clear();
        _queue.AddRange(_marks.Skip(1));
        Actor first = _marks[0];
        int count = _marks.Count;
        _marks.Clear();
        Wing.Toast(count > 1 ? "STRIKE // " + count + " TARGETS" : first is RPawnVillain ? "STRIKE" : "STRIKE ON " + Foes.Kind(first));
        BeginStrike(first);
    }

    private void BeginStrike(Actor aim)
    {
        _strike = aim;
        _strikeUntil = Clock.Now + StrikeSeconds;
        _targetSeen = false;
        Log.Detail("Strike: on " + aim.Name + " (" + Foes.Kind(aim).ToLowerInvariant() + ") at " + Geo.Position(aim.Location) + ".");
    }

    // One strike is over. On to the next marked target that's still worth hitting.
    private void EndStrike(string why)
    {
        _strike = null;
        while (_queue.Count > 0)
        {
            Actor next = _queue[0];
            _queue.RemoveAt(0);
            if (!next.IsValid || !Foes.Strikeable(next))
                continue;

            Wing.Toast(why + " // NEXT TARGET");
            BeginStrike(next);
            return;
        }

        Wing.Toast(why);
    }

    private Actor? StrikeCandidate(Vector3 batman)
    {
        if (CameraHook.GameView() is not { } pov || World.Player is not { } me)
            return null;

        Vector3 eye = pov.Location;
        Vector3 look = Geo.Dir(pov.Rotation.Yaw, pov.Rotation.Pitch);
        Actor? best = null;
        float bestDot = StrikeCone;
        foreach (RPawnVillain thug in World.All<RPawnVillain>())
        {
            // Only thugs that can really be seen. The game parks some out of sight, under the streets or in
            // interiors, with their tick off.
            if (!Foes.CanStrike(thug) || thug.bTickIsDisabled || Vector3.DistanceSquared(thug.Location, batman) > StrikeReach * StrikeReach
                || !Ahead(eye, look, thug.Location, bestDot, out float dot))
                continue;

            if (_craft.Weapons.Ride(thug) is { } ride)
            {
                if (Foes.Is(ride) && Sight.Clear(me, eye, thug.Location, HullSlack))
                {
                    bestDot = dot;
                    best = ride;
                }
            }
            else if (Sight.Clear(me, eye, thug.Location, 0f))
            {
                bestDot = dot;
                best = thug;
            }
        }

        foreach (Actor foe in _craft.Weapons.Near)
        {
            if (Foes.Is(foe) && Vector3.DistanceSquared(foe.Location, batman) < ChaseReach * ChaseReach
                && Ahead(eye, look, foe.Location, bestDot, out float dot) && Sight.Clear(me, eye, foe.Location, HullSlack))
            {
                bestDot = dot;
                best = foe;
            }
        }

        return best;
    }

    private static bool Ahead(Vector3 eye, Vector3 look, Vector3 at, float least, out float dot)
    {
        Vector3 to = at - eye;
        float dist = to.Length();
        dot = dist < 1f ? -1f : Vector3.Dot(to / dist, look);
        return dot > least;
    }

    // Only something that drives off is chased. A turret stays put, so it gets a firing spot like a thug.
    private bool Chasing(Actor? target) => target != null && _strike != null && target.Ptr == _strike.Ptr && target is not RPawnVillain && !Foes.Turret(target);

    // Where to watch a target from. A chased vehicle is led a little so the Batwing keeps up.
    private Vector3 Watched(Actor target) => Chasing(target) ? target.Location + target.Velocity * ChaseLead : target.Location;

    private void StrikeCheck(Vector3 centre)
    {
        if (_strike == null)
            return;

        // A thug who got into a vehicle takes the vehicle down with him.
        if (_strike is RPawnVillain { IsValid: true } thug && Foes.Alive(thug) && _craft.Weapons.Ride(thug) is { } ride && Foes.Is(ride))
            _strike = ride;

        if (!_strike.IsValid || !Foes.Strikeable(_strike))
        {
            bool down = !_strike.IsValid || _strike.bDeleteMe || _strike is Pawn { Health: <= 0 } || _strike is RPawnVillain || Foes.TurretDown(_strike);
            EndStrike(down ? "TARGET DOWN" : "TARGET LOST");
            return;
        }

        if (_strike is not RPawnVillain)
        {
            if (Geo.FlatDistance(_strike.Location, centre) > ChaseLeash)
            {
                EndStrike("TARGET GOT AWAY");
                return;
            }

            // A vehicle is chased as long as it stays in sight. APCs and tough ones get longer to come back into view.
            if (_targetSeen)
                _strikeUntil = Math.Max(_strikeUntil, Clock.Now + (Foes.Apc(_strike) || Foes.HeavyTank(_strike) ? ToughPatience : StrikeSeconds));
        }

        if (Clock.Now > _strikeUntil)
        {
            Log.Detail("Strike: on " + _strike.Name + " ran out, target " + (_targetSeen ? "in sight." : "not in sight."));
            EndStrike("NO CLEAR SHOT");
        }
    }

    // Picks from the weapons' own scan. Order only targets wait for a strike.
    private void PickFoe(Vector3 centre)
    {
        Actor? best = null;
        float bestDist = EngageRange * EngageRange;
        int near = 0;
        foreach (Actor foe in _craft.Weapons.Near)
        {
            if (!Foes.Is(foe) || Foes.OrderOnly(foe))
                continue;
            float d = Vector3.DistanceSquared(foe.Location, centre);
            if (d > EngageRange * EngageRange)
                continue;
            near++;
            if (d < bestDist && _craft.Actor.FastTrace(foe.Location, _fm.Position, Vector3.Zero, false))
            {
                bestDist = d;
                best = foe;
            }
        }

        _foesNear = near;
        if (best != null && (_escortFoe == null || best.Ptr != _escortFoe.Ptr))
            Log.Detail("Escort: engaging " + best.Name + ".");
        _escortFoe = best;
    }

    private Actor? Target()
    {
        if (_strike is { IsValid: true } s && Foes.Strikeable(s))
            return s;
        return _escortFoe is { IsValid: true } f && Foes.Is(f) ? f : null;
    }

    private Orders Orders()
    {
        var orders = new Orders { Look = _fm.Forward, Free = false, Homing = true };
        if (Target() is not { } target)
            return orders;

        Vector3 to = target.Location - _fm.Position;
        float dist = to.Length();
        if (dist < 1f)
            return orders;

        // Hovering it only turns flat, so in front means facing it, whatever the height difference.
        Vector3 dir = to / dist;
        float front = Vector3.Dot(Geo.Flat(dir), Geo.Flat(_fm.Forward));
        Vector3 nose = _fm.Position + dir * (_craft.Half.X + 100f);
        _targetSeen = _craft.Weapons.Sees(nose, target);
        orders.Look = dir;
        orders.Target = target;
        orders.Gun = _targetSeen && front > GunFront && dist < Weapons.GunRange * 0.5f;
        if (!_targetSeen || target is RPawnVillain)
            return orders;

        // Guns don't bring down anything flying, so those get the cannon more often and missiles even alone.
        // So does a vehicle Batman ordered a strike on, and a struck turret.
        bool ordered = Chasing(target) || Foes.Turret(target);
        bool heavy = Foes.Flying(target) || ordered;
        double now = Clock.Now;
        if (front > 0.9f && now >= _nextCannon && Batmobile.Weapon<RBatmobileHeavyCannon>()?.ProjectileArchetype is { IsValid: true })
        {
            orders.Cannon = true;
            _nextCannon = now + (heavy ? CannonEveryHeavy : CannonEvery);
        }

        if ((heavy || _foesNear >= 2) && front > 0.8f && now >= _nextBarrage && Batmobile.Weapon<RBatmobileMissileLauncher>()?.MissileClass is { IsValid: true })
        {
            _lockUntil = now + LockHold;
            _nextBarrage = now + (heavy ? BarrageEveryHeavy : BarrageEvery);
        }

        orders.Lock = now < _lockUntil;
        orders.Stack = orders.Lock && ordered;
        return orders;
    }
}
