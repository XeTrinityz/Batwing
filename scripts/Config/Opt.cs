namespace Trinity.Batwing;

// Every page and setting of the deck, one field each.
internal static class Opt
{
    public static readonly List<Page> Pages = new();
    public static readonly List<Option> All = new();
    public static readonly Dictionary<Act, Bind> KeyBinds = new();
    public static readonly Dictionary<Act, PadBind> PadBinds = new();

    public static Command Call = null!, RemoteCall = null!, EscortCall = null!, LiftCall = null!, Reset = null!;
    public static Switch FlyIn = null!, Grapple = null!, HoverDrift = null!, HoverHold = null!, Crashes = null!, EscortOnBail = null!;
    public static Pick Assist = null!, BankStyle = null!;
    public static Dial TopSpeed = null!;

    public static Switch CatchEject = null!, DropDamage = null!;

    public static Switch Weapons = null!, GunAssist = null!, CannonHoming = null!;

    public static Pick ControlMode = null!;
    public static Dial MouseAim = null!;

    public static Switch Controller = null!, PadInvert = null!, Rumble = null!;
    public static Dial PadAim = null!, Deadzone = null!, RumbleStrength = null!;
    public static Pick PadResponse = null!, StickLayout = null!, ButtonStyle = null!;
    public static Switch CommandWheel = null!;

    public static Pick Camera = null!, HudColour = null!, HudLayout = null!, SpeedUnit = null!;
    public static Switch Hud = null!, Nav = null!, Hints = null!, HideGameHud = null!;
    public static Dial HudSize = null!;

    public static Pick DeckOn = null!;
    public static Dial GauntletSize = null!, GauntletText = null!, DeckSize = null!;
    public static Switch Effects = null!, Diagnostics = null!;

    public static bool Direct => ControlMode.Index == 1;

    public static bool DeckOnGauntlet => DeckOn.Index == 0;

    public static Banking BankLevel => (Banking)BankStyle.Index;

    public static Assist AssistLevel => Assist.Index switch { 1 => Batwing.Assist.Standard, 2 => Batwing.Assist.Off, _ => Batwing.Assist.Full };

    public static Bind KeyFor(Act act) => KeyBinds[act];

