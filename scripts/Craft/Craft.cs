using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// One Batwing out in the world: the actor, its model as measured, its effects and sounds and its flight
// model. A new one is made for each call, so nothing is left over from the last.
internal sealed partial class Craft
{
    public static readonly FName Tag = new("TrinityBatwing");

    private static readonly FName LeftFront = new("L_F_Jet_Bone"), RightFront = new("R_F_Jet_Bone");
    private static readonly FName LeftRear = new("L_R_Jet_Bone"), RightRear = new("R_R_Jet_Bone");
    private static readonly FName CockpitBone = new("Batwing_Cockpit"), TailBone = new("MAIN_Tail");
    private static readonly FName LeftWing = new("L_OUTER_WING"), RightWing = new("R_OUTER_WING");
    private static readonly FName LeftEngine = new("L_OUTER_WING_Engine"), RightEngine = new("R_OUTER_WING_Engine");
    private static readonly FName SeatBone = new("SEATING");
    private static readonly FName Chassis = new("Chassie");

    private static readonly Vector3 ExhaustOffset = new(-391.7f, -6.0f, -95.6f);
    private static readonly Vector3 ShowcaseExhaustOffset = new(-467.4f, -0.1f, -95.2f);
    private static readonly Vector3 ShowcaseExhaustScale = new(1.1f, 1f, 1f);
    private static readonly Vector3 TipOffset = new(-9.2f, 264f, -6.1f);
    private const float ExhaustYaw = -90f;
    private const float ShowcaseExhaustRoll = 1.4f;
    private const float ContrailSpread = 450f, ContrailAhead = 32f, ContrailBelow = 49f;

    public readonly Kit Kit;
    public readonly RBatwing Actor;
    public readonly FlightModel Fm = new();
    public readonly Weapons Weapons;
    public readonly Drift Drift = new();

    private readonly SkeletalMeshComponent _body;
    private bool _fitted;
    private int _fitTries;

    // The model as measured off its bones, in the craft's own frame. The guesses hold until it's fitted.
    public Vector3 Seat { get; private set; } = new(590f, 0f, 30f);
    public Vector3 PodLeft { get; private set; } = new(-170f, -530f, 50f);
    public Vector3 PodRight { get; private set; } = new(-170f, 530f, 50f);
    public Vector3 Tail { get; private set; } = new(-500f, 0f, 0f);
    public Vector3 Jets { get; private set; } = new(250f, 400f, 0f);

    // Half its size along each axis, roughly the box it fills.
    public Vector3 Half { get; private set; } = new(700f, 800f, 160f);

    // How far the bottom of the model sits under its middle. The Batmobile hangs just under it.
    public float Belly { get; private set; } = 96f;

    private Vector3 _exhaustAt = new(-336.4f, -6f, 53.6f);
    private Rotator _exhaustTurn = new(0f, ExhaustYaw, 0f);
    private Vector3 _exhaustScale = Vector3.One;
    private Vector3 _contrailAt = new(-400f, 0f, 0f);
    private float _contrailScale = 1.8f;

    // The frame it was last placed in, before the hover drift.
    public Vector3 Fwd { get; private set; } = Vector3.UnitX;
    public Vector3 Right { get; private set; } = Vector3.UnitY;
    public Vector3 Up { get; private set; } = Vector3.UnitZ;

    public Probe Below { get; private set; } = new(Probe.Reach, Vector3.Zero, Vector3.UnitZ);

    public Cargo? Cargo { get; set; }

    public bool Carrying => Cargo is { Alive: true };

    public bool Alive => Actor is { IsValid: true, bDeleteMe: false };

    public Vector3 Position => Fm.Position;

    // The biggest of its width and length, for room checks.
    public float Span => MathF.Max(Half.X, Half.Y);

    private Craft(Kit kit, RBatwing actor, SkeletalMeshComponent body)
    {
        Kit = kit;
        Actor = actor;
        _body = body;
        Weapons = new Weapons(this);
    }

