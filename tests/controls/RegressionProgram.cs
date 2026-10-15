// Managed regression fixtures for the ACTUAL linked 1.4.0 source files.
// Unity components, input polling, reach and the HUD timer are explicit stand-ins.
// These fixtures do not load Unity, Harmony, Rewired or the game and do not
// establish live scene cloning, serialization, physics or player reach behavior.
using System;
using System.Linq;
using System.Reflection;
using DVCCLControlFix;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.HUD;
using DV.KeyboardInput;
using DV.Simulation.Controllers;
using DV.Simulation.Ports;
using DV.Interaction.Inputs;
using UnityEngine;
using CT = DV.HUD.InteriorControlsManager.ControlType;

class RegressionProgram
{
    static int passed, failed, sequence;
    static readonly System.Collections.Generic.List<string> logMessages = new();
    static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static void Assert(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    static void Near(float actual, float expected, string reason)
        => Assert(Math.Abs(actual - expected) < .00001f, reason + ": actual=" + actual + ", expected=" + expected);
    static void Run(string name, Action body)
    {
        InputManager.NewPlayer = new Player();
        DVCCLControlFix.Main.Enabled = true;
        Time.frameCount++;
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + (ex.InnerException ?? ex).Message); }
        finally { InputManager.NewPlayer = new Player(); DVCCLControlFix.Main.Enabled = true; }
    }
    sealed class Fixture
    {
        public readonly TrainCar Car;
        public readonly GameObject Cab;
        public readonly InteriorControlsManager Manager;
        public Fixture(string label)
        {
            var root = new GameObject(label + ++sequence);
            Car = root.AddComponent<TrainCar>(); TrainCar.All.Add(Car);
            Car.carLivery = new CCL.Test.CustomLivery { id = root.name, prefab = root };
            Cab = new GameObject(root.name + "Cab"); Car.carLivery.interiorPrefab = Cab; Car.loadedInterior = Cab;
            Manager = Cab.AddComponent<InteriorControlsManager>(); Manager.Car = Car;
        }
        public TestControl Control(string name, string port, bool isSwitch = false, bool initializedJoint = true)
        {
            var obj = new GameObject(name) { parent = Cab }; Cab.children.Add(obj);
            var c = obj.AddComponent<TestControl>();
            if (isSwitch) obj.AddComponent<ToggleSwitch>();
            else { obj.AddComponent<Lever>(); if (initializedJoint) obj.AddComponent<HingeJoint>(); }
            if (port != null) obj.AddComponent<InteractablePortFeeder>().portId = port;
            return c;
        }
        public void Process()
        {
            Cab.AddComponent<InteractablePortFeedersController>().entries = Cab.GetComponentsInChildren<InteractablePortFeeder>(true);
            KeyboardInputFixer.EnsureFixed(Car);
            foreach (var k in Cab.GetComponentsInChildren<ConnectedValueKeyboardInput>(true)) k.SetupActions(Manager);
        }
        public ConnectedValueKeyboardInput[] Repairs => Cab.GetComponentsInChildren<ConnectedValueKeyboardInput>(true);
        // Native InteractablesKeyboardControl invokes every entry unconditionally.
        // We intentionally do not filter enabled or active components here.
        public void TickRepairs(float dt = .01f) { foreach (var k in Repairs) k.Tick(dt); }
        public int Writes => Cab.GetComponentsInChildren<TestControl>(true).Sum(c => c.setCalls);
    }
    static AKeyboardInput Author(TestControl c, string action, bool flip = false)
    {
        var reference = new AKeyboardInput.ActionReference { name = action, flip = flip };
        if (action.EndsWith("Toggle", StringComparison.Ordinal))
        {
            var toggle = c.gameObject.AddComponent<ToggleValueKeyboardInput>(); toggle.useAction = reference; return toggle;
        }
        var axis = c.gameObject.AddComponent<MouseScrollKeyboardInput>(); axis.scrollAction = reference; return axis;
    }
    static AKeyboardInput.ActionReference ActionRef(AKeyboardInput input)
        => input is MouseScrollKeyboardInput axis ? axis.scrollAction : ((ToggleValueKeyboardInput)input).useAction;
    static void Hold(string action, bool negative = false)
    {
        InputManager.NewPlayer.Reset();
        if (negative) InputManager.NewPlayer.minus.Add(action.GetHashCode());
        else InputManager.NewPlayer.plus.Add(action.GetHashCode());
    }
    static void Press(string action) { InputManager.NewPlayer.down.Add(action.GetHashCode()); }
    static ConnectedValueKeyboardInput Direct(TestControl c, string action, bool toggle = false)
    {
        var k = c.gameObject.AddComponent<ConnectedValueKeyboardInput>();
        if (toggle) k.toggle = new AKeyboardInput.ActionReference { name = action };
        else k.incremental = new AKeyboardInput.ActionReference { name = action };
        return k;
    }
    // Runtime arbitration cases need two candidates even on the broken baseline,
    // whose prefab repair incorrectly adds only the first. This explicit fixture
    // construction does not claim those candidates were generated by Process.
    static ConnectedValueKeyboardInput RuntimeCandidate(Fixture f, TestControl c, string action, bool toggle = false)
    {
        var k = c.GetComponent<ConnectedValueKeyboardInput>() ?? Direct(c, action, toggle);
        k.sharedPortId = c.GetComponent<InteractablePortFeeder>()?.portId;
        k.existingSharedInputs = f.Cab.GetComponentsInChildren<AKeyboardInput>(true).Where(x => x is not ConnectedValueKeyboardInput).ToArray();
        k.SetupActions(f.Manager); return k;
    }
    static void Lifecycle(ConnectedValueKeyboardInput k, string method)
        => typeof(ConnectedValueKeyboardInput).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(k, null);
    static void ReleaseCase(string label, Action<TestControl, ConnectedValueKeyboardInput> stop)
    {
        Run(label, () => {
            var f = new Fixture(label); var c = f.Control("spring", "horn.EXT_IN");
            c.GetComponent<Lever>().useSteppedJoint = false;
            var k = Direct(c, "HornIncremental"); Hold("HornIncremental"); k.Tick(.01f);
            Assert(c.setCalls == 1, "initial held key did not dispatch exactly once");
            stop(c, k); Assert(c.ReleaseCalls == 1, "expected one native scroll Release, got " + c.ReleaseCalls);
        });
    }
    static (bool native, bool result) Hud(InteriorControlsManager manager, CT type, int notches)
    {
        object[] args = { manager, type, notches, false };
        var native = (bool)typeof(HUDStepPatch).GetMethod("Prefix", PrivateStatic).Invoke(null, args);
        return (native, (bool)args[3]);
    }
    public sealed class Widget : Component {}
    sealed class CabWidgets { public Component horn; }
    sealed class SteamWidgets { public Component blower; }
    sealed class HudWidgets { public CabWidgets cab; public SteamWidgets steam; }
    static Widget HiddenWidget(string name) { var obj = new GameObject(name); obj.SetActive(false); return obj.AddComponent<Widget>(); }
    static void Main()
    {
        DVCCLControlFix.Main.Logger = new DVCCLControlFix.ControlsLogger(logMessages.Add);
        Run("original case 1: authored front action remains authoritative", () => {
            var f = new Fixture("normalTwins"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("rear", "brake.EXT_IN");
            var authored = Author(a, "BrakeIncremental", true); var originalRef = ActionRef(authored);
            f.Process(); Hold("BrakeIncremental"); f.TickRepairs();
            Assert(f.Writes == 0, "a repair dispatched beside a reachable author binding");
            Assert(ReferenceEquals(originalRef, ActionRef(authored)) && ActionRef(authored).flip, "author action or flip changed");
        });
        Run("original case 2: complementary author actions stay independent", () => {
            var f = new Fixture("splitActions"); var a = f.Control("first", "fireboxDoor.EXT_IN"); var b = f.Control("second", "fireboxDoor.EXT_IN");
            var axis = Author(a, "FiredoorIncremental"); var toggle = Author(b, "FiredoorToggle");
            f.Process(); Hold("FiredoorIncremental"); Press("FiredoorToggle"); f.TickRepairs();
            Assert(f.Writes == 0, "repair duplicated one of the complementary authored actions");
            Assert(ActionRef(axis).name == "FiredoorIncremental" && ActionRef(toggle).name == "FiredoorToggle", "author action changed");
        });
        Run("original case 3: rear-only reachable twin gets keyboard", () => {
            var f = new Fixture("reachTwins"); var front = f.Control("front", "brake.EXT_IN"); var rear = f.Control("rear", "brake.EXT_IN"); f.Process();
            foreach (var k in f.Repairs) k.reachable = k.gameObject == rear.gameObject;
            Hold("BrakeIncremental"); f.TickRepairs();
            Assert(rear.setCalls == 1 && front.setCalls == 0, "rear writes=" + rear.setCalls + ", unreachable front writes=" + front.setCalls + ", repaired listeners=" + f.Repairs.Length);
        });
        Run("original case 4: skipped twin keeps tiny-scroll F4 marker", () => {
            var f = new Fixture("rearHud"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("rear", "brake.EXT_IN"); Author(a, "BrakeIncremental"); f.Process();
            Assert(b.GetComponent<SmallScrollConnection>() != null, "rear tiny-scroll marker missing");
        });
        Run("original case 5: toggle-bound switch gets no repaired incremental", () => {
            var f = new Fixture("switchBound"); var c = f.Control("cocks", "cylinderCock.EXT_IN", true); Author(c, "CylCockToggle"); f.Process();
            Assert(f.Repairs.All(k => k.incremental == null), "incremental axis added to toggle-bound switch");
        });
        Run("original case 6: unbound switch retains incremental and toggle", () => {
            var f = new Fixture("switchMissing"); f.Control("cocks", "cylinderCock.EXT_IN", true); f.Process();
            Assert(f.Repairs.Any(k => k.incremental?.name == "CylCockIncremental" && k.toggle?.name == "CylCockToggle"), "documented incremental-plus-toggle pair missing");
        });
        ReleaseCase("original case 7: momentary releases on key-up", (c,k) => { InputManager.NewPlayer.Reset(); k.Tick(.01f); k.Tick(.31f); });
        Run("shared-port repaired twins dispatch once when both reachable", () => {
            var f = new Fixture("allReach"); f.Control("front", "brake.EXT_IN"); f.Control("rear", "brake.EXT_IN"); f.Process(); Hold("BrakeIncremental"); f.TickRepairs();
            Assert(f.Writes == 1, "expected one effective step, got " + f.Writes);
        });
        Run("ownership transfers to rear when front loses reach", () => {
            var f = new Fixture("transfer"); var front = f.Control("front", "brake.EXT_IN"); var rear = f.Control("rear", "brake.EXT_IN"); f.Process(); Hold("BrakeIncremental"); f.TickRepairs();
            Assert(front.setCalls == 1, "front did not take first dispatch"); Time.frameCount++;
            foreach (var k in f.Repairs) k.reachable = k.gameObject == rear.gameObject;
            // Reachable candidate ticks before the former owner, exercising forced release.
            foreach (var k in f.Repairs.Reverse()) k.Tick(.01f);
            Assert(rear.setCalls == 1 && front.ReleaseCalls == 1, "rear writes=" + rear.setCalls + ", old-owner releases=" + front.ReleaseCalls);
        });
        Run("unreachable author permits reachable rear fallback", () => {
            var f = new Fixture("authorReach"); var front = f.Control("front", "brake.EXT_IN"); var rear = f.Control("rear", "brake.EXT_IN"); Author(front, "BrakeIncremental").reachable = false; f.Process();
            foreach (var k in f.Repairs) k.reachable = k.gameObject == rear.gameObject;
            Hold("BrakeIncremental"); f.TickRepairs(); Assert(rear.setCalls == 1, "no fallback dispatch on reachable rear");
        });
        Run("author returning to reach takes priority without releasing its held action", () => {
            var f = new Fixture("authorReturn"); var front = f.Control("front", "horn.EXT_IN"); var rear = f.Control("rear", "horn.EXT_IN"); var author = Author(front, "HornIncremental"); author.reachable = false; f.Process();
            foreach (var k in f.Repairs) k.reachable = k.gameObject == rear.gameObject;
            Hold("HornIncremental"); f.TickRepairs(); Assert(rear.setCalls == 1, "rear fallback not initialized");
            author.reachable = true; Time.frameCount++; f.TickRepairs(.3f);
            Assert(rear.setCalls == 1 && rear.ReleaseCalls == 0, "repair moved or released the now-authored held port");
        });
        Run("disabled authored entry retains native scheduler authority", () => {
            var f = new Fixture("disabledAuthor"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("rear", "brake.EXT_IN"); Author(a, "BrakeIncremental").enabled = false; f.Process(); RuntimeCandidate(f, b, "BrakeIncremental"); Hold("BrakeIncremental"); f.TickRepairs();
            Assert(f.Writes == 0, "repair dispatched although native scheduler still ticks disabled author entry");
        });
        foreach (bool flip in new[] { false, true })
        foreach (bool negativeScroll in new[] { false, true })
        foreach (bool runtimeInvert in new[] { false, true })
        Run("native-to-backup direction: flip=" + flip + ", negative-scroll=" + negativeScroll + ", runtime-invert=" + runtimeInvert, () => {
            var f = new Fixture("nativeDirection"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("rear", "brake.EXT_IN"); a.Value = b.Value = .5f; var native = Author(a, "BrakeIncremental", flip);
            a.GetComponent<Lever>().scrollWheelHoverScroll = negativeScroll ? -.025f : .025f; a.GetComponent<Lever>().invertDirection = true; a.InvertScrollingDirection = runtimeInvert; f.Process();
            foreach (var k in f.Repairs) k.reachable = k.gameObject == b.gameObject;
            Hold("BrakeIncremental"); native.Tick(.01f); Assert(a.setCalls == 1, "linked native author input did not dispatch"); int nativeSign = Math.Sign(a.Value - .5f);
            native.reachable = false; Time.frameCount++; f.TickRepairs();
            Assert(b.setCalls == 1, "reachable backup did not dispatch"); Assert(Math.Sign(b.Value - .5f) == nativeSign, "backup changed direction from actual linked native polling/scroll contract"); Assert(a.GetComponent<Lever>().invertDirection, "author physical inversion changed");
        });
        Run("conflicting authored flips leave ambiguous repaired axis inactive", () => {
            var f = new Fixture("ambiguousFlip"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("middle", "brake.EXT_IN"); var rear = f.Control("rear", "brake.EXT_IN"); Author(a, "BrakeIncremental", false).reachable = false; Author(b, "BrakeIncremental", true).reachable = false; f.Process();
            foreach (var k in f.Repairs) k.reachable = k.gameObject == rear.gameObject;
            Hold("BrakeIncremental"); f.TickRepairs(); Assert(rear.setCalls == 0, "ambiguous author orientation guessed for backup");
            Assert(logMessages.Any(x => x.Contains("disagree", StringComparison.OrdinalIgnoreCase) && x.Contains("BrakeIncremental")), "ambiguous authored direction was not reported");
        });
        Run("rear backup retains native onlyScrollOnce repeat policy", () => {
            var f = new Fixture("authorOnce"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("rear", "brake.EXT_IN"); var author = (MouseScrollKeyboardInput)Author(a, "BrakeIncremental"); author.onlyScrollOnce = true; author.reachable = false; f.Process(); foreach (var k in f.Repairs) k.reachable = k.gameObject == b.gameObject;
            Hold("BrakeIncremental"); f.TickRepairs(); Time.frameCount++; f.TickRepairs(.5f); Assert(b.setCalls == 1, "onlyScrollOnce backup repeated while held"); InputManager.NewPlayer.Reset(); Time.frameCount++; f.TickRepairs(); Hold("BrakeIncremental"); Time.frameCount++; f.TickRepairs(); Assert(b.setCalls == 2, "onlyScrollOnce backup failed to rearm after key-up");
        });
        foreach (bool running in new[] { false, true })
        Run("rear backup retains native Run repeat speedup=" + running, () => {
            var f = new Fixture("authorRun"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("rear", "brake.EXT_IN"); Author(a, "BrakeIncremental").reachable = false; f.Process(); foreach (var k in f.Repairs) k.reachable = k.gameObject == b.gameObject;
            Hold("BrakeIncremental"); if(running)InputManager.NewPlayer.plus.Add(InputManager.Actions.Run); f.TickRepairs(); Time.frameCount++; f.TickRepairs(.13f); Assert(b.setCalls == (running ? 2 : 1), "backup repeat timing differs from native Run multiplier");
        });
        Run("conflicting authored repeat policies leave backup inactive", () => {
            var f = new Fixture("repeatConflict"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("middle", "brake.EXT_IN"); var rear = f.Control("rear", "brake.EXT_IN"); ((MouseScrollKeyboardInput)Author(a, "BrakeIncremental")).onlyScrollOnce = true; Author(b, "BrakeIncremental"); foreach(var author in f.Cab.GetComponentsInChildren<AKeyboardInput>(true))author.reachable=false; f.Process(); foreach(var k in f.Repairs)k.reachable=k.gameObject==rear.gameObject;
            Hold("BrakeIncremental"); f.TickRepairs(); Assert(rear.setCalls == 0, "conflicting authored repeat policy was guessed");
        });
        Run("native authored toggle stays authoritative despite physical or base-control blocks", () => {
            var f = new Fixture("blockedAuthor"); var a = f.Control("front", "fireboxDoor.EXT_IN"); var b = f.Control("rear", "fireboxDoor.EXT_IN"); var native = Author(a, "FiredoorToggle"); a.IsScrollingBlocked = true; var block = a.gameObject.AddComponent<BrakeControl>(); block.IsControlBlocked = true; ActionRef(native).overridableBaseControl = block; f.Process(); RuntimeCandidate(f, b, "FiredoorToggle", true);
            Press("FiredoorToggle"); native.Tick(.01f); f.TickRepairs(); Assert(a.setCalls == 1 && b.setCalls == 0, "backup duplicated an author action that actual linked native ToggleValueKeyboardInput still dispatches");
        });
        Run("same port on separate cars has separate owners", () => {
            var a = new Fixture("carA"); a.Control("cab", "brake.EXT_IN"); a.Process(); var b = new Fixture("carB"); b.Control("cab", "brake.EXT_IN"); b.Process(); Hold("BrakeIncremental"); a.TickRepairs(); b.TickRepairs();
            Assert(a.Writes == 1 && b.Writes == 1, "different vehicles incorrectly share ownership");
        });
        Run("two actions on same port have separate ownership", () => {
            var f = new Fixture("perAction"); var a = f.Control("front", "fireboxDoor.EXT_IN"); var b = f.Control("rear", "fireboxDoor.EXT_IN"); f.Process(); Hold("FiredoorIncremental"); Press("FiredoorToggle"); f.TickRepairs();
            Assert(f.Writes == 2, "expected one incremental and one toggle dispatch, got " + f.Writes);
        });
        Run("ownership never dispatches shared port without an owning car", () => {
            var obj = new GameObject("orphan"); var c = obj.AddComponent<TestControl>(); obj.AddComponent<Lever>(); var k = Direct(c, "BrakeIncremental"); k.sharedPortId = "brake.EXT_IN"; Hold("BrakeIncremental"); k.Tick(.01f);
            Assert(c.setCalls == 0, "orphan shared control moved");
        });
        ReleaseCase("momentary releases when component disabled", (c,k) => { k.enabled = false; Lifecycle(k, "OnDisable"); });
        ReleaseCase("momentary releases when mod disabled", (c,k) => { DVCCLControlFix.Main.Enabled = false; k.Tick(.01f); });
        ReleaseCase("momentary releases on physical scroll block", (c,k) => { c.IsScrollingBlocked = true; k.Tick(.01f); });
        ReleaseCase("momentary releases on base-control block", (c,k) => { var block = c.gameObject.AddComponent<BrakeControl>(); block.IsControlBlocked = true; k.incremental.overridableBaseControl = block; k.Tick(.01f); });
        ReleaseCase("momentary releases when player loses reach", (c,k) => { k.reachable = false; k.Tick(.01f); });
        ReleaseCase("momentary releases when physically grabbed", (c,k) => { c.grabbed = true; k.Tick(.01f); });
        ReleaseCase("momentary releases when interaction is disallowed", (c,k) => { c.InteractionAllowed = false; k.Tick(.01f); });
        ReleaseCase("momentary releases on destroy", (c,k) => Lifecycle(k, "OnDestroy"));
        Run("early key-up avoids release into uninitialized lever joint", () => {
            var f = new Fixture("earlyRelease"); var c = f.Control("spring", "horn.EXT_IN", initializedJoint:false); var k = Direct(c, "HornIncremental"); Hold("HornIncremental"); k.Tick(.01f); InputManager.NewPlayer.Reset(); k.Tick(.01f);
            Assert(c.ReleaseCalls == 0, "native release called before a physics joint exists");
        });
        Run("OnDisable before movement does not require initialized input manager", () => {
            var f = new Fixture("earlyDisable"); var k = Direct(f.Control("spring", "horn.EXT_IN"), "HornIncremental"); InputManager.NewPlayer = null; Lifecycle(k, "OnDisable");
        });
        Run("native scheduler cannot reactivate disabled repaired entry", () => {
            var f = new Fixture("disabledRepair"); var c = f.Control("cab", "brake.EXT_IN"); f.Process(); var k = f.Repairs.Single(); k.enabled = false; Lifecycle(k, "OnDisable"); Hold("BrakeIncremental"); f.TickRepairs();
            Assert(c.setCalls == 0, "unconditional scheduler tick moved disabled repair");
        });
        Run("native scheduler cannot dispatch inactive repaired entry", () => {
            var f = new Fixture("inactiveRepair"); var c = f.Control("cab", "brake.EXT_IN"); f.Process(); c.gameObject.SetActive(false); Hold("BrakeIncremental"); f.TickRepairs();
            Assert(c.setCalls == 0, "unconditional scheduler tick moved inactive repair");
        });
        Run("same-frame disable handoff cannot step same port twice", () => {
            var f = new Fixture("frameStep"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("rear", "brake.EXT_IN"); f.Process(); RuntimeCandidate(f, b, "BrakeIncremental"); var first = a.GetComponent<ConnectedValueKeyboardInput>();
            a.Changed += _ => { first.enabled = false; Lifecycle(first, "OnDisable"); }; Hold("BrakeIncremental"); f.TickRepairs();
            Assert(f.Writes == 1, "same-frame handoff caused " + f.Writes + " steps");
            Assert(a.ReleaseCalls == 1, "synchronous disable during SetValue lost the momentary release");
            Assert(a.Operations.Last() == "Release" && a.SpringTarget == 0, "release occurred before native AcceptSetValue: " + string.Join(" -> ", a.Operations));
            Time.frameCount++; f.TickRepairs(); Assert(b.setCalls == 1, "handoff did not resume on the next frame");
        });
        Run("synchronous disable during repeat releases after native AcceptSetValue", () => {
            var f = new Fixture("repeatDisable"); var c = f.Control("spring", "horn.EXT_IN"); c.GetComponent<Lever>().useSteppedJoint = false; var k = Direct(c, "HornIncremental");
            c.Changed += _ => { if (c.setCalls == 2) { k.enabled = false; Lifecycle(k, "OnDisable"); } }; Hold("HornIncremental"); k.Tick(.01f); Time.frameCount++; k.Tick(.26f);
            Assert(c.setCalls == 2 && c.ReleaseCalls == 1, "repeat did not move twice then release once"); Assert(c.Operations.Last() == "Release" && c.SpringTarget == 0, "repeat release occurred before AcceptSetValue: " + string.Join(" -> ", c.Operations));
        });
        ReleaseCase("movement can release safely after input manager disappears", (c,k) => { InputManager.NewPlayer = null; Lifecycle(k, "OnDisable"); });
        Run("same-frame disable handoff cannot toggle same port twice", () => {
            var f = new Fixture("frameToggle"); var a = f.Control("front", "cylinderCock.EXT_IN", true); var b = f.Control("rear", "cylinderCock.EXT_IN", true); f.Process(); RuntimeCandidate(f, b, "CylCockToggle", true); var first = a.GetComponent<ConnectedValueKeyboardInput>();
            a.Changed += _ => { first.enabled = false; Lifecycle(first, "OnDisable"); }; Press("CylCockToggle"); f.TickRepairs();
            Assert(f.Writes == 1, "same-frame handoff caused " + f.Writes + " toggles");
        });
        Run("toggle callback disabling portless input prevents subsequent incremental dispatch", () => {
            var f = new Fixture("portlessDisabled"); var c = f.Control("handbrake", null, true); var k = Direct(c, "HandbrakeToggle", true); k.incremental = new AKeyboardInput.ActionReference { name="HandbrakeIncremental" }; c.Changed += _ => {k.enabled=false;Lifecycle(k,"OnDisable");}; Hold("HandbrakeIncremental"); Press("HandbrakeToggle"); k.Tick(.01f); Assert(c.setCalls == 1, "incremental dispatched after toggle callback disabled its input");
        });
        Run("limited lever uses N minus one intervals", () => {
            var f = new Fixture("limited"); var c = f.Control("cab", null); c.GetComponent<Lever>().notches = 4; ConnectedValueKeyboardInput.Step(c, 1); Near(c.Value, 1f/3, "limited notch spacing");
        });
        Run("unlimited lever uses N intervals", () => {
            var f = new Fixture("unlimited"); var c = f.Control("cab", null); var lever = c.GetComponent<Lever>(); lever.notches = 4; lever.useLimits = false; ConnectedValueKeyboardInput.Step(c, 1); Near(c.Value, .25f, "unlimited notch spacing");
        });
        Run("continuous lever uses continuous delta and preserves inversion", () => {
            var f = new Fixture("continuous"); var c = f.Control("cab", null); var lever = c.GetComponent<Lever>(); lever.useSteppedJoint = false; lever.invertDirection = true; ConnectedValueKeyboardInput.Step(c, 1); Near(c.Value, .05f, "continuous step"); Assert(lever.invertDirection, "physical inversion rewritten");
        });
        Run("held key respects initial repeat delay and repeat interval", () => {
            var f = new Fixture("repeat"); var c = f.Control("cab", null); c.GetComponent<Lever>().useSteppedJoint = false; var k = Direct(c, "HornIncremental"); Hold("HornIncremental"); k.Tick(.01f); Time.frameCount++; k.Tick(.24f); Assert(c.setCalls == 1, "repeated before 0.25 seconds"); Time.frameCount++; k.Tick(.011f); Assert(c.setCalls == 2, "first repeat missing"); Time.frameCount++; k.Tick(.126f); Assert(c.setCalls == 3, "0.125-second repeat missing");
        });
        Run("HUD tiny-scroll twin gets corrected step and native release timer", () => {
            var f = new Fixture("hudTimer"); var a = f.Control("front", "brake.EXT_IN"); var b = f.Control("rear", "brake.EXT_IN"); Author(a, "BrakeIncremental"); f.Process(); f.Manager.Register(CT.TrainBrake, b); var r = Hud(f.Manager, CT.TrainBrake, 1);
            Assert(!r.native && r.result, "HUD patch did not handle repaired control"); Near(b.Value, 1f/3, "HUD notch"); Assert(b.setCalls == 1, "HUD applied extra scroll step"); Near(f.Manager.scrollableTimerUtil.timerDictionary[b].value, .3f, "native release timer"); f.Manager.scrollableTimerUtil.Advance(.31f); Assert(b.ReleaseCalls == 1, "native HUD timer did not release momentary control");
        });
        Run("HUD reversal is preserved without mutating physical inversion", () => {
            var f = new Fixture("hudReverse"); var c = f.Control("cab", "brake.EXT_IN"); c.Value = 1; c.GetComponent<Lever>().invertDirection = true; f.Process(); f.Manager.Register(CT.TrainBrake, c); f.Manager.reverseDirectionList.Add(CT.TrainBrake); Hud(f.Manager, CT.TrainBrake, 1); Near(c.Value, 2f/3, "HUD reverse direction"); Assert(c.GetComponent<Lever>().invertDirection, "physical inversion rewritten");
        });
        Run("HUD blocked base control refuses movement and timer", () => {
            var f = new Fixture("hudBlock"); var c = f.Control("cab", "brake.EXT_IN"); f.Process(); var b = c.gameObject.AddComponent<BrakeControl>(); b.IsControlBlocked = true; f.Manager.Register(CT.TrainBrake, c, b); var r = Hud(f.Manager, CT.TrainBrake, 1); Assert(!r.native && !r.result && c.setCalls == 0 && f.Manager.scrollableTimerUtil.timerDictionary.Count == 0, "blocked HUD dispatched or scheduled release");
        });
        Run("HUD zero notches never schedules release", () => {
            var f = new Fixture("hudZero"); var c = f.Control("cab", "brake.EXT_IN"); f.Process(); f.Manager.Register(CT.TrainBrake, c); Hud(f.Manager, CT.TrainBrake, 0); Assert(f.Manager.scrollableTimerUtil.timerDictionary.Count == 0, "zero movement scheduled release");
        });
        Run("successive HUD steps reuse and refresh the native release holder", () => {
            var f = new Fixture("hudRefresh"); var c = f.Control("cab", "brake.EXT_IN"); f.Process(); f.Manager.Register(CT.TrainBrake, c); Hud(f.Manager, CT.TrainBrake, 1); var timer = f.Manager.scrollableTimerUtil.timerDictionary[c]; f.Manager.scrollableTimerUtil.Advance(.2f); Hud(f.Manager, CT.TrainBrake, 1);
            Assert(ReferenceEquals(timer, f.Manager.scrollableTimerUtil.timerDictionary[c]), "existing native timer holder replaced"); Near(timer.value, .3f, "refreshed HUD timer"); f.Manager.scrollableTimerUtil.Advance(.2f); Assert(c.ReleaseCalls == 0, "HUD timer released too early after refresh"); f.Manager.scrollableTimerUtil.Advance(.11f); Assert(c.ReleaseCalls == 1, "refreshed HUD timer did not release once");
        });
        Run("unmarked author controls keep the native HUD path", () => {
            var f = new Fixture("nativeHud"); var c = f.Control("cab", null); f.Manager.Register(CT.TrainBrake, c); var r = Hud(f.Manager, CT.TrainBrake, 1); Assert(r.native && c.setCalls == 0, "unmarked author control was intercepted");
        });
        Run("non-CCL cars keep the native HUD path", () => {
            var f = new Fixture("plainHud"); var c = f.Control("cab", "brake.EXT_IN"); f.Process(); f.Manager.Register(CT.TrainBrake, c); f.Car.carLivery = new DV.ThingTypes.TrainCarLivery(); var r = Hud(f.Manager, CT.TrainBrake, 1); Assert(r.native && c.setCalls == 0, "non-CCL car was intercepted");
        });
        Run("F4 reveals registered real widget and leaves absent controls hidden", () => {
            var f = new Fixture("reveal"); var c = f.Control("whistle", "horn.EXT_IN"); f.Manager.Register(CT.Horn, c); var horn = HiddenWidget("hornWidget"); var blower = HiddenWidget("blowerWidget"); var hud = new HudWidgets { cab = new CabWidgets { horn = horn }, steam = new SteamWidgets { blower = blower } };
            HUDConnections.Reveal(new HUDInterfacer { currentCar = f.Car, currentHud = hud }); Assert(horn.gameObject.activeSelf && !blower.gameObject.activeSelf, "HUD visibility did not follow registered real controls");
        });
        Run("F4 reveal waits for initialized manager", () => {
            var f = new Fixture("revealWait"); var c = f.Control("whistle", "horn.EXT_IN"); f.Manager.Register(CT.Horn, c); f.Manager.Initialized = false; var horn = HiddenWidget("hornWidget"); HUDConnections.Reveal(new HUDInterfacer { currentCar = f.Car, currentHud = new HudWidgets { cab = new CabWidgets { horn = horn } } }); Assert(!horn.gameObject.activeSelf, "HUD revealed before manager initialization");
        });
        Run("F4 missing-widget warning is emitted once", () => {
            var f = new Fixture("missingWidget"); var c = f.Control("whistle", "horn.EXT_IN"); f.Manager.Register(CT.Horn, c); var owner = new HUDInterfacer { currentCar = f.Car, currentHud = new HudWidgets { cab = new CabWidgets() } }; int before = logMessages.Count;
            HUDConnections.Reveal(owner); HUDConnections.Reveal(owner); Assert(logMessages.Count == before + 1, "missing HUD widget warning absent or repeated");
        });
        Run("existing unscheduled author input is reconciled once", () => {
            var f = new Fixture("scheduler"); var c = f.Control("cab", "brake.EXT_IN"); var native = Author(c, "BrakeIncremental"); f.Process(); var scheduler = f.Cab.GetComponent<InteractablesKeyboardControl>(); Assert(scheduler.entries.Count(x => ReferenceEquals(x, native)) == 1, "author scheduler entry missing or duplicated"); KeyboardInputFixer.EnsureFixed(f.Car); Assert(scheduler.entries.Distinct().Count() == scheduler.entries.Length, "second repair duplicated scheduler entry");
        });
        Run("semantic reader repair fills null slot and preserves authored slot", () => {
            var f = new Fixture("reader"); var c = f.Control("opaqueObjectName", "brake.EXT_IN"); var retained = new GameObject("authoredThrottle"); var reader = f.Cab.AddComponent<LocoControlsReader>(); reader.throttle = retained; f.Process(); Assert(reader.trainBrake == c.gameObject && reader.throttle == retained, "reader mapping missing or authored reader overwritten");
        });
        Run("parent stop proactively releases initialized portless momentary input", () => {
            var f = new Fixture("parentStopPortless"); var c = f.Control("whistle", null); var k = Direct(c, "HornIncremental"); k.SetupActions(f.Manager);
            Hold("HornIncremental"); k.Tick(.01f); DVCCLControlFix.Main.Enabled = false;
            Assert(ConnectedValueKeyboardInput.ReleaseAll(), "release reported failure"); Assert(c.ReleaseCalls == 1 && c.SpringTarget == 0, "parent stop left a spring held");
            Assert(ConnectedValueKeyboardInput.ReleaseAll() && c.ReleaseCalls == 1, "idempotent stop released twice");
        });
        Run("parent release allows a new activation to track and release again", () => {
            var f = new Fixture("parentReenable"); var c = f.Control("whistle", null); var k = Direct(c, "HornIncremental"); k.SetupActions(f.Manager);
            Hold("HornIncremental"); k.Tick(.01f); DVCCLControlFix.Main.Enabled = false; Assert(ConnectedValueKeyboardInput.ReleaseAll(), "initial release failed");
            DVCCLControlFix.Main.Enabled = true; Time.frameCount++; k.Tick(.01f); DVCCLControlFix.Main.Enabled = false;
            Assert(ConnectedValueKeyboardInput.ReleaseAll() && c.ReleaseCalls == 2 && c.SpringTarget == 0, "re-enabled binding was not released");
        });
        Run("parent off on within a frame cannot double dispatch a shared port", () => {
            var f = new Fixture("parentSameFrame"); var c = f.Control("brake", "brake.EXT_IN"); f.Process(); Hold("BrakeIncremental"); f.TickRepairs(); int before = c.setCalls;
            DVCCLControlFix.Main.Enabled = false; Assert(ConnectedValueKeyboardInput.ReleaseAll(), "stop failed"); DVCCLControlFix.Main.Enabled = true; f.TickRepairs();
            Assert(c.setCalls == before, "parent off/on cleared same-frame dispatch ownership");
        });
        Run("repair mutation callback records changed prefab and HUD without loader", () => {
            int mutations = 0; DVCCLControlFix.Main.Mutation = () => mutations++;
            var f = new Fixture("mutationNotice"); f.Control("brake", "brake.EXT_IN"); f.Process(); Assert(mutations > 0, "prefab changes not marked");
            DVCCLControlFix.Main.Mutation = null;
        });
        Run("parent cleanup unsubscribes per-interior diagnostic callback", () => {
            var f = new Fixture("auditCleanup"); KeyboardInputFixer.ReportWhenReady(f.Car); Assert(DVCCLControlFix.Main.ClearAudits(), "audit cleanup failed");
            int before = logMessages.Count; f.Manager.FireInitialized(); Assert(logMessages.Count == before, "unloaded audit callback remained subscribed");
        });
        var errors = logMessages.Where(x => x.StartsWith("ERROR")).ToArray();
        if (errors.Length != 0) { failed++; Console.WriteLine("FAIL production error log: " + string.Join(" | ", errors)); }
        Console.WriteLine("SUMMARY " + passed + " passed, " + failed + " failed. Actual 1.4.0 source with explicit managed stand-ins; not live Unity.");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
