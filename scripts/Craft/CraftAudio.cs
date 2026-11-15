using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Craft
{
    private const float TransformGap = 0.3f;

    private readonly List<(double At, AkEvent Ev)> _cues = new();
    private AkEvent? _engine, _boostLoop;
    private bool _speedWired;

    public void StartEngine()
    {
        if (_engine != null || !Alive || Kit.Flight is not { IsValid: true } loop)
            return;

        Safe.Run("batwing engine", () => Actor.AddLoopingAudioEvent(loop));
        _engine = loop;
        WireSpeed();
    }

    // The engine loop's pitch follows the speed. The audible works that out itself when it's set up for it,
    // otherwise the speed is handed over every frame.
    private void WireSpeed()
    {
        if (_speedWired || Kit.Speed is not { IsValid: true } speed)
            return;

        Safe.Run("batwing engine pitch", () =>
        {
            if (Actor.DefaultAkAudible is not { IsValid: true } audible)
                return;

            var setup = GameObject.ConstructObject<RAkAudibleParameters>(Actor);
            setup.ObjectVelocityParameter = speed;
            if (Kit.Doppler is { IsValid: true } doppler)
                setup.DopplerParameter = doppler;
            setup.DopplerScale = 100f;
            audible.ParameterSetup = setup;
            _speedWired = true;
        });
    }

    private void EngineSpeed(Vector3 velocity)
    {
        if (_speedWired || Kit.Speed is not { IsValid: true } speed)
            return;

        float v = velocity.Length() / Geo.Metre;
        Safe.Run("batwing engine pitch", () => Actor.SetAudioParameter(speed, speed.MaxValue > speed.MinValue ? Math.Clamp(v, speed.MinValue, speed.MaxValue) : v));
    }

    private void StopSounds()
    {
        _cues.Clear();
        BoostSound(false);
        AkEvent? engine = _engine;
        _engine = null;
        if (engine is { IsValid: true } && Alive)
            Safe.Run("batwing engine", () => Actor.RemoveLoopingAudioEvent(engine));
    }

    public void Sound(AkEvent? ev) => Play(ev, Alive ? Actor : World.Pawn);

    public static void Play(AkEvent? ev, Actor? on)
    {
        if (ev is not { IsValid: true } || on is not { IsValid: true })
            return;

        Safe.Run("batwing sound", () => on.StartAudioEvent(ev, 0, 0));
    }

    public void Whoosh(string path)
    {
        if (Kit.Whooshes.TryGetValue(path, out AkEvent? ev))
            Sound(ev);
    }

    public void Puff() => Sound(Kit.Dodge);

    public void Hatch(bool close)
    {
        AkEvent? ev = close ? Kit.HatchClose : Kit.HatchOpen;
        if (ev is { IsValid: true })
            Sound(ev);
        else
            Whoosh(close ? Kit.BoardSound : Kit.BailSound);
    }

    // The Batmobile's battle mode sounds play as the wings fold, in three parts.
    public void HoverSounds()
    {
        Puff();
        Transform(Kit.TransformIn);
        Rumble.Play(Buzz.Mode);
    }

    public void WingSounds()
    {
        Transform(Kit.TransformOut);
        Rumble.Play(Buzz.Mode);
    }

    private void Transform(AkEvent?[] parts)
    {
        _cues.Clear();
        if (parts.All(p => p == null))
        {
            Whoosh(Kit.ModeSound);
            return;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] is { IsValid: true } part)
                _cues.Add((Clock.Now + i * TransformGap, part));
        }
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

    // The Batmobile's boost sound, which the player's upgrades already picked.
    private void BoostSound(bool on)
    {
        AkPlayerVehicleSoundInfo? info = Safe.Get("batwing boost sound", Batmobile.Sounds, null);
        if (on)
        {
            if (_boostLoop != null || !Alive || info?.BoostStartEvent is not { IsValid: true } start)
                return;
            Safe.Run("batwing boost sound", () => Actor.AddLoopingAudioEvent(start));
            _boostLoop = start;
            return;
        }

        AkEvent? loop = _boostLoop;
        _boostLoop = null;
        if (loop == null || !Alive)
            return;
        if (loop.IsValid)
            Safe.Run("batwing boost sound", () => Actor.RemoveLoopingAudioEvent(loop));
        if (info?.BoostKillEvent is { IsValid: true } kill)
            Sound(kill);
    }
}

// The fly-in's own sound, the one the game plays when the Batwing comes to the rescue. It's played on a
// second, empty Batwing that follows the real one, so it can be faded out by flying it away once the real
// one has arrived, which is the only way to fade a sound the game started.
internal sealed class Voice
{
    private const float FadeSeconds = 7f;
    private const float FadeDistance = 300000f;
    private const float LifeSeconds = 40f;
    private const double StopGrace = 1.5;

    private RBatwing? _actor;
    private float _fade = -1f;
    private Vector3 _from;
    private double _killAt = -1.0;

    public bool Playing => _actor is { IsValid: true };

    public void Start(AkEvent? ev, Vector3 at)
    {
        Stop();
        if (ev is not { IsValid: true } || World.Pawn is not { } spawner)
            return;

        Safe.Run("batwing fly-in sound", () =>
        {
            if (spawner.Spawn(RBatwing.StaticClass(), null!, Craft.Tag, at, new Rotator(0f, 0f, 0f), null!, true) is not RBatwing { IsValid: true } voice)
                return;

            voice.SetPhysics(Actor.EPhysics.PHYS_None, false);
            voice.SetCollision(false, false, true);
            voice.bCollideWorld = false;
            _actor = voice;
            _fade = -1f;
            voice.StartAudioEvent(ev, 0, 0);
        });
    }

    // Arrived. The game's stop event ends it cleanly, a moment later the actor goes.
    public void End(AkEvent? stop)
    {
        if (_actor is not { IsValid: true } voice || _fade >= 0f || _killAt > 0.0)
            return;

        if (stop is { IsValid: true })
            Safe.Run("batwing fly-in sound", () => voice.StartAudioEvent(stop, 0, 0));
        _killAt = Clock.Now + StopGrace;
    }

    public void Fade()
    {
        if (_actor is not { IsValid: true } voice || _fade >= 0f || _killAt > 0.0)
            return;

        _fade = 0f;
        _from = voice.Location;
    }

    public void Step(Craft? follow, float dt)
    {
        if (_actor is not { IsValid: true } voice)
        {
            _actor = null;
            _killAt = -1.0;
            return;
        }

        if (_killAt > 0.0 && Clock.Now >= _killAt)
        {
            Stop();
            return;
        }

        if (_fade < 0f)
        {
            if (follow is { Alive: true })
                voice.SetLocation(follow.Actor.Location);
            else
                Fade();
            return;
        }

        _fade += dt;
        if (_fade >= LifeSeconds)
        {
            Stop();
            return;
        }

        float t = MathF.Min(_fade / FadeSeconds, 1f);
        voice.SetLocation(_from + new Vector3(0f, 0f, FadeDistance * Ease.In(t)));
    }

    public void Stop()
    {
        RBatwing? voice = _actor;
        _actor = null;
        _fade = -1f;
        _killAt = -1.0;
        if (voice is { IsValid: true, bDeleteMe: false })
            Safe.Run("batwing fly-in sound", () => voice.Destroy());
    }
}
