using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Uses actual installed UMM, DoubleTrack.signs, RailTrack.GetKinkedPointSet,
// registry getters and native Unity Transforms. Fixture world/list contents are
// generated here; it does not execute DoubleTrack's world-generation coroutine.
public static class NativeSignClearanceChecks
{
    const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly List<Object> Objects = new List<Object>();
    static readonly StringBuilder Report = new StringBuilder();
    static int checks, failures;
    static Assembly toolbox, umm, dt, native;
    static Type main, registryType, trackType, optionsType, moduleType;
    static object parent, dependency, helper, skinEntry;
    static Component registry;
    static FieldInfo singleton, signsField, shiftField;
    static IList entries;
    static string output, game;

    // A real List<GameObject> subtype with a failing IList read tests module
    // fault containment without adding a production injection hook.
    sealed class FailingSigns : List<GameObject>, IList
    {
        object IList.this[int index] { get { throw new InvalidOperationException("deliberate fixture list failure"); } set { throw new InvalidOperationException(); } }
    }

    static void Check(bool ok, string label)
    { checks++; Report.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
    static object Get(object obj, string name) { return obj.GetType().GetField(name, BF).GetValue(obj); }
    static void Set(object obj, string name, object value) { obj.GetType().GetField(name, BF).SetValue(obj,value); }
    static object Call(object obj, string name, params object[] args) { return obj.GetType().GetMethod(name,BF).Invoke(obj,args); }
    static object Static(string name) { return main.GetField(name,BF).GetValue(null); }
    static object Module { get { return Static("signs"); } }
    static object Settings { get { return Static("settings"); } }
    static object Options { get { return Get(Settings,"SignClearance"); } }
    static bool Active { get { return (bool)Module.GetType().GetProperty("Active").GetValue(Module,null); } }
    static string Status { get { return (string)Module.GetType().GetProperty("Status").GetValue(Module,null); } }
    static bool SkinActive { get { var f=Static("skinDamage"); return (bool)f.GetType().GetProperty("Active").GetValue(f,null); } }
    static bool Notice { get { return (bool)Module.GetType().GetProperty("RequiresRestartForRestoration").GetValue(Module,null); } }
    static T Keep<T>(T obj) where T:Object { Objects.Add(obj); return obj; }
    static Type T(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).First(t=>t!=null); }
    static void ParentCallback(string name, params object[] args) { ((Delegate)Get(parent,name)).DynamicInvoke(args); }
    static void Toggle(bool value) { ParentCallback("OnToggle", parent,value); }
    static void Tick(float now) { Call(Module,"Tick",now); }
    static void Refresh() { Call(Module,"Refresh"); }
    static bool Near(float a, float b) { return Mathf.Abs(a-b)<.003f; }

    static object Entry(string id, Version version, Assembly assembly, bool active, bool started=true)
    {
        var type = umm.GetType("UnityModManagerNet.UnityModManager+ModEntry",true);
        var obj = FormatterServices.GetUninitializedObject(type);
        var info = Activator.CreateInstance(umm.GetType("UnityModManagerNet.UnityModManager+ModInfo",true));
        Set(info,"Id",id); Set(info,"Version",version.ToString()); Set(info,"AssemblyName",id+".dll");
        Set(obj,"Info",info); Set(obj,"Version",version);
        string path=Path.Combine(output,"settings",id); Directory.CreateDirectory(path);
        Set(obj,"Path",path); Set(obj,"mAssembly",assembly); Set(obj,"mStarted",started); Set(obj,"mActive",active);
        var loggerType = type.GetNestedType("ModLogger",BF);
        Set(obj,"Logger",Activator.CreateInstance(loggerType,new object[]{"toolbox-native-"+id}));
        return obj;
    }

    static void SendDependencyEvent(object entry, bool value)
    {
        Set(entry,"mActive",value);
        var handler = (Delegate)umm.GetType("UnityModManagerNet.UnityModManager",true).GetField("toggleModsListen",BF).GetValue(null);
        if (handler!=null) handler.DynamicInvoke(entry,value);
    }

