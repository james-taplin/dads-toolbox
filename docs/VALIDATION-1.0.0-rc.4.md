# Validation: 1.0.0-rc.4

All eleven suites pass: **17,596 checks, zero candidate failures**, against the
same compiled DLL. These build checks were completed on 29 October 2026.

Subsequent installation and game-log reviews through 2 November found no Toolbox
faults or exception stacks. Logs demonstrate chuff adaptation, smoke correction,
pitch requests in both directions, bogie presentation and sign clearance.
James also reports accepted chuff listening tests on 2-, 4-, 6- and 16-cylinder
locomotives. This supports a public RC, not universal vehicle coverage.

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

## Limits

Isolated fixtures and the tested locomotive packs do not establish every vehicle
pack's sound or compatibility. Multiplayer remains unverified. Recent logs show
one-time sound setup costs around 160–204 ms; they do not provide a controlled
steady-state performance comparison. HUD Guard intentionally waits when the
supported HUD is disabled. Skin and control startup status alone does not prove
an individual repair occurred in every session.

DLL SHA256:
`0DCF2A1BB5E951B864AADA6C13C2CA784B3EB7C897906622E35C689EA48CB565`
