using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

internal sealed partial class Pilot
{
    private const string RemoteKey = "batwing.remote";

    // Districts load round Batman, not the craft, so it has to stay close to him.
    private const float LeashSoft = 25000f, LeashHard = 32000f;
    private const float LostAfter = 4f;
    private const float StaticMost = 1f;

    // With a Batmobile about, its own remote limit does the real work and the radius is only a backstop.
    private const float RadiusSoftWide = 60000f, RadiusHardWide = 70000f;
    private const float EdgeSoft = 8000f, EdgeHard = 2000f;

    private static readonly FName StaticAmount = new("Static Amount");
    private static readonly FName EdgeIntensity = new("Edge_Intensity");

    private float _outFor;
    private int _remoteHealth;
    private Vector3 _remoteFrom;
    private bool _far;
    private bool _edgeKnown, _edgeChecked, _edgeBad;
    private float _edge = float.MaxValue;
    private Vector3 _edgeSafe;
    private Every _edgeCheck, _edgeLog;
    private RViewPostProcess? _static;
    private MaterialInstance? _staticMat;
    private float _staticBase, _edgeBase;

    public bool WeakSignal => Remote && _far;

    // Seconds left before the link drops, while it's past the limit.
    public float SignalLeft => _outFor > 0f ? MathF.Max(0f, LostAfter - _outFor) : -1f;

    public static void StartRemote()
    {
        RPawnPlayer pawn = Require.OnFoot();
        if (pawn.Physics != Actor.EPhysics.PHYS_Walking)
            throw new Refusal("stand on the ground first");
        Require.Sky(pawn);

        Kit kit = Kit.Load();
        float yaw = CameraHook.ViewYaw(pawn);
        Vector3 at = pawn.Location + new Vector3(0f, 0f, Inbound.HoverAbove);
        Craft craft = Craft.Spawn(kit, at, yaw);
        Wing.Use(craft);
        craft.Fm.Place(at, yaw / Geo.Deg, Vector3.Zero);
        craft.Fm.Hover = 1f;
        craft.Fm.HoverWanted = true;
        craft.Fm.Throttle = 0.7f;
        craft.Follow();
        TakeOff(craft, pawn, remote: true);
        Log.Info("Batwing: remote control from " + Geo.Position(pawn.Location) + ".");
    }

    // Batman stays put and can still be hit. His look input stays live, since that's how the craft is aimed.
    private void Tether()
    {
        Shared.SetFlag(RemoteKey, true);
        _batman.Velocity = Vector3.Zero;
        _remoteHealth = _batman.Health;
        _remoteFrom = _batman.Location;
        _outFor = 0f;
        _edgeCheck.Force();
        if (World.Pc is { } pc)
            Safe.Run("remote input", () => pc.IgnoreMoveInput(true));
        Safe.Run("remote static", StaticOn);
    }

    private void Untether()
    {
        _far = false;
        StaticOff();
        if (!Shared.Flag(RemoteKey))
            return;

        Shared.SetFlag(RemoteKey, false);
        if (World.Pc is { } pc)
            Safe.Run("remote input", () => pc.IgnoreMoveInput(false));
    }

    // Scripts reloaded mid remote. Our hold on Batman's legs is still on the game's stack, so it's taken off.
    private static void RecoverRemote()
    {
        if (!Shared.Flag(RemoteKey))
            return;

        Shared.SetFlag(RemoteKey, false);
        if (World.Pc is { } pc && pc.bIgnoreMoveInput > 0)
        {
            Log.Info("Batwing: scripts reloaded mid remote, giving Batman his legs back.");
            pc.IgnoreMoveInput(false);
        }
    }

    private void EndRemote(string why)
    {
        Release(null, null);
        Departing.From(_craft, 0.6f);
        Wing.Toast(why);
    }

    // False when remote control ended this frame.
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
            Log.Info("Batwing: the game moved Batman during remote control, ending it.");
            EndRemote("REMOTE LOST");
            return false;
        }

        pawn.Velocity = Vector3.Zero;
        Vector3 back = pawn.Location - _fm.Position;
        float far = back.Length();
        Vector3 home = far > 1f ? back / far : Vector3.Zero;
        EdgeCheck(far);

        // How weak the signal is: 0 at the soft limit, 1 at the hard one.
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
        _far = over > 0f;
        if (!_far || home == Vector3.Zero)
        {
            _outFor = 0f;
            return true;
        }

        // Past the soft limit it's pulled back harder the further out it is. At the hard limit it can't go any
        // further, and only a long stay out there drops the link.
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
        if (_outFor <= LostAfter)
            return true;

        EndRemote("SIGNAL LOST");
        return false;
    }

    // The Batmobile's remote limit is how far it is from the edge of the loaded city. Asking the Batmobile about
    // the Batwing's spot gives the same limit.
    private void EdgeCheck(float fromBatman)
    {
        if (!_edgeCheck.Elapsed(0.2))
            return;

        if (Batmobile.Current() is not { IsValid: true } car)
        {
            _edgeKnown = false;
            return;
        }

        float edge = 0f;
        Vector3 safe = Vector3.Zero;
        byte outside = 0;
        if (!Safe.Try("remote edge", () =>
            {
                edge = car.GetLocationDistanceToRemoteControlBoundary(_fm.Position, 0f, 0f, out safe, false, out outside);
                return true;
            }))
        {
            _edgeKnown = false;
            return;
        }

        // Batman starts in loaded city, so a first reading near or past the edge means it's being misread.
        if (!_edgeChecked)
        {
            _edgeChecked = true;
            _edgeBad = outside != 0 || edge <= EdgeSoft;
            if (_edgeBad)
                Log.Info("Batwing: remote edge read " + Fmt.Metres(edge) + " at the start, using the radius instead.");
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
            Log.Detail("Batwing: remote edge " + Fmt.Metres(edge) + (outside != 0 ? " (outside the world)" : "") + ", " + Fmt.Metres(fromBatman) + " from Batman.");
    }

    // The Batmobile remote's own screen static, turned up with range.
    private void StaticOn()
    {
        if (CameraHook.PlayerCam() is not { } cam || cam.RemoteBatmobilePP is not { IsValid: true } pp || World.Pc is not RPlayerController pc)
        {
            Log.Detail("Batwing: no remote static on this camera.");
            return;
        }

        pp.Activate(pc);
        _static = pp;
        MaterialInstance? live = pp.Effect?.Material as MaterialInstance ?? pp.ViewMTI ?? pp.ViewM;
        if (live is not { IsValid: true } || !live.GetScalarParameterValue(StaticAmount, out _staticBase))
            return;

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
        Safe.Run("remote static", () =>
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
}
