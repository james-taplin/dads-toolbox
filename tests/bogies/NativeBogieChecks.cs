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
using UnityEngine.Animations;
using Object = UnityEngine.Object;

// Exact compiled Toolbox + native Unity/UMM/game components in an isolated
// Editor Play Mode. Only fixture-owned game behaviour callbacks are suppressed; no game
// boot, asset packs, actual player camera, GPU draw or smoothness claim follows.
public static class NativeBogieChecks
{
    const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Assembly toolbox, harmony, standalone;
    static string output, game, dll;
    static int passed, failed;
    static readonly StringBuilder report = new StringBuilder();
    static readonly List<Object> objects = new List<Object>();
    static readonly List<object> modules = new List<object>();
    static readonly HashSet<GameObject> nativeRoots = new HashSet<GameObject>();
    static readonly List<string> messages = new List<string>();
    static readonly List<object> patches = new List<object>();
    static readonly List<Component> testRuntimes = new List<Component>();
    static Component shutdownFailureTarget;
    static int shutdownFailuresRemaining;
    const string PendingRun = "DadsToolbox.NativeBogieChecks.Pending";
    static Type Module => toolbox.GetType("DadsToolbox.BogieVisualModule", true);
    static Type Runtime => toolbox.GetType("DadsToolbox.BogieVisuals.VisualRuntime", true);
    static Type Core(string name) => toolbox.GetType("DadsToolbox.BogieVisuals." + name, true);
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static FieldInfo Field(Type type, string name)
    { for (; type != null; type = type.BaseType) { var f = type.GetField(name, BF); if (f != null) return f; } throw new MissingFieldException(name); }
    static object Get(object target, string name) => Field(target is Type ? (Type)target : target.GetType(), name).GetValue(target is Type ? null : target);
    static void Set(object target, string name, object value) => Field(target is Type ? (Type)target : target.GetType(), name).SetValue(target is Type ? null : target, value);
    static object Property(object target, string name) => target.GetType().GetProperty(name, BF).GetValue(target, null);
    static object Call(object target, string name, params object[] args)
    {
        var type = target is Type ? (Type)target : target.GetType();
        var method = type.GetMethods(BF).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return method.Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static object New(Type type, params object[] args) => Activator.CreateInstance(type, BF, null, args, null);
    static TObj Keep<TObj>(TObj item) where TObj : Object { objects.Add(item); return item; }
    static void Check(bool value, string name) { report.AppendLine((value ? "PASS " : "FAIL ") + name); if (value) passed++; else failed++; }
    static void RunCase(string name, Action action)
    {
        try { action(); } catch (Exception ex) { Check(false, name + ": " + ex); }
        finally
        {
            // A failing scenario must not leave an active orphan that obscures
            // independent later checks. Assertions precede this safety cleanup.
            foreach (var runtime in testRuntimes) if (runtime) Call(runtime, "Shutdown");
            testRuntimes.Clear();
            foreach (var module in modules) Call(module, "Unload");
        }
    }
    static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .0001f;
    static void Drain(IEnumerator steps) { int cap = 20000; while (steps.MoveNext() && --cap > 0) { } if (cap == 0) throw new Exception("iterator did not finish"); (steps as IDisposable)?.Dispose(); }
    static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }

    public static void Run()
    {
        SessionState.SetBool(PendingRun, true);
        InstallPlayContinuation();
        if (EditorApplication.isPlaying) RunPending();
        else EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    static void InstallPlayContinuation()
    {
        EditorApplication.playModeStateChanged -= OnPlayState;
        EditorApplication.playModeStateChanged += OnPlayState;
        if (EditorApplication.isPlaying && SessionState.GetBool(PendingRun, false))
            EditorApplication.delayCall += RunPending;
    }
    static void OnPlayState(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += RunPending;
    }
    static void RunPending()
    {
        if (!EditorApplication.isPlaying || !SessionState.GetBool(PendingRun, false)) return;
        SessionState.SetBool(PendingRun, false);
        ExecuteChecks();
    }
    static void ExecuteChecks()
    {
        output = Environment.GetEnvironmentVariable("TOOLBOX_BOGIE_OUT");
        game = Environment.GetEnvironmentVariable("TOOLBOX_GAME_PATH");
        dll = Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_DLL");
        Directory.CreateDirectory(output);
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            toolbox = Assembly.LoadFrom(dll);
            harmony = Assembly.LoadFrom(Path.Combine(game, "DerailValley_Data/Managed/UnityModManager/0Harmony.dll"));
            Assembly.LoadFrom(Path.Combine(game, "DerailValley_Data/Managed/UnityModManager/UnityModManager.dll"));
            Assembly.LoadFrom(Path.Combine(game, "DerailValley_Data/Managed/Assembly-CSharp.dll"));
            report.AppendLine("Toolbox SHA256: " + Hash(dll));
            report.AppendLine("Host: isolated Unity Editor Play Mode, empty scene; Application.isPlaying=" + Application.isPlaying);
            var legacy = Environment.GetEnvironmentVariable("TOOLBOX_BOGIE_STANDALONE");
            if (!string.IsNullOrEmpty(legacy) && File.Exists(legacy)) { standalone = Assembly.LoadFrom(legacy); report.AppendLine("Standalone SHA256: " + Hash(legacy)); }
            SuppressFixtureCallbacks();
            ((IList)Get(T("UnityModManagerNet.UnityModManager"), "modEntries")).Clear();
            RunCase("module lifetime", ModuleChecks);
            RunCase("cleanup failure ownership and retry", CleanupFailureChecks);
            RunCase("native discovery and constraints", DiscoveryChecks);
            RunCase("native geometry and animation", GeometryChecks);
            RunCase("yielded mapping cancellation and retry", CancellationChecks);
            RunCase("native skin and yielded skeleton replacement", SkinChecks);
            RunCase("world, callbacks and deletion", RuntimeChecks);
            RunCase("standalone constant and pose parity", ParityChecks);
            RunCase("standalone and integrated native visual comparison", VisualComparisonChecks);
            RunCase("parent integration", ParentChecks);
        }
        catch (Exception ex) { Check(false, "harness setup: " + ex); }
        finally
        {
            foreach (var module in modules) try { Call(module, "Unload"); } catch (Exception ex) { Check(false, "module cleanup: " + ex.Message); }
            foreach (var obj in objects.AsEnumerable().Reverse()) if (obj) Object.DestroyImmediate(obj);
            foreach (var patch in patches) try { Call(patch, "UnpatchAll", "dads-toolbox-fixture.bogies"); } catch { }
            report.AppendLine("Scope: native Unity Play Mode and exact DLL; actual enable/disable/destroy callbacks plus explicit world/iterator inputs, seeded renderer/layout data, suppressed fixture-owned game startup callbacks. No game packs, player inputs, real camera ordering, native constraint solver scheduling, GPU draw, physics-track following or live smoothing/performance acceptance.");
            report.AppendLine("Checks: " + passed + "; failures: " + failed);
            report.AppendLine("RESULT: " + passed + " passed, " + failed + " failed.");
            File.WriteAllText(Path.Combine(output, "native-fixture-checks.txt"), report.ToString());
            File.WriteAllText(Path.Combine(output, "result.json"), "{\"passed\":" + passed + ",\"failed\":" + failed + "}");
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }
    }
    static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (name == "DadsToolbox") return Assembly.LoadFrom(dll);
        foreach (string root in new[] { Path.Combine(game, "DerailValley_Data/Managed"), Path.Combine(game, "DerailValley_Data/Managed/UnityModManager") })
        { string path = Path.Combine(root, name + ".dll"); if (File.Exists(path)) return Assembly.LoadFrom(path); }
        return null;
    }
    static void SuppressFixtureCallbacks()
    {
        var patch = New(harmony.GetType("HarmonyLib.Harmony", true), "dads-toolbox-fixture.bogies"); patches.Add(patch);
        var hm = New(harmony.GetType("HarmonyLib.HarmonyMethod", true), typeof(NativeBogieChecks).GetMethod("FixtureBehaviour", BF));
        var method = patch.GetType().GetMethod("Patch");
        foreach (string name in new[] { "TrainCar", "Bogie", "CarSpawner" })
            foreach (string callback in new[] { "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy" })
            {
                var target = T(name).GetMethod(callback, BF);
                if (target == null || target.IsAbstract || target.GetParameters().Length != 0) continue;
                var args = new object[method.GetParameters().Length]; args[0] = target; args[1] = hm; method.Invoke(patch, args);
            }
        var failure = New(harmony.GetType("HarmonyLib.HarmonyMethod", true), typeof(NativeBogieChecks).GetMethod("MaybeThrowShutdown", BF));
        var shutdown = new object[method.GetParameters().Length]; shutdown[0] = Runtime.GetMethod("Shutdown", BF); shutdown[1] = failure; method.Invoke(patch, shutdown);
    }
    static void MaybeThrowShutdown(Component __instance)
    {
        if (ReferenceEquals(__instance, shutdownFailureTarget) && shutdownFailuresRemaining-- > 0)
            throw new InvalidOperationException("controlled one-time fixture shutdown failure");
    }
    static bool FixtureBehaviour(Component __instance)
    { for (var t = __instance.transform; t; t = t.parent) if (nativeRoots.Contains(t.gameObject)) return false; return true; }
    static object Entry(string id, bool loaded, bool active)
    {
        var entry = FormatterServices.GetUninitializedObject(T("UnityModManagerNet.UnityModManager+ModEntry"));
        var info = New(T("UnityModManagerNet.UnityModManager+ModInfo")); Set(info, "Id", id); Set(info, "Version", "0.4.2"); Set(info, "AssemblyName", id + ".dll");
        Set(entry, "Info", info); Set(entry, "Version", new Version(0, 4, 2));
        Set(entry, "Logger", New(T("UnityModManagerNet.UnityModManager+ModEntry+ModLogger"), "bogies-fixture"));
        Set(entry, "mAssembly", loaded ? toolbox : null); Set(entry, "mStarted", loaded); Set(entry, "mActive", active);
        return entry;
    }
    static object MakeModule()
    { var module = New(Module, New(T("UnityModManagerNet.UnityModManager+ModEntry+ModLogger"), "bogies-fixture"), output); modules.Add(module); return module; }
    static bool Active(object module) => (bool)Property(module, "Active");
    static Component RuntimeOf(object module) => Get(module, "runtime") as Component;
    static int CameraCallbacks() => Camera.onPreCull == null ? 0 : Camera.onPreCull.GetInvocationList().Count(d => d.Method.DeclaringType == Runtime);

    static void ModuleChecks()
    {
        var module = MakeModule(); Call(module, "Configure", false, true);
        Check(!Active(module) && !(bool)Property(module, "DesiredEnabled"), "feature preference off creates no runtime");
        Call(module, "Configure", true, false); Check(!Active(module) && (bool)Property(module, "DesiredEnabled"), "parent disabled retains desired choice without runtime");
        Call(module, "Configure", true, true); var first = RuntimeOf(module);
        Check(Active(module) && first && !((IList)Get(T("UnityModManagerNet.UnityModManager"), "modEntries")).Cast<object>().Any(), "native bogie runtime activates with no CCL or other optional mods");
        Call(module, "Configure", true, true);
        Check(RuntimeOf(module) == first, "repeat configuration/tick does not duplicate runtime");
        var duplicate = MakeModule(); Call(duplicate, "Configure", true, true);
        Check(!Active(duplicate) && Active(module), "duplicate wrapper lifetime cannot acquire or disrupt runtime ownership");
        Check((bool)Call(duplicate, "Unload") && Active(module), "rejected duplicate cleanup leaves valid runtime active");
        Check((bool)Call(module, "Stop") && !Active(module) && (!first || !((Behaviour)first).enabled), "stop synchronously disables old runtime before deferred root destruction");
        Call(module, "Configure", true, true); var second = RuntimeOf(module);
        Check(Active(module) && second != first, "reactivation creates a fresh runtime before deferred old-object destruction");
        Check((bool)Call(module, "Unload") && (bool)Call(module, "Unload"), "final unload is idempotent");
        Call(module, "Configure", true, true); Check(!Active(module), "unloaded wrapper cannot reactivate");
        var fresh = MakeModule(); Call(fresh, "Configure", true, true); Check(Active(fresh), "fresh wrapper recovers after old final unload"); Call(fresh, "Unload");
    }
    static void CleanupFailureChecks()
    {
        var module = MakeModule(); Call(module, "Configure", true, true); var runtime = RuntimeOf(module);
        shutdownFailureTarget = runtime; shutdownFailuresRemaining = 1;
        try
        {
            Check(!(bool)Call(module, "Stop") && ReferenceEquals(RuntimeOf(module), runtime) && ReferenceEquals(Get(Module, "owner"), module), "failed synchronous shutdown retains runtime and ownership for cleanup retry");
            var duplicate = MakeModule(); Call(duplicate, "Configure", true, true);
            Check(!Active(duplicate) && ReferenceEquals(Get(Module, "owner"), module), "cleanup failure cannot be bypassed by another module lifetime");
            shutdownFailureTarget = null;
            Check((bool)Call(module, "Stop") && RuntimeOf(module) == null && Get(Module, "owner") == null, "retry after one-time shutdown failure releases runtime and ownership");
            var fresh = MakeModule(); Call(fresh, "Configure", true, true); Check(Active(fresh), "successful cleanup retry permits a fresh module activation"); Call(fresh, "Unload");
        }
        finally { shutdownFailureTarget = null; shutdownFailuresRemaining = 0; }
    }
    sealed class Fixture
    {
        internal GameObject root, owner;
        internal Component car, bogie;
        internal Rigidbody body, bogieBody;
        internal Mesh mesh;
        internal Renderer renderer;
        internal object layout, binding;
        internal Fixture(string name)
        {
            root = Keep(new GameObject(name)); root.SetActive(false); nativeRoots.Add(root);
            car = root.AddComponent(T("TrainCar")); ((Behaviour)car).enabled = false;
            body = root.GetComponent<Rigidbody>(); if (!body) body = root.AddComponent<Rigidbody>(); Set(car, "rb", body);
            owner = new GameObject("Native bogie"); owner.transform.SetParent(root.transform, false);
            bogie = owner.AddComponent(T("Bogie")); ((Behaviour)bogie).enabled = false;
            bogieBody = owner.GetComponent<Rigidbody>(); if (!bogieBody) bogieBody = owner.AddComponent<Rigidbody>(); Set(bogie, "rb", bogieBody);
            if (!body || !bogieBody) throw new InvalidOperationException("Fixture requires actual native Rigidbody components.");
            var visible = new GameObject("Native wheel and rod"); visible.transform.SetParent(owner.transform, false);
            mesh = Keep(new Mesh()); mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 }; mesh.RecalculateBounds();
            visible.AddComponent<MeshFilter>().sharedMesh = mesh; renderer = visible.AddComponent<MeshRenderer>();
            root.SetActive(true);
            layout = New(Core("VisualLayout+Layout")); Set(layout, "car", car);
            binding = New(Core("VisualBinding"), owner.transform, owner.transform, false, bogie);
            Set(binding, "ReferenceFrame", root.transform); Set(binding, "Available", true);
            ((IList)Get(layout, "bindings")).Add(binding); ((IList)Get(layout, "renderers")).Add(renderer);
        }
        internal object Proxy() { var proxy = New(Core("VisualSmoother"), layout); Drain((IEnumerator)Call(proxy, "BuildSteps")); return proxy; }
        internal void Sample(float time = 1) => Call(Get(binding, "History"), "Sample", owner.transform.localPosition, owner.transform.localRotation, time, .02f);
    }
    static void DiscoveryChecks()
    {
        var f = new Fixture("Vanilla native discovery");
        var constrained = new GameObject("Constrained visual owner"); constrained.transform.SetParent(f.root.transform, false);
        var constraint = constrained.AddComponent<PositionConstraint>(); constraint.AddSource(new ConstraintSource { sourceTransform = f.owner.transform, weight = 1 }); constraint.weight = 1; constraint.constraintActive = true;
        var layout = New(Core("VisualLayout+Layout")); Set(layout, "car", f.car); Drain((IEnumerator)Call(Core("VisualLayout"), "Discover", layout));
        var bindings = ((IList)Get(layout, "bindings")).Cast<object>().ToArray();
        Check(bindings.Any(b => (Transform)Get(b, "SourceRoot") == f.owner.transform), "actual native Bogie component yields a visual owner without CCL metadata");
        Check(bindings.Any(b => (Transform)Get(b, "SourceRoot") == constrained.transform && (bool)Get(b, "PresentationSample")), "native position constraint with one bogie association uses final-pose sampling");
        var other = new GameObject("Second native bogie"); other.SetActive(false); other.transform.SetParent(f.root.transform, false);
        var bogie2 = other.AddComponent(T("Bogie")); ((Behaviour)bogie2).enabled = false; other.SetActive(true);
        constraint.AddSource(new ConstraintSource { sourceTransform = other.transform, weight = 1 });
        var ambiguous = New(Core("VisualLayout+Layout")); Set(ambiguous, "car", f.car); Drain((IEnumerator)Call(Core("VisualLayout"), "Discover", ambiguous));
        Check(((IList)Get(ambiguous, "nativeOnly")).Contains(constrained.transform), "multi-bogie constraint ambiguity stays native");
        Check(constraint.sourceCount == 2 && constraint.GetSource(0).sourceTransform == f.owner.transform && constraint.weight == 1, "discovery preserves native constraint sources and weights");
    }
    static void GeometryChecks()
    {
        var f = new Fixture("Native proxy animation"); var proxy = f.Proxy();
        try
        {
            Check(!(bool)Get(f.binding, "History").GetType().GetProperty("Ready").GetValue(Get(f.binding, "History"), null) && !f.renderer.forceRenderingOff, "building proxies leaves original meshes visible before pose history is ready");
            f.Sample(); var originalPosition = f.owner.transform.position; var originalBodyPose = f.bogieBody.position;
            Call(proxy, "Present", 1.02f, false, true);
            Check(f.renderer.forceRenderingOff && ((IEnumerable)Property(proxy, "ProxyRenderers")).Cast<Renderer>().All(r => r.enabled), "ready presentation hides native renderer and enables its actual Unity mesh copy");
            var wheel = f.renderer.transform; wheel.localRotation = Quaternion.Euler(35, 7, 1); wheel.localPosition = new Vector3(.02f, .03f, .04f);
            Call(proxy, "Present", 1.03f, false, true); var copy = (Transform)Call(proxy, "CopyOf", wheel);
            Check(Quaternion.Angle(copy.localRotation, wheel.localRotation) < .001f && Near(copy.localPosition, wheel.localPosition), "wheel/rod local animation is copied exactly without filtering");
            Check(Near(f.owner.transform.position, originalPosition) && Near(f.bogieBody.position, originalBodyPose), "proxy presentation leaves native visual root and Rigidbody position untouched");
            Set(f.binding, "PresentationSample", true); Set(f.binding, "FilterPosition", true); Set(f.binding, "FilterRotation", false);
            f.owner.transform.localPosition = new Vector3(.01f, 0, 0); f.owner.transform.localRotation = Quaternion.Euler(0, 8, 0);
            Call(proxy, "Present", 1.05f, false, true); var ownerCopy = (Transform)Call(proxy, "CopyOf", f.owner.transform);
            Check(Quaternion.Angle(ownerCopy.rotation, f.owner.transform.rotation) < .001f, "position-only final constraint output retains exact native rotation");
            Check((int)Property(Get(f.binding, "History"), "Samples") >= 2, "final-constrained owner samples native rendered pose without a fixed-step callback");
            Set(f.binding, "Radius", 9f); object[] limit = { f.owner.transform.position + Vector3.right, Quaternion.Euler(0, 90, 0) };
            Call(f.binding, "LimitPresentation", limit);
            Check(Vector3.Distance((Vector3)limit[0], f.owner.transform.position) <= .0201f && Quaternion.Angle((Quaternion)limit[1], f.owner.transform.rotation) < .21f, "long assembly presentation respects translation and remaining angular envelope");
        }
        finally { Call(proxy, "Dispose"); }
        Check(!f.renderer.forceRenderingOff && !((GameObject)Property(proxy, "Container")).activeSelf, "disposing presented proxy restores original renderer and synchronously hides container");
    }
    static Component NewRuntime()
    {
        var go = Keep(new GameObject("Toolbox bogie runtime fixture")); go.SetActive(false); var runtime = go.AddComponent(Runtime);
        var cameraRoot = Keep(new GameObject("Nearby game camera")); var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = true;
        Set(runtime, "views", new[] { camera }); Set(runtime, "viewCount", 1); testRuntimes.Add(runtime);
        go.SetActive(true); Call(runtime, "OnDisable"); // Balance any Editor callback before explicit lifecycle assertions.
        return runtime;
    }
    sealed class Throwing : IEnumerator, IDisposable
    { internal readonly Exception error; internal int moves, disposals; internal Throwing(Exception e) { error = e; } public object Current => null; public bool MoveNext() { moves++; throw error; } public void Reset() { throw new NotSupportedException(); } public void Dispose() { disposals++; } }
    static void Inject(Component runtime, Fixture f, object proxy, IEnumerator steps)
    { Set(runtime, "buildingCar", f.car); Set(runtime, "buildingLayout", f.layout); Set(runtime, "buildingProxy", proxy); Set(runtime, "build", steps); Set(runtime, "proxyStage", true); }
    static void CancellationChecks()
    {
        var runtime = NewRuntime(); var f = new Fixture("Partial native cancellation"); var proxy = f.Proxy(); f.Sample(); Call(proxy, "Present", 1.02f, false, true);
        var replacement = new Throwing((Exception)New(Core("VisualLayoutChangedException"), "controlled source replacement")); Inject(runtime, f, proxy, replacement); float before = Time.unscaledTime;
        Call(runtime, "AdvanceBuild"); var retries = (IDictionary)Get(runtime, "retryAfter");
        Check(replacement.disposals == 1 && !f.renderer.forceRenderingOff && Get(runtime, "build") == null, "source replacement disposes iterator and real proxy then restores native rendering");
        Check((float)retries[f.car] >= before + .249f && (float)retries[f.car] <= Time.unscaledTime + .251f, "expected replacement receives quarter-second retry instead of fifteen seconds");
        proxy = f.Proxy(); f.Sample(); Call(proxy, "Present", 1.02f, false, true);
        var error = new Throwing(new InvalidOperationException("controlled genuine mapping error")); Inject(runtime, f, proxy, error); before = Time.unscaledTime; Call(runtime, "AdvanceBuild");
        Check(error.disposals == 1 && !f.renderer.forceRenderingOff && (float)retries[f.car] >= before + 14.999f, "genuine build failure retains fifteen-second cooldown and restores native renderers");
        // Destroy a real source while the production build iterator is yielded.
        var rapid = new Fixture("Rapid source replacements");
        for (int i = 0; i < 4; i++)
        {
            var meshObject = new GameObject("Replacement mesh " + i); meshObject.transform.SetParent(rapid.owner.transform, false); meshObject.AddComponent<MeshFilter>().sharedMesh = rapid.mesh; var source = meshObject.AddComponent<MeshRenderer>();
            ((IList)Get(rapid.layout, "renderers")).Clear(); ((IList)Get(rapid.layout, "renderers")).Add(source);
            var partial = New(Core("VisualSmoother"), rapid.layout); var steps = (IEnumerator)Call(partial, "BuildSteps"); steps.MoveNext(); Object.DestroyImmediate(source);
            Inject(runtime, rapid, partial, steps); Call(runtime, "AdvanceBuild");
            Check(Get(runtime, "build") == null && !((GameObject)Property(partial, "Container")).activeSelf, "yielded source replacement " + i + " discards partial proxy immediately");
        }
        Call(runtime, "Shutdown");
    }
    static int EventCount(Component component, string eventName, Component runtime)
    {
        var field = component.GetType().GetFields(BF).FirstOrDefault(f => typeof(Delegate).IsAssignableFrom(f.FieldType) && f.Name.IndexOf(eventName, StringComparison.OrdinalIgnoreCase) >= 0);
        if (field == null) throw new MissingFieldException(eventName);
        var handlers = field.GetValue(component) as Delegate; return handlers == null ? 0 : handlers.GetInvocationList().Count(d => ReferenceEquals(d.Target, runtime));
    }
    static SkinnedMeshRenderer AddSkin(Fixture f, out Transform bone)
    {
        var first = new GameObject("Animated native bone"); first.transform.SetParent(f.owner.transform, false); bone = first.transform;
        var second = new GameObject("Second native bone"); second.transform.SetParent(first.transform, false);
        var node = new GameObject("Native skinned wheel gear"); node.transform.SetParent(f.owner.transform, false);
        var mesh = Keep(new Mesh()); mesh.vertices = f.mesh.vertices; mesh.triangles = f.mesh.triangles;
        mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
        mesh.boneWeights = Enumerable.Range(0, 3).Select(_ => new BoneWeight { boneIndex0 = 0, weight0 = 1 }).ToArray();
        mesh.AddBlendShapeFrame("Native gear flex", 100, new Vector3[3], new Vector3[3], new Vector3[3]); mesh.RecalculateBounds();
        var skin = node.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.bones = new[] { first.transform, second.transform }; skin.rootBone = first.transform;
        ((IList)Get(f.layout, "renderers")).Add(skin); return skin;
    }
    static void SkinChecks()
    {
        var f = new Fixture("Native skinned animation"); Transform bone; var skin = AddSkin(f, out bone); var proxy = f.Proxy();
        try
        {
            f.Sample(); bone.localRotation = Quaternion.Euler(0, 0, 33); skin.SetBlendShapeWeight(0, 42);
            Call(proxy, "Present", 1.02f, false, true);
            var copy = ((Transform)Call(proxy, "CopyOf", skin.transform)).GetComponent<SkinnedMeshRenderer>();
            Check(copy.sharedMesh == skin.sharedMesh && copy.bones.Length == 2 && copy.bones[0] != bone, "native skinned proxy shares GPU geometry and remaps bones to copied transforms");
            Check(Quaternion.Angle(copy.bones[0].localRotation, bone.localRotation) < .001f && Mathf.Abs(copy.GetBlendShapeWeight(0) - 42) < .001f, "native bone animation and blend-shape weight survive presentation");
        }
        finally { Call(proxy, "Dispose"); }
        Check(!skin.forceRenderingOff, "disposing skinned proxy restores original skin renderer");
        var changed = new Fixture("Yielded native skin replacement"); Transform originalBone; var staleSkin = AddSkin(changed, out originalBone);
        var partial = New(Core("VisualSmoother"), changed.layout); var steps = (IEnumerator)Call(partial, "BuildSteps");
        steps.MoveNext(); steps.MoveNext(); steps.MoveNext(); // Yielded while constructing the first skin bone.
        var replacement = new GameObject("Replacement native bone"); replacement.transform.SetParent(changed.owner.transform, false);
        staleSkin.bones = new[] { replacement.transform, originalBone }; var runtime = NewRuntime(); Inject(runtime, changed, partial, steps);
        for (int i = 0; i < 4 && Get(runtime, "build") != null; i++) Call(runtime, "AdvanceBuild");
        Check(Get(runtime, "build") == null && !changed.renderer.forceRenderingOff && !staleSkin.forceRenderingOff && !((GameObject)Property(partial, "Container")).activeSelf, "skeleton changed between yielded bone steps cancels copies with both native renderers available");
        Check((float)((IDictionary)Get(runtime, "retryAfter"))[changed.car] <= Time.unscaledTime + .251f, "yielded skeleton replacement uses short layout retry");
        Call(runtime, "Shutdown");
    }
    static void RuntimeChecks()
    {
        var runtime = NewRuntime(); int before = CameraCallbacks(); Call(runtime, "OnEnable");
        Check(CameraCallbacks() == before + 1, "runtime subscribes actual native camera callback");
        var spawnerRoot = Keep(new GameObject("Native spawner fixture")); spawnerRoot.SetActive(false); nativeRoots.Add(spawnerRoot); var spawner = spawnerRoot.AddComponent(T("CarSpawner")); ((Behaviour)spawner).enabled = false;
        Call(runtime, "AttachSpawner", spawner);
        Check(EventCount(spawner, "CarSpawned", runtime) == 1 && EventCount(spawner, "CarAboutToBeDeleted", runtime) == 1, "runtime subscribes both actual native car-spawner events once");
        Call(runtime, "AttachSpawner", spawner); Check(EventCount(spawner, "CarSpawned", runtime) == 1, "reattaching same world does not duplicate callbacks");
        var f = new Fixture("World cleanup car"); var proxy = f.Proxy(); f.Sample(); Call(proxy, "Present", 1.02f, false, true); var pending = new Throwing(new Exception("must not advance")); Inject(runtime, f, proxy, pending);
        Call(runtime, "OnDelete", f.car);
        Check(pending.moves == 0 && pending.disposals == 1 && !f.renderer.forceRenderingOff, "native despawn callback cancels outstanding work and restores presented renderer");
        ((Behaviour)runtime).enabled = false;
        Check(CameraCallbacks() == before && EventCount(spawner, "CarSpawned", runtime) == 0 && EventCount(spawner, "CarAboutToBeDeleted", runtime) == 0, "OnDisable removes native camera and spawner callbacks synchronously");
        Call(runtime, "OnDestroy"); Check(CameraCallbacks() == before, "OnDestroy cleanup is safe after OnDisable");
        ((Behaviour)runtime).enabled = true; Call(runtime, "AttachSpawner", spawner); Call(runtime, "ResetWorld");
        Check(Get(runtime, "spawner") == null && EventCount(spawner, "CarSpawned", runtime) == 0, "world exit releases old registry subscriptions");
        Call(runtime, "AttachSpawner", spawner); Check(EventCount(spawner, "CarSpawned", runtime) == 1, "world reentry can attach registry after previous cleanup");
        Call(runtime, "Shutdown"); Check(CameraCallbacks() == before && EventCount(spawner, "CarSpawned", runtime) == 0, "explicit shutdown independently guarantees callback cleanup");
        Object.DestroyImmediate(runtime.gameObject); Check(CameraCallbacks() == before && EventCount(spawner, "CarSpawned", runtime) == 0, "actual native OnDestroy leaves no camera or registry callback after shutdown");
    }
    static void ParityChecks()
    {
        if (standalone == null) { Check(false, "retained standalone DLL required for independent constant and filter parity"); return; }
        var oldHistory = standalone.GetType("DVLocoBogieVisuals.PoseHistory", true); var now = New(Core("PoseHistory")); var old = New(oldHistory);
        bool constants = true;
        foreach (var types in new[] { new[] { oldHistory, Core("PoseHistory") }, new[] { standalone.GetType("DVLocoBogieVisuals.VisualRuntime", true), Runtime } })
            foreach (var field in types[0].GetFields(BF).Where(field => field.IsLiteral))
                constants &= Equals(field.GetRawConstantValue(), Field(types[1], field.Name).GetRawConstantValue());
        Check(constants, "all standalone filter, displacement, distance and maintenance budget constants are unchanged");
        bool same = true;
        for (int i = 0; i < 12; i++)
        {
            var position = new Vector3(i == 9 ? 10 : i * .005f, 0, 0); var rotation = Quaternion.Euler(0, i * .3f, 0);
            Call(now, "Sample", position, rotation, 1 + i * .02f, .02f); Call(old, "Sample", position, rotation, 1 + i * .02f, .02f);
            object[] a = { 1.01f + i * .02f, false, Vector3.zero, Quaternion.identity }, b = { 1.01f + i * .02f, false, Vector3.zero, Quaternion.identity };
            Call(now, "Evaluate", a); Call(old, "Evaluate", b); same &= Near((Vector3)a[2], (Vector3)b[2]) && Quaternion.Angle((Quaternion)a[3], (Quaternion)b[3]) < .001f;
        }
        Check(same && (int)Property(now, "Resets") == (int)Property(old, "Resets"), "exact compiled standalone and Toolbox filters agree across motion and teleport reset");
    }
    static void ParentChecks()
    {
        var path = Path.Combine(output, "parent-entry"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "Settings.xml"), "<ToolboxSettings><HudGuard>false</HudGuard><CclControlFix>false</CclControlFix><LocoSoundPitch>false</LocoSoundPitch><SmokeFix>false</SmokeFix><SkinDamageFix>false</SkinDamageFix><SkinRegistrationFix>false</SkinRegistrationFix><DoubleTrackSignClearance>false</DoubleTrackSignClearance><BogieVisualSmoothing>true</BogieVisualSmoothing><ChuffFix>false</ChuffFix><SoundSettingsMigrationComplete>true</SoundSettingsMigrationComplete></ToolboxSettings>");
        var entry = Entry("DadsToolbox", true, true); Set(entry, "Path", path + Path.DirectorySeparatorChar); var main = toolbox.GetType("DadsToolbox.Main", true);
        try
        {
            Check((bool)Call(main, "Load", entry), "native UMM parent loads with bogies as only selected feature"); var module = Get(main, "bogies");
            Check(Active(module), "parent routes selected bogie feature without CCL installed");
            var toggle = (Delegate)Get(entry, "OnToggle"); Check((bool)toggle.DynamicInvoke(entry, false) && !Active(module), "parent toggle off stops bogie runtime");
            Check((bool)toggle.DynamicInvoke(entry, true) && Active(module), "parent toggle on reactivates bogie runtime");
            var settings = Get(main, "settings"); Set(settings, "BogieVisualSmoothing", false); Call(main, "ApplySettings"); Check(!Active(module), "bogie option applies independently inside live parent");
            Set(settings, "BogieVisualSmoothing", true); Call(main, "ApplySettings"); Check(Active(module), "bogie option can be restored without reloading parent");
        }
        finally { ((Delegate)Get(entry, "OnUnload")).DynamicInvoke(entry); }
    }
    sealed class ComparisonResult
    {
        internal readonly List<Vector3> positions = new List<Vector3>();
        internal readonly List<Quaternion> rotations = new List<Quaternion>();
        internal readonly List<Vector3> limits = new List<Vector3>();
        internal readonly List<Quaternion> limitRotations = new List<Quaternion>();
        internal int yields, nodes, meshes;
        internal double buildMs, presentMs;
        internal bool restored, replaced, nativeUntouched = true;
    }
    static Type VisualType(Assembly assembly, string name) => assembly.GetType((assembly == toolbox ? "DadsToolbox.BogieVisuals." : "DVLocoBogieVisuals.") + name, true);
    static ComparisonResult CompareVisual(Assembly assembly, Fixture f, Transform[] owners, Component[] bogies, bool[] constrained, Renderer[] renderers)
    {
        var result = new ComparisonResult();
        foreach (var owner in owners) { owner.localPosition = Vector3.zero; owner.localRotation = Quaternion.identity; }
        f.renderer.transform.localPosition = Vector3.zero; f.renderer.transform.localRotation = Quaternion.identity;
        var layout = New(VisualType(assembly, "VisualLayout+Layout")); Set(layout, "car", f.car);
        var bindings = new object[owners.Length];
        for (int i = 0; i < owners.Length; i++)
        {
            bindings[i] = New(VisualType(assembly, "VisualBinding"), owners[i], owners[i], false, bogies[i]);
            Set(bindings[i], "ReferenceFrame", f.root.transform); Set(bindings[i], "Available", true); Set(bindings[i], "PresentationSample", constrained[i]);
            ((IList)Get(layout, "bindings")).Add(bindings[i]);
        }
        foreach (var renderer in renderers) ((IList)Get(layout, "renderers")).Add(renderer);
        var watch = System.Diagnostics.Stopwatch.StartNew(); var proxy = New(VisualType(assembly, "VisualSmoother"), layout);
        var build = (IEnumerator)Call(proxy, "BuildSteps");
        while (build.MoveNext()) result.yields++; (build as IDisposable)?.Dispose();
        watch.Stop(); result.buildMs = watch.Elapsed.TotalMilliseconds;
        result.nodes = (int)Property(proxy, "NodeCount"); result.meshes = (int)Property(proxy, "MeshCount");
        var present = proxy.GetType().GetMethod("Present", BF); object[] presentArgs = { 0f, false, true };
        try
        {
            for (int frame = -20; frame < 120; frame++)
            {
                int tick = frame + 20; float time = 1f + tick * .02f;
                for (int i = 0; i < owners.Length; i++)
                {
                    owners[i].localPosition = new Vector3(.016f * Mathf.Sin(tick * .13f + i), .002f * Mathf.Cos(tick * .17f), i * .3f);
                    owners[i].localRotation = Quaternion.Euler(0, 2f * Mathf.Sin(tick * .09f + i), .1f * Mathf.Cos(tick * .07f));
                    if (!constrained[i])
                        Call(Get(bindings[i], "History"), "Sample", f.root.transform.InverseTransformPoint(owners[i].position), Quaternion.Inverse(f.root.transform.rotation) * owners[i].rotation, time, .02f);
                }
                f.renderer.transform.localRotation = Quaternion.Euler(tick * 13f, 0, 0);
                f.renderer.transform.localPosition = new Vector3(0, .003f * Mathf.Sin(tick * .2f), 0);
                var nativePositions = owners.Select(owner => owner.position).ToArray(); var nativeRotations = owners.Select(owner => owner.rotation).ToArray();
                presentArgs[0] = time + .01f;
                watch.Restart(); present.Invoke(proxy, presentArgs); watch.Stop();
                if (frame < 0) continue;
                result.presentMs += watch.Elapsed.TotalMilliseconds;
                for (int i = 0; i < owners.Length; i++)
                {
                    var copy = (Transform)Call(proxy, "CopyOf", owners[i]); result.positions.Add(copy.position); result.rotations.Add(copy.rotation);
                    result.nativeUntouched &= Near(owners[i].position, nativePositions[i]) && Quaternion.Angle(owners[i].rotation, nativeRotations[i]) < .001f;
                }
                var wheel = (Transform)Call(proxy, "CopyOf", f.renderer.transform); result.positions.Add(wheel.localPosition); result.rotations.Add(wheel.localRotation);
            }
            foreach (var binding in bindings)
            {
                var owner = (Transform)Get(binding, "SourceRoot"); object[] limit = { owner.position + Vector3.right * .2f, owner.rotation * Quaternion.Euler(0, 25, 0) };
                Call(binding, "LimitPresentation", limit); result.limits.Add((Vector3)limit[0]); result.limitRotations.Add((Quaternion)limit[1]);
            }
            // A genuine source component replacement, after successful proxy
            // presentation, must be classified the same by both compiled cores.
            Object.DestroyImmediate(f.renderer.GetComponent<MeshFilter>());
            try { Call(proxy, "Present", 5f, false, true); }
            catch (Exception ex) { result.replaced = ex.GetType().Name == "VisualLayoutChangedException"; }
        }
        finally
        {
            Call(proxy, "Dispose"); result.restored = renderers.All(renderer => renderer && !renderer.forceRenderingOff);
            var filter = f.renderer.GetComponent<MeshFilter>(); if (!filter) filter = f.renderer.gameObject.AddComponent<MeshFilter>(); filter.sharedMesh = f.mesh;
        }
        return result;
    }
    static void VisualComparisonChecks()
    {
        if (standalone == null) { Check(false, "standalone baseline required for full visual comparison"); return; }
        for (int pattern = 0; pattern < 3; pattern++)
        {
            string label = new[] { "single vanilla-like bogie", "separate constrained CCL-like visual", "long articulated multiple-owner visual" }[pattern];
            var f = new Fixture(label); var owners = new List<Transform> { f.owner.transform }; var bogies = new List<Component> { f.bogie }; var constrained = new List<bool> { false }; var renderers = new List<Renderer> { f.renderer };
            if (pattern == 2)
            {
                f.mesh.bounds = new Bounds(Vector3.zero, new Vector3(18, 2, 2));
                var second = new GameObject("Second articulated native bogie"); second.SetActive(false); second.transform.SetParent(f.root.transform, false);
                var body = second.AddComponent<Rigidbody>(); var bogie = second.AddComponent(T("Bogie")); ((Behaviour)bogie).enabled = false; Set(bogie, "rb", body); second.SetActive(true);
                owners.Add(second.transform); bogies.Add(bogie); constrained.Add(false);
                var meshObject = new GameObject("Long secondary native mesh"); meshObject.transform.SetParent(second.transform, false); meshObject.AddComponent<MeshFilter>().sharedMesh = f.mesh; renderers.Add(meshObject.AddComponent<MeshRenderer>());
            }
            if (pattern > 0)
            {
                var visual = new GameObject("Native final constraint output"); visual.transform.SetParent(pattern == 2 ? f.owner.transform : f.root.transform, false);
                var constraint = visual.AddComponent<ParentConstraint>(); constraint.AddSource(new ConstraintSource { sourceTransform = owners[owners.Count - 1], weight = 1 }); constraint.weight = 1; constraint.constraintActive = true;
                owners.Add(visual.transform); bogies.Add(bogies[bogies.Count - 1]); constrained.Add(true);
                var meshObject = new GameObject("Native constrained gear mesh"); meshObject.transform.SetParent(visual.transform, false); meshObject.AddComponent<MeshFilter>().sharedMesh = f.mesh; renderers.Add(meshObject.AddComponent<MeshRenderer>());
            }
            var baseline = CompareVisual(standalone, f, owners.ToArray(), bogies.ToArray(), constrained.ToArray(), renderers.ToArray());
            var integrated = CompareVisual(toolbox, f, owners.ToArray(), bogies.ToArray(), constrained.ToArray(), renderers.ToArray());
            bool poseParity = baseline.positions.Count == integrated.positions.Count && baseline.positions.Zip(integrated.positions, Near).All(same => same) && baseline.rotations.Zip(integrated.rotations, (a, b) => Quaternion.Angle(a, b) < .001f).All(same => same);
            Check(poseParity && baseline.nativeUntouched && integrated.nativeUntouched && baseline.yields == integrated.yields && baseline.nodes == integrated.nodes && baseline.meshes == integrated.meshes, label + ": standalone and integrated proxy motion, wheel animation, native ownership and build work agree");
            Check(baseline.replaced && integrated.replaced && baseline.restored && integrated.restored, label + ": native component replacement and original renderer restoration agree");
            Check(baseline.limits.Zip(integrated.limits, Near).All(same => same) && baseline.limitRotations.Zip(integrated.limitRotations, (a, b) => Quaternion.Angle(a, b) < .001f).All(same => same), label + ": bounds-aware position and rotation caps agree with standalone");
            report.AppendLine("MEASURE " + label + ": standalone/integrated build=" + baseline.buildMs.ToString("F3") + "/" + integrated.buildMs.ToString("F3") + "ms; 120 Present calls after20warmup=" + baseline.presentMs.ToString("F3") + "/" + integrated.presentMs.ToString("F3") + "ms; yields=" + baseline.yields + "/" + integrated.yields + "; nodes=" + baseline.nodes + "/" + integrated.nodes + "; meshes=" + baseline.meshes + "/" + integrated.meshes + ". Synthetic host cost includes reflective dispatch; no live FPS or acceptance claim.");
        }
    }
}
