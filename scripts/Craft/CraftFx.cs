using System.Numerics;
using BmSDK;
using BmSDK.Engine;

namespace Trinity.Batwing;

// How the craft is flying this frame, for the effects and sounds. The flight model fills it in, and the
// set moves (arriving, hooking, swooping) fill it in themselves.
internal struct Look
{
    public float Hover;
    public bool Boost;
    public float Jets;
    public Vector3 Velocity;
    public float Throttle;
    public float G;
}

internal sealed partial class Craft
{
    private const string PoseSetName = "Batwing_Arkham_Knight_Showcase";
    private static readonly FName PoseAnim = new(PoseSetName);
    private static readonly FName FoldAnim = new("CH03_Heavy_Cannon_Upgrade_BATWING");
    private const float PoseHoverTime = 3f;
    private const float FoldFrom = 2f, FoldTo = 4f;

    private static readonly FName ExhaustSpawn = new("ExhaustSpawn"), ExhaustOpacity = new("Opacity"), ExhaustSmoke = new("SmokeOpacity");
    private static readonly FName ThrusterSpawn = new("SideThrusters_R"), ThrusterSize = new("SizeScale"), FlareSize = new("ThrusterSize"), DustSpawn = new("SpawnRate1");

    private readonly record struct Mount(FName Bone, Vector3 Offset, Rotator Turn, Vector3? Scale = null);

    private static readonly Mount[] JetMounts =
    {
        new(LeftFront, new Vector3(0f, 0f, -19f), new Rotator(16.7f, -147.2f, 9f)),
        new(RightFront, new Vector3(0f, 0f, -19f), new Rotator(16.7f, 147.2f, -9f)),
        new(LeftRear, new Vector3(0f, 0f, -19f), new Rotator(19.8f, 28.3f, -2.6f)),
        new(RightRear, new Vector3(0f, 0f, -19f), new Rotator(19.8f, -28.3f, 2.6f)),
    };

    private static readonly Mount[] BrakeMounts =
    {
        new(Chassis, new Vector3(113.2f, 3.7f, -136.6f), new Rotator(0f, 90f, 10.2f), new Vector3(1.15f, 0.69f, 1.15f)),
        new(Chassis, new Vector3(-224f, 4.2f, -153.6f), new Rotator(0f, 90f, -15.8f), new Vector3(0.65f)),
    };

    private static readonly Mount LampMount = new(Chassis, new Vector3(512.6f, -0.4f, -142.5f), new Rotator(-35f, 0f, 0f));
    private const float LightBrightness = 4f, LightReach = 6000f, LightInner = 14f, LightOuter = 26f;
    private static readonly int[] ContrailEmitters = { 2, 3 };
    private const double BurstGap = 0.3;
    private const float DustReach = 4500f;

    private AnimNodeSequence? _pose;
    private bool _fold;
    private ParticleSystemComponent? _exhaust, _contrail, _lamp, _dust;
    private SpotLightComponent? _light;
    private readonly ParticleSystemComponent?[] _thrusters = new ParticleSystemComponent?[JetMounts.Length];
    private readonly ParticleSystemComponent?[] _flares = new ParticleSystemComponent?[BrakeMounts.Length];
    private readonly ParticleSystemComponent?[] _bursts = new ParticleSystemComponent?[4];
    private bool _exhaustOn = true, _trailsOn, _lampOn, _dustOn, _boosterOn, _flareOn;
    private float _engineLevel, _thrustLevel;
    private double _flareUntil, _burstDue;
    private int _nextBurst;

    public bool LampOn => _lampOn;

    private void BuildEffects()
    {
        if (Kit.Exhaust is { } exhaust)
            _exhaust = Effect(exhaust, true);

        if (Kit.Booster is { } booster)
        {
            for (int i = 0; i < _bursts.Length; i++)
                _bursts[i] = Effect(booster, false);
        }

        for (int i = 0; i < JetMounts.Length; i++)
        {
            if (Kit.Thrusters is { } thrusters)
                _thrusters[i] = OnBone(thrusters, JetMounts[i], true);
        }

        if (Kit.ThrusterBurst is { } flare)
        {
            for (int i = 0; i < BrakeMounts.Length; i++)
            {
                _flares[i] = OnBone(flare, BrakeMounts[i], false);
                _flares[i]?.SetVectorParameter(FlareSize, new Vector3(0.7f, 0.7f, 0.7f));
            }
        }

        if (Kit.Contrail is { } contrail)
            _contrail = Effect(contrail, false);

        if (Kit.Dust is { } dust)
        {
            _dust = Effect(dust, false);
            _dust?.SetAbsolute(true, true, false);
        }

        if (Kit.Lamp is { } lamp)
            _lamp = OnBone(lamp, LampMount, false);
        _light = Light();
    }

    private SpotLightComponent? Light() => Safe.Get("batwing landing light", () =>
    {
        var c = new SpotLightComponent(Actor)
        {
            Brightness = LightBrightness,
            LightColor = new GameObject.FColor { R = 255, G = 246, B = 232, A = 255 },
            Radius = LightReach,
            InnerConeAngle = LightInner,
            OuterConeAngle = LightOuter,
            CastShadows = false,
        };
        _body.AttachComponent(c, LampMount.Bone, LampMount.Offset, LampMount.Turn, Vector3.One);
        c.SetEnabled(false);
        return c;
    }, null);

