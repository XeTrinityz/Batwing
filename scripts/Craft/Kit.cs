using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// Everything the Batwing is made of, found in the game's own packages. Loaded on the first call and kept
// until the player leaves the game.
internal sealed class Kit
{
    private const string Owner = "batwing kit";

    private const string CityPackage = "CityY__Ch3456789";
    private const string CinePackage = "CityX_10_Cine_Ch3cd_HeavyCannonDrop";
    private const string ShowcasePackage = "Showcase_Vehicles_Batwing";
    private const string RescuePackage = "Playable_BatmobileJoker_SF";

    private const string MeshPath = "Batwing_Vehicle.Batwing_Arkham_Knight.Batwing_Arkham_Knight";
    private const string ShowcaseMeshPath = "Batwing_Vehicle.Batwing_Arkham_Knight_Showcase.Batwing_Arkham_Knight_Showcase";
    public const string ShowcaseMeshName = "Batwing_Arkham_Knight_Showcase";
    private const string ExhaustPath = "VFX_Vehicles.Particles.Batwing.Batwing_Exhaust_INGAME";
    private const string ThrustersPath = "VFX_Cinematics.Particles.Vehicles.BatwingThrusters";
    private const string ThrusterBurstPath = "VFX_Cinematics.Particles.Vehicles.Batwing_ThrusterBurst_CINE";
    private const string BoosterPath = "VFX_Cinematics.Particles.Vehicles.Batwing_Booster_CINE";
    private const string DustPath = "VFX_Cinematics.Particles.Vehicles.FloorDust_Batwing";
    private const string ContrailPath = "VFX_Vehicles.Particles.Batwing_Exhaust_P";
    private const string LampPath = "VFX_Security.Particles.SentryGun.SentryGunLightCone_P";
    private const string PoseSetName = "Baked_Batwing_Arkham_Knight_Showcase_Anims_0";
    private const string FoldSetName = "Baked_Batwing_0";

    private const string RescueEnginePath = "Ww_SEQ_BatWing_VS.Play_BatWing_VS_Batwing";
    private const string RescueStopPath = "Ww_SEQ_BatWing_VS.Stop_AUT_Batwing_All";
    private const string FlightLoopPath = "Ww_VEH_Batwing.Play_VEH_Batwing_Fly_Loop_01";
    private const string CalledPath = "Ww_SEQ_Batwing_Triggered.Play_SEQ_Batwing_Triggered";
    private const string HatchClosePath = "Ww_BML_Batmobile.Play_CockpitClose";
    private const string HatchOpenPath = "Ww_BML_Batmobile.Play_CockpitOpen";
    private const string DodgePath = "Ww_BML_Batmobile.Play_Batmobile_Dodge";
    private const string LandPath = "Ww_BML_Batmobile_Subjective.Play_Batmobile_Jump_Land";
    private const string TransformInPath = "Ww_BML_Batmobile.Play_BattleMode_In_";
    private const string TransformOutPath = "Ww_BML_Batmobile.Play_BattleMode_Out_";
    private const string ImpactPath = "Ww_OBJ_Dest_MetalsVehicles.Play_Metal_Hollow_Large_Impacts";
    private const string ScrapePath = "Ww_OBJ_Dest_MetalsVehicles.Play_Metal_Hollow_Small_Impacts";
    private const string WreckPath = "Ww_OBJ_Dest_MetalsVehicles.Play_Metal_Heavy_Break";
    private const string SpeedParamPath = "Ww_Parameters.ObjectVelocity";
    private const string DopplerParamPath = "Ww_Parameters.Doppler";
    public const string BoardSound = "Ww_SBJ_Camera_Whooshes.Play_Cam_Whoosh16";
    public const string BailSound = "Ww_SBJ_Camera_Whooshes.Play_Cam_Whoosh14";
    public const string ModeSound = "Ww_SBJ_Camera_Whooshes.Play_Cam_Whoosh17_01";

    private static readonly string[] TransformParts = { "A", "B", "C" };

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

    private static Kit? _loaded;

    public SkeletalMesh Mesh = null!;
    public ParticleSystem? Exhaust, Thrusters, ThrusterBurst, Booster, Dust, Explosion, Impact, Contrail, Lamp;
    public AnimSet? Pose, Fold;
    public AkEvent? Rescue, RescueStop, Flight, Called, Hit, Scrape, Wreck, Boom, HatchOpen, HatchClose, Dodge, Land;
    public readonly AkEvent?[] TransformIn = new AkEvent?[TransformParts.Length];
    public readonly AkEvent?[] TransformOut = new AkEvent?[TransformParts.Length];
    public AkParameterName? Speed, Doppler;
    public readonly Dictionary<string, AkEvent?> Whooshes = new();

    public bool Showcase => Mesh.Name == ShowcaseMeshName;

