using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DadsToolbox.HudGuard;
using HarmonyLib;
using UnityModManagerNet;

public static class IntegrationTests
{
    private static int passed,failed;
    private static readonly List<string> logs=new List<string>();
    public static int Main(string[] args)
    {
        NativeHud.ResolveFrom(args[0]);
        try { return Run(args[1]); }
        catch(Exception ex){Console.WriteLine("HARNESS ERROR "+ex);return 2;}
    }
    private static void Check(string name,bool ok)
    { Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(ok)passed++;else failed++; }
    private static bool ThrowsNre(MethodInfo method,object car)
    { try{NativeHud.Invoke(method,car);return false;}catch(NullReferenceException){return true;} }
    private static int Diagnostics()
    { int n=0;foreach(string s in logs)if(s.StartsWith("HUD: readout unavailable"))n++;return n; }
    private static bool AllOwned(bool expected)
    {foreach(MethodInfo m in NativeHud.Providers)if(NativeHud.Has(m,HudGuardModule.HarmonyId)!=expected)return false;return true;}

    [MethodImpl(MethodImplOptions.NoInlining)] private static int Run(string output)
    {
        Directory.CreateDirectory(output);
        NativeHud.Load();
        UnityModManager.modEntries.Clear();
        var module=new HudGuardModule(logs.Add);
        var foreign=new Harmony("hudguard.tests.foreign");
        var speed=new Harmony("hudguard.tests.speed");
        var injected=new Harmony("hudguard.tests.injected");
        var duplicate=new Harmony("hudguard.tests.duplicate");
        var legacy=new Harmony("jimtaplin.derailvalley.rollingstockcompatibility");
        try
        {
            Check("Actual installed HUD has three providers",NativeHud.Providers.Count==3);
            module.Configure(true,true);
            Check("Absent HUD waits and retains desired preference",module.State==HudGuardState.WaitingForHud && module.DesiredEnabled && AllOwned(false));
            var hud=NativeHud.Entry(HudGuardModule.HudModId,null,false);
            UnityModManager.modEntries.Add(hud);module.Refresh();
            Check("Enabled checkbox without loaded HUD cannot install",module.State==HudGuardState.WaitingForHud && AllOwned(false));
            NativeHud.Field(hud.GetType(),"mAssembly").SetValue(hud,NativeHud.Hud);module.Refresh();
            Check("Loaded inactive HUD type cannot install",module.State==HudGuardState.WaitingForHud && AllOwned(false));
            NativeHud.Field(hud.GetType(),"mActive").SetValue(hud,true);
            NativeHud.Field(hud.GetType(),"mAssembly").SetValue(hud,typeof(IntegrationTests).Assembly);module.Refresh();
            Check("Active incompatible HUD is explained and unpatched",module.State==HudGuardState.IncompatibleHud && AllOwned(false));
            NativeHud.Field(hud.GetType(),"mAssembly").SetValue(hud,NativeHud.Hud);
            DiscoveryBoundaries();
            foreach(MethodInfo m in NativeHud.Providers)Check("Native missing simulation originally throws "+m.Name,ThrowsNre(m,NativeHud.UnsupportedCar));

            MethodInfo getSpeed=NativeHud.Game.GetType("TrainCar").GetMethod("GetForwardSpeed",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            NativeHud.SpeedMethod=getSpeed;
            MethodInfo power=NativeHud.Providers.Find(delegate(MethodInfo m){return m.ReturnType.IsGenericType && m.ReturnType.GetGenericArguments()[0].Name=="Power";});
            speed.Patch(power,transpiler:new HarmonyMethod(typeof(NativeHud).GetMethod("SpeedCode")));
            Check("Fixture replaces exactly the native Power speed-input call",NativeHud.SpeedReplacements==1);
            Console.WriteLine("POWER INPUT: native Power lambda with only its GetForwardSpeed call replaced by a fixture constant 36; actual quantity calculation/query/formatting. No Unity Rigidbody/Transform is hosted.");
            var before=new Dictionary<MethodInfo,object>();
            foreach(MethodInfo m in NativeHud.Providers)
            {
                before[m]=NativeHud.Invoke(m,NativeHud.ValidCar);
                Check("Native valid provider returns data "+m.Name,before[m]!=null);
                Console.WriteLine("NATIVE VALUE "+m.Name+" "+before[m]);
                if(m.ReturnType!=typeof(float?))
                {
                    double value=(double)before[m].GetType().GetProperty("Scalar").GetValue(before[m],null);
                    double expected=m.ReturnType.GetGenericArguments()[0].Name=="Force"?125000:1250000;
                    Check("Native known quantity scalar "+m.Name,Math.Abs(value-expected)<.01);
                }
                else Check("Native known slip fraction",Object.Equals(before[m],.25f));
            }
            MethodInfo sentinel=typeof(NativeHud).GetMethod("Sentinel");
            foreign.Patch(sentinel,postfix:new HarmonyMethod(typeof(NativeHud).GetMethod("SentinelPostfix")));
            module.Refresh();
            Check("Active supported HUD enables all three providers",module.State==HudGuardState.Enabled && module.PatchedProviderCount==3 && AllOwned(true));
            for(int i=0;i<20;i++){module.Configure(true,true);module.Refresh();module.Tick();}
            foreach(MethodInfo m in NativeHud.Providers)Check("Repeated enable has exactly one finalizer "+m.Name,NativeHud.Count(m,HudGuardModule.HarmonyId)==1);
            Check("Other module patch remains functional",NativeHud.Sentinel()==14);
            foreach(MethodInfo m in NativeHud.Providers)
            {
                Check("Missing simulation becomes unavailable "+m.Name,NativeHud.Invoke(m,NativeHud.UnsupportedCar)==null);
                Check("Valid force/power/slip unchanged "+m.Name,Object.Equals(before[m],NativeHud.Invoke(m,NativeHud.ValidCar)));
                object query=NativeHud.Query(m);object[] values={NativeHud.UnsupportedCar,null};
                Check("Native query rejects unavailable "+m.Name,!(bool)query.GetType().GetMethod("TryGetValue").Invoke(query,values));
                object[] formatted={NativeHud.UnsupportedCar,null};
                Check("Native formatting omits unavailable "+m.Name,!(bool)query.GetType().GetMethod("TryGetFormatted").Invoke(query,formatted) && (string)formatted[1]=="");
                values=new object[]{NativeHud.ValidCar,null};
                Check("Native query retains valid value "+m.Name,(bool)query.GetType().GetMethod("TryGetValue").Invoke(query,values) && Object.Equals(values[1],before[m]));
                formatted=new object[]{NativeHud.ValidCar,null};
                Check("Native formatting retains valid value "+m.Name,(bool)query.GetType().GetMethod("TryGetFormatted").Invoke(query,formatted) && !String.IsNullOrEmpty((string)formatted[1]));
            }
            int diagnostics=Diagnostics();
            for(int i=0;i<20;i++)foreach(MethodInfo m in NativeHud.Providers)NativeHud.Invoke(m,NativeHud.UnsupportedCar);
            Check("Diagnostics are bounded to one per provider per activation",diagnostics==3 && Diagnostics()==3);
            MethodInfo target=NativeHud.Providers[0];
            injected.Patch(target,prefix:new HarmonyMethod(typeof(NativeHud).GetMethod("Inject")));
            NativeHud.ThrowMode=2;
            try {NativeHud.Invoke(target,NativeHud.ValidCar);Check("Unrelated exceptions remain visible",false);}
            catch(InvalidOperationException){Check("Unrelated exceptions remain visible",true);}
            NativeHud.ThrowMode=1;
            Check("Established broad NRE boundary is retained and documented",NativeHud.Invoke(target,NativeHud.ValidCar)==null);
            NativeHud.ThrowMode=0;
            injected.Unpatch(target,HarmonyPatchType.Prefix,injected.Id);

            module.Configure(false,true);
            Check("Option off immediately removes only HUD module patches",module.State==HudGuardState.Disabled && AllOwned(false) && NativeHud.Sentinel()==14);
            Check("Original missing-simulation exception returns after disable",ThrowsNre(target,NativeHud.UnsupportedCar));
            module.Configure(true,true);
            NativeHud.Invoke(target,NativeHud.UnsupportedCar);
            Check("Re-enable starts a fresh bounded diagnostic interval",Diagnostics()==4 && AllOwned(true));
            hud.Enabled=false;module.Refresh();
            Check("Runtime Active is authoritative over Enabled preference",module.State==HudGuardState.Enabled);
            NativeHud.Field(hud.GetType(),"mActive").SetValue(hud,false);module.Refresh();
            Check("HUD deactivation removes guards without clearing preference",module.State==HudGuardState.WaitingForHud && module.DesiredEnabled && AllOwned(false));
            int count=logs.Count;for(int i=0;i<20;i++)module.Refresh();
            Check("Unchanged unavailable state does not flood logs",logs.Count==count);
            NativeHud.Field(hud.GetType(),"mActive").SetValue(hud,true);
            System.Threading.Thread.Sleep(1100);module.Tick();
            Check("Late HUD activation recovers through throttled Tick",AllOwned(true) && module.State==HudGuardState.Enabled);
            NativeHud.Field(hud.GetType(),"mAssembly").SetValue(hud,typeof(IntegrationTests).Assembly);module.Refresh();
            Check("Incompatible assembly replacement removes prior guards",module.State==HudGuardState.IncompatibleHud && AllOwned(false));
            NativeHud.Field(hud.GetType(),"mAssembly").SetValue(hud,NativeHud.Hud);module.Refresh();
            UnityModManager.modEntries.Remove(hud);module.Refresh();
            Check("HUD removal clears guards while retaining preference",module.State==HudGuardState.WaitingForHud && module.DesiredEnabled && AllOwned(false));
            UnityModManager.modEntries.Add(hud);module.Refresh();
            var second=new HudGuardModule(logs.Add);second.Configure(true,true);
            Check("Second module instance is blocked",second.State==HudGuardState.BlockedByOtherInstance && second.PatchedProviderCount==0);
            second.Stop();second.Unload();
            Check("Stopping another instance preserves the active instance",AllOwned(true));

            module.Stop();
            var collision=new Harmony(HudGuardModule.HarmonyId);
            collision.Patch(sentinel,postfix:new HarmonyMethod(typeof(NativeHud).GetMethod("SentinelPostfix")));
            module.Configure(true,true);
            Check("Pre-existing reserved-owner collision blocks module",module.State==HudGuardState.BlockedByOtherInstance && AllOwned(false));
            module.Stop();
            Check("Reserved-owner conflict cleanup preserves unowned target",NativeHud.Has(sentinel,HudGuardModule.HarmonyId));
            collision.Unpatch(sentinel,HarmonyPatchType.Postfix,HudGuardModule.HarmonyId);
            module.Configure(true,true);
            Check("Reserved-owner conflict removal permits normal enable",module.State==HudGuardState.Enabled && AllOwned(true));

            MethodInfo finalizer=typeof(HudGuardModule).GetMethod("ProviderFinalizer",BindingFlags.NonPublic|BindingFlags.Static);
            duplicate.Patch(target,finalizer:new HarmonyMethod(finalizer));module.Refresh();
            Check("Conflicting reuse of finalizer stops this module only",module.State==HudGuardState.BlockedByOtherInstance && AllOwned(false) && NativeHud.Has(target,duplicate.Id));
            duplicate.Unpatch(target,HarmonyPatchType.Finalizer,duplicate.Id);module.Refresh();
            Check("Conflict removal allows explicit recovery",module.State==HudGuardState.Enabled && AllOwned(true));

            Assembly old=Assembly.LoadFrom(Path.Combine(NativeHud.GameRoot,"Mods","DV_RSCompatibility","DVRollingStockCompatibility.dll"));
            old.GetType("DVRollingStockCompatibility.HudLocoProviderFix",true).GetMethod("Install",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{legacy});
            legacy.Patch(sentinel,postfix:new HarmonyMethod(typeof(NativeHud).GetMethod("SentinelPostfix")));
            module.Refresh();
            Check("Actual separately compiled legacy guard blocks duplicate protection",module.State==HudGuardState.BlockedByStandalone && AllOwned(false));
            foreach(MethodInfo m in NativeHud.Providers)Check("Legacy patch survives module cleanup "+m.Name,NativeHud.Has(m,legacy.Id) && NativeHud.Invoke(m,NativeHud.UnsupportedCar)==null);
            Check("Legacy owner's unrelated patch also survives",NativeHud.Has(sentinel,legacy.Id));
            module.Configure(false,true);
            Check("Option off leaves standalone helper entirely alone",NativeHud.Has(target,legacy.Id) && NativeHud.Has(sentinel,legacy.Id));
            foreach(MethodInfo m in NativeHud.Providers)legacy.Unpatch(m,HarmonyPatchType.Finalizer,legacy.Id);
            legacy.Unpatch(sentinel,HarmonyPatchType.Postfix,legacy.Id);
            var oldEntry=NativeHud.Entry(HudGuardModule.StandaloneModId,old,true);
            UnityModManager.modEntries.Add(oldEntry);module.Configure(true,true);
            Check("Active standalone UMM entry blocks before patching",module.State==HudGuardState.BlockedByStandalone && AllOwned(false));
            NativeHud.Field(oldEntry.GetType(),"mActive").SetValue(oldEntry,false);module.Refresh();
            Check("Inactive standalone without patches permits module",module.State==HudGuardState.Enabled && AllOwned(true));
            UnityModManager.modEntries.Remove(oldEntry);

            module.Stop();
            var install=typeof(HudGuardModule).GetMethod("InstallProviders",BindingFlags.NonPublic|BindingFlags.Instance);
            bool installed=(bool)install.Invoke(module,new object[]{new List<MethodInfo>{target,typeof(NativeHud).GetMethod("VoidTarget")}});
            Check("Actual Harmony failure rolls back already-installed target",!installed && module.State==HudGuardState.Faulted && AllOwned(false) && !Harmony.HasAnyPatches(HudGuardModule.HarmonyId));
            Check("Partial-install rollback preserves unrelated owner",NativeHud.Has(sentinel,foreign.Id));
            module.Configure(true,true);
            Check("Valid retry after failed install succeeds",module.State==HudGuardState.Enabled && AllOwned(true));
            foreign.Unpatch(sentinel,HarmonyPatchType.Postfix,foreign.Id);
            module.Stop();
            var throwingLog=new HudGuardModule(delegate(string message){throw new InvalidOperationException("logger failed");});
            throwingLog.Configure(true,true);
            Check("Parent logger failure cannot break guarded readers",NativeHud.Invoke(target,NativeHud.UnsupportedCar)==null);
            Check("Parent logger failure cannot break cleanup",throwingLog.Unload() && AllOwned(false));
            module.Unload();
            Check("Unload is idempotent and releases owner",module.Unload() && module.State==HudGuardState.Unloaded && AllOwned(false));
            module.Configure(true,true);module.Tick();module.Refresh();
            Check("An unloaded instance cannot silently reactivate",module.State==HudGuardState.Unloaded && AllOwned(false));
            HostChecks(output);
            foreach(AssemblyName reference in typeof(HudGuardModule).Assembly.GetReferencedAssemblies())
                Check("No hard optional-mod reference in combined DLL: "+reference.Name,reference.Name!="HeadsUpDisplay" && reference.Name!="QuantitiesNet" && reference.Name!="CCL.Importer");
            Check("No excluded H6F implementation bundled",typeof(HudGuardModule).Assembly.GetType("DVRollingStockCompatibility.H6FFix")==null);
        }
        finally
        {
            NativeHud.ThrowMode=0;module.Unload();
            foreach(Harmony h in new[]{foreign,speed,injected,duplicate,legacy})h.UnpatchAll(h.Id);
            UnityModManager.modEntries.Clear();
        }
        Check("Every HUD module patch removed at fixture exit",!Harmony.HasAnyPatches(HudGuardModule.HarmonyId));
        File.WriteAllLines(Path.Combine(output,"module-log.txt"),logs.ToArray());
        Console.WriteLine("RESULT passed="+passed+" failed="+failed+"; actual installed HUD/UMM/Harmony in isolated .NET Framework process; controlled speed boundary, no Unity game run.");
        return failed==0?0:1;
    }

    private static void HostChecks(string output)
    {
        string path=Path.Combine(output,"settings");Directory.CreateDirectory(path);
        var entry=NativeHud.Entry("test.toolbox",typeof(IntegrationTests).Assembly,true);
        NativeHud.Field(entry.GetType(),"Path").SetValue(entry,path+Path.DirectorySeparatorChar);
        var host=new ParentHost(entry,new HostSettings{HudGuard=false,OtherChoice=83},logs.Add);
        entry.OnUpdate(entry,0);
        Check("Parent's disabled saved option remains off at startup",host.Module.State==HudGuardState.Disabled && AllOwned(false));
        entry.OnSaveGUI(entry);
        var saved=UnityModManager.ModSettings.Load<HostSettings>(entry);
        Check("One parent settings file persists HUD and another option",!saved.HudGuard && saved.OtherChoice==83);
        host.Settings.HudGuard=true;entry.OnUpdate(entry,0);
        Check("Parent option change activates module immediately",host.Module.State==HudGuardState.Enabled && AllOwned(true));
        entry.OnSaveGUI(entry);saved=UnityModManager.ModSettings.Load<HostSettings>(entry);
        Check("Enabled preference persists without overwriting other setting",saved.HudGuard && saved.OtherChoice==83);
        Check("Parent UMM toggle-off dispatch cleans HUD",entry.OnToggle(entry,false) && AllOwned(false) && host.Module.DesiredEnabled);
        Check("Parent UMM toggle-on dispatch reapplies saved preference",entry.OnToggle(entry,true) && AllOwned(true));
        Check("Parent UMM unload dispatch cleans HUD",entry.OnUnload(entry) && AllOwned(false));
        var restarted=new ParentHost(entry,saved,logs.Add);entry.OnUpdate(entry,0);
        Check("Cold host recreation restores desired enabled state",AllOwned(true) && restarted.Module.DesiredEnabled);
        entry.OnUnload(entry);
    }

    private static void DiscoveryBoundaries()
    {
        var a=AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("HUDGuard.Discovery"),AssemblyBuilderAccess.Run);
        var m=a.DefineDynamicModule("fixture");
        Type shadow=m.DefineType("Shadow.TrainCar",TypeAttributes.Public).CreateType();
        var outer=m.DefineType("Fixture.Providers",TypeAttributes.Public);
        var nested=outer.DefineNestedType("Closures",TypeAttributes.NestedPublic);
        nested.SetCustomAttribute(new CustomAttributeBuilder(typeof(CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes),new object[0]));
        var car=NativeHud.Game.GetType("TrainCar");
        Emit(nested,"Good",typeof(string),car);
        Emit(nested,"Scalar",typeof(int),car);
        Emit(nested,"WrongCar",typeof(string),shadow);
        var utility=outer.DefineNestedType("NonClosure",TypeAttributes.NestedPublic);
        Emit(utility,"Utility",typeof(string),car);
        utility.CreateType();nested.CreateType();Type type=outer.CreateType();
        var find=typeof(HudGuardModule).GetMethod("FindProviderMethods",BindingFlags.Static|BindingFlags.NonPublic);
        var found=(IList)find.Invoke(null,new object[]{type});
        Check("Discovery requires exact native TrainCar and nullable/reference return",found.Count==1 && ((MethodInfo)found[0]).Name=="Good");
        var actual=(IList)find.Invoke(null,new object[]{NativeHud.Hud.GetType("DvMod.HeadsUpDisplay.LocoProviders")});
        Check("Discovery matches all three native generated provider methods",actual.Count==3);
        var query=typeof(HudGuardModule).GetMethod("HasQueryContract",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(MethodInfo provider in NativeHud.Providers)
            Check("Native query API matches reader "+provider.Name,(bool)query.Invoke(null,new object[]{NativeHud.Hud,provider.ReturnType,car}));
        Check("Unknown reference-return provider is not assumed null-safe",!(bool)query.Invoke(null,new object[]{NativeHud.Hud,typeof(string),car}));
        Check("Missing query API is incompatible even with nullable reader",!(bool)query.Invoke(null,new object[]{typeof(IntegrationTests).Assembly,typeof(float?),car}));
    }
    private static void Emit(TypeBuilder type,string name,Type returns,Type parameter)
    {
        var method=type.DefineMethod(name,MethodAttributes.Public|MethodAttributes.Static,returns,new[]{parameter});
        var il=method.GetILGenerator();il.Emit(returns==typeof(int)?OpCodes.Ldc_I4_0:OpCodes.Ldnull);il.Emit(OpCodes.Ret);
    }
}

// Representative parent: actual UMM callback types + actual native settings I/O.
// This is a test host, not a change to the separate Dad's Toolbox project.
public sealed class ParentHost
{
    public readonly HudGuardModule Module;
    public readonly HostSettings Settings;
    private bool active=true;
    public ParentHost(UnityModManager.ModEntry entry,HostSettings settings,Action<string> logger)
    {
        Settings=settings;Module=new HudGuardModule(logger);
        entry.OnUpdate=delegate(UnityModManager.ModEntry e,float dt){Module.Configure(Settings.HudGuard,active);Module.Tick();};
        entry.OnToggle=delegate(UnityModManager.ModEntry e,bool enabled){if(!enabled && !Module.Stop())return false;active=enabled;Module.Configure(Settings.HudGuard,active);return true;};
        entry.OnUnload=delegate(UnityModManager.ModEntry e){return Module.Unload();};
        entry.OnSaveGUI=delegate(UnityModManager.ModEntry e){Settings.Save(e);};
    }
}
public sealed class HostSettings:UnityModManager.ModSettings
{
    public bool HudGuard=true;
    public int OtherChoice=17;
    public override void Save(UnityModManager.ModEntry entry){Save(this,entry);}
}

