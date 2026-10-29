using System;
using System.Collections.Generic;
using UnityEngine;
using UnityModManagerNet;
using CCLSmokeFix;
using DadsToolbox.HudGuard;
using DadsToolbox.CclControls;
using DadsToolbox.LocoSound;

namespace DadsToolbox
{
    public sealed class ToolboxSettings : UnityModManager.ModSettings
    {
        public bool HudGuard = true;
        public bool CclControlFix = true;
        public bool LocoSoundPitch = true;
        public bool SmokeFix = true;
        public bool SkinDamageFix = true;
        public bool SkinRegistrationFix = true;
        public bool DoubleTrackSignClearance = true;
        public bool BogieVisualSmoothing = true;
        public bool ChuffFix = true;
        public SignClearanceOptions SignClearance = new SignClearanceOptions();
        public SoundOptions Sound = new SoundOptions();
        public void Normalize()
        {
            if (SignClearance == null) SignClearance = new SignClearanceOptions();
            SignClearance.Clamp();
            if (Sound == null) Sound = new SoundOptions();
            Sound.Normalize();
        }
        public override void Save(UnityModManager.ModEntry mod) { Save(this, mod); }
    }

    public static class Main
    {
        private static UnityModManager.ModEntry entry;
        private static ToolboxSettings settings;
        private static SkinDamageFix skinDamage;
        private static SkinRegistrationFix skinRegistration;
        private static DoubleTrackSigns signs;
        private static HudGuardModule hud;
        private static SmokeFixModule smoke;
        private static ControlsModule controls;
        private static SoundPitchModule sound;
        private static BogieVisualModule bogies;
        private static ChuffFixModule chuffs;
        private static bool enabled, loaded, finalShutdownStarted;
        private static string status, registrationStatus, smokeStatus;
        private static readonly Dictionary<string, string> faults = new Dictionary<string, string>();

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            if (loaded && !OnUnload(entry)) return false;
            entry = modEntry;
            finalShutdownStarted = false;
            settings = UnityModManager.ModSettings.Load<ToolboxSettings>(entry) ?? new ToolboxSettings();
            settings.Normalize();
            faults.Clear();
            skinDamage = new SkinDamageFix();
            skinRegistration = new SkinRegistrationFix();
            signs = new DoubleTrackSigns(settings.SignClearance, text => Log("DoubleTrack signs", text));
            hud = new HudGuardModule(text => Log("HUD Guard", text));
            smoke = new SmokeFixModule();
            controls = new ControlsModule(text => Log("CCL Control Fix", text));
            sound = new SoundPitchModule(settings.Sound, text => Log("Loco Sound Pitch", text),
                entry.Path, SoundConflict);
            bogies = new BogieVisualModule(entry.Logger, entry.Path);
            chuffs = new ChuffFixModule(entry.Logger);
            entry.OnGUI = OnGUI; entry.OnSaveGUI = OnSaveGUI; entry.OnToggle = OnToggle;
            entry.OnUnload = OnUnload; entry.OnUpdate = OnUpdate;
            UnityModManager.toggleModsListen -= OnDependencyToggle;
            UnityModManager.toggleModsListen += OnDependencyToggle;
            enabled = true; loaded = true;
            ApplySettings();
            return true;
        }

        private static void ApplySettings()
        {
            if (!loaded || finalShutdownStarted) return;
            Run("HUD Guard", () => hud.Configure(settings.HudGuard, enabled));
            Run("CCL Control Fix", () => controls.Configure(settings.CclControlFix, enabled));
            Run("Loco Sound Pitch", () => sound.Configure(settings.LocoSoundPitch, enabled));
            Run("CCL Smoke Fix", ApplySmokeSettings);
            Run("Skin damage", ApplySkinSettings);
            Run("Skin registration", ApplyRegistrationSettings);
            Run("DoubleTrack signs", () => signs.Configure(settings.DoubleTrackSignClearance, enabled));
            Run("Bogie Visual Smoothing", () => bogies.Configure(settings.BogieVisualSmoothing, enabled));
            Run("Chuff Fix", () => chuffs.Configure(settings.ChuffFix, enabled));
        }

