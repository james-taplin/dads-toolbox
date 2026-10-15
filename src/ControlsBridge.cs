using System;
using System.Collections.Generic;
namespace DVCCLControlFix
{
    // Existing repair code keeps its small runtime bridge; there is no
    // standalone loader, callback assignment, assembly metadata or PatchAll.
    internal sealed class ControlsLogger
    {
        private readonly Action<string> log;
        internal ControlsLogger(Action<string> log) { this.log = log; }
        public void Log(string text) { try { log?.Invoke(text); } catch { } }
        public void Error(string text) { Log("ERROR: " + text); }
    }
    internal static class Main
    {
        internal static ControlsLogger Logger = new ControlsLogger(null);
        internal static bool Enabled;
        internal static Action Mutation;
        private static readonly HashSet<string> Warnings = new HashSet<string>();
        private static readonly List<Tuple<WeakReference, Action<DV.HUD.InteriorControlsManager>>> Audits =
            new List<Tuple<WeakReference, Action<DV.HUD.InteriorControlsManager>>>();
        internal static void MarkMutation() { Mutation?.Invoke(); }
        internal static void WarnOnce(string key, string text)
        { if (Warnings.Add(key)) Logger.Log("WARNING: " + key + ": " + text); }
        internal static void TrackAudit(DV.HUD.InteriorControlsManager manager, Action<DV.HUD.InteriorControlsManager> handler)
        { Audits.Add(Tuple.Create(new WeakReference(manager), handler)); }
        internal static bool ClearAudits()
        {
            bool success = true;
            for (int i = Audits.Count - 1; i >= 0; --i)
            {
                try
                {
                    var manager = Audits[i].Item1.Target as DV.HUD.InteriorControlsManager;
                    if (manager != null) manager.OnInitialized -= Audits[i].Item2;
                    Audits.RemoveAt(i);
                }
                catch (Exception ex) { success = false; Logger.Error("Control audit cleanup failed: " + ex); }
            }
            return success;
        }
        internal static void ResetCaches() { Warnings.Clear(); KeyboardInputFixer.ResetCaches(); }
    }
}
