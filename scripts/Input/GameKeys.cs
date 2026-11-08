namespace Trinity.Batwing;

internal static class GameKeys
{
    public static readonly Keys[] DeckKeys =
    {
        Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Enter, Keys.Back, Keys.Tab, Keys.PageUp, Keys.PageDown, Keys.Delete, Keys.Home, Keys.End,
    };

    public static readonly Keys[] FlightKeys =
    {
        Keys.W, Keys.A, Keys.S, Keys.D, Keys.F, Keys.E, Keys.C, Keys.H, Keys.Q, Keys.Space, Keys.ControlKey, Keys.ShiftKey,
        Keys.LButton, Keys.RButton, Keys.MButton,
    };

    public static readonly string[] FlightNames = { "MouseScrollUp", "MouseScrollDown" };

    private static readonly Dictionary<string, (Keys[] Keys, string[] Names)> _held = new(StringComparer.Ordinal);
    private static readonly HashSet<Keys> _keys = new();
    private static readonly HashSet<string> _names = new(StringComparer.Ordinal);

    public static bool Holds(string owner) => _held.ContainsKey(owner);

    public static void Hold(string owner, Keys[] keys, string[]? names = null)
    {
        _held[owner] = (keys, names ?? Array.Empty<string>());
        Apply();
    }

    public static void Release(string owner)
    {
        if (_held.Remove(owner))
            Apply();
    }

    public static void ReleaseAll()
    {
        _held.Clear();
        Apply();
    }

    public static void Reassert()
    {
        foreach (Keys key in _keys)
        {
            if (!Keyboard.IsSwallowed(key))
                Keyboard.Swallow(key);
        }

        foreach (string name in _names)
        {
            if (!Keyboard.IsSwallowed(name))
                Keyboard.Swallow(name);
        }
    }

    private static void Apply()
    {
        var keys = new HashSet<Keys>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach ((Keys[] k, string[] n) in _held.Values)
        {
            keys.UnionWith(k);
            names.UnionWith(n);
        }

        foreach (Keys key in _keys)
        {
            if (!keys.Contains(key))
                Keyboard.Unswallow(key);
        }

        foreach (string name in _names)
        {
            if (!names.Contains(name))
                Keyboard.Unswallow(name);
        }

        foreach (Keys key in keys)
            Keyboard.Swallow(key);
        foreach (string name in names)
            Keyboard.Swallow(name);

        _keys.Clear();
        _keys.UnionWith(keys);
        _names.Clear();
        _names.UnionWith(names);
    }
}
