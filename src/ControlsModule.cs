using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace DadsToolbox.CclControls
{
    // The parent owns UMM callbacks, settings and dependency events. This
    // adapter owns only the five control patches and the repaired input gate.
    public sealed class ControlsModule
    {
        public const string HarmonyOwner = "jimtaplin.derailvalley.toolbox.cclcontrols";
        public const string StandaloneModId = "DVCCLControlFix";
        public const string DependencyId = "DVCustomCarLoader";
        private static ControlsModule owner;
        // Removing a loaded standalone entry cannot undo its prefab edits.
        // Keep the restart boundary for the entire process, including module
        // recreation, rather than treating removal from UMM as a live migration.
        private static bool standaloneApplied;
        private static readonly Type[] PatchTypes = {
            typeof(DVCCLControlFix.BaseControlsOverrider_Init_Patch),
            typeof(DVCCLControlFix.TrainCar_LoadInterior_Patch),
            typeof(DVCCLControlFix.TrainCar_LoadExternalInteractables_Patch),
            typeof(DVCCLControlFix.HUDStepPatch), typeof(DVCCLControlFix.HUDVisibilityPatch)
        };
        private readonly Action<string> logger;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Harmony harmony;
        private bool parentActive, unloaded, faulted, configured;
        private double nextProbe;
        public bool DesiredEnabled { get; private set; }
        public bool Active { get; private set; }
        public bool RestartRequired { get; private set; }
        public string Status { get; private set; } = "Inactive";

        public ControlsModule(Action<string> logger) { this.logger = logger; }

        public void Configure(bool desired, bool runtimeParentActive)
        {
            bool changed = !configured || DesiredEnabled != desired || parentActive != runtimeParentActive;
            DesiredEnabled = desired; parentActive = runtimeParentActive;
            if (unloaded) { SetStatus("Unloaded; create a new module instance."); return; }
            configured = true;
            if (!changed) return;
            Evaluate();
        }
        public void Tick()
        {
            if (unloaded || clock.Elapsed.TotalSeconds < nextProbe) return;
            nextProbe = clock.Elapsed.TotalSeconds + 1;
            Evaluate();
        }
        public void Refresh()
        {
            if (unloaded) return;
            faulted = false; nextProbe = 0; Evaluate();
        }
        public bool Stop()
        {
            parentActive = false;
            bool success = RemoveOwned();
            if (success) SetStatus("Toolbox disabled" + RestartNotice());
            return success;
        }
        public bool Unload()
        {
            if (unloaded) return harmony == null && owner != this;
            parentActive = false;
            bool success = RemoveOwned();
            if (success) { unloaded = true; SetStatus("Unloaded" + RestartNotice()); }
            return success;
        }
        private void Evaluate()
        {
            try
            {
                if (!parentActive || !DesiredEnabled)
                {
                    if (RemoveOwned()) SetStatus((parentActive ? "Off" : "Toolbox disabled") + RestartNotice());
                    return;
                }
                var standalone = UnityModManager.FindMod(StandaloneModId);
                if ((standalone != null && (standalone.Loaded || standalone.Active)) || HasOwner(StandaloneModId))
                    standaloneApplied = true;
                if (standaloneApplied)
                {
                    RestartRequired = true;
                    if (RemoveOwned()) SetStatus("Blocked: standalone DVCCLControlFix was loaded or patched. Disable/remove it and restart.");
                    return;
                }
                var dependency = UnityModManager.FindMod(DependencyId);
                if (dependency == null || !dependency.Loaded || !dependency.Active)
                {
                    if (RemoveOwned()) SetStatus("Inactive: requires loaded and active CCL." + RestartNotice());
                    return;
                }
                if (faulted) return;
                if (owner == this && harmony != null && !Active && !RemoveOwned()) return;
                if (owner != null && owner != this)
                { SetStatus("Blocked: another Controls module owns this lifetime."); return; }
                if (Active) return;
                if (HasOwner(HarmonyOwner))
                { SetStatus("Blocked: the Controls Harmony owner is already in use."); return; }
                owner = this;
                DVCCLControlFix.Main.Logger = new DVCCLControlFix.ControlsLogger(Log);
                DVCCLControlFix.Main.Mutation = MarkMutation;
                // Inputs remain inert until all exact patch classes install.
                DVCCLControlFix.Main.Enabled = false;
                harmony = new Harmony(HarmonyOwner);
                try
                {
                    foreach (Type patch in PatchTypes) harmony.CreateClassProcessor(patch).Patch();
                    DVCCLControlFix.Main.Enabled = true;
                    Active = true;
                    SetStatus("Active: repairs missing CCL control connections." + RestartNotice());
                }
                catch (Exception ex)
                {
                    Log("Control patch installation failed: " + ex);
                    faulted = true;
                    bool clean = RemoveOwned();
                    SetStatus("Fault: control patch installation failed; " + (clean ? "owned patches removed. " : "cleanup requires attention. ") + ex.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                faulted = true;
                bool clean = RemoveOwned();
                SetStatus("Fault: control dependency check failed; " + (clean ? "inactive. " : "cleanup requires attention. ") + ex.GetType().Name);
            }
        }
        private bool RemoveOwned()
        {
            if (owner != this) return true;
            DVCCLControlFix.Main.Enabled = false;
            Active = false;
            bool success = DVCCLControlFix.ConnectedValueKeyboardInput.ReleaseAll();
            success = DVCCLControlFix.Main.ClearAudits() && success;
            try
            {
                if (harmony != null)
                    foreach (MethodBase method in Harmony.GetAllPatchedMethods().ToArray())
                        if (HasOwner(method, HarmonyOwner)) harmony.Unpatch(method, HarmonyPatchType.All, HarmonyOwner);
                if (HasOwner(HarmonyOwner)) success = false;
            }
            catch (Exception ex) { success = false; Log("Control cleanup failed: " + ex); }
            if (success)
            {
                harmony = null; owner = null;
                DVCCLControlFix.Main.Mutation = null;
                DVCCLControlFix.Main.ResetCaches();
            }
            else SetStatus("Fault: Controls cleanup failed; repaired input is disabled. Restart required.");
            return success;
        }
        private void MarkMutation() { RestartRequired = true; }
        private string RestartNotice() => RestartRequired ? " Restart required to restore already modified prefabs/readers/HUD." : "";
        private static bool HasOwner(string id) => Harmony.GetAllPatchedMethods().Any(m => HasOwner(m, id));
        private static bool HasOwner(MethodBase method, string id)
        {
            var info = Harmony.GetPatchInfo(method);
            return info != null && info.Owners.Contains(id);
        }
        private void Log(string message) { try { logger?.Invoke(message); } catch { } }
        private void SetStatus(string value) { if (Status != value) { Status = value; Log(value); } }
    }
}

