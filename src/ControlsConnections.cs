using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.HUD;
using DV.KeyboardInput;
using DV.Simulation.Ports;
using DV.UI.LocoHUD;
using HarmonyLib;
using UnityEngine;
using ControlType = DV.HUD.InteriorControlsManager.ControlType;
namespace DVCCLControlFix;

// Drives the same physical ControlImplBase and ValueChanged event used by the
// cab. No separate simulator state or duplicate key listener is introduced.
public class ConnectedValueKeyboardInput : AKeyboardInput
{
    public ActionReference incremental;
    public ActionReference toggle;
    public string sharedPortId;
    public AKeyboardInput[] existingSharedInputs;
    private int heldDirection;
    private float repeatTimer;
    private bool movedIncremental;
    private bool steppingIncremental;
    private bool resetAfterStep;
    private string incrementalOwnerKey;
    private string toggleOwnerKey;
    private static readonly Dictionary<string, ConnectedValueKeyboardInput> Owners = new Dictionary<string, ConnectedValueKeyboardInput>();
    private static readonly HashSet<string> DispatchedThisFrame = new HashSet<string>();
    private static int dispatchFrame = -1;
    private static readonly Dictionary<Type, PropertyInfo> BlockProperties = new Dictionary<Type, PropertyInfo>();
    // Only initialized live bindings need proactive momentary release on a
    // parent stop. Weak references do not retain streamed-out cab objects.
    private static readonly List<WeakReference> LiveBindings = new List<WeakReference>();
    private bool tracked;
    private void TrackBinding()
    {
        if (tracked) return;
        for (int i = LiveBindings.Count - 1; i >= 0; --i)
        {
            var binding = LiveBindings[i].Target as ConnectedValueKeyboardInput;
            if (binding == null) { LiveBindings.RemoveAt(i); continue; }
            if (binding == this) { tracked = true; return; }
        }
        LiveBindings.Add(new WeakReference(this));
        tracked = true;
    }
    internal static bool ReleaseAll()
    {
        bool success = true;
        foreach (var weak in LiveBindings.ToArray())
        {
            var binding = weak.Target as ConnectedValueKeyboardInput;
            if (binding == null) continue;
            try { binding.ResetInputs(binding.GetComponent<ControlImplBase>()); binding.tracked = false; }
            catch (Exception ex) { success = false; Main.Logger.Error("Could not release a repaired control: " + ex); }
        }
        if (success)
        {
            LiveBindings.Clear(); Owners.Clear(); BlockProperties.Clear();
            // Keep this frame's dispatch stamp across a synchronous parent
            // off/on transition, just as ordinary input-owner handoff does.
        }
        return success;
    }
    public override bool FixedUpdateTick => false;
    public override void SetupActions(InteriorControlsManager manager)
    {
        TrackBinding();
        ResetInputs(GetComponent<ControlImplBase>());
        incremental?.Initialize(manager);
        toggle?.Initialize(manager);
        ConfigureOwnership(manager?.Car);
    }
    public override void Tick(float deltaTime)
    {
        TrackBinding();
        var control = GetComponent<ControlImplBase>();
        // The native controller invokes every serialized entry directly, even
        // when its component is disabled or its child object is inactive.
        if (!Main.Enabled || !enabled || !gameObject.activeInHierarchy || !PlayerCanReach() || !CanMove(control))
        { ResetInputs(control); return; }
        var player = DV.Interaction.Inputs.InputManager.NewPlayer;
        if (player == null) { ResetInputs(control); return; }
        if (!string.IsNullOrEmpty(sharedPortId) && incrementalOwnerKey == null && toggleOwnerKey == null)
            ConfigureOwnership(null);
        if (toggle != null && toggle.CanMoveOverridableBaseControl &&
            player.GetButtonDown(toggle.id) && Claim(toggleOwnerKey, toggle) && TryDispatch(toggleOwnerKey))
            control.SetValue(1f - Mathf.Round(control.Value));
        else if (toggle == null || !player.GetButtonDown(toggle.id))
            ReleaseOwner(toggleOwnerKey);
        // A toggle's ValueChanged callback may disable or block this control.
        if (!Main.Enabled || !enabled || !gameObject.activeInHierarchy || !PlayerCanReach() || !CanMove(control))
        { ResetInputs(control); return; }
        if (incremental == null || !incremental.CanMoveOverridableBaseControl)
        { ResetIncremental(control); return; }
        bool plus = player.GetButton(incremental.id);
        bool minus = player.GetNegativeButton(incremental.id);
        int direction = plus == minus ? 0 : plus ? 1 : -1;
        if (direction == 0 || !Claim(incrementalOwnerKey, incremental))
        { ResetIncremental(control); return; }
        if (!TryGetIncrementalPolicy(out bool flip, out bool singleStep, out bool authoredPolicy))
        { ResetIncremental(control); return; }
        if (flip) direction = -direction;
        if (direction != heldDirection)
        {
            if (!TryDispatch(incrementalOwnerKey)) return;
            heldDirection = direction;
            repeatTimer = singleStep ? float.MaxValue : 0.25f;
            StepIncremental(control, direction);
        }
        else
        {
            if (authoredPolicy && player.GetButton(DV.Interaction.Inputs.InputManager.Actions.Run)) deltaTime *= 2f;
            repeatTimer -= deltaTime;
            if (repeatTimer <= 0f && TryDispatch(incrementalOwnerKey))
            { repeatTimer = 0.125f; StepIncremental(control, direction); }
        }
    }
    private void StepIncremental(ControlImplBase control, int direction)
    {
        movedIncremental = true;
        steppingIncremental = true;
        try { Step(control, direction); }
        finally
        {
            steppingIncremental = false;
            // Native SetValue calls ValueChanged before AcceptSetValue. If a
            // callback disables this input, release after AcceptSetValue has
            // finished so it cannot overwrite the spring reset afterward.
            if (resetAfterStep) { resetAfterStep = false; ResetInputs(control); }
        }
    }
    private static bool TryDispatch(string key)
    {
        if (key == null) return true;
        if (dispatchFrame != Time.frameCount)
        { dispatchFrame = Time.frameCount; DispatchedThisFrame.Clear(); }
        // Keep the stamp through synchronous ValueChanged/OnDisable handoff.
        return DispatchedThisFrame.Add(key);
    }
    private bool TryGetIncrementalPolicy(out bool flip, out bool singleStep, out bool authoredPolicy)
    {
        flip = incremental.flip;
        singleStep = false;
        authoredPolicy = false;
        bool found = false;
        if (existingSharedInputs == null) return true;
        foreach (var existing in existingSharedInputs)
        {
            if (existing == null) continue;
            foreach (var field in existing.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType != typeof(ActionReference) ||
                    !(field.GetValue(existing) is ActionReference authored) || authored.name != incremental.name) continue;
                var spec = existing.GetComponent<Lever>();
                var lever = existing.GetComponent<ControlImplBase>() as LeverBase;
                var mouse = existing as MouseScrollKeyboardInput;
                // Native mouse-scroll lever direction also includes signed
                // scroll magnitude and runtime scrolling inversion. Unknown
                // authored input behavior must not be guessed from flip alone.
                if (mouse == null || spec == null || lever == null || spec.scrollWheelHoverScroll == 0f)
                {
                    Main.WarnOnce("keyboard-direction/" + sharedPortId + "/" + incremental.name,
                        "Cannot establish the existing input's normalized direction; leaving its backup action inactive.");
                    return false;
                }
                bool authoredFlip = authored.flip ^ (spec.scrollWheelHoverScroll < 0f) ^ lever.InvertScrollingDirection;
                if (found && (flip != authoredFlip || singleStep != mouse.onlyScrollOnce))
                {
                    Main.WarnOnce("keyboard-direction/" + sharedPortId + "/" + incremental.name,
                        "Existing inputs disagree on normalized direction or repeat policy; leaving their backup action inactive.");
                    return false;
                }
                flip = authoredFlip;
                singleStep = mouse.onlyScrollOnce;
                found = true;
            }
        }
        authoredPolicy = found;
        return true;
    }
    private void ConfigureOwnership(TrainCar car)
    {
        if (string.IsNullOrEmpty(sharedPortId)) return;
        if (car == null) car = TrainCar.Resolve(transform);
        if (car == null) return;
        string target = car.gameObject.GetInstanceID() + "/" + sharedPortId + "/";
        incrementalOwnerKey = incremental == null ? null : target + incremental.name;
        toggleOwnerKey = toggle == null ? null : target + toggle.name;
    }
    private bool Claim(string key, ActionReference action)
    {
        if (HasReachableExistingAction(action)) return false;
        if (key == null)
        {
            if (!string.IsNullOrEmpty(sharedPortId))
            {
                Main.WarnOnce("keyboard-owner/" + sharedPortId,
                    "Cannot identify the owning vehicle for this shared control; leaving its repaired action inactive.");
                return false;
            }
            return true;
        }
        if (Owners.TryGetValue(key, out var owner) && owner != null && owner != this)
        {
            if (owner.enabled && owner.gameObject.activeInHierarchy && owner.PlayerCanReach() &&
                CanMove(owner.GetComponent<ControlImplBase>())) return false;
            owner.ResetInputs(owner.GetComponent<ControlImplBase>());
        }
        Owners[key] = this;
        return true;
    }
    private bool HasReachableExistingAction(ActionReference action)
    {
        if (action == null) return false;
        // Existing author bindings remain authoritative while reachable. The
        // array references are within this prefab and remap to its live clone.
        if (existingSharedInputs != null)
            foreach (var existing in existingSharedInputs)
            {
                // Native scheduling does not honor these components' enabled
                // state, and native toggles bypass movement/block checks.
                // A reachable author binding therefore remains authoritative.
                if (existing == null || !existing.PlayerCanReach()) continue;
                foreach (var field in existing.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    if (field.FieldType == typeof(ActionReference) &&
                        field.GetValue(existing) is ActionReference authored && authored.name == action.name) return true;
            }
        return false;
    }
    private void ReleaseOwner(string key)
    {
        if (key != null && Owners.TryGetValue(key, out var owner) && owner == this) Owners.Remove(key);
    }
    private void ResetIncremental(ControlImplBase control)
    {
        if (steppingIncremental)
        { resetAfterStep = true; ReleaseOwner(incrementalOwnerKey); return; }
        // Native scroll release resets non-notched spring targets. SetValue
        // alone moves the spring's target too, so autonomous physics cannot
        // return a repaired whistle to rest after the key is released.
        // LeverBase builds its joint on a deferred initialization coroutine.
        // Native release assumes that joint exists; an early key-up must not
        // dispatch into a not-yet-initialized physics lever.
        bool releaseReady = control != null &&
            (control.GetComponent<Lever>() == null || control.GetComponent<HingeJoint>() != null);
        if (movedIncremental && releaseReady)
        {
            var player = DV.Interaction.Inputs.InputManager.NewPlayer;
            bool authoredNowHolding = incremental != null && player != null &&
                (player.GetButton(incremental.id) || player.GetNegativeButton(incremental.id)) &&
                HasReachableExistingAction(incremental);
            if (!authoredNowHolding) control.GetComponent<IScrollable>()?.Scroll(ScrollAction.Release);
        }
        movedIncremental = false;
        heldDirection = 0;
        repeatTimer = 0f;
        ReleaseOwner(incrementalOwnerKey);
    }
    private void ResetInputs(ControlImplBase control)
    {
        ResetIncremental(control);
        ReleaseOwner(toggleOwnerKey);
    }
    private void OnDisable() { ResetInputs(GetComponent<ControlImplBase>()); incrementalOwnerKey = toggleOwnerKey = null; }
    private void OnDestroy() { ResetInputs(GetComponent<ControlImplBase>()); incrementalOwnerKey = toggleOwnerKey = null; }
    internal static void Step(ControlImplBase control, int direction)
    {
        if (!CanMove(control)) return;
        var spec = control.GetComponent<ControlSpec>() as INotchedSpec;
        int positions = spec != null && spec.IsNotched ? spec.NotchCount : 0;
        if (positions > 1 && spec is Lever lever && !lever.useLimits) positions++;
        control.SetValue(ConnectionMath.Move(control.Value, direction, positions));
    }
    internal static bool CanMove(ControlImplBase control)
    {
        if (control == null || !control.InteractionAllowed || control.IsGrabbed()) return false;
        var type = control.GetType();
        if (!BlockProperties.TryGetValue(type, out var property))
        {
            property = AccessTools.Property(type, "IsScrollingBlocked");
            BlockProperties[type] = property;
        }
        return property == null || property.PropertyType != typeof(bool) || !(bool)property.GetValue(control, null);
    }
}

