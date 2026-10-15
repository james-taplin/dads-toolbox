using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DadsToolbox.H6F;
using DadsToolbox.HudGuard;
using HarmonyLib;
using UnityModManagerNet;

public static class IntegrationTests
{
    private static string gameRoot;
    private static int passed, failed, observed;
    private static Type car, variant;
    private static MethodInfo add;
    private static string observedId;
    private static readonly List<string> logs = new List<string>();
    public static int Main(string[] args)
    {
        gameRoot=args[0];AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
        try{return Run(args[1]);}catch(Exception ex){Console.WriteLine("HARNESS ERROR "+ex);return 2;}
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string file=new AssemblyName(args.Name).Name+".dll";
        foreach(string dir in new[]{"DerailValley_Data/Managed","DerailValley_Data/Managed/UnityModManager","Mods/DVCustomCarLoader","Mods/DVLangHelper","Mods/HeadsUpDisplay_994"})
        {string path=Path.Combine(gameRoot,dir.Replace('/',Path.DirectorySeparatorChar),file);if(File.Exists(path))return Assembly.LoadFrom(path);}
        return null;
    }
    private static void Check(string name,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(ok)passed++;else failed++;}
    private static FieldInfo Field(Type type,string name){return type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);}
    private static object Empty(Type type){return FormatterServices.GetUninitializedObject(type);}
    private static UnityModManager.ModEntry Entry(string id, Assembly assembly, bool loaded,bool active,bool enabled)
    {
        var e=(UnityModManager.ModEntry)Empty(typeof(UnityModManager.ModEntry));
        Field(e.GetType(),"Info").SetValue(e,new UnityModManager.ModInfo{Id=id,AssemblyName=assembly==null?null:assembly.GetName().Name+".dll"});
        Field(e.GetType(),"mAssembly").SetValue(e,assembly);Field(e.GetType(),"mStarted").SetValue(e,loaded);Field(e.GetType(),"mActive").SetValue(e,active);e.Enabled=enabled;return e;
    }
    private static void Set(object instance,string name,object value)
    {
        FieldInfo field=Field(instance.GetType(),name);
        if(field!=null){field.SetValue(instance,value);return;}
        instance.GetType().GetProperty(name).GetSetMethod(true).Invoke(instance,new[]{value});
    }
    private static object Variant(string id,string[] ids)
    {object v=Empty(variant);Set(v,"id",id);Set(v,"TrainsetLiveries",ids);return v;}
    private static object Car(string id,params object[] variants)
    {
        object c=Empty(car);Set(c,"id",id);FieldInfo list=Field(car,"liveries");
        if(list.FieldType.IsArray)
        {
            Array array=Array.CreateInstance(list.FieldType.GetElementType(),variants.Length);
            for(int i=0;i<variants.Length;i++)array.SetValue(variants[i],i);
            list.SetValue(c,array);
        }
        else
        {
            IList values=(IList)Activator.CreateInstance(list.FieldType);
            foreach(object v in variants)values.Add(v);
            list.SetValue(c,values);
        }
        return c;
    }
    private static string[] Ids(object v){return (string[])Field(variant,"TrainsetLiveries").GetValue(v);}
    private static bool Has(MethodBase m,string id)
    {var p=Harmony.GetPatchInfo(m);return p!=null && p.Owners.Contains(id);}
    private static int OwnCount()
    {var p=Harmony.GetPatchInfo(add);int n=0;if(p!=null)foreach(Patch item in p.Prefixes)if(item.owner==H6FModule.HarmonyId)n++;return n;}
    private static void RaiseToggle(UnityModManager.ModEntry entry,bool active)
    {
        var field=typeof(UnityModManager).GetField("toggleModsListen",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        var callback=(Delegate)field.GetValue(null);if(callback!=null)callback.DynamicInvoke(entry,active);
    }
    public static void Observe(object c)
    {
        observed++;observedId=null;
        if(c==null)return;
        foreach(object v in (IEnumerable)car.GetProperty("Variants").GetValue(c,null))
        {string[] ids=Ids(v);if(ids!=null && ids.Length>0){observedId=ids[0];break;}}
    }
    public static IEnumerable<CodeInstruction> NativeConsumerStub(IEnumerable<CodeInstruction> original)
    {
        // Keep the actual installed method and production prefix. Replace only
        // its downstream game-registry consumer, which needs a Unity player.
        return new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Call,typeof(IntegrationTests).GetMethod("Observe")),new CodeInstruction(OpCodes.Ret)};
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Sentinel(){ }
    public static void ForeignPrefix(){ }
    private static void Call(object c){try{add.Invoke(null,new[]{c});}catch(TargetInvocationException ex){throw ex.InnerException;}}

    [MethodImpl(MethodImplOptions.NoInlining)] private static int Run(string output)
    {
        Directory.CreateDirectory(output);UnityModManager.modEntries.Clear();
        var module=new H6FModule(logs.Add);var hudModule=new HudGuardModule(logs.Add);
        var consumer=new Harmony("h6f.tests.consumer");var legacy=new Harmony("jimtaplin.derailvalley.rollingstockcompatibility");
        var foreign=new Harmony("h6f.tests.foreign");
        try
        {
            module.Configure(true,true);
            Check("Absent H6F remains inactive and retains desired preference",module.State==H6FState.WaitingForH6F && module.DesiredEnabled);
            Check("Absent H6F creates no Harmony instance or target binding",Field(typeof(H6FModule),"harmony").GetValue(module)==null && Field(typeof(H6FModule),"carType").GetValue(module)==null);
            Check("Absent H6F registers no activation listener",!(bool)Field(typeof(H6FModule),"listening").GetValue(module));
            bool noOptional=true;foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if(a.GetName().Name=="CCL.Importer" || a.GetName().Name=="Assembly-CSharp" || a.GetName().Name=="HeadsUpDisplay")noOptional=false;
            Check("Absent H6F does not load CCL, game or HUD assemblies",noOptional);
            var h6f=Entry(H6FModule.H6FModId,null,true,false,false);UnityModManager.modEntries.Add(h6f);module.Refresh();
            Check("Disabled installed H6F does not arm a hook",module.State==H6FState.WaitingForH6F && Field(typeof(H6FModule),"harmony").GetValue(module)==null);
            h6f.Enabled=true;Set(h6f,"mStarted",false);module.Refresh();
            Check("Registered enabled H6F waits for CCL",module.State==H6FState.WaitingForCcl);
            Assembly ccl=Assembly.LoadFrom(Path.Combine(gameRoot,"Mods","DVCustomCarLoader","CCL.Importer.dll"));
            var cclEntry=Entry(H6FModule.CclModId,ccl,true,false,true);UnityModManager.modEntries.Add(cclEntry);module.Refresh();
            Check("Loaded inactive CCL cannot arm a hook",module.State==H6FState.WaitingForCcl);
            Set(cclEntry,"mActive",true);Set(cclEntry,"mAssembly",typeof(IntegrationTests).Assembly);module.Refresh();
            Check("Incompatible CCL is explained",module.State==H6FState.IncompatibleCcl);
            Set(cclEntry,"mAssembly",ccl);
            car=ccl.GetType("CCL.Importer.Types.CCL_CarType",true);variant=ccl.GetType("CCL.Importer.Types.CCL_CarVariant",true);
            add=ccl.GetType("CCL.Importer.CarManager",true).GetMethod("AddTrainsets",BindingFlags.Static|BindingFlags.NonPublic);
            consumer.Patch(add,transpiler:new HarmonyMethod(typeof(IntegrationTests).GetMethod("NativeConsumerStub")));
            Console.WriteLine("NATIVE BOUNDARY: actual CCL AddTrainsets method and mapped runtime types; downstream Unity registry consumer replaced with a fixture observer.");
            RaiseToggle(cclEntry,true);
            Check("Installed enabled H6F arms before its first import",module.State==H6FState.Armed && OwnCount()==1);
            object v=Variant("em_h6f_main",new[]{"h6f_main","em_h6f_tender"});object c=Car("em_h6f_main",v);
            Call(c);
            Check("Armed hook never repairs an inactive H6F",Ids(v)[0]=="h6f_main" && module.RepairedEntryCount==0 && observedId=="h6f_main");
            Set(h6f,"mStarted",true);Set(h6f,"mActive",true);
            Call(c);
            Check("Real data-only H6F activation repairs before native consumer",h6f.Loaded && h6f.Active && Ids(v)[0]=="em_h6f_main" && observedId=="em_h6f_main");
            RaiseToggle(h6f,true);Check("UMM activation event updates state without waiting for a frame",module.State==H6FState.Enabled);
            Check("Only bad ID is corrected; tender preserved",Ids(v)[1]=="em_h6f_tender" && module.RepairedEntryCount==1);
            Check("Real target still dispatches its consumer",observed==2);
            Check("Metadata changes truthfully require restart to undo",module.RestartRequired);
            module.Refresh();Check("Active H6F reports enabled",module.State==H6FState.Enabled);
            for(int i=0;i<20;i++){module.Configure(true,true);module.Refresh();Call(c);}
            Check("Repeated enables have one prefix and no duplicate repair",OwnCount()==1 && module.RepairedEntryCount==1);
            int repairLogs=logs.FindAll(delegate(string s){return s.StartsWith("H6F: corrected");}).Count;
            Check("Repair diagnostic is emitted once per activation",repairLogs==1);
            object other=Variant("em_h6f_main",new[]{"h6f_main","em_h6f_tender"});Call(Car("other_car",other));
            Check("Other car with matching variant ID is untouched",Ids(other)[0]=="h6f_main");
            object wrong=Variant("other_variant",new[]{"h6f_main","em_h6f_tender"});Call(Car("em_h6f_main",wrong));
            Check("Other variant on H6F car is untouched",Ids(wrong)[0]=="h6f_main");
            object upper=Variant("EM_H6F_MAIN",new[]{"h6f_main"});Call(Car("em_h6f_main",upper));
            Check("IDs are matched exactly",Ids(upper)[0]=="h6f_main");
            Call(null);Call(Car("em_h6f_main",new object[]{null}));
            object empty=Variant("em_h6f_main",null);Call(Car("em_h6f_main",empty));
            Check("Null car, null variants and null trainset are safe",Ids(empty)==null);
            object multi=Variant("em_h6f_main",new[]{"h6f_main",null,"em_h6f_tender","h6f_main","custom"});Call(Car("em_h6f_main",multi));
            Check("All bad entries repaired while order and unrelated values survive",Ids(multi)[0]=="em_h6f_main" && Ids(multi)[1]==null && Ids(multi)[2]=="em_h6f_tender" && Ids(multi)[3]=="em_h6f_main" && Ids(multi)[4]=="custom");
            int repairs=module.RepairedEntryCount;Call(c);
            Check("Corrected/future-fixed metadata is a no-op",module.RepairedEntryCount==repairs);
            Set(cclEntry,"mActive",false);
            object pending=Variant("em_h6f_main",new[]{"h6f_main"});Call(Car("em_h6f_main",pending));
            Check("CCL deactivation blocks repair before periodic unpatch",Ids(pending)[0]=="h6f_main");module.Refresh();
            Check("CCL deactivation removes only own hook",module.State==H6FState.WaitingForCcl && OwnCount()==0 && Has(add,consumer.Id));
            Set(cclEntry,"mActive",true);module.Refresh();
            Set(h6f,"mActive",false);Call(Car("em_h6f_main",pending));
            Check("H6F deactivation blocks repair before periodic unpatch",Ids(pending)[0]=="h6f_main");module.Refresh();
            Check("Loaded inactive H6F is unpatched",module.State==H6FState.WaitingForH6F && OwnCount()==0 && module.DesiredEnabled);
            int count=logs.Count;for(int i=0;i<20;i++)module.Refresh();Check("Unchanged absent/inactive status has bounded logging",logs.Count==count);
            Set(h6f,"mActive",true);h6f.Enabled=false;module.Refresh();
            Check("Actual active H6F is authoritative over checkbox",module.State==H6FState.Enabled && OwnCount()==1);
            var second=new H6FModule(logs.Add);second.Configure(true,true);Check("A second H6F instance is blocked",second.State==H6FState.BlockedByOtherInstance);
            second.Unload();Check("Second instance cleanup preserves first",OwnCount()==1);
            ModuleIndependence(module,hudModule,output);

            Assembly old=Assembly.LoadFrom(Path.Combine(gameRoot,"Mods","DV_RSCompatibility","DVRollingStockCompatibility.dll"));
            old.GetType("DVRollingStockCompatibility.H6FFix",true).GetMethod("Install",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{legacy});
            module.Refresh();
            Check("Actual legacy H6F prefix blocks duplication",module.State==H6FState.BlockedByStandalone && OwnCount()==0 && Has(add,legacy.Id));
            object legacyVariant=Variant("em_h6f_main",new[]{"h6f_main","em_h6f_tender"});Call(Car("em_h6f_main",legacyVariant));
            Check("Separate legacy H6F repair still functions when module is blocked",Ids(legacyVariant)[0]=="em_h6f_main" && observedId=="em_h6f_main");
            module.Stop();Check("H6F stop leaves legacy repair intact",Has(add,legacy.Id) && Has(add,consumer.Id));
            legacy.Unpatch(add,HarmonyPatchType.Prefix,legacy.Id);
            var oldEntry=Entry("DVRollingStockCompatibility",old,true,true,true);UnityModManager.modEntries.Add(oldEntry);module.Configure(true,true);
            Check("Active legacy entry blocks before patching",module.State==H6FState.BlockedByStandalone && OwnCount()==0);
            Set(oldEntry,"mActive",false);module.Refresh();Check("Legacy removal permits H6F recovery",OwnCount()==1);
            UnityModManager.modEntries.Remove(oldEntry);
            module.Configure(false,true);
            object off=Variant("em_h6f_main",new[]{"h6f_main"});Call(Car("em_h6f_main",off));
            Check("Independent H6F switch stops future repair",OwnCount()==0 && Ids(off)[0]=="h6f_main");
            Check("H6F option off removes its activation listener",!(bool)Field(typeof(H6FModule),"listening").GetValue(module));
            Check("Disabling preserves earlier data and restart-required status",Ids(v)[0]=="em_h6f_main" && module.RestartRequired && module.Status.Contains("restart"));
            UnityModManager.modEntries.Remove(h6f);module.Configure(true,true);
            Check("Removing H6F makes module inactive even with CCL present",module.State==H6FState.WaitingForH6F && OwnCount()==0);
            UnityModManager.modEntries.Add(h6f);System.Threading.Thread.Sleep(1100);module.Tick();
            Check("Late H6F recovery runs through throttled Tick",module.State==H6FState.Enabled && OwnCount()==1);
            var throwing=new H6FModule(delegate(string message){throw new Exception("broken logger");});
            module.Stop();throwing.Configure(true,true);Check("Logger failure cannot prevent H6F enable",OwnCount()==1);throwing.Unload();
            module.Configure(true,true);module.Stop();
            var collision=new Harmony(H6FModule.HarmonyId);MethodInfo sentinel=typeof(IntegrationTests).GetMethod("Sentinel");
            collision.Patch(sentinel,prefix:new HarmonyMethod(typeof(IntegrationTests).GetMethod("ForeignPrefix")));module.Configure(true,true);
            Check("Reserved Harmony ID collision is blocked",module.State==H6FState.BlockedByOtherInstance && OwnCount()==0);
            module.Stop();Check("Cleanup preserves an unowned colliding target",Has(sentinel,H6FModule.HarmonyId));collision.Unpatch(sentinel,HarmonyPatchType.Prefix,H6FModule.HarmonyId);
            module.Configure(true,true);Check("H6F unload cleans own patch",module.Unload() && OwnCount()==0 && Has(add,consumer.Id));
            module.Configure(true,true);module.Refresh();module.Tick();Check("Unloaded module cannot reactivate",module.State==H6FState.Unloaded && module.Unload());
            foreach(AssemblyName reference in typeof(H6FModule).Assembly.GetReferencedAssemblies())
                Check("No hard optional CCL/H6F/HUD reference in combined DLL: "+reference.Name,reference.Name!="CCL.Importer" && reference.Name!="CCL.Types" && reference.Name!="HeadsUpDisplay");
        }
        finally
        {
            module.Unload();hudModule.Unload();foreach(Harmony h in new[]{consumer,legacy,foreign})h.UnpatchAll(h.Id);
            UnityModManager.modEntries.Clear();
        }
        Check("No H6F module patches survive suite",!Harmony.HasAnyPatches(H6FModule.HarmonyId));
        File.WriteAllLines(Path.Combine(output,"module-log.txt"),logs.ToArray());
        Console.WriteLine("RESULT passed="+passed+" failed="+failed+"; actual installed CCL mapped types/target and UMM/Harmony, downstream native consumer substituted; no game run.");
        return failed==0?0:1;
    }

    private static void ModuleIndependence(H6FModule h6f,HudGuardModule hud,string output)
    {
        Assembly.LoadFrom(Path.Combine(gameRoot,"DerailValley_Data","Managed","Assembly-CSharp.dll"));
        Assembly assembly=Assembly.LoadFrom(Path.Combine(gameRoot,"Mods","HeadsUpDisplay_994","HeadsUpDisplay.dll"));
        UnityModManager.modEntries.Add(Entry(HudGuardModule.HudModId,assembly,true,true,true));
        var parent=Entry("tests.toolbox",typeof(IntegrationTests).Assembly,true,true,true);
        string dir=Path.Combine(output,"settings");Directory.CreateDirectory(dir);Set(parent,"Path",dir+Path.DirectorySeparatorChar);
        for(int mask=0;mask<4;mask++)
        {
            var settings=new PackSettings{HudGuard=(mask&1)!=0,H6FRepair=(mask&2)!=0,OtherOption=41};
            settings.Save(parent);var saved=UnityModManager.ModSettings.Load<PackSettings>(parent);
            Check("Independent saved submod options round-trip mask "+mask,saved.HudGuard==settings.HudGuard && saved.H6FRepair==settings.H6FRepair && saved.OtherOption==41);
            hud.Configure(saved.HudGuard,true);h6f.Configure(saved.H6FRepair,true);
            Check("HUD has independent effective state mask "+mask,(hud.State==HudGuardState.Enabled)==saved.HudGuard);
            Check("H6F has independent effective state mask "+mask,(OwnCount()==1)==saved.H6FRepair);
        }
        h6f.Stop();Check("Stopping H6F preserves active HUD guard",hud.State==HudGuardState.Enabled && Harmony.HasAnyPatches(HudGuardModule.HarmonyId));
        h6f.Configure(true,true);hud.Stop();Check("Stopping HUD preserves active H6F repair",OwnCount()==1);
        hud.Unload();
    }
}
public sealed class PackSettings:UnityModManager.ModSettings
{
    public bool HudGuard=true,H6FRepair=true;
    public int OtherOption=17;
    public override void Save(UnityModManager.ModEntry entry){Save(this,entry);}
}

