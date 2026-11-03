# Changelog

## [1.0.0-rc.4](https://github.com/james-taplin/dads-toolbox/releases/tag/1.0.0-rc.4) — 3 November 2026

- First public RC with eight independently switchable feature groups.
- Added Chuff Fix and Bogie Visual Smoothing.
- Improved sound/control performance and sign clearance.

## 1.0.0-rc.3 — 29 October 2026

- Removed development-only standalone detection and sound-settings import.
- Kept required-mod checks, ZSounds compatibility and existing Toolbox preferences.

## 1.0.0-rc.2 — 29 October 2026

- Added Bogie Visual Smoothing, based on the working standalone 0.4.2, with its own switch and manual refresh/report controls.
- Added duplicate protection so standalone and Toolbox smoothing cannot own the same visuals.

## 1.0.0-rc.1 — 29 October 2026

- Sign clearance now distinguishes nearby tracks from separate levels at bridges and underpasses.
- Reduced repeated work during locomotive sound discovery and control input handling.
- Cleaned up sound diagnostics when vehicles despawn.

## [0.4.5-dev](https://github.com/james-taplin/dads-toolbox/releases/tag/0.4.5-dev) — 28 October 2026

- Removed repeated compatibility scans from Sound Pitch and CCL Control Fix during normal gameplay, addressing recurring Update hitches.
- Kept conflict checks on activation, dependency changes and explicit retry.

## [0.4.4-dev](https://github.com/james-taplin/dads-toolbox/releases/tag/0.4.4-dev) — 26 October 2026

- Fixed steam pitch scaling: smaller boilers pitch sounds up and larger boilers pitch them down, relative to the sound donor.
- Added a steam upper pitch cap. Steam and diesel settings now show lower/upper caps and effect strength as percentages.
- Added a one-time log message when a layered sound actually receives a pitch adjustment.

## [0.4.3-dev](https://github.com/james-taplin/dads-toolbox/releases/tag/0.4.3-dev) — 23 October 2026

- Fixed sign clearance stopping during world reloads. It now waits for the railway to load and resumes automatically.
- Added fuller diagnostics for unexpected sign-scanning errors.

## [0.4.2-dev](https://github.com/james-taplin/dads-toolbox/releases/tag/0.4.2-dev) — 17 October 2026

- Enabled embedded-skin registration for CCL 3.1.9 and newer, with compatibility checks.
- Checked the correction against CCL 3.1.11, where it is still needed.

## [0.4.1-dev](https://github.com/james-taplin/dads-toolbox/releases/tag/0.4.1-dev) — 16 October 2026

- Removed the H6F Consist Fix following the original developer's repair.
- Kept existing Toolbox settings compatible.

## [0.4.0-dev](https://github.com/james-taplin/dads-toolbox/releases/tag/0.4.0-dev) — 15 October 2026

- First combined development release, with independent switches for HUD Guard, CCL controls, sound pitch, two Skin Manager fixes, CCL smoke and DoubleTrack signs.
- Included the H6F Consist Fix, subsequently removed in 0.4.1-dev.
- Added sound settings import from SteamSizePitch.
