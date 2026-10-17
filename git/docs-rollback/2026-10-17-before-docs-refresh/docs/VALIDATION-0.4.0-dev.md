# Validation: 0.4.0-dev

DLL SHA256: F301D91A959AD49EF8607008EEF9F7EA6CB76BA236C22C8ABA84CD8AB572DA69
Release ZIP SHA256: 485C1D684A43A409DDFC0EF8FA3D2B9366545EBE1A5F2BDB569165D77C2232C4

Compilation completed without warnings or errors. Ten suites passed against this exact DLL, with 16,600 checks and no failures:

| Suite | Passed |
| --- | ---: |
| Managed baseline | 70 |
| Combined parent, preferences, migration, faults and cleanup | 363 |
| HUD, including separate cold-start process | 118 |
| Consist / H6F | 87 |
| CCL Smoke | 29 |
| Controls: 67 source + 23 arithmetic + 31 native | 121 |
| Sound: 14,808 rules + 42 native | 14,850 |
| Native scorch protection | 37 |
| Native embedded-skin registration | 87 |
| Native DoubleTrack signs | 838 |

The tests use actual installed native APIs and UMM/Harmony in controlled managed/Unity fixtures. Some consumer/import bodies, speed inputs and scene contents are substituted. No full game/CCL boot, real map traversal or user-save run is claimed.

Controls action-reference cloning remains unavailable in the isolated Editor: native authored input, original standalone and merged component omit the same fields. Other component/shared-port/authored-input/scheduler reference clone checks pass. Real locomotive prefabs, Rewired, focus and physics need live acceptance.

Further acceptance includes H6F first import/spawn, actual embedded packs without companion data, real artwork/4K/streaming, physical explosions/repair, rendered smoke, audio listening/one-shot scheduling/performance, live UI and save/reload. Keep the existing companion paint-data workaround until actual embedded-pack acceptance passes.

The release archive contains one DadsToolbox root and exactly five entries: our DLL, Info.json, README.md, ISSUE_AND_FIX.md and validation.json. All entries were read and hash-checked; no dependency binaries are bundled.