        private static void ApplySmokeSettings()
        {
            var ccl = UnityModManager.FindMod("DVCustomCarLoader");
            bool eligible = ccl != null && ccl.Loaded && ccl.Active;
            if (enabled && settings.SmokeFix && eligible)
            {
                smoke.Install(text => Log("CCL Smoke Fix", text));
                smokeStatus = null; // Show live module status, including runtime faults.
            }
            else if (!smoke.Stop()) throw new InvalidOperationException(smoke.Status);
            else smokeStatus = !enabled ? "Toolbox disabled" : !settings.SmokeFix ? "Off" : "Inactive: requires loaded and active CCL.";
        }

        private static string SoundConflict()
        {
            var incompatible = UnityModManager.FindMod("ZSounds");
            if (incompatible != null && (incompatible.Loaded || incompatible.Active))
                return "ZSounds is loaded. Disable/remove it and restart before using Toolbox sound pitch.";
            var ccl = UnityModManager.FindMod("DVCustomCarLoader");
            if (ccl == null || !ccl.Loaded || !ccl.Active)
                return "requires loaded and active CCL.";
            return null;
        }

        private static void ApplySkinSettings()
        {
            if (!enabled || !settings.SkinDamageFix)
            { skinDamage.Uninstall(); SetStatus(enabled ? "Off" : "Toolbox disabled"); return; }
            var m = UnityModManager.FindMod("SkinManagerMod");
            if (m == null || !m.Loaded || !m.Active)
            { skinDamage.Uninstall(); SetStatus("Inactive: Skin Manager is absent or disabled."); return; }
            if (m.Version != new Version(4, 4, 1))
            {
                skinDamage.Uninstall();
                SetStatus("Inactive: supported Skin Manager version is 4.4.1; found " + m.Version + ".");
                return;
            }
            skinDamage.Install(m.Assembly, text => Log("Skin damage", text));
            SetStatus(skinDamage.Status);
        }

        private static void ApplyRegistrationSettings()
        {
            var sm = UnityModManager.FindMod("SkinManagerMod");
            var ccl = UnityModManager.FindMod("DVCustomCarLoader");
            string reason = null;
            if (!enabled || !settings.SkinRegistrationFix) reason = enabled ? "Off" : "Toolbox disabled";
            else if (sm == null || !sm.Loaded || !sm.Active || ccl == null || !ccl.Loaded || !ccl.Active)
                reason = "Inactive: requires active Skin Manager and CCL.";
            // The exact callback signatures are checked again by Install;
            // newer CCL versions need not match one exact package version.
            else if (sm.Version != new Version(4, 4, 1) || ccl.Version == null || ccl.Version < new Version(3, 1, 9))
                reason = "Inactive: requires Skin Manager 4.4.1 and CCL 3.1.9 or newer; found Skin Manager " +
                    sm.Version + " and CCL " + (ccl.Version == null ? "unknown" : ccl.Version.ToString()) + ".";
            if (reason != null) skinRegistration.Uninstall();
            else
            {
                skinRegistration.Install(sm.Assembly, ccl.Assembly, text => Log("Skin registration", text));
                reason = skinRegistration.Status;
            }
            if (registrationStatus != reason) { registrationStatus = reason; Log("Skin registration", reason); }
        }

        private static void OnDependencyToggle(UnityModManager.ModEntry mod, bool active)
        {
            if (!loaded || finalShutdownStarted || mod == null || mod.Info == null) return;
            ApplySettings();
            string id = mod.Info.Id;
            if (id == DoubleTrackSigns.DependencyId)
                Run("DoubleTrack signs", () => signs.Refresh());
            if (id == "HeadsUpDisplay_994")
                Run("HUD Guard", () => hud.Refresh());
            if (id == "DVCustomCarLoader")
                Run("CCL Control Fix", () => controls.Refresh());
            if (id == "ZSounds" || id == "DVCustomCarLoader")
                Run("Loco Sound Pitch", () => sound.Refresh());
        }

