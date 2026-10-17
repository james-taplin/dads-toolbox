# Dad's Toolbox user guide

Each fix has its own switch in Unity Mod Manager. Its status tells you whether it is active, waiting for a dependency or blocked by another mod. Your choices stay saved even when a dependency is missing.

## Compatibility

| Fix | Required mod |
| --- | --- |
| HUD Guard | HeadsUpDisplay 1.1.2 |
| CCL Control Fix, Loco Sound Pitch, CCL Smoke Fix | Custom Car Loader |
| Skin scorch protection | Skin Manager 4.4.1 |
| Embedded-skin registration | Skin Manager 4.4.1 and CCL 3.1.9 or newer |
| Sign clearance | DoubleTrack 2.1.1 |

Embedded-skin registration has been checked with CCL 3.1.11. Newer CCL versions are allowed when the required interfaces match; future updates can still introduce incompatibilities.

## Settings

- **Sound pitch:** adjust the strength, sound groups and locomotive overrides. The fix targets native exterior audio; stock locomotives, cab audio and custom recordings are protected.
- **Sign clearance:** choose the minimum clearance and maximum movement. Defaults are 2 m and 8 m. Use **Re-scan signs** after changing clearance settings.
- **Retry optional features:** retry a fix after resolving a dependency or reported error.

## Switching from standalone fixes

Disable the matching standalone fixes before starting the game: CCLSmokeFix, DVCCLControlFix, SteamSizePitch, DoubleTrackSignClearance and Rolling Stock Compatibility (DVRollingStockCompatibility). Keep their underlying mods and your locomotive packs installed. Restart the game after making this change.

For SteamSizePitch, back up its Settings.xml and leave the disabled mod's folder in place for the first Toolbox launch. Toolbox imports those settings automatically; existing Toolbox sound settings take priority. Check the imported values before removing the old folder.

If the import fails, sound pitch stays inactive. Restore a readable settings file and restart, or use **Use current Toolbox sound settings instead** to keep the values shown in Toolbox.

Loco Sound Pitch is incompatible with ZSounds. Choose which one you want to use.

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

For a clean comparison with a fix disabled, restart the game.

## Help

See [troubleshooting](ISSUE_AND_FIX.md). When reporting a problem, include your game and mod versions, the affected locomotive or location, steps to reproduce it and the relevant Player.log messages.