    static Component Track(string name, float x)
    {
        var root=Keep(new GameObject(name)); root.SetActive(false);
        var track=root.AddComponent(trackType);
        SetPointSet(track,x);
        return track;
    }

    static void SetPointSet(Component track, float x)
    {
        var psType=T("DV.PointSet.EquiPointSet"); var pointType=psType.GetNestedType("Point",BF);
        var ps=Activator.CreateInstance(psType); var points=Array.CreateInstance(pointType,2);
        var vectorType=pointType.GetField("position").FieldType;
        for(int i=0;i<2;i++)
        {
            var p=Activator.CreateInstance(pointType);
            Set(p,"position",Activator.CreateInstance(vectorType,new object[]{new Vector3(x,0,i==0?-50:50)}));
            Set(p,"forward",Vector3.forward); Set(p,"up",Vector3.up); Set(p,"index",i);
            points.SetValue(p,i);
        }
        Set(ps,"points",points); Set(track,"pointSet",ps);
    }

    static void Tracks(params Component[] tracks)
    {
        var array=Array.CreateInstance(trackType,tracks.Length);
        for(int i=0;i<tracks.Length;i++) array.SetValue(tracks[i],i);
        registryType.GetField("_orderedRailtracks",BF).SetValue(registry,array);
    }

    static GameObject Sign(float x, float y=5, Quaternion? rotation=null)
    {
        var obj=Keep(new GameObject("Generated DoubleTrack fixture sign"));
        obj.transform.position=new Vector3(x,y,0); obj.transform.rotation=rotation??Quaternion.identity;
        return obj;
    }
    static List<GameObject> Signs(params GameObject[] signs)
    { var list=new List<GameObject>(signs); signsField.SetValue(null,list); return list; }