// Marker only: this control's existing scroll magnitude is too small for a
// driving key or HUD notch. Its mouse drag and original bindings are retained.
public class SmallScrollConnection : MonoBehaviour { }

[HarmonyPatch(typeof(InteriorControlsManager), "MoveScrollable")]
internal static class HUDStepPatch
{
    private static bool Prefix(InteriorControlsManager __instance, ControlType type,
        int notches, ref bool __result)
    {
        if (!Main.Enabled || __instance.Car == null ||
            !KeyboardInputFixer.IsCclLivery(__instance.Car.carLivery)) return true;
        if (!__instance.TryGetControl(type, out var reference) || reference.controlImplBase == null)
            return true;
        var control = reference.controlImplBase;
        var addedInput = control.GetComponent<ConnectedValueKeyboardInput>();
        var marker = control.GetComponent<SmallScrollConnection>();
        if (addedInput == null && marker == null) return true;
        if (reference.overridableBaseControl != null && reference.overridableBaseControl.IsControlBlocked)
        { __result = false; return false; }
        if (!ConnectedValueKeyboardInput.CanMove(control)) { __result = false; return false; }
        // Preserve the manager's configured HUD reversal. Existing keyboard
        // bindings and physical lever inversion are never rewritten.
        if (__instance.reverseDirectionList != null && __instance.reverseDirectionList.Contains(type))
            notches = -notches;
        ConnectedValueKeyboardInput.Step(control, notches);
        // Use the game's existing HUD release timer, without applying an
        // additional scroll step. This preserves momentary/spring behaviour.
        if (notches != 0 && reference.scrollable != null && __instance.scrollableTimerUtil != null)
        {
            if (!__instance.scrollableTimerUtil.timerDictionary.TryGetValue(reference.scrollable, out var timer))
                timer = __instance.scrollableTimerUtil.timerDictionary[reference.scrollable] = new ScrollableTimerUtil.FloatHolder();
            timer.value = 0.3f;
        }
        __result = true;
        return false;
    }
}

