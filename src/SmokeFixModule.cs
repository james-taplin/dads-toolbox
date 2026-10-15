using System;
using System.Linq;
using System.Reflection;
using DV.Simulation.Controllers;
using HarmonyLib;

namespace CCLSmokeFix
{
    // Compile this file and SmokeColorFix.cs into the parent assembly. The parent
    // owns persistence, UMM callbacks and UI; this object owns only the smoke patch.
    public sealed class SmokeFixModule
    {
        public const string HarmonyId = "jimtaplin.derailvalley.toolbox.smoke";
        public const string StandaloneModId = "CCLSmokeFix";
        public const string ReloadNotice = "Applies when locomotives spawn/load. Previously corrected locomotives retain their values until reload/restart.";

        private readonly string ownerId;
        private Harmony harmony;
        private MethodInfo target;
        private Action<string> logger;
        private Func<bool> standaloneIsActive;
        internal static SmokeFixModule Current { get; private set; }

        public bool Active { get; private set; }
        public bool RequiresReloadForRestoration { get; private set; }
        public string Status { get; private set; } = "Off";

        public SmokeFixModule() : this(HarmonyId) { }

        // The existing standalone host shares the correction, but keeps its own ID.
        internal SmokeFixModule(string ownerId) { this.ownerId = ownerId; }

        public bool Install(Action<string> log, Func<bool> isStandaloneActive)
        {
            logger = log;
            standaloneIsActive = isStandaloneActive;
            if (Current != null && !ReferenceEquals(Current, this))
            {
                SetStatus("Inactive: another smoke module instance already owns this assembly's patch.");
                return false;
            }

            try
            {
                target = AccessTools.Method(typeof(ParticlesPortReadersController), nameof(ParticlesPortReadersController.Init));
                if (target == null) throw new MissingMethodException("ParticlesPortReadersController.Init");
                if (HasStandaloneConflict())
                {
                    Stop("Inactive: standalone CCL Smoke Fix is active or still patched. Disable/remove it and restart before enabling Toolbox smoke.");
                    return false;
                }
                if (Active) return true;
                if (Harmony.GetPatchInfo(target)?.Owners.Contains(ownerId) == true)
                {
                    SetStatus("Inactive: this smoke patch owner is already registered by another copy.");
                    return false;
                }

                harmony = new Harmony(ownerId);
                Current = this;
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(SmokeColorFix).GetMethod(
                    nameof(SmokeColorFix.Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
                Active = true;
                TemplateAvailable();
                return true;
            }
            catch (Exception ex)
            {
                Fault(ex);
                return false;
            }
        }

        public void Uninstall()
        {
            Stop();
        }

        // The combined parent must be able to reject an unsuccessful shutdown.
        // Preserve Uninstall for the original module contract and fixtures.
        public bool Stop()
        { return Stop(RequiresReloadForRestoration ? "Off. " + ReloadNotice : "Off"); }

        internal bool CanApply()
        {
            if (!Active || !ReferenceEquals(Current, this)) return false;
            try
            {
                // Also guard the next Init if a standalone mod activates before the
                // parent's dependency callback has refreshed this module.
                if (!HasStandaloneConflict()) return true;
                Stop("Inactive: standalone CCL Smoke Fix became active. Toolbox smoke stopped.");
            }
            catch (Exception ex) { Fault(ex); }
            return false;
        }

        private bool HasStandaloneConflict()
        {
            if (ownerId == StandaloneModId) return false;
            if (standaloneIsActive == null)
                throw new ArgumentNullException(nameof(standaloneIsActive), "The parent must supply the standalone mod's active state.");
            return standaloneIsActive() || Harmony.GetPatchInfo(target)?.Owners.Contains(StandaloneModId) == true;
        }

        private bool Stop(string status)
        {
            // Gate callbacks before unpatching. Even a removal failure leaves the
            // prefix inert and never disables a different module's Harmony owner.
            Active = false;
            if (ReferenceEquals(Current, this)) Current = null;
            try
            {
                if (harmony != null && target != null)
                    harmony.Unpatch(target, HarmonyPatchType.Prefix, ownerId);
                harmony = null;
            }
            catch (Exception ex)
            {
                SetStatus("Inactive: smoke patch removal failed: " + ex.Message);
                Log(ex.ToString());
                return false;
            }
            SetStatus(status);
            return true;
        }

        internal void Fault(Exception ex)
        {
            Stop("Inactive: smoke correction failed: " + ex.GetType().Name + ": " + ex.Message);
            Log(ex.ToString());
        }

        internal void WaitingForTemplate()
        {
            SetStatus("On: waiting for the vanilla S282 smoke template; retrying at the next particle initialization.");
        }

        internal void TemplateAvailable() { SetStatus("On: corrects CCL stack smoke on spawn/load."); }
        internal void ReaderCorrected() { RequiresReloadForRestoration = true; }

        private void SetStatus(string value)
        {
            if (Status == value) return;
            Status = value;
            Log(value);
        }

        internal void Log(string value)
        {
            // A host logging failure must not interrupt the game's particle Init.
            try { logger?.Invoke(value); }
            catch { }
        }
    }
}