    public static void Run() { EditorApplication.update+=Start; }
    static void Start()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update-=Start;
        output=Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_OUT"); Directory.CreateDirectory(output);
        game=Environment.GetEnvironmentVariable("TOOLBOX_GAME_PATH");
        string managed=Path.Combine(game,"DerailValley_Data/Managed");
        ResolveEventHandler resolver=(sender,e)=>{
            string name=new AssemblyName(e.Name).Name+".dll";
            foreach(string folder in new[]{managed,Path.Combine(managed,"UnityModManager"),Path.Combine(game,"Mods/DoubleTrack"),
                Path.Combine(game,"Mods/SkinManagerMod"),Path.Combine(game,"Mods/DVCustomCarLoader"),Path.Combine(game,"Mods/DVLangHelper")})
            { string file=Path.Combine(folder,name); if(File.Exists(file)) return Assembly.LoadFrom(file); }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve+=resolver;
        try
        {
            Report.AppendLine("Unity "+Application.unityVersion+"; "+Application.platform);
            native=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));
            foreach(string name in new[]{"DV.RailTrack.dll","DV.PointSet.dll","DV.OriginShiftInfo.dll","DV.Utils.dll","net.smkd.vector3d.dll"})
                Assembly.LoadFrom(Path.Combine(managed,name));
            umm=Assembly.LoadFrom(Path.Combine(managed,"UnityModManager/UnityModManager.dll"));
            Assembly.LoadFrom(Path.Combine(managed,"UnityModManager/0Harmony.dll"));
            dt=Assembly.LoadFrom(Path.Combine(game,"Mods/DoubleTrack/DoubleTrack.dll"));
            toolbox=Assembly.LoadFrom(Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_DLL"));
            main=toolbox.GetType("DadsToolbox.Main",true);
            optionsType=toolbox.GetType("DadsToolbox.SignClearanceOptions",true);
            moduleType=toolbox.GetType("DadsToolbox.DoubleTrackSigns",true);
            entries=(IList)umm.GetType("UnityModManagerNet.UnityModManager",true).GetField("modEntries",BF).GetValue(null); entries.Clear();
            signsField=dt.GetType("DV.Signs.CustomSignPlacer",true).GetField("signs",BF);
            // Control the getter's offset without bootstrapping the game's
            // separate Burst shift system inside this Editor fixture.
            shiftField=T("DV.OriginShift.OriginShift").GetField("_currentMove",BF); shiftField.SetValue(null,Vector3.zero);
            registryType=T("RailTrackRegistryBase"); trackType=T("RailTrack");
            var registryObject=Keep(new GameObject("Inactive native railway registry fixture")); registryObject.SetActive(false);
            registry=registryObject.AddComponent(native.GetType("RailTrackRegistry",true));
            singleton=registryType.BaseType.GetField("_instance",BF); singleton.SetValue(null,registry);
            var railway=Keep(new GameObject("[railway]"));
            var first=Track("Fixture source railway",0); var second=Track("Fixture parallel railway",4); Tracks(first,second);
            Signs();
            parent=Entry("DadsToolbox",new Version(0,2,0),toolbox,false);
            dependency=Entry("DoubleTrack",new Version(2,1,1),dt,true);
            var sm=Assembly.LoadFrom(Path.Combine(game,"Mods/SkinManagerMod/SkinManagerMod.dll"));
            skinEntry=Entry("SkinManagerMod",new Version(4,4,1),sm,true);
            entries.Add(dependency); entries.Add(skinEntry);
            // Clean fixture settings make repeated runs independent.
            string settingsPath=Path.Combine((string)Get(parent,"Path"),"Settings.xml");
            File.WriteAllText(settingsPath,"<ToolboxSettings><SkinDamageFix>true</SkinDamageFix><DoubleTrackSignClearance>true</DoubleTrackSignClearance></ToolboxSettings>");
            Check((bool)main.GetMethod("Load").Invoke(null,new[]{parent}),"actual combined Toolbox parent loads with both optional mods");
            Check(Active && SkinActive,"both corrections become active without sharing runtime ownership");
            Check(Object.FindObjectsOfType<MonoBehaviour>().All(x=>x.GetType().Name!="ClearanceWatcher"),"integrated signs use parent UMM update without a legacy watcher");
            ParentCallback("OnUpdate",parent,0f);
            Check(Active,"parent update accepts an empty generated-sign list without a fault");

            var close=Sign(1); var clear=Sign(-3); var boundary=Sign(2);
            var opposite=Sign(3,7,Quaternion.Euler(0,180,0)); var unlisted=Sign(.5f);
            var listed=Signs(close,clear,boundary,opposite);
            Quaternion facing=close.transform.rotation; Refresh(); ParentCallback("OnUpdate",parent,0f);
            Check(Near(close.transform.position.x,-2),"actual parent update moves a close sign clear of both parallel centreline segments");
            Check(Near(opposite.transform.position.x,6),"opposite-facing sign moves in DoubleTrack original outward direction");
            Check(Near(close.transform.position.y,5) && close.transform.rotation==facing && Near(opposite.transform.position.y,7),"correction retains native sign height and facing");
            Check(Near(clear.transform.position.x,-3) && Near(boundary.transform.position.x,2),"already-clear and exact-boundary signs stay untouched");
            Check(Near(unlisted.transform.position.x,.5f),"objects outside actual DoubleTrack sign list are untouched");
            Check(Notice,"real transform correction records restart restoration notice");
            Vector3 corrected=close.transform.position; Refresh(); Tick(10);
            Check(close.transform.position==corrected,"rescan does not cumulatively push a corrected sign farther away");

            var later=Sign(1); listed.Add(later); Tick(10.5f);
            Check(Near(later.transform.position.x,1),"subsecond updates are throttled before the next scan");
            Tick(11.1f);
            Check(Near(later.transform.position.x,-2),"newly generated signs are corrected on the next one-second scan");
            close.transform.position=new Vector3(1,5,0); Signs(close); Tick(12.2f);
            Check(Near(close.transform.position.x,-2),"replacement generated-sign list invalidates processed-sign state");

            Set(Options,"MaximumMove",.5f); var limited=Sign(1); Signs(limited); Refresh(); Tick(13.3f);
            Check(Near(limited.transform.position.x,1),"sign remains in place if the configured movement limit has no solution");
            Set(Options,"MaximumMove",8f);
            var shift=new Vector3(1000,20,-2000); shiftField.SetValue(null,shift);
            var shifted=Sign(1001,25); shifted.transform.position=new Vector3(1001,25,-2000);
            Signs(shifted); Refresh(); Tick(14.4f);
            Check(Near(shifted.transform.position.x,998) && Near(shifted.transform.position.y,25) && Near(shifted.transform.position.z,-2000),
                "origin-shifted Unity positions are compared with absolute native track points");
            shiftField.SetValue(null,Vector3.zero);

            var replacement=Track("Replacement world railway",10); Tracks(replacement);
            var newWorld=Sign(11); Signs(newWorld); Refresh(); Tick(15.5f);
            Check(Near(newWorld.transform.position.x,8),"registry replacement uses new-world track geometry");
            SetPointSet(replacement,20); newWorld.transform.position=new Vector3(21,5,0); Refresh(); Tick(16.6f);
            Check(Near(newWorld.transform.position.x,18),"manual rescan rebuilds changed point sets even when registry array identity is unchanged");
            Tracks(first,second);
            var destroyed=Sign(1); Signs(destroyed,null); Object.DestroyImmediate(destroyed); Refresh(); Tick(17.7f);
            Check(Active,"destroyed and null generated signs are skipped without a module fault");
            signsField.SetValue(null,null); Refresh(); Tick(18.8f);
            Check(Active,"null DoubleTrack sign list during generation is a harmless waiting state");
            var waiting=Sign(1); Signs(waiting); singleton.SetValue(null,null); Refresh(); Tick(20);
            Check(Active && Near(waiting.transform.position.x,1),"missing live registry waits without accessing a null singleton");
            singleton.SetValue(null,registry); Tick(21.1f);
            Check(Near(waiting.transform.position.x,-2),"late live registry is picked up on the next scan");

            // Native OrderedRailtracks lazily dereferences Find("[railway]").
            // A new registry can exist during reload before that root is loaded.
            var reloadSign=Sign(1); Signs(reloadSign);
            Object.DestroyImmediate(railway);
            registryType.GetField("_orderedRailtracks",BF).SetValue(registry,null);
            registryType.GetField("_trackRootParent",BF).SetValue(registry,null);
            bool nativeMissingRoot=false;
            try { registryType.GetProperty("OrderedRailtracks").GetValue(registry,null); }
            catch(TargetInvocationException ex) { nativeMissingRoot=ex.InnerException is NullReferenceException; }
            Check(nativeMissingRoot,"native registry getter reproduces NullReferenceException before railway root exists");
            Refresh(); Tick(21.2f);
            Check(Active && SkinActive && Near(reloadSign.transform.position.x,1),"registry-before-railway reload waits without latching a scanner fault or moving signs");
            railway=Keep(new GameObject("[railway]"));
            var reloadTrack=Track("Reloaded railway",0); reloadTrack.transform.SetParent(railway.transform,false); reloadTrack.gameObject.SetActive(true);
            Tick(21.3f);
            Check(Active && Near(reloadSign.transform.position.x,1),"waiting reload retains the one-second scan throttle");
            Tick(21.4f+1f);
            Check(Active && Near(reloadSign.transform.position.x,-2),"late railway root recovers automatically through the native track getter without manual rescan");
            reloadTrack.gameObject.SetActive(false); Object.DestroyImmediate(railway);
            registryType.GetField("_orderedRailtracks",BF).SetValue(registry,null);
            registryType.GetField("_trackRootParent",BF).SetValue(registry,null);
            var secondReload=Sign(1); Signs(secondReload); Refresh(); Tick(21.5f);
            Check(Active && Near(secondReload.transform.position.x,1),"a second railway teardown waits safely after a successful scan");
            railway=Keep(new GameObject("[railway]"));
            Tracks(first,second); Refresh(); Tick(21.6f);
            Check(Active && Near(secondReload.transform.position.x,-2),"scanner resumes on the next ready railway after repeated teardown");

            var inactiveRootSign=Sign(1); Signs(inactiveRootSign); railway.SetActive(false); Refresh(); Tick(21.7f);
            Check(Active && Near(inactiveRootSign.transform.position.x,1) && ((ICollection)Get(Get(Module,"engine"),"Segments")).Count==0,
                "inactive railway root pauses correction and discards the previous geometry even when registry tracks are cached");
            railway.SetActive(true); Tick(22.8f);
            Check(Active && Near(inactiveRootSign.transform.position.x,-2),"reactivated railway resumes automatically without a manual rescan");
            Refresh();

            Set(Settings,"DoubleTrackSignClearance",false); Toggle(true);
            var disabled=Sign(1); Signs(disabled); Tick(22.2f);
            Check(!Active && Near(disabled.transform.position.x,1) && SkinActive,"sign option off stops transform changes while skin module remains active");
            Check(Notice && Near(waiting.transform.position.x,-2),"disable retains prior sign movement and restoration notice");
            Set(Settings,"DoubleTrackSignClearance",true); Toggle(true); Tick(23.3f);
            Check(Active && Near(disabled.transform.position.x,-2),"sign re-enable resumes correction from cleared runtime caches");
            Toggle(false); var parentOff=Sign(1); Signs(parentOff); ParentCallback("OnUpdate",parent,0f);
            Check(!Active && !SkinActive && Near(parentOff.transform.position.x,1),"disabled parent update cannot scan or leave skin prefix active");
            Toggle(true); Refresh(); Tick(24.4f);
            Check(Active && SkinActive && Near(parentOff.transform.position.x,-2),"parent re-enable restores both independently selected modules");

            var blocked=Sign(1); Signs(blocked);
            var legacyMods=Environment.GetEnvironmentVariable("TOOLBOX_LEGACY_MODS") ?? Path.Combine(game,"Mods");
            var standaloneAssembly=Assembly.LoadFrom(Path.Combine(legacyMods,"DoubleTrackSignClearance/DoubleTrackSignClearance.dll"));
            helper=Entry("DoubleTrackSignClearance",new Version(1,0,0),standaloneAssembly,false); entries.Add(helper);
            Tick(25.5f);
            Check(!Active && SkinActive && Near(blocked.transform.position.x,1) && Status.Contains("restart"),"loaded inactive standalone stops actual native sign scanning without disturbing skin protection");
            SendDependencyEvent(helper,true); Tick(26.6f);
            Check(!Active && Near(blocked.transform.position.x,1),"late active standalone remains blocked before transform correction");
            SendDependencyEvent(helper,false);
            Check(!Active && (bool)Get(Settings,"DoubleTrackSignClearance"),"standalone toggle-off retains conflict and selected preference for safe restart migration");
            entries.Remove(helper); Refresh(); Tick(27.7f);
            Check(Active && Near(blocked.transform.position.x,-2),"cold-host equivalent without loaded helper permits the Toolbox correction");
            SendDependencyEvent(dependency,false); var dtOff=Sign(1); Signs(dtOff); Tick(28.8f);
            Check(!Active && Near(dtOff.transform.position.x,1) && SkinActive,"DoubleTrack deactivation stops only its sign scanner");
            SendDependencyEvent(dependency,true); Tick(29.9f);
            Check(Active && Near(dtOff.transform.position.x,-2),"DoubleTrack reactivation resumes sign correction");

            var failureSign=Sign(1); var failing=new FailingSigns(); failing.Add(failureSign); signsField.SetValue(null,failing); Refresh(); Tick(31);
            Check(!Active && Status.Contains("deliberate fixture list failure") && SkinActive,"generated-sign API failure is contained to this optional module");
            int logLength=Report.Length; Tick(32.1f); Tick(33.2f);
            Check(Report.Length==logLength,"persistent scan fault is latched without repeated error logging");
            Signs(failureSign); Refresh(); Tick(34.3f);
            Check(Active && Near(failureSign.transform.position.x,-2),"explicit rescan retries successfully after the external list fault is corrected");

            GeometryComparison();
            ParentCallback("OnUnload",parent); var unloaded=Module;
            var afterUnload=Sign(1); Signs(afterUnload); Call(unloaded,"Tick",1000f);
            Check(!(bool)unloaded.GetType().GetProperty("Active").GetValue(unloaded,null) && Near(afterUnload.transform.position.x,1) && !SkinActive,
                "parent unload prevents subsequent sign changes and removes the independent skin guard");
            main.GetMethod("Load").Invoke(null,new[]{parent}); Refresh(); Tick(1001);
            Check(!ReferenceEquals(unloaded,Module) && Active && SkinActive && Near(afterUnload.transform.position.x,-2),
                "new Toolbox lifetime recreates a single working sign scanner and skin guard");
        }
        catch(Exception ex) { failures++; Report.AppendLine("EXCEPTION "+ex); }
        finally
        {
            if(parent!=null) try { ParentCallback("OnUnload",parent); } catch(Exception ex) { failures++; Report.AppendLine(ex.ToString()); }
            if(signsField!=null) signsField.SetValue(null,null);
            if(shiftField!=null) shiftField.SetValue(null,Vector3.zero);
            if(singleton!=null) singleton.SetValue(null,null);
            if(entries!=null) entries.Clear();
            foreach(var obj in Objects.AsEnumerable().Reverse()) if(obj) Object.DestroyImmediate(obj);
            AppDomain.CurrentDomain.AssemblyResolve-=resolver;
            Report.AppendLine("Checks: "+checks+"; failures: "+failures);
            Report.AppendLine("Scope: actual compiled Toolbox parent, installed UMM/DoubleTrack sign list, native registry/RailTrack point sets and Unity Transform corrections in isolated Unity Editor. Generated list and rail data; controlled parent update dispatch. Does not run full game boot, DoubleTrack world-generation coroutine, real map traversal, save/reload or in-game GUI.");
            File.WriteAllText(Path.Combine(output,"native-fixture-checks.txt"),Report.ToString());
            EditorApplication.Exit(failures==0?0:1);
        }
    }

    static void GeometryComparison()
    {
        var engine=Get(Module,"engine"); Call(engine,"Reset");
        var add=engine.GetType().GetMethod("AddSegment",BF); var nearest=engine.GetType().GetMethod("TryFindNearestWithin",BF);
        var random=new System.Random(132026); var segments=new List<float[]>();
        for(int i=0;i<120;i++)
        {
            float x=(float)(random.NextDouble()*400-200),z=(float)(random.NextDouble()*400-200);
            float ex=x+(float)(random.NextDouble()*60-30),ez=z+(float)(random.NextDouble()*60-30);
            segments.Add(new[]{x,z,ex,ez}); add.Invoke(engine,new object[]{new Vector2(x,z),new Vector2(ex,ez),"fixture"});
        }
        for(int i=0;i<800;i++)
        {
            float x=(float)(random.NextDouble()*440-220),z=(float)(random.NextDouble()*440-220),r=(float)(random.NextDouble()*9.5+.5);
            double best=double.PositiveInfinity;
            foreach(var s in segments)
            {
                double dx=s[2]-s[0],dz=s[3]-s[1],t=((x-s[0])*dx+(z-s[1])*dz)/(dx*dx+dz*dz);
                t=Math.Max(0,Math.Min(1,t)); double qx=x-(s[0]+t*dx),qz=z-(s[1]+t*dz); best=Math.Min(best,qx*qx+qz*qz);
            }
            object[] args={new Vector2(x,z),r,null}; bool found=(bool)nearest.Invoke(engine,args);
            bool expected=best<r*r-.005f;
            double distance=found?(float)Get(args[2],"Distance"):double.PositiveInfinity;
            Check(found==expected && (!found || Math.Abs(distance-Math.Sqrt(best))<.001),"spatial index against independent complete segment scan "+i);
        }
    }
}
