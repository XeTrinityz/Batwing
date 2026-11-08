namespace Trinity.Batwing;

internal static class Shared
{
    private const string MenuOwnerKey = "CeZeY.ModMenuOwner";

    public const string OwnerName = "Batwing";

    private static IDictionary<string, object?> _state = new Dictionary<string, object?>();

    public static void Bind(Mod mod)
    {
        if (ReferenceEquals(_state, mod.State))
            return;

        foreach ((string key, object? value) in _state)
            mod.State.TryAdd(key, value);
        _state = mod.State;
    }

    public static T GetValue<T>(string key, T fallback) where T : struct =>
        _state.TryGetValue(key, out object? value) && value is T held ? held : fallback;

    public static void Set(string key, object? value) => _state[key] = value;

    public static string? MenuOwner
    {
        get => AppDomain.CurrentDomain.GetData(MenuOwnerKey) as string;
        set => AppDomain.CurrentDomain.SetData(MenuOwnerKey, value);
    }

    public static bool ScreenFree => MenuOwner is null or OwnerName;

    public static bool AnyMenuOpen => MenuOwner is not null;
}

// Toasts from every mod share one column at the top of the screen. Each mod posts where its toast
// sits, and a newer toast goes under the older ones instead of on top of them.
internal static class ToastLane
{
    private const string Key = "CeZeY.Toasts";
    private const long Linger = 250;

    private static Dictionary<string, double[]> Lane
    {
        get
        {
            if (AppDomain.CurrentDomain.GetData(Key) is Dictionary<string, double[]> lane)
                return lane;

            var made = new Dictionary<string, double[]>();
            AppDomain.CurrentDomain.SetData(Key, made);
            return made;
        }
    }

    public static long Now => Environment.TickCount64;

    public static float Top(string owner, long started, float own, float gap)
    {
        long now = Now;
        float top = own;
        foreach ((string who, double[] t) in Lane)
        {
            if (who == owner || t.Length < 3 || t[1] < now)
                continue;
            if (t[0] < started || (t[0] == started && string.CompareOrdinal(who, owner) < 0))
                top = MathF.Max(top, (float)t[2] + gap);
        }

        return top;
    }

    // Posted every frame it's drawn. The entry lapses on its own if a mod stops drawing or unloads.
    public static void Post(string owner, long started, float bottom) =>
        Lane[owner] = new double[] { started, Now + Linger, bottom };

    public static void Clear(string owner) => Lane.Remove(owner);
}
