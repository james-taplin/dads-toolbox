# Dad's Toolbox

**0.4.0-dev** — one Unity Mod Manager mod for Derail Valley, with independent options and dependency/conflict status for each fix.

| Feature | Included scope |
| --- | --- |
| HUD Guard | Protects supported HeadsUpDisplay readers from null-reference failures while preserving valid results. |
| Consist Fix | Currently repairs the H6F trainset livery reference. Other locomotives are not included. |
| CCL Control Fix | Missing control connections, shared-port/rear-cab arbitration, momentary release and the documented HUD/notch cases. |
| Loco Sound Pitch | Size-based pitch adjustment for native exterior audio on CCL steam and non-steam locomotives. Stock, cab and custom recordings are protected. |
| Skin Manager fixes | Separate scorch protection and embedded-skin registration switches. |
| CCL Smoke Fix | Corrects matching CCL stack-smoke readers from the native S282 template on load/spawn. |
| DoubleTrack signs | Moves generated signs clear of nearby tracks while retaining height and facing. |

Seven feature groups expose eight saved switches. Dependencies remain optional. A missing/inactive dependency or conflicting standalone leaves only the corresponding feature inactive, preserving the selected preference.

## Download and installation

Download **DadsToolbox-0.4.0-dev.zip** from the [0.4.0-dev release](https://github.com/james-taplin/dads-toolbox/releases/tag/0.4.0-dev) and install the ZIP through Unity Mod Manager with Derail Valley closed.

When migrating, disable/remove the superseded standalone fixes before a cold restart: CCLSmokeFix, DVCCLControlFix, SteamSizePitch, DoubleTrackSignClearance and DVRollingStockCompatibility (the latter supplies both HUD and H6F repairs). Keep the actual CCL, HUD, Skin Manager, H6F and DoubleTrack dependency mods. Do not run duplicate fixes together. ZSounds is incompatible with Loco Sound Pitch.

Preserve SteamSizePitch/Settings.xml before removing that folder. Automatic one-time import reads that original sibling file; if it is removed first, migrate its values into Toolbox settings before starting the game. Existing explicit Toolbox sound values take precedence. Unreadable imports leave sound inactive rather than silently resetting a nondefault strength.

See [feature behaviour, dependencies and toggle limits](docs/README.md). This is a **development release**: controlled automated fixtures have passed; full live-world acceptance remains pending. Some mutations need a restart for exact restoration.

## Build and verification

Build on Windows with a .NET SDK and an installed Derail Valley/UMM installation:

~~~powershell
.\Build.ps1 -GamePath 'C:\path\to\Derail Valley'
~~~

Test runners are under tests/. Native tests use an isolated Unity 2019.4.40f1 Editor and installed optional-mod assemblies. They do not start Derail Valley or load user saves. Paths can be supplied to the runners. Run the three tests/Run-Native* fixtures sequentially because they share one Editor test project.

All ten suites passed against the released DLL: **16,600 checks, zero failures**, including 14,808 parameterised sound-rule checks. See [validation](docs/VALIDATION-0.4.0-dev.md) and [machine-readable results](evidence/validation.json). This does not mean 16,600 gameplay scenarios.

After rebuilding, all exact-DLL test receipts must be regenerated before WriteValidation.ps1 and Package.ps1 will accept the candidate. Runtime dependencies, generated Editor caches, raw local logs, private handovers and user settings are not included in this repository or release.

No game, Unity, Harmony, UMM, CCL, HUD, Skin Manager or DoubleTrack binaries are redistributed.
