using BmSDK;

namespace Trinity.Batwing;

// Keeps objects the mod loaded or made away from the garbage collector, by owner. An object is let go
// a few seconds after its last owner gives it up, so a quick give back and take again costs nothing.
internal static class Roots
{
    private const string StateKey = "batwing.roots";
    private const double LetGrace = 5.0;

    // Lives in the mod's state so a script reload still knows what it holds.
    private static Dictionary<GameObject, HashSet<string>> _kept = new();
    private static readonly Dictionary<GameObject, double> _letAt = new();

    public static void Bind()
    {
        _kept = Shared.Kept(StateKey, _kept);

        // Anything with no owner was waiting to be let go when the scripts reloaded.
        foreach ((GameObject obj, HashSet<string> owners) in _kept)
        {
            if (owners.Count == 0)
                _letAt[obj] = Clock.Now + LetGrace;
        }
    }

    public static T Keep<T>(T obj, string owner) where T : GameObject
    {
        if (!obj.IsValid)
            return obj;
        if (!_kept.TryGetValue(obj, out HashSet<string>? owners))
        {
            _kept[obj] = owners = new HashSet<string>(StringComparer.Ordinal);
            obj.AddToRoot();
        }

        owners.Add(owner);
        _letAt.Remove(obj);
        return obj;
    }

    public static T? KeepFound<T>(T? obj, string owner) where T : GameObject => obj != null ? Keep(obj, owner) : null;

    public static void Let(string owner)
    {
        int freed = 0;
        foreach ((GameObject obj, HashSet<string> owners) in _kept)
        {
            if (!owners.Remove(owner) || owners.Count > 0)
                continue;
            _letAt[obj] = Clock.Now + LetGrace;
            freed++;
        }

        if (freed > 0)
            Log.Info("Released " + freed + " objects (" + ObjectTable.Describe() + ").");
    }

    public static void Tick()
    {
        if (_letAt.Count == 0)
            return;

        double now = Clock.Now;
        List<GameObject>? done = null;
        foreach ((GameObject obj, double at) in _letAt)
        {
            if (now >= at)
                (done ??= new()).Add(obj);
        }

        if (done == null)
            return;
        foreach (GameObject obj in done)
        {
            _letAt.Remove(obj);
            _kept.Remove(obj);
            if (obj.IsValid)
                Safe.Run("root let go", obj.RemoveFromRoot);
        }
    }

    public static void ReleaseAll()
    {
        foreach (GameObject obj in _kept.Keys)
        {
            if (obj.IsValid)
                Safe.Run("root let go", obj.RemoveFromRoot);
        }

        _kept.Clear();
        _letAt.Clear();
    }
}