        private static bool OnToggle(UnityModManager.ModEntry mod, bool value)
        {
            if (!loaded) return !value;
            if (value && finalShutdownStarted)
            {
                Log("Toolbox", "Final unload was attempted; complete cleanup and reload the entry or restart before activating again.");
                return false;
            }
            enabled = value;
            if (!value) return StopAll(false);
            ApplySettings();
            return true;
        }

        private static bool StopAll(bool final)
        {
            // &= evaluates every cleanup even if an earlier module failed.
            bool ok = true;
            ok &= Cleanup("HUD Guard", () => final ? hud.Unload() : hud.Stop());
            ok &= Cleanup("CCL Control Fix", () => final ? controls.Unload() : controls.Stop());
            ok &= Cleanup("Chuff Fix", () => final ? chuffs.Unload() : chuffs.Stop());
            ok &= Cleanup("Loco Sound Pitch", () => final ? sound.Unload() : sound.Stop());
            ok &= Cleanup("Bogie Visual Smoothing", () => final ? bogies.Unload() : bogies.Stop());
            ok &= Cleanup("CCL Smoke Fix", () => smoke.Stop());
            ok &= Cleanup("Skin damage", () => { skinDamage.Uninstall(); SetStatus("Toolbox disabled"); return !skinDamage.Active; });
            ok &= Cleanup("Skin registration", () => { skinRegistration.Uninstall(); registrationStatus = "Toolbox disabled"; return !skinRegistration.Active; });
            ok &= Cleanup("DoubleTrack signs", () => {
                if (final) signs.Unload(); else signs.Configure(settings.DoubleTrackSignClearance, false);
                return !signs.Active;
            });
            return ok;
        }

        private static bool OnUnload(UnityModManager.ModEntry mod)
        {
            if (!loaded) return true;
            finalShutdownStarted = true;
            enabled = false;
            UnityModManager.toggleModsListen -= OnDependencyToggle;
            bool ok = StopAll(true);
            if (ok) loaded = false;
            return ok;
        }

        private static void OnUpdate(UnityModManager.ModEntry mod, float deltaTime)
        {
            if (!loaded || finalShutdownStarted) return;
            Run("HUD Guard", () => { hud.Configure(settings.HudGuard, enabled); hud.Tick(); });
            Run("CCL Control Fix", () => controls.Tick());
            Run("Chuff Fix", () => chuffs.Tick());
            if (enabled)
            {
                Run("DoubleTrack signs", () => signs.Tick(Time.realtimeSinceStartup));
                Run("Loco Sound Pitch", () => sound.Tick(Time.realtimeSinceStartup));
            }
        }