    public static void Build()
    {
        Pages.Clear();
        All.Clear();
        KeyBinds.Clear();
        PadBinds.Clear();

        Page flight = AddPage("flight", "FLIGHT", "Calling the Batwing and how it flies.");
        Call = Do(flight, "call", "CALL BATWING",
            "Calls the Batwing and puts you in the cockpit. Press again while it's on the way to wave it off, or while flying to bail out. Needs open sky above you.",
            Wing.CallPressed);
        Call.Live = () => Wing.CallLabel;
        Call.Note = () => Wing.Status;
        RemoteCall = Do(flight, "remote", "REMOTE CONTROL",
            "Fly the Batwing from where Batman stands, like the Batmobile's remote. Batman can't move and the link drops if he gets hit. It stays in range of him.",
            Wing.RemotePressed);
        RemoteCall.Live = () => Wing.RemoteLabel;
        EscortCall = Do(flight, "escort", "ESCORT",
            "The Batwing covers you from the air and takes on drones and helicopters by itself. Thugs, manned cars, APCs, turrets and heavy tanks only when you order a strike.",
            Wing.EscortPressed);
        EscortCall.Live = () => Wing.EscortLabel;
        Assist = AddPick(flight, "assist", "FLIGHT ASSIST", new[] { "full", "standard", "off" }, new[] { "FULL", "STANDARD", "OFF" }, 0,
            "FULL levels the wings, stops stalls and drops into hover when you slow right down. STANDARD stops stalls and keeps turns clean but leaves the rest to you. OFF gives no help at all and you can stall and fall.");
        BankStyle = AddPick(flight, "banking", "BANKING", new[] { "normal", "gentle", "sharp" }, new[] { "NORMAL", "GENTLE", "SHARP" }, 0,
            "How far mouse aim banks into a turn. GENTLE keeps the wings flatter and turns slower. SHARP rolls right over for the tightest turns. NORMAL is in between.");
        HoverHold = AddSwitch(flight, "hover_hold", "HOVER HOLD", true,
            "Let go of the sticks in hover and the Batwing holds its spot and height. Turn off to let it drift.");
        HoverDrift = AddSwitch(flight, "hover_drift", "HOVER DRIFT", true,
            "The Batwing bobs and sways a little while it hovers. It's only the look of it, steering and aim aren't touched.");
        TopSpeed = AddDial(flight, "top_speed", "TOP SPEED", 90f, 50f, 140f, 5f, v => Fmt.Whole(v) + " M/S",
            "Top speed at full throttle. The afterburner adds about 40% for a few seconds. The city can stutter as it loads in at high speed.");
        FlyIn = AddSwitch(flight, "fly_in", "FLY IN", true,
            "The Batwing flies in and hovers over you before you get in. Turn off to board straight away. Called while gliding it picks you up either way.");
        Grapple = AddSwitch(flight, "grapple_in", "GRAPPLE IN", true,
            "When it flies in and you're on the ground, Batman grapples up into the cockpit.");
        Crashes = AddSwitch(flight, "crashes", "CRASHES", true,
            "Hit something hard enough and the Batwing is wrecked and you're thrown out. Bumps and enemy fire damage the hull. Turn off for no damage.");
        EscortOnBail = AddSwitch(flight, "escort_on_bail", "ESCORT ON BAIL", true,
            "When you bail out the Batwing stays and covers you on escort instead of flying off.");

        Page batmobile = AddPage("batmobile", "BATMOBILE", "The Batwing and the Batmobile together.");
        LiftCall = Do(batmobile, "lift", "LIFT BATMOBILE",
            "In the Batmobile it hooks the car and flies you to your map waypoint or the mission marker. On foot it fetches the Batmobile and sets it down where you're looking. While flying, hover over the Batmobile to hook it and press again to drop it.",
            Wing.LiftPressed);
        LiftCall.Live = () => Wing.LiftLabel;
        LiftCall.Note = () => Wing.LiftNote;
        CatchEject = AddSwitch(batmobile, "catch_eject", "CATCH ON EJECT", false,
            "On escort, eject from the Batmobile and the Batwing swoops under you and picks you up in the air.");
        DropDamage = AddSwitch(batmobile, "drop_damage", "DROP DAMAGE", true,
            "Drop the Batmobile from high up and it wrecks vehicles and knocks down thugs where it lands.");

        Page weapons = AddPage("weapons", "WEAPONS", "The Batmobile's weapons on the Batwing.");
        Weapons = AddSwitch(weapons, "weapons", "WEAPONS", true,
            "Uses your Batmobile's weapons and upgrades. The Vulcan gun, the heavy cannon and lock-on missiles. Aim at a thug and the guns fire riot rounds that knock them out. The Batmobile has to be in the city.");
        GunAssist = AddSwitch(weapons, "gun_assist", "GUN AIM ASSIST", true,
            "The Vulcan gun snaps onto drones and tanks close to your aim. Turn off and rounds go exactly where you aim.");
        CannonHoming = AddSwitch(weapons, "cannon_homing", "CANNON HOMING", true,
            "Cannon shells home in on the vehicle you're aiming at. Turn off and they fly straight.");

        Page keys = AddPage("keyboard", "KEYBOARD", "Mouse and keyboard controls.");
        ControlMode = AddPick(keys, "control_mode", "CONTROL MODE", new[] { "aim", "direct" }, new[] { "MOUSE AIM", "DIRECT" }, 0,
            "MOUSE AIM flies the Batwing towards where you look and banks it into turns for you. DIRECT puts the mouse or right stick on pitch and roll like a joystick, with the roll keys on the rudder.");
        MouseAim = AddDial(keys, "mouse_aim", "MOUSE SPEED", 1f, 0.25f, 3f, 0.05f, Fmt.Times,
            "How fast the mouse aims. It stacks with the game's own mouse sensitivity.");
        AddKey(keys, Act.Call, "call_key", "CALL", Keys.Y, "Call, wave off or bail out.");
        AddKey(keys, Act.Remote, "remote_key", "REMOTE", Keys.U, "Start or end remote control.");
        AddKey(keys, Act.Escort, "escort_key", "ESCORT", Keys.I, "Bring in or dismiss the escort.");
        AddKey(keys, Act.Strike, "strike_key", "STRIKE", Keys.T,
            "On escort, look at a target and press to have the Batwing take it down. Hold and sweep across targets to mark up to 6.");
        AddKey(keys, Act.Lift, "lift_key", "LIFT", Keys.O, "Lift or fetch the Batmobile, or hook and drop it while flying.");
        AddKey(keys, Act.Deck, "deck_key", "DECK", Keys.F4, "Open and close this deck.");
        AddKey(keys, Act.Forward, "key_forward", "FORWARD", Keys.W, "Throttle up on the wings, forward in hover.", mouse: true);
        AddKey(keys, Act.Back, "key_back", "BACK", Keys.S, "Throttle down on the wings, back in hover.", mouse: true);
        AddKey(keys, Act.Left, "key_left", "LEFT", Keys.A, "Roll left on the wings, or rudder in direct control. Slide left in hover.", mouse: true);
        AddKey(keys, Act.Right, "key_right", "RIGHT", Keys.D, "Roll right on the wings, or rudder in direct control. Slide right in hover.", mouse: true);
        AddKey(keys, Act.Up, "key_up", "UP", Keys.Space, "Climb in hover.", mouse: true);
        AddKey(keys, Act.Down, "key_down", "DOWN AND BRAKE", Keys.ControlKey, "Sink in hover, air brake on the wings.", mouse: true);
        AddKey(keys, Act.Boost, "key_boost", "AFTERBURNER", Keys.ShiftKey, "Afterburner on the wings, fast in hover.", mouse: true);
        AddKey(keys, Act.Mode, "key_mode", "HOVER OR WINGS", Keys.F, "Switch between hover and wings.", mouse: true);
        AddKey(keys, Act.Bail, "key_bail", "BAIL OUT", Keys.E, "Bail out, or end remote control.", mouse: true);
        AddKey(keys, Act.Gun, "key_gun", "GUNS", Keys.LButton, "Hold to fire the Vulcan gun.", mouse: true);
        AddKey(keys, Act.Cannon, "key_cannon", "CANNON", Keys.Q, "Fire the heavy cannon.", mouse: true);
        AddKey(keys, Act.Lock, "key_lock", "MISSILES", Keys.RButton,
            "Hold to lock on, let go to fire. Tap the guns while locking to put another missile on what you aim at.", mouse: true);
        AddKey(keys, Act.Look, "key_look", "LOOK AROUND", Keys.MButton, "Hold to look round without steering.", mouse: true);
        AddKey(keys, Act.Camera, "key_camera", "CAMERA", Keys.C, "Change the camera.", mouse: true);
        AddKey(keys, Act.Hints, "key_hints", "KEY HINTS", Keys.H, "Show or hide the flight keys.", mouse: true);

        Page pad = AddPage("controller", "CONTROLLER", "Controller settings and buttons.");
        Controller = AddSwitch(pad, "controller", "CONTROLLER", true, "Controller support. Turn off if the mod picks up a pad you don't want it to.");
        CommandWheel = AddSwitch(pad, "command_wheel", "COMMAND WHEEL", true,
            "Hold VIEW for the command wheel. Point a stick at a command and let go to use it. The wheel has the call, remote, escort, strike, lift and this deck. A quick tap of VIEW still opens the map. Turn off to leave VIEW to the game.");
        PadAim = AddDial(pad, "pad_aim", "AIM SPEED", 1f, 0.25f, 3f, 0.05f, Fmt.Times, "How fast the right stick aims and steers.");
        PadResponse = AddPick(pad, "pad_response", "AIM RESPONSE", new[] { "smooth", "linear", "precise" }, new[] { "SMOOTH", "LINEAR", "PRECISE" }, 0,
            "How the stick picks up speed as you push it. SMOOTH is gentle in the middle and quick at the edge. LINEAR is even all the way. PRECISE is slower in the middle still, for fine aiming.");
        Deadzone = AddDial(pad, "deadzone", "STICK DEADZONE", 0.2f, 0.05f, 0.4f, 0.01f, Fmt.Percent,
            "How far a stick moves before it counts. Turn up if the Batwing creeps with the sticks let go.");
        PadInvert = AddSwitch(pad, "pad_invert", "INVERT PITCH", false, "Pull the stick back to climb, like a flight stick.");
        StickLayout = AddPick(pad, "stick_layout", "STICK LAYOUT", new[] { "standard", "swapped" }, new[] { "STANDARD", "SWAPPED" }, 0,
            "STANDARD steers with the right stick and flies with the left. SWAPPED is the other way round.");
        Rumble = AddSwitch(pad, "rumble", "VIBRATION", true, "The pad rumbles with the guns, hits, the afterburner and landings. The game's own vibration setting has to be on too.");
        RumbleStrength = AddDial(pad, "rumble_strength", "VIBRATION STRENGTH", 1f, 0.25f, 1.5f, 0.05f, Fmt.Times, "How strong the rumble is.");
        ButtonStyle = AddPick(pad, "button_style", "BUTTON PROMPTS", new[] { "xbox", "playstation" }, new[] { "XBOX", "PLAYSTATION" }, 0,
            "Which buttons the hints and prompts show.");
        AddButton(pad, Act.Mode, "pad_mode", "HOVER OR WINGS", PadButton.Y);
        AddButton(pad, Act.Bail, "pad_bail", "BAIL OUT", PadButton.B);
        AddButton(pad, Act.Gun, "pad_gun", "GUNS", PadButton.X);
        AddButton(pad, Act.Cannon, "pad_cannon", "CANNON", PadButton.RB);
        AddButton(pad, Act.Lock, "pad_lock", "MISSILES", PadButton.A);
        AddButton(pad, Act.Look, "pad_look", "LOOK AROUND", PadButton.LB);
        AddButton(pad, Act.Boost, "pad_fast", "FAST IN HOVER", PadButton.LS);
        AddButton(pad, Act.Camera, "pad_camera", "CAMERA", PadButton.Up);
        AddButton(pad, Act.Lift, "pad_lift", "HOOK OR DROP", PadButton.Down);

        Page display = AddPage("display", "DISPLAY", "Camera and HUD.");
        Camera = AddPick(display, "camera", "CAMERA", new[] { "chase", "far", "cockpit" }, new[] { "CHASE", "FAR CHASE", "COCKPIT" }, 0,
            "Where the camera sits while you fly. The camera button switches it in flight.");
        Hud = AddSwitch(display, "hud", "FLIGHT HUD", true, "Jet style HUD with speed, height, heading, pitch ladder, throttle, hull, gun heat and warnings.");
        HudLayout = AddPick(display, "hud_layout", "HUD LAYOUT", new[] { "full", "minimal" }, new[] { "FULL", "MINIMAL" }, 0,
            "FULL has the tapes and pitch ladder. MINIMAL keeps the aim, speed, height and warnings and leaves the view clear.");
        Nav = AddSwitch(display, "nav", "WAYPOINT MARKER", true, "Shows your map waypoint or the mission marker on the HUD with how far it is.");
        HudColour = AddPick(display, "hud_colour", "HUD COLOUR", new[] { "blue", "green", "amber", "white" }, new[] { "BLUE", "GREEN", "AMBER", "WHITE" }, 0,
            "HUD and deck colour.");
        HudSize = AddDial(display, "hud_size", "HUD SIZE", 1f, 0.7f, 1.4f, 0.05f, Fmt.Times, "HUD scale.");
        SpeedUnit = AddPick(display, "speed_unit", "SPEED UNIT", new[] { "mph", "kmh", "ms" }, new[] { "MPH", "KM/H", "M/S" }, 0, "Speed unit on the HUD.");
        Hints = AddSwitch(display, "hints", "KEY HINTS", true, "Shows the flight controls for a few seconds when you get in.");
        HideGameHud = AddSwitch(display, "hide_game_hud", "HIDE GAME HUD", false, "Hides the game's own HUD while flying.");

        Page system = AddPage("system", "SYSTEM", "Deck settings.");
        DeckOn = AddPick(system, "deck_on", "OPEN DECK ON", new[] { "gauntlet", "screen" }, new[] { "GAUNTLET", "SCREEN" }, 0,
            "GAUNTLET has Batman raise his gauntlet and shows the deck on its hologram. In the Batmobile it shows over the dashboard and the car stops. While flying, gliding or fighting it opens on the screen.");
        GauntletSize = AddDial(system, "gauntlet_size", "GAUNTLET SIZE", 1.25f, 1f, 1.8f, 0.05f, Fmt.Times,
            "How big the hologram is while the deck is on it. The text gets smaller as it grows so more fits.");
        GauntletText = AddDial(system, "gauntlet_text", "GAUNTLET TEXT", 1f, 0.6f, 1.4f, 0.05f, Fmt.Times, "Text size on the gauntlet.");
        DeckSize = AddDial(system, "deck_size", "DECK SIZE", 1f, 0.75f, 2f, 0.05f, Fmt.Times, "Deck scale on the screen. It shrinks to fit a small screen.");
        Effects = AddSwitch(system, "effects", "DECK EFFECTS", true, "Scanlines, glow and text animation. Turn off for a plain deck.");
        Diagnostics = AddSwitch(system, "diagnostics", "DIAGNOSTICS", false, "Writes extra detail to Batwing.log. Turn on if you're reporting a bug.");
        Reset = Do(system, "reset", "RESET TO DEFAULTS", "Puts every setting back to its default. Press twice to confirm.", ResetAll);
        Reset.Confirm = true;

        Diagnostics.Changed = () => Log.Diagnostics = Diagnostics.On;
        RumbleStrength.Changed = () => Batwing.Rumble.Play(Buzz.Hit);
    }

