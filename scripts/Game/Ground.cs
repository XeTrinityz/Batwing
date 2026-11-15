using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal static class Ground
{
    private const float WalkableNormal = 0.6f;
    private const float FloorGap = 15f;
    private const int FloorTraces = 8;

    private static readonly FName StandingPose = new("Standing");
    private static readonly FName RelaxedPose = new("Relaxed");

    // A trace against the world only. A hit at the origin is the engine's way of saying nothing.
    public static bool Ray(Actor from, Vector3 start, Vector3 end, out Vector3 hit, out Vector3 normal)
    {
        Actor? hitActor = from.Trace(out hit, out normal, end, start, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0);
        return hitActor != null && hit != Vector3.Zero;
    }

    public static bool Clear(Actor from, Vector3 start, Vector3 end) => !Ray(from, start, end, out _, out _);

    public static bool SkyAbove(Actor from, Vector3 feet, float reach) =>
        Clear(from, feet + new Vector3(0f, 0f, 100f), feet + new Vector3(0f, 0f, reach));

    public static float Lift(Pawn mover) => mover.GetCollisionHeight() + 40f;

    public static void Settle(Pawn mover)
    {
        if (mover is RPawnPlayer batman)
            Safe.Run("player pose", () => batman.ForceChangePose(StandingPose, RelaxedPose, default, false, default, default, default, 0.001f));
        Safe.Run("player moved", () => World.Pc?.OnPlayerHasBeenMoved());
    }

    public static bool InCity(Vector3 at) => Districts.At(at) != null;

    // Enters a district volume so the game streams the city round it. False when streaming is busy and the
    // caller should try again shortly.
    public static bool Enter(string? level)
    {
        if (World.Ri is not { } ri || level == null || !level.StartsWith("City", StringComparison.OrdinalIgnoreCase)
            || Districts.Volume(level) is not { } volume)
            return true;

        RLevelVolume? old = ri.CurrentLevelVolume is { IsValid: true } v ? v : null;
        if (old?.Ptr == volume.Ptr)
            return true;

        // Entering a volume while it or a neighbour it links to is being streamed out is a hard crash inside
        // EnteredLevelVolume, which no try/catch can stop.
        if (Busy(ri, volume) is { } why)
        {
            Log.Throttled("district wait", "Districts: holding off entering " + level + ", " + why + ".");
            return false;
        }

        ri.RemovePredictedLevel();
        volume.EnteredLevelVolume();
        if (old == null)
            return true;

        // The game only switches a volume on if it's off. One left on keeps its levels loaded and isn't
        // entered properly the next time.
        if (Busy(ri, old) is { } left)
            Log.Detail("Districts: left " + old.Level + " switched on, " + left + ".");
        else
            old.LevelVolumeDeactivated();
        return true;
    }

    private static string? Busy(RGameRI ri, RLevelVolume volume)
    {
        if (ri.LevelStreamingLocked)
            return "streaming is locked by " + (ri.LevelStreamingLockedBy.ToString() is { Length: > 0 } by ? by : "the game");
        if (ri.LevelStreamingInitialising)
            return "streaming is starting up";
        if (Gone(ri, volume))
            return "its volume is going away";

        // A district volume always sits in a streamed level. Once that level has unloaded the actor lives on
        // until the next collection, and entering it then crashes.
        if (ri.GetLevelForObject(volume) is not { IsValid: true, bIsVisible: true })
            return "its level isn't loaded";

        // Entering reads the door actors each linked level is listed with, and only checks them for null.
        if (LinksGone(ri, volume.OtherLevelsVisibleInfo) || LinksGone(ri, volume.OtherLevelLODsVisibleInfo)
            || AnyGone(ri, volume.OtherLevelsTransitionActors) || AnyGone(ri, volume.OtherLevelsTransitionActors2)
            || AnyGone(ri, volume.OtherLevelLODsTransitionActors) || AnyGone(ri, volume.OtherLevelLODsTransitionActors2))
            return "a linked level is going away";

        return null;
    }

    private static bool LinksGone(RGameRI ri, TArray<RLevelVolume.FVisibleLevelInfo> links)
    {
        for (int i = 0; i < links.Count; i++)
        {
            RLevelVolume.FVisibleLevelInfo link = links[i];
            if (Gone(ri, link.TransitionActor) || Gone(ri, link.TransitionActor2))
                return true;
        }

        return false;
    }

    private static bool AnyGone(RGameRI ri, TArray<RLevelTransition> doors)
    {
        for (int i = 0; i < doors.Count; i++)
        {
            if (Gone(ri, doors[i]))
                return true;
        }

        return false;
    }

    // In a streamed level that's loaded, on show and staying. An unloaded level's actors live on until the
    // next collection, so a valid actor isn't enough to go on.
    public static bool Standing(RGameRI ri, Actor a) =>
        a is { IsValid: true, bDeleteMe: false, bPendingDelete: false }
        && ri.GetLevelForObject(a) is { IsValid: true, bIsVisible: true } ls && !Leaving(ls);

    private static bool Gone(RGameRI ri, Actor? a)
    {
        if (a == null)
            return false;
        if (!a.IsValid || a.bDeleteMe || a.bPendingDelete)
            return true;

        // A level that's hidden and staying hidden is fine, LOD levels sit like that. One on its way out isn't.
        if (ri.GetLevelForObject(a) is { IsValid: true } ls)
            return Leaving(ls);

        // No streaming level owns it. In the always loaded level that's normal. Anywhere else its level has
        // unloaded and the actor is only waiting to be collected.
        return !Permanent(a);
    }

    private static bool Leaving(LevelStreaming ls) =>
        ls.bHasUnloadRequestPending || ls.bIsRequestingUnloadAndRemoval || !ls.bShouldBeLoaded
        || ls.bIsVisible != ls.bShouldBeVisible || ls.LoadedLevel is not { IsValid: true };

    private static bool Permanent(Actor a) =>
        World.WorldInfo is { } wi && wi.Outer is { IsValid: true } main && a.Outer is { IsValid: true } home && home.Ptr == main.Ptr;

    // The first real floor under a point. Low detail stand-ins, volumes, pawns and walls don't count.
    public static bool Real(Actor mover, Vector3 above, float reach, out Vector3 floor)
    {
        floor = default;
        float bottom = above.Z - reach;
        float z = above.Z;
        for (int i = 0; i < FloorTraces && z > bottom; i++)
        {
            Actor? what = mover.Trace(out Vector3 hit, out Vector3 normal, new Vector3(above.X, above.Y, bottom), new Vector3(above.X, above.Y, z),
                Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0);
            if (what == null || hit == Vector3.Zero)
                return false;

            z = hit.Z >= z - 1f ? z - 200f : hit.Z - FloorGap;
            if (what is Volume || what is Pawn || normal.Z < WalkableNormal || IsStandIn(what))
                continue;

            floor = hit;
            return true;
        }

        return false;
    }

    private static bool IsStandIn(Actor what)
    {
        try
        {
            if (World.Ri is { } ri && ri.GetLevelForObject(what) is { IsValid: true } ls)
                return ls.PackageName.ToString().Contains("_LOD", StringComparison.OrdinalIgnoreCase);

            string path = what.GetPathName(null);
            int dot = path.IndexOf('.');
            return (dot > 0 ? path[..dot] : path).Contains("_LOD", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
