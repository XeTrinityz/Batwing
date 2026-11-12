using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// Batman raises his gauntlet the way he does for a call, and the deck is drawn on the game's own
// hologram. In the Batmobile the hologram shows over the dashboard instead, as calls do there. The
// game does this in its radio state, which needs a mission script behind it. Here only the pieces
// are used: the call stance or the cockpit, the call camera and the projector with no video.
internal static class Gauntlet
{
    private enum Where
    {
        Nowhere,
        Arm,
        Cockpit,
    }

    private static readonly FName CallStance = new("RadioHologram");
    private static readonly FName Standing = new("Standing");
    private static readonly FName Outgoing = new("OutgoingCall");
    private static readonly FName CallCamera = new("GauntletConversation");
    private static readonly FName Walking = new("WalkingMode");
    private static readonly FName Driving = new("DrivingMode");

    // Short moves that end with Batman back on his feet, like climbing out of the car.
    private static readonly FName[] Passing = { new("SpecialMove"), new("PairedAnimation"), new("ControlledMoveTo") };

    // Batman has to be back on his feet this long before the arm goes up again, so a hop or a
    // stumble doesn't throw it up and down.
    private const float SettleSeconds = 0.25f;

    // Leaving the car counts as passing for this long, as it can end in a short fall. Passing
    // holds the deck back for at most the second figure before it opens on the screen.
    private const double LeftCarSeconds = 1.5;
    private const float MostWaitSeconds = 1.5f;

    private static Where _where;
    private static RPawnPlayer? _pawn;
    private static RVehicleBatmobileBase? _car;
    private static RPlayerController? _pc;
    private static FName _cameraBefore;
    private static float _settled;
    private static bool _hadCar;
    private static double _leftCarAt = double.MinValue;
    private static float _waited;
    private static bool _passing;
    private static bool _warned;

    // Batman could raise the gauntlet as of the last tick.
    private static bool _able;

    // The screen couldn't take the deck this time, so it stays on the screen until it closes.
    private static bool _refused;

    private static bool Wanted => Deck.IsOpen && Opt.DeckOnGauntlet && !_refused;

    // The deck belongs on the gauntlet right now. It may still be on its way up.
    public static bool Hosting => _able && Wanted;

    // Batman can't raise it yet but soon will, so the deck shows nowhere for that moment rather
    // than opening on the screen and jumping to the gauntlet.
    public static bool Waiting => _passing && Wanted;

    private static bool Up => _where != Where.Nowhere;

    public static void Tick(float dt)
    {
        if (!Deck.IsOpen)
            _refused = false;

        bool want = Wanted;
        RPlayerController? pc = World.Pc;
        RPawnPlayer? pawn = pc?.CombatPawn is { IsValid: true } combat ? combat : null;
        RVehicleBatmobileBase? car = World.Pawn as RVehicleBatmobileBase;

        // A call the game is showing keeps the screen, and the deck opens flat until it is over.
        // Our own screen still fading out after the deck closed is taken straight back.
        RGauntletProjector? projector = pc?.GauntletProjector;
        Where where = WhereNow(pc, projector, pawn, car);
        bool able = where != Where.Nowhere;

        // Our texture stays on the screen while it fades out, and comes off once it is off or a
        // game call has it.
        if (GauntletScreen.Active && (projector is not { IsValid: true } || !projector.ScreenOn || !Ours(projector)))
            GauntletScreen.Give();

        GauntletScreen.Refresh();

        bool moved = (where == Where.Arm && !ReferenceEquals(pawn, _pawn)) || (where == Where.Cockpit && !ReferenceEquals(car, _car));
        if (Up && (!want || where != _where || moved))
            Lower();

        _settled = able ? _settled + dt : 0f;
        if (want && able && !Up && _settled >= SettleSeconds)
            Raise(pc!, projector!, where, pawn, car);

        if (_where == Where.Cockpit)
            Cockpit.Tick(pc!, dt);

        _able = able;

        if (_hadCar && car == null)
            _leftCarAt = Clock.Now;
        _hadCar = car != null;

        bool passing = !able && car == null && pc != null && (Clock.Now - _leftCarAt < LeftCarSeconds || InPassing(pc));
        _waited = passing ? _waited + dt : 0f;
        _passing = passing && _waited < MostWaitSeconds;
    }

