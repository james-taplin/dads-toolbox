using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DadsToolbox;
using HarmonyLib;
using UnityModManagerNet;

public static class ManagedChecks
{
    private static string managed, toolboxPath, fakePath, output, doubleTrackPath, standalonePath;
    private static int passed, failed;
    private static readonly List<string> checks = new List<string>();
    public static int Main(string[] args)
    {
        managed = args[0]; toolboxPath = args[1]; fakePath = args[2]; output = args[3];
        doubleTrackPath = args[4]; standalonePath = args[5];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try { return Run(); }
        catch (Exception ex) { Console.WriteLine("HARNESS ERROR: " + ex); return 2; }
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (name == "DadsToolbox") return Assembly.LoadFrom(toolboxPath);
        foreach (string root in new[] { managed, Path.Combine(managed, "UnityModManager") })
        {
            string file = Path.Combine(root, name + ".dll");
            if (File.Exists(file)) return Assembly.LoadFrom(file);
        }
        return null;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        Directory.CreateDirectory(output);
        UnityModManager.modEntries.Clear();
        var testEntry = Entry("DadsToolbox", new Version(0, 1, 0), typeof(DadsToolbox.Main).Assembly, false);
        var settings = new ToolboxSettings();
        DisableNewModules(settings);
        Check(settings.SkinDamageFix, "default user preference is enabled");
        settings.SkinDamageFix = false;
        settings.Save(testEntry);
        Check(!UnityModManager.ModSettings.Load<ToolboxSettings>(testEntry).SkinDamageFix,
            "actual UMM settings persist disabled preference");
        settings.SkinDamageFix = true;
        settings.Save(testEntry);
        Check(UnityModManager.ModSettings.Load<ToolboxSettings>(testEntry).SkinDamageFix,
            "actual UMM settings persist enabled preference");

        Check(DadsToolbox.Main.Load(testEntry), "main entry loads with optional dependency absent");
        try
        {
            Check(Status().Contains("absent or disabled"), "absent dependency has actionable inactive status");
            Check(!Module().Active, "absent dependency installs no patches");
            Check(testEntry.OnGUI != null && testEntry.OnSaveGUI != null && testEntry.OnToggle != null &&
                  testEntry.OnUnload != null, "single parent owns UMM GUI/save/toggle/unload callbacks");
            Check(EventSubscriptions() == 1, "main subscribes once to actual UMM toggle event");
            testEntry.Active = true;
            Check(testEntry.Active && !Module().Active, "actual UMM parent activation works without optional dependency");

            var fake = Assembly.LoadFrom(fakePath);
            var dependency = Entry("SkinManagerMod", new Version(4, 4, 1), fake, false);
            dependency.Enabled = true;
            UnityModManager.modEntries.Add(dependency);
            testEntry.OnToggle(testEntry, true);
            Check(dependency.Loaded && !dependency.Active && !Module().Active,
                "loaded and enabled optional mod is insufficient while inactive");

            bool callbackSawCurrentActive = false;
            UnityModManager.ToggleModsListen observer = (mod, active) =>
            {
                if (mod == dependency) callbackSawCurrentActive = mod.Active == active;
            };
            UnityModManager.toggleModsListen += observer;
            dependency.OnToggle = (mod, value) => true;
            dependency.Active = true;
            Check(callbackSawCurrentActive, "actual UMM event runs after Active is updated on enable");
            Check(Module().Active && Status().StartsWith("Active:"),
                "dependency activation installs the actual toolbox module");
            MethodInfo target = fake.GetType("SkinManagerMod.Patches.TrainCarPaintPatches").GetMethod("ReapplyThemes");
            Check(PrefixCount(target) == 1, "initial and UMM activation callbacks produce one owned prefix");
            dependency.Enabled = false;
            testEntry.OnToggle(testEntry, true);
            Check(Module().Active, "active runtime dependency remains usable despite changed restart preference");
            dependency.Active = false;
            Check(callbackSawCurrentActive && !Module().Active && PrefixCount(target) == 0,
                "actual UMM deactivate event sees current Active and removes module patches");
            Check(Preference(), "dependency deactivation preserves desired user preference");
            dependency.Active = true;
            Check(Module().Active && PrefixCount(target) == 1, "dependency reactivation installs once");
            dependency.OnToggle = (mod, value) => value;
            dependency.Active = false;
            Check(dependency.Active && Module().Active && PrefixCount(target) == 1,
                "refused dependency deactivation retains actual runtime state and active guard");
            dependency.OnToggle = (mod, value) => true;

            testEntry.Active = false;
            Check(!testEntry.Active && !Module().Active && PrefixCount(target) == 0,
                "actual UMM parent deactivation removes owned patches");
            dependency.Active = false; dependency.Active = true;
            Check(!Module().Active && Preference(), "dependency changes cannot enable a disabled toolbox");
            testEntry.Active = true;
            Check(Module().Active && PrefixCount(target) == 1, "parent reactivation restores desired module");

            SetPreference(false);
            testEntry.OnToggle(testEntry, true);
            Check(!Module().Active && Status() == "Off", "disabled user module preference is effective");
            dependency.Active = false; dependency.Active = true;
            Check(!Module().Active && !Preference(), "dependency toggle preserves disabled preference");
            testEntry.OnSaveGUI(testEntry);
            Check(!UnityModManager.ModSettings.Load<ToolboxSettings>(testEntry).SkinDamageFix,
                "parent save callback persists the selected setting through actual UMM");
            SetPreference(true);

            SetField(dependency, "Version", new Version(4, 4, 0));
            testEntry.OnToggle(testEntry, true);
            Check(!Module().Active && Status().Contains("found 4.4.0"), "older unvalidated version fails closed");
            SetField(dependency, "Version", new Version(4, 4, 2));
            testEntry.OnToggle(testEntry, true);
            Check(!Module().Active && Status().Contains("found 4.4.2"), "newer unvalidated version fails closed");
            SetField(dependency, "Version", new Version(4, 4, 1));
            SetField(dependency, "mAssembly", typeof(ManagedChecks).Assembly);
            testEntry.OnToggle(testEntry, true);
            Check(!Module().Active && Status().Contains("incompatible"), "wrong assembly/API fails closed");
            SetField(dependency, "mAssembly", null);
            testEntry.OnToggle(testEntry, true);
            Check(!dependency.Loaded && !Module().Active && PrefixCount(target) == 0 &&
                (Status().Contains("unavailable") || Status().Contains("absent or disabled")), "missing runtime assembly installs no patches");
            SetField(dependency, "mAssembly", fake);
            testEntry.OnToggle(testEntry, true);
            Check(Module().Active, "compatible dependency recovers after unavailable assembly");

            UnityModManager.toggleModsListen -= observer;
            Check(testEntry.OnUnload(testEntry), "parent unload callback succeeds");
            Check(!Module().Active && PrefixCount(target) == 0, "parent unload removes owned patch");
            Check(EventSubscriptions() == 0, "parent unload unsubscribes its actual UMM event handler");
            dependency.Active = false; dependency.Active = true;
            Check(!Module().Active, "dependency cannot restart unloaded module");

            // Model UMM's real loader order: invoke Main.Load, then mark the
            // entry started and call Active=true (which calls OnToggle again).
            settings.SkinDamageFix = true; settings.Save(testEntry);
            SetField(testEntry, "mActive", false);
            Check(DadsToolbox.Main.Load(testEntry), "main initial loader succeeds with dependency already active");
            Check(Module().Active && PrefixCount(target) == 1,
                "load-after active optional dependency installs one guard during main loader");
            testEntry.Active = true;
            Check(Module().Active && PrefixCount(target) == 1 && EventSubscriptions() == 1,
                "actual UMM post-loader activation remains idempotent with one subscription");
            testEntry.OnUnload(testEntry);
            Check(!Module().Active && PrefixCount(target) == 0 && EventSubscriptions() == 0,
                "second full lifecycle cleans up patch and event handler");
        }
        finally
        {
            testEntry.OnUnload(testEntry);
            UnityModManager.modEntries.Clear();
        }

        SignChecks(testEntry);

        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        File.WriteAllLines(Path.Combine(output, "checks.txt"), checks.Concat(new[] {
            "RESULT: " + passed + " passed, " + failed + " failed.",
            "Actual installed UMM/Harmony and compiled DadsToolbox entry; synthetic Skin Manager API target only.",
            "No Unity runtime, game launch, paint rendering or native explosion acceptance in this managed fixture." }));
        return failed == 0 ? 0 : 1;
    }

