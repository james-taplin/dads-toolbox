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

// Real UMM event dispatch and installed SM discovery. The fixture replaces only
// CCL's asset-import body with controlled livery registration; no skin is injected.
public static class NativeSkinRegistrationChecks
{
    const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const string FixtureOwner = "dads-toolbox-fixture.skinregistration";
    const string FixOwner = "jimtaplin.derailvalley.toolbox.skinregistration";
    static readonly StringBuilder Report = new StringBuilder();
    static readonly List<Object> Objects = new List<Object>();
    static Assembly native, things, sm, ccl, umm, harmony, toolbox;
    static Type provider, modType, infoType, paintType, customType;
    static MethodInfo callback, cclCallback;
    static object fixtureHarmony, fix, currentPack;
    static IList liveries, modEntries;
    static readonly List<Object> prepared = new List<Object>();
    static readonly List<Delegate> listeners = new List<Delegate>();
    static readonly List<string> Order = new List<string>();
    static int checks, failures, errors, registrations;
    static bool register = true, throwRegistration;
    static string output, packPath, cclVersion;
    [Serializable] sealed class PackageInfo { public string Version; }
    static readonly string[] Ids = { "ToolboxStartupEngine", "ToolboxStartupTender" };
    static readonly string[] Themes = { "DVRT", "DVRT_New", "Null" };
    static readonly string[] Props = { "_MainTex", "_MetallicGlossMap", "_BumpMap" };

