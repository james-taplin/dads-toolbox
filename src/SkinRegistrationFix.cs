using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace DadsToolbox
{
    // UMM dispatches listeners synchronously: Skin Manager runs before CCL.
    // Defer only this pack's paint callback until CCL's callback finishes.
    public sealed class SkinRegistrationFix
    {
        public const string HarmonyId = "jimtaplin.derailvalley.toolbox.skinregistration";
        private static SkinRegistrationFix owner;
        private readonly HashSet<UnityModManager.ModEntry> pending = new HashSet<UnityModManager.ModEntry>();
        private readonly HashSet<UnityModManager.ModEntry> completed = new HashSet<UnityModManager.ModEntry>();
        private readonly HashSet<UnityModManager.ModEntry> replaying = new HashSet<UnityModManager.ModEntry>();
        private Harmony harmony;
        private MethodInfo skinCallback;
        private Action<string> logger;
        public bool Active { get; private set; }
        public string Status { get; private set; } = "Inactive";
        public int PendingCount { get { return pending.Count; } }
        public int RecoveredPacks { get; private set; }

        public bool Install(Assembly skinManager, Assembly ccl, Action<string> log)
        {
            if (Active) return true;
            logger = log;
            var skinType = skinManager == null ? null : skinManager.GetType("SkinManagerMod.SkinProvider", false);
            var cclType = ccl == null ? null : ccl.GetType("CCL.Importer.CarManager", false);
            var signature = new[] { typeof(UnityModManager.ModEntry), typeof(bool) };
            var skin = skinType == null ? null : skinType.GetMethod("HandleModToggle", BindingFlags.Public | BindingFlags.Static, null, signature, null);
            var registration = cclType == null ? null : cclType.GetMethod("HandleModToggled", BindingFlags.Public | BindingFlags.Static, null, signature, null);
            if (skinManager == null || ccl == null || skinManager.GetName().Name != "SkinManagerMod" ||
                ccl.GetName().Name != "CCL.Importer" || skin == null || registration == null ||
                skin.ReturnType != typeof(void) || registration.ReturnType != typeof(void))
            {
                Status = "Inactive: incompatible Skin Manager/CCL activation API.";
                return false;
            }
            if (owner != null || HasOwner(skin) || HasOwner(registration))
            {
                Status = "Inactive: skin registration fix already has an owner (duplicate copy).";
                return false;
            }
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.Patch(skin, prefix: new HarmonyMethod(typeof(SkinRegistrationFix), "BeforeSkinActivation"));
                harmony.Patch(registration, finalizer: new HarmonyMethod(typeof(SkinRegistrationFix), "AfterCclActivation"));
                skinCallback = skin;
                owner = this;
                Active = true;
                Status = "Active: embedded CCL skins load after vehicle registration.";
                // LoadAfter cannot order Toolbox ahead of every data pack. One
                // bounded catch-up also recovers packs active before we loaded.
                foreach (var mod in UnityModManager.modEntries.ToArray())
                    if (mod.Active && IsEmbeddedVehiclePack(mod)) Replay(mod);
                return true;
            }
            catch (Exception ex)
            {
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
                if (owner == this) owner = null;
                Active = false;
                pending.Clear(); completed.Clear(); replaying.Clear();
                Status = "Inactive: skin registration guard failed: " + ex.GetType().Name + ": " + ex.Message;
                Log(Status);
                return false;
            }
        }

        public void Uninstall()
        {
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            if (owner == this) owner = null;
            // No delayed job or event subscription survives a disabled module.
            pending.Clear(); completed.Clear(); replaying.Clear();
            skinCallback = null;
            Active = false;
            Status = "Inactive";
        }

        private static bool HasOwner(MethodInfo method)
        {
            var info = Harmony.GetPatchInfo(method);
            return info != null && info.Owners.Contains(HarmonyId);
        }

        private static bool IsEmbeddedVehiclePack(UnityModManager.ModEntry mod)
        {
            // Match the native data-mod filter and CCL's exact bundle filename.
            return mod != null && mod.Info != null && mod.Info.Id != "SkinManagerMod" &&
                string.IsNullOrWhiteSpace(mod.Info.EntryMethod) && !string.IsNullOrEmpty(mod.Path) &&
                Directory.Exists(mod.Path) &&
                Directory.EnumerateFiles(mod.Path, "ccl_bundle", SearchOption.AllDirectories).Any() &&
                Directory.EnumerateFiles(mod.Path, "skin.json", SearchOption.AllDirectories).Any();
        }

        private static bool BeforeSkinActivation(UnityModManager.ModEntry __0, bool __1)
        {
            var current = owner;
            if (current == null || !current.Active || __0 == null || current.replaying.Contains(__0)) return true;
            if (!__1)
            {
                current.pending.Remove(__0);
                current.completed.Remove(__0);
                return true; // Let Skin Manager unload its own skin collection.
            }
            if (!__0.Active) return true;
            try
            {
                if (!IsEmbeddedVehiclePack(__0)) return true;
                // Also tolerate CCL running first without reordering listeners.
                if (current.completed.Remove(__0)) return true;
                current.pending.Add(__0);
                return false;
            }
            catch (Exception ex)
            {
                current.Log("Could not inspect " + __0.Info.Id + ": " + ex.Message);
                return true; // Preserve the original callback and its diagnostics.
            }
        }

        private static Exception AfterCclActivation(UnityModManager.ModEntry __0, bool __1, Exception __exception)
        {
            var current = owner;
            if (current == null || !current.Active || __0 == null) return __exception;
            try
            {
                if (!__1) { current.completed.Remove(__0); return __exception; }
                if (current.pending.Remove(__0))
                {
                    // Failed imports still reach their registration boundary.
                    // Native Skin Manager now reports genuinely unresolved IDs.
                    if (__0.Active) current.Replay(__0);
                }
                else if (__0.Active && IsEmbeddedVehiclePack(__0)) current.completed.Add(__0);
            }
            catch (Exception ex) { current.Log("Registration completion for " + __0.Info.Id + " failed: " + ex.Message); }
            return __exception; // Never suppress CCL's own import exception.
        }

        private void Replay(UnityModManager.ModEntry mod)
        {
            if (!mod.Active || !replaying.Add(mod)) return;
            try
            {
                skinCallback.Invoke(null, new object[] { mod, true });
                RecoveredPacks++;
            }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Log("Skin discovery after registration failed for " + mod.Info.Id + ": " + cause);
            }
            finally { replaying.Remove(mod); }
        }

        private void Log(string text)
        {
            if (logger != null) try { logger(text); } catch { /* A logger must not break another mod's callback. */ }
        }
    }
}
