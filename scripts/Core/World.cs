using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// The player and the world, looked up a few times a second and kept between.
internal static class World
{
    private const double RefindSeconds = 0.25;
    private const double SettleSeconds = 2.0;

    private static RPlayerController? _pc;
    private static WorldInfo? _wi;
    private static Every _refind;
    private static nint _checkedPawn;
    private static bool _frontend;
    private static double _inGameSince = -1.0;
    private static int _paused;

    public static RPlayerController? Pc => _pc is { IsValid: true } ? _pc : null;

    public static WorldInfo? WorldInfo => _wi is { IsValid: true } ? _wi : null;

    public static RGameInfo? Info => WorldInfo?.Game is RGameInfo { IsValid: true } info ? info : null;

    public static RGameRI? Ri => WorldInfo?.GRI is RGameRI { IsValid: true } ri ? ri : null;

    public static Pawn? Pawn => Pc?.Pawn is { IsValid: true } pawn ? pawn : null;

    // Batman, whether he's on foot or driving.
    public static RPawnPlayer? Player
    {
        get
        {
            if (Pc is not { } pc)
                return null;
            if (pc.Pawn is RPawnPlayer { IsValid: true } owned)
                return owned;
            return pc.CombatPawn is { IsValid: true } combat ? combat : null;
        }
    }

    public static RVehicleBatmobileBase? Driving => Pawn as RVehicleBatmobileBase;

    public static bool InGame { get; private set; }

    public static bool InFrontend => _frontend;

    // In the world long enough for everything to have settled after loading.
    public static bool Ready => InGame && _inGameSince >= 0.0 && Clock.Now - _inGameSince > SettleSeconds;

    public static bool Paused
    {
        get
        {
            if (_paused == 0)
                _paused = Safe.Try("pause check", () => Pc is { } pc && pc.IsPaused()) ? 2 : 1;
            return _paused == 2;
        }
    }

    public static void Refresh()
    {
        _paused = 0;
        if (Pc == null || WorldInfo == null || _refind.Elapsed(RefindSeconds))
            Refind();
        if (WorldInfo is { } wi)
            Clock.Follow(wi.TimeSeconds);

        if (Pawn is not { } pawn)
        {
            InGame = false;
            _inGameSince = -1.0;
            return;
        }

        if (pawn.Ptr != _checkedPawn)
        {
            _frontend = pawn.GetPathName(null).StartsWith("frontend.", StringComparison.OrdinalIgnoreCase);
            _checkedPawn = pawn.Ptr;
        }

        InGame = !_frontend;
        if (!InGame)
            _inGameSince = -1.0;
        else if (_inGameSince < 0.0)
            _inGameSince = Clock.Now;
    }

    public static void Forget()
    {
        _pc = null;
        _wi = null;
        _checkedPawn = 0;
        _paused = 0;
        _inGameSince = -1.0;
        _refind.Force();
        InGame = false;
        Clock.Unfollow();
    }

    private static void Refind()
    {
        _pc = Game.TryGetPlayerController(out RPlayerController? pc) ? pc : null;
        WorldInfo? wi = Game.TryGetWorldInfo(out WorldInfo? found) ? found : null;
        if (!ReferenceEquals(wi, _wi))
            Clock.Unfollow();
        _wi = wi;
    }

    public static bool InCinematic(PlayerController pc) =>
        pc.bCinematicMode || pc is RPlayerControllerBase { ActiveCinematicMode: { IsValid: true } };

    // What the game is doing right now, for log lines.
    public static string Describe()
    {
        try
        {
            if (Pc is not { } pc)
                return "no player";

            WorldInfo? wi = WorldInfo;
            return "controller " + pc.GetStateName()
                + ", game " + (Info is { } info ? info.GetStateName().ToString() : "none")
                + ", pawn " + (Pawn?.Class.Name.ToString() ?? "none")
                + (wi is { bPlayersOnly: true } ? ", players only" : "")
                + (wi?.Pauser is { IsValid: true } ? ", pauser set" : "")
                + (wi is { DebugMenuWantsPause: true } ? ", debug menu pause" : "")
                + (Game.TryGetEngine(out GameEngine? engine) && engine.bShouldCommitPendingMapChange ? ", map change pending" : "")
                + (pc.bCinematicMode ? ", cinematic" : "")
                + (Native.GameInFront ? "" : ", window in the background");
        }
        catch (Exception ex)
        {
            return "state unknown, " + ex.Message;
        }
    }

    public static IEnumerable<T> All<T>() where T : GameObject
    {
        foreach (T obj in Game.FindObjects<T>())
        {
            if (obj is { IsValid: true } && !obj.IsClassDefaultObject)
                yield return obj;
        }
    }
}
