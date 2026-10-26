using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// This isolated Editor fixture loads installed managed assemblies read-only.
// It never boots Derail Valley or auditions sound. Catalogue/discovery are
// deliberately seeded, so these checks prove audio hook/lifecycle ownership.
public static class NativeSoundChecks
{
    const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Owner = "jimtaplin.derailvalley.toolbox.locosoundpitch";
    const string Other = "dads-toolbox-fixture.sound-foreign";
    static Assembly toolbox, harmony;
    static Type moduleType, engineType;
    static object module, options;
    static string conflict;
    static readonly List<Object> objects = new List<Object>();
    static readonly StringBuilder report = new StringBuilder();
    static int checks, failures;
    static int ticks, pending;
    static string output;
    static readonly List<object> foreign = new List<object>();

    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object Get(object obj, string name) => (obj is Type ? (Type)obj : obj.GetType()).GetField(name, BF).GetValue(obj is Type ? null : obj);
    static void Set(object obj, string name, object value) => (obj is Type ? (Type)obj : obj.GetType()).GetField(name, BF).SetValue(obj is Type ? null : obj, value);
    static object Call(object obj, string method, params object[] args) => (obj is Type ? (Type)obj : obj.GetType()).GetMethod(method, BF).Invoke(obj is Type ? null : obj, args);
    static void Check(bool condition, string label) { checks++; report.AppendLine((condition ? "PASS " : "FAIL ") + label); if (!condition) failures++; }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.0001f;
    static object New(Type type) => Activator.CreateInstance(type, true);
    static TObj Keep<TObj>(TObj obj) where TObj : Object { objects.Add(obj); return obj; }
    static bool Active(object obj) => (bool)obj.GetType().GetProperty("Active").GetValue(obj, null);
    static string Status(object obj) => (string)obj.GetType().GetProperty("Status").GetValue(obj, null);
    static object Module(Action<string> log = null, Func<string> probe = null) => Activator.CreateInstance(moduleType,
        options, log ?? (Action<string>)(s => report.AppendLine("INFO " + s)), output, probe ?? (Func<string>)(() => conflict));

    static MethodBase[] OwnedMethods(string id)
    {
        var ht = harmony.GetType("HarmonyLib.Harmony", true);
        return ((IEnumerable)ht.GetMethod("GetAllPatchedMethods").Invoke(null, null)).Cast<MethodBase>()
            .Where(m => { var info = ht.GetMethod("GetPatchInfo").Invoke(null, new object[] { m });
                return ((IEnumerable)info.GetType().GetProperty("Owners").GetValue(info, null)).Cast<string>().Contains(id); }).ToArray();
    }
    static object ForeignPatch(string id, MethodBase target)
    {
        var ht = harmony.GetType("HarmonyLib.Harmony", true);
        var instance = Activator.CreateInstance(ht, id);
        foreign.Add(instance);
        var hm = Activator.CreateInstance(harmony.GetType("HarmonyLib.HarmonyMethod", true), typeof(NativeSoundChecks).GetMethod("Observe", BF));
        var patch = ht.GetMethod("Patch");
        var args = new object[patch.GetParameters().Length]; args[0] = target; args[2] = hm; patch.Invoke(instance, args);
        return instance;
    }
    static void Observe() { }
    static bool ClockTick() { ticks++; return false; }
    static bool ClockPending() { pending++; return false; }
    static void ThrowDuringPitch() { throw new InvalidOperationException("controlled later-prefix failure"); }
    static object Prefix(string id, MethodBase target, string method, int priority = 400)
    {
        var ht = harmony.GetType("HarmonyLib.Harmony", true);
        var instance = Activator.CreateInstance(ht, id); foreign.Add(instance);
        var hm = Activator.CreateInstance(harmony.GetType("HarmonyLib.HarmonyMethod", true), typeof(NativeSoundChecks).GetMethod(method, BF));
        Set(hm, "priority", priority);
        var patch = ht.GetMethod("Patch"); var args = new object[patch.GetParameters().Length];
        args[0] = target; args[1] = hm; patch.Invoke(instance, args); return instance;
    }
    static void Unpatch(object instance, string id)
    {
        // The same synthetic observer method is intentionally reused by two
        // owners. Harmony.UnpatchAll can remove that method under both IDs;
        // match production's owner-filtered per-target overload instead.
        var ht = instance.GetType();
        var kind = harmony.GetType("HarmonyLib.HarmonyPatchType", true);
        var method = ht.GetMethod("Unpatch", new[] { typeof(MethodBase), kind, typeof(string) });
        foreach (var target in OwnedMethods(id)) method.Invoke(instance, new[] { (object)target, Enum.Parse(kind, "All"), id });
    }

