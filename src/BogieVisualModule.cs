using System;
using System.IO;
using System.Runtime.CompilerServices;
using DadsToolbox.BogieVisuals;
using UnityEngine;
using UnityModManagerNet;

namespace DadsToolbox
{
    // Owns only the imported visual runtime. The parent owns UMM callbacks,
    // persistence and GUI; native bogie physics does not depend on CCL.
    public sealed class BogieVisualModule
    {
        private static BogieVisualModule owner;
        private readonly UnityModManager.ModEntry.ModLogger logger;
        private readonly string reportPath;
        private VisualRuntime runtime;
        private GameObject runtimeRoot;
        private bool parentActive, unloaded, faulted;
        private string status = "Off";

        public bool DesiredEnabled { get; private set; }
        public bool Active { get { return !unloaded && !ReferenceEquals(runtime, null) && RuntimeIsActive(runtime); } }
        public string Status { get { return Active ? "Active. " + runtime.Status : status; } }

        public BogieVisualModule(UnityModManager.ModEntry.ModLogger logger, string diagnosticsDirectory)
        {
            this.logger = logger;
            reportPath = string.IsNullOrEmpty(diagnosticsDirectory) ? null :
                Path.Combine(diagnosticsDirectory, "BogieVisualCoverage.txt");
        }

        public void Configure(bool selected, bool toolboxActive)
        {
            if (unloaded) return;
            bool changed = DesiredEnabled != selected || parentActive != toolboxActive;
            DesiredEnabled = selected;
            parentActive = toolboxActive;
            if (changed) faulted = false;
            Reconcile();
        }

        public void Refresh()
        {
            if (unloaded) return;
            faulted = false;
            Reconcile();
            if (Active) runtime.RefreshMapped();
        }

        public void WriteCoverage()
        {
            if (Active) runtime.WriteCoverage();
        }

        public bool Stop()
        {
            parentActive = false;
            return StopRuntime("Toolbox disabled");
        }

        public bool Unload()
        {
            if (unloaded) return true;
            if (!Stop()) return false;
            unloaded = true;
            SetStatus("Unloaded");
            return true;
        }

        private void Reconcile()
        {
            if (unloaded) return;
            if (!parentActive) { StopRuntime("Toolbox disabled"); return; }
            if (!DesiredEnabled) { StopRuntime("Off"); return; }
            try
            {
                if (owner != null && owner != this)
                {
                    StopRuntime("Inactive: another Toolbox bogie runtime owns the visuals.");
                    return;
                }
                if (faulted) return;
                if (Active) return;
                if (!ReferenceEquals(runtime, null) && !StopRuntime("Restarting bogie visuals")) return;
                StartRuntime();
                SetStatus("Active: visual-only bogie smoothing. Native physics and steering unchanged.");
            }
            catch (Exception ex)
            {
                faulted = true;
                StopRuntime("Inactive: bogie visual activation failed: " + ex.GetType().Name + ": " + ex.Message);
                Error(ex.ToString());
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool RuntimeIsActive(VisualRuntime value)
        { return value && value.isActiveAndEnabled; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void StartRuntime()
        {
            owner = this;
            runtimeRoot = new GameObject("Toolbox bogie visual smoothing");
            runtimeRoot.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(runtimeRoot);
            runtime = runtimeRoot.AddComponent<VisualRuntime>();
            runtime.Logger = logger;
            runtime.ReportPath = reportPath;
            runtimeRoot.SetActive(true);
        }

        private bool StopRuntime(string reason)
        {
            try
            {
                if (!ReferenceEquals(runtime, null) || !ReferenceEquals(runtimeRoot, null)) ShutdownRuntime();
                runtime = null;
                runtimeRoot = null;
                if (owner == this) owner = null;
                SetStatus(reason);
                return true;
            }
            catch (Exception ex)
            {
                faulted = true;
                // Retain the runtime/owner so Stop or Unload can retry cleanup.
                SetStatus("Cleanup failed; native restoration requires retry: " + ex.Message);
                Error(ex.ToString());
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ShutdownRuntime()
        {
            // Shutdown explicitly restores native rendering and cancels
            // sampling/events before Unity's deferred object destruction.
            if (runtime) runtime.Shutdown();
            if (runtimeRoot) UnityEngine.Object.Destroy(runtimeRoot);
        }

        private void SetStatus(string value)
        {
            if (status == value) return;
            status = value;
            Log(value);
        }

        private void Log(string value)
        {
            try { if (logger != null) logger.Log("Bogie visuals: " + value); } catch { }
        }

        private void Error(string value)
        {
            try { if (logger != null) logger.Error("Bogie visuals: " + value); } catch { }
        }
    }
}
