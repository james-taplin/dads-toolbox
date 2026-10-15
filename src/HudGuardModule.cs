using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityModManagerNet;

namespace DadsToolbox.HudGuard
{
    public enum HudGuardState
    {
        Disabled, WaitingForHud, IncompatibleHud, BlockedByStandalone,
        BlockedByOtherInstance, Enabled, Faulted, Unloaded
    }

    // The parent owns settings and UMM callbacks. This module never assigns
    // callbacks, saves settings, changes another ModEntry or invokes PatchAll.
    // Call Configure on option/parent changes, Tick from OnUpdate, Stop from
    // OnToggle(false), and Unload from OnUnload. All calls are on the main thread.
    public sealed class HudGuardModule
    {
        public const string HarmonyId = "jimtaplin.derailvalley.toolbox.hudguard";
        public const string HudModId = "HeadsUpDisplay_994";
        public const string StandaloneModId = "DVRollingStockCompatibility";
        private const string StandaloneOwner = "jimtaplin.derailvalley.rollingstockcompatibility";
        private static readonly MethodInfo Finalizer = typeof(HudGuardModule).GetMethod(
            "ProviderFinalizer", BindingFlags.NonPublic | BindingFlags.Static);
        private static HudGuardModule owner;
        private readonly Harmony harmony = new Harmony(HarmonyId);
        private readonly Action<string> log;
        private readonly List<MethodInfo> patched = new List<MethodInfo>();
        private readonly HashSet<MethodBase> loggedProviders = new HashSet<MethodBase>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long nextProbe;
        private bool toolboxActive, unloaded;
        private Assembly installedAssembly, faultAssembly;

        public HudGuardModule(Action<string> logger)
        {
            log = logger;
            State = HudGuardState.Disabled;
            Status = "Disabled.";
        }

        public bool DesiredEnabled { get; private set; }
        public HudGuardState State { get; private set; }
        public string Status { get; private set; }
        public int PatchedProviderCount { get { return patched.Count; } }

        // Pass the parent's saved preference; dependency failures never change it.
        public void Configure(bool desiredEnabled, bool parentActive)
        {
            if (unloaded) return;
            if (DesiredEnabled == desiredEnabled && toolboxActive == parentActive) return;
            DesiredEnabled = desiredEnabled;
            toolboxActive = parentActive;
            faultAssembly = null;
            Reconcile();
        }

        // Poll once per real second, including while game time is paused.
        public void Tick()
        {
            if (unloaded || clock.ElapsedMilliseconds < nextProbe) return;
            Reconcile();
        }

        // Useful immediately after a dependency change or an explicit Retry.
        public void Refresh()
        {
            if (unloaded) return;
            faultAssembly = null;
            Reconcile();
        }

        // Return false if cleanup failed, so the parent can reject deactivation.
        public bool Stop()
        {
            if (unloaded) return true;
            toolboxActive = false;
            Reconcile();
            return patched.Count == 0;
        }

        public bool Unload()
        {
            if (unloaded) return true;
            if (!Stop()) return false;
            unloaded = true;
            SetState(HudGuardState.Unloaded, "Unloaded; HUD Guard patches removed.");
            return true;
        }

