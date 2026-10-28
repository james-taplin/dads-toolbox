# Validation: 0.4.5-dev

## Recurring compatibility scans

A live toggle/reversal capture on 0.4.4 isolated two recurring Toolbox costs:
Sound Pitch and CCL Control Fix. With 1,792 methods timed by DVPerfProbe, disabling
Sound reduced Toolbox callback cost; disabling Controls removed the remaining
regular spikes. Re-enabling Sound alone restored roughly 200 ms maximum calls.
These magnitudes are specific to that instrumented run, not profiler-off estimates.

The source matches the mechanism: Sound's quarter-second poll performed a full
negative Harmony owner scan, and Controls did the same every second. Installed
Harmony 2.3.6 deserializes metadata for each patch-info query and allocates owner
collections. HasAnyPatches and instance GetPatchedMethods also scan globally,
so replacing helper names would not avoid the work.

## Change

Sound retains cheap availability polling; unchanged active, waiting and blocked
states skip global ownership reconciliation and repeated cleanup. Controls polls
cheap dependency identity/readiness/assembly snapshots. Configure, explicit
refresh/retry, dependency transitions and teardown retain full validation.
The parent no longer calls Controls.Configure on every frame; its existing GUI,
parent-toggle and dependency callbacks already apply configuration changes.

The existing UMM dispatcher remains the lifecycle integration point. No new
watcher or broad patching hook was added. A foreign residual patch installed
silently outside UMM, without a dependency change, is detected on the next
explicit reconciliation rather than through constant global polling.
Sound's cheap poll tracks availability-reason changes; relevant UMM events force
refresh even if that reason is unchanged. Controls additionally observes direct
dependency entry and assembly replacement.

## Regression evidence

Counters patch the underlying PatchProcessor.GetAllPatchedMethods/GetPatchInfo
boundaries, avoiding inlining of Harmony's small wrapper methods. On 0.4.4, three
Sound performance assertions, three Controls assertions and the parent Update
assertion fail. They pass on the corrected DLL:

- Stable Sound ticks make zero global patch snapshots/metadata reads while
  maintaining its discovery cadence and pending-audio dispatch.
- Stable waiting and residual-owner blocked Sound states also make zero reads.
- 128 stable Controls polls make zero global snapshots or metadata reads.
- Activation, dependency loss/recovery/replacement and explicit retry still
  revalidate conflicts, clean up owned patches and preserve foreign owners.
- The parent Update callback no longer forces Controls.Configure every frame.

Existing pitch-write, control repair, release, rollback and unload tests remain
in place. HUD, graphics and render-scale behavior were not changed.

The player will run the live stationary comparison after installation, including
the mode-3 case and then normal play. That follow-up is pending; isolated tests
do not establish a new FPS, allocation rate or callback maximum. Separate audio,
GC, streaming and autosave stalls are not attributed to this change.

DLL SHA256:
890B3898DD4A54D2F0EA2DBF2A0249A08EA9B7594857E6B02E305EF260901A15

All nine exact-DLL suites pass: **16,965 checks, zero failures**. The sound suite
contains 15,324 rule checks and 73 native checks; controls contains 67 source,
23 arithmetic and 44 native lifecycle checks. The parent suite passes 238 checks.
Other suites retain their existing validation scope and limits.
