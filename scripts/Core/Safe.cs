namespace Trinity.Batwing;

// Nothing the mod does may throw into the game. Every entry point and every step that touches the game goes through here.
internal static class Safe
{
    public static void Run(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error(what, ex);
        }
    }

    public static bool Try(string what, Func<bool> check, bool fallback = false)
    {
        try
        {
            return check();
        }
        catch (Exception ex)
        {
            Log.Error(what, ex);
            return fallback;
        }
    }

    public static T Get<T>(string what, Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Log.Error(what, ex);
            return fallback;
        }
    }
}
