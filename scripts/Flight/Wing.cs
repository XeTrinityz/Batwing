using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;
using Pov = BmSDK.GameObject.FTPOV;

namespace Trinity.Batwing;

// What the Batwing is doing right now. Each one is made fresh when it starts, so nothing carries over.
internal abstract class Phase
{
    public abstract string Status { get; }

    public virtual Rgba Lamp => Palette.Hud;

    public virtual bool Blink => false;

    public abstract void Step(PlayerController pc, float dt);

    // Called once when another phase takes over or the Batwing goes. Puts back whatever it changed.
    public virtual void Leave()
    {
    }

    public virtual void Draw(Canvas canvas)
    {
    }
}

// The Batwing as the rest of the mod sees it. It holds the craft and the phase and hands calls on.
internal static class Wing
{
    private const float BlendOutSeconds = 0.8f;
    private const double QuietCamera = 0.25;

    private static Phase? _phase;
    private static Craft? _craft;
    private static string? _ended;
    private static bool _endedGood;
    private static bool _tickSaid;
    private static float _blendOut;
    private static Pov _blendFrom;

    public static readonly Voice Voice = new();

    public static Craft? Craft => _craft is { Alive: true } c ? c : null;

    public static Phase? Now => _phase;

    public static bool Piloting => _phase is Pilot;

    public static bool Remote => _phase is Pilot { Remote: true };

    public static bool Escorting => _phase is Escort;

    public static bool Busy => _phase != null;

    public static bool Lifting => _phase is Escort { Hauling: true };

    public static bool Live => _phase is Pilot or Inbound;

    public static bool HidesGameHud => _phase is Pilot && Opt.HideGameHud.On;

    public static string Status => _phase?.Status ?? (World.InGame ? "STANDBY" : "NO WORLD");

    public static Rgba Lamp => _phase?.Lamp ?? Palette.Faint;

    public static bool LampBlink => _phase?.Blink ?? false;

    public static string CallLabel => _phase switch
    {
        Inbound => "WAVE OFF",
        Pilot { Remote: true } => "END REMOTE",
        Pilot { Waiting: true } => "JUMP NOW",
        Pilot => "BAIL OUT",
        _ => "CALL BATWING",
    };

    public static string RemoteLabel => Remote ? "END REMOTE" : "REMOTE CONTROL";

    public static string EscortLabel => Escorting ? "DISMISS ESCORT" : "ESCORT";

    public static string LiftLabel => _phase switch
    {
        Pilot => Craft?.Carrying == true ? "DROP BATMOBILE" : "HOOK BATMOBILE",
        Escort { Hauling: true } e => e.Carrying ? "SET DOWN HERE" : "CANCEL LIFT",
        _ => World.Driving != null ? "LIFT TO WAYPOINT" : "BRING BATMOBILE",
    };

    public static string LiftNote => _phase is Escort { Hauling: true } e ? e.HaulStatus : Craft?.Carrying == true ? "CARRYING" : "";

    // Takes over from whatever phase there was.
    public static void Go(Phase? next)
    {
        Phase? was = _phase;
        _phase = next;
        if (was != null && !ReferenceEquals(was, next))
            Safe.Run("phase leave", was.Leave);
        if (next == null)
            Drop();
    }

    public static void Use(Craft craft)
    {
        if (!ReferenceEquals(_craft, craft))
            Drop();
        _craft = craft;
    }

    // Takes the craft out of the world and leaves nothing behind.
    public static void Drop()
    {
        Craft? craft = _craft;
        _craft = null;
        craft?.Remove();
    }

    // Ends whatever is going on, with a notice saying why.
    public static void Stop(string? why, bool good)
    {
        Go(null);
        if (why == null)
            return;
        _ended = why;
        _endedGood = good;
    }

    public static void Fail(Exception ex)
    {
        Log.Info("Batwing stopped: " + Log.Why(ex));
        Safe.Run("batwing stop", () => Go(null));
        _ended = ex is Refusal ? ex.Message.ToUpperInvariant() : "STOPPED: " + ex.Message.ToUpperInvariant();
        _endedGood = false;
    }

    public static void Toast(string text) => Toasts.Show(text, _phase is Pilot or Escort);

    public static void BlendOut(Pov from)
    {
        _blendFrom = from;
        _blendOut = from.FOV > 1f ? BlendOutSeconds : 0f;
    }

