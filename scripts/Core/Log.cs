using System.Globalization;
using System.Reflection;

namespace Trinity.Batwing;

internal static class Log
{
    private const double ThrottleSeconds = 5.0;
    private const long KeepPrevious = 200;

    private static readonly Dictionary<string, (double At, int Missed)> _next = new(StringComparer.Ordinal);
    private static string _path = "";

    public static bool Diagnostics;

    public static void Open(string dir)
    {
        string host = Host();
        try
        {
            _path = Path.Combine(dir, "Batwing.log");
            var old = new FileInfo(_path);
            if (old.Exists && old.Length > KeepPrevious)
                File.Copy(_path, Path.Combine(dir, "Batwing.prev.log"), true);
            File.WriteAllText(_path, "Batwing " + App.Version + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ", " + host + Environment.NewLine);
        }
        catch
        {
            _path = "";
        }
    }

    // The game build and SDK the mod is running on, for bug reports.
    private static string Host()
    {
        try
        {
            string sdk = typeof(Script).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            int hash = sdk.IndexOf('+');
            if (hash >= 0 && sdk.Length > hash + 8)
                sdk = sdk[..(hash + 8)];
            return GameDefine.Current.Name + " build, BmSDK " + sdk;
        }
        catch (Exception)
        {
            return "build unknown";
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

    public static void Throttled(string key, string text)
    {
        if (Due(key, out _))
            Info(text);
    }

    public static void Error(string what, Exception ex)
    {
        if (Due(what, out int missed))
            Info(what + " failed: " + Why(ex) + (missed > 0 ? ", and " + missed + " more times since the last line" : ""));
    }

    // The message and where in the mod it came from, which is what a bug report needs.
    public static string Why(Exception ex)
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(ex, true);
            foreach (System.Diagnostics.StackFrame frame in trace.GetFrames())
            {
                MethodBase? m = frame.GetMethod();
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

    private static bool Due(string key, out int missed)
    {
        double now = Clock.Now;
        _next.TryGetValue(key, out (double At, int Missed) last);
        if (now < last.At)
        {
            _next[key] = (last.At, last.Missed + 1);
            missed = 0;
            return false;
        }

        missed = last.Missed;
        _next[key] = (now + ThrottleSeconds, 0);
        return true;
    }
}
