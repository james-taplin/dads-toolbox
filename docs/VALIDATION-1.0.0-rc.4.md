# Validation: 1.0.0-rc.4

All eleven suites pass: **17,596 checks, zero candidate failures**, against the
same compiled DLL. This candidate is staged locally and has not been installed
or published.

| Suite | Passing checks |
| --- | ---: |
| Managed integration | 63 |
| Combined lifecycle/settings | 608 |
| HUD Guard | 111 |
| Smoke | 21 |
| Controls | 133 |
| Sound rules/native audio | 15,404 |
| Skin damage | 37 |
| Embedded registration | 96 |
| Sign clearance | 854 |
| Bogie integration/native comparison | 66 |
| Chuff adapter/native audio | 203 |

## Chuff coverage

The imported 0.2.1 behaviour retains exact per-engine event matching, six-port
validation, last-priority routing, short-bank expansion and recognised native
loop adaptation. Toolbox adds reader enumeration, restoration and independent
module lifecycle. Sound Pitch yields only the rhythmic layers owned by Chuff Fix.

Validation separates the original real-Harmony/native-method adapter fixture
from an isolated Unity Play Mode fixture using the compiled Toolbox DLL, native
reader methods and real AudioClip PCM operations. The adapter fixture substitutes
Unity/audio support objects; its results are not native playback acceptance.
The retained 0.2.0 negative control records 109 passes and three routing failures.

The Chuff total comprises 112 adapter cases and 91 native checks. Native checks
cover real PCM samples and deferred clip destruction, both Sound activation
orders, Doppler requests and application, live restoration and parent shutdown.
They reproduced an initial PCM/Doppler handoff mismatch; the correction scales
the pending Doppler request alongside source pitch and restores it on rollback.

The parent checks all 512 XML combinations of nine saved switches. Active native
Chuff and bogie behaviour is covered separately from the CLR parent fixture.
Existing module suites are rerun against the same combined DLL. The package
contains one UMM entry and no game or dependency assemblies.

## Remaining acceptance

Final listening tests must cover stock two-cylinder, multi-cylinder, compound,
articulated and geared locomotives through crawl, loop transitions, high speed,
stop, reverse, slip and reload. Include conventional → Meyer → conventional and
Dockside/Shay. Isolated fixtures cannot establish every vehicle pack's sound,
real-game patch order or performance.

Deployment requires a closed game, backing up standalone MultiCylinderChuffFix
outside Mods, and a fresh restart. Preserve Settings.xml and locomotive packs.
The standalone 0.2.1 remains the fallback until game acceptance is complete.

DLL SHA256:
`0DCF2A1BB5E951B864AADA6C13C2CA784B3EB7C897906622E35C689EA48CB565`
