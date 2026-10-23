# Validation: 0.4.3-dev

## Sign clearance during world reload

The affected game log shows the scanner's NullReferenceException during a new
game_w3 scene load, after it had corrected signs in the previous world. Its old
diagnostic included no stack trace, so the exact original throw site cannot be
proven from that message alone.

The installed native RailTrackRegistryBase.OrderedRailtracks getter lazily
resolves TrackRootParent via GameObject.Find("[railway]").transform. A registry
can exist before that scene root, which makes the getter throw. The native
fixture reproduces this exception and the old scanner's persistent fault. That
matches the load-transition state in the report.

The scanner now waits for a live, active railway root before reading the native
track list, discards old geometry while waiting and resumes on its next scheduled
scan. The root is cached while alive to avoid repeating a scene lookup every
second. Unexpected exceptions still stop the scanner, with a one-time full
exception log and the existing explicit re-scan option.

## Verification

The sign fixture passes 846 checks. Eight new checks cover the native getter's
missing-root exception, scanner containment, throttled automatic recovery,
repeated teardown and inactive-root recovery. Existing geometry, origin offset,
height/facing, movement-limit, dependency, conflict and cleanup checks still pass.

All nine suites pass against the final DLL: **16,402 checks, zero failures**.

| Suite | Passed |
| --- | ---: |
| Managed baseline | 70 |
| Combined parent/settings/migration/faults | 235 |
| HUD, including separate cold start | 118 |
| CCL Smoke | 29 |
| Controls | 121 |
| Sound | 14,850 |
| Native scorch protection | 37 |
| Native embedded-skin registration | 96 |
| Native DoubleTrack signs | 846 |

DLL SHA256:
ECDA2B3D147A4F25925B40C828898980BDBD06CB9EA861CDA94838C3086C7297

The fixtures use installed native APIs with generated railway/sign data and
controlled lifecycle dispatch. They do not run a full game scene reload or the
DoubleTrack generation coroutine. Live game acceptance remains to be checked
after updating. No HUD Guard behavior or user HUD settings were changed.
