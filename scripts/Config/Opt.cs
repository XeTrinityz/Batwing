namespace Trinity.Batwing;

internal static class Opt
{
    public static readonly List<Page> Pages = new();
    public static readonly List<Option> All = new();

    public static Act Call = null!;
    public static Act RemoteCall = null!;
    public static Act EscortCall = null!;
    public static Switch FlyIn = null!;
    public static Switch Grapple = null!;
    public static Switch Assist = null!;
    public static Dial TopSpeed = null!;
    public static Switch Crashes = null!;
    public static Switch EscortOnBail = null!;

    public static Pick Controls = null!;
    public static Dial AimSpeed = null!;
    public static Bind CallKey = null!;
    public static Bind RemoteKey = null!;
    public static Bind EscortKey = null!;
    public static Bind StrikeKey = null!;
    public static Bind DeckKey = null!;
    public static Switch Controller = null!;
    public static Switch PadInvert = null!;
    public static Pick PadButton = null!;

    public static Switch Weapons = null!;
    public static Switch GunAssist = null!;
    public static Switch CannonHoming = null!;

    public static Pick Camera = null!;
    public static Switch Hud = null!;
    public static Pick HudColour = null!;
    public static Dial HudSize = null!;
    public static Pick SpeedUnit = null!;
    public static Switch Hints = null!;
    public static Switch HideGameHud = null!;

    public static Dial DeckSize = null!;
    public static Switch Effects = null!;
    public static Switch Diagnostics = null!;
    public static Act Reset = null!;

    public static bool Direct => Controls.Index == 1;

    public static void Build()
    {
        Pages.Clear();
        All.Clear();

        Page flight = Add("flight", "FLIGHT", "Flight settings.");
        Call = Act(flight, "call", "CALL BATWING",
            "Calls the Batwing to you and puts you in the cockpit. Press again while it's on the way to cancel, or while flying to bail out. Same as the call key. Needs open sky above you.",
            () => Wing.I.Toggle());
        Call.Live = () => Wing.I.ActionLabel;
        Call.Note = () => Wing.I.StateLabel;
        RemoteCall = Act(flight, "remote", "REMOTE CONTROL",
            "Fly the Batwing from where Batman stands, like the Batmobile's remote. Batman can't move and if he gets hit the link drops. Same range as the Batmobile's remote, or about 300 m with no Batmobile in the city. Same as the remote key.",
            () => Wing.I.RemoteToggle());
        RemoteCall.Live = () => Wing.I.RemoteLabel;
        EscortCall = Act(flight, "escort", "ESCORT",
            "The Batwing circles overhead and takes on drones and helicopters near Batman by itself. Thugs, cars with thugs in them and APCs only when you order a strike. Call it to get picked up. Same as the escort key.",
            () => Wing.I.EscortToggle());
        EscortCall.Live = () => Wing.I.EscortLabel;
        FlyIn = Add(flight, "fly_in", "FLY IN", true,
            "Batwing flies in and hovers above you before you get in. Turn off to board instantly. If you call it while gliding it picks you up either way.");
        Grapple = Add(flight, "grapple_in", "GRAPPLE IN", true,
            "When it flies in and you're on the ground, Batman grapples up into the cockpit instead of appearing in it.");
        Assist = Add(flight, "assist", "FLIGHT ASSIST", true,
            "Auto levels the wings, stops you stalling and switches to VTOL hover at low speed. Turn off for no help at all, you can stall and fall.");
        TopSpeed = Add(flight, "top_speed", "TOP SPEED", 90f, 50f, 140f, 5f, v => Fmt.Whole(v) + " M/S",
            "Max speed at full throttle. Afterburner adds about 40% for a few seconds. The city may stutter a little as it loads in at high speed.");
        Crashes = Add(flight, "crashes", "CRASHES", true,
            "Hit something hard enough and the Batwing gets destroyed and you're thrown out. Small bumps and enemy fire damage the hull. Turn off for no damage.");
        EscortOnBail = Add(flight, "escort_on_bail", "ESCORT ON BAIL", true,
            "When you bail out the Batwing stays and covers you on escort instead of flying off. Turn off to have it leave.");

        Page controls = Add("controls", "CONTROLS", "Keybinds and controller.");
        Controls = Add(controls, "controls", "CONTROL MODE", new[] { "aim", "direct" }, new[] { "MOUSE AIM", "DIRECT" },
            "MOUSE AIM flies the Batwing towards where you look, A and D roll. DIRECT uses the mouse or right stick for pitch and roll, A and D for rudder.");
        AimSpeed = Add(controls, "aim_speed", "AIM SPEED", 1f, 0.25f, 3f, 0.05f, Fmt.Times,
            "Aim sensitivity for the mouse and right stick. Mouse stacks with the game's own sensitivity setting.");
        CallKey = Add(controls, "call_key", "CALL KEY", Keys.Y,
            "Key to call, cancel or bail out. Press ENTER, then the new key.");
        RemoteKey = Add(controls, "remote_key", "REMOTE KEY", Keys.U,
            "Key to start or end remote control. Press ENTER, then the new key.");
        EscortKey = Add(controls, "escort_key", "ESCORT KEY", Keys.I,
            "Key to bring in or dismiss the escort. Press ENTER, then the new key.");
        StrikeKey = Add(controls, "strike_key", "STRIKE KEY", Keys.T,
            "On escort, look at a thug or vehicle and press this to have the Batwing take it down. Hold it and sweep across targets to mark up to 6, and it takes them in order. Press ENTER, then the new key.");
        DeckKey = Add(controls, "deck_key", "DECK KEY", Keys.F4,
            "Key to open and close the deck. Press ENTER, then the new key.");
        Controller = Add(controls, "controller", "CONTROLLER", true,
            "Xbox controller support. RS steers, LS throttle and roll, RT afterburner, LT brake, Y hover, B bail out or end remote, LB free look, D-pad up camera, X guns, RB cannon, A missiles. Start remote control and escort from the deck.");
        PadInvert = Add(controls, "pad_invert", "INVERT PAD PITCH", false,
            "Pull the right stick back to climb, like a flight stick. Off means push up to climb.");
        PadButton = Add(controls, "pad_button", "PAD DECK BUTTON", new[] { "lb+rb", "ls+rs", "off" }, new[] { "HOLD LB + RB", "HOLD LS + RS", "OFF" },
            "Hold to open the deck on controller. A select, B back, LB and RB change page, X resets a setting.");

        Page weapons = Add("weapons", "WEAPONS", "Batmobile weapons on the Batwing.");
        Weapons = Add(weapons, "weapons", "WEAPONS", true,
            "Uses your Batmobile's weapons and upgrades. Hold LMB (X) for the Vulcan gun, Q (RB) for the cannon, hold RMB (A) to lock onto drones and tanks and release to fire missiles. Click LMB (X) while locking to put another missile on the target you aim at. Aim at a thug and the guns fire riot rounds that knock them out. Batmobile has to be in the city.");
        GunAssist = Add(weapons, "gun_assist", "GUN AIM ASSIST", true,
            "Vulcan gun snaps onto drones and tanks close to your aim. Turn off and shots go exactly where you aim.");
        CannonHoming = Add(weapons, "cannon_homing", "CANNON HOMING", true,
            "Cannon shells home in on the vehicle you're aiming at. Turn off and they fly straight.");

        Page display = Add("display", "DISPLAY", "Camera and HUD.");
        Camera = Add(display, "camera", "CAMERA", new[] { "chase", "far", "cockpit" }, new[] { "CHASE", "FAR CHASE", "COCKPIT" },
            "Camera position. Press C while flying to switch (D-pad up on controller).");
        Hud = Add(display, "hud", "FLIGHT HUD", true,
            "Jet style HUD with speed, altitude, heading, pitch ladder, throttle, hull, gun heat and warnings.");
        HudColour = Add(display, "hud_colour", "HUD COLOUR", new[] { "blue", "green", "amber", "white" }, new[] { "BLUE", "GREEN", "AMBER", "WHITE" },
            "HUD and deck colour.");
        HudSize = Add(display, "hud_size", "HUD SIZE", 1f, 0.7f, 1.4f, 0.05f, Fmt.Times, "HUD scale.");
        SpeedUnit = Add(display, "speed_unit", "SPEED UNIT", new[] { "mph", "kmh", "ms" }, new[] { "MPH", "KM/H", "M/S" }, "Speed unit on the HUD.");
        Hints = Add(display, "hints", "KEY HINTS", true, "Shows the controls for a few seconds when you get in. H to hide.");
        HideGameHud = Add(display, "hide_game_hud", "HIDE GAME HUD", false, "Hides the normal game HUD while flying.");

        Page system = Add("system", "SYSTEM", "Deck settings.");
        DeckSize = Add(system, "deck_size", "DECK SIZE", 1f, 0.75f, 1.5f, 0.05f, Fmt.Times, "Deck scale.");
        Effects = Add(system, "effects", "DECK EFFECTS", true, "Scanlines, glow and text animation. Turn off for a plain deck.");
        Diagnostics = Add(system, "diagnostics", "DIAGNOSTICS", false, "Logs extra info to Batwing.log. Turn on if you're reporting a bug.");
        Reset = Act(system, "reset", "RESET TO DEFAULTS", "Resets every setting to default. Press twice to confirm.", ResetAll);
        Reset.Confirm = true;
        Reset.Note = () => "";

        Diagnostics.Changed = () => Log.Diagnostics = Diagnostics.On;
        Controller.Changed = () => Pad.Enabled = Controller.On;
    }

