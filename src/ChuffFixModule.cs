using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DadsToolbox.Chuffs;
using DV.ModularAudioCar;
using HarmonyLib;
using LocoSim.Implementations;
using UnityEngine;
using UnityModManagerNet;

namespace DadsToolbox
{
    public sealed class ChuffFixModule
    {
        public const string Owner = "jimtaplin.derailvalley.toolbox.chufffix";
        public const string StandaloneId = "MultiCylinderChuffFix";
        private static ChuffFixModule current;
        private readonly UnityModManager.ModEntry.ModLogger logger;
        private readonly Action ownershipChanged;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Harmony harmony;
        private bool parentActive, unloaded, faulted, compatibilityChecked, residualConflict;
        private UnityModManager.ModEntry lastStandalone;
        private bool lastStandaloneBusy;
        private double nextProbe;
        private string status = "Off";
        public bool DesiredEnabled { get; private set; }
        public bool Active { get; private set; }
        public bool RestartRequired { get; private set; }
        public string Status => status;

        public ChuffFixModule(UnityModManager.ModEntry.ModLogger logger, Action ownershipChanged = null)
        { this.logger = logger; this.ownershipChanged = ownershipChanged; }
        public void Configure(bool selected, bool toolboxActive)
        {
            if (unloaded) return;
            bool changed = selected != DesiredEnabled || toolboxActive != parentActive;
            DesiredEnabled = selected; parentActive = toolboxActive;
            if (changed) { faulted = false; compatibilityChecked = false; }
            Reconcile(changed);
        }
        public void Tick()
        {
            if (unloaded || !parentActive || !DesiredEnabled || clock.Elapsed.TotalSeconds < nextProbe) return;
            nextProbe = clock.Elapsed.TotalSeconds + 1;
            Reconcile(false);
            if (Active) ReaderAdaptation.Prune();
        }
        public void Refresh()
        {
            if (unloaded) return;
            faulted = false; compatibilityChecked = false;
            bool wasActive = Active; Reconcile(true);
            if (wasActive && Active)
            {
                RestartRequired |= !ReaderAdaptation.RebindExisting();
                PublishStatus(); Changed();
            }
        }
        public bool Stop()
        { parentActive = false; return StopCore("Toolbox disabled"); }
        public bool Unload()
        {
            if (unloaded) return true;
            if (!Stop()) return false;
            unloaded = true; SetStatus("Unloaded" + RestartNotice()); return true;
        }
        private void Reconcile(bool force)
        {
            if (!parentActive || !DesiredEnabled)
            { StopCore(parentActive ? "Off" : "Toolbox disabled"); return; }
            if (faulted) return;
            try
            {
                var standalone = UnityModManager.FindMod(StandaloneId);
                bool busy = standalone != null && (standalone.Loaded || standalone.Active);
                force |= !ReferenceEquals(standalone, lastStandalone) || busy != lastStandaloneBusy;
                lastStandalone = standalone; lastStandaloneBusy = busy;
                if (busy)
                {
                    compatibilityChecked = false;
                    StopCore("Inactive: standalone Chuff Fix is loaded. Remove it and restart before using Toolbox Chuff Fix.");
                    return;
                }
                if (current != null && current != this)
                { SetStatus("Inactive: another Chuff Fix module owns this lifetime."); return; }
                bool validate = force || !compatibilityChecked;
                if (validate)
                {
                    compatibilityChecked = true;
                    residualConflict = HasOwner(StandaloneId);
                }
                if (residualConflict)
                { StopCore("Inactive: standalone Chuff Fix patches remain loaded. Remove it and restart."); return; }
                if (Active) return;
                if (!validate) return;
                if (harmony != null && !StopCore("Retrying Chuff Fix cleanup")) return;
                if (HasOwner(Owner)) { SetStatus("Inactive: the Toolbox Chuff Fix owner is already in use."); return; }
                Install();
            }
            catch (Exception ex)
            {
                faulted = true; StopCore("Inactive: Chuff Fix startup failed: " + ex.Message);
                Log("ERROR: " + ex);
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Install()
        {
            var chuff = AccessTools.Method(typeof(ChuffClipsSimReader), "OnChuff", new[] { typeof(float) });
            var init = AccessTools.Method(typeof(ChuffClipsSimReader), "Init", new[] { typeof(TrainCar), typeof(SimulationFlow) });
            var deinit = AccessTools.Method(typeof(ChuffClipsSimReader), "Deinit", Type.EmptyTypes);
            var loops = AccessTools.Method(typeof(ChuffClipsSimReader), "UpdateLoops", Type.EmptyTypes);
            var tick = AccessTools.Method(typeof(SimulationFlow), "Tick", new[] { typeof(float) });
            bool portsMatch = true;
            foreach (string name in ReaderAdaptation.PortFields)
                portsMatch &= AccessTools.Field(typeof(ChuffClipsSimReader), name)?.FieldType == typeof(Port);
            if (chuff == null || init == null || deinit == null || loops == null || tick == null || !portsMatch ||
                ReaderAdaptation.NativeCycleField?.FieldType != typeof(int) || ReaderAdaptation.CarField?.FieldType != typeof(TrainCar))
                throw new NotSupportedException("Native steam/audio lifecycle contract changed; this game build is unsupported.");
            current = this;
            Chuffs.Main.Logger = Log; Chuffs.Main.Enabled = false;
            harmony = new Harmony(Owner);
            harmony.Patch(chuff, prefix: new HarmonyMethod(typeof(OnChuffPatch), nameof(OnChuffPatch.Prefix)));
            harmony.Patch(init,
                prefix: new HarmonyMethod(typeof(OnInitPatch), nameof(OnInitPatch.Prefix)) { priority = Priority.Last },
                postfix: new HarmonyMethod(typeof(OnInitPatch), nameof(OnInitPatch.Postfix)),
                finalizer: new HarmonyMethod(typeof(OnInitPatch), nameof(OnInitPatch.Finalizer)));
            harmony.Patch(deinit, prefix: new HarmonyMethod(typeof(OnDeinitPatch), nameof(OnDeinitPatch.Prefix)));
            harmony.Patch(tick, prefix: new HarmonyMethod(typeof(OnFlowTickPatch), nameof(OnFlowTickPatch.Prefix)));
            harmony.Patch(loops, prefix: new HarmonyMethod(typeof(OnLoopsPatch), nameof(OnLoopsPatch.Prefix)),
                postfix: new HarmonyMethod(typeof(OnLoopsPatch), nameof(OnLoopsPatch.Postfix)));
            Chuffs.Main.UpdateNativeLoops = AccessTools.MethodDelegate<Action<ChuffClipsSimReader>>(loops);
            Chuffs.Main.Enabled = true; Active = true;
            RestartRequired |= !ReaderAdaptation.RebindExisting();
            PublishStatus(); Changed();
        }
        private bool StopCore(string reason)
        {
            if (current != this) { SetStatus(reason + RestartNotice()); return true; }
            Chuffs.Main.Enabled = false; Active = false;
            bool clean = ReaderAdaptation.RestoreAll();
            try { ChuffLoopOwnership.RestoreAll(); }
            catch (Exception ex) { clean = false; Log("ERROR: Rhythmic loop cleanup remains pending: " + ex); }
            if (!clean)
            { faulted = true; SetStatus("Cleanup incomplete: Chuff Fix is gated off; retry disabling or restart."); return false; }
            RestartRequired |= ReaderAdaptation.RestartRequired;
            try
            {
                if (harmony != null)
                    foreach (var method in Harmony.GetAllPatchedMethods().ToArray())
                        if (Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true)
                            harmony.Unpatch(method, HarmonyPatchType.All, Owner);
                if (HasOwner(Owner)) throw new InvalidOperationException("owned patches remain");
            }
            catch (Exception ex) { faulted = true; Log("ERROR: Chuff Fix cleanup: " + ex); return false; }
            harmony = null; current = null;
            Chuffs.Main.UpdateNativeLoops = null; Chuffs.Main.ResetDiagnostics(); Chuffs.Main.Logger = null;
            SetStatus(reason + RestartNotice()); Changed(); return true;
        }
        private static bool HasOwner(string id) => Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(id) == true);
        private void PublishStatus() => SetStatus("Active: native engine exhaust count/frequency and rhythmic loops." + RestartNotice());
        private string RestartNotice() => RestartRequired ? " Restart required for a reader that could not be safely rebound." : "";
        private void Changed() { try { ownershipChanged?.Invoke(); } catch (Exception ex) { Log("Ownership refresh callback failed: " + ex.Message); } }
        private void SetStatus(string value) { if (status != value) { status = value; Log(value); } }
        private void Log(string text) { try { logger?.Log("Chuff Fix: " + text); } catch { } }
    }
}

namespace DadsToolbox.Chuffs
{
    // Runtime bridge only; the parent remains the sole UMM loader.
    internal static class Main
    {
        internal static bool Enabled;
        internal static Action<string> Logger;
        internal static Action<ChuffClipsSimReader> UpdateNativeLoops;
        internal static readonly HashSet<string> Reported = new HashSet<string>();
        private static readonly Queue<string> Order = new Queue<string>();
        internal static bool Once(string key)
        {
            if (!Reported.Add(key)) return false;
            Order.Enqueue(key); if (Order.Count > 256) Reported.Remove(Order.Dequeue()); return true;
        }
        internal static void ResetDiagnostics() { Reported.Clear(); Order.Clear(); }
        internal static void Log(string message) { try { Logger?.Invoke(message); } catch { } }
        internal static void WarnOnce(ChuffClipsSimReader reader, string kind, string message)
        { string car = CarName(reader); if (Once(kind + ":" + car)) Log("WARNING: " + car + ": " + message); }
        internal static string CarName(ChuffClipsSimReader reader)
        {
            var car = reader ? ReaderAdaptation.CarField.GetValue(reader) as TrainCar : null;
            if (!car && reader) car = reader.GetComponentInParent<TrainCar>();
            return car && car.carLivery ? car.carLivery.id : reader ? reader.transform.root.name : "unknown";
        }
    }
    internal static class OnChuffPatch
    {
        internal static void Prefix(ChuffClipsSimReader __instance, Port ___exhaustPressurePort, ref float chuffIndexFloat)
        {
            if (!Main.Enabled || ___exhaustPressurePort == null) return;
            float pressure = ___exhaustPressurePort.Value;
            if (!(pressure > 1f)) return;
            var clips = pressure < __instance.mediumPressureThreshold ? __instance.lowPressureClips :
                pressure < __instance.highPressureThreshold ? __instance.mediumPressureClips : __instance.highPressureClips;
            if (clips == null || clips.Length == 0) return;
            int n = clips.Length, i = Mathf.RoundToInt(chuffIndexFloat);
            if (i >= 0 && i < n) return;
            int wrapped = i % n; if (wrapped < 0) wrapped += n;
            if (Main.Once("wrap:" + Main.CarName(__instance))) Main.Log(Main.CarName(__instance) + ": chuff " + i + " wrapped onto clip set " + wrapped + " (" + n + " sets). Further wraps for this car type are silent.");
            chuffIndexFloat = wrapped;
        }
    }
}
