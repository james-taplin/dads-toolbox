# Merged HUD / Consist / Smoke checks

Run tests/Run-ModuleChecks.ps1 -Kind hud, consist or smoke. Each executable references the exact build/DadsToolbox.dll and records its hash plus fixture/native-reference/verdict hashes. Original maintained projects are read-only.

HUD fixture sources came from Mods (code)/HUDGuard/tests; Consist from Mods (code)/H6FTrainsetFix/tests; Smoke from CCLSmokeFix/validation. HUD still runs its separate cold process without loading the game/HUD/CCL/quantities assemblies. Native Power speed input is replaced by a constant; actual query/calculation/formatting run. H6F uses actual mapped types/AddTrainsets, replacing only the downstream registry consumer with an observer. Smoke uses its real Init target with a final gate skipping the native body; its correction flag is explicitly simulated.

Two original structural expectations needed adaptation for a combined DLL:
- The parent necessarily references base-game assemblies for other features. The reference check still rejects hard optional HUD/CCL dependencies, while allowing Assembly-CSharp.
- The smoke fixture now verifies its class comes from the actual DadsToolbox DLL; the standalone module's UMM-free assembly claim does not describe the shared parent.
- The smoke correction-record test reaches the same internal hook by reflection because the fixture is now a separate assembly.

Correction/lifecycle/conflict/owner/fault assertions are retained. The extra combined-DLL reference metadata assertions increase totals; counts should not be confused with extra gameplay coverage. The runner adds one normalized aggregate verdict, including four separately executed HUD cold-start checks.

These fixtures do not establish rendered smoke, actual trainset registry/spawn, real Rigidbody speed, full parent GUI or game startup. See evidence/validation.json and remaining acceptance.