    public static Craft Spawn(Kit kit, Vector3 at, float yawDegrees)
    {
        Pawn spawner = World.Pawn ?? throw new Refusal("the game is still loading");
        Actor? made = spawner.Spawn(RBatwing.StaticClass(), null!, Tag, at, new Rotator(0f, 0f, 0f), null!, true);
        if (made is not RBatwing { IsValid: true } actor)
            throw new Refusal("the Batwing couldn't be put there");

        actor.SetPhysics(BmSDK.Engine.Actor.EPhysics.PHYS_None, false);
        actor.SetCollision(false, false, true);
        actor.bCollideWorld = false;

        if (actor.SkeletalMeshComponent is not { IsValid: true } body)
        {
            Safe.Run("batwing remove", () => actor.Destroy());
            throw new Refusal("the Batwing has no body");
        }

        var craft = new Craft(kit, actor, body);
        craft.Dress();
        craft.Fm.Place(at, yawDegrees / Geo.Deg, Vector3.Zero);
        actor.SetRotation(new Rotator(0f, yawDegrees, 0f));
        craft.BuildEffects();
        craft.Follow();
        return craft;
    }

    // Leftover Batwings from before a script reload.
    public static void ClearLeftovers()
    {
        foreach (RBatwing craft in World.All<RBatwing>())
        {
            if (craft.Tag.Index == Tag.Index && !craft.bDeleteMe)
                Safe.Run("batwing leftover", () => craft.Destroy());
        }
    }

    private void Dress()
    {
        Drift.Reset();
        Safe.Run("batwing pose", () =>
        {
            if (Kit.Fold is not { IsValid: true } && Kit.Pose is not { IsValid: true })
                return;

            var node = new AnimNodeSequence(_body) { bZeroRootTranslation = true, bZeroRootRotation = true };
            _body.Animations = node;
            if (Kit.Fold is { IsValid: true } fold)
                _body.AnimSets.Push(fold);
            if (Kit.Pose is { IsValid: true } flex)
                _body.AnimSets.Push(flex);
            _pose = node;
        });

        _body.SetSkeletalMesh(Kit.Mesh, false);
        _fold = Kit.Fold is { IsValid: true };
        if (_pose is { } posed)
        {
            Safe.Run("batwing pose", () =>
            {
                posed.SetAnim(_fold ? FoldAnim : PoseAnim);
                posed.bPlaying = false;
                posed.SetPosition(_fold ? FoldFrom : 0f, false);
            });
        }

        Fit();
    }

