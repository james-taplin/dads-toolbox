# Troubleshooting

## A fix says inactive

Check its status in Dad's Toolbox. Make sure the required mod is installed, enabled and supported by the [compatibility list](README.md#compatibility). A blocked fix does not turn off the others.

After resolving an error, try **Retry optional features**. Restart if you have added, removed or disabled another mod.

## A standalone fix is detected

Disable the old standalone version and restart the game. Turning it off during a running session can leave its changes active. Keep the underlying CCL, HUD, Skin Manager or DoubleTrack mod installed.

## Sound pitch is unavailable

Check for a SteamSizePitch settings-import message or a conflict with SteamSizePitch or ZSounds. See [sound settings migration](README.md#switching-from-standalone-fixes).

## A bundled skin is missing

Enable embedded-skin registration and check that Skin Manager 4.4.1 and CCL 3.1.9 or newer are active. The correction helps skins load after their vehicles are registered; it cannot repair an invalid vehicle ID, missing texture or broken skin definition.

## Changes remain after turning a fix off

Some corrections modify vehicles or signs already loaded into the world. Restart for a clean comparison. The [toggle guide](README.md#toggles-and-restarts) explains what each switch can undo.

## Reporting a problem

Include the Toolbox, game and relevant mod versions, steps to reproduce, and Player.log messages around the failure. Mention which locomotive or map location is affected.
