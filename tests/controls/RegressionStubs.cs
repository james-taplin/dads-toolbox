// Explicit managed stand-ins. They do not load Unity, Harmony or Rewired.
// The harness compiles and executes unchanged production source to test its
// selection, scheduling, dispatch and mutation logic; no live game claim follows.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace UnityEngine {
 public static class Time {public static int frameCount;}
 public static class Debug {public static void LogError(string text)=>throw new InvalidOperationException(text);}
 public class Object { public static implicit operator bool(Object value)=>value!=null; }
 public class Component:Object {
  public GameObject gameObject;
  public bool enabled=true;
  public Transform transform=>new Transform{gameObject=gameObject};
  public string name=>gameObject.name;
  public T GetComponent<T>()=>gameObject.GetComponent<T>();
  public Component GetComponent(Type type)=>gameObject.GetComponent(type);
  public T[] GetComponents<T>()=>gameObject.GetComponents<T>();
  public T[] GetComponentsInChildren<T>(bool includeInactive=false)=>gameObject.GetComponentsInChildren<T>(includeInactive);
 }
 public class MonoBehaviour:Component {}
 public class HingeJoint:Component {}
 public class Transform {public GameObject gameObject;}
 public class GameObject:Object {
  static int nextId; readonly int id=++nextId;
  public string name; public GameObject parent;public bool activeSelf=true;public bool activeInHierarchy=>activeSelf&&(parent==null||parent.activeInHierarchy);
  readonly List<Component> components=new(); public readonly List<GameObject> children=new();
  public GameObject(string name="object"){this.name=name;}
  public int GetInstanceID()=>id;
  public void SetActive(bool value)=>activeSelf=value;
  public T AddComponent<T>() where T:Component,new()=> (T)AddComponent(typeof(T));
  public Component AddComponent(Type type){var item=(Component)Activator.CreateInstance(type);item.gameObject=this;components.Add(item);return item;}
  public T GetComponent<T>()=>components.OfType<T>().FirstOrDefault();
  public Component GetComponent(Type type)=>components.FirstOrDefault(type.IsInstanceOfType);
  public T[] GetComponents<T>()=>components.OfType<T>().ToArray();
  public T[] GetComponentsInChildren<T>(bool includeInactive=false)=> (!includeInactive && !activeSelf) ? Array.Empty<T>() : components.OfType<T>().Concat(children.SelectMany(c=>c.GetComponentsInChildren<T>(includeInactive))).ToArray();
  public T GetComponentInChildren<T>(bool includeInactive=false)=>GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
 }
 public static class Mathf { public static float Abs(float v)=>Math.Abs(v); public static float Round(float v)=>(float)Math.Round(v); }
}
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch:Attribute {public HarmonyPatch(Type type,string member){} public HarmonyPatch(Type type,string member,Type[] parameters){} }
 public class Harmony {public Harmony(string id){}public void PatchAll(Assembly a){} public void UnpatchAll(string id){} }
 public static class AccessTools { public static FieldInfo Field(Type t,string n)=>t.GetField(n,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); public static PropertyInfo Property(Type t,string n)=>t.GetProperty(n,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); }
}
namespace UnityModManagerNet {
 public static class UnityModManager {
  public class ModEntry { public class ModLogger {public readonly List<string> messages=new();public void Log(string s)=>messages.Add(s);public void Error(string s)=>messages.Add("ERROR "+s);} public class ModInfo{public string Id="DVCCLControlFix";} public ModLogger Logger=new();public ModInfo Info=new(); public Func<ModEntry,bool,bool> OnToggle; }
 }
}
public class TrainCar:UnityEngine.Component {public DV.ThingTypes.TrainCarLivery carLivery;public UnityEngine.GameObject loadedInterior;public static readonly List<TrainCar> All=new(); public static TrainCar Resolve(UnityEngine.Transform t)=>All.FirstOrDefault(c=>c.carLivery!=null&&new[]{c.carLivery.interiorPrefab,c.carLivery.explodedInteriorPrefab,c.carLivery.externalInteractablesPrefab,c.carLivery.explodedExternalInteractablesPrefab}.Any(p=>Contains(p,t.gameObject)));static bool Contains(UnityEngine.GameObject p,UnityEngine.GameObject child)=>p!=null&&(p==child||p.children.Any(c=>Contains(c,child)));}
public enum ScrollAction {ScrollUp,ScrollDown,Release} public enum ScrollSource {Mouse,HUD}
namespace DV.ThingTypes { public class TrainCarLivery:UnityEngine.Object { public string id;public UnityEngine.GameObject prefab,interiorPrefab,explodedInteriorPrefab,externalInteractablesPrefab,explodedExternalInteractablesPrefab; } }
namespace CCL.Test {public class CustomLivery:DV.ThingTypes.TrainCarLivery{} }
namespace LocoSim.Implementations { public class SimulationFlow{} }
namespace DV.CabControls.Spec {
 public class ControlSpec:UnityEngine.Component {}
 public interface INotchedSpec {bool IsNotched{get;}int NotchCount{get;} }
 public class Lever:ControlSpec,INotchedSpec {public bool useSteppedJoint=true,useLimits=true,useSpring=true,invertDirection;public float jointSpring=100f;public float scrollWheelHoverScroll=0.025f;public int notches=4;public bool IsNotched=>useSteppedJoint;public int NotchCount=>notches;}
}
namespace DV.CabControls {
 public class ControlImplBase:UnityEngine.Component {
  public bool InteractionAllowed=true,grabbed;public float Value;public int setCalls;public readonly List<string> Operations=new();public event Action<float> Changed;public bool IsGrabbed()=>grabbed;
  // Audited ControlImplBase publishes ValueChanged BEFORE AcceptSetValue.
  public void SetValue(float v){float old=Value;Value=Math.Clamp(v,0,1);setCalls++;if(Value!=old){Operations.Add("ValueChanged");Changed?.Invoke(Value);}Operations.Add("AcceptSetValue");AcceptSetValue(Value);}
  protected virtual void AcceptSetValue(float v){}
 }
 // Simplified normalized scroll contract from audited LeverBase. This models
 // direction only; actual hinge angles, stepped joints and physics are absent.
 public abstract class LeverBase:ControlImplBase,DV.HUD.IScrollable {
  public bool IsScrollingBlocked{get;set;}public bool InvertScrollingDirection{get;set;}
  public int ReleaseCalls;public float SpringTarget;
  protected override void AcceptSetValue(float v){SpringTarget=v;}
  public void Scroll(ScrollAction action,ScrollSource source=ScrollSource.Mouse){
   var spec=GetComponent<DV.CabControls.Spec.Lever>();
   if(action==ScrollAction.Release){
    if(spec!=null&&GetComponent<UnityEngine.HingeJoint>()==null)throw new InvalidOperationException("release before lever joint initialization");
    Operations.Add("Release");ReleaseCalls++;SpringTarget=0;return;
   }
   if(IsScrollingBlocked)return;
   // spec.invertDirection affects both joint rotation and normalized reading,
   // so cancels in normalized Value. Signed scroll and runtime inversion remain.
   int direction=action==ScrollAction.ScrollUp?1:-1;
   if(spec!=null&&spec.scrollWheelHoverScroll<0)direction=-direction;
   if(InvertScrollingDirection)direction=-direction;
   if(spec!=null&&spec.scrollWheelHoverScroll==0)return;
   SetValue(Value+direction*.05f);
  }
  public bool IsAtEnd(ScrollAction a)=>false;
 }
 public class TestControl:LeverBase {}
 public class InteractablesKeyboardControl:UnityEngine.Component {public DV.KeyboardInput.AKeyboardInput[] entries; }
}
namespace DV.Simulation.Controllers {
 public class OverridableBaseControl:UnityEngine.Component {public string portId;public bool IsControlBlocked;}
 public class ThrottleControl:OverridableBaseControl{} public class ReverserControl:OverridableBaseControl{} public class BrakeControl:OverridableBaseControl{} public class IndependentBrakeControl:OverridableBaseControl{} public class BrakeCutoutControl:OverridableBaseControl{} public class SanderControl:OverridableBaseControl{} public class HornControl:OverridableBaseControl{} public class DynamoControl:OverridableBaseControl{} public class AirPumpControl:OverridableBaseControl{} public class DynamicBrakeControl:OverridableBaseControl{}
 public class InteractablePortFeedersController:UnityEngine.Component {public DV.Simulation.Ports.InteractablePortFeeder[] entries;}
}
namespace DV.Simulation.Ports {public class InteractablePortFeeder:UnityEngine.Component {public string portId;} }
namespace DV.Simulation.Brake { public class HandbrakeFeeder:UnityEngine.Component{} public class HandbrakeFeedersController:UnityEngine.Component {public HandbrakeFeeder[] entries;} }
namespace DV.Simulation.Cars {
 public class BaseControlsOverrider:UnityEngine.Component {
  public DV.Simulation.Controllers.OverridableBaseControl throttle,reverser,brake,independentBrake,brakeCutout,sander,horn,dynamo,airPump,dynamicBrake,headlightsFront,headlightsRear,cabLight,wipers;
  public DV.Simulation.Controllers.OverridableBaseControl Throttle=>throttle; public DV.Simulation.Controllers.OverridableBaseControl Reverser=>reverser; public DV.Simulation.Controllers.OverridableBaseControl Brake=>brake; public DV.Simulation.Controllers.OverridableBaseControl IndependentBrake=>independentBrake; public DV.Simulation.Controllers.OverridableBaseControl BrakeCutout=>brakeCutout; public DV.Simulation.Controllers.OverridableBaseControl Sander=>sander; public DV.Simulation.Controllers.OverridableBaseControl Horn=>horn; public DV.Simulation.Controllers.OverridableBaseControl Dynamo=>dynamo; public DV.Simulation.Controllers.OverridableBaseControl AirPump=>airPump; public DV.Simulation.Controllers.OverridableBaseControl DynamicBrake=>dynamicBrake; public DV.Simulation.Controllers.OverridableBaseControl HeadlightsFront=>headlightsFront; public DV.Simulation.Controllers.OverridableBaseControl HeadlightsRear=>headlightsRear; public DV.Simulation.Controllers.OverridableBaseControl CabLight=>cabLight; public DV.Simulation.Controllers.OverridableBaseControl Wipers=>wipers;
 }
}
namespace DV.HUD {
 public interface IScrollable {void Scroll(ScrollAction action,ScrollSource source=ScrollSource.Mouse);bool IsAtEnd(ScrollAction action);}
 public class ScrollableTimerUtil {public class FloatHolder{public float value;}public Dictionary<IScrollable,FloatHolder> timerDictionary=new();public void Advance(float delta){foreach(var pair in timerDictionary.ToArray()){pair.Value.value-=delta;if(pair.Value.value<0){pair.Key.Scroll(ScrollAction.Release,ScrollSource.HUD);timerDictionary.Remove(pair.Key);}}}}
 public class InteriorControlsManager:UnityEngine.Component {
  public enum ControlType {None,Throttle,TrainBrake,Reverser,IndBrake,Handbrake,Sander,Horn,HeadlightsFront,HeadlightsRear,StarterFuse,ElectricsFuse,TractionMotorFuse,StarterControl,DynamicBrake,CabLight,Wipers,FuelCutoff,ReleaseCyl,IndHeadlightsTypeFront,IndHeadlights1Front,IndHeadlights2Front,IndHeadlightsTypeRear,IndHeadlights1Rear,IndHeadlights2Rear,IndWipers1,IndWipers2,IndCabLight,IndDashLight,GearboxA,GearboxB,CylCock,Injector,Firedoor,Blower,Damper,Blowdown,CoalDump,Dynamo,AirPump,Lubricator,Bell,TrainBrakeCutout}
  public struct ControlReference {public DV.CabControls.ControlImplBase controlImplBase;public IScrollable scrollable;public DV.Simulation.Controllers.OverridableBaseControl overridableBaseControl;}
  public TrainCar Car;public bool Initialized=true;public ScrollableTimerUtil scrollableTimerUtil=new(); public List<ControlType> reverseDirectionList=new();private Dictionary<ControlType,ControlReference> controls=new();public event Action<InteriorControlsManager> OnInitialized;
  public bool TryGetControl(ControlType t,out ControlReference r)=>controls.TryGetValue(t,out r);
  public void Register(ControlType t,DV.CabControls.TestControl c,DV.Simulation.Controllers.OverridableBaseControl b=null)=>controls[t]=new ControlReference{controlImplBase=c,scrollable=c,overridableBaseControl=b};
  public void FireInitialized()=>OnInitialized?.Invoke(this);
 }
 public class LocoControlsReader:UnityEngine.Component {public UnityEngine.GameObject throttle,trainBrake,reverser,indBrake,handbrake,sander,horn,headlightsFront,headlightsRear,dynamicBrake,cabLight,wipers,cylCock,injector,firedoor,blower,damper,blowdown,coalDump,dynamo,airPump,lubricator,bell,trainBrakeCutout;}
 public class HUDInterfacer {public TrainCar currentCar;public object currentHud;}
}
namespace DV.UI.LocoHUD { }
namespace DV.RailDriver {
 public static class RailDriver {public enum DisplayBuffer {UP,DN,ON,OFF}}
 public static class RailDriverDisplayDV {public static void DisplayNotification(RailDriver.DisplayBuffer value){}}
}
namespace DV.Interaction.Inputs {
 public static class InputManager {public static Player NewPlayer=new();public static class Actions{public static readonly int Run="Run".GetHashCode();}}
 public class Player {public readonly HashSet<int> plus=new(),minus=new(),down=new();public bool GetButton(int i)=>plus.Contains(i);public bool GetNegativeButton(int i)=>minus.Contains(i);public bool GetButtonDown(int i)=>down.Contains(i);public void Reset(){plus.Clear();minus.Clear();down.Clear();} }
}
namespace DV.KeyboardInput {
 public abstract class AKeyboardInput:UnityEngine.Component {
  public bool reachable=true;public bool PlayerCanReach()=>reachable;
  public class ActionReference {public string name;public bool flip;public DV.Simulation.Controllers.OverridableBaseControl overridableBaseControl;public int id=>name?.GetHashCode()??0;public bool CanMoveOverridableBaseControl=>overridableBaseControl==null||!overridableBaseControl.IsControlBlocked;public void Initialize(DV.HUD.InteriorControlsManager m){} }
  public abstract bool FixedUpdateTick{get;} public abstract void SetupActions(DV.HUD.InteriorControlsManager m);public abstract void Tick(float dt);
 }
}

