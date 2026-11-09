using System.Globalization;
using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    public static readonly Wing I = new();

    private const string Title = "BATWING";

    private const string KitOwner = "batwing";

    private const string HeldKey = "batwing.held";

    private const float Metre = 100f;
    private const float Deg = Geo.Deg;

    private enum Phase
    {
        Off,
        Arriving,
        Flying,
        Leaving,
        Escort,
    }

    private Phase _phase;
    private readonly FlightModel _fm = new();
    private string? _ended;
    private bool _endedGood;
    private Every _reassert;

    public bool OwnsCamera => _phase == Phase.Flying || _blendOut > 0f;

    public bool Flying => _phase == Phase.Flying;

    public bool HidesHud => _phase == Phase.Flying && Opt.HideGameHud.On;

    public bool HasHud => _phase is Phase.Flying or Phase.Arriving or Phase.Escort;

    public bool CraftLive => _phase is Phase.Flying or Phase.Arriving;

    public string ActionLabel => _phase switch
    {
        Phase.Arriving => "WAVE OFF",
        Phase.Escort => "CALL BATWING",
        Phase.Flying => _remote ? "END REMOTE" : _bailWait >= 0f ? "JUMP NOW" : "BAIL OUT",
        _ => "CALL BATWING",
    };

    public string StateLabel => _phase switch
    {
        Phase.Arriving => "INBOUND",
        Phase.Flying => _remote ? (_remoteFar ? "REMOTE // WEAK SIGNAL" : "REMOTE") : _landed ? "LANDED" : _bailWait >= 0f ? "WAITING FOR GROUND" : Carrying ? "CARRYING BATMOBILE" : _fm.Boosting ? "AFTERBURNER" : _fm.Hover > 0.5f ? "HOVER" : "WINGS",
        Phase.Leaving => "DEPARTING",
        Phase.Escort => Hauling ? (_delivery ? "DELIVERY // " : "LIFT // ") + HaulState() : Catching ? "ESCORT // CATCHING" : _pickup ? "ESCORT // PICKUP" : _strike != null ? (_strike is RPawnVillain ? "ESCORT // STRIKING" : "ESCORT // CHASING") : EscortTarget() != null ? "ESCORT // ENGAGING" : "ESCORT",
        _ => World.InGame ? "STANDBY" : "NO WORLD",
    };

    public Rgba Lamp => _phase switch
    {
        Phase.Flying or Phase.Escort => Palette.Hud,
        Phase.Arriving => Palette.CautionNow,
        Phase.Leaving => Palette.Dim,
        _ => Palette.Faint,
    };

    public bool LampBlink => _phase == Phase.Arriving || (_phase == Phase.Flying && _bailWait >= 0f);

    public float PitchDegrees => _fm.Pitch() * Deg;

    public float BankRadians => _fm.Bank();

    public float ThrottleFraction => _fm.Hover > 0.5f ? Math.Clamp(_fm.Jets / 2f, 0f, 1f) : _fm.Throttle;

    public float HullFraction => Math.Clamp(_hull, 0f, 1f);

    public float GLoad => _fm.GLoad;

    public string SpeedText => Speed(_fm.Velocity.Length(), out string unit) + " " + unit;

    public string AltitudeText => _ground >= GroundReach ? "---" : Fmt.Whole(MathF.Max(0f, _ground) / Metre) + " M";

    private static string Speed(float unitsPerSecond, out string unit)
    {
        float factor;
        switch (Opt.SpeedUnit.Index)
        {
            case 1:
                unit = "KM/H";
                factor = 0.036f;
                break;
            case 2:
                unit = "M/S";
                factor = 0.01f;
                break;
            default:
                unit = "MPH";
                factor = 0.022369363f;
                break;
        }

        return MathF.Round(unitsPerSecond * factor).ToString(CultureInfo.InvariantCulture);
    }

    public void Started() => Recover();

    public void WorldChanged()
    {
        Stop(silent: true);
        Car.Forget();
    }

    public void LeftGame()
    {
        Stop(silent: true);
        ForgetKit();
        Car.Forget();
    }

    public void Shutdown()
    {
        Stop(silent: true);
        Pad.HoldButtons = false;
    }

    private double _viewAt;
    private bool _tickStepSaid;

    public void Tick(float dt)
    {
        Safe.Run("batwing drop", FallStep);
        Safe.Run("batwing settle", SettleStep);

        // The escort and departure normally run from Batman's camera update. If that goes quiet, like it
        // might in the Batmobile, run them from the game tick so the Batwing doesn't freeze in the air.
        if (_phase is not (Phase.Escort or Phase.Leaving) || Clock.Now - _viewAt < 0.25 || World.Paused || World.Pc is not { } pc)
            return;

        if (!_tickStepSaid)
        {
            _tickStepSaid = true;
            Log.Info(Title + ": camera update went quiet (camera " + (pc.PlayerCamera?.Class.Name.ToString() ?? "none") + ", pawn "
                + (World.Pawn?.Class.Name.ToString() ?? "none") + "), running from the game tick.");
        }

        try
        {
            float step = Math.Clamp(dt, 0f, 0.1f);
            if (_phase == Phase.Escort)
                EscortStep(pc, step);
            else
                LeaveStep(step);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    public void Frame()
    {
        Pad.HoldButtons = _phase == Phase.Flying && !Deck.IsOpen;

        if (_ended == null)
            return;

        Readout.Notice(Title, _ended, _endedGood);
        _ended = null;
    }

    public void Toggle()
    {
        switch (_phase)
        {
            case Phase.Flying:
                Safe.Run("batwing exit", () =>
                {
                    if (_remote)
                        EndRemote("REMOTE OFF");
                    else if (_bailWait >= 0f)
                        Exit("BAILED OUT");
                    else
                        Bailout();
                });
                break;
            case Phase.Arriving:
                Safe.Run("batwing wave off", () => Depart(0f));
                Readout.Toast(Title, "WAVED OFF", false);
                break;
            default:
                try
                {
                    Call();
                }
                catch (Exception ex)
                {
                    Log.Info(Title + " refused: " + ex.Message);
                    Readout.Notice(Title, ex.Message.ToUpperInvariant(), false);
                }

                break;
        }
    }

    private static RPawnPlayer Require()
    {
        if (!World.Ready)
            throw new InvalidOperationException("the game is still loading");
        if (World.Paused)
            throw new InvalidOperationException("not while paused");

        PlayerController pc = World.Pc ?? throw new InvalidOperationException("the game is still loading");
        if (World.Pawn is RVehicle)
            throw new InvalidOperationException("get out of the Batmobile first");
        if (World.Pawn is not RPawnPlayer pawn)
            throw new InvalidOperationException("the game is still loading");
        if (Hooks.InCinematic(pc) || Hooks.PlayerCam() is not { } cam || !Hooks.IsPlayerView(cam, pc))
            throw new InvalidOperationException("not during a cutscene");
        if (pawn.Health <= 0)
            throw new InvalidOperationException("not while you're down");
        if (!Ground.InCity(pawn.Location))
            throw new InvalidOperationException("only out in the city");

        return pawn;
    }

    private void Call()
    {
        RPawnPlayer pawn = Require();
        if (_phase == Phase.Flying)
            return;

        _remote = false;
        Kit kit = LoadKit();
        bool airborne = pawn.Physics != Actor.EPhysics.PHYS_Walking && !NearGround(pawn);

        // The escort is already close, so he goes up into that one rather than a second one flying in.
        if (_phase == Phase.Escort && _craft is { IsValid: true } && EscortPickup(pawn, airborne))
            return;

        if (_phase != Phase.Off)
            DropCraft();

        if (!Opt.FlyIn.On || airborne)
        {
            BoardNow(pawn, kit, airborne);
            return;
        }

        StartArrival(pawn, kit);
    }

    // Some ground moves aren't PHYS_Walking. Boarding in place there spawns the craft in the floor and wrecks it.
    private static bool NearGround(RPawnPlayer pawn)
    {
        Vector3 at = pawn.Location;
        float reach = Ground.Lift(pawn) + 400f;
        return Ground.Ray(pawn, at, at - new Vector3(0f, 0f, reach), out _, out _);
    }

    private void Exit(string why)
    {
        if (_phase != Phase.Flying)
            return;

        _bailWait = -1f;
        if (_cargo is { } held)
        {
            bool low = Floor(held, _rest + LowDrop, out _);
            DropCargo(low ? Vector3.Zero : _fm.Velocity, low);
        }

        RPawnPlayer? pawn = HeldPawn();
        LetGo(pawn, Bail());
        if (pawn is { IsValid: true } && EscortAfterBail(pawn.Location))
        {
            Toast(why + " // ON ESCORT");
            return;
        }

        Hatch(close: false);
        Depart(1.2f);
        Toast(why);
    }

    private void Stop(bool silent)
    {
        bool wasFlying = _phase == Phase.Flying;
        if (wasFlying)
            LetGo(HeldPawn(), null);

        DropCraft();
        _phase = Phase.Off;
        _haul = Haul.None;
        _liftCar = null;
        _catchUntil = -1.0;
        _blendIn = 0f;
        if (silent)
        {
            _blendOut = 0f;
            StopVoice();
        }

        Pad.HoldButtons = false;
    }

    private void Fail(Exception ex)
    {
        Log.Info(Title + " stopped: " + Log.Why(ex));
        Safe.Run("batwing stop", () => Stop(silent: false));
        _ended = "STOPPED: " + ex.Message.ToUpperInvariant();
        _endedGood = false;
    }

    private void Toast(string state) => Readout.Toast(Title, state, _phase == Phase.Flying);

    private void Recover()
    {
        foreach (RBatwing craft in World.All<RBatwing>())
        {
            if (craft.Tag.Index == CraftTag.Index && !craft.bDeleteMe)
                Safe.Run("batwing leftover", () => craft.Destroy());
        }

        Safe.Run("batwing remote leftover", RecoverRemote);
        Safe.Run("batwing cargo leftover", RecoverCargo);

        if (!Shared.GetValue(HeldKey, false))
            return;

        Shared.Set(HeldKey, false);
        if (World.Player is not { IsValid: true } pawn)
            return;

        Log.Info(Title + ": scripts reloaded mid flight, restoring Batman.");
        if (pawn.bHidden)
            pawn.SetHidden(false);
        if (pawn.bTickIsDisabled)
            pawn.SetTickIsDisabled(false);
        if (pawn.Mesh is { IsValid: true } mesh)
            mesh.bPauseAnims = false;
        pawn.SetCollision(true, true, pawn.bIgnoreEncroachers);
        pawn.SetPhysics(Actor.EPhysics.PHYS_Falling, false);
    }
}
