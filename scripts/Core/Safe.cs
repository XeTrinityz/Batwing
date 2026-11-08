namespace Trinity.Batwing;

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
            Log.Error(what, what, ex);
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
            Log.Error(what, what, ex);
            return fallback;
        }
    }
}
