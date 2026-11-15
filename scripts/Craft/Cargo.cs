using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// The Batmobile hooked under the Batwing. Its physics is off while it hangs and it's moved with the craft
// every frame. Its collision stays on: turning that off and on again left the car frozen in the air after
// letting go, so the craft's own traces skip the car instead.
internal sealed class Cargo
{
    private const string HeldKey = "batwing.cargo";

    // A little under zero. The model bounds still run a touch above the car's roof, and the two never collide.
    private const float HangGap = -20f;

    public readonly RVehicleBatmobileBase Car;

    // Craft middle to car origin, the car's roof over its origin, and how high the car sits when parked.
    public readonly float Hang;
    public readonly float Top;
    public readonly float Rest;
    private readonly Vector3 _box;

    public bool Alive => Car is { IsValid: true, bDeleteMe: false };

    private Cargo(RVehicleBatmobileBase car, float hang, float top, float rest, Vector3 box)
    {
        Car = car;
        Hang = hang;
        Top = top;
        Rest = rest;
        _box = box;
    }

    // How far under the craft's middle the car will hang, worked out before it's hooked.
    public static float HangFor(Craft craft, RVehicleBatmobileBase car)
    {
        float roof = Roof(car, out string how);
        float hang = craft.Belly + roof + HangGap;
        Log.Detail("Lift: hang " + Fmt.Whole(hang) + ", belly " + Fmt.Whole(craft.Belly) + ", car roof " + Fmt.Whole(roof) + " (" + how + ").");
        return hang;
    }

    public static Cargo Hook(Craft craft, RVehicleBatmobileBase car)
    {
        float top = Roof(car, out _);
        float hang = craft.Belly + top + HangGap;
        float rest = RestHeight(car);
        var box = new Vector3(160f, 160f, 110f);
        if (car.Mesh is { IsValid: true } mesh)
        {
            Vector3 extent = mesh.Bounds.BoxExtent;
            float side = Math.Clamp(MathF.Min(extent.X, extent.Y) * 0.8f, 80f, 220f);
            box = new Vector3(side, side, Math.Clamp(extent.Z * 0.8f, 60f, 160f));
        }

        Safe.Run("batwing hook", () =>
        {
            car.SetPhysics(Actor.EPhysics.PHYS_None, false);
            car.Velocity = Vector3.Zero;
            car.AngularVelocity = Vector3.Zero;
        });

        Shared.SetFlag(HeldKey, true);
        Craft.Play(craft.Kit.Land, car);
        Rumble.Play(Buzz.Hook);
        Log.Info("Lift: hooked the Batmobile at " + Geo.Position(car.Location) + ", hanging " + Fmt.Whole(hang) + " below, resting " + Fmt.Whole(rest) + " off the ground.");
        return new Cargo(car, hang, top, rest, box);
    }

    // Keeps the car under the belly. Runs from the craft's Place, so every move carries it.
    public void Hold(Vector3 at, Vector3 fwd, Vector3 right, Vector3 up)
    {
        if (!Alive)
            return;

        Vector3 spot = at - up * Hang;
        Rotator rot = Geo.FromAxes(fwd, right, up);
        Safe.Run("batwing cargo", () =>
        {
            // Something like an eject can turn its physics back on.
            if (Car.Physics != Actor.EPhysics.PHYS_None)
                Car.SetPhysics(Actor.EPhysics.PHYS_None, false);
            Car.SetLocation(spot);
            Car.SetRotation(rot);
            Car.Velocity = Vector3.Zero;
            if (Car.Mesh is { IsValid: true } mesh)
            {
                mesh.SetRBPosition(spot);
                mesh.SetRBRotation(rot);
            }
        });
    }

