using System.Globalization;

namespace Trinity.Batwing;

internal static class Log
{
    private const double ThrottleSeconds = 5.0;

    private static readonly Dictionary<string, double> _next = new(StringComparer.Ordinal);
    private static string _path = "";

    public static bool Diagnostics;

    public static void Open(string dir)
    {
        try
        {
            _path = Path.Combine(dir, "Batwing.log");
            var old = new FileInfo(_path);
            if (old.Exists && old.Length > 200)
                File.Copy(_path, Path.Combine(dir, "Batwing.prev.log"), true);
            File.WriteAllText(_path, "Batwing " + App.Version + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine);
        }
        catch
        {
            _path = "";
        }
    }

    public static void Info(string text)
    {
        Debug.Log("[Batwing] " + text);
        if (_path.Length == 0)
            return;

        try
        {
            File.AppendAllText(_path, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " | " + text + Environment.NewLine);
        }
        catch
        {
        }
    }

    public static void Detail(string text)
    {
        if (Diagnostics)
            Info(text);
    }

    public static string Why(Exception ex)
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(ex, true);
            foreach (System.Diagnostics.StackFrame frame in trace.GetFrames())
            {
                System.Reflection.MethodBase? m = frame.GetMethod();
                if (m?.DeclaringType?.Namespace?.StartsWith("Trinity", StringComparison.Ordinal) != true)
                    continue;

                int line = frame.GetFileLineNumber();
                return ex.Message + " (" + ex.GetType().Name + " in " + m.DeclaringType!.Name + "." + m.Name + (line > 0 ? " line " + line : "") + ")";
            }
        }
        catch (Exception)
        {
        }

        return ex.Message + " (" + ex.GetType().Name + ")";
    }

    public static bool Due(string key)
    {
        double now = Clock.Now;
        if (_next.TryGetValue(key, out double due) && now < due)
            return false;

        _next[key] = now + ThrottleSeconds;
        return true;
    }

    public static void Throttled(string key, string text)
    {
        if (Due(key))
            Info(text);
    }

    public static void Error(string key, string what, Exception ex)
    {
        if (Due(key))
            Info(what + " failed: " + Why(ex));
    }
}
