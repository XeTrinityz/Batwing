using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal static class Ground
{
    private const float WalkableNormal = 0.6f;
    private const float FloorGap = 15f;

    private static readonly FName StandingPose = new("Standing");
    private static readonly FName RelaxedPose = new("Relaxed");

    private readonly record struct Floor(Vector3 At, string? Level);

    public static bool Ray(Actor from, Vector3 start, Vector3 end, out Vector3 hit, out Vector3 normal)
    {
        Actor? hitActor = from.Trace(out hit, out normal, end, start, Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0);
        return hitActor != null && hit != Vector3.Zero;
    }

    public static float Lift(Pawn mover) => mover.GetCollisionHeight() + 40f;

    public static void Settle(Pawn mover)
    {
        if (mover is RPawnPlayer batman)
            Safe.Run("player pose", () => batman.ForceChangePose(StandingPose, RelaxedPose, default, false, default, default, default, 0.001f));
        Safe.Run("player moved", () => World.Pc?.OnPlayerHasBeenMoved());
    }

    public static bool InCity(Vector3 at) => Districts.At(at) != null;

    // False when streaming is busy and the caller should try again shortly.
    public static bool Enter(string? level)
    {
        if (World.Ri is not { } ri || level == null || !level.StartsWith("City", StringComparison.OrdinalIgnoreCase)
            || Districts.Volume(level) is not { } volume)
            return true;

        RLevelVolume? old = ri.CurrentLevelVolume is { IsValid: true } v ? v : null;
        if (old?.Ptr == volume.Ptr)
            return true;

        // Entering a volume while it or a neighbour it links to is being streamed out is a hard crash
        // inside EnteredLevelVolume, which no try/catch can stop.
        if (Busy(ri, volume) is { } why)
        {
            Log.Throttled("district wait", "Districts: holding off entering " + level + ", " + why + ".");
            return false;
        }

        ri.RemovePredictedLevel();
        volume.EnteredLevelVolume();
        if (old != null && Busy(ri, old) == null)
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

        // District volumes always sit in a streamed level. Once that level has unloaded no LevelStreaming owns
        // the volume any more, but the actor lives on until the next collection and entering it then crashes.
        if (ri.GetLevelForObject(volume) is not { IsValid: true, bIsVisible: true })
            return "its level isn't loaded";

        foreach (TArray<RLevelTransition> links in new[]
        {
            volume.OtherLevelsTransitionActors, volume.OtherLevelsTransitionActors2,
            volume.OtherLevelLODsTransitionActors, volume.OtherLevelLODsTransitionActors2,
        })
        {
            for (int i = 0; i < links.Count; i++)
            {
                if (Gone(ri, links[i]))
                    return "a linked level is going away";
            }
        }

        return null;
    }

    private static bool Gone(RGameRI ri, Actor? a)
    {
        if (a == null)
            return false;
        if (!a.IsValid || a.bDeleteMe || a.bPendingDelete)
            return true;

        // A level that's hidden and staying hidden is fine, LOD levels sit like that. One on its way out isn't.
        LevelStreaming? ls = ri.GetLevelForObject(a);
        return ls is { IsValid: true } && (ls.bHasUnloadRequestPending || ls.bIsRequestingUnloadAndRemoval || !ls.bShouldBeLoaded
            || ls.bIsVisible != ls.bShouldBeVisible || ls.LoadedLevel is not { IsValid: true });
    }

    public static bool Real(Pawn mover, Vector3 above, float reach, out Vector3 floor)
    {
        List<Floor> floors = Floors(mover, above.X, above.Y, above.Z, above.Z - reach, 1, 8);
        floor = floors.Count > 0 ? floors[0].At : default;
        return floors.Count > 0;
    }

    private static List<Floor> Floors(Pawn mover, float x, float y, float top, float bottom, int max, int traces)
    {
        var found = new List<Floor>();
        float z = top;
        for (int i = 0; i < traces && found.Count < max && z > bottom; i++)
        {
            Actor? what = mover.Trace(out Vector3 hit, out Vector3 normal, new Vector3(x, y, bottom), new Vector3(x, y, z),
                Component.ECollisionFilter.COF_TraceWorld, Vector3.Zero, out _, 0);
            if (what == null || hit == Vector3.Zero)
                break;

            z = hit.Z >= z - 1f ? z - 200f : hit.Z - FloorGap;
            if (what is Volume || what is Pawn || normal.Z < WalkableNormal)
                continue;

            string? level = LevelOf(what);
            if (IsStandIn(what, level))
                continue;

            found.Add(new Floor(hit, level));
        }

        return found;
    }

    private static string? LevelOf(Actor what)
    {
        try
        {
            if (World.Ri is not { } ri)
                return null;

            LevelStreaming? ls = ri.GetLevelForObject(what);
            return ls is { IsValid: true } ? ls.PackageName.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsStandIn(Actor what, string? level)
    {
        if (level != null)
            return level.Contains("_LOD", StringComparison.OrdinalIgnoreCase);

        try
        {
            string path = what.GetPathName(null);
            int dot = path.IndexOf('.');
            string package = dot > 0 ? path[..dot] : path;
            return package.Contains("_LOD", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