internal static class HUDConnections
{
    // Explicit pairs prevent guessing that a similarly named diesel widget
    // belongs to a steam control. Only registered, real controls are revealed.
    private static readonly string[] Pairs = {
        "Throttle|basicControls|throttle", "Reverser|basicControls|reverser",
        "Sander|basicControls|sand", "GearboxA|basicControls|gearboxA", "GearboxB|basicControls|gearboxB",
        "TrainBrake|braking|trainBrake", "IndBrake|braking|indBrake", "DynamicBrake|braking|dynBrake",
        "TrainBrakeCutout|braking|brakeCutout", "Handbrake|braking|handbrake", "ReleaseCyl|braking|releaseCyl",
        "CylCock|steam|cylCock", "Injector|steam|injector", "Firedoor|steam|firedoor",
        "Blower|steam|blower", "Damper|steam|damper", "Blowdown|steam|blowdown",
        "CoalDump|steam|coalDump", "Dynamo|steam|dynamo", "AirPump|steam|airPump", "Lubricator|steam|lubricator",
        "Horn|cab|horn", "Bell|cab|bell", "HeadlightsFront|cab|headlightsFront", "HeadlightsRear|cab|headlightsRear",
        "IndHeadlightsTypeFront|cab|indHeadlightsTypeFront", "IndHeadlights1Front|cab|indHeadlights1Front",
        "IndHeadlights2Front|cab|indHeadlights2Front", "IndHeadlightsTypeRear|cab|indHeadlightsTypeRear",
        "IndHeadlights1Rear|cab|indHeadlights1Rear", "IndHeadlights2Rear|cab|indHeadlights2Rear",
        "CabLight|cab|cabLight", "IndCabLight|cab|indCabLight", "IndDashLight|cab|indDashLight",
        "Wipers|cab|wipers", "IndWipers1|cab|indWipers1", "IndWipers2|cab|indWipers2",
        "FuelCutoff|mechanical|fuelCutoff", "StarterControl|mechanical|starterControl",
        "StarterFuse|mechanical|starterFuse", "ElectricsFuse|mechanical|electricsFuse",
        "TractionMotorFuse|mechanical|tractionMotorFuse"
    };
    internal static void Reveal(HUDInterfacer owner)
    {
        if (!Main.Enabled || owner.currentCar == null || owner.currentHud == null ||
            !KeyboardInputFixer.IsCclLivery(owner.currentCar.carLivery) || owner.currentCar.loadedInterior == null) return;
        var manager = owner.currentCar.loadedInterior.GetComponent<InteriorControlsManager>();
        if (manager == null || !manager.Initialized) return;
        var revealed = new List<string>();
        foreach (string pair in Pairs)
        {
            string[] p = pair.Split('|');
            ControlType type = (ControlType)Enum.Parse(typeof(ControlType), p[0]);
            if (!manager.TryGetControl(type, out var r) || r.controlImplBase == null || r.scrollable == null) continue;
            object group = AccessTools.Field(owner.currentHud.GetType(), p[1])?.GetValue(owner.currentHud);
            var widget = group == null ? null : AccessTools.Field(group.GetType(), p[2])?.GetValue(group) as Component;
            if (widget == null)
            {
                Main.WarnOnce(owner.currentCar.carLivery.id + "/hud/" + type,
                    type + " is wired but this HUD has no corresponding widget.");
                continue;
            }
            if (!widget.gameObject.activeSelf)
            {
                widget.gameObject.SetActive(true);
                Main.MarkMutation();
                revealed.Add(type.ToString());
            }
        }
        if (revealed.Count > 0) Main.Logger.Log(owner.currentCar.carLivery.id +
            ": restored hidden HUD control(s): " + string.Join(", ", revealed.ToArray()));
    }
}
[HarmonyPatch(typeof(HUDInterfacer), "FireHUDChangedEvent")]
internal static class HUDVisibilityPatch
{
    private static void Prefix(HUDInterfacer __instance, bool on)
    {
        if (!on) return;
        try { HUDConnections.Reveal(__instance); }
        catch (Exception ex) { Main.Logger.Error("HUD connection repair failed: " + ex); }
    }
}
