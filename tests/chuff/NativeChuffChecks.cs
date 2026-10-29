using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Actual combined DLL, native Init/Deinit/UpdateLoops/Port and Unity PCM APIs.
// Engine/flow data are seeded; the fixture does not run engine physics or packs.
public static class NativeChuffChecks
{
    const BindingFlags BF=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    const string FixtureOwner="dads-toolbox-fixture.chuff";
    const string Pending="DadsToolbox.NativeChuffChecks.Pending";
    static Assembly toolbox,harmony;
    static string output,game,dll;
    static int passed,failed,ownershipChanges,loopCalls;
    static bool expectedMissingPort;
    static int unexpectedErrors;
    static IEnumerator steps;
    static readonly StringBuilder report=new StringBuilder();
    static readonly List<Object> objects=new List<Object>();
    static readonly List<object> modules=new List<object>(),patches=new List<object>();
    static readonly HashSet<GameObject> carRoots=new HashSet<GameObject>();
    static readonly HashSet<object> seededEngines=new HashSet<object>();
    static object routedReader; static string routeFrom,routeTo; static string[] originalRoute;
    static Type Module=>T("DadsToolbox.ChuffFixModule");
    static Type Reader=>T("DV.ModularAudioCar.ChuffClipsSimReader");
    static Type Flow=>T("LocoSim.Implementations.SimulationFlow");
    static Type Port=>T("LocoSim.Implementations.Port");
    static Type Engine=>T("LocoSim.Implementations.ReciprocatingSteamEngine");
    static Type T(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).First(t=>t!=null);
    static FieldInfo Field(Type t,string name) { for(;t!=null;t=t.BaseType){var f=t.GetField(name,BF);if(f!=null)return f;}throw new MissingFieldException(name); }
    static object Get(object o,string n)=>Field(o is Type?(Type)o:o.GetType(),n).GetValue(o is Type?null:o);
    static void Set(object o,string n,object v)=>Field(o is Type?(Type)o:o.GetType(),n).SetValue(o is Type?null:o,v);
    static object Prop(object o,string n){var p=o.GetType().GetProperty(n,BF);return p!=null?p.GetValue(o,null):Get(o,n);}
    static object Call(object o,string n,params object[] args)
    {
        var t=o is Type?(Type)o:o.GetType();var m=t.GetMethods(BF).Single(x=>x.Name==n&&x.GetParameters().Length==args.Length);
        try{return m.Invoke(o is Type?null:o,args);}catch(TargetInvocationException e){throw e.InnerException;}
    }
    static object New(Type t,params object[] a)=>Activator.CreateInstance(t,BF,null,a,null);
    static TObj Keep<TObj>(TObj o) where TObj:Object {objects.Add(o);return o;}
    static bool Active(object m)=>(bool)Prop(m,"Active");
    static bool Near(float a,float b)=>Mathf.Abs(a-b)<.0001f*Mathf.Max(1,Mathf.Abs(b));
    static void Check(bool ok,string text){report.AppendLine((ok?"PASS ":"FAIL ")+text);if(ok)passed++;else failed++;}
    static void Case(string text,Action action){try{action();}catch(Exception e){Check(false,text+": "+e);}}
    static string Hash(string p){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
    public static void Run(){SessionState.SetBool(Pending,true);Continue();if(!EditorApplication.isPlaying)EditorApplication.EnterPlaymode();else Begin();}
    [InitializeOnLoadMethod] static void Continue(){EditorApplication.playModeStateChanged-=PlayState;EditorApplication.playModeStateChanged+=PlayState;if(EditorApplication.isPlaying&&SessionState.GetBool(Pending,false))EditorApplication.delayCall+=Begin;}
    static void PlayState(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=Begin;}
    static void Begin(){if(!EditorApplication.isPlaying||!SessionState.GetBool(Pending,false))return;SessionState.SetBool(Pending,false);steps=Suite();EditorApplication.update+=Advance;}
    static void Advance(){try{if(steps.MoveNext())return;}catch(Exception e){Check(false,"fixture dispatch: "+e);}EditorApplication.update-=Advance;Finish();}
    static IEnumerator Suite()
    {
        output=Environment.GetEnvironmentVariable("TOOLBOX_CHUFF_OUT");game=Environment.GetEnvironmentVariable("TOOLBOX_GAME_PATH");dll=Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_DLL");Directory.CreateDirectory(output);
        AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
        Application.logMessageReceived+=CaptureLog;
        toolbox=Assembly.LoadFrom(dll);harmony=Assembly.LoadFrom(Path.Combine(game,"DerailValley_Data/Managed/UnityModManager/0Harmony.dll"));
        foreach(string f in new[]{"Assembly-CSharp.dll","DV.Simulation.dll","DV.LayeredAudio.dll","DV.NAudio.dll","DV.ThingTypes.dll","UnityModManager/UnityModManager.dll"})Assembly.LoadFrom(Path.Combine(game,"DerailValley_Data/Managed",f));
        report.AppendLine("Toolbox SHA256: "+Hash(dll));report.AppendLine("Unity "+Application.unityVersion+" Play Mode="+Application.isPlaying);
        ((IList)Get(T("UnityModManagerNet.UnityModManager"),"modEntries")).Clear();
        SuppressCarCallbacks();
        Case("module lifecycle and PCM",LifecycleChecks);
        // Let Unity execute the production deferred AudioClip.Destroy requests.
        int frame=Time.frameCount;while(Time.frameCount<=frame)yield return null;
        Check(generatedAfterStop==null,"generated PCM clip is destroyed after the native deferred-destruction frame");
        Case("routing prefix order",RoutingChecks);
        Case("live rebind and pooling",ExistingChecks);
        Case("destroyed owner with surviving detached reader",DestroyedOwnerChecks);
        Case("native refusal and custom audio",RefusalChecks);
        Case("standalone ownership",ConflictChecks);
        Case("sound/chuff coordination",SoundChecks);
        Case("Doppler and PCM coordination",DopplerChecks);
        frame=Time.frameCount;while(Time.frameCount<=frame)yield return null;
        Check(dopplerGenerated.All(clip=>clip==null),"both activation orders release their Doppler PCM clips after native deferred destruction");
        Case("parent shutdown",ParentChecks);
    }
    static Assembly Resolve(object sender,ResolveEventArgs e)
    {
        string name=new AssemblyName(e.Name).Name;if(name=="DadsToolbox")return Assembly.LoadFrom(dll);
        foreach(string root in new[]{"DerailValley_Data/Managed","DerailValley_Data/Managed/UnityModManager"}){string p=Path.Combine(game,root,name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;
    }
    static void CaptureLog(string message,string stack,LogType type)
    {
        if(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;
        if(expectedMissingPort&&(message.Contains("Port[primary.ash] not defined!")||message.Contains("ChuffClipsSimReader not initialized properly"))){report.AppendLine("EXPECTED NATIVE REFUSAL "+message);return;}
        unexpectedErrors++;report.AppendLine("UNEXPECTED UNITY "+type+" "+message+"\n"+stack);
    }
    static object Harmony(string owner){var h=New(harmony.GetType("HarmonyLib.Harmony",true),owner);patches.Add(h);return h;}
    static void Patch(object h,MethodBase target,string prefix=null,string postfix=null)
    {
        var m=h.GetType().GetMethod("Patch");var a=new object[m.GetParameters().Length];a[0]=target;
        if(prefix!=null)a[1]=New(harmony.GetType("HarmonyLib.HarmonyMethod",true),typeof(NativeChuffChecks).GetMethod(prefix,BF));
        if(postfix!=null)a[2]=New(harmony.GetType("HarmonyLib.HarmonyMethod",true),typeof(NativeChuffChecks).GetMethod(postfix,BF));m.Invoke(h,a);
    }
    static void SuppressCarCallbacks()
    {
        var h=Harmony(FixtureOwner);
        foreach(string name in new[]{"TrainCar","DV.Simulation.Cars.SimController"})foreach(string callback in new[]{"Awake","Start","OnEnable","OnDisable","OnDestroy"}){var m=T(name).GetMethod(callback,BF);if(m!=null&&m.GetParameters().Length==0)Patch(h,m,"FixtureCar");}
        Patch(h,Engine.GetMethod("Tick",BF),"SkipSeededPhysics");
        Patch(h,Reader.GetMethod("UpdateLoops",BF),"ObserveLoops");
    }
    static bool SkipSeededPhysics(object __instance)=>!seededEngines.Contains(__instance);
    static void ObserveLoops(){loopCalls++;}
    static bool FixtureCar(Component __instance){for(var n=__instance.transform;n;n=n.parent)if(carRoots.Contains(n.gameObject))return false;return true;}
    static object ModuleInstance()
    {
        var logger=New(T("UnityModManagerNet.UnityModManager+ModEntry+ModLogger"),"chuff-fixture");
        var m=New(Module,logger,(Action)(()=>ownershipChanges++));modules.Add(m);return m;
    }
    static object NativePort(string id,float value)
    {var p=FormatterServices.GetUninitializedObject(Port);Set(p,"id",id);Set(p,"value",value);Set(p,"prevValue",value);return p;}
    static object NativeEngine(string id,int cylinders)
    {var e=FormatterServices.GetUninitializedObject(Engine);Set(e,"id",id);Set(e,"numCylinders",cylinders);Set(e,"chuffsPerCycle",cylinders*2);Set(e,"chuffEventReadOut",NativePort(id+".event",-1));Set(e,"chuffFrequencyReadOut",NativePort(id+".frequency",19));seededEngines.Add(e);return e;}
    static object NativeFlow(GameObject root,params object[] engines)
    {
        var flow=FormatterServices.GetUninitializedObject(Flow);Set(flow,"simGO",root);
        var array=Array.CreateInstance(T("LocoSim.Implementations.SimComponent"),engines.Length);for(int i=0;i<engines.Length;i++)array.SetValue(engines[i],i);Set(flow,"OrderedSimComps",array);
        return flow;
    }
    static void Ports(object flow,IDictionary ports)
    {Set(flow,"fullPortIdToPort",New(typeof(System.Collections.ObjectModel.ReadOnlyDictionary<,>).MakeGenericType(typeof(string),Port),ports));}
    static int Subscribers(object o,string field){var d=Get(o,field) as Delegate;return d==null?0:d.GetInvocationList().Length;}
    static readonly string[] IdFields={"chuffEventPortId","exhaustPressurePortId","chuffFrequencyPortId","cylinderWaterNormalizedPortId","cylinderCockControlPortId","ashesInPipesPortId"};
    static string[] Ids(object r)=>IdFields.Select(n=>(string)Get(r,n)).ToArray();
    static void Ids(object r,string[] ids){for(int i=0;i<IdFields.Length;i++)Set(r,IdFields[i],ids[i]);}
    sealed class Fixture
    {
        internal GameObject root;internal Component car,reader;internal object engine,flow,authored,layer,loop,bank,audio;internal IDictionary ports;internal AudioSource source;internal AudioClip clip;internal AnimationCurve curve;
        internal Fixture(int cylinders=4,string prefix="primary",bool detached=false)
        {
            root=Keep(new GameObject("Native chuff fixture "+Guid.NewGuid().ToString("N")));root.SetActive(false);carRoots.Add(root);car=root.AddComponent(T("TrainCar"));((Behaviour)car).enabled=false;
            var controller=root.AddComponent(T("DV.Simulation.Cars.SimController"));((Behaviour)controller).enabled=false;
            var node=new GameObject("Steam exhaust reader");if(!detached)node.transform.SetParent(root.transform,false);else Keep(node);node.SetActive(false);reader=node.AddComponent(Reader);
            engine=NativeEngine(prefix,cylinders);flow=NativeFlow(root,engine);Set(controller,"simFlow",flow);
            ports=(IDictionary)New(typeof(Dictionary<,>).MakeGenericType(typeof(string),Port));
            AddEngine(engine,prefix);Ports(flow,ports);Ids(reader,new[]{prefix+".event",prefix+".pressure",prefix+".authored",prefix+".water",prefix+".cocks",prefix+".ash"});authored=ports[prefix+".authored"];
            clip=Keep(AudioClip.Create("Steam_ChuffLoop16s_01",2048,2,22050,false));var data=Enumerable.Range(0,4096).Select(i=>(float)Math.Sin(i*.13)*.2f).ToArray();if(!clip.SetData(data,0))throw new Exception("native seed SetData failed");
            var bankType=Reader.GetNestedType("OrderedChuffClips",BF);var banks=Array.CreateInstance(bankType,4);for(int i=0;i<4;i++){var b=New(bankType);Set(b,"chuffVariations",new[]{clip});banks.SetValue(b,i);}bank=banks;
            foreach(string field in new[]{"lowPressureClips","mediumPressureClips","highPressureClips"})Set(reader,field,banks);
            foreach(string field in new[]{"regularChuffConfig","waterChuffConfig","ashChuffConfig"}){var c=New(Reader.GetNestedType("IndividualChuffAudioSourceConfig",BF));Set(c,"parent",node.transform);Set(c,"spatialCurve",new AnimationCurve());Set(c,"chuffFrequencyToMasterVolume",AnimationCurve.Linear(0,0,1000,0));Set(reader,field,c);}
            Set(reader,"pressureToVolumeCurve",AnimationCurve.Linear(0,0,1000,0));Set(reader,"mediumPressureThreshold",10f);Set(reader,"highPressureThreshold",20f);Set(reader,"waterChuffClips",new AudioClip[0]);Set(reader,"ashChuffClips",new AudioClip[0]);
            var sound=new GameObject("Muted native stock loop");sound.transform.SetParent(node.transform,false);audio=sound.AddComponent(T("LayeredAudio"));source=sound.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=true;source.mute=true;source.clip=clip;source.pitch=1f;
            layer=New(T("LayeredAudio+Layer"));curve=new AnimationCurve(new Keyframe(-16,-1,.0625f,.0625f),new Keyframe(32,2,.0625f,.0625f));Set(layer,"source",source);Set(layer,"startPitch",1f);Set(layer,"usePitchCurve",true);Set(layer,"pitchCurve",curve);Set(layer,"volumeCurve",AnimationCurve.Linear(0,0,1000,0));Set(layer,"clips",new AudioClip[0]);
            var layers=Array.CreateInstance(layer.GetType(),1);layers.SetValue(layer,0);Set(audio,"layers",layers);Set(audio,"randomizeStartTime",false);
            loop=New(Reader.GetNestedType("ChuffLoop",BF));Set(loop,"chuffLoop",audio);Set(loop,"chuffFrequencyToMasterVolume",AnimationCurve.Linear(0,1,1000,1));var loops=Array.CreateInstance(loop.GetType(),1);loops.SetValue(loop,0);Set(reader,"chuffLoops",loops);
            Set(reader,"waterChuffLoops",Array.CreateInstance(loop.GetType(),0));Set(reader,"ashChuffLoops",Array.CreateInstance(loop.GetType(),0));
            node.SetActive(true);root.SetActive(true);
        }
        internal void AddEngine(object e,string id){ports[id+".event"]=Get(e,"chuffEventReadOut");ports[id+".frequency"]=Get(e,"chuffFrequencyReadOut");ports[id+".pressure"]=NativePort(id+".pressure",5);ports[id+".authored"]=NativePort(id+".authored",2);ports[id+".water"]=NativePort(id+".water",0);ports[id+".cocks"]=NativePort(id+".cocks",0);ports[id+".ash"]=NativePort(id+".ash",0);}
        internal void Init()=>Call(reader,"Init",car,flow);
        internal void Deinit(){if(!ReferenceEquals(Get(reader,"car"),null))Call(reader,"Deinit");}
        internal void Hz(float hz){Port.GetProperty("Value").SetValue(Get(engine,"chuffFrequencyReadOut"),hz,null);}
        internal void Loops()=>Call(reader,"UpdateLoops");
    }
    static AudioClip generatedAfterStop;
    static void LifecycleChecks()
    {
        var m=ModuleInstance();Call(m,"Configure",true,true);Check(Active(m),"Chuff Fix activates without CCL or Sound dependencies");
        var info=harmony.GetType("HarmonyLib.Harmony").GetMethod("GetPatchInfo").Invoke(null,new object[]{Reader.GetMethod("Init",BF)});var owner=(string)Module.GetField("Owner").GetRawConstantValue();
        Check(((IEnumerable)Prop(info,"Prefixes")).Cast<object>().Any(p=>(string)Get(p,"owner")==owner&&(int)Get(p,"priority")==0),"exact combined Init prefix uses Harmony Priority.Last");
        var f=new Fixture();f.Init();Check(((Array)Get(f.reader,"lowPressureClips")).Length==8,"actual native Init expands four slots to the matched four-cylinder cycle");
        Check(ReferenceEquals(Get(f.reader,"chuffFrequencyPort"),Get(f.engine,"chuffFrequencyReadOut")),"actual native frequency binding selects the exact matched engine port");
        Check(Subscribers(f.flow,"TickEvent")==1,"reader owns one native flow-tick refresh callback");
        int loopsBefore=loopCalls;f.Hz(42);Call(f.flow,"Tick",.02f);
        Check(loopCalls==loopsBefore+1&&Near(f.source.pitch*f.source.clip.frequency/f.clip.frequency*16,42),"actual native flow Tick refreshes loops once without a new exhaust event; seeded engine physics alone is skipped");
        f.Hz(180);f.Loops();var generated=f.source.clip;var pcm=new float[f.clip.samples*f.clip.channels];var actual=new float[pcm.Length];
        Check(generated!=f.clip&&generated.GetData(actual,0)&&f.clip.GetData(pcm,0)&&actual.Zip(pcm,(a,b)=>Near(a,b)).All(x=>x),"real AudioClip.GetData/Create/SetData retain every stereo PCM sample");
        Check(generated.samples==f.clip.samples&&generated.channels==f.clip.channels&&generated.frequency>f.clip.frequency,"native runtime clip retains frame/channel count at a higher accepted sample rate");
        Check(Near(f.source.pitch*generated.frequency/f.clip.frequency*16,180),"native UpdateLoops source pitch and runtime sample rate encode the supplied 180 exhaust Hz");
        generatedAfterStop=generated;
        Check((bool)Call(m,"Stop"),"live module stop succeeds before native reader Deinit");
        Check(ReferenceEquals(Get(f.reader,"lowPressureClips"),f.bank)&&ReferenceEquals(Get(f.reader,"chuffFrequencyPort"),f.authored),"live stop restores both bank identity and effective bound frequency port");
        Check(f.source.clip==f.clip&&ReferenceEquals(Get(f.layer,"pitchCurve"),f.curve)&&Near((float)Get(f.layer,"startPitch"),1),"live stop restores original native clip, curve and start pitch");
        Check(Subscribers(f.flow,"TickEvent")==0,"live stop removes all owned refresh callbacks");
        f.Deinit();Call(m,"Unload");
    }
    static void RoutingPrefix(object __instance,object simFlow){if(!ReferenceEquals(__instance,routedReader))return;originalRoute=Ids(__instance);Ids(__instance,originalRoute.Select(s=>s.Replace(routeFrom+".",routeTo+".")).ToArray());}
    static void RoutingPostfix(object __instance){if(ReferenceEquals(__instance,routedReader)&&originalRoute!=null)Ids(__instance,originalRoute);}
    static void RoutingChecks()
    {
        foreach(bool first in new[]{false,true})foreach(int cylinders in new[]{2,3,4})
        {
            var router=Harmony(FixtureOwner+".router");var m=ModuleInstance();var init=Reader.GetMethod("Init",BF);var deinit=Reader.GetMethod("Deinit",BF);
            if(first){Patch(router,init,"RoutingPrefix");Patch(router,deinit,null,"RoutingPostfix");}Call(m,"Configure",true,true);if(!first){Patch(router,init,"RoutingPrefix");Patch(router,deinit,null,"RoutingPostfix");}
            var f=new Fixture(2);var target=NativeEngine("secondary",cylinders);f.AddEngine(target,"secondary");var engines=Array.CreateInstance(T("LocoSim.Implementations.SimComponent"),2);engines.SetValue(f.engine,0);engines.SetValue(target,1);Set(f.flow,"OrderedSimComps",engines);Ports(f.flow,f.ports);
            routedReader=f.reader;routeFrom="primary";routeTo="secondary";var original=Ids(f.reader);bool ok=true;
            try{for(int cycle=0;cycle<2;cycle++){f.Init();ok&=ReferenceEquals(Get(f.reader,"chuffFrequencyPort"),Get(target,"chuffFrequencyReadOut"))&&((Array)Get(f.reader,"lowPressureClips")).Length==cylinders*2;f.Deinit();ok&=Ids(f.reader).SequenceEqual(original)&&ReferenceEquals(Get(f.reader,"lowPressureClips"),f.bank)&&Subscribers(f.flow,"TickEvent")==0;}}
            finally{f.Deinit();routedReader=null;originalRoute=null;Call(m,"Unload");Call(router,"UnpatchAll",FixtureOwner+".router");}
            Check(ok,"real Harmony six-port routing, both native lifecycle cycles: routerFirst="+first+", cylinders="+cylinders);
        }
    }
    static void ExistingChecks()
    {
        var f=new Fixture(3,"detached",true);f.Init();var m=ModuleInstance();Call(m,"Configure",true,true);
        Check(Active(m)&&((Array)Get(f.reader,"lowPressureClips")).Length==6&&ReferenceEquals(Get(f.reader,"chuffFrequencyPort"),Get(f.engine,"chuffFrequencyReadOut")),"live enable discovers and rebinds an already initialized detached reader");
        int nativeSubscriptions=Subscribers(Get(f.engine,"chuffEventReadOut"),"ValueUpdatedInternally");Call(m,"Refresh");
        Check(Subscribers(f.flow,"TickEvent")==1&&Subscribers(Get(f.engine,"chuffEventReadOut"),"ValueUpdatedInternally")==nativeSubscriptions,"rescan does not duplicate native chuff or owned tick subscriptions");
        f.Deinit();f.Init();Check(((Array)Get(f.reader,"lowPressureClips")).Length==6&&Subscribers(f.flow,"TickEvent")==1,"pooled native reader reinitializes without compounding banks or callbacks");
        Object.DestroyImmediate(f.reader);Call(m,"Tick");Call(m,"Refresh");Check(Subscribers(f.flow,"TickEvent")==0,"destroyed reader cleanup releases its flow refresh callback without Deinit");Call(m,"Unload");
    }
    static void DestroyedOwnerChecks()
    {
        var m=ModuleInstance();Call(m,"Configure",true,true);var f=new Fixture(3,"deletedOwner",true);f.Init();f.Loops();
        Object.DestroyImmediate(f.root);int before=loopCalls;Call(f.flow,"Tick",.02f);
        Check(f.reader&&loopCalls==before,"native flow tick does not update loops on a surviving detached reader after its owning TrainCar is destroyed");
        Check(Subscribers(f.flow,"TickEvent")==0,"destroyed owner cleanup removes only the owned flow refresh callback");
        Check(ReferenceEquals(Get(f.reader,"lowPressureClips"),f.bank)&&ReferenceEquals(Get(f.reader,"chuffFrequencyPort"),f.authored),"destroyed owner cleanup restores surviving reader bank and effective frequency references");
        Check(f.source.clip==f.clip&&ReferenceEquals(Get(f.layer,"pitchCurve"),f.curve),"destroyed owner cleanup restores native loop clip and curve without native car reinitialization");
        Check((bool)Call(m,"Stop"),"module stop succeeds after owning car destruction without invoking native Deinit on that dead owner");Call(m,"Unload");
        Object.DestroyImmediate(f.reader.gameObject);
    }
    static object Entry(string id,bool loaded,bool active)
    {var e=FormatterServices.GetUninitializedObject(T("UnityModManagerNet.UnityModManager+ModEntry"));var info=New(T("UnityModManagerNet.UnityModManager+ModInfo"));Set(info,"Id",id);Set(info,"Version","0.2.1");Set(info,"AssemblyName",id+".dll");Set(e,"Info",info);Set(e,"Version",new Version(0,2,1));Set(e,"Logger",New(T("UnityModManagerNet.UnityModManager+ModEntry+ModLogger"),"chuff-parent-fixture"));Set(e,"mAssembly",loaded?toolbox:null);Set(e,"mStarted",loaded);Set(e,"mActive",active);return e;}
    static void Observe(){}
    static void ConflictChecks()
    {
        var entries=(IList)Get(T("UnityModManagerNet.UnityModManager"),"modEntries");var e=Entry("MultiCylinderChuffFix",true,false);entries.Add(e);var m=ModuleInstance();Call(m,"Configure",true,true);
        Check(!Active(m)&&(bool)Prop(m,"DesiredEnabled"),"loaded inactive standalone Chuff Fix blocks integration without clearing preference");entries.Remove(e);Call(m,"Refresh");Check(Active(m),"explicit refresh activates after standalone entry removal");Call(m,"Stop");
        var foreign=Harmony("MultiCylinderChuffFix");var target=Reader.GetMethod("Init",BF);Patch(foreign,target,"Observe");Call(m,"Configure",true,true);
        Check(!Active(m),"residual standalone Harmony owner blocks Chuff Fix even without a UMM entry");
        Call(m,"Tick");Call(m,"Configure",true,true);Check(!Active(m),"cached residual-owner refusal survives first tick and unchanged configuration");
        Call(m,"Refresh");Check(!Active(m),"explicit refresh rechecks and retains residual-owner refusal while the owner remains");Call(m,"Unload");
        var info=harmony.GetType("HarmonyLib.Harmony").GetMethod("GetPatchInfo").Invoke(null,new object[]{target});Check(((IEnumerable)Prop(info,"Owners")).Cast<string>().Contains("MultiCylinderChuffFix"),"conflict cleanup leaves standalone Harmony ownership intact");Call(foreign,"UnpatchAll","MultiCylinderChuffFix");
    }
    static void RefusalChecks()
    {
        var m=ModuleInstance();Call(m,"Configure",true,true);
        var broken=new Fixture();broken.ports.Remove("primary.ash");Ports(broken.flow,broken.ports);expectedMissingPort=true;try{broken.Init();}finally{expectedMissingPort=false;}
        Check(ReferenceEquals(Get(broken.reader,"lowPressureClips"),broken.bank)&&Subscribers(broken.flow,"TickEvent")==0&&(string)Get(broken.reader,"chuffFrequencyPortId")=="primary.authored","missing sixth native port refuses adaptation and rolls back frequency ID/banks without a callback");broken.Deinit();
        foreach(string kind in new[]{"custom recording","nonlinear curve","ash loop"})
        {
            var f=new Fixture();
            if(kind=="custom recording"){f.clip.name="Authored custom recording";}
            if(kind=="nonlinear curve"){f.curve=new AnimationCurve(new Keyframe(-16,-1),new Keyframe(0,.7f),new Keyframe(32,2));Set(f.layer,"pitchCurve",f.curve);}
            if(kind=="ash loop"){Set(f.reader,"ashChuffLoops",Get(f.reader,"chuffLoops"));Set(f.reader,"chuffLoops",Array.CreateInstance(f.loop.GetType(),0));}
            f.Init();f.Hz(180);f.Loops();
            Check(ReferenceEquals(Get(f.layer,"pitchCurve"),f.curve)&&f.source.clip==f.clip&&!(bool)Call(T("DadsToolbox.Chuffs.ChuffLoopOwnership"),"OwnsLayer",f.layer),kind+" remains authored and has no rhythmic-loop ownership claim");f.Deinit();
        }
        var changed=new Fixture();changed.Init();var foreign=Keep(AudioClip.Create("Other mod clip",256,1,22050,false));var curve=AnimationCurve.Linear(0,.1f,32,.4f);changed.source.clip=foreign;Set(changed.layer,"pitchCurve",curve);Set(changed.layer,"startPitch",.77f);changed.source.pitch=.44f;
        Call(T("DadsToolbox.Chuffs.ReaderAdaptation"),"Restore",changed.reader);
        Check(changed.source.clip==foreign&&ReferenceEquals(Get(changed.layer,"pitchCurve"),curve)&&Near((float)Get(changed.layer,"startPitch"),.77f)&&Near(changed.source.pitch,.44f),"owned state restoration preserves another mod's replacement clip, curve, start pitch and source pitch");changed.Deinit();Call(m,"Unload");
    }
    static object SeedSound(Fixture f)
    {
        if(!(Object)Get(f.car,"carLivery")){var livery=Keep(ScriptableObject.CreateInstance(T("DV.ThingTypes.TrainCarLivery")));Set(livery,"id","ChuffSoundFixture");Set(f.car,"carLivery",livery);}
        var engineType=T("DadsToolbox.LocoSound.SoundEngine");var engine=New(engineType.GetNestedType("Engine",BF));Set(engine,"Car",f.car);Set(engine,"Stock",false);Set(engine,"Steam",true);Set(engine,"Area",94d);Set(engine,"Mass",200d);((IList)Get(engineType,"Engines")).Add(engine);
        var catalogue=Get(engineType,"Catalogue");var donor=New(T("DadsToolbox.LocoSound.StockAudio+Donor"));Set(donor,"Id","Chuff coordinated native donor");Set(donor,"Mask",1);Set(donor,"Area",47d);Set(donor,"Mass",100d);((IList)Get(catalogue,"Donors")).Add(donor);
        foreach(var clip in new[]{f.clip,f.source.clip}.Distinct()){var info=New(T("DadsToolbox.LocoSound.StockAudio+ClipInfo"));Set(info,"Donors",1);Set(info,"Kind",Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"),"Chuffs"));((IDictionary)Get(catalogue,"Clips"))[clip]=info;}
        var binding=New(engineType.GetNestedType("Binding",BF));Set(binding,"Engine",engine);Set(binding,"Audio",f.audio);Set(binding,"Source",f.source);Set(binding,"Clip",f.source.clip);Set(binding,"Donors",1);Set(binding,"Loop",true);Set(binding,"Kind",Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"),"Chuffs"));Call(Get(binding,"AllowedClips"),"Add",f.source.clip);Call(Get(binding,"AllowedClips"),"Add",f.clip);
        ((IDictionary)Get(engineType,"Layers"))[f.layer]=binding;Call(Get(engineType,"Layered"),"Add",f.audio);((IList)Get(engine,"Nodes")).Add(f.audio);((IList)Get(engine,"LayerKeys")).Add(f.layer);return binding;
    }
    static readonly List<AudioClip> dopplerGenerated=new List<AudioClip>();
    static void DopplerChecks()
    {
        foreach(bool soundFirst in new[]{true,false})
        {
            var f=new Fixture();
            // Keep Doppler itself inactive so its game ECS Awake never runs.
            // The real native request/apply methods write the active muted source.
            var dopplerRoot=Keep(new GameObject("Inactive native Doppler fixture"));dopplerRoot.SetActive(false);
            var doppler=dopplerRoot.AddComponent(T("DV.DopplerEffects.Doppler"));
            if(!doppler)throw new InvalidOperationException("Native Doppler fixture component was not created.");
            Set(doppler,"source",f.source);Set(doppler,"desiredPitch",1f);Set(f.layer,"doppler",doppler);Set(f.layer,"useDoppler",true);
            var options=New(T("DadsToolbox.LocoSound.SoundOptions"));Set(options,"ChuffLoops",true);Set(options,"Strength",1.75f);Set(options,"MinimumPitch",.4f);
            var choices=options.GetType().GetFields().Where(field=>field.FieldType.IsPrimitive).ToDictionary(field=>field.Name,field=>field.GetValue(options));
            var sound=New(T("DadsToolbox.LocoSound.SoundPitchModule"),options,(Action<string>)(s=>report.AppendLine("DOPPLER SOUND "+s)),output,(Func<string>)(()=>null));modules.Add(sound);
            var chuff=ModuleInstance();float tone=(float)Math.Pow(2,-.2*1.75);
            try
            {
                if(soundFirst)
                {
                    Call(sound,"Configure",true,true);Call(f.audio,"Reset");SeedSound(f);Call(f.layer,"SetPitch",19f,false,1f,1f);
                    float wanted=(float)Get(doppler,"desiredPitch");Call(doppler,"ApplyPitch",wanted);
                    Check(Near(wanted,19f/16*tone)&&Near(f.source.pitch,wanted),"steam boiler size changes the native Doppler request and applied source before Chuff claims it");
                    f.source.enabled=true;f.source.Play();
                    Call(chuff,"Configure",true,true);f.Init();InitialDopplerHandoff(f,doppler,soundFirst);
                }
                else
                {
                    Call(f.audio,"Reset");Call(f.layer,"SetPitch",19f,false,1f,1f);Call(doppler,"ApplyPitch",(float)Get(doppler,"desiredPitch"));
                    f.source.enabled=true;f.source.Play();
                    Call(chuff,"Configure",true,true);f.Init();InitialDopplerHandoff(f,doppler,soundFirst);
                    Call(sound,"Configure",true,true);SeedSound(f);
                }
                f.Hz(180);f.Loops();var clip=f.source.clip;float requested=(float)Get(doppler,"desiredPitch");float rate=(float)clip.frequency/f.clip.frequency;
                if(clip!=f.clip)dopplerGenerated.Add(clip);
                Check(clip!=f.clip&&clip.samples==f.clip.samples&&clip.channels==f.clip.channels&&rate>1,
                    "Doppler loop uses a real higher-rate PCM clip with intact frames/channels; Sound first="+soundFirst);
                Check(Near(requested*rate*16,180)&&!Near(f.source.pitch,requested),
                    "native Doppler stores the cadence-correct request before its separate source application; Sound first="+soundFirst);
                Call(doppler,"ApplyPitch",requested);
                Check(Near(f.source.pitch*rate*16,180)&&Near((float)Get(f.layer,"startPitch"),1/rate),
                    "native Doppler ApplyPitch preserves 180 exhaust Hz through PCM rebasing without size scaling; Sound first="+soundFirst);
                Check((bool)Call(sound,"Stop")&&Active(chuff)&&Near((float)Get(doppler,"desiredPitch")*rate*16,180),
                    "stopping Sound leaves the Chuff-owned Doppler cadence intact; Sound first="+soundFirst);
                Call(sound,"Configure",true,true);SeedSound(f);
                Check((bool)Call(chuff,"Stop")&&f.source.clip==f.clip&&ReferenceEquals(Get(f.layer,"pitchCurve"),f.curve)&&
                    Near((float)Get(f.layer,"startPitch"),1)&&ReferenceEquals(Get(f.reader,"chuffFrequencyPort"),f.authored)&&Subscribers(f.flow,"TickEvent")==0,
                    "Chuff stop restores native clip/curve/start pitch/frequency and removes its Doppler refresh callback; Sound first="+soundFirst);
                float nativePitch=(float)Prop(f.authored,"Value")/16;requested=(float)Get(doppler,"desiredPitch");
                Check(Active(sound)&&!Active(chuff)&&Near(requested,nativePitch*tone),
                    "restored native loop immediately regains the saved steam size-pitch request; Sound first="+soundFirst);
                Call(doppler,"ApplyPitch",requested);
                Check(Near(f.source.pitch,nativePitch*tone),
                    "native Doppler applies the resumed steam size pitch after Chuff stops; Sound first="+soundFirst);
                Check((bool)Call(sound,"Stop")&&Near((float)Get(doppler,"desiredPitch"),nativePitch),
                    "stopping both modules restores the native Doppler request; Sound first="+soundFirst);
                Call(doppler,"ApplyPitch",(float)Get(doppler,"desiredPitch"));
                Check(Near(f.source.pitch,nativePitch)&&choices.All(pair=>Equals(Get(options,pair.Key),pair.Value)),
                    "native Doppler source and all saved Sound choices survive complete cleanup; Sound first="+soundFirst);
            }
            finally
            {
                f.Deinit();Call(chuff,"Unload");Call(sound,"Unload");
                Set(f.layer,"useDoppler",false);Set(f.layer,"doppler",null);Object.DestroyImmediate(dopplerRoot);
            }
        }
    }
    static void InitialDopplerHandoff(Fixture f,Component doppler,bool soundFirst)
    {
        // No UpdateLoops has run since Init: test the initial PCM handoff
        // itself, including an already-playing muted source and delayed apply.
        float rate=(float)f.source.clip.frequency/f.clip.frequency;
        Check(f.source.isPlaying&&rate>1&&Near(f.source.pitch*rate*16,19),
            "initial PCM handoff preserves the playing native source's 19 Hz cadence before any loop update; Sound first="+soundFirst);
        float requested=(float)Get(doppler,"desiredPitch");
        Check(Near(requested*rate*16,19),
            "initial PCM handoff also compensates the pending native Doppler request before any loop update; Sound first="+soundFirst);
        Call(doppler,"ApplyPitch",requested);
        Check(Near(f.source.pitch*rate*16,19),
            "applying that initial native Doppler request cannot double the new PCM clip's cadence; Sound first="+soundFirst);
        // This inactive Doppler has no game ECS entity. Keep later volume
        // transitions from invoking Enable/Disable; only request/apply is tested.
        f.source.Stop();
    }
    static void SoundChecks()
    {
        foreach(bool soundFirst in new[]{true,false})
        {
            var options=New(T("DadsToolbox.LocoSound.SoundOptions"));Set(options,"ChuffLoops",true);Set(options,"Strength",1.75f);Set(options,"MinimumPitch",.4f);Set(options,"NonSteamStrength",1f);
            var choices=options.GetType().GetFields().Where(field=>field.FieldType.IsPrimitive).ToDictionary(field=>field.Name,field=>field.GetValue(options));
            var sound=New(T("DadsToolbox.LocoSound.SoundPitchModule"),options,(Action<string>)(s=>report.AppendLine("SOUND "+s)),output,(Func<string>)(()=>null));modules.Add(sound);var chuff=ModuleInstance();
            var f=new Fixture();
            if(soundFirst){Call(sound,"Configure",true,true);Call(f.audio,"Reset");SeedSound(f);Call(f.layer,"SetPitch",19f,false,1f,1f);Call(chuff,"Configure",true,true);f.Init();}
            else{Call(chuff,"Configure",true,true);f.Init();Call(sound,"Configure",true,true);SeedSound(f);}
            f.Hz(90);f.Loops();var engine=T("DadsToolbox.LocoSound.SoundEngine");
            Check(Active(sound)&&Active(chuff)&&(bool)Call(T("DadsToolbox.Chuffs.ChuffLoopOwnership"),"OwnsLayer",f.layer)&&Near(f.source.pitch*f.source.clip.frequency/f.clip.frequency*16,90),"both modules preserve native exhaust cadence; Sound activated first="+soundFirst);
            var binding=((IDictionary)Get(engine,"Layers"))[f.layer];Check(Near((float)Call(engine,"LayerFactor",f.layer,binding),1),"recognized owned rhythmic loop explicitly bypasses size pitch instead of relying on unknown runtime clip");
            Call(chuff,"Refresh");f.Loops();Check(choices.All(p=>Equals(Get(options,p.Key),p.Value)),"Chuff rescan preserves all saved Sound primitive choices including ChuffLoops");
            Check((bool)Call(sound,"Stop")&&Active(chuff),"stopping Sound preserves active Chuff Fix");f.Loops();Check(Near(f.source.pitch*f.source.clip.frequency/f.clip.frequency*16,90),"Chuff cadence remains native after Sound stops");
            Call(sound,"Configure",true,true);SeedSound(f);Check((bool)Call(chuff,"Stop")&&Active(sound),"stopping Chuff Fix preserves active Sound module");binding=((IDictionary)Get(engine,"Layers"))[f.layer];
            Check((float)Call(engine,"LayerFactor",f.layer,binding)<1&&!((bool)Call(T("DadsToolbox.Chuffs.ChuffLoopOwnership"),"OwnsLayer",f.layer)),"release restores recognized native clip and resumes the user's size-pitch eligibility");
            Check(choices.All(p=>Equals(Get(options,p.Key),p.Value)),"both stop orders preserve Sound settings");f.Deinit();Call(chuff,"Unload");Call(sound,"Unload");
        }
    }
    static void ParentChecks()
    {
        var m=ModuleInstance();Call(m,"Configure",true,true);Call(m,"Configure",true,false);Call(m,"Refresh");Call(m,"Tick");Check(!Active(m),"parent disabled state survives refresh and tick");Call(m,"Unload");Call(m,"Configure",true,true);Call(m,"Refresh");Check(!Active(m),"unloaded module cannot reactivate from later configuration");
        string path=Path.Combine(output,"parent-entry");Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path,"Settings.xml"),"<ToolboxSettings><HudGuard>false</HudGuard><CclControlFix>false</CclControlFix><LocoSoundPitch>false</LocoSoundPitch><SmokeFix>false</SmokeFix><SkinDamageFix>false</SkinDamageFix><SkinRegistrationFix>false</SkinRegistrationFix><DoubleTrackSignClearance>false</DoubleTrackSignClearance><BogieVisualSmoothing>false</BogieVisualSmoothing><ChuffFix>true</ChuffFix><SoundSettingsMigrationComplete>true</SoundSettingsMigrationComplete></ToolboxSettings>");
        var entry=Entry("DadsToolbox",true,true);Set(entry,"Path",path+Path.DirectorySeparatorChar);var parent=toolbox.GetType("DadsToolbox.Main",true);Fixture f=null;
        try
        {
            Check((bool)Call(parent,"Load",entry),"actual UMM parent loads with only independent Chuff Fix selected");var chuff=Get(parent,"chuffs");f=new Fixture(3);f.Init();
            Check(Active(chuff)&&((Array)Get(f.reader,"lowPressureClips")).Length==6,"parent owns an effective native reader adaptation without Sound or CCL");
            var toggle=(Delegate)Get(entry,"OnToggle");Check((bool)toggle.DynamicInvoke(entry,false)&&!Active(chuff)&&Subscribers(f.flow,"TickEvent")==0&&ReferenceEquals(Get(f.reader,"chuffFrequencyPort"),f.authored),"parent toggle stops Chuff Fix and restores effective native frequency and callbacks");
            Call(chuff,"Refresh");((Delegate)Get(entry,"OnUpdate")).DynamicInvoke(entry,0f);Check(!Active(chuff),"late child refresh and parent Update cannot reactivate disabled parent");
            Check((bool)toggle.DynamicInvoke(entry,true)&&Active(chuff)&&((Array)Get(f.reader,"lowPressureClips")).Length==6,"parent re-enable rebinds the already existing reader");
            Set(Get(parent,"settings"),"ChuffFix",false);Call(parent,"ApplySettings");Check(!Active(chuff)&&ReferenceEquals(Get(f.reader,"lowPressureClips"),f.bank),"independent saved Chuff preference disables and restores native bank references");
        }
        finally{((Delegate)Get(entry,"OnUnload")).DynamicInvoke(entry);if(f!=null)f.Deinit();}
    }
    static void Finish()
    {
        foreach(var m in modules)try{Call(m,"Unload");}catch(Exception e){Check(false,"cleanup "+e.Message);}foreach(var p in patches)try{Call(p,"UnpatchAll",Prop(p,"Id"));}catch{}
        foreach(var o in objects.AsEnumerable().Reverse())if(o)Object.DestroyImmediate(o);
        Check(unexpectedErrors==0,"no unexpected Unity Error/Exception/Assert messages during native checks or cleanup");Application.logMessageReceived-=CaptureLog;
        report.AppendLine("Scope: exact Toolbox DLL and real native reader lifecycle/ports/Harmony/Unity PCM in isolated Play Mode; seeded engines/flows and muted sources. Native car startup suppressed only for fixture roots. No game packs, real Sim physics, native NAudio one-shots, audible playback or listening acceptance.");
        report.AppendLine("RESULT: "+passed+" passed, "+failed+" failed.");File.WriteAllText(Path.Combine(output,"native-fixture-checks.txt"),report.ToString());File.WriteAllText(Path.Combine(output,"result.json"),"{\"passed\":"+passed+",\"failed\":"+failed+"}");EditorApplication.Exit(failed==0?0:1);
    }
}
