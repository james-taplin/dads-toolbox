using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using HarmonyLib;

namespace DadsToolbox.LocoSound
{
    // Field names intentionally match SteamSizePitch 0.2.0 Settings.xml.
    [Serializable]
    public sealed class SoundOptions
    {
        public float Strength = 1;
        public float MinimumPitch = 0.25f;
        public float MaximumPitch = 2;
        public float NonSteamStrength = 1;
        public float NonSteamMinimumPitch = 0.25f;
        public float NonSteamMaximumPitch = 2;
        public List<DonorOverride> DonorOverrides = new List<DonorOverride>();
        public bool Chuffs = true, Gear = true, Rolling = true, Brakes = true, Cocks = true, Whistle = true;
        public bool ChuffLoops = true;
        public bool AllSounds = false;
        public bool TraceAncillaries = false;
        public int SharedDonor;

        public void Normalize()
        {
            Strength = Safe(Strength, 1, 0, 10);
            MinimumPitch = Safe(MinimumPitch, 0.25f, 0.1f, 1);
            MaximumPitch = Safe(MaximumPitch, 2, 1, 3);
            NonSteamStrength = Safe(NonSteamStrength, 1, 0, 10);
            NonSteamMinimumPitch = Safe(NonSteamMinimumPitch, 0.25f, 0.1f, 1);
            NonSteamMaximumPitch = Safe(NonSteamMaximumPitch, 2, 1, 3);
            SharedDonor = Math.Max(0, Math.Min(2, SharedDonor));
            if (DonorOverrides == null) DonorOverrides = new List<DonorOverride>();
        }

        private static float Safe(float value, float fallback, float minimum, float maximum) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(minimum, Math.Min(maximum, value));

        public static bool TryReadLegacySettings(string path, out SoundOptions options, out string reason)
        {
            options = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            { reason = "No standalone sound settings file was found."; return false; }
            try
            {
                var serializer = new XmlSerializer(typeof(SoundOptions), new XmlRootAttribute("Settings"));
                using (var stream = File.OpenRead(path)) options = (SoundOptions)serializer.Deserialize(stream);
                if (options == null) throw new InvalidDataException("The sound settings file is empty.");
                options.Normalize();
                reason = "Imported standalone sound settings; the original file was preserved.";
                return true;
            }
            catch (Exception ex)
            { options = null; reason = "Could not import standalone sound settings: " + ex.Message; return false; }
        }
    }

    [Serializable]
    public sealed class DonorOverride
    {
        public string LiveryId;
        public string DonorId;
    }

    // Sole parent owns UMM callbacks, the shared settings writer and update dispatch.
    public sealed class SoundPitchModule
    {
        public const string Owner = "jimtaplin.derailvalley.toolbox.locosoundpitch";
        public const string StandaloneId = "SteamSizePitch";
        private static SoundPitchModule current;
        private readonly SoundOptions options;
        private readonly Action<string> logger;
        private readonly string diagnosticsDirectory;
        private readonly Func<string> conflictReason;
        private Harmony harmony;
        private bool parentActive;
        private bool unloaded;
        private bool faulted;
        private bool compatibilityChecked;
        private string lastAvailability;
        private float nextTick;
        private string status = "Off";
        public bool DesiredEnabled { get; private set; }
        public bool Active { get; private set; }
        public string Status => Active ? "Active. " + SoundEngine.Status : status;

        public SoundPitchModule(SoundOptions options, Action<string> logger,
            string diagnosticsDirectory, Func<string> conflictReason)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger;
            this.diagnosticsDirectory = diagnosticsDirectory;
            this.conflictReason = conflictReason;
            options.Normalize();
        }

        public void Configure(bool desired, bool actualParentActive)
        {
            if (unloaded) return;
            DesiredEnabled = desired;
            parentActive = actualParentActive;
            Reconcile(true);
        }

        private void Reconcile(bool validateOwnership)
        {
            if (!DesiredEnabled || !parentActive)
            {
                compatibilityChecked = false;
                if (StopCore()) SetStatus(parentActive ? "Off" : "Toolbox disabled");
                return;
            }
            if (faulted) return;
            string availability = Availability();
            // Poll cheap dependency state, not Harmony's entire patch registry.
            // Stable waiting/blocked states also need no repeated cleanup sweep.
            if (!validateOwnership && compatibilityChecked && availability == lastAvailability) return;
            lastAvailability = availability;
            compatibilityChecked = true;
            string conflict = availability ?? ResidualConflict();
            if (conflict != null)
            { if (StopCore()) SetStatus("Inactive: " + conflict); return; }
            if (Active) return;
            Install();
        }

        public void Tick(float realtime)
        {
            if (unloaded || !DesiredEnabled || !parentActive || faulted) return;
            if (realtime >= nextTick)
            {
                nextTick = realtime + 0.25f;
                Reconcile(false);
                if (Active) SoundEngine.Tick();
            }
            if (Active) SoundEngine.ProcessPending();
        }

