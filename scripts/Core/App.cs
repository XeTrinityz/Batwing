using BmSDK.Engine;

namespace Trinity.Batwing;

internal static class App
{
    public const string Version = "1.4.2";
    private const float MaxStep = 0.25f;

    private static FrameTimer _tick;
    private static bool _started;
    private static bool _inWorld;

    public static string Dir { get; private set; } = "";

    public static void Start(string dir)
    {
        if (_started)
            return;
        _started = true;

        Dir = dir;
        Log.Open(dir);
        Opt.Build();
        Settings.Load(first: true);
        Opt.ApplyAll();
        Safe.Run("recover", Wing.I.Started);
        Log.Info("Loaded. Deck key " + Bind.KeyName(Opt.DeckKey.Key) + ", call key " + Bind.KeyName(Opt.CallKey.Key) + ", " + ObjectTable.Describe() + ".");
    }

    public static void Stop()
    {
        if (!_started)
            return;
        _started = false;

        Deck.Close();
        Pad.Shutdown();
        Shapes.Release();
        Settings.Flush();
        Safe.Run("shutdown", Wing.I.Shutdown);
        Safe.Run("camera restore", Hooks.Shutdown);
        GameKeys.ReleaseAll();
        World.Forget();
    }

    public static void EnterWorld()
    {
        Deck.Close();
        World.Forget();
        Districts.Forget();
        Safe.Run("world change", Wing.I.WorldChanged);
    }

    public static void EnterMenu()
    {
        Deck.Close();
        World.Forget();
    }

    public static void SdkTick()
    {
        Fallback.NoteSdkTick();
        Tick();
    }

    public static void SdkKey(Keys key)
    {
        if (_started)
            KeyInput.Pressed(key);
    }

    public static void Tick()
    {
        if (!_started)
            return;

        float dt = _tick.Step(MaxStep);
        World.Refresh();
        Settings.Tick(dt);
        Safe.Run("roots", Roots.Tick);

        if (World.InGame)
        {
            _inWorld = true;
            Safe.Run("flight tick", () => Wing.I.Tick(dt));
            Safe.Run("hud hide", Hooks.HudTick);
        }
        else if (World.InFrontend && _inWorld)
        {
            _inWorld = false;
            Safe.Run("leave game", Wing.I.LeftGame);
        }

        Deck.Tick(dt);
    }

    public static void Frame(Canvas canvas)
    {
        if (!_started)
            return;

        World.Refresh();
        Safe.Run("flight frame", Wing.I.Frame);
        Safe.Run("controller", Pad.Frame);
        Safe.Run("fallback", Fallback.Frame);
        Safe.Run("overlay", () => Overlay.Draw(canvas));
    }
}

internal static class Fallback
{
    private const double QuietSeconds = 0.5;

    private static long _lastSdkTick = System.Diagnostics.Stopwatch.GetTimestamp();
    private static bool _logged;

    public static bool Active => System.Diagnostics.Stopwatch.GetElapsedTime(_lastSdkTick).TotalSeconds > QuietSeconds;

    public static void NoteSdkTick() => _lastSdkTick = System.Diagnostics.Stopwatch.GetTimestamp();

    public static void Frame()
    {
        if (!Active || World.Paused)
            return;

        if (!_logged)
        {
            _logged = true;
            Log.Info("OnTick stopped firing, running from OnDraw instead.");
        }

        App.Tick();
    }
}