    static void Check(bool pass, string message) { checks++; Report.AppendLine((pass ? "PASS " : "FAIL ") + message); if (!pass) failures++; }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, BF).SetValue(target, value); }
    static object Get(object target, string name) { return target.GetType().GetField(name, BF).GetValue(target); }
    static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, BF).Invoke(target, args); }
    static T Keep<T>(T obj) where T : Object { Objects.Add(obj); return obj; }
    static bool Active { get { return (bool)fix.GetType().GetProperty("Active").GetValue(fix, null); } }
    static int Pending { get { return (int)fix.GetType().GetProperty("PendingCount").GetValue(fix, null); } }
    static int Recovered { get { return (int)fix.GetType().GetProperty("RecoveredPacks").GetValue(fix, null); } }
    static bool Install(object instance) { return (bool)Call(instance, "Install", sm, ccl, (Action<string>)(s => Report.AppendLine("INFO " + s))); }
    static void Stop(object instance) { Call(instance, "Uninstall"); }
    static object HM(string name) { return Activator.CreateInstance(harmony.GetType("HarmonyLib.HarmonyMethod"), new object[] { typeof(NativeSkinRegistrationChecks).GetMethod(name, BF) }); }
    static void Patch(MethodInfo target, string prefix = null, string postfix = null)
    {
        var patch = fixtureHarmony.GetType().GetMethod("Patch");
        var args = new object[patch.GetParameters().Length]; args[0] = target;
        if (prefix != null) args[1] = HM(prefix);
        if (postfix != null) args[2] = HM(postfix);
        patch.Invoke(fixtureHarmony, args);
    }
    static bool RegistrationBody(object __0, bool __1)
    {
        if (__1 && ReferenceEquals(__0, currentPack))
        {
            registrations++; Order.Add("CCL registration");
            if (register) foreach (var livery in prepared) if (!liveries.Contains(livery)) liveries.Add(livery);
            if (throwRegistration) throw new InvalidOperationException("EXPECTED fixture import failure");
        }
        return false; // The generated marker file is not a real ccl_bundle.
    }
    static void DiscoveryBody() { Order.Add("SM discovery"); }
    static void ErrorSeen(string __0) { if (__0.StartsWith("Unknown livery id:")) { errors++; Order.Add("unknown ID"); } }
    static int ConfigCount(object mod)
    {
        var collections = (IEnumerable)provider.GetField("skinConfigs", BF).GetValue(null);
        foreach (var collection in collections)
            if (ReferenceEquals(Get(collection, "modEntry"), mod)) return ((IEnumerable)Get(collection, "Configs")).Cast<object>().Count();
        return 0;
    }
    static void Toggle(object mod, bool value) { modType.GetProperty("Active").SetValue(mod, value, null); }
    static void Subscribe(bool cclFirst = false)
    {
        Unsubscribe();
        var evt = umm.GetType("UnityModManagerNet.UnityModManager").GetEvent("toggleModsListen");
        foreach (var method in cclFirst ? new[] { cclCallback, callback } : new[] { callback, cclCallback })
        {
            var listener = Delegate.CreateDelegate(evt.EventHandlerType, method);
            evt.AddEventHandler(null, listener); listeners.Add(listener);
        }
    }
    static void Unsubscribe()
    {
        if (umm == null) return;
        var evt = umm.GetType("UnityModManagerNet.UnityModManager").GetEvent("toggleModsListen");
        foreach (var listener in listeners) evt.RemoveEventHandler(null, listener);
        listeners.Clear();
    }
    static object Entry(string id, string path, bool active, string version = "1.0.0", Assembly assembly = null)
    {
        Directory.CreateDirectory(path);
        var info = Activator.CreateInstance(infoType); Set(info, "Id", id); Set(info, "Version", version); Set(info, "ManagerVersion", "0.27.3");
        var entry = Activator.CreateInstance(modType, new object[] { info, path + Path.DirectorySeparatorChar });
        Set(entry, "mActive", active); Set(entry, "mStarted", true);
        if (assembly != null) Set(entry, "mAssembly", assembly);
        return entry;
    }
    static void PrepareData()
    {
        var globals = Keep(ScriptableObject.CreateInstance(native.GetType("DV.Globals")));
        var model = Keep(ScriptableObject.CreateInstance(things.GetType("DV.ThingTypes.DVObjectModel")));
        liveries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(things.GetType("DV.ThingTypes.TrainCarLivery")));
        Set(model, "_liveries", liveries); Set(globals, "types", model);
        globals.GetType().GetField("_current", BF).SetValue(null, globals);
        model.GetType().GetField("current", BF).SetValue(null, model);
        var smMain = sm.GetType("SkinManagerMod.Main");
        var settings = smMain.GetProperty("Settings", BF);
        var configured = Activator.CreateInstance(settings.PropertyType); Set(configured, "parallelLoading", false);
        settings.GetSetMethod(true).Invoke(null, new[] { configured });
        var smEntry = Entry("SkinManagerMod", Path.Combine(output, "host"), true, "4.4.1", sm);
        smMain.GetProperty("Instance", BF).GetSetMethod(true).Invoke(null, new[] { smEntry });
        modEntries.Add(smEntry);
        modEntries.Add(Entry("DVCustomCarLoader", Path.Combine(output, "ccl-host"), true, cclVersion, ccl));
        var themeDict = (IDictionary)provider.GetField("_themeDict", BF).GetValue(null);
        var nativeDict = (IDictionary)provider.GetField("_builtInThemeDict", BF).GetValue(null);
        foreach (string name in new[] { "DVRT", "DVRT_New", "Relic", "Relic_Rusty", "Null" })
        {
            var theme = Keep(ScriptableObject.CreateInstance(customType)); theme.name = name;
            themeDict[name] = theme; nativeDict[name] = theme;
        }
        packPath = Path.Combine(output, "embedded-pack"); Directory.CreateDirectory(packPath);
        File.WriteAllText(Path.Combine(packPath, "ccl_bundle"), "Fixture marker: CCL body replaced by controlled registration.");
        foreach (string id in Ids)
        {
            var root = Keep(new GameObject(id + " prefab")); root.SetActive(false);
            var material = Keep(new Material(Shader.Find("Standard")) { name = id + " body" });
            foreach (string prop in Props)
            {
                var tex = Keep(new Texture2D(4, 4)); tex.name = id + prop;
                material.SetTexture(prop, tex);
            }
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube); leaf.transform.SetParent(root.transform, false);
            leaf.GetComponent<Renderer>().sharedMaterial = material;
            var paint = root.AddComponent(paintType);
            Set(paint, "sets", Array.CreateInstance(paintType.GetNestedType("MaterialSet"), 0));
            var livery = Keep(ScriptableObject.CreateInstance(things.GetType("DV.ThingTypes.TrainCarLivery")));
            Set(livery, "id", id); Set(livery, "prefab", root);
            var parent = Keep(ScriptableObject.CreateInstance(livery.GetType().GetField("parentType", BF).FieldType));
            Set(parent, "id", id); Set(livery, "parentType", parent);
            prepared.Add(livery);
            foreach (string theme in Themes)
            {
                string folder = Path.Combine(packPath, id, theme); Directory.CreateDirectory(folder);
                string baseTheme = theme == "DVRT_New" ? "Pristine" : theme == "Null" ? "Primer" : "DVRT";
                File.WriteAllText(Path.Combine(folder, "skin.json"), "{\"Name\":\"" + theme + "\",\"CarId\":\"" + id + "\",\"BaseTheme\":\"" + baseTheme + "\"}");
                foreach (string prop in Props)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var color = theme == "DVRT_New" ? Color.blue : theme == "Null" ? Color.gray : Color.black;
                    tex.SetPixels(Enumerable.Repeat(color, 16).ToArray()); tex.Apply();
                    File.WriteAllBytes(Path.Combine(folder, id + prop + ".png"), tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                }
            }
        }
        currentPack = Entry("EmbeddedFixture", packPath, false); modEntries.Add(currentPack);
    }

    public static void Run() { EditorApplication.update += Start; }
    static void Start()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= Start;
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        output = Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_OUT") ?? Path.Combine(root, "evidence/skin-startup");
        Directory.CreateDirectory(output);
        string game = Environment.GetEnvironmentVariable("TOOLBOX_GAME_PATH") ?? @"B:\SteamLibrary\steamapps\common\Derail Valley";
        var managed = Path.Combine(game, "DerailValley_Data/Managed");
        ResolveEventHandler resolver = (s, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string folder in new[] { managed, Path.Combine(managed, "UnityModManager"), Path.Combine(game, "Mods/SkinManagerMod"), Path.Combine(game, "Mods/DVCustomCarLoader"), Path.Combine(game, "Mods/DVLangHelper") })
            { string path = Path.Combine(folder, name); if (File.Exists(path)) return Assembly.LoadFrom(path); }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            Report.AppendLine("Unity " + Application.unityVersion);
            native = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
            things = Assembly.LoadFrom(Path.Combine(managed, "DV.ThingTypes.dll"));
            harmony = Assembly.LoadFrom(Path.Combine(managed, "UnityModManager/0Harmony.dll"));
            umm = Assembly.LoadFrom(Path.Combine(managed, "UnityModManager/UnityModManager.dll"));
            sm = Assembly.LoadFrom(Path.Combine(game, "Mods/SkinManagerMod/SkinManagerMod.dll"));
            ccl = Assembly.LoadFrom(Path.Combine(game, "Mods/DVCustomCarLoader/CCL.Importer.dll"));
            cclVersion = JsonUtility.FromJson<PackageInfo>(File.ReadAllText(Path.Combine(game, "Mods/DVCustomCarLoader/Info.json"))).Version;
            Report.AppendLine("Installed CCL UMM version " + cclVersion);
            toolbox = Assembly.LoadFrom(Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_DLL") ?? Path.Combine(root, "build/DadsToolbox.dll"));
            provider = sm.GetType("SkinManagerMod.SkinProvider");
            modType = umm.GetType("UnityModManagerNet.UnityModManager+ModEntry"); infoType = umm.GetType("UnityModManagerNet.UnityModManager+ModInfo");
            modEntries = (IList)umm.GetType("UnityModManagerNet.UnityModManager").GetField("modEntries", BF).GetValue(null);
            paintType = native.GetType("DV.Customization.Paint.TrainCarPaint"); customType = sm.GetType("SkinManagerMod.CustomPaintTheme");
            callback = provider.GetMethod("HandleModToggle", BF); cclCallback = ccl.GetType("CCL.Importer.CarManager").GetMethod("HandleModToggled", BF);
            fixtureHarmony = Activator.CreateInstance(harmony.GetType("HarmonyLib.Harmony"), new object[] { FixtureOwner });
            Patch(cclCallback, "RegistrationBody");
            Patch(provider.GetMethod("ReloadSkinMod", BF), "DiscoveryBody");
            Patch(sm.GetType("SkinManagerMod.Main").GetMethod("Error", BF), "ErrorSeen");
            PrepareData(); Subscribe();
            Toggle(currentPack, true);
            Check(registrations == 1 && liveries.Count == 2, "controlled CCL listener registers both vehicle liveries after the early scan");
            Check(errors == 6 && ConfigCount(currentPack) == 0, "UNPATCHED real Skin Manager disk discovery permanently rejects six valid embedded configs before registration");
            Check(Order.IndexOf("SM discovery") < Order.IndexOf("CCL registration"), "actual UMM listeners reproduce Skin Manager-before-CCL ordering");
            Toggle(currentPack, false); liveries.Clear(); Order.Clear(); errors = 0;
            fix = Activator.CreateInstance(toolbox.GetType("DadsToolbox.SkinRegistrationFix"));
            Check(Install(fix) && Active, "compiled registration correction installs on actual SM and CCL methods");
            Check(Install(fix), "repeated install is idempotent");
            var duplicate = Activator.CreateInstance(fix.GetType());
            Check(!Install(duplicate), "a second instance cannot claim the same patch owner"); Stop(duplicate);
            Check(Active, "rejected duplicate cleanup preserves active correction");
            Toggle(currentPack, true);
            Check(errors == 0 && ConfigCount(currentPack) == 6, "PATCHED actual UMM activation automatically discovers all six embedded skins after registration");
            Check(Order.IndexOf("CCL registration") < Order.IndexOf("SM discovery") && Order.Count(x => x == "SM discovery") == 1,
                "one same-pack discovery follows synchronous registration without a frame timer or global rescan");
            Check(Pending == 0 && Recovered == 1, "activation drains its deferred work exactly once");
            var find = provider.GetMethod("FindSkinByName", new[] { typeof(string), typeof(string) });
            foreach (string id in Ids) foreach (string theme in Themes)
            {
                var skin = find.Invoke(null, new object[] { id, theme });
                Check(skin != null && !((bool)Get(skin, "IsDefault")) && ((IList)Get(skin, "SkinTextures")).Count == 3,
                    id + " " + theme + " uses actual file-discovered skin with three loaded PNGs");
            }
            RepaintPanels(find);
            // Check defaults/partial registration are not required to turn the
            // same definitions into successful configs: actual disk loading did it.
            int recovered = Recovered;
            var unrelatedPath = Path.Combine(output, "unrelated"); Directory.CreateDirectory(unrelatedPath);
            var unrelated = Entry("UnrelatedFixture", unrelatedPath, false); modEntries.Add(unrelated);
            Toggle(unrelated, true);
            Check(Recovered == recovered && Pending == 0, "unrelated activation does not repeatedly rescan the vehicle pack");
            Toggle(currentPack, false);
            Check(ConfigCount(currentPack) == 0, "deactivation still runs native Skin Manager unload");
            Toggle(currentPack, true);
            Check(ConfigCount(currentPack) == 6 && Recovered == recovered + 1 && errors == 0, "repeated deactivate/activate reloads once without duplicate configs or false unknown IDs");
            Toggle(currentPack, false); Subscribe(true); Order.Clear();
            Toggle(currentPack, true);
            Check(ConfigCount(currentPack) == 6 && Order.IndexOf("CCL registration") < Order.IndexOf("SM discovery"),
                "CCL-first listener order also discovers once without reordering the global event");
            Check(Pending == 0, "CCL-first order leaves no abandoned deferred item");
            Toggle(currentPack, false); Subscribe(); liveries.Clear(); Order.Clear(); errors = 0; register = false;
            Toggle(currentPack, true);
            Check(errors == 6 && Order.IndexOf("CCL registration") < Order.IndexOf("unknown ID"), "genuinely unresolved vehicle IDs are reported after the registration boundary");
            Check(Pending == 0, "failed registration has no endless discovery retry");
            Toggle(currentPack, false); register = true;
            Stop(fix);
            Check(!Active, "disable removes both owned hooks");
            Toggle(currentPack, true); Check(ConfigCount(currentPack) == 0, "disabled correction restores original early-discovery behavior");
            errors = 0; Order.Clear();
            Check(Install(fix) && ConfigCount(currentPack) == 6 && errors == 0,
                "one bounded catch-up recovers packs already active before Toolbox enables");
            Check(Pending == 0, "startup catch-up completes synchronously with no queued jobs");
            Toggle(currentPack, false); liveries.Clear(); errors = 0; throwRegistration = true;
            // Direct invocation exposes the same original exception rather than
            // letting UMM's outer logging boundary handle the expected failure.
            Set(currentPack, "mActive", true); callback.Invoke(null, new[] { currentPack, (object)true });
            bool preserved = false;
            try { cclCallback.Invoke(null, new[] { currentPack, (object)true }); }
            catch (TargetInvocationException ex) { preserved = ex.InnerException is InvalidOperationException && ex.InnerException.Message.Contains("EXPECTED"); }
            Check(preserved && Pending == 0, "completion finalizer drains work and preserves CCL's original import exception");
            throwRegistration = false;
            Stop(fix);
            ParentChecks();
        }
        catch (Exception ex) { failures++; Report.AppendLine("EXCEPTION " + ex); }
        finally
        {
            if (fix != null) try { Stop(fix); } catch (Exception ex) { failures++; Report.AppendLine(ex.ToString()); }
            Unsubscribe();
            if (fixtureHarmony != null) fixtureHarmony.GetType().GetMethod("UnpatchAll").Invoke(fixtureHarmony, new object[] { FixtureOwner });
            foreach (var obj in Objects.AsEnumerable().Reverse()) if (obj) Object.DestroyImmediate(obj);
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            Report.AppendLine("Checks: " + checks + "; failures: " + failures);
            Report.AppendLine("Scope: actual UMM activation event and installed Skin Manager callback/config discovery/PNG loader, actual CCL callback target with fixture-owned prefix replacing only its import body by controlled livery registration. No manually supplied skins, companion mod, full game boot, real ccl_bundle import, streaming or SaveManager.");
            File.WriteAllText(Path.Combine(output, "native-fixture-checks.txt"), Report.ToString());
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    static void RepaintPanels(MethodInfo find)
    {
        var dataType = sm.GetType("SkinManagerMod.CarMaterialData");
        var cache = (IDictionary)dataType.GetField("_liveryToMaterialsMap", BF).GetValue(null);
        var themeDict = (IDictionary)provider.GetField("_themeDict", BF).GetValue(null);
        foreach (var livery in prepared)
        {
            string id = (string)Get(livery, "id");
            cache[id] = Activator.CreateInstance(dataType, new object[] { livery });
            var holder = Keep(new GameObject(id + " inactive car")); holder.SetActive(false);
            var car = holder.AddComponent(native.GetType("TrainCar")); Set(car, "carLivery", livery);
            // Generated separately loaded panels have no painters, as in SM's
            // post-initialization path. Test its actual texture application.
            var panel = Keep(new GameObject(id + " moving panel"));
            var source = ((GameObject)Get(livery, "prefab")).GetComponentInChildren<Renderer>(true).sharedMaterial;
            var renderers = new List<Renderer>();
            for (int lod = 0; lod < 3; lod++)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube); leaf.name = "Panel LOD " + lod;
                leaf.transform.SetParent(panel.transform, false); leaf.GetComponent<Renderer>().sharedMaterial = new Material(source);
                renderers.Add(leaf.GetComponent<Renderer>());
            }
            var group = panel.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(.7f, new[] { renderers[0] }), new LOD(.4f, new[] { renderers[1] }), new LOD(.1f, new[] { renderers[2] }) });
            foreach (string name in new[] { "DVRT", "DVRT_New", "DVRT", "Null", "DVRT_New", "DVRT" })
            {
                panel.transform.localRotation = Quaternion.Euler(Ids[0] == id ? 65 : 35, 0, 0);
                var rotation = panel.transform.localRotation;
                var skin = find.Invoke(null, new object[] { id, name });
                customType.GetMethod("Apply").Invoke(themeDict[name], new object[] { panel, car });
                foreach (string prop in Props)
                {
                    var slot = skin.GetType().GetMethod("GetTexture").Invoke(skin, new object[] { id + prop });
                    var expected = (Texture)slot.GetType().GetProperty("TextureData").GetValue(slot, null);
                    Check(expected && renderers.All(r => r.sharedMaterial.GetTexture(prop) == expected),
                        id + " auto-discovered " + name + " updates " + prop + " on every generated moving-panel LOD");
                }
                Check(panel.transform.localRotation == rotation, id + " repaint preserves the open-panel transform");
            }
        }
    }

    static void ParentChecks()
    {
        var parentPath = Path.Combine(output, "parent"); Directory.CreateDirectory(parentPath);
        // Previous Toolbox settings did not have the registration option.
        File.WriteAllText(Path.Combine(parentPath, "Settings.xml"), "<ToolboxSettings><SkinDamageFix>true</SkinDamageFix><DoubleTrackSignClearance>false</DoubleTrackSignClearance></ToolboxSettings>");
        var parent = Entry("DadsToolbox", parentPath, true, "0.3.0", toolbox); modEntries.Add(parent);
        var main = toolbox.GetType("DadsToolbox.Main");
        main.GetMethod("Load").Invoke(null, new[] { parent });
        var module = main.GetField("skinRegistration", BF).GetValue(null);
        var damage = main.GetField("skinDamage", BF).GetValue(null);
        var settings = main.GetField("settings", BF).GetValue(null);
        Func<bool> running = () => (bool)module.GetType().GetProperty("Active").GetValue(module, null);
        Func<bool> scorch = () => (bool)damage.GetType().GetProperty("Active").GetValue(damage, null);
        Action apply = () => main.GetMethod("OnToggle", BF).Invoke(null, new[] { parent, (object)true });
        try
        {
            Check((bool)Get(settings, "SkinRegistrationFix") && running() && scorch(), "actual shared parent migrates older settings and runs both independent Skin Manager fixes");
            Set(settings, "SkinRegistrationFix", false); apply();
            Check(!running() && scorch(), "registration option off preserves the active scorch guard");
            main.GetMethod("OnSaveGUI", BF).Invoke(null, new[] { parent });
            Check(File.ReadAllText(Path.Combine(parentPath, "Settings.xml")).Contains("<SkinRegistrationFix>false</SkinRegistrationFix>"), "actual parent UMM save persists the independent registration preference");
            var cclEntry = modEntries.Cast<object>().First(m => (string)Get(Get(m, "Info"), "Id") == "DVCustomCarLoader");
            Toggle(cclEntry, false); Toggle(cclEntry, true);
            Check(!running() && !(bool)Get(settings, "SkinRegistrationFix") && scorch(), "CCL dependency toggles cannot overwrite an off preference or stop the scorch guard");
            Set(settings, "SkinRegistrationFix", true); apply();
            Check(running() && scorch(), "registration re-enable rejoins the parent without affecting scorch ownership");
            main.GetMethod("OnToggle", BF).Invoke(null, new[] { parent, (object)false });
            Check(!running() && !scorch(), "parent disable removes both Skin Manager guards");
            Toggle(cclEntry, false); Toggle(cclEntry, true);
            Check(!running(), "dependency changes cannot restart a disabled parent module");
            apply(); Check(running() && scorch(), "parent re-enable restores both saved choices");
            Set(cclEntry, "Version", new Version(3, 1, 8)); apply();
            Check(!running() && scorch(), "CCL below 3.1.9 disables registration correction only");
            Set(cclEntry, "Version", null); apply();
            Check(!running() && scorch(), "unknown CCL version disables registration correction only");
            foreach (var version in new[] { new Version(3, 1, 9), new Version(3, 1, 10), new Version(3, 1, 11), new Version(3, 2, 0), new Version(4, 0, 0) })
            {
                Set(cclEntry, "Version", version); apply();
                Check(running() && scorch(), "CCL version policy accepts " + version + " with the installed compatible API; metadata boundary check, not that version's binary");
            }
            Set(cclEntry, "Version", new Version(cclVersion));
            // A loaded assembly cannot change under an installed module. Start
            // a new activation so this exercises Install's cold API validation.
            Set(settings, "SkinRegistrationFix", false); apply();
            Set(cclEntry, "mAssembly", typeof(NativeSkinRegistrationChecks).Assembly); apply();
            Set(settings, "SkinRegistrationFix", true); apply();
            Check(!running() && scorch(), "new-enough CCL metadata cannot bypass incompatible callback API rejection");
            Set(cclEntry, "mAssembly", ccl); apply();
            Check(running() && scorch(), "installed CCL " + cclVersion + " recovers through actual parent API checks");
            var smEntry = modEntries.Cast<object>().First(m => (string)Get(Get(m, "Info"), "Id") == "SkinManagerMod");
            Set(smEntry, "Version", new Version(4, 4, 2)); apply();
            Check(!running(), "Skin Manager version policy remains limited to validated 4.4.1");
            Set(smEntry, "Version", new Version(4, 4, 1)); apply();
            Check(running() && scorch(), "validated Skin Manager restores both independent corrections");
        }
        finally { main.GetMethod("OnUnload", BF).Invoke(null, new[] { parent }); }
        Check(!running() && !scorch(), "actual parent unload cleans both module owners and its dependency subscription");
    }
}
