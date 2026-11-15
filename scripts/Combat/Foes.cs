using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// What counts as an enemy and what state it's in. Shared by the guns, the escort and the Batmobile drop.
internal static class Foes
{
    private static readonly FName Spine = new("Bip01_Spine2");
    private const float RiotPush = 600f;

    // Vehicles with a thug aboard, from the last scan. Those are fair game whatever the game calls them.
    public static readonly HashSet<nint> Manned = new();

    // Turrets and tanks that were finished off. Some don't show it in any flag the guns can read.
    private static readonly HashSet<nint> TakenOut = new();
    private static readonly HashSet<nint> TowersLogged = new();

    public static void Forget()
    {
        Manned.Clear();
        TakenOut.Clear();
        TowersLogged.Clear();
    }

    public static bool Is(Actor? a) => a switch
    {
        null => false,
        _ when Turret(a) => TurretUp(a),
        RVehicleHeavyTank t when HeavyTank(t) => TankUp(t),
        RVehicleNPC v => Hostile(v) || (Manned.Contains(v.Ptr) && Wreckable(v)),
        RHelicopterBase h => h is { IsValid: true, bDeleteMe: false, bHidden: false, IsFriendly: false, bFriendly: false },
        _ => false,
    };

    // Fair game for the guns or a strike, thugs included.
    public static bool Strikeable(Actor a) => a is RPawnVillain thug ? CanStrike(thug) : Is(a);

    public static bool Hostile(RVehicleNPC v) => Wreckable(v) && !v.CivilianVehicle && v.CurrentBehaviour is { IsValid: true };

    // Can be shot up at all, whoever owns it. A thug driving an ordinary car fails Hostile.
    public static bool Wreckable(RVehicleNPC v) =>
        v is { IsValid: true, bDeleteMe: false, bHidden: false, DroneHacked: false } && v.Health > 0 &&
        v is not RVehicleStaticTank && v is not RVehicleHeavyTank;

    // The escort only goes after these when Batman orders a strike: militia cars, APCs, turrets, heavy tanks
    // and anything with a thug aboard. Drone tanks are their own class and stay fair game.
    public static bool OrderOnly(Actor foe) => foe is RVehicleCar || Apc(foe) || Tough(foe) || (foe is RVehicleNPC v && Manned.Contains(v.Ptr));

    // The game flags plain militia cars with an APC style weak spot too, but theirs is the EMP rocket kind.
    // A real APC's weak spots are its wheel arches.
    public static bool Apc(Actor? a) => a switch
    {
        RVehicleNPC { IsMostWantedApc: true } => true,
        RVehicleCar car => car.bHasAPCStyleWeakSpot && car.CurrentWeakSpot != RVehicleCar.EAPCWeakSpot.EAWS_EMPRocket,
        RVehicle v => v.bHasAPCStyleWeakSpot,
        _ => false,
    };

    // Sentry guns the militia set up, security turrets, and the fixed drone turrets out in the city.
    public static bool Turret(Actor? a) => a is RSentryGunBase or RTurretBase or RVehicleStaticTank;

    // Plain heavy tanks. Commander tanks have several weak points and boss stages, those are story fights
    // and stay out of it.
    public static bool HeavyTank(Actor? a) => a is RVehicleHeavyTank { IsValid: true } t && t.WeakPoints.Count == 0 && t.BossStages.Count == 0;

    // The guns barely scratch these, so they get finished off after enough hits.
    public static bool Tough(Actor? a) => Turret(a) || HeavyTank(a);

    public static bool ToughUp(Actor a) => a is RVehicleHeavyTank t ? TankUp(t) : TurretUp(a);

    public static bool Flying(Actor a) => a is RVehicleFlying or RHelicopterBase;

    public static string Kind(Actor a) =>
        a is RPawnVillain ? "THUG" : Turret(a) ? "TURRET" : HeavyTank(a) ? "HEAVY TANK" : Apc(a) ? "APC" : Flying(a) ? "DRONE" : "VEHICLE";

    private static bool TankUp(RVehicleHeavyTank t) =>
        !TakenOut.Contains(t.Ptr) && t is { IsValid: true, bDeleteMe: false, bHidden: false, DroneHacked: false } && t.Health > 0;

    private static bool TurretUp(Actor a) => !TakenOut.Contains(a.Ptr) && a switch
    {
        RSentryGunBase s => s is { IsValid: true, bDeleteMe: false, bHidden: false, bDisabled: false },
        RTurretBase t => t is { IsValid: true, bDeleteMe: false, bHidden: false, bDisabled: false } && !WatchtowerOut(t),
        RVehicleStaticTank v => v is { IsValid: true, bDeleteMe: false, bHidden: false, DroneHacked: false } && v.Health > 0,
        _ => false,
    };

    // Finished off by the guns or switched off by the game. A turret doesn't drive away, so that's a kill.
    public static bool TurretDown(Actor a) =>
        TakenOut.Contains(a.Ptr) || a is RSentryGunBase { bDisabled: true } || a is RTurretBase { bDisabled: true };

    // A watchtower that was taken out keeps every turret flag as it was. Its gun is hidden, or its state
    // says so. Off alone isn't trusted, a live one may switch off when Batman is far away.
    private static bool WatchtowerOut(RTurretBase t)
    {
        if (t is not RTurret_WatchtowerBase tower)
            return false;
        if (tower.unifiedGunMesh is { IsValid: true } gun && gun.HiddenGame)
            return true;

        string state = tower.GetStateName().ToString();
        return state.Contains("Destroy", StringComparison.OrdinalIgnoreCase) || state.Contains("Dead", StringComparison.OrdinalIgnoreCase)
            || state.Contains("TakenOut", StringComparison.OrdinalIgnoreCase);
    }