    public static void ApplyAll()
    {
        foreach (Option o in All)
            Safe.Run(o.Id + " apply", () => o.Changed?.Invoke());
    }

    private static void ResetAll()
    {
        foreach (Option o in All)
        {
            if (o is Command || o.IsDefault)
                continue;
            o.Reset();
            Safe.Run(o.Id + " reset", () => o.Changed?.Invoke());
        }

        Settings.MarkDirty();
        Toasts.Show("DEFAULTS RESTORED", true);
    }

    private static Page AddPage(string key, string title, string blurb)
    {
        var page = new Page(key, title, blurb);
        Pages.Add(page);
        return page;
    }

    private static T Add<T>(Page page, T option) where T : Option
    {
        option.Page = page;
        page.Options.Add(option);
        All.Add(option);
        return option;
    }

    private static Switch AddSwitch(Page page, string id, string label, bool on, string help) =>
        Add(page, new Switch { Id = id, Label = label, Help = help, On = on, Default = on });

    private static Dial AddDial(Page page, string id, string label, float value, float min, float max, float stride, Func<float, string> format, string help) =>
        Add(page, new Dial { Id = id, Label = label, Help = help, Number = value, Default = value, Min = min, Max = max, Stride = stride, Format = format });

    private static Pick AddPick(Page page, string id, string label, string[] keys, string[] labels, int initial, string help) =>
        Add(page, new Pick { Id = id, Label = label, Help = help, Keys = keys, Labels = labels, Index = initial, Default = initial });

    private static void AddKey(Page page, Act act, string id, string label, Keys key, string help, bool mouse = false) =>
        KeyBinds[act] = Add(page, new Bind { Id = id, Label = label, Help = help + " Press ENTER, then the new key.", Key = key, Default = key, For = act, Mouse = mouse });

    private static void AddButton(Page page, Act act, string id, string label, PadButton button) =>
        PadBinds[act] = Add(page, new PadBind { Id = id, Label = label, Help = "Press A, then the new button. A button that's already used swaps with this one.", Button = button, Default = button, For = act });

    private static Command Do(Page page, string id, string label, string help, Action run) =>
        Add(page, new Command { Id = id, Label = label, Help = help, Run = run });
}
