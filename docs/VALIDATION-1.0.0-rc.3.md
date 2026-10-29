# Validation: 1.0.0-rc.3

All ten suites pass: **17,133 checks, zero failures**, against the same compiled
DLL. This candidate is staged locally and has not been installed or published.

| Suite | Passing checks |
| --- | ---: |
| Managed integration | 63 |
| Combined lifecycle/settings | 348 |
| HUD Guard | 111 |
| Smoke | 21 |
| Controls | 133 |
| Sound rules/native audio | 15,404 |
| Skin damage | 37 |
| Embedded registration | 96 |
| Sign clearance | 854 |
| Bogie integration/native comparison | 66 |

## Scope

Removed runtime checks for unpublished standalone fixes, their residual-patch
probes, bogie orphan searches/polling, private load ordering and standalone
sound-settings import/UI. Existing Toolbox feature choices and nested sound
values remain compatible; old import markers are ignored and dropped on save.

Required-mod readiness, supported API/version checks, the public ZSounds
incompatibility, integrated-instance ownership, owned-patch cleanup and foreign
patch preservation remain covered. Obsolete standalone test cases were removed;
the parent still checks all 256 combinations of eight saved switches.

The bogie filtering/layout/presentation implementation is unchanged from rc.2.
Its original 0.4.2 DLL remains an offline test reference, not a runtime dependency
or detection feature. Native Play Mode comparisons and restoration checks pass.
No game assemblies are included in the five-file UMM package.

The [rc.2 validation](VALIDATION-1.0.0-rc.2.md) describes the controlled geometry
comparisons and their limits. These fixtures do not replace real vehicle-pack,
audible sound, visual quality, camera/constraint scheduling or gameplay
performance acceptance. No game launch or new gameplay claim is made here.

DLL SHA256:
`DF17A345159AC897720ED929D98B5A370D1FAC1D1E61EBF1833339F95C5D8EE8`
