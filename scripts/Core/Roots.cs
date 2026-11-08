using BmSDK;

namespace Trinity.Batwing;

internal static class Roots
{
    private sealed class Held
    {
        public GameObject Obj = null!;
        public readonly HashSet<string> Owners = new(StringComparer.Ordinal);
        public double LetAt;
    }

    private const double LetGrace = 5.0;

    private static readonly Dictionary<nint, Held> _kept = new();

    public static T Keep<T>(T obj, string owner) where T : GameObject
    {
        if (!obj.IsValid)
            return obj;
        if (!_kept.TryGetValue(obj.Ptr, out Held? held))
        {
            _kept[obj.Ptr] = held = new Held { Obj = obj };
            obj.AddToRoot();
        }

        held.Owners.Add(owner);
        held.LetAt = 0;
        return obj;
    }

    public static void Let(string owner)
    {
        int freed = 0;
        foreach (Held held in _kept.Values)
        {
            if (!held.Owners.Remove(owner) || held.Owners.Count > 0)
                continue;
            held.LetAt = Clock.Now + LetGrace;
            freed++;
        }

        if (freed > 0)
            Log.Info("Released " + freed + " objects (" + ObjectTable.Describe() + ").");
    }

    public static void Tick()
    {
        if (_kept.Count == 0)
            return;

        double now = Clock.Now;
        List<nint>? done = null;
        foreach ((nint ptr, Held held) in _kept)
        {
            if (held.Owners.Count == 0 && held.LetAt > 0 && now >= held.LetAt)
                (done ??= new()).Add(ptr);
        }

        if (done == null)
            return;
        foreach (nint ptr in done)
        {
            Held held = _kept[ptr];
            _kept.Remove(ptr);
            if (held.Obj.IsValid)
                Safe.Run("root let go", held.Obj.RemoveFromRoot);
        }
    }
}
