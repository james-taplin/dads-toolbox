using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace DadsToolbox.H6F
{
    public enum H6FState
    {
        Disabled, WaitingForH6F, WaitingForCcl, IncompatibleCcl,
        BlockedByStandalone, BlockedByOtherInstance, Armed, Enabled, Faulted, Unloaded
    }

    // Independent Toolbox submod. The parent owns its H6FRepair setting and
    // dispatches Configure/Tick/Stop/Unload just as for the separate HUD module.
    public sealed class H6FModule
    {
        public const string H6FModId = "em_h6f_main";
        public const string CclModId = "DVCustomCarLoader";
        public const string HarmonyId = "jimtaplin.derailvalley.toolbox.h6f";
        private const string LegacyId = "DVRollingStockCompatibility";
        private const string LegacyOwner = "jimtaplin.derailvalley.rollingstockcompatibility";
        private static readonly MethodInfo Prefix = typeof(H6FModule).GetMethod("BeforeAddTrainsets", BindingFlags.Static | BindingFlags.NonPublic);
        private static H6FModule owner;
        private static bool metadataChanged;
        private readonly Action<string> logger;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long nextProbe;
        private bool parentActive, unloaded, faulted, loggedRepair, listening;
        private Harmony harmony;
        private MethodInfo target;
        private Type carType, variantType;
        private MemberInfo carId, variantId, variants;
        private FieldInfo trainset;

        public H6FModule(Action<string> log)
        { logger = log; State = H6FState.Disabled; Status = "Disabled."; }

        public bool DesiredEnabled { get; private set; }
        public H6FState State { get; private set; }
        public string Status { get; private set; }
        public int RepairedEntryCount { get; private set; }
        public bool RestartRequired { get { return metadataChanged; } }

        public void Configure(bool desiredEnabled, bool toolboxActive)
        {
            if (unloaded || (DesiredEnabled == desiredEnabled && parentActive == toolboxActive)) return;
            DesiredEnabled = desiredEnabled; parentActive = toolboxActive; faulted = false;
            Reconcile();
        }
        public void Tick()
        { if (!unloaded && clock.ElapsedMilliseconds >= nextProbe) Reconcile(); }
        public void Refresh()
        { if (!unloaded) { faulted = false; Reconcile(); } }
        public bool Stop()
        { if (unloaded) return true; parentActive = false; Reconcile(); return target == null; }
        public bool Unload()
        {
            if (unloaded) return true;
            if (!Stop()) return false;
            unloaded = true; SetState(H6FState.Unloaded, "Unloaded; future H6F repairs stopped."); return true;
        }

        private static UnityModManager.ModEntry H6FEntry()
        { return UnityModManager.FindMod(H6FModId); }
        private static bool Active(UnityModManager.ModEntry entry)
        { return entry != null && entry.Loaded && entry.Active; }

        private void Reconcile()
        {
            nextProbe = clock.ElapsedMilliseconds + 1000;
            try
            {
                if (!DesiredEnabled || !parentActive)
                {
                    Listen(false);
                    Deactivate(H6FState.Disabled, metadataChanged ?
                        "Disabled; existing H6F metadata corrections need restart to undo." : "Disabled."); return;
                }
                // This is the first dependency probe. If H6F is absent/disabled,
                // do not resolve CCL types, create Harmony or inspect car data.
                UnityModManager.ModEntry h6f = H6FEntry();
                if (h6f == null || (!Active(h6f) && (!h6f.Enabled || h6f.Loaded || h6f.ErrorOnLoading)))
                {
                    Listen(false);
                    Deactivate(H6FState.WaitingForH6F, h6f == null ? "H6F is not installed." : "H6F is inactive or unavailable."); return;
                }
                // Use UMM's supported activation event so CCL startup need not
                // wait for the first game frame before this hook can be armed.
                Listen(true);
                UnityModManager.ModEntry ccl = UnityModManager.FindMod(CclModId);
                if (!Active(ccl)) { Deactivate(H6FState.WaitingForCcl, "Custom Car Loader is absent or inactive."); return; }
                UnityModManager.ModEntry legacy = UnityModManager.FindMod(LegacyId);
                if (Active(legacy))
                { Deactivate(H6FState.BlockedByStandalone, "Rolling Stock Compatibility already supplies H6F repair."); return; }
                MethodInfo method = Bind(ccl.Assembly);
                if (method == null)
                { Deactivate(H6FState.IncompatibleCcl, "Custom Car Loader's trainset API is incompatible."); return; }
                Patches info = Harmony.GetPatchInfo(method);
                if (info != null) foreach (Patch patch in info.Prefixes)
                {
                    Type declaring = patch.PatchMethod.DeclaringType;
                    if (patch.owner == LegacyOwner || (declaring != null && declaring.FullName == "DVRollingStockCompatibility.H6FFix"))
                    { Deactivate(H6FState.BlockedByStandalone, "A standalone H6F repair remains loaded; restart after migration."); return; }
                }
                if (owner != null && owner != this)
                { SetState(H6FState.BlockedByOtherInstance, "Another H6F submod instance is active."); return; }
                if (faulted)
                {
                    if (RemovePatch()) SetState(H6FState.Faulted, "H6F repair is stopped after an error; toggle off/on or Refresh to retry.");
                    return;
                }
                if (target != method || !HasOwnPatch(method))
                {
                    if (!RemovePatch()) return;
                    if (Harmony.HasAnyPatches(HarmonyId))
                    { SetState(H6FState.BlockedByOtherInstance, "The H6F module's Harmony ID is already in use."); return; }
                    if (harmony == null) harmony = new Harmony(HarmonyId);
                    target = method; owner = this; loggedRepair = false;
                    try
                    {
                        harmony.Patch(target, prefix: new HarmonyMethod(Prefix));
                        if (!HasOwnPatch(target)) throw new InvalidOperationException("H6F prefix was not registered.");
                    }
                    catch (Exception ex)
                    {
                        RemovePatch(); faulted = true;
                        SetState(H6FState.Faulted, "H6F patch installation failed: " + ex.GetType().Name + ": " + ex.Message); return;
                    }
                }
                SetState(Active(h6f) ? H6FState.Enabled : H6FState.Armed,
                    Active(h6f) ? "Enabled for H6F trainset registration; existing imports may require restart." :
                    "H6F is installed and enabled; repair is armed for its activation.");
            }
            catch (Exception ex)
            {
                RemovePatch(); faulted = true;
                SetState(H6FState.Faulted, "H6F module failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void Listen(bool enabled)
        {
            if (listening == enabled) return;
            if (enabled) UnityModManager.toggleModsListen += DependencyToggled;
            else UnityModManager.toggleModsListen -= DependencyToggled;
            listening = enabled;
        }
        private void DependencyToggled(UnityModManager.ModEntry entry, bool active)
        {
            if (unloaded || entry == null || entry.Info == null) return;
            if (entry.Info.Id != H6FModId && entry.Info.Id != CclModId) return;
            faulted = false;
            Reconcile();
        }

        private MethodInfo Bind(Assembly assembly)
        {
            if (assembly == null) return null;
            Type manager = assembly.GetType("CCL.Importer.CarManager", false);
            Type car = assembly.GetType("CCL.Importer.Types.CCL_CarType", false);
            Type variant = assembly.GetType("CCL.Importer.Types.CCL_CarVariant", false);
            if (manager == null || car == null || variant == null) return null;
            MethodInfo method = manager.GetMethod("AddTrainsets", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new Type[] { car }, null);
            if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters) return null;
            MemberInfo idCar = Readable(car, "id", typeof(string));
            MemberInfo idVariant = Readable(variant, "id", typeof(string));
            PropertyInfo list = car.GetProperty("Variants", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo ids = variant.GetField("TrainsetLiveries", BindingFlags.Instance | BindingFlags.Public);
            if (idCar == null || idVariant == null || list == null || !list.CanRead || list.GetIndexParameters().Length != 0 ||
                !typeof(IEnumerable).IsAssignableFrom(list.PropertyType) || ids == null || ids.FieldType != typeof(string[])) return null;
            carType = car; variantType = variant; carId = idCar; variantId = idVariant; variants = list; trainset = ids;
            return method;
        }

        private static MemberInfo Readable(Type type, string name, Type value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            FieldInfo field = type.GetField(name, flags);
            if (field != null && field.FieldType == value) return field;
            PropertyInfo property = type.GetProperty(name, flags);
            return property != null && property.CanRead && property.PropertyType == value && property.GetIndexParameters().Length == 0 ? property : null;
        }
        private static object Read(MemberInfo member, object instance)
        {
            FieldInfo field = member as FieldInfo;
            return field != null ? field.GetValue(instance) : ((PropertyInfo)member).GetValue(instance, null);
        }

        private static void BeforeAddTrainsets(object __0)
        {
            H6FModule current = owner;
            // Recheck real runtime state on every target invocation, so a
            // dependency deactivation cannot race the one-second unpatch probe.
            if (current == null || current.faulted || !current.parentActive || !current.DesiredEnabled) return;
            try
            {
                if (!Active(H6FEntry()) || !Active(UnityModManager.FindMod(CclModId))) return;
                current.Repair(__0);
            }
            catch (Exception ex)
            {
                current.faulted = true;
                current.SetState(H6FState.Faulted, "H6F metadata repair failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void Repair(object car)
        {
            if (car == null || !carType.IsInstanceOfType(car) || !String.Equals(Read(carId, car) as string, H6FModId, StringComparison.Ordinal)) return;
            IEnumerable liveries = Read(variants, car) as IEnumerable;
            if (liveries == null) return;
            bool changed = false;
            foreach (object variant in liveries)
            {
                if (variant == null || !variantType.IsInstanceOfType(variant) ||
                    !String.Equals(Read(variantId, variant) as string, H6FModId, StringComparison.Ordinal)) continue;
                string[] ids = trainset.GetValue(variant) as string[];
                if (ids == null) continue;
                for (int i = 0; i < ids.Length; i++)
                    if (String.Equals(ids[i], "h6f_main", StringComparison.Ordinal))
                    { ids[i] = H6FModId; changed = true; RepairedEntryCount++; }
            }
            if (!changed) return;
            metadataChanged = true;
            if (!loggedRepair)
            {
                loggedRepair = true;
                Log("H6F: corrected runtime TrainsetLiveries 'h6f_main' to 'em_h6f_main'. Existing corrections need restart to undo.");
            }
        }

        private static bool HasOwnPatch(MethodInfo method)
        {
            if (method == null) return false;
            Patches info = Harmony.GetPatchInfo(method);
            if (info != null) foreach (Patch patch in info.Prefixes)
                if (patch.owner == HarmonyId && patch.PatchMethod == Prefix) return true;
            return false;
        }
        private bool RemovePatch()
        {
            if (target != null)
            {
                try
                {
                    harmony.Unpatch(target, HarmonyPatchType.Prefix, HarmonyId);
                    if (HasOwnPatch(target)) throw new InvalidOperationException("H6F prefix remains registered.");
                    target = null;
                }
                catch (Exception ex)
                { SetState(H6FState.Faulted, "H6F cleanup failed: " + ex.GetType().Name + ": " + ex.Message); return false; }
            }
            if (owner == this) owner = null;
            return true;
        }
        private void Deactivate(H6FState state, string status)
        { if (RemovePatch()) SetState(state, status); }
        private void SetState(H6FState state, string status)
        { if (State == state && Status == status) return; State = state; Status = status; Log("H6F repair: " + status); }
        private void Log(string message)
        { try { if (logger != null) logger(message); } catch { } }
    }
}