    // Lets go. Gently puts it down on the ground under it, standing still. Otherwise it falls with the speed it had.
    public void Drop(Vector3 velocity, bool gently)
    {
        Shared.SetFlag(HeldKey, false);
        if (!Alive)
            return;

        Vector3 at = Car.Location;
        Rotator rot = Car.Rotation;
        if (gently)
        {
            if (Floor(Car, 60000f, out Vector3 floor))
                at = new Vector3(at.X, at.Y, floor.Z + Rest + 10f);
            rot = new Rotator(0f, rot.Yaw, 0f);
            velocity = Vector3.Zero;
        }

        RVehicleBatmobileBase car = Car;
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

        CargoWatch.Released(car, Rest, falling: !gently);
        Log.Info("Lift: let go of the Batmobile at " + Geo.Position(at) + (gently ? ", set down." : ", dropped at " + Fmt.Whole(velocity.Length() / Geo.Metre) + " m/s."));
    }

    // Close enough to the ground under it to put it down rather than drop it.
    public bool Low(float slack) => Floor(Car, Rest + slack, out _);

    // The car's box swept along with the craft. Moves the craft back if the car hit something. True when it
    // came down on something flat.
    public bool Collide(Craft craft, Vector3 from, Vector3 to, out float into, out bool flat)
    {
        into = 0f;
        flat = false;
        if (!Alive || Vector3.DistanceSquared(from, to) < 0.01f)
            return false;

        Vector3 drop = craft.Up * Hang;
        Actor? hit = Car.Trace(out Vector3 at, out Vector3 n, to - drop, from - drop, Component.ECollisionFilter.COF_TraceWorld, _box, out _, 0);
        if (hit == null || at == Vector3.Zero || n.LengthSquared() < 0.5f)
            return false;

        craft.Fm.Position = at + n * 4f + drop;
        into = -Vector3.Dot(craft.Fm.Velocity, n);
        if (into > 0f)
            craft.Fm.Velocity += n * into * 1.1f;
        craft.Fm.Release();
        flat = n.Z > 0.7f;
        return true;
    }

    public bool Swept(Vector3 from, Vector3 to)
    {
        if (!Alive)
            return false;

        var drop = new Vector3(0f, 0f, Hang);
        Actor? hit = Car.Trace(out Vector3 at, out _, to - drop, from - drop, Component.ECollisionFilter.COF_TraceWorld, _box, out _, 0);
        return hit != null && at != Vector3.Zero;
    }

    // The top of the car model in the car's own frame, from the model's own bounds. The world bounds the
    // engine keeps are padded and left a big gap, and rays straight down don't hit the car at all.
    private static float Roof(RVehicleBatmobileBase car, out string how)
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

    private static float RestHeight(RVehicleBatmobileBase car)
    {
        if (Floor(car, 1500f, out Vector3 floor) && car.Location.Z - floor.Z is > 20f and < 400f and var rest)
            return rest;

        return Math.Clamp(car.GetCollisionHeight(), 60f, 200f);
    }

    public static bool Floor(RVehicleBatmobileBase car, float reach, out Vector3 floor) =>
        Ground.Ray(car, car.Location, car.Location - new Vector3(0f, 0f, reach), out floor, out _);

    // A script reload mid carry leaves the car frozen in the air.
    public static void Recover()
    {
        if (!Shared.Flag(HeldKey))
            return;

        Shared.SetFlag(HeldKey, false);
        if (Batmobile.Current() is not { IsValid: true } car || car.Physics != Actor.EPhysics.PHYS_None)
            return;

        Log.Info("Lift: scripts reloaded mid carry, letting the Batmobile go.");
        car.SetPhysics(Actor.EPhysics.PHYS_RigidBody, false);
        if (car.Mesh is { IsValid: true } mesh)
            mesh.WakeRigidBody();
    }
}

// Watches the Batmobile for a moment after it's let go. It turns physics back on if the car was left frozen,
// and a hard landing from a drop wrecks vehicles and knocks down thugs where it comes down.
internal static class CargoWatch
{
    private const float HurtSpeed = 1500f;
    private const double WatchSeconds = 10.0;
    private const double SettleSeconds = 1.5;

    private static RVehicleBatmobileBase? _settling, _falling;
    private static double _settleAt, _fallSince;
    private static float _settleZ, _fastest, _rest;

