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
around like a helicopter. Flight assist levels the wings, stops stalls and drops into hover when you slow down. Turn it
off and you can stall and fall. Crashes damage the hull and enough of them wreck it. Press **Y** or **E** to bail out.

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

## Controls

These are the defaults. Every mod key can be changed in the settings menu.

| Key | What it does |
|---|---|
| **Y** | Call the Batwing, wave it off, bail out |
| **U** | Remote control |
| **I** | Escort on or off |
| **T** | Strike, hold to mark several targets |
| **O** | Lift, fetch, hook or drop the Batmobile |
| **F4** | Settings menu |

In the cockpit:

| Keyboard | Controller | What it does |
|---|---|---|
| Mouse | Right stick | Aim or steer |
| W A S D | Left stick | Fly in hover, throttle and roll on the wings |
| Space / Ctrl | RT / LT | Up and down in hover |
| Shift / Ctrl | RT / LT | Afterburner and air brake on the wings |
| F | Y | Switch between hover and wings |
| Left mouse | X | Vulcan gun |
| Q | RB | Cannon |
| Hold right mouse | Hold A | Lock on, let go to fire missiles |
| Middle mouse | LB | Look around |
| C | D-pad up | Camera |
| O | D-pad down | Hook or drop the Batmobile |
| E | B | Bail out |
| H | | Hide the key hints |

On a controller hold **LB + RB** to open the settings menu. Remote control, escort and the lift can be started from
there.

## Settings

Press **F4** for the settings menu. It covers flight assist, top speed, crashes, hover drift, camera, the HUD and its
colour, weapons, controls and the Batmobile options. If the HUD text is too small, turn up **HUD SIZE** under DISPLAY.
**DECK SIZE** under SYSTEM does the same for the menu. Your choices are saved to `settings.toml` in the mod folder, so
updating the mod doesn't reset them.

By default Batman raises his gauntlet and the menu shows on its hologram. In the Batmobile it shows over the dashboard
and the car stops. While flying, gliding, fighting, in battle mode or by remote it opens on the screen instead. Set
**OPEN DECK ON** under SYSTEM to SCREEN to always have it on the screen. **GAUNTLET SIZE** makes the hologram bigger
and its text smaller so more fits, and **GAUNTLET TEXT** sizes the text on top of that.

## Install

1. Install [BmSDK-AK](https://github.com/Team-BmSDK/BmSDK-AK) v0.21.0, then unzip the zip from the
   [latest release of XeTrinityz/BmSDK-AK](https://github.com/XeTrinityz/BmSDK-AK/releases/latest) over the game
   folder. Batwing needs that fork's fixes and additions. It works on Steam and Epic copies of the game. On Epic,
   use the manual install for v0.21.0 since its installer won't find the game. Epic support is new, so Steam has
   had more testing.
2. Put this folder in `Batman Arkham Knight/BmGame/Mods/` so you have `BmGame/Mods/Batwing/mod.toml`.
3. Start the game, get out into the city and press **Y**.

The Batwing only works out in the open city, not in interiors or during cutscenes.

## Bug reports

The mod writes `Batwing.log` in its folder. For a bug report turn on **DIAGNOSTICS** under SYSTEM in the settings
menu, make it happen again and attach the log.