        public void Refresh()
        {
            if (unloaded) return;
            faulted = false;
            Configure(DesiredEnabled, parentActive);
            if (Active) { SoundEngine.Refresh(); SoundEngine.RequestScan(); }
        }

        public bool DrawOptions()
        {
            if (unloaded || (current != null && current != this)) return false;
            if (current == null)
            {
                // Options remain available when the feature is off or blocked.
                // This creates no Unity objects or patches and requests no scan.
                SoundEngine.Initialize(options, Log, diagnosticsDirectory);
                current = this;
            }
            bool changed = SoundEngine.Draw();
            if (changed) options.Normalize();
            return changed;
        }

        private void Install()
        {
            if (current != null && current != this)
            { SetStatus("Inactive: another sound module instance owns the audio engine."); return; }
            if (OwnedPatches())
            { SetStatus("Inactive: the sound module Harmony owner is already in use. Restart required."); return; }
            current = this;
            try
            {
                // A stopped instance retains ownership records, including relinquished
                // direct sources, until final unload. Re-enabling must not adopt them.
                if (harmony == null)
                {
                    SoundEngine.Initialize(options, Log, diagnosticsDirectory);
                    harmony = new Harmony(Owner);
                }
                foreach (var patch in PatchClasses) harmony.CreateClassProcessor(patch).Patch();
                SoundEngine.Enabled = true;
                SoundEngine.RequestScan();
                Active = true;
                nextTick = 0;
                SetStatus("Active");
            }
            catch (Exception ex)
            {
                faulted = true;
                SoundEngine.Enabled = false;
                bool cleaned = RemoveOwnedPatches();
                Active = false;
                SetStatus("Failed to start sound pitch: " + ex.Message + (cleaned ? "" : "; patch cleanup failed, restart required."));
            }
        }

        private static readonly Type[] PatchClasses = {
            typeof(LayerPitchPatch), typeof(OneShotPatch), typeof(SetupPatch),
            typeof(PoolPatch), typeof(ContextPatch), typeof(EffectSpawnPatch)
        };

        public bool Stop()
        {
            // A parent stop revokes runtime activation without changing the
            // saved preference. Refresh/Tick cannot re-enable until the parent
            // explicitly configures this lifetime active again.
            parentActive = false;
            return StopCore();
        }

        private bool StopCore()
        {
            if (unloaded) return true;
            if (current != this) { Active = false; return true; }
            bool wasActive = Active;
            bool restored = true;
            // Restore while our Layer.SetPitch prefix still records/writes its
            // native baseline. Pooled one-shots finish with their starting pitch.
            SoundEngine.Enabled = false;
            try { SoundEngine.Refresh(); }
            catch (Exception ex) { restored = false; Log("Audio restoration failed: " + ex); }
            bool removed = RemoveOwnedPatches();
            Active = false;
            if (!restored || !removed)
            {
                faulted = true;
                SetStatus("Sound cleanup failed; restart required. See UMM log.");
            }
            else if (wasActive) SetStatus("Off");
            return restored && removed;
        }

        public bool Unload()
        {
            if (unloaded) return true;
            bool cleaned = Stop();
            if (!cleaned) return false;
            if (current == this)
            {
                SoundEngine.Release();
                current = null;
            }
            harmony = null;
            unloaded = true;
            SetStatus("Unloaded");
            return true;
        }

        private string Availability()
        {
            try
            {
                if (conflictReason == null) return "sound compatibility probe is unavailable.";
                string reason = conflictReason();
                return string.IsNullOrEmpty(reason) ? null : reason;
            }
            catch (Exception ex) { return "sound compatibility check failed: " + ex.Message; }
        }

        private string ResidualConflict()
        {
            try
            {
                if (Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(StandaloneId) == true))
                    return "standalone SteamSizePitch patches remain loaded. Disable/remove it and restart.";
                return null;
            }
            catch (Exception ex) { return "sound compatibility check failed: " + ex.Message; }
        }

        private static bool OwnedPatches() => Harmony.GetAllPatchedMethods()
            .Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true);

        private bool RemoveOwnedPatches()
        {
            if (harmony == null) return !OwnedPatches();
            try
            {
                foreach (var method in Harmony.GetAllPatchedMethods().ToArray())
                    if (Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true)
                        harmony.Unpatch(method, HarmonyPatchType.All, Owner);
                return !OwnedPatches();
            }
            catch (Exception ex) { Log("Audio patch cleanup failed: " + ex); return false; }
        }

        private void SetStatus(string value) { if (status != value) { status = value; Log(value); } }
        private void Log(string value) { try { logger?.Invoke(value); } catch { } }
    }
}
