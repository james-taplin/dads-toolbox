# Validation: 1.0.0-rc.2

Adds Bogie Visual Smoothing from the working standalone **DVLocoBogieVisuals
0.4.2**. The combined candidate passes all ten suites: **17,195 checks, zero
failures**. It is staged only; installation and publication have not occurred.

| Suite | Passing checks |
| --- | ---: |
| Managed integration | 70 |
| Combined lifecycle/settings | 370 |
| HUD Guard | 120 |
| Smoke | 29 |
| Controls | 137 |
| Sound rules/native audio | 15,408 |
| Skin damage | 37 |
| Embedded registration | 96 |
| Sign clearance | 858 |
| Bogie integration/native comparison | 70 |

Most sound checks are deterministic rule cases, not gameplay tests. The parent
suite now covers all 256 combinations of eight saved switches.

## Bogie integration

The import uses the current standalone source, not the earlier interpolation
mod or 0.4.1 candidate. Layout, filtering, sampling and presentation remain
identical after the namespace change. The standalone UMM loader is excluded.
Toolbox owns a fresh runtime per activation, explicit synchronous shutdown,
manual refresh/report controls and duplicate protection. Cleanup completion is
recorded after restoration so a failed cleanup can be retried.

The original car-relative filtering, final constrained-pose sampling, native
wheel/rod animation, size-aware displacement caps and replacement handling are
retained. Maintenance keeps the 0.75 ms scheduling target, 64 work units and
eight registry checks per frame. Sampling and presentation are outside that
budget; a single native call cannot be preempted. Replacement becomes eligible
for remapping after 0.25 seconds, not necessarily complete by then. Genuine
mapping failures retain their error diagnostics and longer retry.

Seventy native Play Mode checks cover activation without CCL, feature/parent
toggles, recreation, partial-build cancellation, native renderer restoration,
mesh/skin/bone/blend-shape copying, source replacement, world registry cleanup,
callbacks, ownership conflicts and failed-cleanup retry. Fixture-owned game
startup callbacks are suppressed to avoid booting game services.

## Standalone comparison

The exact standalone DLL is checked by SHA256. Controlled native scenes compare
simple, separately constrained and long articulated layouts against the same
motion. Proxy poses, wheel animation, build work units, displacement limits,
replacement classification and restoration match.

| Controlled layout | Standalone / integrated time for 120 warmed presentation calls |
| --- | ---: |
| Single bogie | 0.620 / 0.602 ms |
| Separate constrained owner | 0.964 / 1.047 ms |
| Long articulated owners | 1.260 / 1.293 ms |

These are one run's synthetic host measurements, including reflective dispatch.
They are not real vehicle-pack or frame-rate benchmarks. Initial construction
timings include first-use costs and are not used to claim a speed improvement.

## Deployment and remaining acceptance

`WorldStreamer.dll` and `UnityEngine.AnimationModule.dll` are compile-time game
references; no game assemblies are packaged. A loaded standalone or residual
runtime blocks Toolbox smoothing. Vanilla support does not require CCL.

The standalone source, releases, installed 0.4.2 and separate diagnostic probe
remain untouched. Migration requires a closed-game backup and moving the
standalone outside Mods before a cold start. Unticking it mid-session is
insufficient. For native-only probe captures, disable Toolbox smoothing and
restart; the older probe guard does not recognize the integrated feature.

Real vanilla/CCL/long-locomotive visual comparisons, rapid arrangement changes,
GPU drawing, actual constraint/camera scheduling and in-game performance remain
live acceptance checks. The positive standalone feedback and reviewed 28 October
log are baseline evidence, not a gameplay test of this combined candidate.

Combined DLL SHA256:
`FEAAEAB7CC40475F1F681BF79E88845DE9AB527FE065E789914DC492189060F7`

Standalone reference SHA256:
`282F452670B3AFA2E238C08ED0E5B220CD2B7457280E2D2E876CD20A5DFED86B`
