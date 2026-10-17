# Combined Toolbox 0.4.2

The current candidate includes HUD Guard, CCL Control Fix, Loco Sound Pitch, both Skin Manager corrections, CCL Smoke Fix and DoubleTrack signs. The following skin/sign investigation sections retain their original evidence and historical counts; current exact-binary aggregate results are in validation.json.

HUD Guard preserves native valid force/power/slip, turning selected reader NRE into native unavailable results; other exceptions still propagate. It remains a broad NRE boundary and can conceal a genuine null bug inside a supported reader.

Consist Fix (H6F) was removed completely in 0.4.1-dev following the upstream repair. Older settings ignore the obsolete option and drop it on save. HUD Guard remains independent.

CCL Control Fix uses the reconciled 1.4.0 reference. It repairs missing per-representation port/action connections, arbitrates one dispatch per vehicle/port/action/frame, preserves authored bindings/direction/repeat policies, and releases momentary input safely across parent shutdown. Unknown authored direction/repeat leaves backup inactive. Existing prefab/reader/HUD mutations need restart for exact restoration.

Loco Sound Pitch uses boiler barrel area for steam and configured empty mass versus the identified native sound donor for non-steam CCL locomotives. This is a size heuristic. Native clip/signature identification, ambiguous-donor neutrality and overrides are preserved; no inference from names, live cargo mass or arbitrary power definitions is introduced. Shared ownership restoration preserves foreign source changes. Passing synthetic/native hook tests does not establish convincing sound or fleet performance.

CCL Smoke Fix copies established native S282 stack-smoke reader settings to matching CCL readers on future initialisations. An early missing template is retried. Disabling removes future correction, without restoring prior live reader values.

All features now share one parent lifecycle/settings file; their algorithms and dependency/conflict scopes remain separate. Each patching feature has its own owner and explicit classes/targets. A shared DLL introduces an assembly/entrypoint failure boundary across the pack; module-level fault containment does not guarantee survival of a DLL load failure or a native process crash. Restart notices and strict validated API/version gates remain visible. No installation or gameplay acceptance is claimed.

---
# Skin Manager damage/reload correction

Skin Manager 4.4.1 reapplies an ordinary paint theme when an exploded vehicle's interior or external interactables reload. This can replace the game's scorch materials with intact materials. The problem was reproduced during Wardale 5AT 0.9.6 validation: 209 engine and 29 tender painted renderers were overwritten. It affects a shared Skin Manager path, so the correction belongs in Dad's Toolbox.

## Cause and correction

The installed game disables root painters, applies its explosion model, sets `TrainCar.isExploded`, then reloads exploded prefabs. Skin Manager's `TrainCarPatches.AfterInitializePaint` calls its static `TrainCarPaintPatches.ReapplyThemes(TrainCar)` method. That method applies all CCL material substitutions and skin textures without consulting `isExploded`.

The Toolbox adds one owned Harmony prefix to that Skin Manager method. It defers theme reapplication while the vehicle is exploded. Healthy vehicles continue through Skin Manager's original code. Null or destroyed vehicle references are ignored. The fix has no locomotive IDs, CCL dependency, replacement material tables or separate damage state.

Native repair clears `isExploded` and reloads repaired parts before enabling the root painters. The prefix therefore checks only the exploded flag. A blanket disabled-painter check would incorrectly suppress those repair callbacks. No repair hook is needed.

The enabled setting is independent of effective availability. The module requires an active UMM `SkinManagerMod` entry at version 4.4.1 and the exact static `void ReapplyThemes(TrainCar)` API. Its assembly version is 4.2.0.0 despite UMM's 4.4.1 metadata. Unsupported versions stay inactive. The package contains no Skin Manager binary.

## Scope and limits

The toggle applies to future calls and removes only this module's Harmony owner. Enabling it does not reconstruct scorch that an earlier bad callback already overwrote. Native repair restores normal appearance; test a fresh explosion with the fix enabled to check scorch preservation.

This is separate from the 5AT data corrections, glass collision repair and separate built-in paint data mod. Those remain in the locomotive project. Zero condition is not an exploded flag, and an engine explosion does not automatically explode its tender.

See `validation.json` for executed checks and `README.md` for installation and the development release boundary. Full game boot, actual physical explosion, streamed vehicle reload and save/reload still require live acceptance.

The current combined build passes 70 managed lifecycle/settings/dependency checks and the 37 native damage checks, alongside the sign and registration suites described below. The damage fixture demonstrates the unpatched overwrite, patched scorch retention, repair with a changed paint selection while painters are disabled, independent vehicle state, repeated cycles and patch ownership/removal. It uses the installed native material handler and Skin Manager callback on generated objects; explosion flags and reload calls are constructed by the fixture. It does not run a physical blast or the full streaming loader. The original 5AT integration fixture has not been rerun with the Toolbox installed.

## Source evidence

The supplied investigation is retained as `evidence/wardale-investigation-20261015.txt` in the development source. Local reference evidence is in the 5AT project's `reference/cp65_repaint_damage/destruction_skin096_confirmed` and `unity_src/Editor/Proto5ATDestructionSkinChecks.cs`. The previous fixture reports 42 checks with 18 appearance failures; its passing setup checks are not a successful integration result.

Inspected Skin Manager DLL SHA256: `9B98DC0364AB1CD85D1AA3844B9EDAE03C8E95145440C4C2E41CA73858D87620`.

# DoubleTrack sign clearance integration

DoubleTrack 2.1.1 generates speed, grade and junction signs at runtime. Its native placer moves signs along local negative X; the existing standalone correction searches farther in that same direction when a sign is too close to any railway centreline. Dad's Toolbox preserves that calculation and uses the installed native kinked point sets for its horizontal segment index.