        private static void OnGUI(UnityModManager.ModEntry mod)
        {
            GUILayout.Label("Dad's Toolbox — 1.0.0-rc.4");
            Option(ref settings.HudGuard, "HUD Guard", hud.Status);
            Option(ref settings.CclControlFix, "CCL Control Fix", controls.Status);
            GUILayout.Label("Existing control/prefab changes require restart for complete restoration.");
            Option(ref settings.LocoSoundPitch, "Loco Sound Pitch", sound.Status);
            Run("Loco Sound Pitch", () => { if (sound.DrawOptions()) ApplySettings(); });
            Option(ref settings.ChuffFix, "Chuff Fix", chuffs.Status);
            Option(ref settings.SmokeFix, "CCL Smoke Fix", smokeStatus ?? smoke.Status);
            GUILayout.Label(SmokeFixModule.ReloadNotice);
            Option(ref settings.SkinDamageFix, "Skin Manager: preserve scorched appearance", status, "Skin damage");
            GUILayout.Label("Affects future skin applications. Native repair restores normal appearance.");
            Option(ref settings.SkinRegistrationFix, "Skin Manager: embedded CCL skin registration", registrationStatus, "Skin registration");
            Option(ref settings.DoubleTrackSignClearance, "DoubleTrack sign clearance", signs.Status, "DoubleTrack signs");
            GUILayout.Label("Minimum horizontal centreline clearance: " + settings.SignClearance.MinimumClearance.ToString("0.00") + " m");
            settings.SignClearance.MinimumClearance = GUILayout.HorizontalSlider(settings.SignClearance.MinimumClearance, 1f, 4f);
            GUILayout.Label("Maximum sign movement: " + settings.SignClearance.MaximumMove.ToString("0.0") + " m");
            settings.SignClearance.MaximumMove = GUILayout.HorizontalSlider(settings.SignClearance.MaximumMove, 2f, 12f);
            settings.SignClearance.VerboseLogging = GUILayout.Toggle(settings.SignClearance.VerboseLogging, "Log each corrected sign");
            if (GUILayout.Button("Re-scan DoubleTrack signs")) Run("DoubleTrack signs", () => signs.Refresh());
            GUILayout.Label("Previously moved signs require restart for exact restoration.");
            Option(ref settings.BogieVisualSmoothing, "Bogie Visual Smoothing", bogies.Status);
            GUILayout.Label("Smooths visual copies; native steering, physics and animation remain authoritative.");
            if (GUILayout.Button("Refresh mapped bogie visuals")) Run("Bogie Visual Smoothing", () => bogies.Refresh());
            if (GUILayout.Button("Write bogie coverage report")) Run("Bogie Visual Smoothing", () => bogies.WriteCoverage());
            if (GUILayout.Button("Retry optional features"))
            {
                faults.Clear();
                Run("HUD Guard", () => hud.Refresh());
                Run("CCL Control Fix", () => controls.Refresh());
                Run("Loco Sound Pitch", () => sound.Refresh());
                Run("DoubleTrack signs", () => signs.Refresh());
                Run("Bogie Visual Smoothing", () => bogies.Refresh());
                Run("Chuff Fix", () => chuffs.Refresh());
                ApplySettings();
            }
        }

        private static void Option(ref bool selected, string name, string moduleStatus, string faultKey = null)
        {
            faultKey = faultKey ?? name;
            GUILayout.Space(8);
            bool value = GUILayout.Toggle(selected, name);
            if (value != selected) { selected = value; faults.Remove(faultKey); ApplySettings(); }
            string fault;
            GUILayout.Label(faults.TryGetValue(faultKey, out fault) ? "Faulted: " + fault : moduleStatus ?? "Inactive");
        }

        private static void OnSaveGUI(UnityModManager.ModEntry mod) { settings.Normalize(); settings.Save(mod); }

        private static void Run(string name, Action action)
        {
            if (faults.ContainsKey(name)) return;
            try { action(); }
            catch (Exception ex)
            {
                faults[name] = ex.GetType().Name + ": " + ex.Message;
                Log(name, "Suspended after fault: " + ex);
                // Stop this feature even if its update/activation threw, so a
                // latched fault cannot leave its owned callbacks effective.
                Cleanup(name, () => {
                    switch (name)
                    {
                        case "HUD Guard": return hud.Stop();
                        case "CCL Control Fix": return controls.Stop();
                        case "Loco Sound Pitch": return sound.Stop();
                        case "Bogie Visual Smoothing": return bogies.Stop();
                        case "Chuff Fix": return chuffs.Stop();
                        case "CCL Smoke Fix": return smoke.Stop();
                        case "Skin damage": skinDamage.Uninstall(); return !skinDamage.Active;
                        case "Skin registration": skinRegistration.Uninstall(); return !skinRegistration.Active;
                        case "DoubleTrack signs": signs.Configure(settings.DoubleTrackSignClearance, false); return !signs.Active;
                        default: return true;
                    }
                });
            }
        }
        private static bool Cleanup(string name, Func<bool> action)
        {
            try { bool ok = action(); if (!ok) Log(name, "Cleanup incomplete; retry disabling/unloading."); return ok; }
            catch (Exception ex) { Log(name, "Cleanup failed: " + ex); return false; }
        }
        private static void SetStatus(string value) { if (status != value) { status = value; Log("Skin damage", value); } }
        private static void Log(string feature, string text)
        { try { if (entry != null) entry.Logger.Log(feature + ": " + text); } catch { } }
    }
}
