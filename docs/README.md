# Dad's Toolbox user guide

Version 1.0.0-rc.4 is a release candidate for testing before the stable release.

Each fix has its own switch in Unity Mod Manager. Its status tells you whether it is active, waiting for a dependency or blocked by another mod. Your choices stay saved even when a dependency is missing.

## Compatibility

| Fix | Required mod |
| --- | --- |
| HUD Guard | HeadsUpDisplay 1.1.2 |
| CCL Control Fix, Loco Sound Pitch, CCL Smoke Fix | Custom Car Loader |
| Skin scorch protection | Skin Manager 4.4.1 |
| Embedded-skin registration | Skin Manager 4.4.1 and CCL 3.1.9 or newer |
| Sign clearance | DoubleTrack 2.1.1 |
| Bogie Visual Smoothing | No additional mod; supports vanilla and CCL stock |
| Chuff Fix | No additional mod; supports vanilla and CCL steam locomotives |

Embedded-skin registration has been checked with CCL 3.1.11. Newer CCL versions are allowed when the required interfaces match; future updates can still introduce incompatibilities.

## Settings

- **Sound pitch:** steam scales by boiler barrel area; diesel and other non-steam locomotives scale by empty mass. Smaller/lighter than the sound donor gives higher pitch; larger/heavier gives lower pitch. Each has separate lower and upper pitch caps and an effect-strength percentage. A 100% pitch cap means original pitch; 0% strength disables the effect, 100% is normal strength, and higher values make it stronger. Existing settings keep their meaning. The fix targets native exterior audio; stock locomotives, cab audio and custom recordings are protected.
- **Sign clearance:** choose the minimum horizontal clearance and maximum movement. Defaults are 2 m and 8 m. Tracks on clearly separate levels are ignored. Use **Re-scan signs** after changing clearance settings.
- **Bogie Visual Smoothing:** uses visual copies while native physics, steering and animation remain authoritative. **Refresh mapped bogie visuals** remaps loaded vehicles; **Write bogie coverage report** writes a manual snapshot. Maintenance timing is shown in its status and covers maintenance work, not total feature cost.
- **Chuff Fix:** uses each steam engine's native exhaust count and frequency, repeats existing recordings when a chuff bank is too short, and adapts recognised native running loops. Custom recordings and ash-loop behaviour are preserved. While Chuff Fix owns a rhythmic loop, Sound Pitch leaves its playback rate at native cadence; your Sound Pitch group preferences stay saved.
- **Retry optional features:** retry a fix after resolving a dependency or reported error.

## Updating and compatibility

Loco Sound Pitch is incompatible with ZSounds. Choose which one you want to use.

Remove standalone MultiCylinderChuffFix before using the integrated Chuff Fix, then restart. A loaded standalone or its remaining patches keep the integrated feature inactive.

When updating Toolbox, keep its Settings.xml to preserve your preferences.

## Toggles and restarts

| Fix | Effect of turning it off |
| --- | --- |
| HUD Guard | Protection stops immediately. |
| Loco Sound Pitch | Tracked continuous sounds return to their original pitch. Sounds already playing as one-shots finish at their starting pitch. |
| CCL Control Fix | Repairs stop and held momentary controls are released. Restart to fully restore changed vehicle controls and HUD data. |
| CCL Smoke Fix | Future corrections stop. Restart to restore smoke already changed. |
| Sign clearance | Future moves stop. Restart to restore signs already moved. |
| Embedded-skin registration | Future recovery stops. Skins already loaded remain available for the session. |
| Skin scorch protection | Future skin reloads are unprotected. Enabling it cannot restore scorch already overwritten. |
| Bogie Visual Smoothing | Stops sampling and camera callbacks, cancels mapping, removes visual copies and restores surviving native renderers. Re-enabling creates a fresh runtime. |
| Chuff Fix | Restores its tracked banks, frequency bindings and running-loop changes through the native reader lifecycle. Re-enabling also handles loaded readers. If a reader cannot be restored safely, its status requests a restart. |

For a clean comparison with a fix disabled, restart the game.

## Help

See [troubleshooting](ISSUE_AND_FIX.md). When reporting a problem, include your game and mod versions, the affected locomotive or location, steps to reproduce it and the relevant Player.log messages.