    // On foot anything that takes Batman out of walking ends it, as it ends a call in the game.
    // In the car it works while driving it yourself, not by remote or in battle mode. Flying the
    // Batwing or hanging under it while it lifts the Batmobile keeps the deck on the screen.
    private static Where WhereNow(RPlayerController? pc, RGauntletProjector? projector, RPawnPlayer? pawn, RVehicleBatmobileBase? car)
    {
        if (pc == null || projector is not { IsValid: true } || (projector.ScreenOn && !Ours(projector)))
            return Where.Nowhere;
        if (Wing.I.Flying || Wing.I.Lifting)
            return Where.Nowhere;

        int state = pc.GetStateName().Index;
        if (car == null)
            return pawn != null && state == Walking.Index ? Where.Arm : Where.Nowhere;

        return car is { IsValid: true, bRemoteDrive: false } && state == Driving.Index && !car.IsInBattleMode() ? Where.Cockpit : Where.Nowhere;
    }

    private static bool InPassing(RPlayerController pc)
    {
        int state = pc.GetStateName().Index;
        foreach (FName name in Passing)
        {
            if (name.Index == state)
                return true;
        }

        return false;
    }

    public static void Release()
    {
        Lower();
        GauntletScreen.Release();
        _able = false;
        _passing = false;
        _refused = false;
    }

    private static void Raise(RPlayerController pc, RGauntletProjector projector, Where where, RPawnPlayer? pawn, RVehicleBatmobileBase? car)
    {
        // Tick only raises it while the screen is free. In the car the game puts the screen on
        // the dashboard and trims the HUD by itself.
        projector.StartVideoProjection(new FString(""), new FString(""), bNoMovie: true);

        // With the deck not on it the screen would be a black pane, so it goes on the screen.
        if (!GauntletScreen.Take(projector))
        {
            projector.StopProjection(null!, new FString(""), -1f);
            _refused = true;
            if (!_warned)
            {
                _warned = true;
                Log.Info("The gauntlet screen couldn't show the deck, so it opened on the screen.");
            }

            return;
        }

        _where = where;
        _pawn = pawn;
        _car = car;
        _pc = pc;

        if (where == Where.Cockpit)
            Cockpit.Enter(pc, car!);
        else
            RaiseArm(pc, pawn!);
    }

    private static void RaiseArm(RPlayerController pc, RPawnPlayer pawn)
    {
        // A gadget in hand would hold its own pose over the call stance.
        (pawn.InvManager as RInventoryManager)?.UnequipAllGadgets();

        pawn.BeginChangePose();
        pawn.Pose.TransitionName = Outgoing;
        pawn.Pose.MovementStance = CallStance;
        pawn.Pose.Mirrored = false;
        pawn.Pose.TransitionMirrored = RAnimUtil.EMirrorChoice.MC_No;
        pawn.EndChangePose();
        pawn.SetWalkSpeed();

        _cameraBefore = pc.OverrideWalkCamera;
        pc.OverrideWalkCamera = CallCamera;
    }

    private static void Lower()
    {
        Where where = _where;
        RPawnPlayer? pawn = _pawn;
        RPlayerController? pc = _pc;
        _where = Where.Nowhere;
        _pawn = null;
        _car = null;
        _pc = null;

        if (where == Where.Cockpit)
            Cockpit.Leave(pc);

        if (pawn is { IsValid: true } && pawn.GetMovementStance().Index == CallStance.Index)
        {
            pawn.ChangeMovementStance(Standing);
            pawn.SetWalkSpeed();
        }

        if (pc is not { IsValid: true })
            return;

        if (where == Where.Arm && pc.OverrideWalkCamera.Index == CallCamera.Index)
            pc.OverrideWalkCamera = _cameraBefore;

        // Only our projection is stopped. A real call that took over the screen plays on.
        RGauntletProjector projector = pc.GauntletProjector;
        if (projector is { IsValid: true } && projector.ScreenOn && Ours(projector))
            projector.StopProjection(null!, new FString(""), -1f);
    }

    // A game call always names its video. Ours has none, which the game turns into ".usm".
    private static bool Ours(RGauntletProjector projector) => projector.lastPlayedVideoResource.ToString() is null or "" or ".usm";

    // While walking the game asks this every tick and changes the stance to the answer.
    [Redirect(typeof(RPawnPlayer), nameof(RPawnPlayer.GetWalkingModeMovementStance))]
    private static FName WalkingStance(RPawnPlayer self)
    {
        try
        {
            return _where == Where.Arm && ReferenceEquals(self, _pawn) ? CallStance : self.GetWalkingModeMovementStance();
        }
        catch (Exception ex)
        {
            Log.Error("gauntlet stance", "Walking stance check", ex);
            return Standing;
        }
    }
}
