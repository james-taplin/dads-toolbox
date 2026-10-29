# Chuff integration checks

`Run.ps1` writes only generated fixture outputs and `evidence/chuff`. It never launches Derail Valley or modifies installed mods. Run from a cached private test workspace; the isolated Unity project needs the installed Unity 2019 desktop entitlement.

The managed stage ports the existing 112 real-Harmony/native-method checks. It compiles the current `ChuffReaderAdaptation.cs` and `ChuffLoopAdaptation.cs` against Unity/audio adapters, normalizing their namespace and supplying a thin original loader bridge. Native reader and layer-pitch bodies are extracted verbatim from the local native-contract reference. The adapter's Sound bridge does nothing: interaction with Sound is tested separately against the actual combined DLL.

The same suite runs preserved 0.2.0 source as a negative control. Its expected result is 109 passes and three routing-order failures. This discriminator is recorded separately and is not counted as candidate test failures.

The native stage loads the exact combined DLL in isolated Unity Play Mode. It exercises real reader `Init`, `Deinit`, `UpdateLoops`, port identities, Harmony order, PCM read/write/rate conversion and muted AudioSources. Synthetic native engine/flow fields supply controlled ports; only their engine physics and fixture-owned car startup callbacks are suppressed. Tests cover both routing orders, effective frequency/bank restoration, live detached readers, pooling/destruction, duplicate ownership, custom/nonlinear/ash exclusions, Sound ownership in both activation/stop orders and actual parent shutdown.

The native stage checks deferred generated-clip destruction after a real frame. Unexpected Unity errors fail validation; the deliberately missing sixth-port case permits only its specific native refusal diagnostics.

Native Doppler checks cover both activation orders and an already-playing loop's initial PCM handoff before any loop update. Pending requests and actual source application must preserve cadence; the pre-fix DLL reproduces four failures here. The native suite has 91 assertions, in addition to the 112 adapter cases.

These tests do not load locomotive bundles, drive native simulation physics, exercise native one-shot pooling, audition sound or establish listening quality, real-pack coverage or gameplay performance.

The standalone reference source/package and 0.2.0 control remain read-only inputs outside this repository. `ChuffReferenceRoot`, `NativeSourcePath`, `GamePath`, `ToolboxDll` and `EditorPath` are configurable runner parameters.