    public static Kit Load()
    {
        if (_loaded is { } have && have.Mesh.IsValid)
            return have;

        if (ObjectTable.Headroom < ObjectTable.Full)
        {
            Safe.Run("collect before batwing", () => World.WorldInfo?.ForceGarbageCollection(true));
            throw new Refusal("the game is near its object limit, try again in a moment");
        }

        bool loadedLevel = false;
        if (Find<SkeletalMesh>(MeshPath) == null || Find<ParticleSystem>(ExhaustPath) == null || Find<AkEvent>(FlightLoopPath) == null)
            loadedLevel |= Package(CityPackage, "the exhaust and engine loop");

        if (Find<SkeletalMesh>(ShowcaseMeshPath) == null || FindSet(PoseSetName) == null)
            loadedLevel |= Package(ShowcasePackage, "the Batwing's model");

        SkeletalMesh mesh = Find<SkeletalMesh>(ShowcaseMeshPath) ?? Find<SkeletalMesh>(MeshPath)
            ?? throw new Refusal("the Batwing's model didn't load");

        if (Find<ParticleSystem>(ThrustersPath) == null || Find<ParticleSystem>(ThrusterBurstPath) == null || Find<ParticleSystem>(LampPath) == null
            || Find<ParticleSystem>(BoosterPath) == null || Find<AkEvent>(RescueEnginePath) == null || FindSet(FoldSetName) == null)
            loadedLevel |= Package(CinePackage, "the thrusters, landing light and fly-in sound");

        if (Find<ParticleSystem>(BoosterPath) == null || Find<AkEvent>(RescueEnginePath) == null || Find<ParticleSystem>(ContrailPath) == null)
            Package(RescuePackage, "the contrails");

        var kit = new Kit { Mesh = Roots.Keep(mesh, Owner) };
        kit.Exhaust = Kept(Find<ParticleSystem>(ExhaustPath));
        kit.Thrusters = Kept(Find<ParticleSystem>(ThrustersPath));
        kit.ThrusterBurst = Kept(Find<ParticleSystem>(ThrusterBurstPath));
        kit.Booster = Kept(Find<ParticleSystem>(BoosterPath));
        kit.Dust = Kept(Find<ParticleSystem>(DustPath));
        kit.Contrail = Kept(Find<ParticleSystem>(ContrailPath));
        kit.Lamp = Kept(Find<ParticleSystem>(LampPath));
        kit.Pose = Kept(FindSet(PoseSetName));
        kit.Fold = Kept(FindSet(FoldSetName));
        kit.Explosion = Kept(First<ParticleSystem>(ExplosionPaths));
        kit.Impact = Kept(First<ParticleSystem>(ImpactFxPaths));
        kit.Rescue = Kept(Find<AkEvent>(RescueEnginePath));
        kit.RescueStop = Kept(Find<AkEvent>(RescueStopPath));
        kit.Flight = Kept(Find<AkEvent>(FlightLoopPath));
        kit.Called = Kept(Find<AkEvent>(CalledPath));
        kit.Hit = Kept(Find<AkEvent>(ImpactPath));
        kit.Scrape = Kept(Find<AkEvent>(ScrapePath));
        kit.Wreck = Kept(Find<AkEvent>(WreckPath));
        kit.Boom = Kept(First<AkEvent>(BoomPaths));
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

        // A level package brings a whole district's worth of objects. Only the parts above are kept.
        if (loadedLevel)
            Safe.Run("batwing collect", () => World.WorldInfo?.ForceGarbageCollection(true));

        Log.Info("Batwing: parts ready (" + ObjectTable.Describe() + "): model " + (kit.Showcase ? "showcase" : "city") + ", exhaust " + Has(kit.Exhaust)
            + ", thrusters " + Has(kit.Thrusters) + "/" + Has(kit.ThrusterBurst) + ", afterburner " + Has(kit.Booster) + ", downwash " + Has(kit.Dust)
            + ", contrails " + Has(kit.Contrail) + ", landing light " + Has(kit.Lamp) + ", pose " + Has(kit.Pose) + "/" + Has(kit.Fold)
            + ", explosion " + Has(kit.Explosion) + ", engine loop " + Has(kit.Flight) + ", fly-in sounds " + Has(kit.Rescue) + "/" + Has(kit.Called)
            + ", transform sounds " + kit.TransformIn.Count(t => t != null) + "/" + kit.TransformOut.Count(t => t != null)
            + ", hatch " + Has(kit.HatchClose) + ", impact sounds " + Has(kit.Hit) + "/" + Has(kit.Scrape) + ", speed parameter "
            + (kit.Speed is { } speed ? Fmt.Whole(speed.MinValue) + ".." + Fmt.Whole(speed.MaxValue) : "no") + ".");
        _loaded = kit;
        return kit;
    }

    public static void Forget()
    {
        _loaded = null;
        Roots.Let(Owner);
    }

    private static bool Package(string name, string why)
    {
        Log.Info("Batwing: loading " + name + " for " + why + " (" + ObjectTable.Describe() + ").");
        Safe.Run("batwing package " + name, () => Game.LoadPackage(name));
        return true;
    }

    private static string Has(GameObject? o) => o != null ? "yes" : "no";

    private static T? Kept<T>(T? obj) where T : GameObject => Roots.KeepFound(obj, Owner);

    public static T? Find<T>(string path) where T : GameObject =>
        Safe.Get("find " + path, () => Game.FindObject<T>(path) is { IsValid: true } found ? found : null, null);

    private static T? First<T>(string[] paths) where T : GameObject
    {
        foreach (string path in paths)
        {
            if (Find<T>(path) is { } found)
                return found;
        }

        return null;
    }

    private static AnimSet? FindSet(string name)
    {
        try
        {
            foreach (AnimSet anims in World.All<AnimSet>())
            {
                if (anims.Name == name)
                    return anims;
            }
        }
        catch
        {
        }

        return null;
    }
}

// A request the player made that can't be done right now. The message goes on screen as it is.
internal sealed class Refusal(string message) : Exception(message);
