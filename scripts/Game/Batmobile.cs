using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// The player's Batmobile and its weapons. The Batwing fires the Batmobile's own guns, so it shares its upgrades.
internal static class Batmobile
{
    private const double RefreshSeconds = 2.0;

    private static RVehicleBatmobileBase? _car;
    private static readonly List<RVehicleWeapon> _weapons = new();
    private static Every _refresh;

    public static RVehicleBatmobileBase? Current()
    {
        bool due = _refresh.Elapsed(RefreshSeconds);
        bool usable = _car == null || (_car.IsValid && !_car.bDeleteMe);
        if (!due && usable)
            return _car;

        RVehicleBatmobileBase? found = Locate();
        if (found == null || _car is not { IsValid: true } || found.Ptr != _car.Ptr)
        {
            _car = found;
            FindWeapons(found);
        }
        else if (WeaponsStale())
        {
            FindWeapons(found);
        }

        return _car;
    }

    public static T? Weapon<T>() where T : RVehicleWeapon
    {
        Current();
        foreach (RVehicleWeapon w in _weapons)
        {
            if (w is T { IsValid: true } found)
                return found;
        }

        return null;
    }

    public static AkPlayerVehicleSoundInfo? Sounds() =>
        Current() is { IsValid: true } car && car.PlayerVehicleInfo is { IsValid: true } info ? info : null;

    public static int BarrageSize(RBatmobileMissileLauncher launcher)
    {
        TArray<int> levels = launcher.MaxTargetsPerLevel;
        int top = 0;
        for (int i = 0; i < levels.Count; i++)
            top = Math.Max(top, levels[i]);
        return top;
    }

    public static void Forget()
    {
        _car = null;
        _weapons.Clear();
        _refresh.Force();
        _destination = null;
        _destinationCheck.Force();
    }

    private static (Vector3 At, string What)? _destination;
    private static Every _destinationCheck;

    // The map waypoint if one is set, otherwise the mission marker the game rates highest. The HUD asks every
    // frame, so the answer is kept for a moment.
    public static (Vector3 At, string What)? Destination()
    {
        if (_destinationCheck.Elapsed(0.25))
            _destination = FindDestination();
        return _destination;
    }

    private static (Vector3 At, string What)? FindDestination()
    {
        RPersistentData? pd = null;
        Safe.Run("waypoint", () => pd = Game.GetPersistentData());
        if (pd is { IsValid: true } && Safe.Try("waypoint on", pd.GetCustomWaypointEnabled))
            return (pd.GetCustomWaypoint(true), "WAYPOINT");

        if (World.Info is { } info && Objective(info) is { } objective)
            return (objective, "OBJECTIVE");

        return null;
    }

    private static Vector3? Objective(RGameInfo info)
    {
        var markers = info.ObjectiveMarkers;
        int picked = info.HighestPriorityNonCustomObjectiveMarker;
        if (picked >= 0 && picked < markers.Count && Usable(markers[picked]))
            return markers[picked].Location;

        RObjectiveMarker? best = null;
        foreach (RObjectiveMarker m in markers)
        {
            if (Usable(m) && (best == null || m.Priority > best.Priority))
                best = m;
        }

        return best?.Location;
    }

    private static bool Usable(RObjectiveMarker? m) => m is { IsValid: true, bDeleteMe: false, IsMarkerActive: true, IsCustomWaypoint: false };

    private static RVehicleBatmobileBase? Locate()
    {
        RVehicleBatmobileBase? known = World.Info?.GetBatmobile();
        if (known is { IsValid: true, IsClassDefaultObject: false, bDeleteMe: false })
            return known;

        foreach (RVehicleBatmobileBase b in World.All<RVehicleBatmobileBase>())
        {
            if (!b.bDeleteMe && b.GetPathName(null).Contains("PersistentLevel", StringComparison.Ordinal))
                return b;
        }

        return null;
    }

    private static bool WeaponsStale()
    {
        if (_weapons.Count == 0)
            return true;
        foreach (RVehicleWeapon w in _weapons)
        {
            if (!w.IsValid)
                return true;
        }

        return false;
    }

    private static void FindWeapons(RVehicleBatmobileBase? owner)
    {
        _weapons.Clear();
        if (owner == null)
            return;

        foreach (RVehicleWeapon w in World.All<RVehicleWeapon>())
        {
            if (w.Owner != null && w.Owner.Ptr == owner.Ptr)
                _weapons.Add(w);
        }
    }
}