    private static void ResetAll()
    {
        foreach (Option o in All)
        {
            if (o is Act)
                continue;
            bool was = o.IsDefault;
            o.Reset();
            if (!was)
                Safe.Run(o.Id + " reset", () => o.Changed?.Invoke());
        }

        Settings.MarkDirty();
        Readout.Toast("DECK", "DEFAULTS RESTORED", true);
    }

    public static void ApplyAll()
    {
        foreach (Option o in All)
            Safe.Run(o.Id + " apply", () => o.Changed?.Invoke());
    }

    private static Page Add(string key, string title, string blurb)
    {
        var page = new Page(key, title, blurb);
        Pages.Add(page);
        return page;
    }

    private static T Register<T>(Page page, T option) where T : Option
    {
        option.Page = page;
        page.Options.Add(option);
        All.Add(option);
        return option;
    }

    private static Switch Add(Page page, string id, string label, bool on, string help) =>
        Register(page, new Switch { Id = id, Label = label, Help = help, On = on, Default = on });

    private static Dial Add(Page page, string id, string label, float value, float min, float max, float stride, Func<float, string> format, string help) =>
        Register(page, new Dial { Id = id, Label = label, Help = help, Number = value, Default = value, Min = min, Max = max, Stride = stride, Format = format });

    private static Pick Add(Page page, string id, string label, string[] keys, string[] labels, string help) =>
        Register(page, new Pick { Id = id, Label = label, Help = help, Keys = keys, Labels = labels });

    private static Bind Add(Page page, string id, string label, Keys key, string help) =>
        Register(page, new Bind { Id = id, Label = label, Help = help, Key = key, Default = key });

    private static Act Act(Page page, string id, string label, string help, Action run) =>
        Register(page, new Act { Id = id, Label = label, Help = help, Run = run });
}
