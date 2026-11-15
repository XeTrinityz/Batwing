using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// The Batwing on its own, covering Batman from the air. It fights drones and helicopters by itself, takes on
// anything else only on a strike order, picks him up, catches him and carries the Batmobile.
internal sealed partial class Escort : Phase
{
    public const float CargoSpeed = 0.75f;
    public const float LowDrop = 250f;
    private const float CruiseSpeed = 3800f;
    private const float StationHeight = 3000f;
    private const double LostGrace = 2.0;
    private const double CutsceneGrace = 1.5;

    private readonly Craft _craft;
    private readonly FlightModel _fm;
    private readonly Streamer _streamer = new();
    private double _leadLostAt = -1.0, _cutsceneAt = -1.0;
    private bool _drove;

    private Escort(Craft craft, Vector3 batman)
    {
        _craft = craft;
        _fm = craft.Fm;
        _stationAngle = MathF.Atan2(_fm.Position.Y - batman.Y, _fm.Position.X - batman.X);
        _drove = World.Driving != null;
        _escortPick.Force();
    }

    public override string Status => Hauling
        ? (_delivery ? "DELIVERY // " : "LIFT // ") + HaulStatus
        : Catching ? "ESCORT // CATCHING" : _pickup ? "ESCORT // PICKUP"
        : _strike != null ? (Chasing(_strike) ? "ESCORT // CHASING" : "ESCORT // STRIKING")
        : Target() != null ? "ESCORT // ENGAGING" : "ESCORT";

    public Craft Craft => _craft;

    public bool Carrying => _craft.Carrying;

    // Brought in by the escort key, or from the driver's seat, or for a lift. It comes in high and fast.
    public static Escort Start(RVehicleBatmobileBase? car)
    {
        Pawn lead = car != null ? Require.InCar(car) : Require.OnFoot();
        Kit kit = Kit.Load();

        Vector3 over = lead.Location + new Vector3(0f, 0f, StationHeight + 2000f);
        float camYaw = CameraHook.ViewYaw(lead);
        Vector3? start = null;
        foreach (float turn in new[] { 180f, 135f, -135f, 90f, -90f, 0f })
        {
            Vector3 s = over + Geo.Facing(camYaw + turn) * 16000f + new Vector3(0f, 0f, 2500f);
            if (Ground.Clear(lead, s, over))
            {
                start = s;
                break;
            }
        }

        Vector3 from = start ?? lead.Location + new Vector3(0f, 0f, 12000f);
        Vector3 toward = Vector3.Normalize(over - from);
        float yaw = Geo.YawOf(toward);
        Craft craft = Craft.Spawn(kit, from, yaw);
        Wing.Use(craft);
        craft.Fm.Place(from, yaw / Geo.Deg, toward * CruiseSpeed);
        craft.Fm.Hover = 0f;
        craft.Fm.HoverWanted = false;
        craft.Fm.Throttle = 0.8f;
        craft.Fm.TopSpeed = Opt.TopSpeed.Number * Geo.Metre;
        craft.Follow();
        craft.StartEngine();

        var escort = new Escort(craft, lead.Location);
        Wing.Go(escort);
        Craft.Play(kit.Called, lead);
        Wing.Toast("ESCORT INBOUND");
        Log.Info("Batwing: escort from " + Geo.Position(from) + ".");
        return escort;
    }

    // Bailing out with escort on bail. The Batwing stays and covers him instead of flying off.
    public static bool AfterBail(Craft craft, Vector3 batman)
    {
        if (!Opt.EscortOnBail.On || !Ground.InCity(batman) || !craft.Alive)
            return false;

        Wing.Go(new Escort(craft, batman));
        Log.Info("Batwing: bailed out, the Batwing stays on escort.");
        return true;
    }

    public void Dismiss()
    {
        // Never drops the Batmobile from the sky. It sets it down first and leaves after.
        if (Carrying)
        {
            _dismissAfter = true;
            SetDownHere();
            return;
        }

        End("ESCORT DISMISSED");
    }

    private void End(string why)
    {
        Log.Info("Batwing: escort ended, " + why.ToLowerInvariant() + ".");
        Departing.From(_craft, 0f);
        Wing.Toast(why);
    }

    public override void Leave()
    {
        EndCombat();
        _craft.Weapons.Off();
    }

    private void EndCombat()
    {
        _craft.Weapons.Off();
        _escortFoe = null;
        _strike = _strikeAim = null;
        _queue.Clear();
        _marks.Clear();
        _marking = false;
    }