        private void Reconcile()
        {
            nextProbe = clock.ElapsedMilliseconds + 1000;
            try
            {
                if (!DesiredEnabled || !toolboxActive)
                {
                    faultAssembly = null;
                    Deactivate(HudGuardState.Disabled, !DesiredEnabled ? "Disabled by option." : "Toolbox is inactive.");
                    return;
                }
                UnityModManager.ModEntry hud = UnityModManager.FindMod(HudModId);
                if (hud == null || !hud.Loaded || !hud.Active)
                {
                    faultAssembly = null;
                    Deactivate(HudGuardState.WaitingForHud, hud == null ? "HeadsUpDisplay is absent." :
                        !hud.Loaded ? "HeadsUpDisplay has not loaded." : "HeadsUpDisplay is inactive.");
                    return;
                }
                UnityModManager.ModEntry standalone = UnityModManager.FindMod(StandaloneModId);
                if (standalone != null && standalone.Loaded && standalone.Active)
                {
                    faultAssembly = null;
                    Deactivate(HudGuardState.BlockedByStandalone,
                        "Rolling Stock Compatibility is active and already supplies the HUD guard. It also contains H6F; arrange migration in the parent Toolbox.");
                    return;
                }
                Assembly hudAssembly = hud.Assembly;
                Type providersType = hudAssembly == null ? null : hudAssembly.GetType("DvMod.HeadsUpDisplay.LocoProviders", false);
                List<MethodInfo> providers = FindProviderMethods(providersType);
                if (providers.Count == 0)
                {
                    Deactivate(HudGuardState.IncompatibleHud, "HeadsUpDisplay has no supported locomotive provider methods.");
                    return;
                }
                Type car = Type.GetType("TrainCar, Assembly-CSharp", false);
                foreach (MethodInfo method in providers)
                {
                    if (!HasQueryContract(hudAssembly, method.ReturnType, car))
                    {
                        Deactivate(HudGuardState.IncompatibleHud, "HeadsUpDisplay's query API is incompatible; no provider guards installed.");
                        return;
                    }
                }
                foreach (MethodInfo method in providers)
                {
                    Patches info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;
                    foreach (Patch patch in info.Finalizers)
                    {
                        Type declaring = patch.PatchMethod.DeclaringType;
                        if (patch.owner == StandaloneOwner || (declaring != null &&
                            declaring.FullName == "DVRollingStockCompatibility.HudLocoProviderFix"))
                        {
                            Deactivate(HudGuardState.BlockedByStandalone,
                                "A standalone HUD guard patch remains loaded. Restart after arranging migration; its patches were left intact.");
                            return;
                        }
                        if (patch.PatchMethod == Finalizer && patch.owner != HarmonyId)
                        {
                            Deactivate(HudGuardState.BlockedByOtherInstance,
                                "This HUD Guard finalizer is registered by another owner; its patches were left intact.");
                            return;
                        }
                    }
                }
                if (owner != null && owner != this)
                {
                    SetState(HudGuardState.BlockedByOtherInstance, "Another HUD Guard instance is active.");
                    return;
                }
                if (State == HudGuardState.Faulted && faultAssembly == hudAssembly) return;
                if (installedAssembly == hudAssembly && PatchedExactly(providers))
                {
                    SetState(HudGuardState.Enabled, "Enabled; " + patched.Count + " HUD providers guarded. Applies immediately.");
                    return;
                }
                if (!RemovePatches()) return;
                if (Harmony.HasAnyPatches(HarmonyId))
                {
                    SetState(HudGuardState.BlockedByOtherInstance, "The HUD Guard Harmony ID is already in use; no patches changed.");
                    return;
                }
                if (InstallProviders(providers))
                {
                    installedAssembly = hudAssembly;
                    faultAssembly = null;
                    SetState(HudGuardState.Enabled, "Enabled; " + patched.Count + " HUD providers guarded. Applies immediately.");
                }
                else faultAssembly = hudAssembly;
            }
            catch (Exception ex)
            {
                RemovePatches();
                SetState(HudGuardState.Faulted, "HUD Guard failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void Deactivate(HudGuardState state, string status)
        {
            if (RemovePatches()) SetState(state, status);
        }

        private bool PatchedExactly(List<MethodInfo> providers)
        {
            if (patched.Count != providers.Count) return false;
            foreach (MethodInfo method in providers)
            {
                if (!patched.Contains(method) || !HasOwnPatch(method)) return false;
            }
            return true;
        }

        private static bool HasOwnPatch(MethodInfo method)
        {
            Patches info = Harmony.GetPatchInfo(method);
            if (info == null) return false;
            foreach (Patch patch in info.Finalizers)
                if (patch.owner == HarmonyId && patch.PatchMethod == Finalizer) return true;
            return false;
        }

        private bool InstallProviders(List<MethodInfo> providers)
        {
            owner = this;
            lock (loggedProviders) loggedProviders.Clear();
            try
            {
                foreach (MethodInfo method in providers)
                {
                    // Track before Patch: Harmony can record metadata even when
                    // wrapper generation throws. Cleanup must include that target.
                    patched.Add(method);
                    harmony.Patch(method, finalizer: new HarmonyMethod(Finalizer));
                    if (!HasOwnPatch(method)) throw new InvalidOperationException("Patch not registered: " + method.Name);
                }
                return true;
            }
            catch (Exception ex)
            {
                bool cleaned = RemovePatches();
                SetState(HudGuardState.Faulted, "Provider installation failed: " + ex.GetType().Name + ": " + ex.Message +
                    (cleaned ? ". Partial patches rolled back; toggle off/on or Refresh to retry." : ". Cleanup also failed; see module status/log."));
                return false;
            }
        }

        private bool RemovePatches()
        {
            for (int i = patched.Count - 1; i >= 0; i--)
            {
                MethodInfo method = patched[i];
                try
                {
                    // Owner-filtered removal on the known targets avoids the
                    // installed Harmony's UnpatchAll-by-PatchMethod behavior.
                    harmony.Unpatch(method, HarmonyPatchType.Finalizer, HarmonyId);
                    if (HasOwnPatch(method)) throw new InvalidOperationException("Patch remains on " + method.Name);
                    patched.RemoveAt(i);
                }
                catch (Exception ex)
                {
                    SetState(HudGuardState.Faulted, "Could not remove HUD Guard patch: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            if (patched.Count != 0) return false;
            installedAssembly = null;
            if (owner == this) owner = null;
            lock (loggedProviders) loggedProviders.Clear();
            return true;
        }

        internal static List<MethodInfo> FindProviderMethods(Type providersType)
        {
            var result = new List<MethodInfo>();
            Type car = Type.GetType("TrainCar, Assembly-CSharp", false);
            if (providersType == null || car == null) return result;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags methods = flags | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (Type nested in providersType.GetNestedTypes(flags))
            {
                if (!nested.IsDefined(typeof(CompilerGeneratedAttribute), false)) continue;
                foreach (MethodInfo method in nested.GetMethods(methods))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.ReturnType == typeof(void) ||
                        method.ReturnType.IsByRef || method.ReturnType.IsPointer ||
                        (method.ReturnType.IsValueType && Nullable.GetUnderlyingType(method.ReturnType) == null)) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == car) result.Add(method);
                }
            }
            return result;
        }

        // Verify the native reader/query pairing whose null/unavailable behavior
        // is exercised by the actual-HUD fixture. New arbitrary reference-return
        // lambdas are not assumed safe just because null can be returned.
        internal static bool HasQueryContract(Assembly hud, Type returns, Type car)
        {
            if (hud == null || returns == null || car == null) return false;
            Type query, value;
            if (returns == typeof(float?))
            {
                query = hud.GetType("DvMod.HeadsUpDisplay.FloatQueryDataProvider", false);
                value = typeof(float);
            }
            else
            {
                if (!returns.IsGenericType || returns.GetGenericTypeDefinition().FullName != "QuantitiesNet.Quantity`1" ||
                    returns.Assembly.GetName().Name != "QuantitiesNet" || returns.IsValueType) return false;
                Type generic = hud.GetType("DvMod.HeadsUpDisplay.QuantityQueryDataProvider`1", false);
                if (generic == null || !generic.IsGenericTypeDefinition) return false;
                try { query = generic.MakeGenericType(returns.GetGenericArguments()); }
                catch (ArgumentException) { return false; }
                value = returns;
            }
            if (query == null) return false;
            MethodInfo get = query.GetMethod("TryGetValue", new Type[] { car, value.MakeByRefType() });
            MethodInfo format = query.GetMethod("TryGetFormatted", new Type[] { car, typeof(string).MakeByRefType() });
            if (get == null || get.ReturnType != typeof(bool) || !get.GetParameters()[1].IsOut ||
                format == null || format.ReturnType != typeof(bool) || !format.GetParameters()[1].IsOut) return false;
            Type reader = typeof(Func<,>).MakeGenericType(car, returns);
            foreach (ConstructorInfo ctor in query.GetConstructors())
            {
                ParameterInfo[] parameters = ctor.GetParameters();
                if (parameters.Length >= 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == reader) return true;
            }
            return false;
        }

        private static Exception ProviderFinalizer(Exception __exception, MethodBase __originalMethod, ref object __result)
        {
            if (!(__exception is NullReferenceException)) return __exception;
            HudGuardModule current = owner;
            if (current == null) return __exception;
            __result = null; // All selected return types represent unavailable data with null.
            bool first;
            lock (current.loggedProviders) first = current.loggedProviders.Add(__originalMethod);
            if (first) current.Log("HUD: readout unavailable after NullReferenceException from " +
                __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name + ". Further occurrences suppressed until re-enabled.");
            return null;
        }

        private void SetState(HudGuardState state, string status)
        {
            if (State == state && Status == status) return;
            State = state;
            Status = status;
            Log("HUD Guard: " + status);
        }

        private void Log(string message)
        {
            // An optional parent logger must not break the HUD reader or lifecycle.
            try { if (log != null) log(message); }
            catch { }
        }
    }
}
