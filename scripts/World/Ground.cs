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

    public static void Enter(string? level)
    {
        if (World.Ri is not { } ri || level == null || !level.StartsWith("City", StringComparison.OrdinalIgnoreCase)
            || Districts.Volume(level) is not { } volume)
            return;

        RLevelVolume? old = ri.CurrentLevelVolume is { IsValid: true } v ? v : null;
        if (old?.Ptr == volume.Ptr)
            return;

        ri.RemovePredictedLevel();
        volume.EnteredLevelVolume();
        if (old != null)
            old.LevelVolumeDeactivated();
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