    // The model's pivot isn't its middle and its axes aren't the craft's, so it's measured off its bones
    // and turned and moved to sit square on the actor. Bones may not be ready on the first frame.
    private void Fit()
    {
        if (_fitted || !_body.IsValid)
            return;

        _body.SetRotation(new Rotator(0f, 0f, 0f));
        _body.SetTranslation(Vector3.Zero);
        _body.ForceSkelUpdate();

        Vector3? lf = Bone(LeftFront), rf = Bone(RightFront), lr = Bone(LeftRear), rr = Bone(RightRear);
        Vector3? cockpit = Bone(CockpitBone), tail = Bone(TailBone);
        Vector3? lw = Bone(LeftWing) ?? Bone(LeftEngine), rw = Bone(RightWing) ?? Bone(RightEngine);
        Vector3? chassis = Bone(Chassis), seat = Bone(SeatBone), podL = Bone(LeftEngine), podR = Bone(RightEngine);

        Vector3 fwd = Vector3.UnitX, right = Vector3.UnitY, centre = Vector3.Zero;
        string how;
        if (lf is { } a && rf is { } b && lr is { } c && rr is { } d && Vector3.DistanceSquared(a + b, c + d) > 100f)
        {
            fwd = Vector3.Normalize(a + b - c - d);
            right = a + c - b - d == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(b + d - a - c);
            centre = (a + b + c + d) * 0.25f;
            how = "jets";
        }
        else if (cockpit is { } front && tail is { } back && Vector3.DistanceSquared(front, back) > 100f)
        {
            fwd = Vector3.Normalize(front - back);
            right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, fwd));
            centre = (front + back) * 0.5f;
            how = "cockpit and tail";
        }
        else if (++_fitTries < 30)
        {
            return;
        }
        else
        {
            how = "nothing (no bones found)";
        }

        right = Vector3.Normalize(right - fwd * Vector3.Dot(right, fwd));
        Vector3 up = Vector3.Cross(fwd, right);
        if (up.Z < 0f)
        {
            right = -right;
            up = -up;
        }

        var x = new Vector3(fwd.X, right.X, up.X);
        var y = new Vector3(fwd.Y, right.Y, up.Y);
        var z = new Vector3(fwd.Z, right.Z, up.Z);
        _body.SetRotation(Geo.FromAxes(x, y, z));
        _body.SetTranslation(-(x * centre.X + y * centre.Y + z * centre.Z));

        Vector3 Local(Vector3 p) => new(Vector3.Dot(p - centre, fwd), Vector3.Dot(p - centre, right), Vector3.Dot(p - centre, up));
        Vector3 LocalDir(Vector3 v) => new(Vector3.Dot(v, fwd), Vector3.Dot(v, right), Vector3.Dot(v, up));

        Vector3 jetL = lf is { } l1 ? Local(l1) : new Vector3(250f, -400f, 0f);
        Vector3 jetR = rf is { } r1 ? Local(r1) : new Vector3(250f, 400f, 0f);
        Vector3 rearL = lr is { } l2 ? Local(l2) : new Vector3(-250f, -400f, 0f);
        Vector3 nose = cockpit is { } cp ? Local(cp) : new Vector3(300f, 0f, 60f);
        Tail = tail is { } tl ? Local(tl) : new Vector3(-500f, 0f, 0f);
        Jets = new Vector3(jetL.X, MathF.Max(MathF.Abs(jetL.Y), MathF.Abs(jetR.Y)), jetL.Z);
        if (seat is { } s)
            Seat = Local(s);
        if (podL is { } pl)
            PodLeft = Local(pl);
        if (podR is { } pr)
            PodRight = Local(pr);

        if (lw is { } boneL && rw is { } boneR)
        {
            Vector3 tipL = Local(boneL + TipOffset * new Vector3(1f, -1f, 1f)), tipR = Local(boneR + TipOffset);
            float half = (MathF.Abs(tipL.Y) + MathF.Abs(tipR.Y)) * 0.5f;
            _contrailScale = half / ContrailSpread;
            _contrailAt = new Vector3((tipL.X + tipR.X) * 0.5f - ContrailAhead * _contrailScale, 0f, (tipL.Z + tipR.Z) * 0.5f + ContrailBelow * _contrailScale);
        }

        if (chassis is { } ch)
        {
            bool showcase = Kit.Showcase;
            _exhaustAt = Local(ch + (showcase ? ShowcaseExhaustOffset : ExhaustOffset));
            _exhaustScale = showcase ? ShowcaseExhaustScale : Vector3.One;
            Geo.Axes(new Rotator(0f, ExhaustYaw, showcase ? ShowcaseExhaustRoll : 0f), out Vector3 ex, out Vector3 ey, out Vector3 ez);
            _exhaustTurn = Geo.FromAxes(LocalDir(ex), LocalDir(ey), LocalDir(ez));
        }

        if (Kit.Mesh is { IsValid: true } model)
        {
            ref GameObject.FBoxSphereBounds mb = ref model.Bounds;
            float low = 0f;
            for (int i = 0; i < 8; i++)
            {
                var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                low = MathF.Min(low, Vector3.Dot(mb.Origin + mb.BoxExtent * sign - centre, up));
            }

            if (float.IsFinite(low))
                Belly = Math.Clamp(-low, 30f, 400f);
        }

        float span = MathF.Max(MathF.Max(MathF.Abs(jetL.Y), MathF.Abs(jetR.Y)), MathF.Abs(rearL.Y));
        if (lw is { } lw2 && rw is { } rw2)
            span = MathF.Max(span, MathF.Max(MathF.Abs(Local(lw2).Y), MathF.Abs(Local(rw2).Y)));
        float length = MathF.Max(MathF.Max(MathF.Abs(nose.X), MathF.Abs(Tail.X)), MathF.Max(MathF.Abs(jetL.X), MathF.Abs(rearL.X)));
        Half = new Vector3(Math.Clamp(length * 1.35f, 300f, 1500f), Math.Clamp(span * 1.4f, 300f, 1600f), 160f);
        _fitted = true;

        Log.Info("Batwing: model fitted using " + how + ", half size " + Geo.Position(Half) + ", belly " + Fmt.Whole(Belly) + " under the middle.");
    }

    // Bones come back in the actor's world space on some frames, so a bone right on top of the actor is
    // turned back into the model's own space.
    private Vector3? Bone(FName name)
    {
        if (_body.MatchRefBone(name) < 0)
            return null;

        Vector3 p = _body.GetBoneLocation(name, 1);
        if (!float.IsFinite(p.X))
            return null;

        if (Actor.IsValid && Vector3.Distance(p, Actor.Location) < p.Length() * 0.25f)
        {
            Geo.Axes(Actor.Rotation, out Vector3 ax, out Vector3 ay, out Vector3 az);
            return Geo.In(Actor.Location, ax, ay, az, p);
        }

        return p;
    }

    public Vector3 WorldPoint(Vector3 local) => Geo.Out(Fm.Position, Fwd, Right, Up, local);

    // Moves the actor to where the flight model is.
    public void Follow() => Place(Fm.Position, Fm.Forward, Fm.Right, Fm.Up);

    public void Place(Vector3 at, Vector3 fwd, Vector3 right, Vector3 up)
    {
        Fwd = fwd;
        Right = right;
        Up = up;
        if (!Alive)
            return;

        Fit();
        Vector3 shown = at + Drift.Offset;
        Drift.Turn(ref fwd, ref right, ref up);
        Actor.SetLocation(shown);
        Actor.SetRotation(Geo.FromAxes(fwd, right, up));

        // Hangs off the model as drawn, so it sways with the hover drift too.
        Cargo?.Hold(shown, fwd, right, up);
    }

    // Measures the ground under it. With the Batmobile hooked it measures from the car, so the car's roof
    // isn't read as the ground.
    public Probe Measure()
    {
        if (!Alive)
            return Below;

        Below = Cargo is { Alive: true } c
            ? Probe.Down(c.Car, c.Car.Location)
            : Probe.Down(Actor, Fm.Position - new Vector3(0f, 0f, Half.Z * 0.6f));
        return Below;
    }

    // How far the hull reaches down in world terms, so a banked craft drops Batman clear of the low wingtip.
    public float Depth => MathF.Abs(Fwd.Z) * Half.X + MathF.Abs(Right.Z) * Half.Y + MathF.Abs(Up.Z) * Half.Z;

    // A box swept along a line, about half the wingspan wide, so the wings count and not just the middle.
    public bool Swept(Vector3 from, Vector3 to)
    {
        float w = Span * 0.5f;
        Actor? hit = Actor.Trace(out Vector3 at, out _, to, from, Component.ECollisionFilter.COF_TraceWorld, new Vector3(w, w, BoxDepth(Half.Z)), out _, 0);
        return (hit != null && at != Vector3.Zero && !IsCargo(hit)) || (Cargo?.Swept(from, to) ?? false);
    }

    // With the car hooked, the craft's own boxes stop short of its roof. The car gets its own sweep.
    public float BoxDepth(float want) => Cargo is { Alive: true } c ? MathF.Max(40f, MathF.Min(want, c.Hang - c.Top - 20f)) : want;

    public bool IsCargo(Actor? a) => a != null && Cargo is { Alive: true } c && a.Ptr == c.Car.Ptr;

    // Takes the craft out of the world. The Batmobile goes back on the ground rather than being left
    // frozen in the air, whatever ended it.
    public void Remove()
    {
        if (Cargo != null)
            Cargo.Drop(Vector3.Zero, gently: true);
        Cargo = null;
        Weapons.Off();
        StopSounds();
        if (Alive)
            Safe.Run("batwing remove", () => Actor.Destroy());
    }

    public static void Burst(ParticleSystem? template, Vector3 at, Vector3 facing)
    {
        if (template is not { IsValid: true } || World.Ri?.ObjectPool is not { IsValid: true } pool)
            return;

        Safe.Run("batwing burst", () => pool.SpawnEmitter(template, at, Rotator.FromDirection(facing), 0, 1f));
    }
}