    public override void Step(PlayerController pc, float dt)
    {
        if (!_craft.Alive)
        {
            Wing.Go(null);
            return;
        }

        if (dt <= 0f)
            return;

        // The Batmobile when he's driving, Batman otherwise. Getting in or out of the car can leave the
        // controller without a pawn for a moment, so only a lasting gap ends it.
        Pawn? lead = World.Pawn;
        RPawnPlayer? batman = World.Player ?? (lead as RVehicleBatmobileBase)?.Driver as RPawnPlayer;
        if (batman is { IsValid: true } && batman.Health <= 0)
        {
            End("ESCORT ENDED");
            return;
        }

        if (lead is not { IsValid: true } || batman is not { IsValid: true })
        {
            if (_leadLostAt < 0.0)
            {
                _leadLostAt = Clock.Now;
                Log.Detail("Batwing: escort lost track of Batman for a moment, holding.");
            }

            if (Clock.Now - _leadLostAt > LostGrace)
                End("ESCORT ENDED");
            return;
        }

        _leadLostAt = -1.0;
        if (Cutscene(pc))
            return;

        EjectCheck(lead);

        // Still holding the car with no job, like after a missed catch, so it goes down.
        if (Carrying && !Hauling && !Catching)
            SetDownHere();

        Vector3 centre = lead.Location;
        if (!Hauling && !Catching)
        {
            if (_escortPick.Elapsed(0.5))
                Safe.Run("escort targets", () => PickFoe(centre));
            if (_strikeLook.Elapsed(0.1))
            {
                _strikeAim = lead is RPawnPlayer ? StrikeCandidate(centre) : null;
                if (_strikeAim != null)
                    Foes.WatchtowerLog(_strikeAim);
            }

            Marking();
            StrikeCheck(centre);
        }

        if (Hauling && !HaulBefore(dt))
            return;
        if (Wing.Now != this)
            return;
        if (Catching && lead is RPawnPlayer flier && Swoop(flier, dt))
            return;

        FlightInput input = Autopilot(centre, batman, dt);
        _fm.TopSpeed = Opt.TopSpeed.Number * Geo.Metre * (Carrying ? CargoSpeed : 1f);
        Vector3 from = _fm.Position;
        _fm.Step(dt, input);
        Collide(from);

        _craft.Follow();
        _craft.Measure();
        _craft.Show(_craft.Flying(), dt);

        // Batman rides in the car, so the city has to load ahead of the Batwing as it does when he flies it.
        if (Carrying && !_delivery)
            _streamer.Step(_fm.Position, _fm.Velocity);

        if (Hauling)
        {
            HaulAfter();
            return;
        }

        if (Catching)
        {
            if (lead is RPawnPlayer flying)
                CatchCheck(flying);
            return;
        }

        if (_pickup)
        {
            PickupCheck(batman, centre);
            return;
        }

        Orders orders = Orders();
        Safe.Run("escort weapons", () => _craft.Weapons.Step(batman, orders, dt));
    }

    // The cinematic flag can blink on during moves like getting into the Batmobile. Only a real cutscene that
    // keeps it on sends the escort away. True when it ended.
    private bool Cutscene(PlayerController pc)
    {
        if (!World.InCinematic(pc))
        {
            _cutsceneAt = -1.0;
            return false;
        }

        if (_cutsceneAt < 0.0)
            _cutsceneAt = Clock.Now;
        if (Clock.Now - _cutsceneAt < CutsceneGrace)
            return false;

        End("LEFT FOR A CUTSCENE");
        return true;
    }

    // Collisions on escort do no damage. It bounces off what it clips and climbs.
    private void Collide(Vector3 from)
    {
        Vector3 to = _fm.Position;
        if (Vector3.DistanceSquared(from, to) < 0.01f)
            return;

        if (_craft.Cargo is { Alive: true } cargo && cargo.Collide(_craft, from, to, out _, out bool flat))
        {
            if (!flat)
                _climb = MathF.Min(_climb + 2500f, ClimbMost);
            to = _fm.Position;
        }

        float body = Math.Clamp(MathF.Min(_craft.Half.X, _craft.Half.Y) * 0.45f, 150f, 420f);
        var extent = new Vector3(body, body, _craft.BoxDepth(_craft.Half.Z * 0.8f));
        Actor? hit = _craft.Actor.Trace(out Vector3 at, out Vector3 n, to, from, Component.ECollisionFilter.COF_TraceWorld, extent, out _, 0);
        if (hit == null || at == Vector3.Zero || n.LengthSquared() < 0.5f || _craft.IsCargo(hit))
            return;

        _fm.Position = at + n * 20f;
        float into = Vector3.Dot(_fm.Velocity, n);
        if (into < 0f)
            _fm.Velocity -= n * into * 1.3f;
        _fm.Release();
        _climb = MathF.Min(_climb + 2500f, ClimbMost);
        Craft.Burst(_craft.Kit.Impact, at, n);
        if (_scrape.Elapsed(0.25))
            _craft.Sound(_craft.Kit.Scrape);
    }

    private Every _scrape;

    public override void Draw(Canvas canvas) => Hud.Escort(this, canvas);
}
