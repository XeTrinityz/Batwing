using BmSDK;
using BmSDK.Engine;
using Sample = BmSDK.Engine.ForceFeedbackWaveform.FWaveformSample;
using Shape = BmSDK.Engine.ForceFeedbackWaveform.EWaveformFunction;

namespace Trinity.Batwing;

internal enum Buzz
{
    Gun,
    Riot,
    Cannon,
    Missile,
    Bump,
    Hit,
    Crash,
    Boost,
    Land,
    Hook,
    Mode,
}

// Controller rumble through the game's own force feedback, so it works on any pad the game supports and
// follows the game's vibration setting. The waveforms are made once and kept from the garbage collector,
// since the game keeps playing one after the call that started it.
internal static class Rumble
{
    private const string Owner = "rumble";

    private static readonly Dictionary<Buzz, ForceFeedbackWaveform> _waves = new();
    private static float _madeAt = -1f;
    private static Every _gunGap;
    private static bool _warned;

    public static void Play(Buzz kind)
    {
        if (!Opt.Rumble.On || Opt.RumbleStrength.Number <= 0f || !Controls.PadActive || World.Pc is not { } pc)
            return;
        if (kind is Buzz.Gun or Buzz.Riot && !_gunGap.Elapsed(0.07))
            return;

        Safe.Run("rumble", () =>
        {
            if (Wave(kind) is { } wave)
                pc.ClientPlayForceFeedbackWaveform(wave, null!);
        });
    }

    public static void Release()
    {
        _waves.Clear();
        _madeAt = -1f;
        Roots.Let(Owner);
    }

    private static ForceFeedbackWaveform? Wave(Buzz kind)
    {
        float strength = Opt.RumbleStrength.Number;
        if (MathF.Abs(strength - _madeAt) > 0.001f)
        {
            Release();
            _madeAt = strength;
        }

        if (_waves.TryGetValue(kind, out ForceFeedbackWaveform? have) && have.IsValid)
            return have;

        try
        {
            var wave = new ForceFeedbackWaveform(Package.GetTransientPackage());
            Roots.Keep(wave, Owner);
            foreach (Sample s in Samples(kind, strength))
                wave.Samples.Push(s);
            _waves[kind] = wave;
            return wave;
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Info("Rumble: couldn't make a waveform, rumble is off: " + ex.Message);
            }

            return null;
        }
    }

    // Left is the heavy motor, right the light one.
    private static IEnumerable<Sample> Samples(Buzz kind, float k) => kind switch
    {
        Buzz.Gun => new[] { S(30, 90, Shape.WF_Constant, 0.05f, k) },
        Buzz.Riot => new[] { S(50, 110, Shape.WF_LinearDecreasing, 0.09f, k) },
        Buzz.Cannon => new[] { S(220, 160, Shape.WF_Constant, 0.08f, k), S(160, 60, Shape.WF_LinearDecreasing, 0.3f, k) },
        Buzz.Missile => new[] { S(90, 140, Shape.WF_LinearDecreasing, 0.15f, k) },
        Buzz.Bump => new[] { S(80, 120, Shape.WF_Noise, 0.12f, k) },
        Buzz.Hit => new[] { S(170, 200, Shape.WF_Noise, 0.18f, k), S(90, 60, Shape.WF_LinearDecreasing, 0.25f, k) },
        Buzz.Crash => new[] { S(255, 255, Shape.WF_Noise, 0.35f, k), S(200, 120, Shape.WF_LinearDecreasing, 0.8f, k) },
        Buzz.Boost => new[] { S(60, 40, Shape.WF_LinearIncreasing, 0.2f, k), S(110, 70, Shape.WF_LinearDecreasing, 0.45f, k) },
        Buzz.Land => new[] { S(140, 70, Shape.WF_LinearDecreasing, 0.22f, k) },
        Buzz.Hook => new[] { S(120, 140, Shape.WF_Constant, 0.06f, k), S(60, 60, Shape.WF_LinearDecreasing, 0.2f, k) },
        Buzz.Mode => new[] { S(40, 90, Shape.WF_Sin0to180, 0.25f, k) },
        _ => Array.Empty<Sample>(),
    };

    private static Sample S(int left, int right, Shape shape, float seconds, float k) => new()
    {
        LeftAmplitude = (byte)Math.Clamp(left * k, 0f, 255f),
        RightAmplitude = (byte)Math.Clamp(right * k, 0f, 255f),
        LeftFunction = shape,
        RightFunction = shape,
        Duration = seconds,
    };
}