    private static void SignChecks(UnityModManager.ModEntry parent)
    {
        var shared = new ToolboxSettings { SkinDamageFix = false, DoubleTrackSignClearance = false };
        DisableNewModules(shared);
        shared.SignClearance.MinimumClearance = 3.25f;
        shared.SignClearance.MaximumMove = 6.5f;
        shared.SignClearance.SearchStep = 0.1f;
        shared.SignClearance.VerboseLogging = false;
        shared.Save(parent);
        var loaded = UnityModManager.ModSettings.Load<ToolboxSettings>(parent);
        Check(!loaded.SkinDamageFix && !loaded.DoubleTrackSignClearance &&
            loaded.SignClearance.MinimumClearance == 3.25f && loaded.SignClearance.MaximumMove == 6.5f &&
            loaded.SignClearance.SearchStep == 0.1f && !loaded.SignClearance.VerboseLogging,
            "one actual UMM settings file preserves independent options and nested sign values");
        File.WriteAllText(Path.Combine(parent.Path, "Settings.xml"), "<ToolboxSettings><SkinDamageFix>false</SkinDamageFix></ToolboxSettings>");
        loaded = UnityModManager.ModSettings.Load<ToolboxSettings>(parent); loaded.Normalize();
        Check(!loaded.SkinDamageFix && loaded.DoubleTrackSignClearance && loaded.SignClearance.MinimumClearance == 2f,
            "older skin-only settings retain their choice and receive sign defaults");
        loaded.SignClearance = null; loaded.Normalize();
        Check(loaded.SignClearance != null, "null nested settings are normalized before module construction");
        loaded.SignClearance.MinimumClearance = float.NaN;
        loaded.SignClearance.MaximumMove = float.PositiveInfinity;
        loaded.SignClearance.SearchStep = float.NegativeInfinity;
        loaded.Normalize();
        Check(loaded.SignClearance.MinimumClearance == 2 && loaded.SignClearance.MaximumMove == 8 && loaded.SignClearance.SearchStep == .05f,
            "all non-finite sign settings return to safe defaults");
        loaded.SignClearance.MinimumClearance = -1;
        loaded.SignClearance.MaximumMove = 100;
        loaded.SignClearance.SearchStep = 0;
        loaded.Normalize();
        Check(loaded.SignClearance.MinimumClearance == .5f && loaded.SignClearance.MaximumMove == 25 && loaded.SignClearance.SearchStep == .01f,
            "finite sign settings stay inside the supported clearance/search bounds");

        shared = new ToolboxSettings(); DisableNewModules(shared); shared.Save(parent);
        var skin = Entry("SkinManagerMod", new Version(4,4,1), Assembly.LoadFrom(fakePath), true);
        var dt = Entry("DoubleTrack", new Version(2,1,1), Assembly.LoadFrom(doubleTrackPath), false);
        dt.Enabled = true; dt.OnToggle = (m,v) => true;
        UnityModManager.modEntries.Add(skin); UnityModManager.modEntries.Add(dt);
        DadsToolbox.Main.Load(parent);
        try
        {
            Check(parent.OnUpdate != null && parent.OnUpdate.Method.DeclaringType == typeof(DadsToolbox.Main),
                "one parent update callback owns sign ticking");
            Check(!Signs().Active && Signs().Status.Contains("absent or disabled") && Module().Active,
                "inactive loaded DoubleTrack leaves signs inactive while Skin Manager remains active");
            dt.Active = true;
            Check(Signs().Active && Module().Active && Signs().Status.StartsWith("Active:"),
                "actual UMM DoubleTrack activation enables its module alongside Skin Manager");
            dt.Enabled = false; parent.OnToggle(parent,true);
            Check(Signs().Active, "DoubleTrack runtime Active is authoritative over restart Enabled preference");
            dt.Active = false;
            Check(!Signs().Active && Settings().DoubleTrackSignClearance && Module().Active,
                "DoubleTrack deactivation stops only signs and preserves desired setting");
            dt.Active = true;
            Check(Signs().Active, "DoubleTrack reactivation restores signs without changing desired setting");
            SetField(dt,"Version",new Version(2,1,0)); parent.OnToggle(parent,true);
            Check(!Signs().Active && Signs().Status.Contains("found 2.1.0"), "unvalidated older DoubleTrack stays inactive");
            SetField(dt,"Version",new Version(2,1,2)); parent.OnToggle(parent,true);
            Check(!Signs().Active && Signs().Status.Contains("found 2.1.2"), "unvalidated newer DoubleTrack stays inactive");
            SetField(dt,"Version",new Version(2,1,1));
            var actualAssembly = dt.Assembly;
            SetField(dt,"mAssembly",typeof(ManagedChecks).Assembly); parent.OnToggle(parent,true);
            Check(!Signs().Active && Signs().Status.Contains("API is incompatible"), "wrong DoubleTrack runtime assembly fails closed");
            SetField(dt,"mAssembly",actualAssembly); parent.OnToggle(parent,true);
            Check(Signs().Active, "supported actual DoubleTrack assembly recovers after compatibility failure");

            var helper = Entry(DoubleTrackSigns.StandaloneId,new Version(1,0,0),null,false);
            SetField(helper,"mStarted",false); helper.Enabled = true;
            UnityModManager.modEntries.Add(helper); Signs().Refresh();
            Check(!helper.Loaded && !helper.Active && Signs().Active,
                "installed but never loaded standalone is safe despite enabled restart preference");
            SetField(helper,"mAssembly",Assembly.LoadFrom(standalonePath)); SetField(helper,"mStarted",true);
            Signs().Tick(100);
            Check(helper.Loaded && !helper.Active && !Signs().Active && Signs().Status.Contains("restart"),
                "poll detects loaded inactive standalone without relying on an activation event");
            Check(Settings().DoubleTrackSignClearance && Module().Active,
                "standalone conflict preserves sign preference and independent Skin Manager patch");
            helper.OnToggle = (m,v) => true; helper.Active = true;
            Check(!Signs().Active && Signs().Status.Contains("standalone"), "actual UMM standalone activation keeps sign module blocked");
            helper.Active = false;
            Check(helper.Loaded && !Signs().Active,
                "toggling off a loaded standalone does not bypass restart protection against legacy watchers");
            SetField(helper,"mAssembly",null); SetField(helper,"mStarted",false); SetField(helper,"mActive",true);
            Signs().Refresh();
            Check(!Signs().Active, "active standalone is a conflict even with inconsistent Loaded metadata");
            SetField(helper,"mActive",false); Signs().Refresh();
            Check(Signs().Active, "simulated cold-start unloaded standalone permits the selected Toolbox correction");

            Settings().DoubleTrackSignClearance = false; parent.OnToggle(parent,true);
            Check(!Signs().Active && Signs().Status == "Off" && Module().Active, "sign option off leaves Skin Manager patch active");
            dt.Active = false; dt.Active = true;
            Check(!Signs().Active && !Settings().DoubleTrackSignClearance, "dependency events cannot overwrite disabled sign preference");
            Settings().DoubleTrackSignClearance = true; Settings().SkinDamageFix = false; parent.OnToggle(parent,true);
            Check(Signs().Active && !Module().Active, "Skin Manager option off leaves sign module active");
            parent.OnSaveGUI(parent);
            loaded = UnityModManager.ModSettings.Load<ToolboxSettings>(parent);
            Check(loaded.DoubleTrackSignClearance && !loaded.SkinDamageFix, "parent save persists independent effective user choices");
            parent.OnToggle(parent,false);
            Check(!Signs().Active && Signs().Status == "Toolbox disabled", "parent disable immediately stops signs");
            dt.Active = false; dt.Active = true;
            Check(!Signs().Active, "dependency events cannot restart disabled Toolbox signs");
            parent.OnToggle(parent,true);
            Check(Signs().Active && EventSubscriptions() == 1, "parent re-enable restores signs with one dependency subscription");

            var old = Signs(); parent.OnUnload(parent);
            old.Configure(true,true); old.Refresh(); old.Tick(200);
            Check(!old.Active && old.Status == "Unloaded" && EventSubscriptions() == 0,
                "unloaded sign instance cannot restart through stale configure/update calls");
            parent.OnUnload(parent);
            Check(!old.Active && EventSubscriptions() == 0, "repeated parent unload is harmless");
            DadsToolbox.Main.Load(parent);
            Check(!ReferenceEquals(old,Signs()) && Signs().Active && EventSubscriptions() == 1,
                "new parent lifetime gets a fresh working sign module without duplicate subscription");
            var noisy = new DoubleTrackSigns(new SignClearanceOptions(), text => { throw new IOException("fixture logger"); });
            noisy.Configure(false,true); noisy.Refresh(); noisy.Unload();
            Check(noisy.Status == "Unloaded", "throwing logger cannot break module lifecycle");
        }
        finally { parent.OnUnload(parent); UnityModManager.modEntries.Clear(); }
    }

