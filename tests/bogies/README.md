# Bogie visual smoothing integration checks

Run `Run.ps1 -ToolboxDll <final DLL> -StandaloneDll <retained 0.4.2 DLL>` in the
private validation workspace. The runner creates its own Unity 2019.4 fixture
project and never launches Derail Valley or installs files. It records exact DLL,
source, native-reference and verdict hashes in `evidence/bogies/receipt.json`.

Checks use the actual compiled parent/module and real Unity geometry, renderers,
rigidbodies, native Bogie/TrainCar components, UMM entries/events and standalone
filter code. Only synthetic game components' startup/destruction callbacks are
suppressed, preventing them from booting game globals. Tests run in Editor Play
Mode in an empty scene. A SessionState continuation survives the domain reload
before loading native game assemblies. Actual enable/disable/destroy callbacks
are exercised; world and iterator inputs are explicit. Real game scheduler,
camera and constraint-solver ordering remain outside these checks.

Coverage includes standalone conflicts, no-CCL activation, fresh and duplicate
lifetimes, parent and feature switches, native owner discovery and ambiguous
constraints, proxy takeover/restoration, local wheel/rod animation, final-pose
sampling, displacement limits, cancellation during yielded source replacement,
different retry deadlines, despawn/world cleanup, real callback unsubscription,
and unchanged standalone filter constants/output.

Three further controlled comparisons run the standalone and integrated visual
cores on the same synthetic native scene: a single bogie, a separate constrained
owner, and a long articulated layout with multiple owners. They compare proxy
poses, local animation, build work units, displacement caps, source replacement
classification and renderer restoration. Warmed presentation timings are recorded
without a fragile pass/fail speed threshold. These are configuration patterns,
not imported vanilla or CCL vehicle assets and not live gameplay benchmarks.

The older 33-check standalone harness extracted selected methods into stand-ins;
it is retained separately and is not presented as native integration validation.
Visual quality, real vehicle layouts, automatic constraint evaluation, rerailing,
gameplay performance and GPU draws
still require in-game acceptance. These checks do not certify universal coverage.
