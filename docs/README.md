# Dad's Toolbox

Development release **0.4.1-dev**, 16 October 2026.

One Unity Mod Manager entry and one DLL contain six feature groups. Skin Manager has two independent corrections, giving seven saved switches. A selected switch expresses your preference; its status says whether the feature is actually running, waiting for a dependency, blocked by a standalone copy, or faulted.

| Feature | Scope |
| --- | --- |
| HUD Guard | Supported HeadsUpDisplay force/power/slip readers return native unavailable values after a null-reference failure. Valid values and other exception types retain their behaviour. |
| CCL Control Fix | Preserves authored bindings and repairs missing CCL control connections, shared-port/rear-cab dispatch, momentary release and the documented F4/notch cases. |
| Loco Sound Pitch | Original steam boiler-barrel-area heuristic plus configured-empty-mass/donor comparison for non-steam CCL locomotives. Native exterior audio only; cab/custom recordings and stock locomotives are protected. |
| Skin Manager Fix | Separate switches preserve scorch on exploded vehicles and defer embedded-skin discovery until CCL registration completes. |
| CCL Smoke Fix | Applies native S282 stack-smoke reader colour/opacity/curve values to eligible CCL systems at subsequent controller initialisations. |
| DoubleTrack signs | Moves generated signs outward to clear nearby horizontal rail centrelines, retaining height/facing. Defaults: 2 m clearance, 8 m movement limit, 5 cm search steps. |

The H6F Consist Fix has been removed entirely: no trainset patch, dependency watcher, setting or panel remains. The actual locomotive mod is managed separately. Older Settings.xml files are accepted and retain every other preference.

## Optional dependencies and controls

HUD Guard checks the actual loaded/active HeadsUpDisplay entry and its native reader API (tested 1.1.2). Controls, smoke and sound wait for loaded/active CCL. The original Control and Sound behaviour remains within its documented scope; a missing locomotive/control/audio configuration is not invented.

Scorch protection requires active Skin Manager UMM 4.4.1, with no CCL prerequisite. Embedded-skin registration additionally requires CCL UMM 3.1.9. CCL 3.1.11 is intentionally not enabled for that correction before runtime validation. Skin Manager's DLL version is 4.2.0.0 despite its package version of 4.4.1. Signs require DoubleTrack UMM 2.1.1 and its checked generated-sign API. These dependencies remain optional for the pack.

The UMM panel has independent switches/statuses and sound/sign options. Re-scan signs rebuilds geometry caches. Retry optional features clears contained faults and retries availability. Normal UMM save dispatch writes the one Toolbox Settings.xml. Missing/disabled dependencies preserve your saved choices and leave the relevant feature inactive.

## Sound settings migration

On first use, older Toolbox settings import the sibling standalone SteamSizePitch/Settings.xml once. All steam/non-steam values, group switches, shared donor and per-livery overrides are retained. Explicit existing Toolbox sound settings take precedence. Only Toolbox's own file is written; the standalone original remains untouched.

Unreadable standalone settings leave sound inactive with a retryable status. Correcting that file retries on the next load. The explicit “Use current Toolbox sound settings instead” button permits a deliberate choice to use the displayed Toolbox values. Import never silently resets a known nondefault steam strength.

## Standalone migration

Keep actual dependency mods installed. Duplicate standalone fixes must be disabled/removed before a cold restart; the Toolbox does not change another mod's files, enable choice or patches.

- DVRollingStockCompatibility supplies both HUD and H6F repairs. Toolbox retains only HUD Guard; the H6F repair was removed in 0.4.1-dev after its developer fixed it upstream.
- DVCCLControlFix, SteamSizePitch, CCLSmokeFix and DoubleTrackSignClearance must not operate beside their integrated replacements. Loaded-but-inactive copies/residual owners can still require restart.
- ZSounds is incompatible with Loco Sound Pitch.

A conflict blocks only the relevant feature. Turning a standalone checkbox off during the same process may leave patches, components or watchers behind, so it does not establish a clean migration.

## What disabling means

HUD guard removes its owned finalizers. Sound restores owned continuous/direct pitches, preserves sources another mod has taken over, and lets active one-shots finish at their starting pitch. Control shutdown releases repaired momentary inputs before removing its owned hooks.

Some changes cannot be undone by unpatching: controls can leave changed prefab/readers/HUD data, smoke leaves values already copied to live readers, and signs stay moved. Restart for exact restoration. Skin registration leaves already-discovered skins under native Skin Manager lifetime. Scorch protection affects future applications and cannot recreate scorch overwritten earlier.

## Shared DLL design and risks

The parent owns one entrypoint, callback set, settings file and update dispatcher. Each patching feature has a distinct Harmony owner and explicit targets; no assembly-wide PatchAll is used. Signs use the parent's update and create no watcher. Sound uses that update instead of a second persistent runner. Unload attempts every module's cleanup, reports failures and creates fresh instances for a new parent lifetime.

This prevents the old standalone loaders from replacing one another's callbacks/settings or installing every annotated patch. Feature-level dependency, conflict and update failures are contained. It does not eliminate a shared failure boundary: an assembly/entrypoint load failure can prevent the entire pack loading, and a base-game API change may affect several features. Shared patch targets/startup timing therefore need combined validation.

Skin registration also depends on the real synchronous activation boundary. Audio quality, real input/focus/physics, streaming and saves require player acceptance beyond isolated fixtures.

## Verification boundary

Read validation.json for exact tested DLL identity, suite counts, source hashes and remaining acceptance. Fixtures exercise actual installed UMM/Harmony and native Unity APIs with controlled data; some game consumers/import bodies/speed inputs are deliberately substituted. Those boundaries are documented in the source evidence.

Still pending: clean game startup and standalone migration; both Leader cabs and other CCL controls; actual sound listening/performance and rendered smoke; actual embedded packs/5AT artwork, 4K transitions, streaming, physical explosion/repair, GUI and save/reload. Keep the existing companion paint-data workaround until actual embedded-pack acceptance passes.

The archive contains one DadsToolbox root with only our DLL, metadata, documentation and validation record. No game, Unity, UMM, Harmony or optional-mod binaries are redistributed. Bogie interpolation/visual smoothing and Position Sync are outside this release.
