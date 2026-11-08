using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    private const string CityPackage = "CityY__Ch3456789";
    private const string CinePackage = "CityX_10_Cine_Ch3cd_HeavyCannonDrop";
    private const string ShowcasePackage = "Showcase_Vehicles_Batwing";
    private const string RescuePackage = "Playable_BatmobileJoker_SF";
    private const string MeshPath = "Batwing_Vehicle.Batwing_Arkham_Knight.Batwing_Arkham_Knight";
    private const string ShowcaseMeshPath = "Batwing_Vehicle.Batwing_Arkham_Knight_Showcase.Batwing_Arkham_Knight_Showcase";
    private const string ExhaustPath = "VFX_Vehicles.Particles.Batwing.Batwing_Exhaust_INGAME";
    private const string ThrustersPath = "VFX_Cinematics.Particles.Vehicles.BatwingThrusters";
    private const string ThrusterBurstPath = "VFX_Cinematics.Particles.Vehicles.Batwing_ThrusterBurst_CINE";
    private const string BoosterPath = "VFX_Cinematics.Particles.Vehicles.Batwing_Booster_CINE";
    private const string DustPath = "VFX_Cinematics.Particles.Vehicles.FloorDust_Batwing";
    private const string ContrailPath = "VFX_Vehicles.Particles.Batwing_Exhaust_P";
    private const string LampPath = "VFX_Security.Particles.SentryGun.SentryGunLightCone_P";
    private const string RescueEnginePath = "Ww_SEQ_BatWing_VS.Play_BatWing_VS_Batwing";
    private const string RescueStopPath = "Ww_SEQ_BatWing_VS.Stop_AUT_Batwing_All";
    private const string HatchClosePath = "Ww_BML_Batmobile.Play_CockpitClose";
    private const string HatchOpenPath = "Ww_BML_Batmobile.Play_CockpitOpen";
    private const string DodgePath = "Ww_BML_Batmobile.Play_Batmobile_Dodge";
    private const string LandPath = "Ww_BML_Batmobile_Subjective.Play_Batmobile_Jump_Land";
    private const string TransformInPath = "Ww_BML_Batmobile.Play_BattleMode_In_";
    private const string TransformOutPath = "Ww_BML_Batmobile.Play_BattleMode_Out_";
    private static readonly string[] TransformParts = { "A", "B", "C" };
    private const string SpeedParamPath = "Ww_Parameters.ObjectVelocity";
    private const string DopplerParamPath = "Ww_Parameters.Doppler";

    private const string FlightLoopPath = "Ww_VEH_Batwing.Play_VEH_Batwing_Fly_Loop_01";
    private const string CalledPath = "Ww_SEQ_Batwing_Triggered.Play_SEQ_Batwing_Triggered";
    private const string ImpactPath = "Ww_OBJ_Dest_MetalsVehicles.Play_Metal_Hollow_Large_Impacts";
    private const string ScrapePath = "Ww_OBJ_Dest_MetalsVehicles.Play_Metal_Hollow_Small_Impacts";
    private const string WreckPath = "Ww_OBJ_Dest_MetalsVehicles.Play_Metal_Heavy_Break";
    private const string BoardSound = "Ww_SBJ_Camera_Whooshes.Play_Cam_Whoosh16";
    private const string BailSound = "Ww_SBJ_Camera_Whooshes.Play_Cam_Whoosh14";
    private const string ModeSound = "Ww_SBJ_Camera_Whooshes.Play_Cam_Whoosh17_01";

    private static readonly string[] ExplosionPaths =
    {
        "VFX_Vehicles.Particles.Tank.Mech_Explosion", "VFX_Vehicles.Particles.Impacts.Batmobile_Explosion", "VFX_Batmobile.Particles.Rocket.Batmobil_Rocket_Explosion",
    };

    private static readonly string[] ImpactFxPaths =
    {
        "VFX_Batmobile.Particles.Batmobile_HeavyCannon_Impact_2", "VFX_Batmobile.Particles.Batmobile_HeavyCannon_Impact", "VFX_Batmobile.Particles.Rocket.Batmobil_Rocket_Explosion_Small",
    };

    private static readonly string[] BoomPaths =
    {
        "Ww_VFX_Tank_Explosion.Play_Batmobile_HeavyCannon_Explosion", "Ww_VFX_Missile_Explosion.Play_Missile_Explosion_MidAir", "Ww_VFX_Missile_Explosion.Play_Missile_Explosion",
    };

    private static readonly FName CraftTag = new("TrinityBatwing");

    private const int MinHeadroom = 40000;

    private static readonly FName LeftFront = new("L_F_Jet_Bone"), RightFront = new("R_F_Jet_Bone");
    private static readonly FName LeftRear = new("L_R_Jet_Bone"), RightRear = new("R_R_Jet_Bone");
    private static readonly FName CockpitBone = new("Batwing_Cockpit"), TailBone = new("MAIN_Tail");
    private static readonly FName LeftWing = new("L_OUTER_WING"), RightWing = new("R_OUTER_WING");
    private static readonly FName LeftEngine = new("L_OUTER_WING_Engine"), RightEngine = new("R_OUTER_WING_Engine");
    private static readonly FName SeatBone = new("SEATING");

    private static readonly FName ExhaustBone = new("Chassie");
    private static readonly Vector3 ExhaustOffset = new(-391.7f, -6.0f, -95.6f);
    private const float ExhaustYaw = -90f;
    private static readonly Vector3 ShowcaseExhaustOffset = new(-467.4f, -0.1f, -95.2f);
    private const float ShowcaseExhaustRoll = 1.4f;
    private static readonly Vector3 ShowcaseExhaustScale = new(1.1f, 1f, 1f);
    private Vector3 _exhaustScale = Vector3.One;
    private bool _exhaustOn = true;

    private static readonly FName ExhaustSpawn = new("ExhaustSpawn"), ExhaustOpacity = new("Opacity"), ExhaustSmoke = new("SmokeOpacity");
    private static readonly FName ThrusterSpawn = new("SideThrusters_R"), ThrusterSize = new("SizeScale"), FlareSize = new("ThrusterSize"), DustSpawn = new("SpawnRate1");

    private readonly record struct JetMount(FName Bone, Vector3 Offset, Rotator Turn, Vector3? Scale = null);

    private static readonly JetMount[] BrakeMounts =
    {
        new(ExhaustBone, new Vector3(113.2f, 3.7f, -136.6f), new Rotator(0f, 90f, 10.2f), new Vector3(1.15f, 0.69f, 1.15f)),
        new(ExhaustBone, new Vector3(-224f, 4.2f, -153.6f), new Rotator(0f, 90f, -15.8f), new Vector3(0.65f)),
    };

    private static readonly JetMount[] JetMounts =
    {
        new(LeftFront, new Vector3(0f, 0f, -19f), new Rotator(16.7f, -147.2f, 9f)),
        new(RightFront, new Vector3(0f, 0f, -19f), new Rotator(16.7f, 147.2f, -9f)),
        new(LeftRear, new Vector3(0f, 0f, -19f), new Rotator(19.8f, 28.3f, -2.6f)),
        new(RightRear, new Vector3(0f, 0f, -19f), new Rotator(19.8f, -28.3f, 2.6f)),
    };

    private const string PoseSetName = "Baked_Batwing_Arkham_Knight_Showcase_Anims_0";
    private static readonly FName PoseAnim = new("Batwing_Arkham_Knight_Showcase");
    private const float PoseHoverTime = 3f;
    private const float FoldFrom = 2f, FoldTo = 4f;

    private static readonly JetMount LampMount = new(ExhaustBone, new Vector3(512.6f, -0.4f, -142.5f), new Rotator(-35f, 0f, 0f));
    private const float LightBrightness = 4f, LightReach = 6000f, LightInner = 14f, LightOuter = 26f;

    private static readonly int[] ContrailEmitters = { 2, 3 };
    private const float ContrailSpread = 450f, ContrailAhead = 32f, ContrailBelow = 49f;
    private static readonly Vector3 TipOffset = new(-9.2f, 264f, -6.1f);

    private const string CinePoseSetName = "Baked_Batwing_0";
    private static readonly FName CinePoseAnim = new("CH03_Heavy_Cannon_Upgrade_BATWING");

    private sealed class Kit
    {
        public SkeletalMesh Mesh = null!;
        public ParticleSystem? Exhaust, Thrusters, ThrusterBurst, Booster, Dust, Explosion, Impact, Contrail, Lamp;
        public AnimSet? Pose, CinePose;
        public AkEvent? Rescue, RescueStop, Flight, Called, Hit, Scrape, Wreck, Boom, HatchOpen, HatchClose, Dodge, Land;
        public readonly AkEvent?[] TransformIn = new AkEvent?[TransformParts.Length], TransformOut = new AkEvent?[TransformParts.Length];
        public AkParameterName? Speed, Doppler;
        public readonly Dictionary<string, AkEvent?> Whooshes = new();
    }

    private Kit? _kit;

    private RBatwing? _craft;
    private SkeletalMeshComponent? _body;

    private Vector3 _lf, _rf, _lr, _rr, _cockpit, _tail;

    private Vector3 _seat = new(590f, 0f, 30f);
    private Vector3 _podL = new(-170f, -530f, 50f), _podR = new(-170f, 530f, 50f);

    private Vector3 _half = new(700f, 800f, 160f);

    private bool _fitted;
    private int _fitTries;
    private ParticleSystemComponent? _exhaust;
    private readonly ParticleSystemComponent?[] _thrusters = new ParticleSystemComponent?[4];
    private readonly ParticleSystemComponent?[] _flares = new ParticleSystemComponent?[BrakeMounts.Length];
    private AnimNodeSequence? _pose;
    private bool _fold;
    private float _engineLevel, _thrustLevel;
    private bool _speedWired;
    private bool _flareOn;
    private double _flareUntil;
    private ParticleSystemComponent? _contrail;
    private Vector3 _contrailAt = new(-400f, 0f, 0f);
    private float _contrailScale = 1.8f;
    private ParticleSystemComponent? _lamp;
    private SpotLightComponent? _light;
    private bool _trailsOn, _lampOn;

    private readonly ParticleSystemComponent?[] _bursts = new ParticleSystemComponent?[4];

    private const double BurstGap = 0.3;
    private int _nextBurst;
    private double _burstDue;

    private Vector3 _exhaustAt = new(-336.4f, -6f, 53.6f);
    private Rotator _exhaustTurn = new(0f, ExhaustYaw, 0f);
    private ParticleSystemComponent? _dust;
    private bool _boosterOn, _dustOn;
    private AkEvent? _engineOn;
    private AkEvent? _boostLoop;

    private const float VoiceFadeSeconds = 7f;
    private const float VoiceFadeDistance = 300000f;
    private const float VoiceLifeSeconds = 40f;
    private RBatwing? _voice;
    private float _voiceFade = -1f;
    private Vector3 _voiceFrom;

    private Vector3 _fwd = Vector3.UnitX, _right = Vector3.UnitY, _up = Vector3.UnitZ;

    private Kit LoadKit()
    {
        if (_kit is { } have && have.Mesh.IsValid)
            return have;

        if (ObjectTable.Headroom < MinHeadroom)
        {
            Safe.Run("collect before batwing", () => World.WorldInfo?.ForceGarbageCollection(true));
            throw new InvalidOperationException("the game is near its object limit, try again in a moment");
        }

        bool loadedLevel = false;
        if (Find<SkeletalMesh>(MeshPath) == null || Find<ParticleSystem>(ExhaustPath) == null || Find<AkEvent>(FlightLoopPath) == null)
        {
            Log.Info(Title + ": loading " + CityPackage + " for the exhaust and engine loop (" + ObjectTable.Describe() + ").");
            Safe.Run("batwing city package", () => Game.LoadPackage(CityPackage));
            loadedLevel = true;
        }

        if (Find<SkeletalMesh>(ShowcaseMeshPath) == null || FindPose() == null)
        {
            Log.Info(Title + ": loading " + ShowcasePackage + " for the Batwing's model.");
            Safe.Run("batwing showcase package", () => Game.LoadPackage(ShowcasePackage));
            loadedLevel = true;
        }

        SkeletalMesh? mesh = Find<SkeletalMesh>(ShowcaseMeshPath) ?? Find<SkeletalMesh>(MeshPath);
        if (mesh == null)
            throw new InvalidOperationException("the Batwing's model didn't load");

        if (Find<ParticleSystem>(ThrustersPath) == null || Find<ParticleSystem>(ThrusterBurstPath) == null || Find<ParticleSystem>(LampPath) == null
            || Find<ParticleSystem>(BoosterPath) == null || Find<AkEvent>(RescueEnginePath) == null || FindSet(CinePoseSetName) == null)
        {
            Log.Info(Title + ": loading " + CinePackage + " for the thrusters, landing light and rescue parts.");
            Safe.Run("batwing effects package", () => Game.LoadPackage(CinePackage));
            loadedLevel = true;
        }

        if (Find<ParticleSystem>(BoosterPath) == null || Find<AkEvent>(RescueEnginePath) == null || Find<ParticleSystem>(ContrailPath) == null)
        {
            Log.Info(Title + ": loading " + RescuePackage + " for the contrails.");
            Safe.Run("batwing rescue package", () => Game.LoadPackage(RescuePackage));
        }

        var kit = new Kit { Mesh = Roots.Keep(mesh, KitOwner) };
        kit.Exhaust = Kept(Find<ParticleSystem>(ExhaustPath));
        kit.Thrusters = Kept(Find<ParticleSystem>(ThrustersPath));
        kit.ThrusterBurst = Kept(Find<ParticleSystem>(ThrusterBurstPath));
        kit.Booster = Kept(Find<ParticleSystem>(BoosterPath));
        kit.Dust = Kept(Find<ParticleSystem>(DustPath));
        kit.Contrail = Kept(Find<ParticleSystem>(ContrailPath));
        kit.Lamp = Kept(Find<ParticleSystem>(LampPath));
        kit.Pose = Kept(FindPose());
        kit.CinePose = Kept(FindSet(CinePoseSetName));
        kit.Explosion = Kept(First<ParticleSystem>(ExplosionPaths));
        kit.Impact = Kept(First<ParticleSystem>(ImpactFxPaths));
        kit.Rescue = Kept(Find<AkEvent>(RescueEnginePath));
        kit.Flight = Kept(Find<AkEvent>(FlightLoopPath));
        kit.Called = Kept(Find<AkEvent>(CalledPath));
        kit.Hit = Kept(Find<AkEvent>(ImpactPath));
        kit.Scrape = Kept(Find<AkEvent>(ScrapePath));
        kit.Wreck = Kept(Find<AkEvent>(WreckPath));
        kit.Boom = Kept(First<AkEvent>(BoomPaths));
        kit.RescueStop = Kept(Find<AkEvent>(RescueStopPath));
        kit.HatchOpen = Kept(Find<AkEvent>(HatchOpenPath));
        kit.HatchClose = Kept(Find<AkEvent>(HatchClosePath));
        kit.Dodge = Kept(Find<AkEvent>(DodgePath));
        kit.Land = Kept(Find<AkEvent>(LandPath));
        for (int i = 0; i < TransformParts.Length; i++)
        {
            kit.TransformIn[i] = Kept(Find<AkEvent>(TransformInPath + TransformParts[i]));
            kit.TransformOut[i] = Kept(Find<AkEvent>(TransformOutPath + TransformParts[i]));
        }

        kit.Speed = Kept(Find<AkParameterName>(SpeedParamPath));
        kit.Doppler = Kept(Find<AkParameterName>(DopplerParamPath));
        foreach (string path in new[] { BoardSound, BailSound, ModeSound })
            kit.Whooshes[path] = Kept(Find<AkEvent>(path));

        foreach (AkEvent? ev in new[] { kit.Rescue, kit.Flight, kit.Called, kit.Hit, kit.Scrape, kit.Wreck, kit.Boom, kit.HatchOpen, kit.HatchClose, kit.Dodge, kit.Land }
                     .Concat(kit.TransformIn).Concat(kit.TransformOut).Concat(kit.Whooshes.Values))
        {
            if (ev is { IsValid: true })
                Safe.Run("batwing sound bank", () => ev.RequestAssetBankLoad());
        }

        if (loadedLevel)
            Safe.Run("batwing collect", () => World.WorldInfo?.ForceGarbageCollection(true));

        Log.Info(Title + ": parts ready (" + ObjectTable.Describe() + "): model " + (mesh.Name == "Batwing_Arkham_Knight_Showcase" ? "showcase" : "city") + ", exhaust " + Has(kit.Exhaust) + ", thrusters " + Has(kit.Thrusters) + "/" + Has(kit.ThrusterBurst)
            + ", afterburner " + Has(kit.Booster) + ", downwash " + Has(kit.Dust) + ", contrails " + Has(kit.Contrail) + ", landing light " + Has(kit.Lamp) + ", pose " + Has(kit.Pose)
            + ", explosion " + Has(kit.Explosion) + ", engine loop " + Has(kit.Flight)
            + ", fly-in sounds " + Has(kit.Rescue) + "/" + Has(kit.Called) + ", transform sounds " + kit.TransformIn.Count(t => t != null) + "/" + kit.TransformOut.Count(t => t != null)
            + ", thruster puff " + Has(kit.Dodge) + ", landing " + Has(kit.Land) + ", hatch " + Has(kit.HatchClose)
            + ", impact sounds " + Has(kit.Hit) + "/" + Has(kit.Scrape) + ", explosion sound " + Has(kit.Boom)
            + ", speed parameter " + (kit.Speed is { } speed ? Fmt.Whole(speed.MinValue) + ".." + Fmt.Whole(speed.MaxValue) + " (default " + Fmt.Whole(speed.DefaultValue) + ")" : "no") + ".");
        _kit = kit;
        return kit;
    }

    private static string Has(GameObject? o) => o != null ? "yes" : "no";

    private static AnimSet? FindPose() => FindSet(PoseSetName);

    private static AnimSet? FindSet(string name)
    {
        try
        {
            foreach (AnimSet anims in World.All<AnimSet>())
            {
                if (anims.IsValid && anims.Name == name)
                    return anims;
            }
        }
        catch
        {
        }

        return null;
    }

    private static T? Kept<T>(T? obj) where T : GameObject => obj != null ? Roots.Keep(obj, KitOwner) : null;

    private static T? Find<T>(string path) where T : GameObject
    {
        try
        {
            return Game.FindObject<T>(path) is { IsValid: true } found ? found : null;
        }
        catch
        {
            return null;
        }
    }

    private static T? First<T>(string[] paths) where T : GameObject
    {
        foreach (string path in paths)
        {
            if (Find<T>(path) is { } found)
                return found;
        }

        return null;
    }

    private void ForgetKit()
    {
        _kit = null;
        Roots.Let(KitOwner);
    }

    private RBatwing SpawnCraft(Kit kit, Vector3 at, float yaw)
    {
        Pawn spawner = World.Pawn ?? throw new InvalidOperationException("the game is still loading");
        Actor? made = spawner.Spawn(RBatwing.StaticClass(), null!, CraftTag, at, new Rotator(0f, 0f, 0f), null!, true);
        if (made is not RBatwing craft || !craft.IsValid)
            throw new InvalidOperationException("the Batwing couldn't be put there");

        _craft = craft;
        craft.SetPhysics(Actor.EPhysics.PHYS_None, false);
        craft.SetCollision(false, false, true);
        craft.bCollideWorld = false;

        SkeletalMeshComponent body = craft.SkeletalMeshComponent ?? throw new InvalidOperationException("the Batwing has no body");
        _body = body;
        _pose = null;
        _fold = kit.CinePose is { IsValid: true };
        if (kit.CinePose is { IsValid: true } || kit.Pose is { IsValid: true })
        {
            Safe.Run("batwing pose", () =>
            {
                var node = new AnimNodeSequence(body);
                node.bZeroRootTranslation = true;
                node.bZeroRootRotation = true;
                body.Animations = node;
                if (kit.CinePose is { IsValid: true } cine)
                    body.AnimSets.Push(cine);
                if (kit.Pose is { IsValid: true } flex)
                    body.AnimSets.Push(flex);
                _pose = node;
            });
        }

        body.SetSkeletalMesh(kit.Mesh, false);
        if (_pose is { } posed)
        {
            Safe.Run("batwing pose", () =>
            {
                posed.SetAnim(_fold ? CinePoseAnim : PoseAnim);
                posed.bPlaying = false;
                posed.SetPosition(_fold ? FoldFrom : 0f, false);
            });
        }

        _fitted = false;
        _fitTries = 0;
        Fit();

        craft.SetRotation(new Rotator(0f, yaw, 0f));
        BuildEffects(kit);
        return craft;
    }

    private void Fit()
    {
        if (_fitted || _body is not { IsValid: true } body)
            return;

        body.SetRotation(new Rotator(0f, 0f, 0f));
        body.SetTranslation(Vector3.Zero);
        body.ForceSkelUpdate();

        Vector3? lf = Bone(body, LeftFront), rf = Bone(body, RightFront), lr = Bone(body, LeftRear), rr = Bone(body, RightRear);
        Vector3? cockpit = Bone(body, CockpitBone), tail = Bone(body, TailBone);
        Vector3? lw = Bone(body, LeftWing) ?? Bone(body, LeftEngine), rw = Bone(body, RightWing) ?? Bone(body, RightEngine);
        Vector3? chassis = Bone(body, ExhaustBone);

        if (_craft is { IsValid: true } owner && (lf ?? cockpit) is { } probe && Vector3.Distance(probe, owner.Location) < probe.Length() * 0.25f)
        {
            Geo.Axes(owner.Rotation, out Vector3 ax, out Vector3 ay, out Vector3 az);
            Vector3 origin = owner.Location;
            Vector3? Back(Vector3? w) => w is { } v ? new Vector3(Vector3.Dot(v - origin, ax), Vector3.Dot(v - origin, ay), Vector3.Dot(v - origin, az)) : null;
            lf = Back(lf);
            rf = Back(rf);
            lr = Back(lr);
            rr = Back(rr);
            cockpit = Back(cockpit);
            tail = Back(tail);
            lw = Back(lw);
            rw = Back(rw);
            chassis = Back(chassis);
            Log.Info(Title + ": bone positions came back in world space, converted.");
        }

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
        Rotator turn = Geo.FromAxes(x, y, z);
        Vector3 Local(Vector3 p) => new(Vector3.Dot(p - centre, fwd), Vector3.Dot(p - centre, right), Vector3.Dot(p - centre, up));
        Vector3 LocalDir(Vector3 v) => new(Vector3.Dot(v, fwd), Vector3.Dot(v, right), Vector3.Dot(v, up));

        body.SetRotation(turn);
        body.SetTranslation(-(x * centre.X + y * centre.Y + z * centre.Z));

        _lf = lf is { } lf2 ? Local(lf2) : new Vector3(250f, -400f, 0f);
        _rf = rf is { } rf2 ? Local(rf2) : new Vector3(250f, 400f, 0f);
        _lr = lr is { } lr2 ? Local(lr2) : new Vector3(-250f, -400f, 0f);
        _rr = rr is { } rr2 ? Local(rr2) : new Vector3(-250f, 400f, 0f);
        _cockpit = cockpit is { } cp ? Local(cp) : new Vector3(300f, 0f, 60f);
        _tail = tail is { } tl ? Local(tl) : new Vector3(-500f, 0f, 0f);
        if (Point(body, SeatBone) is { } seat)
            _seat = Local(seat);
        if (Point(body, LeftEngine) is { } podL)
            _podL = Local(podL);
        if (Point(body, RightEngine) is { } podR)
            _podR = Local(podR);

        if (lw is { } boneL && rw is { } boneR)
        {
            Vector3 tipL = Local(boneL + TipOffset * new Vector3(1f, -1f, 1f)), tipR = Local(boneR + TipOffset);
            float half = (MathF.Abs(tipL.Y) + MathF.Abs(tipR.Y)) * 0.5f;
            _contrailScale = half / ContrailSpread;
            _contrailAt = new Vector3((tipL.X + tipR.X) * 0.5f - ContrailAhead * _contrailScale, 0f, (tipL.Z + tipR.Z) * 0.5f + ContrailBelow * _contrailScale);
        }

        if (chassis is { } ch)
        {
            bool showcase = body.SkeletalMesh is { IsValid: true } skin && skin.Name == "Batwing_Arkham_Knight_Showcase";
            _exhaustAt = Local(ch + (showcase ? ShowcaseExhaustOffset : ExhaustOffset));
            _exhaustScale = showcase ? ShowcaseExhaustScale : Vector3.One;
            Geo.Axes(new Rotator(0f, ExhaustYaw, showcase ? ShowcaseExhaustRoll : 0f), out Vector3 ex, out Vector3 ey, out Vector3 ez);
            _exhaustTurn = Geo.FromAxes(LocalDir(ex), LocalDir(ey), LocalDir(ez));
        }

        float span = MathF.Max(MathF.Max(MathF.Abs(_lf.Y), MathF.Abs(_rf.Y)), MathF.Max(MathF.Abs(_lr.Y), MathF.Abs(_rr.Y)));
        if (lw is { } lw2 && rw is { } rw2)
            span = MathF.Max(span, MathF.Max(MathF.Abs(Local(lw2).Y), MathF.Abs(Local(rw2).Y)));
        float length = MathF.Max(MathF.Max(MathF.Abs(_cockpit.X), MathF.Abs(_tail.X)), MathF.Max(MathF.Abs(_lf.X), MathF.Abs(_lr.X)));
        _half = new Vector3(Math.Clamp(length * 1.35f, 300f, 1500f), Math.Clamp(span * 1.4f, 300f, 1600f), 160f);
        _fitted = true;

        Log.Info(Title + ": model fitted using " + how + ", hull half size " + Geo.Position(_half) + ", exhaust at " + Geo.Position(_exhaustAt) + ".");
    }

    private Vector3? Point(SkeletalMeshComponent body, FName name)
    {
        if (Bone(body, name) is not { } p)
            return null;
        if (_craft is { IsValid: true } owner && Vector3.Distance(p, owner.Location) < p.Length() * 0.25f)
        {
            Geo.Axes(owner.Rotation, out Vector3 ax, out Vector3 ay, out Vector3 az);
            Vector3 d = p - owner.Location;
            return new Vector3(Vector3.Dot(d, ax), Vector3.Dot(d, ay), Vector3.Dot(d, az));
        }

        return p;
    }

    private static Vector3? Bone(SkeletalMeshComponent body, FName name)
    {
        if (body.MatchRefBone(name) < 0)
            return null;

        Vector3 p = body.GetBoneLocation(name, 1);
        return float.IsFinite(p.X) ? p : null;
    }

    private Vector3 WorldPoint(Vector3 at, Vector3 local) => at + _fwd * local.X + _right * local.Y + _up * local.Z;

    private void Place(Vector3 at, Vector3 fwd, Vector3 right, Vector3 up)
    {
        _fwd = fwd;
        _right = right;
        _up = up;
        if (_craft is not { IsValid: true } craft)
            return;

        Fit();
        craft.SetLocation(at);
        craft.SetRotation(Geo.FromAxes(fwd, right, up));
    }

    private void DropCraft()
    {
        WeaponsOff();
        BoostSound(false);
        StopEngine();
        RBatwing? craft = _craft;
        _craft = null;
        _body = null;
        _dust = null;
        _exhaust = null;
        _lamp = null;
        _light = null;
        _contrail = null;
        _exhaustOn = true;
        _pose = null;
        Array.Clear(_bursts);
        Array.Clear(_thrusters);
        Array.Clear(_flares);
        _trailsOn = _lampOn = false;
        _engineLevel = _thrustLevel = 0f;
        _speedWired = false;
        _flareOn = false;
        _cues.Clear();
        _boosterOn = _dustOn = false;
        if (craft is { IsValid: true } && !craft.bDeleteMe)
            Safe.Run("batwing remove", () => craft.Destroy());
    }

    private void BuildEffects(Kit kit)
    {
        if (_craft is not { IsValid: true } craft)
            return;

        if (kit.Exhaust is { } exhaust)
            _exhaust = Effect(craft, exhaust, true);

        if (kit.Booster is { } booster)
        {
            for (int i = 0; i < _bursts.Length; i++)
                _bursts[i] = Effect(craft, booster, false);
        }

        if (_body is { IsValid: true } body)
        {
            for (int i = 0; i < JetMounts.Length; i++)
            {
                JetMount m = JetMounts[i];
                if (kit.Thrusters is { } thrusters)
                    _thrusters[i] = JetEffect(craft, body, thrusters, m, true);
            }

            if (kit.ThrusterBurst is { } flare)
            {
                for (int i = 0; i < BrakeMounts.Length; i++)
                {
                    _flares[i] = JetEffect(craft, body, flare, BrakeMounts[i], false);
                    _flares[i]?.SetVectorParameter(FlareSize, new Vector3(0.7f, 0.7f, 0.7f));
                }
            }
        }

        if (kit.Contrail is { } contrail)
            _contrail = Effect(craft, contrail, false);

        if (kit.Dust is { } dust)
        {
            _dust = Effect(craft, dust, false);
            _dust?.SetAbsolute(true, true, false);
        }

        if (_body is { IsValid: true } skin)
        {
            if (kit.Lamp is { } lamp)
                _lamp = JetEffect(craft, skin, lamp, LampMount, false);
            _light = Light(craft, skin);
        }
    }

    private static void Contrails(ParticleSystemComponent c)
    {
        Safe.Run("batwing contrails", () =>
        {
            c.ActivateSystem(true, false);
            for (int i = 0; i < 11; i++)
            {
                if (Array.IndexOf(ContrailEmitters, i) < 0)
                    c.SetStopSpawning(i, true);
            }
        });
    }

    private static SpotLightComponent? Light(Actor owner, SkeletalMeshComponent body)
    {
        SpotLightComponent? made = null;
        Safe.Run("batwing landing light", () =>
        {
            var c = new SpotLightComponent(owner);
            c.Brightness = LightBrightness;
            c.LightColor = new GameObject.FColor { R = 255, G = 246, B = 232, A = 255 };
            c.Radius = LightReach;
            c.InnerConeAngle = LightInner;
            c.OuterConeAngle = LightOuter;
            c.CastShadows = false;
            body.AttachComponent(c, LampMount.Bone, LampMount.Offset, LampMount.Turn, Vector3.One);
            c.SetEnabled(false);
            made = c;
        });
        return made;
    }

    private static ParticleSystemComponent? JetEffect(Actor owner, SkeletalMeshComponent body, ParticleSystem template, JetMount mount, bool on)
    {
        ParticleSystemComponent? made = null;
        Safe.Run("batwing jet effect", () =>
        {
            var c = new ParticleSystemComponent(owner);
            c.bAutoActivate = false;
            c.SetTemplate(template);
            body.AttachComponent(c, mount.Bone, mount.Offset, mount.Turn, mount.Scale ?? Vector3.One);
            if (on)
                c.ActivateSystem(true, false);
            made = c;
        });
        return made;
    }

    private static ParticleSystemComponent? Effect(Actor owner, ParticleSystem template, bool on)
    {
        ParticleSystemComponent? made = null;
        Safe.Run("batwing effect", () =>
        {
            var c = new ParticleSystemComponent(owner);
            c.bAutoActivate = false;
            c.SetTemplate(template);
            owner.AttachComponent(c);
            if (on)
                c.ActivateSystem(true, false);
            made = c;
        });
        return made;
    }

    private void UpdateEffects(float hover, bool boosting, Vector3 at, float ground, Vector3 groundAt, Vector3 groundNormal, float jets, Vector3 velocity, float throttle, float g, float dt)
    {
        bool boost = boosting && hover < 0.5f;
        float ease = Ease.Follow(5f, dt);

        bool fast = hover < 0.5f && velocity.LengthSquared() > 4000f * 4000f;
        bool trails = boost || fast && (_trailsOn ? g > 1.4f : g > 1.7f);
        if (_contrail is { IsValid: true } vapour)
        {
            vapour.SetTranslation(_contrailAt);
            vapour.SetRotation(new Rotator(0f, -90f, 0f));
            vapour.SetScale(_contrailScale);
            if (trails != _trailsOn)
            {
                if (trails)
                    Contrails(vapour);
                else
                    vapour.DeactivateSystem();
            }
        }

        _trailsOn = trails;

        bool lamp = hover > 0.5f && ground < LightReach;
        if (lamp != _lampOn && _light is { IsValid: true } beam)
            Safe.Run("batwing landing light", () => beam.SetEnabled(lamp));
        if (_lamp is { IsValid: true } l)
        {
            if (lamp != _lampOn)
            {
                Log.Detail(Title + ": landing light " + (lamp ? "on" : "off") + ".");
                if (lamp)
                    l.ActivateSystem(true, false);
                else
                    l.DeactivateSystem();
            }
        }

        _lampOn = lamp;

        float engine = hover > 0.5f ? 0.1f + 0.15f * jets : 0.4f + 0.6f * throttle;
        float thrust = hover > 0.5f ? 0.3f + 0.4f * jets : 0.35f + 0.25f * throttle;
        if (boost)
        {
            engine = 1f;
            thrust = 0.7f;
        }
        _engineLevel += (engine - _engineLevel) * ease;
        _thrustLevel += (thrust - _thrustLevel) * ease;

        Vector3 exhaustAt = _exhaustAt;
        bool burn = hover < 0.5f || boost;
        if (_exhaust is { IsValid: true } exhaust)
        {
            if (burn != _exhaustOn)
            {
                if (burn)
                    exhaust.ActivateSystem(true, false);
                else
                    exhaust.DeactivateSystem();
                _exhaustOn = burn;
            }

            exhaust.SetTranslation(exhaustAt);
            exhaust.SetRotation(_exhaustTurn);
            exhaust.SetScale3D(_exhaustScale);
            exhaust.SetFloatParameter(ExhaustSpawn, _engineLevel);
            exhaust.SetFloatParameter(ExhaustOpacity, boost ? 1.6f : 0.6f + 0.8f * _engineLevel);
            exhaust.SetFloatParameter(ExhaustSmoke, _engineLevel);
        }

        for (int i = 0; i < _thrusters.Length; i++)
        {
            if (_thrusters[i] is { IsValid: true } thruster)
            {
                thruster.SetFloatParameter(ThrusterSpawn, _thrustLevel);
                float size = 0.4f + 0.35f * _thrustLevel;
                thruster.SetVectorParameter(ThrusterSize, new Vector3(size, size, 0f));
            }
        }

        foreach (ParticleSystemComponent? flare in _flares)
        {
            if (flare is { IsValid: true } && _flareOn && Clock.Now >= _flareUntil)
                flare.DeactivateSystem();
        }

        if (_pose is { IsValid: true } pose)
        {
            float h = Math.Clamp(hover, 0f, 1f);
            pose.SetPosition(_fold ? FoldFrom + h * (FoldTo - FoldFrom) : h * PoseHoverTime, false);
        }

        if (_flareOn && Clock.Now >= _flareUntil)
            _flareOn = false;

        PlayCues();

        if (_craft is { IsValid: true } craft)
        {
            craft.Velocity = velocity;
            if (!_speedWired && _kit?.Speed is { IsValid: true } speed)
            {
                float v = velocity.Length() / Metre;
                Safe.Run("batwing engine pitch", () => craft.SetAudioParameter(speed, speed.MaxValue > speed.MinValue ? Math.Clamp(v, speed.MinValue, speed.MaxValue) : v));
            }
        }

        foreach (ParticleSystemComponent? burst in _bursts)
        {
            if (burst is { IsValid: true })
            {
                burst.SetTranslation(exhaustAt);
                burst.SetRotation(_exhaustTurn);
            }
        }

        double now = Clock.Now;
        if (boost && (!_boosterOn || now >= _burstDue))
        {
            if (_bursts[_nextBurst] is { IsValid: true } next)
                next.ActivateSystem(true, false);
            _nextBurst = (_nextBurst + 1) % _bursts.Length;
            _burstDue = now + BurstGap;
        }

        if (boost != _boosterOn)
            BoostSound(boost);
        _boosterOn = boost;

        const float reach = 4500f;
        bool dust = ground < reach && jets > 0.25f && hover > 0.2f;
        if (_dust is { IsValid: true } d)
        {
            if (dust)
            {
                d.SetTranslation(groundAt);
                d.SetRotation(Rotator.FromDirection(groundNormal));
                d.SetFloatParameter(DustSpawn, Math.Clamp(1.3f - ground / reach, 0.25f, 1f));
            }

            if (dust != _dustOn)
            {
                if (dust)
                    d.ActivateSystem(true, false);
                else
                    d.DeactivateSystem();
            }
        }

        _dustOn = dust;
    }

    private static void Burst(ParticleSystem? template, Vector3 at, Vector3 facing)
    {
        if (template is not { IsValid: true } || World.Ri?.ObjectPool is not { IsValid: true } pool)
            return;

        Safe.Run("batwing burst", () => pool.SpawnEmitter(template, at, Rotator.FromDirection(facing), 0, 1f));
    }

    private void StartEngine()
    {
        if (_engineOn != null || _craft is not { IsValid: true } craft || _kit is not { Flight: { IsValid: true } engine } kit)
            return;

        craft.AddLoopingAudioEvent(engine);
        _engineOn = engine;
        if (!_speedWired)
            WireSpeed(craft, kit);
    }

    private void WireSpeed(RBatwing craft, Kit kit)
    {
        if (kit.Speed is not { IsValid: true } speed)
            return;

        Safe.Run("batwing engine pitch", () =>
        {
            if (craft.DefaultAkAudible is not { IsValid: true } audible)
            {
                Log.Info(Title + ": the craft has no audible, speed is set by hand.");
                return;
            }

            var setup = GameObject.ConstructObject<RAkAudibleParameters>(craft);
            setup.ObjectVelocityParameter = speed;
            if (kit.Doppler is { IsValid: true } doppler)
                setup.DopplerParameter = doppler;
            setup.DopplerScale = 100f;
            audible.ParameterSetup = setup;
            _speedWired = true;
            Log.Info(Title + ": engine loop follows the speed through the audible.");
        });
    }

    private void Flare(float seconds)
    {
        foreach (ParticleSystemComponent? flare in _flares)
        {
            if (flare is { IsValid: true })
                flare.ActivateSystem(true, false);
        }

        _flareOn = true;
        _flareUntil = Clock.Now + seconds;
    }

    private const float TransformGap = 0.3f;

    private readonly List<(double At, AkEvent Ev)> _cues = new();

    private void Cue(AkEvent? ev, float delay)
    {
        if (ev is { IsValid: true })
            _cues.Add((Clock.Now + delay, ev));
    }

    private void PlayCues()
    {
        double now = Clock.Now;
        for (int i = _cues.Count - 1; i >= 0; i--)
        {
            if (_cues[i].At > now)
                continue;

            Sound(_cues[i].Ev);
            _cues.RemoveAt(i);
        }
    }

    private void Transform(AkEvent?[] parts)
    {
        _cues.Clear();
        if (parts.All(p => p == null))
        {
            Whoosh(ModeSound);
            return;
        }

        for (int i = 0; i < parts.Length; i++)
            Cue(parts[i], i * TransformGap);
    }

    private void Puff() => Sound(_kit?.Dodge);

    private void HoverSounds()
    {
        Puff();
        if (_kit is { } kit)
            Transform(kit.TransformIn);
    }

    private void WingSounds()
    {
        if (_kit is { } kit)
            Transform(kit.TransformOut);
    }

    private void Hatch(bool close)
    {
        AkEvent? ev = close ? _kit?.HatchClose : _kit?.HatchOpen;
        if (ev is { IsValid: true })
            Sound(ev);
        else
            Whoosh(close ? BoardSound : BailSound);
    }

    private void StopEngine()
    {
        AkEvent? engine = _engineOn;
        _engineOn = null;
        if (engine is { IsValid: true } && _craft is { IsValid: true } craft)
            Safe.Run("batwing engine", () => craft.RemoveLoopingAudioEvent(engine));
    }

    private void BoostSound(bool on)
    {
        RBatwing? craft = _craft is { IsValid: true } c ? c : null;
        AkPlayerVehicleSoundInfo? info = null;
        Safe.Run("batwing boost sound", () => info = Car.Sounds());

        if (on)
        {
            if (_boostLoop != null || craft == null || info?.BoostStartEvent is not { IsValid: true } start)
                return;
            Safe.Run("batwing boost sound", () => craft.AddLoopingAudioEvent(start));
            _boostLoop = start;
            return;
        }

        AkEvent? loop = _boostLoop;
        _boostLoop = null;
        if (loop == null || craft == null)
            return;
        if (loop.IsValid)
            Safe.Run("batwing boost sound", () => craft.RemoveLoopingAudioEvent(loop));
        if (info?.BoostKillEvent is { IsValid: true } kill)
            Sound(kill);
    }

    private void StartVoice(AkEvent? ev, Vector3 at)
    {
        StopVoice();
        if (ev is not { IsValid: true } || World.Pawn is not { } spawner)
            return;

        Safe.Run("batwing fly-in sound", () =>
        {
            if (spawner.Spawn(RBatwing.StaticClass(), null!, CraftTag, at, new Rotator(0f, 0f, 0f), null!, true) is not RBatwing voice || !voice.IsValid)
                return;

            voice.SetPhysics(Actor.EPhysics.PHYS_None, false);
            voice.SetCollision(false, false, true);
            voice.bCollideWorld = false;
            _voice = voice;
            _voiceFade = -1f;
            voice.StartAudioEvent(ev, 0, 0);
        });
    }

    private const double VoiceStopGrace = 1.5;
    private double _voiceKillAt = -1.0;

    private void EndVoice()
    {
        if (_voice is not { IsValid: true } voice || _voiceFade >= 0f || _voiceKillAt > 0.0)
            return;

        if (_kit?.RescueStop is { IsValid: true } stop)
            Safe.Run("batwing fly-in sound", () => voice.StartAudioEvent(stop, 0, 0));
        _voiceKillAt = Clock.Now + VoiceStopGrace;
    }

    private void VoiceStep(float dt)
    {
        if (_voice is not { IsValid: true } voice)
        {
            _voice = null;
            _voiceKillAt = -1.0;
            return;
        }

        if (_voiceKillAt > 0.0 && Clock.Now >= _voiceKillAt)
        {
            StopVoice();
            return;
        }

        if (_voiceFade < 0f)
        {
            if (_craft is { IsValid: true } craft)
                voice.SetLocation(craft.Location);
            else
                FadeVoice();
            return;
        }

        _voiceFade += dt;
        if (_voiceFade >= VoiceLifeSeconds)
        {
            StopVoice();
            return;
        }

        float t = MathF.Min(_voiceFade / VoiceFadeSeconds, 1f);
        voice.SetLocation(_voiceFrom + new Vector3(0f, 0f, VoiceFadeDistance * Ease.In(t)));
    }

    private void FadeVoice()
    {
        if (_voice is not { IsValid: true } voice || _voiceFade >= 0f || _voiceKillAt > 0.0)
            return;

        _voiceFade = 0f;
        _voiceFrom = voice.Location;
    }

    private void StopVoice()
    {
        RBatwing? voice = _voice;
        _voice = null;
        _voiceFade = -1f;
        _voiceKillAt = -1.0;
        if (voice is { IsValid: true } && !voice.bDeleteMe)
            Safe.Run("batwing fly-in sound", () => voice.Destroy());
    }

    private void Sound(AkEvent? ev) => Sound(ev, _craft is { IsValid: true } craft ? craft : World.Pawn);

    private static void Sound(AkEvent? ev, Actor? on)
    {
        if (ev is not { IsValid: true } || on is not { IsValid: true })
            return;

        Safe.Run("batwing sound", () => on.StartAudioEvent(ev, 0, 0));
    }

    private void Whoosh(string path)
    {
        if (_kit != null && _kit.Whooshes.TryGetValue(path, out AkEvent? ev))
            Sound(ev);
    }

    private const float ArriveSeconds = 5.5f;
    private const float HoverAbove = 1300f;
    private const float SkyCheck = 4000f;

    private Vector3 _from;
    private Vector3 _arriveAt;
    private float _arriveT;
    private float _arriveYaw;
    private float _arriveBank;
    private Vector3 _arriveLast;
    private Vector3 _arriveVel;

    private const float GrappleReach = 450f;
    private const float GrappleMost = 5f;

    private float _grappleT = -1f;
    private Vector3 _grappleTo;
    private float _grappleYaw;

    public float Eta => MathF.Max(0f, ArriveSeconds - _arriveT);

    public float EtaFraction => Math.Clamp(_arriveT / ArriveSeconds, 0f, 1f);

    private static Vector3 HoverPoint(Pawn pawn) => pawn.Location + new Vector3(0f, 0f, HoverAbove);

    private void StartArrival(RPawnPlayer pawn, Kit kit)
    {
        Vector3 feet = pawn.Location;
        if (Ground.Ray(pawn, feet + new Vector3(0f, 0f, 100f), feet + new Vector3(0f, 0f, SkyCheck), out _, out _))
            throw new InvalidOperationException("it needs open sky above you");

        Vector3 hover = HoverPoint(pawn);
        float camYaw = Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Rotation.Yaw : pawn.Rotation.Yaw;
        Vector3? start = null;
        foreach (float turn in new[] { 180f, 135f, -135f, 90f, -90f, 45f, -45f, 0f })
        {
            Vector3 flat = Geo.Facing(camYaw + turn);
            Vector3 s = hover + flat * 16000f + new Vector3(0f, 0f, 5000f);
            Vector3 mid = hover + flat * 6000f + new Vector3(0f, 0f, 2600f);
            if (!Ground.Ray(pawn, s, mid, out _, out _) && !Ground.Ray(pawn, mid, hover + new Vector3(0f, 0f, 2200f), out _, out _))
            {
                start = s;
                break;
            }
        }

        start ??= hover + new Vector3(0f, 0f, 9000f);
        _from = start.Value;
        _arriveAt = hover;
        _arriveT = 0f;
        _arriveBank = 0f;
        _arriveLast = _from;
        _arriveVel = Vector3.Zero;
        _grappleT = -1f;
        Vector3 toward = hover - _from;
        _arriveYaw = MathF.Atan2(toward.Y, toward.X) * Deg;

        SpawnCraft(kit, _from, _arriveYaw);

        StartEngine();
        _phase = Phase.Arriving;
        StartVoice(kit.Rescue, _from);
        Sound(kit.Called, pawn);
        Toast("INBOUND");
        Log.Info(Title + ": called in from " + Geo.Position(_from) + " to hover over " + Geo.Position(hover) + ".");
    }

    private void ArriveStep(R3rdPersonCamera cam, PlayerController pc, float dt)
    {
        if (World.Pawn is not RPawnPlayer pawn || pawn.Health <= 0 || Hooks.InCinematic(pc))
        {
            Depart(0f);
            return;
        }

        if (dt <= 0f)
            return;

        _arriveT += dt;
        float t = Math.Clamp(_arriveT / ArriveSeconds, 0f, 1f);
        if (_grappleT < 0f)
            _arriveAt = Vector3.Lerp(_arriveAt, HoverPoint(pawn), Ease.Follow(3f, dt));

        Vector3 p0 = _from;
        var flatIn = new Vector3(_arriveAt.X - _from.X, _arriveAt.Y - _from.Y, 0f);
        Vector3 p1 = _from + flatIn * 0.55f;
        Vector3 p2 = _arriveAt + new Vector3(0f, 0f, 2200f);
        Vector3 p3 = _arriveAt;
        float e = 1f - MathF.Pow(1f - t, 2.4f);
        float u = 1f - e;
        Vector3 at = u * u * u * p0 + 3f * u * u * e * p1 + 3f * u * e * e * p2 + e * e * e * p3;

        Vector3 vel = (at - _arriveLast) / dt;
        _arriveLast = at;
        _arriveVel = Vector3.Lerp(_arriveVel, vel, Ease.Follow(8f, dt));
        float speed = _arriveVel.Length();

        float settle = Ease.Smooth((t - 0.5f) / 0.4f);
        if (settle > 0.05f && !_flareOn && t < 0.8f)
        {
            Flare(1.6f);
            HoverSounds();
        }
        float pathYaw = speed > 200f ? MathF.Atan2(_arriveVel.Y, _arriveVel.X) * Deg : _arriveYaw;
        float camYaw = _grappleT >= 0f ? _grappleYaw : cam.CameraCache.POV.Rotation.Yaw;
        float yawRate = Geo.WrapDegrees(pathYaw - _arriveYaw) / dt;
        _arriveYaw += Geo.WrapDegrees(pathYaw - _arriveYaw) * Ease.Follow(6f, dt);
        float bankWant = Math.Clamp(MathF.Atan(yawRate / Deg * speed / _fm.Gravity) * Deg, -40f, 40f);
        _arriveBank += (bankWant - _arriveBank) * Ease.Follow(4f, dt);

        float yaw = _arriveYaw + Geo.WrapDegrees(camYaw - _arriveYaw) * settle;
        float climb = speed > 200f ? Math.Clamp(MathF.Asin(Math.Clamp(_arriveVel.Z / speed, -1f, 1f)) * Deg * 0.5f, -15f, 12f) : 0f;
        float pitch = climb * (1f - settle) + 9f * MathF.Sin(MathF.PI * settle);
        float roll = _arriveBank * (1f - settle);
        Geo.Axes(new Rotator(pitch, yaw, roll), out Vector3 fwd, out Vector3 right, out Vector3 up);
        Place(at, fwd, right, up);

        GroundBelow(at, out float ground, out Vector3 groundAt, out Vector3 groundNormal);
        UpdateEffects(settle, false, at, ground, groundAt, groundNormal, 0.4f + settle, _arriveVel, 0.6f, 1f, dt);

        if (t < 1f)
            return;

        EndVoice();

        if (_grappleT >= 0f)
        {
            GrappleStep(pawn, at, yaw, dt);
            return;
        }

        if (Opt.Grapple.On && pawn.Physics == Actor.EPhysics.PHYS_Walking && StartGrapple(pawn, pc, at))
            return;

        Board(pawn, at, yaw);
    }

    private bool StartGrapple(RPawnPlayer pawn, PlayerController pc, Vector3 at)
    {
        if (pc is not RPlayerController rpc || pawn.GrappleGun is not { IsValid: true } gun || gun.GrappleFireMove is not { IsValid: true } move)
            return false;

        Vector3 to = at - _up * (_half.Z + 80f);
        var flat = new Vector3(pawn.Location.X - to.X, pawn.Location.Y - to.Y, 0f);
        var loc = new REnvironmentCheckTicker.FEnvironmentSpecialMoveLocator
        {
            Location = to,
            Normal = flat.LengthSquared() > 1f ? Vector3.Normalize(flat) : -_fwd,
        };

        bool fired = false;
        Safe.Run("batwing grapple", () =>
        {
            gun.ActiveGrappleTarget = to;
            gun.ActiveOutwardNormal = loc.Normal;
            gun.ActiveHidePoint = null!;
            gun.ActiveSwingGrapple = false;
            RGrapplePoint.FGrapplePointInfo info = gun.ActiveGrapplePoint;
            info.bValid = true;
            info.OutwardNormal = loc.Normal;
            info.PointA = to;
            info.PointB = to;
            info.SourceGrapplePoint = null!;
            info.VantagePointGrapplePoint = null!;
            gun.ActiveGrapplePoint = info;
            move.TriggerSpecialMove(rpc, loc, false);
            fired = true;
        });
        if (!fired)
            return false;

        _grappleT = 0f;
        _grappleTo = to;
        _grappleYaw = Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Rotation.Yaw : _arriveYaw;
        Toast("GRAPPLE");
        Log.Info(Title + ": grapple fired at the belly, " + Fmt.Whole(Vector3.Distance(pawn.Location, to) / Metre) + " m up.");
        return true;
    }

    private void GrappleStep(RPawnPlayer pawn, Vector3 at, float yaw, float dt)
    {
        _grappleT += dt;
        float away = Vector3.Distance(pawn.Location, _grappleTo);
        bool standing = _grappleT > 1f && pawn.Physics == Actor.EPhysics.PHYS_Walking;
        bool late = _grappleT >= GrappleMost;
        if (away > GrappleReach && !standing && !late)
            return;

        if (!late && pawn.CurrentSpecialMove is RSpecialMoveInstance_FireGrapple { IsValid: true })
            return;

        if (standing || late)
            Log.Info(Title + ": the grapple didn't take (" + Fmt.Whole(away / Metre) + " m away after " + Fmt.Whole(_grappleT) + " s), boarding anyway.");
        _grappleT = -1f;
        EndGrapple();
        Board(pawn, at, yaw);
    }

    private static readonly FName FallingState = new("FallingMode");
    private static readonly FName[] LineStates = { new("GrappleMovement"), new("SpecialMove"), new("HangMode"), new("LedgeHanging") };

    private static void EndGrapple()
    {
        if (World.Pc is not { } pc)
            return;

        Safe.Run("batwing grapple end", () =>
        {
            foreach (FName state in LineStates)
            {
                if (!pc.IsInState(state, true))
                    continue;

                Log.Info(Title + ": leaving the " + state + " state.");
                pc.GotoState(FallingState, new FName("None"), false, false);
                return;
            }
        });
    }

    private const float LeaveSeconds = 9f;
    private const float LeaveFar = 30000f;

    private float _leaveT;
    private float _leaveWait;
    private float _leaveYaw;

    private void Depart(float wait)
    {
        if (_craft is not { IsValid: true } craft)
        {
            _phase = Phase.Off;
            return;
        }

        if (_phase == Phase.Arriving)
        {
            _fm.Place(craft.Location, _arriveYaw / Deg, _arriveVel);
            _fm.Forward = _fwd;
            _fm.Right = _right;
            _fm.Up = _up;
            _fm.Hover = _fm.Velocity.Length() < 2500f ? 1f : 0f;
            _fm.HoverWanted = _fm.Hover > 0.5f;
        }

        FadeVoice();
        _grappleT = -1f;
        _phase = Phase.Leaving;
        _leaveT = 0f;
        _leaveWait = wait;
        _leaveYaw = MathF.Atan2(_fm.Forward.Y, _fm.Forward.X);
    }

    private void LeaveStep(float dt)
    {
        if (_craft is not { IsValid: true })
        {
            DropCraft();
            _phase = Phase.Off;
            return;
        }

        if (dt <= 0f)
            return;

        _leaveT += dt;
        float t = _leaveT - _leaveWait;
        var input = new FlightInput { Heading = _leaveYaw, Assist = true };
        if (t > 0f)
        {
            if (_fm.HoverWanted && t < 1.6f)
            {
                input.Lift = 1f;
                input.Move = new Vector2(0f, 0.4f);
            }
            else
            {
                if (_fm.HoverWanted)
                {
                    _fm.HoverWanted = false;
                    WingSounds();
                }

                _fm.Throttle = 1f;
                input.Boost = t > 2.5f;
                input.Aim = Vector3.Normalize(new Vector3(MathF.Cos(_leaveYaw), MathF.Sin(_leaveYaw), 0.35f));
            }
        }
        else
        {
            input.Aim = _fm.Forward;
        }

        _fm.Step(dt, input);
        Place(_fm.Position, _fm.Forward, _fm.Right, _fm.Up);
        GroundBelow(_fm.Position, out float ground, out Vector3 groundAt, out Vector3 groundNormal);
        UpdateEffects(_fm.Hover, _fm.Boosting, _fm.Position, ground, groundAt, groundNormal, _fm.Jets, _fm.Velocity, _fm.Throttle, _fm.GLoad, dt);

        Vector3 eye = Hooks.PlayerCam() is { } cam ? cam.CameraCache.POV.Location : _lastPov.Location;
        if (_leaveT > LeaveSeconds || (_leaveT > 3f && Vector3.DistanceSquared(eye, _fm.Position) > LeaveFar * LeaveFar))
        {
            DropCraft();
            _phase = Phase.Off;
        }
    }
}
