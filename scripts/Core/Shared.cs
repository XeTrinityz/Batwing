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
