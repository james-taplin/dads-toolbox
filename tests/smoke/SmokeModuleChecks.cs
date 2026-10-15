using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using CCLSmokeFix;
using HarmonyLib;

internal static class SmokeModuleChecks
{
    private const string OtherOwner = "cclsmokefix.validation.other";
    private const string GateOwner = "cclsmokefix.validation.skip-native-body";
    private static string gameRoot;
    private static int passed;

    private static int Main(string[] args)
    {
        gameRoot = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try { Run(); Console.WriteLine("PASS " + passed + " checks; installed managed target and Harmony, no Unity player launched."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string dir in new[] { "DerailValley_Data/Managed", "DerailValley_Data/Managed/UnityModManager" })
        {
            string file = Path.Combine(gameRoot, dir, name);
            // Read installed assemblies into this isolated managed fixture without
            // changing their zone metadata or the game's files.
            if (File.Exists(file)) return Assembly.Load(File.ReadAllBytes(file));
        }
        return null;
    }

    // Delay resolution of game/Unity/Harmony types until the resolver is registered.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var target = AccessTools.Method(typeof(DV.Simulation.Controllers.ParticlesPortReadersController), "Init");
        var other = new Harmony(OtherOwner);
        var legacy = new Harmony(SmokeFixModule.StandaloneModId);
        var gate = new Harmony(GateOwner);
        var module = new SmokeFixModule();
        var logs = new List<string>();
        bool standalone = false;
        Func<bool> conflict = () => standalone;
        try
        {
            Check(typeof(SmokeFixModule).Assembly.GetName().Name == "DadsToolbox", "smoke module is compiled into the actual combined Toolbox DLL");
            other.Patch(target, prefix: new HarmonyMethod(typeof(SmokeModuleChecks), nameof(OtherPrefix)));
            Check(module.Install(logs.Add, conflict), "enabled against the installed game target");
            Check(OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 1, "one smoke prefix installed");
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(UnrelatedTarget), nameof(UnrelatedTarget.Run))) == null, "unselected annotated patch was not installed");
            Check(module.Install(logs.Add, conflict) && OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 1, "repeated enable does not duplicate patches");
            module.Uninstall();
            Check(!module.Active && OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 0, "disable removes smoke owner");
            Check(OwnedPrefixes(target, OtherOwner) == 1, "disable preserves another owner's prefix on the same target");
            module.Uninstall();
            Check(OwnedPrefixes(target, OtherOwner) == 1, "repeated disable preserves other module");
            Check(module.Install(logs.Add, conflict), "reenable installs again");
            gate.Patch(target, prefix: new HarmonyMethod(typeof(SmokeModuleChecks), nameof(SkipNativeBody)) { priority = Priority.Last });
            standalone = true;
            // Exercise removal from inside the real patched Init. The separate
            // final prefix skips its original body, so no Unity native code runs.
            target.Invoke(FormatterServices.GetUninitializedObject(target.DeclaringType), new object[target.GetParameters().Length]);
            Check(!module.Active && OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 0, "late standalone activation safely unpatches during real Init invocation");
            Check(OwnedPrefixes(target, GateOwner) == 1, "self-removal preserves the independent Init gate");
            gate.Unpatch(target, HarmonyPatchType.Prefix, GateOwner);
            Check(!module.Install(logs.Add, conflict), "active standalone prevents installation");
            standalone = false;
            legacy.Patch(target, prefix: new HarmonyMethod(typeof(SmokeModuleChecks), nameof(OtherPrefix)));
            Check(!module.Install(logs.Add, conflict), "legacy standalone Harmony owner prevents double application even if active probe says false");
            Check(OwnedPrefixes(target, SmokeFixModule.StandaloneModId) == 1, "conflict handling preserves standalone patch");
            legacy.Unpatch(target, HarmonyPatchType.Prefix, SmokeFixModule.StandaloneModId);
            Check(module.Install(logs.Add, conflict), "enable succeeds after standalone owner is removed");
            module.Uninstall();
            Check(!module.Install(logs.Add, () => { throw new InvalidOperationException("probe test"); }) && !module.Active, "failed host probe leaves module inactive");
            Check(!module.Install(logs.Add, null), "missing host probe fails closed");
            bool failProbe = false;
            Check(module.Install(logs.Add, () => { if (failProbe) throw new InvalidOperationException("late probe test"); return false; }), "host probe can succeed at install");
            gate.Patch(target, prefix: new HarmonyMethod(typeof(SmokeModuleChecks), nameof(SkipNativeBody)) { priority = Priority.Last });
            failProbe = true;
            target.Invoke(FormatterServices.GetUninitializedObject(target.DeclaringType), new object[target.GetParameters().Length]);
            Check(!module.Active && OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 0 && OwnedPrefixes(target, GateOwner) == 1, "runtime host-probe exception is contained and removes only smoke");
            gate.Unpatch(target, HarmonyPatchType.Prefix, GateOwner);
            Check(module.Install(text => { throw new InvalidOperationException("log test"); }, conflict), "logger failure does not prevent installation");
            module.Uninstall();
            Check(module.Install(logs.Add, conflict), "module recovers on deliberate retry");
            var duplicate = new SmokeFixModule();
            Check(!duplicate.Install(logs.Add, conflict), "second instance cannot take ownership");
            duplicate.Uninstall();
            Check(module.Active && OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 1, "uninstalling rejected duplicate preserves active instance");
            typeof(SmokeFixModule).GetMethod("ReaderCorrected", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(module, null);
            module.Uninstall();
            Check(module.RequiresReloadForRestoration && module.Status.Contains("reload/restart"), "disable reports restoration limits after a simulated reader correction");
            var foreignCopy = new Harmony(SmokeFixModule.HarmonyId);
            foreignCopy.Patch(target, prefix: new HarmonyMethod(typeof(SmokeModuleChecks), nameof(OtherPrefix)));
            Check(!module.Install(logs.Add, conflict), "an existing owner from another copy is not adopted");
            module.Uninstall();
            Check(OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 1, "rejected install does not remove another copy's patch");
            foreignCopy.Unpatch(target, HarmonyPatchType.Prefix, SmokeFixModule.HarmonyId);
            Check(OwnedPrefixes(target, OtherOwner) == 1, "other owner survives all lifecycle and failure cases");
            Check(logs.Count > 0, "host receives module status diagnostics");
        }
        finally
        {
            module.Uninstall();
            legacy.Unpatch(target, HarmonyPatchType.Prefix, SmokeFixModule.StandaloneModId);
            other.Unpatch(target, HarmonyPatchType.Prefix, OtherOwner);
            gate.Unpatch(target, HarmonyPatchType.Prefix, GateOwner);
        }
        Check(OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 0 && OwnedPrefixes(target, OtherOwner) == 0, "fixture removes only its own test patches");
    }

    private static int OwnedPrefixes(MethodBase target, string owner) => Harmony.GetPatchInfo(target)?.Prefixes.Count(p => p.owner == owner) ?? 0;
    public static void OtherPrefix() { }
    public static bool SkipNativeBody() => false;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + label);
        passed++;
        Console.WriteLine("PASS " + label);
    }

    internal static class UnrelatedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run() { }
    }

    [HarmonyPatch(typeof(UnrelatedTarget), nameof(UnrelatedTarget.Run))]
    internal static class UnselectedPatch
    {
        private static void Prefix() { throw new InvalidOperationException("Unselected patch ran"); }
    }
}

