# Dad's Toolbox

A collection of fixes and tweaks for Derail Valley, together in one Unity Mod Manager mod. Turn each fix on or off independently, with extra settings for sound pitch and sign clearance.

**[Download 0.4.3-dev](https://github.com/james-taplin/dads-toolbox/releases/tag/0.4.3-dev)** · [Changelog](CHANGELOG.md) · [User guide](docs/README.md)

## What's included

| Fix | What it does |
| --- | --- |
| HUD Guard | Prevents certain HUD errors when reading locomotive force, power and wheel slip. |
| CCL Control Fix | Repairs missing control connections and fixes some shared-control, rear-cab and HUD input issues on CCL locomotives. |
| Loco Sound Pitch | Adjusts native exterior locomotive sounds to better suit the size of CCL steam and diesel locomotives. |
| Skin Manager fixes | Preserves scorch on exploded vehicles and helps skins bundled with CCL vehicles load correctly. Two separate switches. |
| CCL Smoke Fix | Corrects stack-smoke colour and opacity on affected CCL locomotives. |
| DoubleTrack sign clearance | Moves generated signs clear of nearby tracks. |

Only the fixes relevant to your installed mods run. A missing dependency leaves that fix inactive and keeps your chosen settings.

## Installation

1. Close Derail Valley and install the release ZIP through Unity Mod Manager.
2. Disable standalone versions of the included fixes. If moving from SteamSizePitch, [preserve your sound settings first](docs/README.md#switching-from-standalone-fixes).
3. Start the game and open Dad's Toolbox in UMM to choose your fixes.

Keep CCL, Skin Manager, HeadsUpDisplay and DoubleTrack installed if you use their fixes. Loco Sound Pitch cannot run alongside ZSounds.

This is a development release. Restart after changing mods or when you need to undo changes already applied to vehicles or signs. See the [user guide](docs/README.md) for compatibility and toggle behaviour.

## Building from source

On Windows, with a .NET SDK and Derail Valley installed:

~~~powershell
.\Build.ps1 -GamePath 'C:\path\to\Derail Valley'
~~~

[Technical validation](docs/VALIDATION-0.4.3-dev.md) is available for contributors.
