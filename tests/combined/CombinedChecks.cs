using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DadsToolbox;
using HarmonyLib;
using UnityModManagerNet;

// Exercises the shipped parent DLL, rather than compiling another representative
// parent. Dependencies are deliberately absent and no Unity native API is run.
public static class CombinedChecks
{
    static string managed, toolbox, output;
    static int passed, failed;
    static int controlConfigureCalls;
    static void CountControlConfigure() { controlConfigureCalls++; }
    static readonly List<string> report = new List<string>();
    static readonly string[] Flags = { "SkinDamageFix", "SkinRegistrationFix", "DoubleTrackSignClearance", "HudGuard", "CclControlFix", "LocoSoundPitch", "SmokeFix", "BogieVisualSmoothing" };
    static readonly string[] ModuleFields = { "skinDamage", "skinRegistration", "signs", "hud", "controls", "sound", "smoke", "bogies" };
    static readonly string[] Owners = {
        "jimtaplin.derailvalley.toolbox.skindamage", "jimtaplin.derailvalley.toolbox.skinregistration",
        "jimtaplin.derailvalley.toolbox.hudguard",
        "jimtaplin.derailvalley.toolbox.cclcontrols", "jimtaplin.derailvalley.toolbox.locosoundpitch",
        "jimtaplin.derailvalley.toolbox.smoke" };
    static readonly Dictionary<string, int> CleanupCalls = new Dictionary<string, int>();
    const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    public static int Main(string[] args)
    {
        managed = args[0]; toolbox = args[1]; output = args[2];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try { return Run(); }
        catch (Exception ex) { Console.WriteLine("HARNESS ERROR: " + ex); return 2; }
    }
    static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        string name = new AssemblyName(e.Name).Name;
        if (name == "DadsToolbox") return Assembly.LoadFrom(toolbox);
        foreach (string root in new[] { managed, Path.Combine(managed, "UnityModManager") })
        {
            string path = Path.Combine(root, name + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        Directory.CreateDirectory(output);
        UnityModManager.modEntries.Clear();
        var parent = Entry("DadsToolbox", Path.Combine(output, "Mods", "DadsToolbox"));
        var asm = typeof(DadsToolbox.Main).Assembly;
        Check(asm.GetName().Name == "DadsToolbox", "one parent assembly identity");
        string[] forbidden = { "SkinManagerMod", "CCL.Importer", "CCL.Types", "DoubleTrack", "HeadsUpDisplay", "SteamSizePitch", "DVCCLControlFix", "CCLSmokeFix", "HUDGuard", "H6FTrainsetFix", "DVLocoBogieVisuals" };
        Check(!asm.GetReferencedAssemblies().Any(a => forbidden.Contains(a.Name)), "optional mod assemblies are not hard assembly references");
        Check(Owners.Distinct().Count() == Owners.Length, "all patching features reserve distinct owner IDs");
        Check(asm.GetType("DadsToolbox.H6F.H6FModule") == null, "retired H6F module is absent from shipped DLL");
        Check(typeof(ToolboxSettings).GetField("ConsistFix") == null && typeof(DadsToolbox.Main).GetField("consist", BF) == null, "retired setting and parent instance are absent");
        Check(!asm.GetTypes().Any(t => t.Namespace == "DadsToolbox.H6F"), "no H6F repair types remain in the assembly");
        var legacyPath = Path.Combine(parent.Path, "Settings.xml");
        File.WriteAllText(legacyPath, "<ToolboxSettings><ConsistFix>true</ConsistFix><HudGuard>false</HudGuard><CclControlFix>false</CclControlFix><SmokeFix>false</SmokeFix><Sound><Strength>6.4955945</Strength></Sound><SoundSettingsMigrationComplete>true</SoundSettingsMigrationComplete></ToolboxSettings>");
        var legacySettings = UnityModManager.ModSettings.Load<ToolboxSettings>(parent);
        Check(!legacySettings.HudGuard && !legacySettings.CclControlFix && !legacySettings.SmokeFix && legacySettings.Sound.Strength == 6.4955945f, "older XML ignores retired options and preserves existing feature and sound preferences");
        legacySettings.Save(parent);
        Check(!File.ReadAllText(legacyPath).Contains("<ConsistFix>") && !File.ReadAllText(legacyPath).Contains("<SoundSettingsMigrationComplete>"), "saving older settings drops obsolete options without changing retained preferences");
        foreach (string flag in Flags) Check((bool)typeof(ToolboxSettings).GetField(flag).GetValue(new ToolboxSettings()), flag + " has its independent enabled default");

        for (int mask = 0; mask < 1 << Flags.Length; mask++)
        {
            var s = SettingsFor(mask); s.Save(parent);
            var loaded = UnityModManager.ModSettings.Load<ToolboxSettings>(parent);
            Check(Enumerable.Range(0, Flags.Length).All(i => GetFlag(loaded, Flags[i]) == ((mask & (1 << i)) != 0)), "native UMM XML roundtrip option combination " + mask);
        }

        File.WriteAllText(Path.Combine(parent.Path, "Settings.xml"), "<ToolboxSettings><SkinDamageFix>false</SkinDamageFix><SkinRegistrationFix>false</SkinRegistrationFix><DoubleTrackSignClearance>false</DoubleTrackSignClearance><SignClearance><MinimumClearance>3.25</MinimumClearance><MaximumMove>6.5</MaximumMove><SearchStep>0.1</SearchStep><VerboseLogging>false</VerboseLogging></SignClearance></ToolboxSettings>");
        var old = UnityModManager.ModSettings.Load<ToolboxSettings>(parent); old.Normalize();
        Check(!old.SkinDamageFix && !old.SkinRegistrationFix && !old.DoubleTrackSignClearance && old.SignClearance.MinimumClearance == 3.25f && old.SignClearance.MaximumMove == 6.5f, "0.3.0 parent XML retains skin and sign preferences");
        Check(Flags.Skip(3).All(f => GetFlag(old, f)), "older parent XML receives new module defaults independently");
        var soundField = typeof(ToolboxSettings).GetField("Sound");
        object soundOptions = soundField.GetValue(old);
        Check(soundOptions != null, "old settings create nested sound options");
        Check((float)Get(soundOptions,"MaximumPitch")==2f,"older XML receives a default steam upper cap without resetting strength");
        Set(soundOptions,"MaximumPitch",1.65f);
        Set(soundOptions, "Strength", 3.5f); Set(soundOptions, "MinimumPitch", .43f);
        Set(soundOptions, "NonSteamStrength", 2.5f); Set(soundOptions, "NonSteamMinimumPitch", .38f); Set(soundOptions, "NonSteamMaximumPitch", 1.8f);
        Set(soundOptions, "Chuffs", false); Set(soundOptions, "Whistle", false); Set(soundOptions, "SharedDonor", 1);
        old.Save(parent);
        var nested = UnityModManager.ModSettings.Load<ToolboxSettings>(parent); object soundAgain = soundField.GetValue(nested);
        Check((float)Get(soundAgain,"MaximumPitch")==1.65f,"new steam upper cap survives native UMM XML save and reload");
        Check((float)Get(soundAgain, "Strength") == 3.5f && (float)Get(soundAgain, "MinimumPitch") == .43f && !(bool)Get(soundAgain, "Chuffs") && !(bool)Get(soundAgain, "Whistle"), "shared XML preserves existing steam strength/minimum and group choices");
        Check((float)Get(soundAgain, "NonSteamStrength") == 2.5f && (float)Get(soundAgain, "NonSteamMinimumPitch") == .38f && (float)Get(soundAgain, "NonSteamMaximumPitch") == 1.8f && (int)Get(soundAgain, "SharedDonor") == 1, "shared XML preserves independent non-steam sound options");
        soundField.SetValue(nested, null); nested.Normalize();
        Check(soundField.GetValue(nested) != null, "null nested sound XML normalizes before module construction");

        // All off is the safe managed boundary: native runtime behavior has its
        // separate module and isolated Unity suites.
        var off = SettingsFor(0); off.Save(parent);
        Check(DadsToolbox.Main.Load(parent), "actual parent loads with all optional modules off");
        var snapshots = ModuleFields.ToDictionary(f => f, f => ParentField(f));
        try
        {
            foreach (string field in ModuleFields) Check(snapshots[field] != null, field + " has one parent-owned module instance");
            Check(parent.OnGUI != null && parent.OnSaveGUI != null && parent.OnToggle != null && parent.OnUnload != null && parent.OnUpdate != null, "parent owns every shared UMM callback");
            Check(new Delegate[] { parent.OnGUI, parent.OnSaveGUI, parent.OnToggle, parent.OnUnload, parent.OnUpdate }.All(d => d.Method.DeclaringType == typeof(DadsToolbox.Main)), "module loaders have not replaced parent callbacks");
            Check(ParentSubscriptions() == 1, "one parent dependency event subscription");
            Check(ToolboxPatchCount() == 0, "off modules install no Toolbox Harmony patches");
            var updateObserver=new Harmony("fixture.dadstoolbox.combined.update");
            updateObserver.Patch(snapshots["controls"].GetType().GetMethod("Configure"),prefix:new HarmonyMethod(typeof(CombinedChecks).GetMethod("CountControlConfigure",BF)));
            try
            {
                controlConfigureCalls=0;
                parent.OnUpdate(parent,0);parent.OnUpdate(parent,0);
                Check(controlConfigureCalls==0,"parent Update does not force Controls compatibility reconciliation every frame");
            }
            finally {updateObserver.UnpatchAll(updateObserver.Id);}
            Check(parent.OnToggle(parent, false), "parent disable cleanup succeeds for all modules");
            // Desired choices can all change while the parent is disabled.
            var current = (ToolboxSettings)ParentField("settings");
            foreach (string flag in Flags) Set(current, flag, true);
            typeof(DadsToolbox.Main).GetMethod("ApplySettings", BF).Invoke(null, null);
            Check(Flags.All(f => GetFlag(current, f)) && ToolboxPatchCount() == 0, "disabled parent retains every desired option without enabling patches");
            foreach (string flag in Flags)
            {
                Set(current, flag, false);
                parent.OnSaveGUI(parent);
                var reloaded = UnityModManager.ModSettings.Load<ToolboxSettings>(parent);
                Check(!GetFlag(reloaded, flag) && Flags.Where(f => f != flag).All(f => GetFlag(reloaded, f)), "turning off " + flag + " preserves the other six options");
                Set(current, flag, true);
            }
            foreach (string flag in Flags) Set(current, flag, false);
            Check(parent.OnToggle(parent, true) && ToolboxPatchCount() == 0, "re-enabling parent honors all disabled module preferences");
            Check(ParentSubscriptions() == 1, "repeated parent activation does not duplicate subscription");
            parent.Enabled = false;
            Check(parent.OnToggle(parent, true), "actual callback activation is independent of next-start Enabled checkbox");
            parent.OnSaveGUI(parent);
            Check(parent.OnUnload(parent), "all module unload results succeed");
            Check(ParentSubscriptions() == 0 && ToolboxPatchCount() == 0, "unload removes parent listener and all Toolbox owned patches");
            Check(parent.OnUnload(parent) && ParentSubscriptions() == 0, "repeated parent unload is safe");
            Check(DadsToolbox.Main.Load(parent), "new parent lifetime loads after final unload");
            foreach (string field in ModuleFields) Check(!ReferenceEquals(snapshots[field], ParentField(field)), field + " recreated for a fresh parent lifetime");
            Check(ParentSubscriptions() == 1 && ToolboxPatchCount() == 0, "recreated parent has one listener and respects saved off preferences");
        }
        finally { parent.OnUnload(parent); UnityModManager.modEntries.Clear(); }
        ColdDependencyChecks(parent);
        CleanupFailureChecks(parent);
        FinalUnloadFailureChecks(parent);
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => forbidden.Contains(a.GetName().Name)), "cold optional dependencies were not resolved merely by parent/settings/off lifecycle");
        string verdict = "RESULT: " + passed + " passed, " + failed + " failed.";
        report.Add(verdict); report.Add("Exact compiled parent DLL, installed UMM/Harmony, real XML serialization, cold optional mods. All features off for callback lifecycle; no native Unity/game/audio execution.");
        File.WriteAllLines(Path.Combine(output, "checks.txt"), report); Console.WriteLine(verdict);
        return failed == 0 ? 0 : 1;
    }
    static ToolboxSettings SettingsFor(int mask) { var s = new ToolboxSettings(); for (int i = 0; i < Flags.Length; i++) Set(s, Flags[i], (mask & (1 << i)) != 0); return s; }
    // Bogie smoothing also supports vanilla stock and requires Unity native
    // objects, so its active lifecycle belongs to the separate native suite.
    static ToolboxSettings ColdSettings() { var s = SettingsFor((1 << Flags.Length) - 1); s.BogieVisualSmoothing = false; return s; }
    static bool ColdChoicesPreserved() { return Flags.Where(f => f != "BogieVisualSmoothing").All(f => GetFlag(ParentField("settings"), f)) && !GetFlag(ParentField("settings"), "BogieVisualSmoothing"); }
    static void FinalUnloadFailureChecks(UnityModManager.ModEntry parent)
    {
        SettingsFor(0).Save(parent); DadsToolbox.Main.Load(parent);
        var prior = ModuleFields.ToDictionary(f => f, f => ParentField(f));
        var fixture = new Harmony("fixture.dadstoolbox.combined.finalunload");
        var target = ParentField("hud").GetType().GetMethod("Unload", BF);
        fixture.Patch(target, prefix: new HarmonyMethod(typeof(CombinedChecks).GetMethod("FailedHudStop", BF)));
        fixture.Patch(typeof(DadsToolbox.Main).GetMethod("Cleanup", BF), prefix: new HarmonyMethod(typeof(CombinedChecks).GetMethod("ObserveCleanup", BF)));
        try
        {
            CleanupCalls.Clear();
            Check(!parent.OnUnload(parent), "failed final HUD cleanup rejects whole parent unload");
            Check(CleanupCalls.Count == ModuleFields.Length && CleanupCalls.Values.All(n => n == 1), "failed final cleanup still attempts every other module finalizer");
            Check((bool)ParentField("finalShutdownStarted") && !(bool)ParentField("enabled") && ParentSubscriptions() == 0, "failed final unload latches suspended parent and removes dependency listener");
            Check(!parent.OnToggle(parent, true), "partially finalized parent refuses reactivation");
            typeof(DadsToolbox.Main).GetMethod("ApplySettings", BF).Invoke(null, null);
            parent.OnUpdate(parent, 0);
            Check(ToolboxPatchCount() == 0 && !(bool)ParentField("enabled"), "settings/update cannot resume a partially finalized lifetime");
            CleanupCalls.Clear();
            Check(!parent.OnUnload(parent) && CleanupCalls.Count == ModuleFields.Length && CleanupCalls.Values.All(n => n == 1), "retrying final unload reattempts all cleanups and preserves failure");
        }
        finally { fixture.UnpatchAll(fixture.Id); }
        Check(parent.OnUnload(parent) && !(bool)ParentField("loaded"), "cleanup retry completes after the failing boundary recovers");
        Check(DadsToolbox.Main.Load(parent) && !(bool)ParentField("finalShutdownStarted"), "successful new Load clears final shutdown latch");
        try
        {
            Check(ModuleFields.All(f => !ReferenceEquals(prior[f], ParentField(f))) && ParentSubscriptions() == 1, "recovered parent creates seven fresh lifetimes with one listener");
            Check(parent.OnToggle(parent, true) && ToolboxPatchCount() == 0, "recovered lifetime can reactivate with saved off preferences");
        }
        finally { parent.OnUnload(parent); }
    }
    static void ColdDependencyChecks(UnityModManager.ModEntry parent)
    {
        ColdSettings().Save(parent);
        Check(DadsToolbox.Main.Load(parent), "default-selected parent starts with every optional dependency absent");
        try
        {
            Check(ToolboxPatchCount() == 0 && ColdChoicesPreserved(), "missing optional dependencies preserve selected choices and install no patches; native bogie runtime tested separately");
            Check(GetProperty(ParentField("hud"), "State").ToString() == "WaitingForHud", "HUD Guard waits for absent HUD");
            Check(!(bool)GetProperty(ParentField("controls"), "Active") && ((string)GetProperty(ParentField("controls"), "Status")).Contains("CCL"), "CCL controls wait for optional CCL");
            Check(!(bool)GetProperty(ParentField("sound"), "Active") && ((string)GetProperty(ParentField("sound"), "Status")).Contains("CCL"), "Loco Sound Pitch waits for optional CCL without touching native audio");
            Check(!(bool)GetProperty(ParentField("smoke"), "Active") && ((string)ParentField("smokeStatus")).Contains("CCL"), "CCL smoke waits for optional CCL");
            Check(ParentSubscriptions() == 1, "only the parent owns the dependency listener");
            foreach (string id in new[] { "DVCustomCarLoader", "HeadsUpDisplay_994", "SkinManagerMod", "DoubleTrack" })
            {
                var mod = Entry(id, Path.Combine(output, "inactive", id)); mod.Enabled = true;
                UnityModManager.modEntries.Add(mod);
                typeof(DadsToolbox.Main).GetMethod("OnDependencyToggle", BF).Invoke(null, new object[] { mod, false });
            }
            Check(UnityModManager.modEntries.All(m => m.Loaded && !m.Active && m.Enabled) && ToolboxPatchCount() == 0, "loaded but inactive optional dependencies remain ineligible despite Enabled preferences");
            Check(ColdChoicesPreserved() && ParentSubscriptions() == 1, "dependency events preserve all desired choices and the single parent listener");
            Check(parent.OnToggle(parent, false), "default-selected dependency-waiting parent disables cleanly");
            var stoppedSound = ParentField("sound");
            stoppedSound.GetType().GetMethod("Refresh", BF).Invoke(stoppedSound, null);
            Check(!(bool)Get(stoppedSound, "parentActive") && (bool)GetProperty(stoppedSound, "DesiredEnabled") &&
                  !(bool)GetProperty(stoppedSound, "Active") && ToolboxPatchCount() == 0,
                  "parent disable revokes sound runtime permission while preserving desire, including after explicit refresh");
        }
        finally { parent.OnUnload(parent); UnityModManager.modEntries.Clear(); }

    }
    static void CleanupFailureChecks(UnityModManager.ModEntry parent)
    {
        SettingsFor(0).Save(parent); DadsToolbox.Main.Load(parent);
        var fixture = new Harmony("fixture.dadstoolbox.combined.cleanup");
        var foreign = new Harmony("fixture.dadstoolbox.combined.foreign");
        var marker = typeof(CombinedChecks).GetMethod("ForeignMarker", BF);
        foreign.Patch(marker, prefix: new HarmonyMethod(typeof(CombinedChecks).GetMethod("ForeignPrefix", BF)));
        CleanupCalls.Clear();
        try
        {
            var hudStop = ParentField("hud").GetType().GetMethods(BF).Single(m => m.Name == "Stop" && m.IsPublic);
            fixture.Patch(hudStop, prefix: new HarmonyMethod(typeof(CombinedChecks).GetMethod("FailedHudStop", BF)));
            // Observe the dispatcher boundary. Spying on tiny leaf Uninstall
            // methods after prior calls would miss CLR-inlined implementations.
            fixture.Patch(typeof(DadsToolbox.Main).GetMethod("Cleanup", BF), prefix: new HarmonyMethod(typeof(CombinedChecks).GetMethod("ObserveCleanup", BF)));
            bool result = parent.OnToggle(parent, false);
            Check(!result, "parent propagates a failed first-module cleanup result");
            var cleanupNames = new[] { "HUD Guard", "CCL Control Fix", "Loco Sound Pitch", "CCL Smoke Fix", "Skin damage", "Skin registration", "DoubleTrack signs" };
            Check(cleanupNames.All(f => CleanupCalls.ContainsKey(f) && CleanupCalls[f] == 1), "failed HUD cleanup still attempts all other six module cleanups exactly once");
            Check(!(bool)ParentField("enabled") && ToolboxPatchCount() == 0, "failed disable remains internally suspended instead of silently resuming work");
            Check(Flags.All(f => !GetFlag(ParentField("settings"), f)), "cleanup failure preserves every saved preference");
            Check(ForeignMarker() == 17 && Harmony.GetPatchInfo(marker).Owners.Contains("fixture.dadstoolbox.combined.foreign"), "failed Toolbox cleanup preserves an unrelated Harmony owner");
        }
        finally
        {
            fixture.UnpatchAll(fixture.Id);
            parent.OnUnload(parent);
            Check(ForeignMarker() == 17 && Harmony.GetPatchInfo(marker).Owners.Contains("fixture.dadstoolbox.combined.foreign"), "final Toolbox unload preserves an unrelated Harmony owner");
            foreign.UnpatchAll(foreign.Id);
        }
    }
    static void ObserveCleanup(string __0) { if (!CleanupCalls.ContainsKey(__0)) CleanupCalls[__0] = 0; CleanupCalls[__0]++; }
    static bool FailedHudStop(ref bool __result) { __result = false; return false; }
    [MethodImpl(MethodImplOptions.NoInlining)] static int ForeignMarker() { return 5; }
    static bool ForeignPrefix(ref int __result) { __result = 17; return false; }
    static bool GetFlag(object s, string f) { return (bool)Get(s, f); }
    static object Get(object target, string f) { return target.GetType().GetField(f, BF).GetValue(target); }
    static object GetProperty(object target, string f) { return target.GetType().GetProperty(f, BF).GetValue(target, null); }
    static void Set(object target, string f, object value) { target.GetType().GetField(f, BF).SetValue(target, value); }
    static object ParentField(string f) { return typeof(DadsToolbox.Main).GetField(f, BF).GetValue(null); }
    static int ParentSubscriptions() { var d = (Delegate)typeof(UnityModManager).GetField("toggleModsListen", BF).GetValue(null); return d == null ? 0 : d.GetInvocationList().Count(e => e.Method.DeclaringType == typeof(DadsToolbox.Main)); }
    static int ToolboxPatchCount() { return Harmony.GetAllPatchedMethods().Sum(m => { var p = Harmony.GetPatchInfo(m); return p == null ? 0 : p.Owners.Count(o => Owners.Contains(o)); }); }
    static UnityModManager.ModEntry Entry(string id, string path)
    {
        Directory.CreateDirectory(path);
        var e = (UnityModManager.ModEntry)FormatterServices.GetUninitializedObject(typeof(UnityModManager.ModEntry));
        Set(e, "Info", new UnityModManager.ModInfo { Id = id, Version = "0.4.1", AssemblyName = "DadsToolbox.dll" });
        Set(e, "Path", path); Set(e, "Logger", new UnityModManager.ModEntry.ModLogger("combined-" + id));
        Set(e, "Version", new Version(0, 4, 1)); Set(e, "mAssembly", typeof(DadsToolbox.Main).Assembly); Set(e, "mStarted", true); Set(e, "mActive", false);
        return e;
    }
    static void Check(bool result, string text) { string line = (result ? "PASS: " : "FAIL: ") + text; report.Add(line); Console.WriteLine(line); if (result) passed++; else failed++; }
}

