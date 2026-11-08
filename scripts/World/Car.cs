using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal static class Car
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

    public static bool Hostile(RVehicleNPC v) =>
        Wreckable(v) && !v.CivilianVehicle && v.CurrentBehaviour is { IsValid: true };

    // Can be shot up at all, whoever owns it. A thug driving an ordinary car fails Hostile.
    public static bool Wreckable(RVehicleNPC v) =>
        v is { IsValid: true, bDeleteMe: false, bHidden: false, DroneHacked: false } && v.Health > 0 &&
        v is not RVehicleStaticTank && v is not RVehicleHeavyTank;

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
    }

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
