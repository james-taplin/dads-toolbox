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

// Reflection loads installed game/mod assemblies without copying or patching
// their files. Inactive TrainCar owners avoid whole-game Awake/bootstrap.
public static class NativeSkinDamageChecks
{
    const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Owner = "jimtaplin.derailvalley.toolbox.skindamage";
    const string OtherOwner = "dads-toolbox-fixture.other-owner";
    static readonly List<Object> Objects = new List<Object>();
    static readonly StringBuilder Report = new StringBuilder();
    static int checks, failures, otherCalls;
    static Assembly native, sm, harmony, toolbox;
    static MethodInfo reapply, callback;
    static object fix, otherHarmony;
    static string output;

    static void Check(bool value, string label)
    {
        checks++;
        Report.AppendLine((value ? "PASS " : "FAIL ") + label);
        if (!value) failures++;
    }

    static Type T(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null); }
    static void Set(object obj, string name, object value) { obj.GetType().GetField(name, BF).SetValue(obj, value); }
    static object Get(object obj, string name) { return obj.GetType().GetField(name, BF).GetValue(obj); }
    static object Call(object obj, string name, params object[] args) { return obj.GetType().GetMethod(name, BF).Invoke(obj, args); }
    static bool IsActive { get { return (bool)fix.GetType().GetProperty("Active").GetValue(fix, null); } }
    static bool Install(object instance, Assembly assembly) { return (bool)Call(instance, "Install", assembly, (Action<string>)(s => Report.AppendLine("INFO " + s))); }
    static void Uninstall(object instance) { Call(instance, "Uninstall"); }
    static TObj Keep<TObj>(TObj value) where TObj : Object { Objects.Add(value); return value; }

    sealed class CarFixture
    {
        public Component car, handler, paint;
        public MeshRenderer renderer;
        public Material normal, skin, scorch;
        public FixtureSkinMessages messages;
    }

    static CarFixture Create(string id, Color color)
    {
        var f = new CarFixture();
        var root = Keep(new GameObject("Inactive native TrainCar " + id));
        root.SetActive(false);
        f.messages = root.AddComponent<FixtureSkinMessages>();
        f.car = root.AddComponent(T("TrainCar"));
        var livery = Keep(ScriptableObject.CreateInstance(T("DV.ThingTypes.TrainCarLivery")));
        Set(livery, "id", id);
        var parentField = livery.GetType().GetField("parentType", BF);
        var parent = Keep(ScriptableObject.CreateInstance(parentField.FieldType));
        Set(parent, "id", id); parentField.SetValue(livery, parent);
        Set(livery, "prefab", root); Set(f.car, "carLivery", livery);

        var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leaf.name = "Painted fixture surface"; leaf.transform.SetParent(root.transform, false);
        f.renderer = leaf.GetComponent<MeshRenderer>();
        f.normal = Keep(new Material(Shader.Find("Unlit/Color")) { name = "Factory base", color = Color.gray });
        f.skin = Keep(new Material(f.normal) { name = "Selected custom skin", color = color });
        f.scorch = Keep(new Material(f.normal) { name = "Native scorch", color = new Color(.08f, .055f, .035f) });
        f.renderer.sharedMaterial = f.normal;

        var paintType = T("DV.Customization.Paint.TrainCarPaint");
        f.paint = root.AddComponent(paintType);
        var setType = paintType.GetNestedType("MaterialSet", BF);
        var refType = paintType.GetNestedType("RendererMaterialReplacement", BF);
        var set = Activator.CreateInstance(setType);
        Set(set, "originalMaterial", f.normal);
        var references = Array.CreateInstance(refType, 1);
        references.SetValue(Activator.CreateInstance(refType, new object[] { f.renderer, 0 }), 0);
        Set(set, "renderers", references);
        var sets = Array.CreateInstance(setType, 1); sets.SetValue(set, 0);
        Set(f.paint, "sets", sets);

        var theme = Theme(id, f.normal, f.skin);
        Set(f.paint, "currentTheme", theme);
        Set(f.car, "<PaintExterior>k__BackingField", f.paint);

        var handlerType = T("ExplosionModelHandler");
        f.handler = root.AddComponent(handlerType);
        Set(f.handler, "gameObjectsToDisable", new GameObject[0]);
        Set(f.handler, "gameObjectSwaps", Array.CreateInstance(handlerType.GetNestedType("GameObjectSwapData", BF), 0));
        var swapType = handlerType.GetNestedType("MaterialSwapData", BF);
        var swap = Activator.CreateInstance(swapType);
        Set(swap, "swapMaterial", f.scorch); Set(swap, "affectedGameObjects", new[] { leaf });
        var swaps = Array.CreateInstance(swapType, 1); swaps.SetValue(swap, 0);
        Set(f.handler, "materialSwaps", swaps);
        Call(f.handler, "Awake");
        return f;
    }

    static ScriptableObject Theme(string id, Material normal, Material skin)
    {
        var theme = Keep(ScriptableObject.CreateInstance(sm.GetType("SkinManagerMod.CustomPaintTheme", true)));
        theme.name = id + " custom theme";
        var substitutionType = T("DV.Customization.Paint.PaintTheme").GetNestedType("Substitution", BF);
        var substitution = Activator.CreateInstance(substitutionType);
        Set(substitution, "original", normal); Set(substitution, "substitute", skin);
        ((IList)Get(theme, "CCLSubstitutions")).Add(substitution);
        // Native OnEnable may apply this table after repair, independently of
        // Skin Manager; populate both real supported substitution structures.
        var nativeSubstitutions = Array.CreateInstance(substitutionType, 1);
        nativeSubstitutions.SetValue(substitution, 0);
        theme.GetType().BaseType.GetField("substitutions", BF).SetValue(theme, nativeSubstitutions);
        return theme;
    }

    static void Explode(CarFixture f)
    {
        ((Behaviour)f.paint).enabled = false;
        Call(f.handler, "HandleExplosionModelChange");
        Set(f.car, "isExploded", true);
    }

    static void Repair(CarFixture f)
    {
        // The native order matters: appearance is restored, then the flag is
        // cleared before prefab initialization, with root painters disabled.
        Call(f.handler, "RevertToUnexplodedModel");
        Set(f.car, "isExploded", false);
    }

    static object HarmonyMethod(MethodInfo method)
    {
        return Activator.CreateInstance(harmony.GetType("HarmonyLib.HarmonyMethod", true), new object[] { method });
    }

    static void OtherPostfix() { otherCalls++; }

    static int PrefixCount(string owner)
    {
        var info = harmony.GetType("HarmonyLib.Harmony", true).GetMethod("GetPatchInfo").Invoke(null, new object[] { reapply });
        if (info == null) return 0;
        return ((IEnumerable)Get(info, "Prefixes")).Cast<object>().Count(p => (string)Get(p, "owner") == owner);
    }

    static bool HasOwner(string owner)
    {
        var info = harmony.GetType("HarmonyLib.Harmony", true).GetMethod("GetPatchInfo").Invoke(null, new object[] { reapply });
        if (info == null) return false;
        return ((IEnumerable)info.GetType().GetProperty("Owners").GetValue(info, null)).Cast<string>().Contains(owner);
    }

    public static void Run()
    {
        // Wait until startup has a complete scene; avoid rendering in startup.
        EditorApplication.update += Start;
    }

    static void Start()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= Start;
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        output = Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_OUT") ?? Path.Combine(root, "evidence/skin-damage");
        Directory.CreateDirectory(output);
        string game = Environment.GetEnvironmentVariable("TOOLBOX_GAME_PATH") ?? @"B:\SteamLibrary\steamapps\common\Derail Valley";
        string managed = Path.Combine(game, "DerailValley_Data/Managed");
        ResolveEventHandler resolver = (sender, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string folder in new[] { managed, Path.Combine(managed, "UnityModManager"), Path.Combine(game, "Mods/SkinManagerMod"),
                Path.Combine(game, "Mods/DVLangHelper"), Path.Combine(game, "Mods/DVCustomCarLoader"), Path.Combine(game, "Mods/DVCarChanger") })
            { string path = Path.Combine(folder, name); if (File.Exists(path)) return Assembly.LoadFrom(path); }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            Report.AppendLine("Unity " + Application.unityVersion + "; platform " + Application.platform);
            native = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
            Assembly.LoadFrom(Path.Combine(managed, "DV.ThingTypes.dll"));
            harmony = Assembly.LoadFrom(Path.Combine(managed, "UnityModManager/0Harmony.dll"));
            sm = Assembly.LoadFrom(Path.Combine(game, "Mods/SkinManagerMod/SkinManagerMod.dll"));
            var settings = sm.GetType("SkinManagerMod.Main", true).GetProperty("Settings", BF);
            settings.GetSetMethod(true).Invoke(null, new[] { Activator.CreateInstance(settings.PropertyType) });
            toolbox = Assembly.LoadFrom(Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_DLL") ?? Path.Combine(root, "build/DadsToolbox.dll"));
            reapply = sm.GetType("SkinManagerMod.Patches.TrainCarPaintPatches", true).GetMethod("ReapplyThemes", BF);
            callback = sm.GetType("SkinManagerMod.Patches.TrainCarPatches", true).GetMethod("AfterInitializePaint", BF);
            fix = Activator.CreateInstance(toolbox.GetType("DadsToolbox.SkinDamageFix", true));
            Report.AppendLine("Native: " + native.Location);
            Report.AppendLine("Skin Manager: " + sm.Location + "; " + sm.GetName().Version);
            Report.AppendLine("Harmony: " + harmony.Location + "; " + harmony.GetName().Version);
            Report.AppendLine("Toolbox: " + toolbox.Location + "; " + toolbox.GetName().Version);
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(toolbox.Location))
                Report.AppendLine("Toolbox SHA256: " + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""));
            Check(!Install(fix, null) && !IsActive, "missing optional Skin Manager stays inactive");
            Check(!Install(fix, native) && !IsActive, "incompatible assembly stays inactive without patching");
            Check(PrefixCount(Owner) == 0, "baseline has no Toolbox damage prefix");

            var f = Create("ToolboxFixture_CCL", new Color(.1f, .55f, .8f));
            callback.Invoke(null, new object[] { f.car });
            Check(f.renderer.sharedMaterial == f.skin, "installed Skin Manager callback applies healthy CCL skin");
            Explode(f);
            Check(f.renderer.sharedMaterial == f.scorch, "installed native explosion handler applies scorch");
            callback.Invoke(null, new object[] { f.car });
            Check(f.renderer.sharedMaterial == f.skin, "UNPATCHED reproducer: prefab paint callback overwrites native scorch");
            var unpatched = f.renderer.sharedMaterial;
            Repair(f); callback.Invoke(null, new object[] { f.car });

            var harmonyType = harmony.GetType("HarmonyLib.Harmony", true);
            otherHarmony = Activator.CreateInstance(harmonyType, new object[] { OtherOwner });
            var patch = harmonyType.GetMethod("Patch");
            var patchArgs = new object[patch.GetParameters().Length]; patchArgs[0] = reapply;
            patchArgs[2] = HarmonyMethod(typeof(NativeSkinDamageChecks).GetMethod("OtherPostfix", BF));
            patch.Invoke(otherHarmony, patchArgs);
            Check(HasOwner(OtherOwner), "independent Harmony owner installed for removal isolation check");
            Check(Install(fix, sm) && IsActive, "compiled Toolbox fix installs actual Harmony prefix");
            Check(Install(fix, sm) && PrefixCount(Owner) == 1, "repeat install is idempotent with one prefix");
            var duplicate = Activator.CreateInstance(fix.GetType());
            Check(!Install(duplicate, sm), "second fix instance rejects duplicate owner");
            Uninstall(duplicate);
            Check(PrefixCount(Owner) == 1 && IsActive, "uninstalling rejected duplicate preserves active instance");

            Explode(f);
            int before = f.messages.BeforeCount, after = f.messages.AfterCount;
            for (int i = 0; i < 25; i++) callback.Invoke(null, new object[] { f.car });
            Check(f.renderer.sharedMaterial == f.scorch, "PATCHED: 25 actual prefab callbacks retain native scorch");
            Check((bool)Get(f.car, "isExploded") && !((Behaviour)f.paint).enabled, "damage callbacks retain exploded flag and disabled painter");
            Check(f.messages.BeforeCount == before && f.messages.AfterCount == after, "suppressed paint reload does not emit false skin-change messages");
            var patched = f.renderer.sharedMaterial;
            Check(otherCalls >= 25, "independent Harmony postfix still executes when paint body is skipped");

            var other = Create("ToolboxFixture_SecondCar", new Color(.7f, .3f, .1f));
            callback.Invoke(null, new object[] { other.car });
            Check(other.renderer.sharedMaterial == other.skin && f.renderer.sharedMaterial == f.scorch, "healthy second vehicle paints independently while first is exploded");
            Explode(other); callback.Invoke(null, new object[] { other.car });
            Check(other.renderer.sharedMaterial == other.scorch && f.renderer.sharedMaterial == f.scorch, "each vehicle independently retains its own scorch material");
            Repair(other); callback.Invoke(null, new object[] { other.car });
            Check(other.renderer.sharedMaterial == other.skin && f.renderer.sharedMaterial == f.scorch, "repairing second vehicle preserves first vehicle damage state");

            var changedSkin = Keep(new Material(f.normal) { name = "Changed skin while exploded", color = new Color(.2f, .8f, .25f) });
            var changedTheme = Theme("changed-while-exploded", f.normal, changedSkin);
            f.paint.GetType().GetProperty("CurrentTheme").SetValue(f.paint, changedTheme, null);
            Check((bool)Get(f.paint, "hasChangedWhileDisabled") && ReferenceEquals(Get(f.paint, "currentTheme"), changedTheme), "native disabled-painter setter defers a newly selected theme while exploded");
            reapply.Invoke(null, new object[] { f.car });
            Check(f.renderer.sharedMaterial == f.scorch, "direct Skin Manager reload retains scorch after theme changes while exploded");

            Repair(f);
            Check(!((Behaviour)f.paint).enabled && !(bool)Get(f.car, "isExploded"), "repair transition has healthy flag while root painter remains disabled");
            // Force base material so this callback must do real work, rather
            // than merely observing the handler's already painted restoration.
            f.renderer.sharedMaterial = f.normal;
            callback.Invoke(null, new object[] { f.car });
            Check(f.renderer.sharedMaterial == changedSkin, "PATCHED repair callback restores newly selected skin while root painter is disabled");
            var repaired = f.renderer.sharedMaterial;
            Call(f.paint, "OnEnable");
            Check(f.renderer.sharedMaterial == changedSkin && !(bool)Get(f.paint, "hasChangedWhileDisabled"), "native OnEnable applies deferred theme and clears its changed-while-disabled flag after repair");
            f.skin = changedSkin;
            ((Behaviour)f.paint).enabled = true;
            f.renderer.sharedMaterial = f.normal;
            callback.Invoke(null, new object[] { f.car });
            Check(f.renderer.sharedMaterial == f.skin, "PATCHED healthy enabled painter reload still applies skin");
            for (int i = 0; i < 4; i++)
            {
                Explode(f); callback.Invoke(null, new object[] { f.car });
                Check(f.renderer.sharedMaterial == f.scorch, "repeat damage/repair cycle " + (i + 1) + " retains scorch");
                Repair(f); f.renderer.sharedMaterial = f.normal; callback.Invoke(null, new object[] { f.car });
                Check(f.renderer.sharedMaterial == f.skin, "repeat damage/repair cycle " + (i + 1) + " restores skin");
            }

            RenderSamples(unpatched, patched, repaired);
            Uninstall(fix);
            Check(!IsActive && PrefixCount(Owner) == 0, "disable removes the Toolbox prefix");
            Check(HasOwner(OtherOwner), "disable preserves unrelated Harmony owner");
            Explode(f); callback.Invoke(null, new object[] { f.car });
            Check(f.renderer.sharedMaterial == f.skin, "uninstall restores original scorch-overwriting behavior");
            Check(Install(fix, sm) && PrefixCount(Owner) == 1, "re-enable installs exactly one prefix");
            Repair(f); Explode(f); callback.Invoke(null, new object[] { f.car });
            Check(f.renderer.sharedMaterial == f.scorch, "re-enable restores scorch protection");
        }
        catch (Exception e) { failures++; Report.AppendLine("EXCEPTION " + e); }
        finally
        {
            if (fix != null) try { Uninstall(fix); } catch (Exception e) { failures++; Report.AppendLine(e.ToString()); }
            if (otherHarmony != null) try { otherHarmony.GetType().GetMethod("UnpatchAll").Invoke(otherHarmony, new object[] { OtherOwner }); } catch (Exception e) { failures++; Report.AppendLine(e.ToString()); }
            foreach (var obj in Objects.AsEnumerable().Reverse()) if (obj) Object.DestroyImmediate(obj);
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            Report.AppendLine("Checks: " + checks + "; failures: " + failures);
            Report.AppendLine("Scope: installed native material handler, installed Skin Manager callback and compiled Toolbox Harmony prefix in isolated Unity Editor objects. Generic CCL substitution/material references; inactive TrainCar owners. No full game bootstrap, saves, streaming, physical explosion, repair UI, or locomotive-specific asset acceptance is claimed.");
            File.WriteAllText(Path.Combine(output, "native-fixture-checks.txt"), Report.ToString());
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    static void RenderSamples(Material unpatched, Material patched, Material repaired)
    {
        var cameraObject = Keep(new GameObject("Fixture evidence camera"));
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 1.5f;
        camera.transform.position = new Vector3(0, 0, -6);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .15f, .18f);
        var samples = new[] { unpatched, patched, repaired };
        for (int i = 0; i < samples.Length; i++)
        {
            var cube = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
            cube.name = new[] { "Unpatched callback", "Patched callback", "Patched repair" }[i];
            cube.transform.position = new Vector3((i - 1) * 2, 0, 0);
            cube.GetComponent<Renderer>().sharedMaterial = samples[i];
        }
        var rt = new RenderTexture(900, 360, 16); camera.targetTexture = rt;
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = rt;
        var pixels = new Texture2D(900, 360, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, 900, 360), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.Combine(output, "native-material-samples.png"), pixels.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        Object.DestroyImmediate(pixels); Object.DestroyImmediate(rt);
        Report.AppendLine("Rendered material evidence: native-material-samples.png; left unpatched repaint, centre patched scorch, right repaired skin. Cubes display the actual material references observed by assertions.");
    }
}
