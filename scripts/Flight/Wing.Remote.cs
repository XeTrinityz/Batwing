using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Wing
{
    private const string RemoteKey = "batwing.remote";

    // Districts load around Batman, not the craft, so it has to stay close to him.
    private const float LeashSoft = 25000f;
    private const float LeashHard = 32000f;
    private const float LostAfter = 4f;
    private const float StaticMost = 1f;

    // With a Batmobile around its loaded-edge limit does the real work and the radius is only a backstop.
    private const float RadiusSoftWide = 60000f;
    private const float RadiusHardWide = 70000f;
    private const float EdgeSoft = 8000f;
    private const float EdgeHard = 2000f;

    private bool _remote;
    private float _outFor;
    private int _remoteHealth;
    private Vector3 _remoteFrom;
    private bool _remoteFar;

    public bool Remote => _remote && _phase == Phase.Flying;

    public string RemoteLabel => Remote ? "END REMOTE" : "REMOTE CONTROL";

    public void RemoteToggle()
    {
        if (Remote)
        {
            Safe.Run("batwing remote end", () => EndRemote("REMOTE OFF"));
            return;
        }

        if (_phase != Phase.Off)
        {
            Readout.Notice(Title, _phase == Phase.Flying ? "ALREADY FLYING" : "THE BATWING IS BUSY", false);
            return;
        }

        try
        {
            StartRemote();
        }
        catch (Exception ex)
        {
            Log.Info(Title + " remote refused: " + ex.Message);
            Readout.Notice(Title, ex.Message.ToUpperInvariant(), false);
        }
    }

    private void StartRemote()
    {
        RPawnPlayer pawn = Require();
        if (pawn.Physics != Actor.EPhysics.PHYS_Walking)
            throw new InvalidOperationException("stand on the ground first");
        if (Ground.Ray(pawn, pawn.Location + new Vector3(0f, 0f, 100f), pawn.Location + new Vector3(0f, 0f, SkyCheck), out _, out _))
            throw new InvalidOperationException("it needs open sky above you");

        Kit kit = LoadKit();
        float yaw = CameraYaw(pawn);
        Vector3 at = HoverPoint(pawn);
        SpawnCraft(kit, at, yaw);
        _fm.Place(at, yaw / Deg, Vector3.Zero);
        _fm.Hover = 1f;
        _fm.HoverWanted = true;
        _fm.Throttle = 0.7f;
        Place(_fm.Position, _fm.Forward, _fm.Right, _fm.Up);

        _remote = true;
        _remoteHealth = pawn.Health;
        _remoteFar = false;
        _remoteFrom = pawn.Location;
        TakeOff(pawn);
        Log.Info(Title + ": remote control from " + Geo.Position(pawn.Location) + ".");
    }

    // Batman stays put and can still be hit. The look input stays live since that's how the craft is aimed.
    private void Tether(RPawnPlayer pawn)
    {
        Shared.Set(RemoteKey, true);
        pawn.Velocity = Vector3.Zero;
        _outFor = 0f;
        _edgeKnown = _edgeChecked = _edgeBad = false;
        _edgeCheck.Force();
        if (World.Pc is { } pc)
            Safe.Run("batwing remote input", () => pc.IgnoreMoveInput(true));
        Safe.Run("batwing remote static", StaticOn);
        MuteKeys();
    }

    private void Untether()
    {
        _remote = false;
        _remoteFar = false;
        StaticOff();
        if (!Shared.GetValue(RemoteKey, false))
            return;

        Shared.Set(RemoteKey, false);
        if (World.Pc is { } pc)
            Safe.Run("batwing remote input", () => pc.IgnoreMoveInput(false));
    }

    // Scripts reloaded mid remote. Our IgnoreMoveInput push is still on the stack, so take it off.
    private static void RecoverRemote()
    {
        if (!Shared.GetValue(RemoteKey, false))
            return;

        Shared.Set(RemoteKey, false);
        if (World.Pc is { } pc && pc.bIgnoreMoveInput > 0)
        {
            Log.Info(Title + ": scripts reloaded mid remote, giving Batman his legs back.");
            pc.IgnoreMoveInput(false);
        }
    }

    private void EndRemote(string why)
    {
        if (!Remote)
            return;

        LetGo(null, null);
        Depart(0.6f);
        Toast(why);
    }

    private RPawnPlayer? RemotePawn() =>
        World.Player is { IsValid: true, bDeleteMe: false } p ? p : null;

    // Returns false when remote control ended this frame.
    private bool RemoteChecks(RPawnPlayer pawn, float dt)
    {
        // Health catches gunfire and melee alike. Melee goes through DamagedBy, not TakeDamage.
        if (pawn.Health < _remoteHealth)
        {
            EndRemote("BATMAN UNDER ATTACK");
            return false;
        }

        _remoteHealth = pawn.Health;

        if (Vector3.DistanceSquared(pawn.Location, _remoteFrom) > MovedAway * MovedAway)
        {
            Log.Info(Title + ": game moved Batman during remote control, ending it.");
            EndRemote("REMOTE LOST");
            return false;
        }

        pawn.Velocity = Vector3.Zero;
        Vector3 back = pawn.Location - _fm.Position;
        float far = back.Length();
        Vector3 home = far > 1f ? back / far : Vector3.Zero;
        EdgeCheck(far);

        // How weak the signal is: 0 at the soft limit, 1 at the hard one. Uses the Batmobile's own remote
        // boundary when there's a Batmobile, with a wide radius around Batman as a backstop.
        float soft = _edgeKnown ? RadiusSoftWide : LeashSoft;
        float hard = _edgeKnown ? RadiusHardWide : LeashHard;
        float over = (far - soft) / (hard - soft);
        if (_edgeKnown)
        {
            float edgeOver = (EdgeSoft - _edge) / (EdgeSoft - EdgeHard);
            if (edgeOver > over)
            {
                over = edgeOver;
                if (_edgeSafe.LengthSquared() > 0.01f)
                    home = Vector3.Normalize(_edgeSafe);
            }
        }

        StaticStep(Math.Clamp((over + 1f) * 0.5f, 0f, 1f));
        _remoteFar = over > 0f;
        if (!_remoteFar || home == Vector3.Zero)
        {
            _outFor = 0f;
            return true;
        }

        // Past the soft limit it gets pulled back harder the further out it is. At the hard limit it can't go
        // any further out, and only a long stay out there (pushed by something) drops the link.
        _fm.Velocity += home * (2500f * dt * Math.Clamp(over * 3f, 0.5f, 4f));
        if (over < 1f)
        {
            _outFor = 0f;
            return true;
        }

        float outward = -Vector3.Dot(_fm.Velocity, home);
        if (outward > 0f)
            _fm.Velocity += home * outward;
        _outFor += dt;
        if (_outFor > LostAfter)
        {
            EndRemote("SIGNAL LOST");
            return false;
        }

        return true;
    }

    private bool _edgeKnown, _edgeChecked, _edgeBad;
    private float _edge = float.MaxValue;
    private Vector3 _edgeSafe;
    private Every _edgeCheck;

    // The Batmobile's remote limit is how far it is from the edge of the loaded city, not a fixed radius.
    // Asking the Batmobile about the Batwing's spot gives the same limit.
    private void EdgeCheck(float fromBatman)
    {
        if (!_edgeCheck.Elapsed(0.2))
            return;

        if (Car.Current() is not { IsValid: true } car)
        {
            _edgeKnown = false;
            return;
        }

        float edge = 0f;
        Vector3 safe = Vector3.Zero;
        byte outside = 0;
        if (!Safe.Try("batwing remote edge", () =>
            {
                edge = car.GetLocationDistanceToRemoteControlBoundary(_fm.Position, 0f, 0f, out safe, false, out outside);
                return true;
            }))
        {
            _edgeKnown = false;
            return;
        }

        // Batman starts in loaded city, so a first reading near or past the edge means we've misread it.
        if (!_edgeChecked)
        {
            _edgeChecked = true;
            _edgeBad = outside != 0 || edge <= EdgeSoft;
            if (_edgeBad)
                Log.Info(Title + ": remote edge read " + Fmt.Whole(edge / Metre) + " m at the start, not trusting it. Using the radius.");
        }

        if (_edgeBad)
        {
            _edgeKnown = false;
            return;
        }

        _edgeKnown = true;
        _edge = outside != 0 ? -EdgeSoft : edge;
        _edgeSafe = safe;
        if (_edgeLog.Elapsed(2.0))
        {
            Log.Detail(Title + ": remote edge " + Fmt.Whole(edge / Metre) + " m" + (outside != 0 ? " (outside the world)" : "")
                + ", safe way " + Geo.Position(safe) + ", " + Fmt.Whole(fromBatman / Metre) + " m from Batman.");
        }
    }

    private Every _edgeLog;

    private static readonly FName StaticAmount = new("Static Amount");
    private static readonly FName EdgeIntensity = new("Edge_Intensity");

    private RViewPostProcess? _static;
    private MaterialInstance? _staticMat;
    private float _staticBase, _edgeBase;

    // The Batmobile remote's own screen static. Its strength is the material's "Static Amount", which sits at
    // 0 until the game raises it, so it's ramped up with range. The vignette edge grows a little too.
    private void StaticOn()
    {
        _static = null;
        _staticMat = null;
        if (Hooks.PlayerCam() is not { } cam || cam.RemoteBatmobilePP is not { IsValid: true } pp || World.Pc is not RPlayerController pc)
        {
            Log.Info(Title + ": no remote static on this camera.");
            return;
        }

        pp.Activate(pc);
        _static = pp;
        MaterialInstance? live = pp.Effect?.Material as MaterialInstance ?? pp.ViewMTI ?? pp.ViewM;
        if (live is not { IsValid: true } || !live.GetScalarParameterValue(StaticAmount, out _staticBase))
        {
            Log.Info(Title + ": remote static has no Static Amount to turn up.");
            return;
        }

        if (!live.GetScalarParameterValue(EdgeIntensity, out _edgeBase))
            _edgeBase = -1f;
        _staticMat = live;
    }

    private void StaticStep(float t)
    {
        if (_staticMat is not { IsValid: true } mat)
            return;

        float eased = t * t;
        mat.SetScalarParameterValue(StaticAmount, _staticBase + StaticMost * eased);
        if (_edgeBase >= 0f)
            mat.SetScalarParameterValue(EdgeIntensity, _edgeBase * (1f + 0.5f * eased));
    }

    private void StaticOff()
    {
        RViewPostProcess? pp = _static;
        MaterialInstance? mat = _staticMat;
        _static = null;
        _staticMat = null;
        Safe.Run("batwing remote static", () =>
        {
            if (mat is { IsValid: true })
            {
                mat.SetScalarParameterValue(StaticAmount, _staticBase);
                if (_edgeBase >= 0f)
                    mat.SetScalarParameterValue(EdgeIntensity, _edgeBase);
            }

            if (pp is { IsValid: true })
                pp.Deactivate();
        });
    }

    public bool RemoteFar => Remote && _remoteFar;
}
