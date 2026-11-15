using System.Numerics;
using BmSDK.Engine;

namespace Trinity.Batwing;

// The Batwing climbs away and is gone once it's well out of sight.
internal sealed class Departing : Phase
{
    private const float MostSeconds = 9f;
    private const float FarAway = 30000f;
    private const float HoverBelow = 2500f;

    private readonly Craft _craft;
    private readonly float _wait;
    private readonly float _yaw;
    private float _t;

    private Departing(Craft craft, float wait)
    {
        _craft = craft;
        _wait = wait;
        _yaw = craft.Fm.Yaw();
    }

    public override string Status => "DEPARTING";

    public override Rgba Lamp => Palette.Dim;

    // Leaves after the wait, the Batmobile let go first.
    public static void From(Craft craft, float wait)
    {
        if (!craft.Alive)
        {
            Wing.Go(null);
            return;
        }

        // From a set move like the fly-in it picks up where the move left it.
        FlightModel fm = craft.Fm;
        if (Wing.Now is Inbound)
        {
            fm.Hover = fm.Velocity.Length() < HoverBelow ? 1f : 0f;
            fm.HoverWanted = fm.Hover > 0.5f;
        }

        if (craft.Cargo is { Alive: true } cargo)
        {
            cargo.Drop(Vector3.Zero, gently: true);
            craft.Cargo = null;
        }

        Wing.Voice.Fade();
        Wing.Go(new Departing(craft, wait));
    }

    public override void Step(BmSDK.Engine.PlayerController pc, float dt)
    {
        if (!_craft.Alive)
        {
            Wing.Go(null);
            return;
        }

        if (dt <= 0f)
            return;

        _t += dt;
        float t = _t - _wait;
        FlightModel fm = _craft.Fm;
        var input = new FlightInput { Heading = _yaw, Assist = Assist.Full };
        if (t <= 0f)
        {
            input.Aim = fm.Forward;
        }
        else if (fm.HoverWanted && t < 1.6f)
        {
            input.Lift = 1f;
            input.Move = new Vector2(0f, 0.4f);
        }
        else
        {
            if (fm.HoverWanted)
            {
                fm.HoverWanted = false;
                _craft.WingSounds();
            }

            fm.Throttle = 1f;
            input.Boost = t > 2.5f;
            input.Aim = Vector3.Normalize(new Vector3(MathF.Cos(_yaw), MathF.Sin(_yaw), 0.35f));
        }

        fm.Step(dt, input);
        _craft.Follow();
        _craft.Measure();
        _craft.Show(_craft.Flying(), dt);

        Vector3 eye = CameraHook.GameView() is { } pov ? pov.Location : fm.Position;
        if (_t > MostSeconds || (_t > 3f && Vector3.DistanceSquared(eye, fm.Position) > FarAway * FarAway))
            Wing.Go(null);
    }

    public override void Draw(Canvas canvas)
    {
    }
}
