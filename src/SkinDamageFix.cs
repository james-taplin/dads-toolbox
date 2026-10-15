using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace DadsToolbox
{
    // Optional integration: no compile-time reference to Skin Manager or CCL.
    public sealed class SkinDamageFix
    {
        public const string HarmonyId = "jimtaplin.derailvalley.toolbox.skindamage";
        private Harmony harmony;
        public bool Active { get; private set; }
        public string Status { get; private set; } = "Inactive";

        public bool Install(Assembly skinManager, Action<string> log)
        {
            if (Active) return true;
            if (skinManager == null)
            {
                Status = "Inactive: Skin Manager is unavailable.";
                return false;
            }

            var type = skinManager.GetType("SkinManagerMod.Patches.TrainCarPaintPatches", false);
            var target = type == null ? null : type.GetMethod("ReapplyThemes",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(TrainCar) }, null);
            if (skinManager.GetName().Name != "SkinManagerMod" || target == null ||
                target.ReturnType != typeof(void) || target.ContainsGenericParameters)
            {
                Status = "Inactive: incompatible Skin Manager paint API.";
                return false;
            }

            var existing = Harmony.GetPatchInfo(target);
            if (existing != null && existing.Owners.Contains(HarmonyId))
            {
                Status = "Inactive: this fix already has a patch owner (duplicate copy).";
                return false;
            }

            try
            {
                harmony = new Harmony(HarmonyId);
                var prefix = new HarmonyMethod(typeof(SkinDamageFix).GetMethod(
                    "BeforeReapplyThemes", BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(target, prefix: prefix);
                Active = true;
                Status = "Active: preserves exploded appearance during skin reloads.";
                if (log != null) log(Status);
                return true;
            }
            catch (Exception ex)
            {
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
                Status = "Inactive: paint guard failed: " + ex.GetType().Name + ": " + ex.Message;
                if (log != null) log(Status);
                return false;
            }
        }

        public void Uninstall()
        {
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            Active = false;
            Status = "Inactive";
        }

        private static bool BeforeReapplyThemes(TrainCar __0)
        {
            // DV disables painters, applies scorch, sets this flag, then reloads
            // exploded prefabs. Skin Manager's reload callback ignores the flag.
            // Repair clears it BEFORE reloading repaired prefabs, so normal skin
            // application must be allowed even while root painters remain disabled.
            return __0 != null && !__0.isExploded;
        }
    }
}
