using System.Numerics;
using System.Runtime.CompilerServices;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// In the Batmobile the game shows its hologram over the dashboard, and a call moves the camera
// into the cockpit and holds the car still. This does the same for the deck. The deck already
// holds the car's controls while it is open.
internal static class Cockpit
{
    private static readonly FName ChassisBone = new("Batmobile_Chassis_Mesh");
    private static readonly FName Hold = new("InCar_GauntletConversationCam");
    private const string CameraAnims = "Anim_Batman_Batmobile.Code.Camera";

    // The game's own figures for a call in the car, except the way in. A call swoops in with a
    // set move that can't be steered. The deck blends straight to the shot, so the view can be
    // turned at once.
    private const float FieldOfView = 50f;
    private const float BlendInSeconds = 1.25f;
    private const float BlendOutSeconds = 0.5f;

    // How hard the car is slowed, in units a second each second, and when it counts as stopped.
    private const float Braking = 3000f;
    private const float StoppedSpeed = 60f;

    private static RVehicleBatmobileBase? _car;
    private static RInGameCinematicCam? _camera;
    private static AnimSet? _anims;
    private static bool _mirrored;
    private static float _startYaw;
    private static bool _closeBefore;
    private static bool _warned;

    public static void Enter(RPlayerController pc, RVehicleBatmobileBase car)
    {
        _car = car;
        car.EmergencyBrake();

        if (pc.CinematicCamera is not { IsValid: true } camera || pc.PlayerCamera is not R3rdPersonCamera { IsValid: true } view)
            return;

        _anims ??= Game.FindObject<AnimSet>(CameraAnims);
        if (_anims is not { IsValid: true })
        {
            if (!_warned)
            {
                _warned = true;
                Log.Info("The Batmobile's call camera wasn't found, so the camera stays where it is.");
            }

            return;
        }

        Follow(camera, car);
        camera.FOVAngle = FieldOfView;
        camera.bUseAspectRatioCorretion = false;
        camera.bExitPlayingWhenFinished = false;

        // The camera comes in from whichever side of the car it was on.
        pc.GetPlayerViewPoint(out Vector3 eye, out _);
        Matrix4x4 place = Unsafe.As<GameObject.FMatrix, Matrix4x4>(ref car.Mesh.LocalToWorld);
        var right = new Vector3(place.M21, place.M22, place.M23);
        _mirrored = Vector3.Dot(right, eye - car.Location) < 0f;

        // The held shot is one turn round the seat. Turning the view moves the camera along it,
        // from where the view pointed when the shot began.
        camera.StaticAnim(Hold, _mirrored, _anims);
        _startYaw = pc.Rotation.Yaw;

        // The camera hides whatever comes close to the lens. On the way in the car itself is that
        // close, and it would vanish, so nothing is hidden in the cockpit.
        _closeBefore = view.DontDrawClosePawns;
        view.DontDrawClosePawns = false;
        view.BlendToCameraTarget(camera, BlendInSeconds);
        pc.SetRainClippingDistForCockpitView(true);
        _camera = camera;
    }

    public static void Tick(RPlayerController pc, float dt)
    {
        if (_car is not { IsValid: true } car)
            return;

        Brake(car, dt);
        if (_camera is not { IsValid: true } camera)
            return;

        Follow(camera, car);

        float turns = (pc.Rotation.Yaw - _startYaw) / 360f;
        float along = turns - MathF.Floor(turns);

        // Unmirrored the shot runs the other way round.
        if (!_mirrored && along != 0f)
            along = 1f - along;

        camera.SetNormalizedTime(along);
    }

    // Gives the camera back.
    public static void Leave(RPlayerController? pc)
    {
        RInGameCinematicCam? camera = _camera;
        _camera = null;
        _car = null;

        if (pc is not { IsValid: true } || camera is not { IsValid: true })
            return;

        camera.Finished();
        if (pc.PlayerCamera is R3rdPersonCamera { IsValid: true } view)
        {
            if (view.GetTargetActor() is { } target && target.Ptr == camera.Ptr)
                view.BlendToCameraTarget(pc, BlendOutSeconds, true);

            view.DontDrawClosePawns = _closeBefore;
        }

        pc.SetRainClippingDistForCockpitView(false);
    }

    // The shot is authored round the car's chassis and moves with it.
    private static void Follow(RInGameCinematicCam camera, RVehicleBatmobileBase car)
    {
        camera.RootLocation = car.Mesh.GetBoneLocation(ChassisBone, 0);
        camera.RootRotation = car.Rotation;
    }

    // Slows the car to a stop along the ground.
    private static void Brake(RVehicleBatmobileBase car, float dt)
    {
        if (car.CollisionComponent is not { IsValid: true } body)
            return;

        var flat = new Vector3(car.Velocity.X, car.Velocity.Y, 0f);
        float speed = flat.Length();
        if (speed < StoppedSpeed)
            return;

        body.SetRBLinearVelocity(-flat / speed * MathF.Min(Braking * dt, speed), true);
    }
}
