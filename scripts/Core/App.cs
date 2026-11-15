using BmSDK.Engine;

namespace Trinity.Batwing;

// The mod's life: starting, the game tick, the draw, and stopping. Every call from the SDK comes through here.
internal static class App
{
    public const string Version = "2.0.2";
    private const float MaxStep = 0.25f;

    private static FrameTimer _tick, _frame;
    private static bool _started, _inWorld;

    public static string Dir { get; private set; } = "";

    // True while the tick runs from inside a draw because the SDK's tick stopped. The game may be partway
    // through streaming then, so nothing that streams runs.
    public static bool TickFromDraw => Fallback.Running;

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
        Roots.Bind();

        // Recovery looks for the player, and after a reload nothing has looked the world up yet.
        Safe.Run("world", World.Refresh);
        Safe.Run("recover", Wing.Started);
        Log.Info("Loaded. Deck key " + Bind.KeyName(Controls.Key(Act.Deck)) + ", call key " + Bind.KeyName(Controls.Key(Act.Call)) + ", " + ObjectTable.Describe() + ".");
    }

    public static void Stop()
    {
        if (!_started)
            return;
        _started = false;

        // Each step runs on its own, so one that fails can't leave the game's keys or camera held.
        Safe.Run("deck close", Deck.Close);
        Safe.Run("gauntlet", Gauntlet.Release);
        Safe.Run("wheel", Wheel.Shutdown);
        Safe.Run("shapes", Shapes.Release);
        Safe.Run("settings save", Settings.Flush);
        Safe.Run("batwing", Wing.Shutdown);
        Safe.Run("camera", CameraHook.Shutdown);
        Safe.Run("game hud", GameHud.Shutdown);
        Safe.Run("keys", GameKeys.ReleaseAll);

        // The SDK only unloads for good when mod.toml is gone. Nothing would come back for the kept objects then.
        if (!File.Exists(Path.Combine(Dir, "mod.toml")))
            Safe.Run("roots", Roots.ReleaseAll);
        World.Forget();
    }

    public static void EnterWorld()
    {
        Deck.Close();
        Safe.Run("gauntlet", Gauntlet.Release);

        // Stopping undoes things on the player controller, so it comes before the world is forgotten.
        Safe.Run("world change", Wing.WorldChanged);
        World.Forget();
        Districts.Forget();
    }

    public static void SdkTick()
    {
        Fallback.NoteSdkTick();
        Tick();
    }

    public static void Key(Keys key)
    {
        if (_started)
            Hotkeys.Pressed(key);
    }

    public static void Tick()
    {
        if (!_started)
            return;

        float dt = _tick.Step(MaxStep);
        World.Refresh();
        Safe.Run("roots", Roots.Tick);

        if (World.InGame)
        {
            _inWorld = true;
            Safe.Run("batwing tick", () => Wing.Tick(dt));
        }
        else if (World.InFrontend && _inWorld)
        {
            // The SDK only reports the main menu once, so coming back to it is caught here.
            _inWorld = false;
            Deck.Close();
            Safe.Run("gauntlet", Gauntlet.Release);
            Safe.Run("leave game", Wing.LeftGame);
        }

        Deck.Tick(dt);
        Safe.Run("gauntlet", () => Gauntlet.Tick(dt));
    }

    public static void Frame(Canvas canvas)
    {
        if (!_started)
            return;

        World.Refresh();

        // Input, saving and the settings watch run from the draw, so they keep going while the game is paused.
        float dt = _frame.Step(MaxStep);
        Safe.Run("settings", () => Settings.Tick(dt));
        Safe.Run("controls", Controls.Frame);
        Safe.Run("hotkeys", Hotkeys.Frame);
        Safe.Run("game keys", GameKeys.Frame);
        Safe.Run("wheel", () => Wheel.Frame(dt));
        Safe.Run("deck pad", () => Deck.PadFrame(dt));
        Safe.Run("batwing frame", Wing.Frame);
        Safe.Run("fallback", Fallback.Frame);
        Safe.Run("overlay", () => Overlay.Draw(canvas));
    }
}

// The SDK's tick can stop while the game keeps drawing. Then the tick runs from the draw instead.
internal static class Fallback
{
    private const double QuietSeconds = 0.5;

    // One long frame, like a streaming hitch, also leaves a gap between ticks. Only a run of draws with no
    // tick at all means the tick really stopped.
    private const int QuietDraws = 30;

    private static double _lastSdkTick = Clock.Now;
    private static int _drawsSinceTick;
    private static bool _driving;
    private static int _stops;
    private static double _since, _sinceGame;

    public static bool Running { get; private set; }

    private static bool Active => _drawsSinceTick >= QuietDraws && Clock.Now - _lastSdkTick > QuietSeconds;

    public static void NoteSdkTick()
    {
        _lastSdkTick = Clock.Now;
        _drawsSinceTick = 0;
        if (!_driving)
            return;

        _driving = false;
        Say("The tick is back after " + Fmt.Tenths((float)(Clock.Now - _since)) + " s. The game world ran for "
            + Fmt.Tenths((float)(Clock.Game - _sinceGame)) + " s of that.");
    }

    public static void Frame()
    {
        // A pause stops the tick as well. That's not the tick going missing, so the count starts again after it.
        if (World.Paused)
        {
            _lastSdkTick = Clock.Now;
            _drawsSinceTick = 0;
            return;
        }

        if (_drawsSinceTick < QuietDraws)
            _drawsSinceTick++;
        if (!Active)
            return;

        if (!_driving)
        {
            _driving = true;
            _stops++;
            _since = Clock.Now;
            _sinceGame = Clock.Game;
            Say("The tick stopped (" + World.Describe() + "), running from the draw instead.");
        }

        Running = true;
        try
        {
            App.Tick();
        }
        finally
        {
            Running = false;
        }
    }

    // The first stop and the first return always go in the log. After that only with diagnostics on.
    private static void Say(string text)
    {
        if (_stops == 1)
            Log.Info(text);
        else
            Log.Detail(text);
    }
}