The canonical input was `Desktop/Derail Valley Mods/Mods (code)/DoubleTrackSignClearance/source/DoubleTrackSignClearance.cs`, SHA256 `8EE451C7E584CC0BD7F3469CB2E844F26EE7EB07E1441E1B4E0704E65394D49D`. The integration does not compile its standalone loader, settings writer or watcher. `DadsToolbox.DoubleTrackSigns` is dispatched by the existing parent UMM update, dependency-toggle and lifecycle callbacks. Its option and numeric values live in the one shared Toolbox settings file. Existing skin-only settings retain their prior preference and receive sign defaults.

A loaded or active UMM `DoubleTrackSignClearance` entry blocks this optional module. Loaded-but-inactive is deliberately blocked because the original 1.0.0 helper did not stop its watcher. This avoids duplicate correction without taking over or disabling another mod. Migration requires disabling/removing that helper and a cold restart. DoubleTrack itself stays installed. Conflict/missing-dependency/fault state does not overwrite the selected preference or affect the Skin Manager patch.

The module accepts DoubleTrack UMM version 2.1.1 plus a static generated-sign list compatible with `IList<GameObject>`. It resolves the API from the active entry's actual assembly. Runtime caches clear on stop, regenerated lists and track-array replacement; explicit rescan also rebuilds mutated point sets. A scan failure is contained to this module and latched until rescan, avoiding repeated error logging. Disabling stops future corrections; prior movements require restart for exact restoration.

The sign suite's 838 checks pass against the current 0.3.0 combined DLL as well as the earlier 0.2.0 integration. It uses actual installed UMM and DoubleTrack APIs, native railway point sets and Unity Transforms with generated data. It verifies the parent update path, horizontal clearance, opposite-facing signs, height/facing preservation, unlisted-object exclusion, movement limits, origin offsets, regenerated lists/world caches, missing registry, throttling, dependency and standalone transitions, fault/retry, unload/recreation and independent Skin Manager operation. Eight hundred spatial-index queries are compared with an independent complete segment scan. This is isolated Editor validation; full game boot, real generated maps, GUI and save/reload remain game acceptance checks.

# Skin Manager embedded CCL skin registration

The second supplied Wardale investigation identifies a different failure. CCL loads after Skin Manager and subscribes to UMM's activation event later. Skin Manager's `SkinProvider.HandleModToggle` scans an activating data pack before CCL's `CarManager.HandleModToggled` imports its vehicles. `SkinConfig.LoadFromFile` resolves CarId immediately, rejects not-yet-registered liveries and discards those definitions. CCL's subsequent registration does not retry them. A later streamed panel can therefore lack explicit skin data even though the body can use CCL's normal material substitutions.

`src/SkinRegistrationFix.cs` uses that exact synchronous registration boundary. Its prefix defers the Skin Manager callback only for an active data-only mod containing both `ccl_bundle` and `skin.json`. Its finalizer on the CCL activation handler then runs the original Skin Manager callback once for that same mod. It preserves a CCL import exception and lets native discovery report genuinely unresolved IDs after the boundary. CCL-first event order also passes without altering the event subscription order. One bounded catch-up covers eligible packs already active when Toolbox installs the correction. There is no timer, frame coroutine or recurring global rescan.

The parent supplies an independent saved `SkinRegistrationFix` option, effective status and lifecycle dispatch. Owner `jimtaplin.derailvalley.toolbox.skinregistration` is distinct from the scorch guard. Only this option requires both active Skin Manager 4.4.1 and CCL 3.1.9 or newer; it verifies both runtime callback signatures and introduces no compile-time CCL/Skin Manager reference. As of 0.4.2-dev, the parent accepts CCL 3.1.9 and newer while Install still checks both assembly identities and exact static void activation signatures. Installed 3.1.11 was inspected: registration remains synchronous and the native fixture still reproduces six rejected embedded definitions without the correction, then discovers all six with it. The extended 96-check suite covers version boundaries, incompatible APIs, lifecycle and discovery. Future version-number checks use the installed 3.1.11 binary; they are policy tests rather than tests of hypothetical binaries. Code-bearing vehicle packs remain excluded, matching Skin Manager's existing EntryMethod filter.

The new suite passes 87 checks. An actual UMM activation reproduces six rejected embedded definitions before controlled registration. With the correction, the same native Skin Manager callback automatically discovers all six definitions and loads eighteen generated PNGs from disk. Both generated liveries cycle DVRT -> DVRT_New -> DVRT and primer, with all three maps applied to three generated moving-panel LODs while open transforms are retained. Additional checks cover listener order, repeated activation, invalid IDs, an import exception, early-active-pack catch-up, duplicate owner rejection, older shared settings, actual parent preferences, independent scorch ownership and unload.

This fixture replaces only CCL's bundle-import body with controlled livery registration. It does not manually add skins after registration, but it is still not a full clean game/UMM/CCL boot or an actual 5AT import. The existing companion paint mod, canonical vehicle IDs, themes and source artwork were not changed. Do not retire the companion or claim first-boot gameplay acceptance until a disposable full-game test covers embedded data without that companion, actual moving panels/LODs, external 4K paints, streaming, save/reload and Skin Manager disabled.

Evidence: `evidence/skin-startup/wardale-handoff-20261015.txt`, `native-fixture-checks.txt`, `receipt.json` and `VALIDATION.md`. Across all current suites the exact compiled DLL passes 1,032 checks with zero assertion failures. Deliberate baseline/invalid-input diagnostics and the initial sandbox IPC failure are retained separately and are not represented as clean game logs.