    private ParticleSystemComponent? OnBone(ParticleSystem template, Mount mount, bool on) => Safe.Get("batwing jet effect", () =>
    {
        var c = new ParticleSystemComponent(Actor) { bAutoActivate = false };
        c.SetTemplate(template);
        _body.AttachComponent(c, mount.Bone, mount.Offset, mount.Turn, mount.Scale ?? Vector3.One);
        if (on)
            c.ActivateSystem(true, false);
        return c;
    }, null);

    private ParticleSystemComponent? Effect(ParticleSystem template, bool on) => Safe.Get("batwing effect", () =>
    {
        var c = new ParticleSystemComponent(Actor) { bAutoActivate = false };
        c.SetTemplate(template);
        Actor.AttachComponent(c);
        if (on)
            c.ActivateSystem(true, false);
        return c;
    }, null);

    // The braking thrusters under the nose, for a moment.
    public void Flare(float seconds)
    {
        foreach (ParticleSystemComponent? flare in _flares)
        {
            if (flare is { IsValid: true })
                flare.ActivateSystem(true, false);
        }

        _flareOn = true;
        _flareUntil = Clock.Now + seconds;
    }

    public bool Flaring => _flareOn;

    public Look Flying() => new()
    {
        Hover = Fm.Hover,
        Boost = Fm.Boosting,
        Jets = Fm.Jets,
        Velocity = Fm.Velocity,
        Throttle = Fm.Throttle,
        G = Fm.GLoad,
    };

    // Moves the effects and sounds along with how it's flying.
    public void Show(in Look look, float dt)
    {
        if (!Alive || !(dt > 0f))
            return;

        bool boost = look.Boost && look.Hover < 0.5f;
        Drift.Step(this, look.Hover, look.Velocity, Below.Distance, dt);

        bool fast = look.Hover < 0.5f && look.Velocity.LengthSquared() > 4000f * 4000f;
        bool trails = boost || fast && (_trailsOn ? look.G > 1.4f : look.G > 1.7f);
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

        bool lamp = look.Hover > 0.5f && Below.Distance < LightReach;
        if (lamp != _lampOn)
        {
            if (_light is { IsValid: true } beam)
                Safe.Run("batwing landing light", () => beam.SetEnabled(lamp));
            if (_lamp is { IsValid: true } cone)
            {
                if (lamp)
                    cone.ActivateSystem(true, false);
                else
                    cone.DeactivateSystem();
            }
        }

        _lampOn = lamp;

        float ease = Ease.Follow(5f, dt);
        float engine = look.Hover > 0.5f ? 0.1f + 0.15f * look.Jets : 0.4f + 0.6f * look.Throttle;
        float thrust = look.Hover > 0.5f ? 0.3f + 0.4f * look.Jets : 0.35f + 0.25f * look.Throttle;
        if (boost)
        {
            engine = 1f;
            thrust = 0.7f;
        }

        _engineLevel += (engine - _engineLevel) * ease;
        _thrustLevel += (thrust - _thrustLevel) * ease;

        bool burn = look.Hover < 0.5f || boost;
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

            exhaust.SetTranslation(_exhaustAt);
            exhaust.SetRotation(_exhaustTurn);
            exhaust.SetScale3D(_exhaustScale);
            exhaust.SetFloatParameter(ExhaustSpawn, _engineLevel);
            exhaust.SetFloatParameter(ExhaustOpacity, boost ? 1.6f : 0.6f + 0.8f * _engineLevel);
            exhaust.SetFloatParameter(ExhaustSmoke, _engineLevel);
        }

        foreach (ParticleSystemComponent? thruster in _thrusters)
        {
            if (thruster is not { IsValid: true })
                continue;
            thruster.SetFloatParameter(ThrusterSpawn, _thrustLevel);
            float size = 0.4f + 0.35f * _thrustLevel;
            thruster.SetVectorParameter(ThrusterSize, new Vector3(size, size, 0f));
        }

        if (_flareOn && Clock.Now >= _flareUntil)
        {
            _flareOn = false;
            foreach (ParticleSystemComponent? flare in _flares)
            {
                if (flare is { IsValid: true })
                    flare.DeactivateSystem();
            }
        }

        // The model folds its wings down for hover.
        if (_pose is { IsValid: true } pose)
        {
            float h = Math.Clamp(look.Hover, 0f, 1f);
            pose.SetPosition(_fold ? FoldFrom + h * (FoldTo - FoldFrom) : h * PoseHoverTime, false);
        }

        Actor.Velocity = look.Velocity;
        EngineSpeed(look.Velocity);
        PlayCues();

        foreach (ParticleSystemComponent? burst in _bursts)
        {
            if (burst is not { IsValid: true })
                continue;
            burst.SetTranslation(_exhaustAt);
            burst.SetRotation(_exhaustTurn);
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
        {
            BoostSound(boost);
            if (boost)
                Rumble.Play(Buzz.Boost);
        }

        _boosterOn = boost;

        bool dust = Below.Distance < DustReach && look.Jets > 0.25f && look.Hover > 0.2f;
        if (_dust is { IsValid: true } d)
        {
            if (dust)
            {
                d.SetTranslation(Below.Point);
                d.SetRotation(Rotator.FromDirection(Below.Normal));
                d.SetFloatParameter(DustSpawn, Math.Clamp(1.3f - Below.Distance / DustReach, 0.25f, 1f));
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

    private static void Contrails(ParticleSystemComponent c) => Safe.Run("batwing contrails", () =>
    {
        c.ActivateSystem(true, false);
        for (int i = 0; i < 11; i++)
        {
            if (Array.IndexOf(ContrailEmitters, i) < 0)
                c.SetStopSpawning(i, true);
        }
    });
}