    sealed class AudioFixture
    {
        internal Component car, layered;
        internal AudioSource source, direct;
        internal AudioClip clip, custom;
        internal object engine, layer, binding, catalogue;
        internal MethodInfo setPitch;
        internal float baseline;
    }

    static AudioFixture CreateAudio()
    {
        var f = new AudioFixture();
        var root = Keep(new GameObject("Controlled CCL audio ownership")); root.SetActive(false);
        f.car = root.AddComponent(T("TrainCar"));
        var livery = Keep(ScriptableObject.CreateInstance(T("DV.ThingTypes.TrainCarLivery")));
        Set(livery, "id", "ToolboxSoundFixture"); Set(f.car, "carLivery", livery);
        f.engine = New(engineType.GetNestedType("Engine", BF));
        Set(f.engine, "Car", f.car); Set(f.engine, "Stock", false); Set(f.engine, "Steam", false); Set(f.engine, "Mass", 200d);
        ((IList)Get(engineType, "Engines")).Add(f.engine);
        f.catalogue = Get(engineType, "Catalogue");
        var donor = New(T("DadsToolbox.LocoSound.StockAudio+Donor"));
        Set(donor, "Id", "ControlledNativeDonor"); Set(donor, "Mask", 1); Set(donor, "Mass", 100d);
        ((IList)Get(f.catalogue, "Donors")).Add(donor);
        f.clip = Keep(AudioClip.Create("Exact native reference fixture", 4096, 1, 22050, false));
        f.custom = Keep(AudioClip.Create("Same-name replacement fixture", 4096, 1, 22050, false));
        var info = New(T("DadsToolbox.LocoSound.StockAudio+ClipInfo"));
        Set(info, "Donors", 1); Set(info, "Kind", Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"), "Unknown"));
        ((IDictionary)Get(f.catalogue, "Clips")).Add(f.clip, info);

        var audioRoot = new GameObject("Exterior controlled layer"); audioRoot.transform.SetParent(root.transform, false);
        f.layered = audioRoot.AddComponent(T("LayeredAudio"));
        Set(f.layered, "type", Enum.Parse(T("LayeredAudio+Type"), "Continuous"));
        f.source = audioRoot.AddComponent<AudioSource>(); f.source.playOnAwake = false; f.source.clip = f.clip;
        f.layer = New(T("LayeredAudio+Layer")); Set(f.layer, "source", f.source); Set(f.layer, "startPitch", 1.2f);
        var layers = Array.CreateInstance(f.layer.GetType(), 1); layers.SetValue(f.layer, 0); Set(f.layered, "layers", layers);
        f.setPitch = f.layer.GetType().GetMethod("SetPitch", BF);
        f.setPitch.Invoke(f.layer, new object[] { .5f, true, .5f, 1.5f }); f.baseline = f.source.pitch;
        f.binding = New(engineType.GetNestedType("Binding", BF));
        Set(f.binding, "Engine", f.engine); Set(f.binding, "Audio", f.layered); Set(f.binding, "Source", f.source); Set(f.binding, "Clip", f.clip);
        Set(f.binding, "Donors", 1); Set(f.binding, "Kind", Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"), "Unknown"));
        Call(Get(f.binding, "AllowedClips"), "Add", f.clip);
        ((IDictionary)Get(engineType, "Layers")).Add(f.layer, f.binding);
        Call(Get(engineType, "Layered"), "Add", f.layered);
        ((IList)Get(f.engine, "Nodes")).Add(f.layered);
        var directRoot = new GameObject("Exterior native failure fixture"); directRoot.transform.SetParent(root.transform, false);
        f.direct = directRoot.AddComponent<AudioSource>(); f.direct.playOnAwake = true; f.direct.clip = f.clip; f.direct.pitch = 1.1f;
        return f;
    }

    static void Pitch(AudioFixture f) => f.setPitch.Invoke(f.layer, new object[] { .5f, true, .5f, 1.5f });
    static void Refresh() => Call(engineType, "Refresh", new object[] { null });
    static void Discover(AudioFixture f) => Call(T("DadsToolbox.LocoSound.DirectAudio"), "Discover", f.engine, f.catalogue, new[] { f.car.gameObject });

    static void DirectionalPitchChecks(AudioFixture f)
    {
        var donor=((IList)Get(f.catalogue,"Donors")).Cast<object>().Single(d=>(int)Get(d,"Mask")==1);
        var clipInfo=((IDictionary)Get(f.catalogue,"Clips"))[f.clip];
        var chuffs=Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"),"Chuffs");
        var maximum=options.GetType().GetField("MaximumPitch",BF);
        Action<double,double,float> steam=(area,baseline,strength)=>{
            Set(f.engine,"Steam",true); Set(f.engine,"Area",area); Set(donor,"Area",baseline);
            Set(f.catalogue,"Area060",18.06d); Set(f.catalogue,"Area282",47d);
            Set(options,"Strength",strength); Set(f.binding,"Kind",chuffs); Set(clipInfo,"Kind",chuffs);
            ((IDictionary)Get(f.engine,"ResolvedDonors")).Clear();
        };
        try
        {
            steam(44.50,47,6.4955945f);
            float c48=(float)Math.Pow(44.50/47,-.2*6.4955945f);
            long writes=(long)Get(f.binding,"ShiftWrites"); Pitch(f);
            Check(f.source.pitch>f.baseline && Near(f.source.pitch,f.baseline*c48) && (long)Get(f.binding,"ShiftWrites")==writes+1,
                "C48A-sized boiler using S282 audio changes actual AudioSource pitch upward and records a write");
            int firstAppliedLog=report.ToString().Split(new[]{"Applied pitch:"},StringSplitOptions.None).Length;
            Check(firstAppliedLog>1,"completed native pitch write produces an applied-pitch diagnostic");
            for(int i=0;i<20;i++)Pitch(f);
            Check(Near(f.source.pitch,f.baseline*c48) && Near((float)Get(f.layer,"startPitch"),1.2f),"repeated upward steam calls preserve native baseline without compounding");
            Check(report.ToString().Split(new[]{"Applied pitch:"},StringSplitOptions.None).Length==firstAppliedLog,"applied-pitch diagnostic is not repeated for every audio update");
            Set(engineType,"Context",f.engine);
            var shot=toolbox.GetType("DadsToolbox.LocoSound.OneShotPatch",true).GetMethod("Prefix",BF);
            var args=new object[]{f.clip,null,null,1.5f};shot.Invoke(null,args);
            Check(Near((float)args[3],1.5f*c48),"smaller steam boiler raises native one-shot pitch before scheduling");
            steam(47,47,6.4955945f);writes=(long)Get(f.binding,"ShiftWrites");Pitch(f);
            Check(Near(f.source.pitch,f.baseline) && (long)Get(f.binding,"ShiftWrites")==writes,"L74-sized boiler using S282 audio stays neutral with no pitch write");
            steam(94,47,1);Pitch(f);
            Check(f.source.pitch<f.baseline && Near(f.source.pitch,f.baseline*(float)Math.Pow(2,-.2)),"larger boiler lowers actual native steam layer pitch");
            steam(18.06,47,1);Pitch(f);
            Check(f.source.pitch>f.baseline,"matching S060 boiler size does not suppress an S282-donor upward adjustment");
            steam(47,18.06,1);Pitch(f);
            Check(f.source.pitch<f.baseline,"matching S282 boiler size does not suppress an S060-donor downward adjustment");
            Check(maximum!=null,"steam upper pitch cap is independently configurable");
            if(maximum!=null)
            {
                maximum.SetValue(options,1.1f);steam(5,47,10);Pitch(f);
                Check(Near(f.source.pitch,f.baseline*1.1f),"steam upper cap limits actual upward pitch writes");
                Set(options,"MinimumPitch",.8f);steam(470,47,10);Pitch(f);
                Check(Near(f.source.pitch,f.baseline*.8f),"steam lower cap limits actual downward pitch writes");
                Set(options,"MinimumPitch",.25f);maximum.SetValue(options,2f);
                steam(44.50,47,0);Pitch(f);
                Check(Near(f.source.pitch,f.baseline),"zero-percent steam strength restores native pitch");
                steam(44.50,47,1);Pitch(f);float normal=f.source.pitch;
                Set(options,"Strength",.5f);Refresh();
                Check(f.source.pitch>f.baseline && f.source.pitch<normal,"steam strength edits refresh captured input at the reduced percentage");
                steam(44.50,47,1);Pitch(f);Discover(f);
                Check(f.direct.pitch>1.1f,"smaller boiler also raises owned direct AudioSource pitch");
                Set(options,"Strength",0f);Refresh();
                Check(Near(f.source.pitch,f.baseline) && Near(f.direct.pitch,1.1f),"zero steam strength restores both layered and direct sources");
                steam(47,47,1);Set(f.engine,"Stock",true);Pitch(f);
                Check(Near(f.source.pitch,f.baseline),"real vanilla identity remains protected with bidirectional steam scaling");
                Set(f.engine,"Stock",false);
                steam(44.50,47,1);f.source.clip=f.custom;Pitch(f);
                Check(Near(f.source.pitch,f.baseline),"custom replacement stays excluded from upward steam scaling");
                f.source.clip=f.clip;Set(f.binding,"Kind",Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"),"CabEquipment"));Pitch(f);
                Check(Near(f.source.pitch,f.baseline),"cab-equipment exclusion also prevents upward steam writes");
            }
            Set(f.binding,"Kind",chuffs);
            Set(f.engine,"Steam",false);Set(f.engine,"Mass",50d);Set(donor,"Area",0d);
            Set(options,"NonSteamStrength",1f);Set(options,"NonSteamMaximumPitch",1.1f);Pitch(f);
            Check(Near(f.source.pitch,f.baseline*1.1f),"lighter diesel receives actual upward pitch bounded by its upper cap");
            Set(f.engine,"Mass",200d);Set(options,"NonSteamMinimumPitch",.9f);Pitch(f);
            Check(Near(f.source.pitch,f.baseline*.9f),"heavier diesel receives actual downward pitch bounded by its lower cap");
            Set(f.engine,"Mass",100d);Pitch(f);
            Check(Near(f.source.pitch,f.baseline),"diesel matching its actual sound donor mass remains neutral");
            Set(f.engine,"Mass",50d);Set(options,"NonSteamMaximumPitch",2f);Set(options,"NonSteamStrength",0f);Pitch(f);
            Check(Near(f.source.pitch,f.baseline),"zero-percent diesel strength restores actual native pitch");
        }
        finally
        {
            Set(engineType,"Context",null);Set(f.engine,"Steam",false);Set(f.engine,"Mass",200d);Set(f.engine,"Stock",false);
            Set(donor,"Area",0d);Set(options,"Strength",1f);Set(options,"MinimumPitch",.25f);if(maximum!=null)maximum.SetValue(options,2f);
            Set(options,"NonSteamStrength",1f);Set(options,"NonSteamMinimumPitch",.25f);Set(options,"NonSteamMaximumPitch",2f);
            Set(f.binding,"Kind",Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"),"Unknown"));Set(clipInfo,"Kind",Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"),"Unknown"));
            f.source.clip=f.clip;((IDictionary)Get(f.engine,"ResolvedDonors")).Clear();Refresh();
            Call(T("DadsToolbox.LocoSound.DirectAudio"),"Clear",true);
        }
    }

    public static void Run() { EditorApplication.update += Start; }
    static void Start()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= Start;
        output = Environment.GetEnvironmentVariable("TOOLBOX_SOUND_OUT"); Directory.CreateDirectory(output);
        string game = Environment.GetEnvironmentVariable("TOOLBOX_GAME_PATH");
        string managed = Path.Combine(game, "DerailValley_Data/Managed");
        ResolveEventHandler resolver = (s, e) => {
            foreach (var folder in new[] { managed, Path.Combine(managed, "UnityModManager") })
            { var path = Path.Combine(folder, new AssemblyName(e.Name).Name + ".dll"); if (File.Exists(path)) return Assembly.LoadFrom(path); }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            foreach (string file in new[] { "Assembly-CSharp.dll", "DV.LayeredAudio.dll", "DV.NAudio.dll", "DV.ThingTypes.dll" }) Assembly.LoadFrom(Path.Combine(managed, file));
            harmony = Assembly.LoadFrom(Path.Combine(managed, "UnityModManager/0Harmony.dll"));
            toolbox = Assembly.LoadFrom(Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_DLL"));
            report.AppendLine("Unity " + Application.unityVersion + "; actual DLL: " + toolbox.Location);
            using (var hash = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(toolbox.Location))
                report.AppendLine("Toolbox SHA256: " + BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""));
            moduleType = toolbox.GetType("DadsToolbox.LocoSound.SoundPitchModule", true);
            engineType = toolbox.GetType("DadsToolbox.LocoSound.SoundEngine", true);
            options = New(toolbox.GetType("DadsToolbox.LocoSound.SoundOptions", true));
            module = Module();
            Call(module, "Configure", false, true);
            Check(!Active(module) && OwnedMethods(Owner).Length == 0, "saved off creates no owned patches");
            conflict = "Standalone sound mod loaded; restart required"; Call(module, "Configure", true, true);
            Check(!Active(module) && Status(module).Contains("Standalone"), "native parent conflict probe preserves desired-on while inactive");
            conflict = null; Call(module, "Configure", true, true);
            Check(Active(module), "actual six native Harmony class processors activate");
            Check(OwnedMethods(Owner).Length == 20, "exactly twenty intended native audio methods patched under exclusive owner");
            var initial = OwnedMethods(Owner).Select(m => m.ToString()).OrderBy(x => x).ToArray();
            Call(module, "Configure", true, true);
            Check(OwnedMethods(Owner).Length == 20, "repeat configure does not duplicate native hooks");
            var duplicate = Module(); Call(duplicate, "Configure", true, true);
            Check(!Active(duplicate) && Active(module), "second module instance cannot take the engine");
            Check((bool)Call(duplicate, "Unload") && OwnedMethods(Owner).Length == 20, "unloading rejected duplicate preserves first owner");
            var clock = Prefix("dads-toolbox-fixture.sound-clock", engineType.GetMethod("Tick", BF), "ClockTick");
            Prefix("dads-toolbox-fixture.sound-clock", engineType.GetMethod("ProcessPending", BF), "ClockPending");
            foreach (float now in new[] { 0f, .05f, .1f, .24f, .25f, .3f, .5f }) Call(module, "Tick", now);
            Check(ticks == 3 && pending == 7, "parent frame dispatcher keeps quarterly discovery cadence and one pending dispatch per update");
            Unpatch(clock, "dads-toolbox-fixture.sound-clock");
            var f = CreateAudio();
            DirectionalPitchChecks(f);
            float factor = (float)Math.Pow(2, -.2);
            Set(engineType, "Context", f.engine);
            var oneShot = toolbox.GetType("DadsToolbox.LocoSound.OneShotPatch", true).GetMethod("Prefix", BF);
            var shotArgs = new object[] { f.clip, null, null, 1.5f }; oneShot.Invoke(null, shotArgs);
            Check(Near((float)shotArgs[3], 1.5f * factor), "parentless native one-shot argument uses scoped car ownership before scheduling");
            shotArgs = new object[] { f.custom, null, null, 1.5f }; oneShot.Invoke(null, shotArgs);
            Check(Near((float)shotArgs[3], 1.5f), "custom one-shot clip leaves native starting pitch unchanged");
            Set(f.engine, "Mass", .01d);
            Set(options, "NonSteamMaximumPitch", 3f);
            ((IDictionary)Get(f.engine, "ResolvedDonors")).Clear();
            shotArgs = new object[] { f.clip, null, null, 2f }; oneShot.Invoke(null, shotArgs);
            Check(Near((float)shotArgs[3], 3f), "changed positive one-shot pitch is clamped before native pool deadline calculation");
            Set(f.engine, "Mass", 200d); Set(options, "NonSteamMaximumPitch", 2f);
            Set(engineType, "Context", null);
            Pitch(f);
            Check(Near(f.source.pitch, f.baseline * factor), "native Layer.SetPitch applies configured mass multiplier to native baseline");
            Check(Near((float)Get(f.layer, "startPitch"), 1.2f), "layer finalizer restores authored startPitch");
            var throwing = Prefix("dads-toolbox-fixture.sound-throw", f.setPitch, "ThrowDuringPitch", 0);
            bool threw = false;
            try { Pitch(f); } catch (TargetInvocationException ex) { threw = ex.InnerException is InvalidOperationException; }
            Check(threw && Near((float)Get(f.layer, "startPitch"), 1.2f), "native pitch finalizer restores temporary baseline when a later prefix throws");
            Unpatch(throwing, "dads-toolbox-fixture.sound-throw");
            for (int i = 0; i < 30; i++) Pitch(f);
            Check(Near(f.source.pitch, f.baseline * factor), "repeated native pitch calls do not compound");
            Set(f.engine, "Stock", true); Pitch(f);
            Check(Near(f.source.pitch, f.baseline), "vanilla/non-CCL protected state stays native");
            Set(f.engine, "Stock", false);
            Set(f.binding, "Kind", Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"), "CabEquipment")); Pitch(f);
            Check(Near(f.source.pitch, f.baseline), "cab-equipment classification excludes pitch writes");
            Set(f.binding, "Kind", Enum.Parse(T("DadsToolbox.LocoSound.SoundKind"), "Unknown"));
            f.source.clip = f.custom; Pitch(f);
            Check(Near(f.source.pitch, f.baseline), "replacement clip is not adopted by layer binding");
            f.source.clip = f.clip; Pitch(f);
            Discover(f);
            Check(Near(f.direct.pitch, 1.1f * factor), "native playOnAwake direct source receives size pitch");
            f.direct.pitch = .77f; Refresh();
            Check(Near(f.direct.pitch, .77f), "foreign direct-source pitch change relinquishes ownership");
            Call(T("DadsToolbox.LocoSound.DirectAudio"), "Clear", false); Discover(f);
            Check(Near(f.direct.pitch, .77f), "full reference rebuild preserves relinquished source marker");
            Call(T("DadsToolbox.LocoSound.DirectAudio"), "Remove", f.engine, false); Discover(f);
            Check(Near(f.direct.pitch, .77f), "per-car rebuild preserves relinquished source marker");
            var target = f.setPitch;
            var other = ForeignPatch(Other, target);
            Check(OwnedMethods(Other).Length == 1, "foreign Harmony observer shares native target");
            Check((bool)Call(module, "Stop"), "stop succeeds after restoring owned native continuous layer");
            Check(Near(f.source.pitch, f.baseline) && Near(f.direct.pitch, .77f), "stop restores owned layer and preserves foreign direct pitch");
            Check(OwnedMethods(Owner).Length == 0 && OwnedMethods(Other).Length == 1, "stop removes only sound feature hooks");
            Check((bool)moduleType.GetProperty("DesiredEnabled").GetValue(module, null), "runtime stop preserves saved desired preference");
            Call(module, "Tick", 100f);
            Call(module, "Refresh");
            Check(!Active(module) && OwnedMethods(Owner).Length == 0 && Near(f.source.pitch, f.baseline), "explicit stop prevents Tick and manual Refresh from reactivating audio while parent is off");
            Check((bool)moduleType.GetProperty("DesiredEnabled").GetValue(module, null), "stopped Tick/Refresh retain the saved desired-on preference");
            Call(module, "Configure", true, true); Pitch(f); Discover(f);
            Check(Active(module) && Near(f.source.pitch, f.baseline * factor) && Near(f.direct.pitch, .77f), "re-enable restores effect without reclaiming relinquished source");
            Check(OwnedMethods(Owner).Select(m => m.ToString()).OrderBy(x => x).SequenceEqual(initial), "re-enable restores exact native hook set");
            conflict = "Late standalone activation"; Call(module, "Configure", true, true);
            Check(!Active(module) && Near(f.source.pitch, f.baseline), "late native conflict stops and restores only sound module");
            conflict = null; Call(module, "Configure", true, true);
            var old = ForeignPatch("SteamSizePitch", target); Call(module, "Configure", true, true);
            Check(!Active(module) && OwnedMethods("SteamSizePitch").Length == 1 && Status(module).Contains("restart"), "residual standalone owner blocks without unpatching foreign implementation");
            Unpatch(old, "SteamSizePitch");
            Check((bool)Call(module, "Unload"), "final unload cleans stopped engine");
            Call(module, "Configure", true, true);
            Check(!Active(module) && OwnedMethods(Owner).Length == 0, "final unloaded instance cannot restart");
            Check(((IDictionary)Get(engineType, "Layers")).Count == 0 && Get(engineType, "Config") == null, "unload clears audio bindings/options/context for fresh lifetime");
            module = Module(s => { throw new InvalidOperationException("host logger failed"); });
            Call(module, "Configure", true, true);
            Check(Active(module), "fresh lifetime tolerates host logger faults and activates");
            Check((bool)Call(module, "Unload") && OwnedMethods(Other).Length == 1, "fresh lifetime unload preserves foreign patch");
            module = Module(probe: () => { throw new InvalidOperationException("probe failed"); });
            Call(module, "Configure", true, true);
            Check(!Active(module) && Status(module).Contains("check failed"), "failing compatibility probe fails closed locally");
            Check((bool)Call(module, "Unload"), "failed-closed module unload is safe");
            Unpatch(other, Other);
            var ownerCollision = ForeignPatch(Owner, target);
            module = Module(); Call(module, "Configure", true, true);
            Check(!Active(module) && Status(module).Contains("already in use"), "residual integrated owner rejects duplicate installation before engine takeover");
            Check((bool)Call(module, "Unload") && OwnedMethods(Owner).Length == 1, "rejected owner collision cannot unpatch existing owner");
            Unpatch(ownerCollision, Owner);
        }
        catch (Exception ex) { failures++; report.AppendLine("ERROR " + ex); }
        finally
        {
            try { if (module != null) Call(module, "Unload"); } catch (Exception ex) { report.AppendLine("CLEANUP " + ex); failures++; }
            foreach (var id in new[] { Owner, Other, "SteamSizePitch", "dads-toolbox-fixture.sound-clock", "dads-toolbox-fixture.sound-throw" })
                try { if (harmony != null) Unpatch(Activator.CreateInstance(harmony.GetType("HarmonyLib.Harmony"), id), id); } catch { }
            foreach (var obj in objects.AsEnumerable().Reverse()) if (obj) Object.DestroyImmediate(obj);
            report.AppendLine("Checks: " + checks + "; failures: " + failures);
            File.WriteAllText(Path.Combine(output, "native-fixture-checks.txt"), report.ToString());
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
