# Validation: 0.4.4-dev

## Sound pitch correction

The old steam rule returned 1x for any boiler smaller than its sound donor and
clamped upward changes to 1x. A second gate suppressed an entire CCL locomotive
when its boiler matched either stock steam size, even when a sound came from
the other donor. The native baseline reproduces six failures covering actual
upward AudioSource/one-shot writes, cross-donor scaling and the missing upper cap.

Steam now uses the same bidirectional ratio curve as non-steam: ratio raised
to (-0.2 * strength), within the selected lower/upper caps. Steam uses boiler
barrel area; non-steam uses configured empty mass. Matching size is neutral only
relative to the resolved donor. Stock identity, custom clips, cab equipment and
ownership protection remain in place. Steam's existing non-steam-donor mass
fallback also respects both steam caps.

The UMM panel keeps separate steam/non-steam settings, with strength and caps
displayed as percentages. Stored units are unchanged: strength 1 means 100%.
The new steam upper cap defaults to 2x and permits 1x–3x; the existing lower cap
permits 0.1x–1x. Existing preferences are retained. A completed layered pitch call
emits one applied-pitch log per discovered engine, without per-frame log spam.

## Verification

The sound fixture uses the exact compiled DLL, seeded donor/vehicle metadata,
native LayeredAudio.SetPitch and Unity AudioSources. It verifies actual pitch
properties and non-neutral write counters, including:

- C48A-sized boiler (44.50) with S282-sized donor (47): upward pitch, about 1.074x
  at the previously configured strength of 6.4955945.
- L74-sized boiler (47) with S282 donor (47): neutral, no shifted write.
- Larger boilers, cross-stock-size donors, both steam caps and zero strength.
- Actual one-shot arguments, direct sources, refresh/restoration and no compounding.
- Lighter/heavier/equal-mass diesels, both caps and zero strength.
- Stock, cab and custom-clip exclusions; applied-pitch logging without repetition.

Source-linked rule tests cover both directions, tolerance boundaries, strength
monotonicity, caps, corrupt inputs and extreme ratios. Combined parent tests
verify old XML receives the new cap without resetting values, and that it saves
and reloads through native UMM serialization.

These fixtures do not load the real C48A/L74 bundles or audition audio in-game.
Live sound quality and interactions with other audio mods still need listening
acceptance. No HUD Guard or other feature behavior was changed.

## Final results

Nine suites pass against the final DLL: **16,943 checks, zero failures**.
Sound contributes 15,324 rule checks and 65 native checks. The combined parent
suite passes 237 checks, including both old-settings compatibility and the new
steam cap roundtrip. Other suites retain their existing scope and limitations.

DLL SHA256:
1C2524FEEBB445611F8ABC0E5670C0B68AB055C494990AEB1DEFE50B8A694E5F
