using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal static class World
{
    private const double RefindSeconds = 0.25;

    private const double SettleSeconds = 2.0;

    private static RPlayerController? _pc;
    private static RGameInfo? _info;
    private static Every _refind;
    private static nint _checkedPawn;
    private static bool _frontend;
    private static double _inGameSince = -1.0;
    private static int _paused;

    public static RPlayerController? Pc => _pc is { IsValid: true } ? _pc : null;

    public static RGameInfo? Info => _info is { IsValid: true } ? _info : null;

    public static Pawn? Pawn
    {
        get
        {
            Pawn? pawn = Pc?.Pawn;
            return pawn is { IsValid: true } ? pawn : null;
        }
    }

    public static RPawnPlayer? Player
    {
        get
        {
            RPlayerController? pc = Pc;
            if (pc == null)
                return null;
            if (pc.Pawn is RPawnPlayer owned && owned.IsValid)
                return owned;

            RPawnPlayer combat = pc.CombatPawn;
            return combat is { IsValid: true } ? combat : null;
        }
    }

    public static bool InGame { get; private set; }

    public static bool InFrontend => _frontend;

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
        if (Pc == null || Info == null || _refind.Elapsed(RefindSeconds))
            Refind();

        Pawn? pawn = Pawn;
        if (pawn == null)
        {
            InGame = false;
            _inGameSince = -1.0;
            return;
        }

        if (pawn.Ptr != _checkedPawn)
        {
            _checkedPawn = pawn.Ptr;
            _frontend = pawn.GetPathName(null).StartsWith("frontend.", StringComparison.OrdinalIgnoreCase);
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
        _info = null;
        _checkedPawn = 0;
        _paused = 0;
        _inGameSince = -1.0;
        _refind.Force();
        InGame = false;
    }

    private static void Refind()
    {
        _pc = Game.TryGetPlayerController(out RPlayerController? pc) ? pc : null;
        _info = _pc != null && Game.TryGetGameInfo(out RGameInfo? info) ? info : null;
    }

    public static RGameRI? Ri => Game.TryGetGameRI(out RGameRI? ri) ? ri : null;

    public static WorldInfo? WorldInfo => Game.TryGetWorldInfo(out WorldInfo? wi) ? wi : null;

    public static IEnumerable<T> All<T>() where T : GameObject
    {
        foreach (T obj in Game.FindObjects<T>())
        {
            if (obj is { IsValid: true } && !obj.IsClassDefaultObject)
                yield return obj;
        }
    }
}
