# Validation: 1.0.0-rc.1

This candidate compiles against Derail Valley build 99 and passes all nine
automated suites: **16,991 checks, zero failures**. It has not been installed
or published. Final gameplay acceptance of this candidate is pending.

| Suite | Passing checks |
| --- | ---: |
| Managed integration | 70 |
| Combined lifecycle and settings | 238 |
| HUD Guard | 118 |
| Smoke | 29 |
| Controls | 137 |
| Sound rules and native audio | 15,408 |
| Skin damage | 37 |
| Embedded skin registration | 96 |
| Sign clearance | 858 |

Most sound checks exercise deterministic pitch rules; 84 use native audio and
lifecycle APIs. Controls includes 70 source regressions, 23 arithmetic checks
and 44 native lifecycle checks. These totals are not counts of gameplay tests.

## Corrections verified

**Sign elevation.** DoubleTrack 2.1.1 places sign roots at rail-curve height.
Clearance now retains segment elevations and interpolates height at the closest
horizontal point. A conservative five-metre vertical envelope separates distant
railway levels while preserving full horizontal clearance for nearby tracks.
Twelve new checks cover bridges, underpasses, slopes, boundaries and origin
shifts. Eight fail on 0.4.5 and pass here. The envelope is a placement policy,
not an exact mesh-intersection test. The extra segment elevations use about
5 MiB for the track count observed in the reviewed game logs.

**Sound discovery.** A shared hierarchy inventory avoids repeated traversal,
and module metadata is rebuilt once per discovery lifetime. An isolated native
fixture bound all 64 layers in both builds: full signature construction fell
from 128 calls to one, measuring 49.72 ms versus 4.56 ms in that run. This does
not establish the improvement for a real Class 20 spawn. Rediscovery observes
changed clips and cab classification; stock, custom-clip and cab protections
retain their regression coverage.

**Input and cleanup.** Control field metadata is cached while action bindings,
direction and inversion remain live reads. Tests cover replacement actions,
cache reuse and cleanup. Sound descriptions are removed on despawn and fault
deduplication is bounded; repeated lifecycle checks confirm cleanup.

**Doppler.** Native tests verify scaled desired pitch, application to the actual
AudioSource, curve handling and restoration on disable. Diagnostics now separate
the requested pitch from the immediate source snapshot.

## Compatibility and limits

The installed dependencies used for validation were CCL 3.1.11, Skin Manager
4.4.1, DoubleTrack 2.1.1 and HeadsUpDisplay 1.1.2. Existing settings, optional
dependency behaviour, standalone conflict protection and independent switches
remain covered. Both source workspaces produce the same DLL.

Fixtures use controlled objects and seeded audio references. They do not replace
a real both-cab input/focus test, audible sound check, representative bundled-paint
selection or scorch/repaint/repair check. Native input ActionReference cloning
remains unavailable in the isolated Editor. HUD was deliberately disabled in the
reviewed game sessions. Those sessions ran 0.4.5, so their gameplay results do not
constitute a live test of this candidate or establish that every hitch is fixed.

DLL SHA256:
`2BD6F0717EB90DC374D79EF5E4C5069C1B6EDE1BA6A090618E35F514F0AF45BD`
