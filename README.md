# Batwing

Fly the Batwing around Gotham in **Batman: Arkham Knight** (PC). Call it down, climb into the cockpit and fly
anywhere in the city. It has a proper flight model with stalls and a VTOL hover, and it uses your Batmobile's weapons.
You can also fly it by remote, have it escort you, or have it carry the Batmobile.

Built on [BmSDK-AK](https://github.com/Team-BmSDK/BmSDK-AK) with the fixes in
[XeTrinityz/BmSDK-AK](https://github.com/XeTrinityz/BmSDK-AK). Runs fine alongside
[Oracle: Trinity Edition](https://github.com/XeTrinityz/Oracle).

## What it does

**Flying.** Press **Y** and the Batwing flies in and hovers over you. On the ground Batman grapples up into the
cockpit. Call it while gliding and it picks you up in the air. Fly on the wings like a jet or switch to hover and move
around like a helicopter. With mouse aim the Batwing flies where you look and banks into turns for you. Small
corrections slide the nose across without banking, so lining up a shot is easy. In hover it holds its spot and height
when you let go of the sticks. Crashes damage the hull and enough of them wreck it. Press **Y** or **E** to bail out.

**Flight assist.** FULL levels the wings, stops stalls and drops into hover when you slow right down. STANDARD stops
stalls and keeps turns clean but leaves the rest to you. OFF gives no help at all.

**Weapons.** It uses your Batmobile's weapons and upgrades. The Vulcan gun, the heavy cannon and lock-on missiles.
Aim at a thug and the guns fire riot rounds that knock them out. The Batmobile has to be in the city.

**Remote control.** Press **U** to fly the Batwing from where Batman stands, like the Batmobile's remote. Batman can't
move and the link drops if he gets hit.

**Escort.** Press **I** and the Batwing covers you from the air. It goes after drones and helicopters by itself. Look
at a thug, a car, an APC, a turret or a heavy tank and press **T** to order a strike. Hold **T** and sweep across
targets to mark up to 6. Call it with **Y** to get picked up. You can call it from the Batmobile too and it follows
the car.

**Batmobile lift.** Press **O**.
- In the Batmobile it hooks the car with you inside and flies you to your map waypoint, or the mission marker.
- On foot it fetches the Batmobile and sets it down where you're looking.
- While flying, hover over the Batmobile to hook it and press again to drop it. Dropped from high up it wrecks
  what it lands on.

**Waypoint marker.** Your map waypoint, or the mission marker, shows on the flight HUD with how far away it is.

## Controller

Everything the mod does on a controller is on one button. Hold **View** (the touchpad through Steam Input on a
PlayStation pad) for the command wheel, point a stick at a command and let go to use it. The wheel has the call, remote
control, escort, strike, lift and the settings deck. A quick tap of **View** still opens the map as normal.

**GTA fly style.** On a controller it flies like a GTA jet by default. Set **FLY STYLE** to STANDARD on the CONTROLLER
page for the mouse aim style layout in the table below. The left stick rolls and
pitches, pull back to climb. **RT** and **LT** move the throttle up and down, and **LT** brakes too. **LB** and **RB**
yaw. The right stick only moves the camera, and it swings back behind you a moment after you let go. Click the left
stick for the afterburner. **A** fires and **X** switches between the guns, cannon and missiles. Hold **A** for the
guns or to lock missiles, and tap **X** while locking to add another missile. In hover the left stick moves, the
triggers climb and sink and the bumpers turn. It's only for the controller. Pick up the mouse and the Batwing flies
with your CONTROL MODE again, and back on the pad it's GTA style.

The pad rumbles with the guns, hits, the afterburner and landings. Every flight button can be changed in the settings
deck, along with aim speed, aim response, stick deadzone, inverted pitch and swapped sticks. Prompts can show Xbox or
PlayStation buttons.

## Controls

These are the defaults. Every key and button can be changed in the settings deck. Pick a key that's already used and
the two swap over.

| Key | What it does |
|---|---|
| **Y** | Call the Batwing, wave it off, bail out |
| **U** | Remote control |
| **I** | Escort on or off |
| **T** | Strike, hold to mark several targets |
| **O** | Lift, fetch, hook or drop the Batmobile |
| **F4** | Settings deck |

In the cockpit, with the controller on the STANDARD fly style:

| Keyboard | Controller | What it does |
|---|---|---|
| Mouse | Right stick | Aim or steer |
| W A S D | Left stick | Fly in hover, throttle and roll on the wings |
| Space / Ctrl | RT / LT | Up and down in hover |
| Shift / Ctrl | RT / LT | Afterburner and air brake on the wings |
| Shift | Left stick click | Fast in hover |
| F | Y | Switch between hover and wings |
| Left mouse | X | Vulcan gun |
| Q | RB | Cannon |
| Hold right mouse | Hold A | Lock on, let go to fire missiles |
| Middle mouse | LB | Look around |
| C | D-pad up | Camera |
| O | D-pad down | Hook or drop the Batmobile |
| E | B | Bail out |
| H | | Show or hide the key hints |

## Settings

Press **F4**, or pick SETTINGS on the command wheel. The pages are FLIGHT, BATMOBILE, WEAPONS, KEYBOARD, CONTROLLER,
DISPLAY and SYSTEM. If the HUD text is too small, turn up **HUD SIZE** under DISPLAY. **HUD LAYOUT** set to MINIMAL
keeps the aim, speed, height and warnings and clears the rest. **DECK SIZE** under SYSTEM does the same for the deck.
Your choices are saved to `settings.toml` in the mod folder, so updating the mod doesn't reset them.

By default Batman raises his gauntlet and the deck shows on its hologram. In the Batmobile it shows over the dashboard
and the car stops. While flying, gliding or fighting it opens on the screen instead. Set **OPEN DECK ON** under SYSTEM
to SCREEN to always have it on the screen.

## Install

1. Install [XeTrinityz/BmSDK-AK v0.24.0](https://github.com/XeTrinityz/BmSDK-AK/releases/tag/v0.24.0) or newer.
   Run its installer, or copy the `Binaries` and `BmGame` folders from its zip into your game folder. Older BmSDK
   builds don't run Batwing. It works on Steam and Epic copies of the game. Epic support is new, so Steam has had
   more testing.
2. Put the `Batwing` folder from the zip in `Batman Arkham Knight/BmGame/Mods/` so you have
   `BmGame/Mods/Batwing/Batwing.dll`. When you update, copy it over the old one. Your settings stay. You can delete
   an old `scripts` folder in there. It isn't used any more.
3. Start the game, get out into the city and press **Y**.

The Batwing only works out in the open city, not in interiors or during cutscenes.

## Bug reports

The mod writes `Batwing.log` in its folder. For a bug report turn on **DIAGNOSTICS** under SYSTEM in the settings
deck, make it happen again and attach the log.
