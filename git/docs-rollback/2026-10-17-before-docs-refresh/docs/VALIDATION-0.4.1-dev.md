# Validation: 0.4.1-dev

Consist Fix (H6F) is removed following the upstream repair. Its implementation,
parent dispatch, dependency monitoring, setting and UMM controls are absent.
No other production module was changed. Six feature groups expose seven switches.

DLL SHA256: 82A771AAD6417188330D39D6CAFBBD7AC12BA30D4C8BE999C5576EC29ED21F48

Compilation completed without warnings or errors. Builds from both source
locations produce the same DLL. Nine suites passed against that exact DLL:
**16,385 checks, zero failures**.

| Suite | Passed |
| --- | ---: |
| Managed baseline | 70 |
| Combined parent, preferences, migration, faults and cleanup | 235 |
| HUD, including separate cold-start process | 118 |
| CCL Smoke | 29 |
| Controls: 67 source + 23 arithmetic + 31 native | 121 |
| Sound: 14,808 rules + 42 native | 14,850 |
| Native scorch protection | 37 |
| Native embedded-skin registration | 87 |
| Native DoubleTrack signs | 838 |

The combined suite verifies all 128 combinations of the remaining switches,
absence of H6F repair types/parent state, and actual UMM loading and saving of
0.4.0 settings containing ConsistFix. Other preferences survive; saving drops
the obsolete element. Counts are automated assertions, not gameplay scenarios.

Legacy standalone conflict fixtures read explicit archived copies through
TOOLBOX_LEGACY_MODS, because the standalone mods were removed during installation.
Receipts bind those DLL inputs by hash. No legacy mod is reinstalled or packaged.

Fixtures use installed UMM/Harmony and native APIs in isolated managed/Unity
processes. Some scene contents, consumer/import bodies and speed inputs are
controlled. Controls action-reference cloning is unavailable in this Editor for
native authored inputs, the original standalone and the merged component alike.
Other component/shared-port/authored-input/scheduler reference checks pass.

No full game launch, actual CCL pack import, real map traversal, user-save run,
sound audition, rendered smoke or live input acceptance was performed for this
revision. Those limits remain as documented in validation.json. The H6F vehicle
mod itself is outside this change and its upstream repair was not retested here.

The release archive contains one DadsToolbox root and exactly five entries:
our DLL, Info.json, README.md, ISSUE_AND_FIX.md and validation.json. Every entry
is read and hash-checked; dependency binaries and user settings are excluded.
