using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using Pov = BmSDK.GameObject.FTPOV;

namespace Trinity.Batwing;

internal static class Hooks
{
    private static bool _povWritten;
    private static nint _povCam;
    private static Pov _povGame, _povMine;

    private static bool _hudHidden;
    private static nint _hudPtr;
    private static Every _hudEvery;

    internal static R3rdPersonCamera? PlayerCam() =>
        World.Pc?.PlayerCamera is R3rdPersonCamera { IsValid: true, IsClassDefaultObject: false } cam ? cam : null;

    internal static bool IsPlayerView(Camera cam, PlayerController pc)
    {
        if (!Mine(cam.ViewTarget.Target, pc))
            return false;

        Actor? pending = cam.PendingViewTarget.Target;
        return pending is not { IsValid: true } || Mine(pending, pc);
    }

    private static bool Mine(Actor? target, PlayerController pc) => target is { IsValid: true } && (target.Ptr == pc.Ptr || target is Pawn);

    internal static bool InCinematic(PlayerController pc) =>
        pc.bCinematicMode || pc is RPlayerControllerBase { ActiveCinematicMode: { IsValid: true } };

    [Redirect(typeof(R3rdPersonCamera), nameof(R3rdPersonCamera.UpdateCamera))]
    private static void UpdateCameraRedirect(R3rdPersonCamera self, float deltaTime)
    {
        PlayerController? pc = null;
        bool ours = false;
        try
        {
            ours = Before(self, out pc);
        }
        catch (Exception ex)
        {
            Log.Error("camera before", "Camera update (before)", ex);
        }

        self.UpdateCamera(deltaTime);

        if (!ours || pc == null)
            return;

        try
        {
            After(self, pc, deltaTime);
        }
        catch (Exception ex)
        {
            Log.Error("camera after", "Camera update (after)", ex);
        }
    }

    private static bool Before(R3rdPersonCamera self, out PlayerController? pc)
    {
        pc = null;
        R3rdPersonCamera? cam = PlayerCam();
        if (cam == null || cam.Ptr != self.Ptr || World.Pc is not { } player)
        {
            _povWritten = false;
            return false;
        }

        pc = player;
        if (_povWritten && _povCam == cam.Ptr)
        {
            ref Camera.FTCameraCache cache = ref cam.CameraCache;
            if (SamePov(cache.POV, _povMine))
                cache.POV = _povGame;
        }

        _povWritten = false;
        return true;
    }

    private static void After(R3rdPersonCamera cam, PlayerController pc, float gameDt)
    {
        ref Camera.FTCameraCache cache = ref cam.CameraCache;
        Pov game = cache.POV;
        Pov pov = game;
        if (!Wing.I.View(cam, pc, game, ref pov, gameDt))
            return;

        cache.POV = pov;
        _povGame = game;
        _povMine = pov;
        _povCam = cam.Ptr;
        _povWritten = true;
    }

    private static bool SamePov(in Pov a, in Pov b)
    {
        Rotator ra = a.Rotation, rb = b.Rotation;
        return Vector3.DistanceSquared(a.Location, b.Location) < 0.01f
            && MathF.Abs(ra.Pitch - rb.Pitch) < 0.01f && MathF.Abs(ra.Yaw - rb.Yaw) < 0.01f && MathF.Abs(ra.Roll - rb.Roll) < 0.01f;
    }

    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.TakeDamage))]
    private static void TakeDamageRedirect(RPawnPlayer self, int damage, Controller instigatedBy, Vector3 hitLocation, Vector3 momentum, Class damageType, Actor.FTraceHitInfo hitInfo, Actor damageCauser)
    {
        if (Safe.Try("batwing fire", () => Wing.I.Absorbs(self, damage, damageType, hitLocation)))
            return;

        self.TakeDamage(damage, instigatedBy, hitLocation, momentum, damageType, hitInfo, damageCauser);
    }

    public static void HudTick()
    {
        bool want = Wing.I.HidesHud;
        if (!want && !_hudHidden)
            return;

        RGFxMovieModularHud? hud = World.Pc?.ModularHud is { IsValid: true } h ? h : null;
        if (want)
        {
            if (hud == null || (_hudHidden && hud.Ptr == _hudPtr && !_hudEvery.Elapsed(0.25)))
                return;

            hud.ManuallySetHudVisibility(false);
            _hudHidden = true;
            _hudPtr = hud.Ptr;
            return;
        }

        _hudHidden = false;
        if (hud != null && hud.Ptr == _hudPtr)
            hud.ManuallySetHudVisibility(true);
    }

    public static void Shutdown()
    {
        if (_povWritten && PlayerCam() is { } cam && cam.Ptr == _povCam)
        {
            ref Camera.FTCameraCache cache = ref cam.CameraCache;
            if (SamePov(cache.POV, _povMine))
                cache.POV = _povGame;
        }

        _povWritten = false;
        if (_hudHidden && World.Pc?.ModularHud is { IsValid: true } hud && hud.Ptr == _hudPtr)
            hud.ManuallySetHudVisibility(true);
        _hudHidden = false;
    }
}