    // Requests the player can make. Anything that can't be done right now comes back as a notice.
    private static void Request(string what, Action run)
    {
        try
        {
            run();
        }
        catch (Refusal no)
        {
            Log.Info("Batwing: " + what + " refused, " + no.Message + ".");
            Toasts.Notice(no.Message.ToUpperInvariant(), false);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    public static void CallPressed() => Request("call", () =>
    {
        switch (_phase)
        {
            case Pilot pilot:
                pilot.Exit();
                break;
            case Inbound inbound:
                Departing.From(inbound.Craft, 0f);
                Toast("WAVED OFF");
                break;
            case Escort escort:
                escort.Board();
                break;
            default:
                Inbound.Call();
                break;
        }
    });

    public static void RemotePressed() => Request("remote", () =>
    {
        if (_phase is Pilot { Remote: true } pilot)
            pilot.Exit();
        else if (_phase != null)
            throw new Refusal(_phase is Pilot ? "already flying" : "the Batwing is busy");
        else
            Pilot.StartRemote();
    });

    public static void EscortPressed() => Request("escort", () =>
    {
        if (_phase is Escort escort)
            escort.Dismiss();
        else if (_phase != null)
            throw new Refusal(_phase is Pilot ? "already flying" : "the Batwing is busy");
        else
            Escort.Start(World.Driving);
    });

    public static void StrikePressed() => Request("strike", () =>
    {
        if (_phase is Escort escort)
            escort.Strike();
        else
            throw new Refusal("strikes are for the escort, call it with " + Prompt.Of(Act.Escort).Text);
    });

    public static void LiftPressed() => Request("lift", () =>
    {
        if (!World.Ready)
            throw new Refusal("the game is still loading");
        if (World.Paused)
            throw new Refusal("not while paused");

        switch (_phase)
        {
            case Pilot pilot:
                pilot.HookOrDrop();
                return;
            case Inbound or Departing:
                throw new Refusal("the Batwing is busy");
            case Escort escort:
                escort.Lift();
                return;
        }

        if (World.Driving is { } car)
            Escort.StartRide(car);
        else
            Escort.StartDelivery();
    });

    // Runs from Batman's camera update, which is where the Batwing moves.
    public static bool View(R3rdPersonCamera cam, PlayerController pc, in Pov game, ref Pov pov, float dt)
    {
        if (_phase == null && _blendOut <= 0f && !Voice.Playing)
            return false;

        try
        {
            dt = World.Paused ? 0f : Math.Clamp(dt, 0f, 0.1f);
            Voice.Step(Craft, dt);
            if (_phase is Pilot pilot)
            {
                pilot.Turn(game.Rotation, dt);
                _phase.Step(pc, dt);
                if (_phase is Pilot still && ReferenceEquals(still, pilot))
                {
                    pov = pilot.View(game, dt);
                    return true;
                }
            }
            else
            {
                _phase?.Step(pc, dt);
            }

            if (_blendOut <= 0f)
                return false;

            _blendOut = MathF.Max(0f, _blendOut - MathF.Max(dt, 1f / 60f));
            pov = ChaseCam.Lerp(_blendFrom, game, Ease.Smooth(1f - _blendOut / BlendOutSeconds));
            return true;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return false;
        }
    }

    // The game tick. The escort and departure normally run from Batman's camera update. If that goes quiet,
    // as it can in the Batmobile, they run from here so the Batwing doesn't freeze in the air.
    public static void Tick(float dt)
    {
        CargoWatch.Tick();
        if (_phase is not (Escort or Departing) || Clock.Now - CameraHook.LastUpdate < QuietCamera || World.Paused || World.Pc is not { } pc)
            return;

        if (!_tickSaid)
        {
            _tickSaid = true;
            Log.Info("Batwing: the camera update went quiet (camera " + (pc.PlayerCamera?.Class.Name.ToString() ?? "none") + ", pawn "
                + (World.Pawn?.Class.Name.ToString() ?? "none") + "), running from the game tick.");
        }

        try
        {
            Voice.Step(Craft, dt);
            _phase.Step(pc, Math.Clamp(dt, 0f, 0.1f));
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    public static void Frame()
    {
        GameHud.Tick(HidesGameHud);
        if (_ended == null)
            return;

        Toasts.Notice(_ended, _endedGood);
        _ended = null;
    }

    public static void Draw(Canvas canvas) => _phase?.Draw(canvas);

    public static bool HasHud => _phase != null && Opt.Hud.On || _phase is Pilot;

    // Gunfire meant for Batman in the cockpit hits the Batwing.
    public static bool Absorbs(RPawnPlayer pawn, int damage, Class? type, Vector3 at) =>
        _phase is Pilot pilot && pilot.Absorbs(pawn, damage, type, at);

    public static void Started()
    {
        Craft.ClearLeftovers();
        Safe.Run("pilot leftover", Pilot.Recover);
        Safe.Run("cargo leftover", Cargo.Recover);
    }

    public static void WorldChanged()
    {
        Quit();
        Batmobile.Forget();
        Foes.Forget();
        CargoWatch.Forget();
    }

    public static void LeftGame()
    {
        WorldChanged();
        Kit.Forget();
        Rumble.Release();
    }

    public static void Shutdown() => Quit();

    private static void Quit()
    {
        Go(null);
        Voice.Stop();
        _blendOut = 0f;
    }
}
