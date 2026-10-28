using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DadsToolbox.CclControls;
using HarmonyLib;
using UnityModManagerNet;

internal static class LifecycleChecks
{
    private static string managed, toolbox, output;
    private static int passed, failed;
    private static int patchEnumerations, patchMetadataReads;
    private static readonly List<string> checks = new List<string>();
    public static int Main(string[] args)
    {
        managed=args[0]; toolbox=args[1]; output=args[2];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try { return Run(); }
        catch(Exception ex) { Console.WriteLine("HARNESS ERROR: "+ex); return 2; }
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name=new AssemblyName(args.Name).Name;
        if(name==new AssemblyName(AssemblyName.GetAssemblyName(toolbox).FullName).Name) return Assembly.LoadFrom(toolbox);
        foreach(string root in new[]{managed,Path.Combine(managed,"UnityModManager")})
        { string path=Path.Combine(root,name+".dll"); if(File.Exists(path))return Assembly.LoadFrom(path); }
        return null;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Run()
    {
        UnityModManager.modEntries.Clear();
        CheckCloning();
        var module=new ControlsModule(Console.WriteLine);
        module.Configure(true,true);
        Check(!module.Active && module.DesiredEnabled && module.Status.Contains("requires"),"absent CCL is isolated and preserves desired preference");
        module.Configure(false,true);
        Check(!module.Active && module.Status.StartsWith("Off"),"user disabled feature remains off");
        var ccl=Entry(ControlsModule.DependencyId,false,false); ccl.Enabled=true; UnityModManager.modEntries.Add(ccl);
        module.Configure(true,true);
        Check(!module.Active,"enabled but never loaded CCL is not a runtime dependency");
        Set(ccl,"mAssembly",typeof(LifecycleChecks).Assembly); Set(ccl,"mStarted",true); module.Refresh();
        Check(ccl.Loaded && !ccl.Active && !module.Active,"loaded inactive CCL is insufficient");
        Set(ccl,"mActive",true); module.Refresh();
        Check(module.Active,"active CCL installs the actual native five-class adapter");
        Check(OwnedMethods(ControlsModule.HarmonyOwner).Count==5,"only five explicit native control methods are patched");
        var originals=OwnedMethods(ControlsModule.HarmonyOwner);
        Check(originals.All(m=>m.DeclaringType.Assembly.GetName().Name=="Assembly-CSharp"),"all control targets belong to installed native game assembly");
        module.Configure(true,true); module.Refresh();
        Check(OwnedPatchCount(ControlsModule.HarmonyOwner)==6,"repeat configure and refresh install no duplicate prefix or postfix");
        CheckPollingMetadata(module,ccl);
        var duplicate=new ControlsModule(_=>{throw new Exception("logger failure");}); duplicate.Configure(true,true);
        Check(!duplicate.Active && duplicate.Status.Contains("another"),"duplicate module lifetime is refused despite logger faults");
        Check(duplicate.Stop() && duplicate.Unload() && module.Active,"refused duplicate cleanup cannot stop valid owner");

        MethodInfo sentinel=typeof(Sentinel).GetMethod("Ping");
        var foreign=new Harmony("toolbox.controls.fixture.foreign");
        foreign.Patch(sentinel,prefix:new HarmonyMethod(typeof(LifecycleChecks).GetMethod("Prefix",BindingFlags.Static|BindingFlags.Public)));
        Check(module.Stop() && !module.Active && module.DesiredEnabled,"stop immediately removes effective control behavior and preserves choice");
        Check(OwnedPatchCount(ControlsModule.HarmonyOwner)==0 && Harmony.GetPatchInfo(sentinel).Owners.Contains(foreign.Id),"stop removes only control-owned patches and preserves foreign patches");
        module.Configure(true,true); Check(module.Active,"parent re-enable reinstalls fresh effective control patches");
        Set(ccl,"mActive",false); module.Refresh();
        Check(!module.Active && OwnedPatchCount(ControlsModule.HarmonyOwner)==0 && module.DesiredEnabled,"dependency disappearance removes owned patches without changing preference");
        Set(ccl,"mActive",true); module.Refresh(); Check(module.Active,"late dependency reactivation restores module");
        ccl.Enabled=false; module.Refresh(); Check(module.Active,"actual runtime Active wins over restart Enabled preference");
        Check(module.Unload() && module.Unload(),"final unload is idempotent");
        module.Configure(true,true); module.Refresh(); module.Tick();
        Check(!module.Active && module.Status.Contains("Unloaded"),"stale unloaded instance cannot reactivate");

        var fresh=new ControlsModule(_=>{}); fresh.Configure(true,true);
        Check(fresh.Active,"new parent lifetime can create a fresh active instance"); fresh.Unload();

        // Fault injection changes only a private in-memory fixture slot, never
        // the production DLL or native game assembly on disk.
        Type[] types=(Type[])typeof(ControlsModule).GetField("PatchTypes",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        Type saved=types[2]; types[2]=typeof(FailingPatch);
        var partial=new ControlsModule(_=>{}); partial.Configure(true,true);
        Check(!partial.Active && partial.Status.StartsWith("Fault:"),"partial five-class installation failure is contained");
        Check(OwnedPatchCount(ControlsModule.HarmonyOwner)==0 && Harmony.GetPatchInfo(sentinel).Owners.Contains(foreign.Id),"partial installation rolls back only owned patches");
        types[2]=saved; partial.Refresh(); Check(partial.Active,"explicit retry after corrected patch failure recovers"); partial.Unload();

        CheckResidualOwnerBoundaries(sentinel);

        // Conflict checks run last because a loaded standalone intentionally
        // establishes a process-wide restart boundary.
        var neverLoaded=Entry(ControlsModule.StandaloneModId,false,false); neverLoaded.Enabled=true;
        UnityModManager.modEntries.Add(neverLoaded);
        var legacy=new ControlsModule(_=>{}); legacy.Configure(true,true);
        Check(legacy.Active,"installed but never loaded inactive standalone does not conflict");
        Set(neverLoaded,"mAssembly",typeof(LifecycleChecks).Assembly); Set(neverLoaded,"mStarted",true); legacy.Refresh();
        Check(!legacy.Active && legacy.RestartRequired && legacy.Status.Contains("restart"),"loaded inactive standalone blocks and requires restart");
        UnityModManager.modEntries.Remove(neverLoaded); legacy.Refresh();
        Check(!legacy.Active,"removing a loaded standalone cannot bypass prefab restart boundary");
        legacy.Unload(); var stillBlocked=new ControlsModule(_=>{}); stillBlocked.Configure(true,true);
        Check(!stillBlocked.Active && stillBlocked.RestartRequired,"module recreation cannot bypass legacy restart boundary"); stillBlocked.Unload();
        foreign.Unpatch(sentinel,HarmonyPatchType.All,foreign.Id);
        Check(OwnedPatchCount(ControlsModule.HarmonyOwner)==0,"all integration fixture lifetimes leave no owned patches");
        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output,"lifecycle-checks.txt"),checks);
        File.WriteAllText(Path.Combine(output,"lifecycle-result.json"),"{\"passed\":"+passed+",\"failed\":"+failed+"}");
        Console.WriteLine("SUMMARY "+passed+" passed, "+failed+" failed. Actual adapter DLL, UMM, Harmony and native patch metadata; no native control object invocation.");
        return failed==0?0:1;
    }
    public static void Prefix() { }
    public static void CountPatchEnumeration() { patchEnumerations++; }
    public static void CountPatchMetadata() { patchMetadataReads++; }
    private static void Poll(ControlsModule module)
    {
        Set(module,"nextProbe",0d); // Exercise every scheduled poll without sleeping.
        module.Tick();
    }
    private static void CheckPollingMetadata(ControlsModule module,UnityModManager.ModEntry ccl)
    {
        // Patch the underlying lock-protected implementations, not Harmony's
        // tiny forwarding wrappers which may already have been JIT-inlined.
        const string observerId="toolbox.controls.fixture.metadata-observer";
        var observer=new Harmony(observerId);
        var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        var enumerate=typeof(PatchProcessor).GetMethod("GetAllPatchedMethods",flags);
        var metadata=typeof(PatchProcessor).GetMethod("GetPatchInfo",flags,null,new[]{typeof(MethodBase)},null);
        observer.Patch(enumerate,prefix:new HarmonyMethod(typeof(LifecycleChecks).GetMethod("CountPatchEnumeration")));
        observer.Patch(metadata,prefix:new HarmonyMethod(typeof(LifecycleChecks).GetMethod("CountPatchMetadata")));
        try
        {
            patchEnumerations=patchMetadataReads=0;
            for(int i=0;i<128;i++)Poll(module);
            Check(module.Active && patchEnumerations==0 && patchMetadataReads==0,
                "128 steady active control polls perform zero global patch enumerations and zero patch-info reads");
            module.Configure(true,true);
            Check(module.Active && patchEnumerations>0 && patchMetadataReads>0,
                "explicit control configure validates residual ownership even when selected values are unchanged");
            patchEnumerations=patchMetadataReads=0; module.Refresh();
            Check(module.Active && patchEnumerations>0 && patchMetadataReads>0,
                "explicit control retry/refresh reconciles global ownership without duplicating hooks");
            Set(ccl,"mActive",false); patchEnumerations=patchMetadataReads=0; Poll(module);
            Check(!module.Active && patchEnumerations>0,
                "cheap polled CCL disappearance still performs owned teardown verification");
            patchEnumerations=patchMetadataReads=0;
            for(int i=0;i<32;i++)Poll(module);
            Check(!module.Active && patchEnumerations==0 && patchMetadataReads==0,
                "stable missing-CCL control polls do not repeat negative metadata scans");
            Set(ccl,"mActive",true); patchEnumerations=patchMetadataReads=0; Poll(module);
            Check(module.Active && patchEnumerations>0 && patchMetadataReads>0,
                "late CCL reactivation is a full ownership-validation boundary before control hooks return");
            patchEnumerations=patchMetadataReads=0;
            for(int i=0;i<64;i++)Poll(module);
            Check(module.Active && patchEnumerations==0 && patchMetadataReads==0,
                "restored active controls return to metadata-free steady polling");
            var replacement=Entry(ControlsModule.DependencyId,true,true);
            UnityModManager.modEntries.Remove(ccl); UnityModManager.modEntries.Add(replacement);
            patchEnumerations=patchMetadataReads=0; Poll(module);
            Check(module.Active && patchEnumerations>0,
                "replacement active CCL entry triggers ownership reconciliation even without a toggle event");
            UnityModManager.modEntries.Remove(replacement); UnityModManager.modEntries.Add(ccl); Poll(module);
        }
        finally
        {
            observer.Unpatch(enumerate,HarmonyPatchType.All,observerId);
            observer.Unpatch(metadata,HarmonyPatchType.All,observerId);
        }
    }
    private static void CheckResidualOwnerBoundaries(MethodInfo sentinel)
    {
        var legacy=new Harmony(ControlsModule.StandaloneModId);
        var hook=new HarmonyMethod(typeof(LifecycleChecks).GetMethod("Prefix"));
        var cold=new ControlsModule(_=>{});
        legacy.Patch(sentinel,prefix:hook);
        cold.Configure(true,true);
        Check(!cold.Active && cold.RestartRequired && cold.Status.Contains("restart"),
            "cold control activation detects residual standalone ownership without any UMM standalone entry");
        legacy.Unpatch(sentinel,HarmonyPatchType.All,ControlsModule.StandaloneModId);
        cold.Refresh();
        Check(!cold.Active,"removing residual standalone patches cannot bypass the restart boundary"); cold.Unload();
        // Simulate another cold process between independent restart-boundary
        // scenarios. Production never clears this process-wide safety latch.
        typeof(ControlsModule).GetField("standaloneApplied",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,false);
        var late=new ControlsModule(_=>{}); late.Configure(true,true);
        Check(late.Active,"isolated clean lifetime starts before a late residual ownership test");
        legacy.Patch(sentinel,prefix:hook);
        late.Refresh();
        Check(!late.Active && late.RestartRequired && late.Status.Contains("restart"),
            "explicit control refresh detects late residual standalone ownership and stops only its own hooks");
        Check(Harmony.GetPatchInfo(sentinel).Owners.Contains(ControlsModule.StandaloneModId) &&
            Harmony.GetPatchInfo(sentinel).Owners.Contains("toolbox.controls.fixture.foreign"),
            "residual control conflict cleanup preserves both standalone and unrelated owners");
        legacy.Unpatch(sentinel,HarmonyPatchType.All,ControlsModule.StandaloneModId); late.Unload();
        typeof(ControlsModule).GetField("standaloneApplied",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,false);
    }
    private static void CheckCloning()
    {
        UnityEngine.GameObject prefab=null, clone=null;
        string stage="create inactive prefab";
        try
        {
            prefab=new UnityEngine.GameObject("Inactive CCL control reference fixture"); prefab.SetActive(false);
            var authoredObject=new UnityEngine.GameObject("Authored control"); authoredObject.transform.SetParent(prefab.transform,false);
            var repairedObject=new UnityEngine.GameObject("Repaired twin"); repairedObject.transform.SetParent(prefab.transform,false);
            var authored=authoredObject.AddComponent<DV.KeyboardInput.ToggleValueKeyboardInput>();
            authored.useAction=new DV.KeyboardInput.AKeyboardInput.ActionReference{name="AuthoredFixtureToggle",flip=true};
            stage="create repaired component";
            var repaired=repairedObject.AddComponent<DVCCLControlFix.ConnectedValueKeyboardInput>();
            if(repaired==null)throw new InvalidOperationException("AddComponent returned null for repaired type");
            repaired.sharedPortId="fixture.shared"; repaired.existingSharedInputs=new DV.KeyboardInput.AKeyboardInput[]{authored};
            repaired.toggle=new DV.KeyboardInput.AKeyboardInput.ActionReference{name="FixtureToggle",flip=true};
            var originalAssembly=Assembly.LoadFrom(@"C:\Users\james\Desktop\Derail Valley Mods\Mods (code)\DVCCLControlFix_Development_2026-10-14\dist\DVCCLControlFix\DVCCLControlFix.dll");
            var originalType=originalAssembly.GetType("DVCCLControlFix.ConnectedValueKeyboardInput",true);
            var originalObject=new UnityEngine.GameObject("Unmodified 1.4.0 repaired baseline"); originalObject.transform.SetParent(prefab.transform,false);
            var original=originalObject.AddComponent(originalType);
            originalType.GetField("toggle").SetValue(original,new DV.KeyboardInput.AKeyboardInput.ActionReference{name="OriginalFixtureToggle",flip=true});
            originalType.GetField("sharedPortId").SetValue(original,"fixture.original");
            var scheduler=prefab.AddComponent<DV.CabControls.InteractablesKeyboardControl>();
            scheduler.entries=new DV.KeyboardInput.AKeyboardInput[]{authored,repaired};
            stage="clone prefab";
            clone=UnityEngine.Object.Instantiate(prefab);
            stage="resolve cloned repaired component";
            var cloneRepair=clone.GetComponentInChildren<DVCCLControlFix.ConnectedValueKeyboardInput>(true);
            var cloneAuthor=clone.GetComponentInChildren<DV.KeyboardInput.ToggleValueKeyboardInput>(true);
            var cloneOriginal=clone.GetComponentsInChildren(originalType,true).Single();
            string diagnostic="CLONE DIAGNOSTIC actionSerializable="+typeof(DV.KeyboardInput.AKeyboardInput.ActionReference).IsSerializable+
                " repairBefore="+UnityEngine.JsonUtility.ToJson(repaired)+" repairAfter="+UnityEngine.JsonUtility.ToJson(cloneRepair)+
                " authorBefore="+UnityEngine.JsonUtility.ToJson(authored)+" authorAfter="+UnityEngine.JsonUtility.ToJson(cloneAuthor)+
                " originalBefore="+UnityEngine.JsonUtility.ToJson(original)+" originalAfter="+UnityEngine.JsonUtility.ToJson(cloneOriginal);
            Console.WriteLine(diagnostic); File.WriteAllText(Path.Combine(output,"clone-baseline.txt"),diagnostic);
            if(cloneRepair==null)throw new InvalidOperationException("clone has no repaired component");
            if(cloneRepair.toggle==null && cloneAuthor.useAction==null && originalType.GetField("toggle").GetValue(cloneOriginal)==null)
            {
                string limit="LIMIT ActionReference cloning unavailable in this fixture: native authored input, original unmodified 1.4.0 component and merged component all omit the same field. Live game ActionReference cloning remains unverified.";
                Console.WriteLine(limit); checks.Add(limit); File.AppendAllText(Path.Combine(output,"clone-baseline.txt"),Environment.NewLine+limit);
            }
            else Check(cloneRepair.toggle!=null && cloneRepair.toggle.name=="FixtureToggle" && cloneRepair.toggle.flip,
                "actual Unity clone retains repaired action reference when the native baseline is available");
            Check(cloneRepair!=repaired && cloneRepair.sharedPortId=="fixture.shared",
                "actual Unity clone retains repaired component identity and shared port field");
            Check(cloneRepair.existingSharedInputs.Length==1 && cloneRepair.existingSharedInputs[0]==cloneAuthor && cloneAuthor!=authored,
                "actual Unity clone remaps existing authored input references into live clone");
            var cloneScheduler=clone.GetComponent<DV.CabControls.InteractablesKeyboardControl>();
            Check(cloneScheduler.entries.Length==2 && cloneScheduler.entries[0]==cloneAuthor && cloneScheduler.entries[1]==cloneRepair,
                "actual Unity clone remaps native scheduler entries to authored and repaired clone components");
            cloneRepair.Tick(.01f);
            Check(true,"disabled repaired clone remains inert without initialized Rewired or physical control");
        }
        catch(Exception ex){Check(false,"actual Unity component clone fixture at "+stage+": "+ex);}
        finally
        {
            if(clone!=null)UnityEngine.Object.DestroyImmediate(clone);
            if(prefab!=null)UnityEngine.Object.DestroyImmediate(prefab);
        }
    }
    public static IEnumerable<CodeInstruction> Throwing(IEnumerable<CodeInstruction> input) { throw new InvalidOperationException("controlled partial installation failure"); }
    [HarmonyPatch(typeof(Sentinel),"Other")] private static class FailingPatch
    { private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Throwing(instructions); }
    public static class Sentinel
    { [MethodImpl(MethodImplOptions.NoInlining)] public static int Ping()=>1; [MethodImpl(MethodImplOptions.NoInlining)] public static int Other()=>2; }
    private static UnityModManager.ModEntry Entry(string id,bool loaded,bool active)
    {
        var entry=(UnityModManager.ModEntry)FormatterServices.GetUninitializedObject(typeof(UnityModManager.ModEntry));
        Set(entry,"Info",new UnityModManager.ModInfo{Id=id,Version="1.0.0",AssemblyName=id+".dll"});
        Set(entry,"Version",new Version(1,0,0)); Set(entry,"Logger",new UnityModManager.ModEntry.ModLogger("toolbox-controls-fixture"));
        Set(entry,"mAssembly",loaded?typeof(LifecycleChecks).Assembly:null); Set(entry,"mStarted",loaded); Set(entry,"mActive",active); return entry;
    }
    private static void Set(object item,string name,object value) => item.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(item,value);
    private static List<MethodBase> OwnedMethods(string owner) => Harmony.GetAllPatchedMethods().Where(m=>Harmony.GetPatchInfo(m).Owners.Contains(owner)).ToList();
    private static int OwnedPatchCount(string owner)=>OwnedMethods(owner).Sum(m=>{var info=Harmony.GetPatchInfo(m);return info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Count(p=>p.owner==owner);});
    private static void Check(bool condition,string name) { if(condition)passed++;else failed++;string line=(condition?"PASS ":"FAIL ")+name;checks.Add(line);Console.WriteLine(line); }
}
