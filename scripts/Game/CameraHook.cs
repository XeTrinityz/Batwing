using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using Pov = BmSDK.GameObject.FTPOV;

namespace Trinity.Batwing;

// Batman's camera update. The Batwing steps from here, since it's the smoothest clock the game offers, and
// while it's flown the view the camera worked out is swapped for ours. What the game wrote is put back
// before its next update, so its own camera logic never builds on our view.
internal static class CameraHook
{
    private static bool _written;
    private static nint _cam;
    private static Pov _game, _mine;

    // When the camera last updated. Something else steps the Batwing if it goes quiet.
    public static double LastUpdate { get; private set; }

    public static R3rdPersonCamera? PlayerCam() =>
        World.Pc?.PlayerCamera is R3rdPersonCamera { IsValid: true, IsClassDefaultObject: false } cam ? cam : null;

    public static Pov? GameView() => PlayerCam() is { } cam ? cam.CameraCache.POV : null;

    public static float ViewYaw(Actor fallback) => GameView() is { } pov ? pov.Rotation.Yaw : fallback.Rotation.Yaw;

    // The camera is following Batman, not a cutscene camera or some other target.
    public static bool OnPlayer(Camera cam, PlayerController pc)
    {
        if (!Mine(cam.ViewTarget.Target, pc))
            return false;

        Actor? pending = cam.PendingViewTarget.Target;
        return pending is not { IsValid: true } || Mine(pending, pc);
    }

    private static bool Mine(Actor? target, PlayerController pc) => target is { IsValid: true } && (target.Ptr == pc.Ptr || target is Pawn);

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
            Log.Error("camera before", ex);
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
            Log.Error("camera after", ex);
        }
    }

    private static bool Before(R3rdPersonCamera self, out PlayerController? pc)
    {
        pc = null;
        if (PlayerCam() is not { } cam || World.Pc is not { } player)
        {
            _written = false;
            return false;
        }

        if (cam.Ptr != self.Ptr)
            return false;

        pc = player;
        Restore(cam);
        return true;
    }

    private static void After(R3rdPersonCamera cam, PlayerController pc, float dt)
    {
        LastUpdate = Clock.Now;
        ref Camera.FTCameraCache cache = ref cam.CameraCache;
        Pov game = cache.POV;
        Pov pov = game;
        if (!Wing.View(cam, pc, game, ref pov, dt))
            return;

        cache.POV = pov;
        _game = game;
        _mine = pov;
        _cam = cam.Ptr;
        _written = true;
    }

    // Puts the game's view back if ours is still the one in the cache.
    private static void Restore(R3rdPersonCamera cam)
    {
        if (_written && _cam == cam.Ptr)
        {
            ref Camera.FTCameraCache cache = ref cam.CameraCache;
            if (Same(cache.POV, _mine))
                cache.POV = _game;
        }

        _written = false;
    }

    private static bool Same(in Pov a, in Pov b)
    {
        Rotator ra = a.Rotation, rb = b.Rotation;
        return Vector3.DistanceSquared(a.Location, b.Location) < 0.01f
            && MathF.Abs(ra.Pitch - rb.Pitch) < 0.01f && MathF.Abs(ra.Yaw - rb.Yaw) < 0.01f && MathF.Abs(ra.Roll - rb.Roll) < 0.01f;
    }

    public static void Shutdown()
    {
        if (PlayerCam() is { } cam)
            Restore(cam);
        _written = false;
    }
}

internal static class DamageHook
{
    // Gunfire meant for Batman while he's in the cockpit hits the Batwing instead.
    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.TakeDamage))]
    private static void TakeDamageRedirect(RPawnPlayer self, int damage, Controller instigatedBy, Vector3 hitLocation, Vector3 momentum, Class damageType, Actor.FTraceHitInfo hitInfo, Actor damageCauser)
    {
        if (Safe.Try("batwing takes fire", () => Wing.Absorbs(self, damage, damageType, hitLocation)))
            return;

        self.TakeDamage(damage, instigatedBy, hitLocation, momentum, damageType, hitInfo, damageCauser);
    }
}

// Hides the game's own HUD while flying, if asked. Whatever it was before is what goes back.
internal static class GameHud
{
    private static bool _hidden;
    private static bool _was;
    private static nint _hud;
    private static Every _check;

    public static void Tick(bool want)
    {
        if (!want && !_hidden)
            return;

        RGFxMovieModularHud? hud = World.Pc?.ModularHud is { IsValid: true } h ? h : null;
        if (want)
        {
            if (hud == null)
                return;

            bool mine = _hidden && hud.Ptr == _hud;
            if (mine && !_check.Elapsed(0.25))
                return;

            // The game turns its HUD back on by itself when a radio call or dialogue camera ends, and then on
            // is what it wants back afterwards.
            bool on = hud.bHudVisibilityManual;
            _was = on || (mine && _was);
            if (!mine || on)
                hud.ManuallySetHudVisibility(false);
            _hidden = true;
            _hud = hud.Ptr;
            return;
        }

        _hidden = false;
        if (hud != null && hud.Ptr == _hud && _was)
            hud.ManuallySetHudVisibility(true);
    }

    public static void Shutdown()
    {
        if (_hidden && _was && World.Pc?.ModularHud is { IsValid: true } hud && hud.Ptr == _hud)
            hud.ManuallySetHudVisibility(true);
        _hidden = false;
    }
}
