# Troubleshooting

## A fix says inactive

Check its status in Dad's Toolbox. Make sure the required mod is installed, enabled and supported by the [compatibility list](README.md#compatibility). A blocked fix does not turn off the others.

After resolving an error, try **Retry optional features**. Restart if you have added, removed or disabled another mod.

## Sound pitch is unavailable

Check that CCL is active and ZSounds is not loaded. Existing Toolbox sound values are saved in Settings.xml.

## A bundled skin is missing

Enable embedded-skin registration and check that Skin Manager 4.4.1 and CCL 3.1.9 or newer are active. The correction helps skins load after their vehicles are registered; it cannot repair an invalid vehicle ID, missing texture or broken skin definition.

## Changes remain after turning a fix off

Some corrections modify vehicles or signs already loaded into the world. Restart for a clean comparison. The [toggle guide](README.md#toggles-and-restarts) explains what each switch can undo.

## Sign clearance stops after reloading the world

Update to 0.4.3-dev or newer. Sign clearance now waits while the railway loads and resumes automatically. For another reported scanning error, try **Re-scan signs** and include the error from Player.log if it persists.

## Reporting a problem

Include the Toolbox, game and relevant mod versions, steps to reproduce, and Player.log messages around the failure. Mention which locomotive or map location is affected.