    private static DoubleTrackSigns Signs()
    { return (DoubleTrackSigns)typeof(DadsToolbox.Main).GetField("signs", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); }

    private static UnityModManager.ModEntry Entry(string id, Version version, Assembly assembly, bool active)
    {
        string path = Path.Combine(output, id);
        Directory.CreateDirectory(path);
        var result = (UnityModManager.ModEntry)FormatterServices.GetUninitializedObject(typeof(UnityModManager.ModEntry));
        SetField(result, "Info", new UnityModManager.ModInfo { Id = id, Version = version.ToString(), AssemblyName = id + ".dll" });
        SetField(result, "Path", path);
        SetField(result, "Logger", new UnityModManager.ModEntry.ModLogger("toolbox-managed-" + id));
        SetField(result, "Version", version);
        SetField(result, "mAssembly", assembly);
        SetField(result, "mStarted", true);
        SetField(result, "mActive", active);
        return result;
    }
    private static SkinDamageFix Module()
    { return (SkinDamageFix)typeof(DadsToolbox.Main).GetField("skinDamage", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); }
    private static string Status()
    { return (string)typeof(DadsToolbox.Main).GetField("status", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); }
    private static ToolboxSettings Settings()
    { return (ToolboxSettings)typeof(DadsToolbox.Main).GetField("settings", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); }
    private static bool Preference() { return Settings().SkinDamageFix; }
    // Preserve this suite's original skin/sign isolation. New optional features
    // receive their own exact-DLL parent/module suites; sound activation requires
    // actual Unity audio APIs and must not run in a CLR-only fixture.
    private static void DisableNewModules(ToolboxSettings value)
    {
        foreach (string name in new[] { "HudGuard", "ConsistFix", "CclControlFix", "LocoSoundPitch", "SmokeFix" })
        {
            var field = typeof(ToolboxSettings).GetField(name);
            if (field != null) field.SetValue(value, false);
        }
    }
    private static void SetPreference(bool value) { Settings().SkinDamageFix = value; }
    private static void SetField(object instance, string name, object value)
    { instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, value); }
    private static int PrefixCount(MethodInfo target)
    { var info = Harmony.GetPatchInfo(target); return info == null ? 0 : info.Prefixes.Count(p => p.owner == SkinDamageFix.HarmonyId); }
    private static int EventSubscriptions()
    {
        var current = (Delegate)typeof(UnityModManager).GetField("toggleModsListen", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return current == null ? 0 : current.GetInvocationList().Count(d => d.Method.DeclaringType == typeof(DadsToolbox.Main));
    }
    private static void Check(bool value, string name)
    { string line = (value ? "PASS: " : "FAIL: ") + name; checks.Add(line); Console.WriteLine(line); if (value) passed++; else failed++; }
}
