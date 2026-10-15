# CCL Controls integration fixtures

The canonical reference is the reconciled 1.4.0 working source in
`C:/Users/james/Desktop/Derail Valley Mods/Mods (code)/DVCCLControlFix_Development_2026-10-14`.
That project and the installed standalone 1.3.0 are read-only inputs here.

The Toolbox adapter compiles five production files into the parent DLL:
`ControlsRuntime.cs`, `ControlsConnections.cs`, `ControlsMath.cs`,
`ControlsBridge.cs` and `ControlsModule.cs`. The first three retain the repair
algorithm from the canonical three-file implementation; the last two replace
the standalone loader with parent-dispatched lifecycle and safe logging.

Run `tests/controls/Run.ps1 -ToolboxDll <exact final combined DLL>` after the
parent build. It runs these stages without installing anything or starting
Derail Valley:

1. **67 source-linked regressions:** the original 62 fixtures plus five parent
   cleanup/recreation/mutation cases. They compile the actual adapted repair
   sources and audited native mouse-scroll/toggle method excerpts. Unity
   objects, control physics, Rewired input state, reach and HUD timers are
   explicit managed stand-ins.
2. **23 arithmetic/key-plan checks:** original stepping and historical key-plan
   examples, linked to the actual adapted `ControlsMath.cs`.
3. **31 native lifecycle checks:** the actual combined DLL, installed UMM and
   Harmony, inside an isolated Unity 2019.4.40f1 Editor. These install/remove the
   real five native method patches, exercise optional dependency state,
   duplicate-owner rejection, logger faults, partial installation rollback,
   restart-latched standalone conflicts, off/on/unload and fresh lifetime.
   An inactive synthetic prefab also verifies actual Unity cloning of the
   repaired component, shared-port field, authored-input references and native
   keyboard scheduler references, without initialized Rewired or cab physics.
   A fixture-only in-memory patch-class substitution triggers a deliberate
   partial installation fault. It changes no binary on disk.

The clone fixture cannot host native `ActionReference` field serialization:
the native authored input, unmodified standalone 1.4.0 component and merged
component all omit those same fields in this isolated Editor. This is recorded
as an **unavailable check**, not a passing action-cloning test or an established
production cause. `clone-baseline.txt` retains the three-way comparison. Live
game action-reference cloning still needs acceptance.

Regular .NET Framework cannot host these game method wrappers: the installed
native Unity ECalls throw `SecurityException` during Harmony wrapper creation.
The failed CLR attempt is retained separately as host evidence and is not a
passing test. `RunNative.ps1` uses the Editor's existing desktop licence context
in a hidden window, with its own `tests/controls/UnityFixture`; no licence
settings are changed. `BuildAdapter.ps1` supports isolated adapter compilation
to `evidence/controls`, without overwriting the combined build.

The native lifecycle suite does **not** invoke a live cab, Rewired or vehicle
physics. Its clone fixture is synthetic; no real game boot, focus/reach geometry,
real locomotive prefab remapping, audible release, save/load or gameplay acceptance is
claimed. Those still require the Leader both cabs, JNR 9600, shared firedoor,
freight handbrake and stock-loco acceptance list in the canonical handover.

`evidence/controls/receipt.json` binds the current combined DLL, production and
fixture sources, native reference hashes and successful verdict. Regenerate
it whenever the combined DLL changes.
