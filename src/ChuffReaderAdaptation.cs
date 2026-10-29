using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DV.ModularAudioCar;
using HarmonyLib;
using LocoSim.Implementations;
using UnityEngine;

namespace DadsToolbox.Chuffs
{
    internal static class ReaderAdaptation
    {
        internal static readonly FieldInfo NativeCycleField = AccessTools.Field(typeof(ReciprocatingSteamEngine), "chuffsPerCycle");
        internal static readonly FieldInfo CarField = AccessTools.Field(typeof(ChuffClipsSimReader), "car");
        internal static readonly string[] PortFields = { "chuffEventPort", "exhaustPressurePort", "chuffFrequencyPort", "cylinderWaterNormalizedPort", "cylinderCockControlPort", "ashesInPipesPort" };
        private static readonly FieldInfo FrequencyField = AccessTools.Field(typeof(ChuffClipsSimReader), "chuffFrequencyPort");
        private static readonly ConditionalWeakTable<ChuffClipsSimReader, ReaderState> Readers = new ConditionalWeakTable<ChuffClipsSimReader, ReaderState>();
        private static readonly ConditionalWeakTable<SimulationFlow, FlowState> Flows = new ConditionalWeakTable<SimulationFlow, FlowState>();
        private static readonly List<WeakReference> ActiveStates = new List<WeakReference>();
        // Strong roots exist only while failed cleanup/native rebind needs retry.
        private static readonly List<ReaderState> FailedCleanup = new List<ReaderState>();
        private static readonly List<RebindTarget> PendingRebinds = new List<RebindTarget>();
        private static readonly Dictionary<ChuffClipsSimReader, Port> RebindingFrequency = new Dictionary<ChuffClipsSimReader, Port>();
        internal static bool RestartRequired { get; private set; }
        internal sealed class FlowState { internal long Epoch; internal int Readers; }
        internal sealed class ReaderState
        {
            internal ChuffClipsSimReader Reader;
            internal TrainCar Car;
            internal ReciprocatingSteamEngine Engine;
            internal SimulationFlow Flow;
            internal FlowState Clock;
            internal string FrequencyId;
            internal string OwnedFrequencyId;
            internal Port OriginalBoundFrequency, AdaptedFrequency;
            internal ChuffClipsSimReader.OrderedChuffClips[] Low, Medium, High, OwnedLow, OwnedMedium, OwnedHigh;
            internal int Beats;
            internal long LastLoopEpoch = long.MinValue;
            internal LoopAdaptation Loops;
            internal Action Refresh;
            internal bool Registered, Restored, NativeInitialized;
            internal void Restore()
            {
                try { RestoreCore(); }
                catch { if (!FailedCleanup.Contains(this)) FailedCleanup.Add(this); throw; }
            }
            private void RestoreCore()
            {
                if (Restored) return;
                if (Refresh != null) { Flow.TickEvent -= Refresh; Refresh = null; }
                // Destroyed Unity components still have their managed fields.
                // Restore those references without accessing native object APIs.
                if (!ReferenceEquals(Reader, null))
                {
                    var boundFrequency = FrequencyField.GetValue(Reader) as Port;
                    bool ownedBinding = ReferenceEquals(boundFrequency, AdaptedFrequency);
                    if (Reader.chuffFrequencyPortId == OwnedFrequencyId &&
                        (ownedBinding || boundFrequency == null || ReferenceEquals(boundFrequency, OriginalBoundFrequency)))
                        Reader.chuffFrequencyPortId = FrequencyId;
                    if (ownedBinding)
                    {
                        Port original = OriginalBoundFrequency;
                        if (original == null && !string.IsNullOrEmpty(FrequencyId)) Flow.TryGetPort(FrequencyId, out original);
                        FrequencyField.SetValue(Reader, original);
                    }
                    if (ReferenceEquals(Reader.lowPressureClips, OwnedLow)) Reader.lowPressureClips = Low;
                    if (ReferenceEquals(Reader.mediumPressureClips, OwnedMedium)) Reader.mediumPressureClips = Medium;
                    if (ReferenceEquals(Reader.highPressureClips, OwnedHigh)) Reader.highPressureClips = High;
                }
                // Keep ownership and this state retryable if clip/callback cleanup fails.
                Loops?.Dispose(); Loops = null;
                if (Readers.TryGetValue(Reader, out var current) && ReferenceEquals(current, this)) Readers.Remove(Reader);
                if (Registered)
                { Registered = false; if (--Clock.Readers == 0) Flows.Remove(Flow); }
                Restored = true; FailedCleanup.Remove(this);
            }
            internal void RefreshAfterTick()
            {
                if (!Main.Enabled || Restored) return;
                if (!Reader || !Car) { TryRestore(this); return; }
                if (LastLoopEpoch == Clock.Epoch) return;
                Main.UpdateNativeLoops(Reader); LastLoopEpoch = Clock.Epoch;
            }
        }
        private sealed class RebindTarget
        { internal ChuffClipsSimReader Reader; internal TrainCar Car; internal SimulationFlow Flow; }
        internal static ReaderState Prepare(ChuffClipsSimReader reader, SimulationFlow flow)
        {
            Restore(reader);
            if (flow == null || string.IsNullOrEmpty(reader.chuffEventPortId) || !flow.TryGetPort(reader.chuffEventPortId, out Port events)) return null;
            ReciprocatingSteamEngine matched = null;
            foreach (var component in flow.OrderedSimComps)
            {
                var engine = component as ReciprocatingSteamEngine;
                if (engine == null || !ReferenceEquals(engine.chuffEventReadOut, events)) continue;
                if (matched != null && !ReferenceEquals(matched, engine))
                { Main.WarnOnce(reader, "engine-ambiguous", "More than one steam engine owns the event port; configured audio retained."); return null; }
                matched = engine;
            }
            if (matched == null || matched.chuffFrequencyReadOut == null)
            { Main.WarnOnce(reader, "engine-unmatched", "No exact native steam-engine event-port match; configured frequency retained, index guard only."); return null; }
            int beats = (int)NativeCycleField.GetValue(matched);
            if (beats <= 0) { Main.WarnOnce(reader, "engine-cycle", "Native engine has no valid exhaust cycle; configured audio retained."); return null; }
            var state = new ReaderState { Reader = reader, Engine = matched, Flow = flow,
                FrequencyId = reader.chuffFrequencyPortId, OriginalBoundFrequency = FrequencyField.GetValue(reader) as Port,
                OwnedFrequencyId = matched.chuffFrequencyReadOut.id, AdaptedFrequency = matched.chuffFrequencyReadOut,
                Low = reader.lowPressureClips, Medium = reader.mediumPressureClips, High = reader.highPressureClips, Beats = beats };
            // A live native Deinit cleared the original private binding. Keep
            // the effective port when it agrees with the final routed Init ID.
            if (state.OriginalBoundFrequency == null && RebindingFrequency.TryGetValue(reader, out var beforeRebind) &&
                beforeRebind != null && beforeRebind.id == state.FrequencyId) state.OriginalBoundFrequency = beforeRebind;
            Readers.Add(reader, state); ActiveStates.Add(new WeakReference(state));
            reader.chuffFrequencyPortId = matched.chuffFrequencyReadOut.id;
            return state;
        }
        internal static void Complete(ReaderState state, Port events, Port pressure, Port frequency, Port water, Port cocks, Port ash)
        {
            if (state == null || state.Restored) return;
            if (!ReferenceEquals(events, state.Engine.chuffEventReadOut) || !ReferenceEquals(frequency, state.Engine.chuffFrequencyReadOut) || pressure == null || water == null || cocks == null || ash == null)
            { state.Restore(); Main.WarnOnce(state.Reader, "init-incomplete", "Native chuff reader did not bind the matched engine and all six ports; adaptation rolled back."); return; }
            var reader = state.Reader;
            state.Car = CarField.GetValue(reader) as TrainCar;
            state.NativeInitialized = true;
            reader.lowPressureClips = state.OwnedLow = Expand(state.Low, state.Beats);
            reader.mediumPressureClips = state.OwnedMedium = Expand(state.Medium, state.Beats);
            reader.highPressureClips = state.OwnedHigh = Expand(state.High, state.Beats);
            if (state.Low == null || state.Low.Length == 0 || state.Medium == null || state.Medium.Length == 0 || state.High == null || state.High.Length == 0)
                Main.WarnOnce(reader, "missing-bank", "A pressure bank has no recordings; no replacement sound was invented and native errors remain visible.");
            state.Loops = LoopAdaptation.Apply(reader, state.Beats, message => Main.WarnOnce(reader, "loop:" + message, message));
            state.Clock = Flows.GetValue(state.Flow, _ => new FlowState()); state.Refresh = state.RefreshAfterTick;
            state.Registered = true; state.Clock.Readers++; state.Flow.TickEvent += state.Refresh;
            string key = "adapt:" + Main.CarName(reader) + ":" + state.Engine.id + ":" + state.Beats;
            if (Main.Once(key)) Main.Log(Main.CarName(reader) + ": engine " + state.Engine.id + ", " + state.Engine.numCylinders + " cylinders, " + state.Beats + " exhausts/revolution; audio bound to native exhaust Hz. Short pressure banks expanded; native running loops retained.");
        }
        private static ChuffClipsSimReader.OrderedChuffClips[] Expand(ChuffClipsSimReader.OrderedChuffClips[] source, int beats)
        {
            if (source == null || source.Length == 0 || source.Length >= beats) return source;
            var result = new ChuffClipsSimReader.OrderedChuffClips[beats]; for (int i = 0; i < beats; i++) result[i] = source[i % source.Length]; return result;
        }
        internal static void Restore(ChuffClipsSimReader reader)
        { if (reader != null && Readers.TryGetValue(reader, out var state)) state.Restore(); }
        private static bool TryRestore(ReaderState state)
        {
            try { state.Restore(); return true; }
            catch (Exception ex)
            { if (!FailedCleanup.Contains(state)) FailedCleanup.Add(state); Main.WarnOnce(state.Reader, "cleanup", "Chuff cleanup remains pending: " + ex); return false; }
        }
        private static List<ReaderState> Snapshot()
        {
            var states = new List<ReaderState>();
            for (int i = ActiveStates.Count - 1; i >= 0; i--)
            {
                var state = ActiveStates[i].Target as ReaderState;
                if (state == null || state.Restored) { ActiveStates.RemoveAt(i); continue; }
                states.Add(state);
            }
            foreach (var state in FailedCleanup) if (!states.Contains(state)) states.Add(state);
            return states;
        }
        internal static void Prune()
        {
            for (int i = ActiveStates.Count - 1; i >= 0; i--)
            {
                var state = ActiveStates[i].Target as ReaderState;
                if (state == null || state.Restored) { ActiveStates.RemoveAt(i); continue; }
                if (!state.Reader || (state.NativeInitialized && !state.Car)) TryRestore(state);
            }
        }
        internal static void BeginTick(SimulationFlow flow)
        { if (Main.Enabled && Flows.TryGetValue(flow, out var state)) unchecked { state.Epoch++; } }
        internal static void BeforeLoops(ChuffClipsSimReader reader)
        { if (Main.Enabled && Readers.TryGetValue(reader, out var state)) state.Loops?.Refresh(state.Engine.chuffFrequencyReadOut.Value); }
        internal static void AfterLoops(ChuffClipsSimReader reader)
        { if (Main.Enabled && Readers.TryGetValue(reader, out var state) && state.Registered) { state.LastLoopEpoch = state.Clock.Epoch; state.Loops?.AfterNativeLoops(); } }
        private static bool AllBound(ChuffClipsSimReader reader)
        { foreach (string name in PortFields) if (AccessTools.Field(typeof(ChuffClipsSimReader), name).GetValue(reader) == null) return false; return true; }
        private static bool AllResolvable(ChuffClipsSimReader reader, SimulationFlow flow)
        {
            foreach (string id in new[] { reader.chuffEventPortId, reader.exhaustPressurePortId, reader.chuffFrequencyPortId, reader.cylinderWaterNormalizedPortId, reader.cylinderCockControlPortId, reader.ashesInPipesPortId })
                if (string.IsNullOrEmpty(id) || !flow.TryGetPort(id, out Port port) || port == null) return false;
            return true;
        }
        internal static bool RebindExisting()
        {
            bool success = true;
            // Activation/explicit refresh only, not a recurring hierarchy search.
            foreach (var reader in Resources.FindObjectsOfTypeAll<ChuffClipsSimReader>())
            {
                if (!reader || !reader.gameObject.scene.IsValid()) continue;
                var car = CarField.GetValue(reader) as TrainCar;
                if (!car) continue; // Uninitialized prefab/pool entries will use the normal Init patch.
                var flow = car.SimController ? car.SimController.simFlow : null;
                if (flow == null || !AllBound(reader)) { success = false; Main.WarnOnce(reader, "live-rebind", "Existing reader cannot be safely rebound; reload/restart is required for that reader."); continue; }
                var nativeEvents = AccessTools.Field(typeof(ChuffClipsSimReader), "chuffEventPort").GetValue(reader) as Port;
                if (nativeEvents == null || !flow.TryGetPort(nativeEvents.id, out Port flowEvents) || !ReferenceEquals(flowEvents, nativeEvents))
                { success = false; Main.WarnOnce(reader, "live-flow", "Existing reader's bound event is outside this vehicle flow; no flow was guessed, reload/restart is required."); continue; }
                var originalFrequency = FrequencyField.GetValue(reader) as Port;
                if (Readers.TryGetValue(reader, out var previous) && ReferenceEquals(originalFrequency, previous.AdaptedFrequency))
                {
                    originalFrequency = previous.OriginalBoundFrequency;
                    if (originalFrequency == null && !string.IsNullOrEmpty(previous.FrequencyId)) flow.TryGetPort(previous.FrequencyId, out originalFrequency);
                }
                RebindingFrequency[reader] = originalFrequency;
                try
                {
                    reader.Deinit(); reader.Init(car, flow);
                    if (AllBound(reader)) Main.UpdateNativeLoops?.Invoke(reader);
                    else { success = false; RememberRebind(reader, car, flow); }
                }
                catch (Exception ex) { success = false; RememberRebind(reader, car, flow); Main.WarnOnce(reader, "live-rebind", "Native reader reinitialization failed; reload/restart is required: " + ex); }
                finally { RebindingFrequency.Remove(reader); }
            }
            return success;
        }
        private static void RememberRebind(ChuffClipsSimReader reader, TrainCar car, SimulationFlow flow)
        { if (!PendingRebinds.Exists(target => ReferenceEquals(target.Reader, reader))) PendingRebinds.Add(new RebindTarget { Reader = reader, Car = car, Flow = flow }); }
        internal static bool RestoreAll()
        {
            bool success = true;
            foreach (var state in Snapshot())
            {
                if (state.NativeInitialized && state.Reader && !PendingRebinds.Exists(target => ReferenceEquals(target.Reader, state.Reader)))
                    PendingRebinds.Add(new RebindTarget { Reader = state.Reader, Car = CarField.GetValue(state.Reader) as TrainCar, Flow = state.Flow });
                success &= TryRestore(state);
            }
            if (!success) return false;
            foreach (var target in PendingRebinds.ToArray())
            {
                if (!target.Reader || !target.Car) { PendingRebinds.Remove(target); continue; }
                try
                {
                    if (CarField.GetValue(target.Reader) as TrainCar) target.Reader.Deinit();
                    if (AllResolvable(target.Reader, target.Flow))
                    {
                        target.Reader.Init(target.Car, target.Flow);
                        if (AllBound(target.Reader)) Main.UpdateNativeLoops?.Invoke(target.Reader);
                        else { RestartRequired = true; Main.WarnOnce(target.Reader, "stop-rebind", "Native reader did not fully reinitialize after disabling Chuff Fix; reload/restart is required."); }
                    }
                    else { RestartRequired = true; Main.WarnOnce(target.Reader, "stop-rebind", "Original audio configuration has missing ports; reader left deinitialized, reload/restart is required."); }
                    PendingRebinds.Remove(target);
                }
                catch (Exception ex) { success = false; Main.WarnOnce(target.Reader, "stop-retry", "Native audio lifecycle cleanup needs retry: " + ex); }
            }
            return success;
        }
    }
    internal static class OnInitPatch
    {
        internal static void Prefix(ChuffClipsSimReader __instance, SimulationFlow simFlow, out ReaderAdaptation.ReaderState __state)
        { __state = Main.Enabled ? ReaderAdaptation.Prepare(__instance, simFlow) : null; }
        internal static void Postfix(ReaderAdaptation.ReaderState __state, Port ___chuffEventPort, Port ___exhaustPressurePort, Port ___chuffFrequencyPort, Port ___cylinderWaterNormalizedPort, Port ___cylinderCockControlPort, Port ___ashesInPipesPort)
        { if (Main.Enabled) ReaderAdaptation.Complete(__state, ___chuffEventPort, ___exhaustPressurePort, ___chuffFrequencyPort, ___cylinderWaterNormalizedPort, ___cylinderCockControlPort, ___ashesInPipesPort); }
        internal static Exception Finalizer(ReaderAdaptation.ReaderState __state, Exception __exception)
        {
            if (__exception != null && __state != null)
                try { __state.Restore(); } catch (Exception ex) { Main.WarnOnce(__state.Reader, "init-cleanup", "Audio cleanup failed while preserving the original Init exception: " + ex); }
            return __exception;
        }
    }
    internal static class OnDeinitPatch
    {
        internal static void Prefix(ChuffClipsSimReader __instance)
        {
            try { ReaderAdaptation.Restore(__instance); }
            catch (Exception ex) { Main.WarnOnce(__instance, "deinit-cleanup", "Adaptation cleanup failed; native Deinit will still run: " + ex); }
        }
    }
    internal static class OnFlowTickPatch { internal static void Prefix(SimulationFlow __instance) => ReaderAdaptation.BeginTick(__instance); }
    internal static class OnLoopsPatch
    {
        internal static void Prefix(ChuffClipsSimReader __instance) => ReaderAdaptation.BeforeLoops(__instance);
        internal static void Postfix(ChuffClipsSimReader __instance) => ReaderAdaptation.AfterLoops(__instance);
    }
}
