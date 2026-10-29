using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CCLSmokeFix;
using HarmonyLib;

internal static class SmokeModuleChecks
{
    private const string OtherOwner = "cclsmokefix.validation.other";
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
        var module = new SmokeFixModule();
        var logs = new List<string>();
        try
        {
            Check(typeof(SmokeFixModule).Assembly.GetName().Name == "DadsToolbox", "smoke module is compiled into the actual combined Toolbox DLL");
            other.Patch(target, prefix: new HarmonyMethod(typeof(SmokeModuleChecks), nameof(OtherPrefix)));
            Check(module.Install(logs.Add), "enabled against the installed game target");
            Check(OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 1, "one smoke prefix installed");
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(UnrelatedTarget), nameof(UnrelatedTarget.Run))) == null, "unselected annotated patch was not installed");
            Check(module.Install(logs.Add) && OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 1, "repeated enable does not duplicate patches");
            module.Uninstall();
            Check(!module.Active && OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 0, "disable removes smoke owner");
            Check(OwnedPrefixes(target, OtherOwner) == 1, "disable preserves another owner's prefix on the same target");
            module.Uninstall();
            Check(OwnedPrefixes(target, OtherOwner) == 1, "repeated disable preserves other module");
            Check(module.Install(logs.Add), "reenable installs again");
            typeof(SmokeFixModule).GetMethod("Fault", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(module, new object[] { new InvalidOperationException("controlled correction failure") });
            Check(!module.Active && OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 0,
                "runtime correction failure stops and removes its own smoke patch");
            Check(OwnedPrefixes(target, OtherOwner) == 1, "runtime failure cleanup preserves unrelated owner's patch");
            Check(module.Install(text => { throw new InvalidOperationException("log test"); }), "logger failure does not prevent installation");
            module.Uninstall();
            Check(module.Install(logs.Add), "module recovers on deliberate retry");
            var duplicate = new SmokeFixModule();
            Check(!duplicate.Install(logs.Add), "second instance cannot take ownership");
            duplicate.Uninstall();
            Check(module.Active && OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 1, "uninstalling rejected duplicate preserves active instance");
            typeof(SmokeFixModule).GetMethod("ReaderCorrected", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(module, null);
            module.Uninstall();
            Check(module.RequiresReloadForRestoration && module.Status.Contains("reload/restart"), "disable reports restoration limits after a simulated reader correction");
            var foreignCopy = new Harmony(SmokeFixModule.HarmonyId);
            foreignCopy.Patch(target, prefix: new HarmonyMethod(typeof(SmokeModuleChecks), nameof(OtherPrefix)));
            Check(!module.Install(logs.Add), "an existing owner from another copy is not adopted");
            module.Uninstall();
            Check(OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 1, "rejected install does not remove another copy's patch");
            foreignCopy.Unpatch(target, HarmonyPatchType.Prefix, SmokeFixModule.HarmonyId);
            Check(OwnedPrefixes(target, OtherOwner) == 1, "other owner survives all lifecycle and failure cases");
            Check(logs.Count > 0, "host receives module status diagnostics");
        }
        finally
        {
            module.Uninstall();
            other.Unpatch(target, HarmonyPatchType.Prefix, OtherOwner);
        }
        Check(OwnedPrefixes(target, SmokeFixModule.HarmonyId) == 0 && OwnedPrefixes(target, OtherOwner) == 0, "fixture removes only its own test patches");
    }

    private static int OwnedPrefixes(MethodBase target, string owner) => Harmony.GetPatchInfo(target)?.Prefixes.Count(p => p.owner == owner) ?? 0;
    public static void OtherPrefix() { }
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