    // What a watchtower looks like to a strike, once each, so a destroyed one can be told from a live one.
    public static void WatchtowerLog(Actor a)
    {
        if (a is not RTurret_WatchtowerBase tower || !TowersLogged.Add(tower.Ptr))
            return;

        bool active = Safe.Try("watchtower active", tower.IsActive);
        Log.Detail("Strike: watchtower " + tower.Name + " state " + tower.GetStateName() + ", disabled " + tower.bDisabled + ", active " + active
            + ", gun hidden " + (tower.unifiedGunMesh is { IsValid: true } g ? g.HiddenGame.ToString() : "no gun") + ", taken out " + TakenOut.Contains(tower.Ptr) + ".");
    }

    // A thug still up and hostile, seen or not.
    public static bool Alive(RPawnVillain thug) =>
        thug is { IsValid: true, bDeleteMe: false, bGoodAsDead: false } && thug.Health > 0 && thug.CombatPawnType switch
        {
            // Brutes are MultiStage and count. Robots and the Riddler mech don't.
            RPawnCombat.ECombatPawnType.ECPT_Robot or RPawnCombat.ECombatPawnType.ECPT_RiddlerMechSuit
                or RPawnCombat.ECombatPawnType.ECPT_Player or RPawnCombat.ECombatPawnType.ECPT_Friendly => false,
            _ => true,
        };

    // Alive goes first, it checks the thug still exists before anything reads from it.
    public static bool CanStrike(RPawnVillain thug) => Alive(thug) && !thug.bHidden;

    // The vehicle a thug is driving or standing on.
    public static RVehicleNPC? Carried(RPawnVillain thug)
    {
        if (thug.Base is RVehicleNPC { IsValid: true } on)
            return on;
        return thug.DrivenVehicle is RVehicleNPC { IsValid: true } driving ? driving : null;
    }

    // One riot round puts a thug down. Built the way the Batmobile's own chain gun does it, since calling
    // DamagedBy straight with Batman as the attacker did nothing at all.
    public static void Knock(RPawnVillain thug, Vector3 dir, RPawnPlayer batman, RBatmobileCannon vulcan)
    {
        Vector3 push = dir * (vulcan.ImpactMomentum > 0f ? vulcan.ImpactMomentum : RiotPush);
        TArray<FName> bones = vulcan.RiotSuppressorImpactBones;
        var info = new RPawnCombat.FDamageInfo
        {
            Attacker = vulcan.Owner is { IsValid: true } car ? car : batman,
            BoneName = bones.Count > 0 ? bones[Random.Shared.Next(bones.Count)] : Spine,
            Impulse = push,
            ForceHitReactionHeading = push,
            DamageAmount = thug.Health + 1,
            DamageType = vulcan.DamageType is { IsValid: true } type ? type : RDmgType_BatmobileChainGun.StaticClass(),
            bWillKnockOverOtherPawns = true,
        };

        int health = thug.Health;
        vulcan.DamagePawnWithChainGun(thug, info);
        Log.Detail("Guns: riot round on " + thug.Name + ", health " + health + " to " + thug.Health + ".");
    }

    public static Vector3? TankWeakSpot(Actor a) =>
        a is RVehicleHeavyTank t && t.WeakPoint is { IsValid: true } wp ? wp.Bounds.Origin : null;

    // The heavy cannon's shell damage, the nearest thing to what a hacked tank hits other tanks with.
    public static Class ShellDamage() =>
        Batmobile.Weapon<RBatmobileHeavyCannon>()?.ProjectileArchetype is { IsValid: true } shell && shell.MyDamageType is { IsValid: true } type
            ? type
            : RDmgType_BatmobileChainGun.StaticClass();

    // A turret or tank that has soaked up a full burst is finished off the game's own way.
    public static void Finish(Actor tough, int hits)
    {
        switch (tough)
        {
            case RVehicleHeavyTank tank:
                FinishTank(tank);
                return;
            case RSentryGunBase sentry:
                sentry.Explode(true);
                break;
            // Watchtowers have a shield that stops every shot. The game takes them out with this once
            // Batman has dealt with one, and it counts towards the district's watchtowers too.
            case BmSDK.BmScript.RTurret_Watchtower tower:
                tower.WatchtowerTakenOut(false);
                break;
            case RTurretBase gun:
                gun.SetDamaged();
                break;
        }

        bool still = TurretUp(tough);
        Down(tough);
        Log.Info("Guns: " + tough.Name + " took " + hits + " rounds and was still up, finished it off" + (still ? ", its flags still say it's up." : "."));
    }

    // Shield off and a killing hit on the weak spot. Died only if even that didn't do it.
    private static void FinishTank(RVehicleHeavyTank tank)
    {
        int was = tank.Health;
        Vector3 weak = TankWeakSpot(tank) ?? tank.Location;
        Class type = ShellDamage();
        PlayerController? pc = World.Pc;
        Safe.Run("heavy tank shield", tank.DisableShield);
        Safe.Run("heavy tank finish", () => tank.TakeDamageFromWeapon(tank.Health + 1, pc!, weak, Vector3.UnitZ, Vector3.Zero, type, default, World.Player!, false));
        bool died = false;
        if (tank.IsValid && tank.Health > 0)
        {
            Safe.Run("heavy tank died", () => tank.Died(pc!, type, weak));
            died = true;
        }

        Down(tank);
        Log.Info("Guns: heavy tank " + tank.Name + " still up after the barrage, health " + was + ", finished it off" + (died ? " with Died." : "."));
    }

    private static void Down(Actor a)
    {
        if (TakenOut.Count > 64)
            TakenOut.Clear();
        TakenOut.Add(a.Ptr);
    }
}
