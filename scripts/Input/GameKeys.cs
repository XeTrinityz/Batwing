namespace Trinity.Batwing;

// Keeps keys and pad inputs from the game while the mod uses them. Each part of the mod holds its own set
// by name, and the game gets everything back the moment nothing holds it.
internal static class GameKeys
{
    // Every button and axis of the pad, by the name the game's binds use. Start is left out so pause still works.
    public static readonly string[] PadNames =
    {
        "XboxTypeS_A", "XboxTypeS_B", "XboxTypeS_X", "XboxTypeS_Y",
        "XboxTypeS_LeftShoulder", "XboxTypeS_RightShoulder", "XboxTypeS_LeftTrigger", "XboxTypeS_RightTrigger",
        "XboxTypeS_LeftThumbstick", "XboxTypeS_RightThumbstick", "XboxTypeS_Back",
        "XboxTypeS_DPad_Up", "XboxTypeS_DPad_Down", "XboxTypeS_DPad_Left", "XboxTypeS_DPad_Right",
        "XboxTypeS_LeftX", "XboxTypeS_LeftY", "XboxTypeS_RightX", "XboxTypeS_RightY",
        "XboxTypeS_LeftTriggerAxis", "XboxTypeS_RightTriggerAxis",
    };

    public static readonly string[] Wheel = { "MouseScrollUp", "MouseScrollDown" };

    private static readonly Dictionary<string, (Keys[] Keys, string[] Names)> _held = new(StringComparer.Ordinal);
    private static readonly HashSet<Keys> _keys = new();
    private static readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private static readonly HashSet<Keys> _stale = new();
    private static bool _idle;
    private static Every _check;

    public static bool Holds(string owner) => _held.ContainsKey(owner);

    public static void Hold(string owner, Keys[] keys, string[]? names = null)
    {
        _held[owner] = (keys, names ?? Array.Empty<string>());
        Apply();
    }

    public static void Hold(string owner, bool on, Keys[] keys, string[]? names = null)
    {
        if (on)
        {
            if (!_held.TryGetValue(owner, out var had) || !had.Keys.SequenceEqual(keys) || !had.Names.SequenceEqual(names ?? Array.Empty<string>()))
                Hold(owner, keys, names);
        }
        else
        {
            Release(owner);
        }
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

    // A key that was pressed in the pause menu belongs to the menu until it's let go.
    public static bool Down(Keys key) => key != Keys.None && !_idle && Keyboard.IsDown(key) && !_stale.Contains(key);

    // Runs every draw, so it keeps going behind the pause menu.
    public static void Frame()
    {
        if (_stale.Count > 0)
            _stale.RemoveWhere(static key => !Keyboard.IsDown(key));

        // The pause menu and the map need the mouse, the keys and the pad. Nothing is kept from the game there.
        bool idle = World.Paused || !World.InGame;
        if (idle != _idle)
        {
            _idle = idle;
            Apply();
            if (!idle)
            {
                foreach (Keys key in _keys)
                {
                    if (Keyboard.IsDown(key))
                        _stale.Add(key);
                }
            }
        }
        else if (!idle && _check.Elapsed(0.5))
        {
            Reassert();
        }
    }

    // Another mod letting go of all keys would let go of ours too.
    private static void Reassert()
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
        if (!_idle)
        {
            foreach ((Keys[] k, string[] n) in _held.Values)
            {
                keys.UnionWith(k.Where(x => x != Keys.None));
                names.UnionWith(n);
            }
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