    public static void Released(RVehicleBatmobileBase car, float rest, bool falling)
    {
        _settling = car;
        _settleAt = Clock.Now + SettleSeconds;
        _settleZ = car.Location.Z;
        _rest = rest;
        if (!falling)
            return;

        _falling = car;
        _fallSince = Clock.Now;
        _fastest = 0f;
    }

    public static void Tick()
    {
        Safe.Run("batmobile settle", Settle);
        Safe.Run("batmobile fall", Fall);
    }

    public static void Forget()
    {
        _settling = null;
        _falling = null;
    }

    private static void Settle()
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
        Log.Detail("Lift: Batmobile after letting go, physics " + physics + ", moved " + Fmt.Whole(car.Location.Z - _settleZ) + " up and down.");
        if (physics == Actor.EPhysics.PHYS_RigidBody)
            return;

        Log.Info("Lift: the Batmobile was left frozen, turning its physics back on.");
        car.SetPhysics(Actor.EPhysics.PHYS_RigidBody, false);
        if (car.Mesh is { IsValid: true } mesh)
            mesh.WakeRigidBody();
    }

    private static void Fall()
    {
        if (_falling is not { IsValid: true, bDeleteMe: false } car)
        {
            _falling = null;
            return;
        }

        double t = Clock.Now - _fallSince;
        float down = -car.Velocity.Z;
        _fastest = MathF.Max(_fastest, down);
        bool near = t > 0.15 && Cargo.Floor(car, _rest + 150f, out _);
        bool stopped = t > 0.4 && _fastest > 600f && down < _fastest * 0.3f;
        if (!near && !stopped)
        {
            if (t > WatchSeconds)
                _falling = null;
            return;
        }

        _falling = null;
        if (_fastest >= HurtSpeed && Opt.DropDamage.On)
            Impact(car, _fastest);
    }

    private static void Impact(RVehicleBatmobileBase car, float speed)
    {
        Vector3 at = car.Location;
        float radius = Math.Clamp(500f + speed * 0.4f, 800f, 2500f);
        float inner = radius * 0.4f;
        if (World.Pc is not { } pc || World.Player is not { IsValid: true } batman)
            return;

        int hits = 0;
        foreach (RVehicleNPC v in World.All<RVehicleNPC>())
        {
            if (!Foes.Is(v))
                continue;
            float d = Vector3.Distance(v.Location, at);
            if (d > radius)
                continue;

            int damage = d < inner ? v.Health + 1 : Math.Max(1, (int)(v.Health * 0.5f));
            Vector3 push = Vector3.Normalize(v.Location - at + new Vector3(0f, 0f, 100f)) * 1500f;
            Safe.Run("drop damage", () => v.TakeDamageFromWeapon(damage, pc, v.Location, push, Vector3.Zero, RDmgType_BatmobileChainGun.StaticClass(), default, batman, false));
            hits++;
        }

        if (Batmobile.Weapon<RBatmobileCannon>() is { IsValid: true } vulcan)
        {
            foreach (RPawnVillain thug in World.All<RPawnVillain>())
            {
                if (!Foes.CanStrike(thug) || Vector3.Distance(thug.Location, at) > radius)
                    continue;
                Vector3 away = thug.Location - at;
                away.Z = 0f;
                Safe.Run("drop knock", () => Foes.Knock(thug, away.LengthSquared() > 1f ? Vector3.Normalize(away) : Vector3.UnitX, batman, vulcan));
                hits++;
            }
        }

        if (Kit.Find<ParticleSystem>("VFX_Batmobile.Particles.Batmobile_HeavyCannon_Impact") is { } fx)
            Craft.Burst(fx, at, Vector3.UnitZ);
        Log.Info("Lift: Batmobile came down at " + Fmt.Whole(speed / Geo.Metre) + " m/s at " + Geo.Position(at) + ", hit " + hits + ".");
        if (hits > 0)
            Toasts.Show("BATMOBILE DROP // " + hits + " HIT", true);
    }
}
