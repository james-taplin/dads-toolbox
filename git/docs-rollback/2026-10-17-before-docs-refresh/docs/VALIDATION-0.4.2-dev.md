# Validation: 0.4.2-dev

Embedded-skin registration now accepts CCL 3.1.9 or newer rather than requiring
exactly 3.1.9. It still requires active Skin Manager 4.4.1, active CCL, both
assembly identities and exact static void activation callback signatures.
Missing/older version metadata or incompatible APIs leave this correction inactive.
No registration algorithm, other feature or saved preference was changed.

## Need for the correction

Inspection of installed CCL 3.1.11 shows that CCLPlugin.Load scans already-active
mods and then subscribes CarManager.HandleModToggled to UMM activation. That
handler synchronously calls LoadCarDefinitions, imports the pack and registers
its liveries before returning. Skin Manager 4.4.1 loads first and its activation
listener attempts embedded skin discovery before those liveries exist. CCL has
no equivalent post-registration Skin Manager replay in that path.

The native fixture against the installed 3.1.11 assembly reproduces six rejected
embedded definitions before controlled registration; with the correction the
same real UMM event and Skin Manager disk discovery load all six automatically.
The original 0.4.1 parent remains blocked by its exact-version gate. The updated
parent passes all 96 registration/lifecycle checks. A test of an incompatible
assembly begins a fresh activation, because an already-installed module does
not replace its loaded assembly during its lifetime.

CCL 3.1.11 DLL SHA256:
93C860871869DF943A407BD8435FF05BF5C5086949E5B110BD4E3BCF3CD92D65

## Exact-binary regression results

Toolbox DLL SHA256:
03AD903F1B3B1559016A163C0728EC64C029902622B3EF09817D55853FF80C4E

Release ZIP SHA256:
7DC5262EAFF34B2580B64201F4A4D74548A7E6DB47DECDF51D144A3F54EC3964

Both source locations compile to the identical DLL. Nine suites pass against
that DLL: **16,394 assertions, zero failures**.

| Suite | Passed |
| --- | ---: |
| Managed baseline | 70 |
| Combined parent/settings/migration/faults | 235 |
| HUD, including separate cold start | 118 |
| CCL Smoke | 29 |
| Controls: source/arithmetic/native | 121 |
| Sound: 14,808 rules + 42 native | 14,850 |
| Native scorch protection | 37 |
| Native embedded-skin registration | 96 |
| Native DoubleTrack signs | 838 |

The fixture reads actual installed CCL package metadata, and its receipt hashes
Info.json as well as the importer DLL. Policy tests cover 3.1.8 rejection,
unknown-version rejection, 3.1.9/3.1.10/3.1.11 and later version acceptance,
incompatible API rejection, restored dependencies and unchanged Skin Manager
version restrictions. Later version checks use the installed 3.1.11 binary with
different metadata: they do not certify hypothetical future binaries. Historical
3.1.9 native results remain in the earlier validation records.

## Limits

The native fixture replaces CCL's bundle-import body with controlled livery
registration, while exercising the actual callback target, UMM event and Skin
Manager discovery/PNG loading. Installed callback inspection corroborates that
synchronous registration boundary. Full clean game boot, real bundle import,
actual artwork, streaming and save/reload remain live acceptance checks.
The existing companion paint-data workaround is unchanged. The controls fixture
retains its documented ActionReference cloning limitation. No game or user save
was opened for this validation. No dependency binaries or decompiled sources
are included in the release.
