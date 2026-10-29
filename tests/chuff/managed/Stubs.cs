using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object obj) { return !ReferenceEquals(obj,null) && !obj.Destroyed; }
        public static bool operator ==(Object a,Object b)
        {
            if(ReferenceEquals(a,b)) return true;
            if(ReferenceEquals(a,null)) return b.Destroyed;
            if(ReferenceEquals(b,null)) return a.Destroyed;
            return false;
        }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object value) { return value is Object && this==(Object)value; }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object obj) { if(obj!=null) obj.Destroyed=true; }
        public int GetInstanceID() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    public class Transform : Object { public string name = "fixture-root"; public Transform root { get { return this; } } public Vector3 position = new Vector3(1f, 2f, 3f); }
    public class Component : Object
    {
        public Transform transform = new Transform(); public TrainCar FixtureCar;
        public GameObject gameObject = new GameObject();
        public T GetComponentInParent<T>() where T : class { return FixtureCar as T; }
        public void StopCoroutine(Coroutine value) { }
    }
    public struct Scene { public bool IsValid() { return true; } }
    public class GameObject { public Scene scene; public string GetPath() { return "fixture/audio-reader"; } }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() { return new T[0]; } }
    public class Coroutine { }
    public enum AudioClipLoadType { DecompressOnLoad, CompressedInMemory, Streaming }
    public enum AudioDataLoadState { Unloaded, Loading, Loaded, Failed }
    public class AudioSource : Object
    {
        public float pitch = 1f, volume; public AudioClip clip; public int timeSamples; public bool isPlaying; public int Plays;
        public bool loop=true;
        public void Play() { isPlaying=true; Plays++; }
        public void Stop() { isPlaying=false; }
    }
    public static class Debug
    {
        public static readonly List<string> Errors = new List<string>();
        public static void LogError(string value) { Errors.Add(value); }
    }
    public struct Vector3 { public float x,y,z; public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; } }
    public class AudioClip : Object
    {
        public static int MaximumCreateFrequency=int.MaxValue;
        public static readonly List<int> CreateAttempts=new List<int>();
        public static void ResetFixture() { MaximumCreateFrequency=int.MaxValue; CreateAttempts.Clear(); }
        public string name; public int samples, channels, frequency;
        public AudioClipLoadType loadType=AudioClipLoadType.DecompressOnLoad;
        public AudioDataLoadState loadState=AudioDataLoadState.Loaded;
        public bool ReadSucceeds=true, WriteSucceeds=true;
        public float[] Data;
        public AudioClip(string name) : this(name,16,1,44100) { }
        public AudioClip(string name,int samples,int channels,int frequency) { this.name=name; this.samples=samples; this.channels=channels; this.frequency=frequency; Data=new float[samples*channels]; }
        public float length { get { return (float)samples/frequency; } }
        public bool LoadAudioData() { return loadState==AudioDataLoadState.Loaded; }
        public static AudioClip Create(string name,int lengthSamples,int channels,int frequency,bool stream) { CreateAttempts.Add(frequency); return frequency>MaximumCreateFrequency?null:new AudioClip(name,lengthSamples,channels,frequency); }
        public bool GetData(float[] data,int offsetSamples) { if(!ReadSucceeds) return false; Array.Copy(Data,offsetSamples*channels,data,0,data.Length); return true; }
        public bool SetData(float[] data,int offsetSamples) { if(!WriteSucceeds) return false; Array.Copy(data,0,Data,offsetSamples*channels,data.Length); return true; }
    }
    public enum WrapMode { Default=0, Once=1, Clamp=1, Loop=2, PingPong=4, ClampForever=8 }
    public enum WeightedMode { None=0, In=1, Out=2, Both=3 }
    public struct Keyframe
    {
        public float time,value,inTangent,outTangent,inWeight,outWeight; public WeightedMode weightedMode;
        public Keyframe(float time,float value) : this(time,value,0f,0f) { }
        public Keyframe(float time,float value,float inTangent,float outTangent) { this.time=time; this.value=value; this.inTangent=inTangent; this.outTangent=outTangent; inWeight=outWeight=0f; weightedMode=WeightedMode.None; }
    }
    public class AnimationCurve
    {
        private readonly Func<float,float> fn; private Keyframe[] frames=new Keyframe[0]; public int Calls; public float LastInput;
        public AnimationCurve(Func<float,float> fn) { this.fn = fn; }
        public AnimationCurve(params Keyframe[] keys) { this.keys=keys; }
        public Keyframe[] keys { get { return (Keyframe[])frames.Clone(); } set { frames=value==null?new Keyframe[0]:(Keyframe[])value.Clone(); Array.Sort(frames,(a,b)=>a.time.CompareTo(b.time)); } }
        public WrapMode preWrapMode=WrapMode.ClampForever, postWrapMode=WrapMode.ClampForever;
        public int length { get { return frames.Length; } }
        public static AnimationCurve Linear(float timeStart,float valueStart,float timeEnd,float valueEnd) { float slope=(valueEnd-valueStart)/(timeEnd-timeStart); return new AnimationCurve(new Keyframe(timeStart,valueStart,slope,slope),new Keyframe(timeEnd,valueEnd,slope,slope)); }
        public float Evaluate(float input)
        {
            Calls++; LastInput=input; if(fn!=null) return fn(input); if(frames.Length==0) return 0f;
            if(input<=frames[0].time) return frames[0].value; if(input>=frames[frames.Length-1].time) return frames[frames.Length-1].value;
            for(int i=1;i<frames.Length;i++) { var a=frames[i-1]; var b=frames[i]; if(input>b.time) continue;
                if(a.weightedMode!=WeightedMode.None || b.weightedMode!=WeightedMode.None) throw new NotSupportedException("Fixture supports unweighted native curves only.");
                if(float.IsInfinity(a.outTangent)||float.IsInfinity(b.inTangent)) return a.value;
                float duration=b.time-a.time,t=(input-a.time)/duration,t2=t*t,t3=t2*t;
                return (2*t3-3*t2+1)*a.value+(t3-2*t2+t)*duration*a.outTangent+(-2*t3+3*t2)*b.value+(t3-t2)*duration*b.inTangent;
            }
            throw new InvalidOperationException("Curve adapter could not select segment.");
        }
    }
    public static class Mathf
    {
        // Unity's documented RoundToInt tie rule is nearest even. Actual Unity native binary is not executed here.
        public static int RoundToInt(float value) { return (int)Math.Round(value, MidpointRounding.ToEven); }
        public static float Max(float a,float b) { return Math.Max(a,b); }
        public static float Min(float a,float b) { return Math.Min(a,b); }
        public static float Abs(float value) { return Math.Abs(value); }
        public static float Clamp(float value,float min,float max) { return Math.Max(min,Math.Min(max,value)); }
        public static bool Approximately(float a,float b) { return Math.Abs(a-b)<.000001f*Math.Max(1f,Math.Max(Math.Abs(a),Math.Abs(b))); }
        public static int CeilToInt(float value) { return (int)Math.Ceiling(value); }
        public static float Lerp(float a,float b,float t) { t=Math.Max(0f,Math.Min(1f,t)); return a+(b-a)*t; }
    }
}
namespace UnityEngine.Audio { public class AudioMixerGroup {} }
namespace DV.DopplerEffects
{
    public class Doppler : UnityEngine.Object { public float DesiredPitch; public float desiredPitch { get { return DesiredPitch; } } public void SetDesiredPitch(float value) { DesiredPitch = value; } }
}
namespace LocoSim.Implementations
{
    public class Port
    {
        public readonly string id; public float Value;
        public event Action<float> ValueUpdatedInternally;
        public int Subscribers { get { return ValueUpdatedInternally == null ? 0 : ValueUpdatedInternally.GetInvocationList().Length; } }
        public Port(float value) : this("fixture-port", value) { }
        public Port(string id, float value=0f) { this.id=id; Value=value; }
        public void Emit(float value) { float prior=Value; Value=value; if(value!=prior && ValueUpdatedInternally!=null) ValueUpdatedInternally(value); }
    }
    public class SimComponent
    {
        public string id; public Action<float> TickAction;
        public SimComponent(string id) { this.id=id; }
        public virtual void Tick(float delta) { if(TickAction!=null) TickAction(delta); }
    }
    public class ReciprocatingSteamEngine : SimComponent
    {
        public int numCylinders;
        private readonly int chuffsPerCycle;
        public readonly Port chuffEventReadOut, chuffFrequencyReadOut;
        public ReciprocatingSteamEngine(string id, int count) : base(id)
        {
            numCylinders=count; chuffsPerCycle=count*2;
            chuffEventReadOut=new Port(id+".CHUFF_EVENT"); chuffFrequencyReadOut=new Port(id+".CHUFF_FREQUENCY");
        }
    }
    public class SimulationFlow
    {
        public readonly IReadOnlyList<SimComponent> OrderedSimComps;
        public readonly Dictionary<string,Port> Ports = new Dictionary<string,Port>();
        public event Action TickEvent;
        public int TickSubscribers { get { return TickEvent==null?0:TickEvent.GetInvocationList().Length; } }
        public SimulationFlow(params SimComponent[] components)
        {
            OrderedSimComps=components;
            foreach(var c in components) { var e=c as ReciprocatingSteamEngine; if(e!=null) { Add(e.chuffEventReadOut); Add(e.chuffFrequencyReadOut); } }
        }
        public void Add(Port value) { Ports[value.id]=value; }
        public bool TryGetPort(string id, out Port value, bool canBeNullOrEmpty=false) { value=null; return id!=null && Ports.TryGetValue(id,out value); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Tick(float delta) { foreach(var component in OrderedSimComps) component.Tick(delta); if(TickEvent!=null) TickEvent(); }
    }
}
public class OptionalWheelslip
{
    public WheelslipController Value;
    public bool IsSome(out WheelslipController value) { value=Value; return value!=null; }
}
public class WheelslipController { public event Action<bool> WheelslipStateChanged { add { } remove { } } }
public class AdhesionController
{
    public OptionalWheelslip wheelslipController=new OptionalWheelslip();
    public event Action<bool> WheelSlideStateChanged { add { } remove { } }
}
public class FixtureSimController : UnityEngine.Object { public LocoSim.Implementations.SimulationFlow simFlow; }
public class TrainCar : UnityEngine.Object
{
    public Livery carLivery; public AdhesionController adhesionController; public FixtureSimController SimController;
    public event Action<bool> MovementStateChanged { add { } remove { } }
}
namespace DadsToolbox.LocoSound
{
    // Sound ownership is exercised against the real DLL in the Unity suite.
    internal static class SoundEngine
    {
        internal static void SuspendRhythmicPitch(LayeredAudio.Layer layer) { }
        internal static void ResumeRhythmicPitch(LayeredAudio.Layer layer, bool refreshPitch) { }
    }
}
public class Livery { public string id; }
public class AudioSourceCurves { public AudioSourceCurves() { } public AudioSourceCurves(object a, object b, object c, object d) { } }
namespace DV.ModularAudioCar
{
    public class AAudioClipSimReader : UnityEngine.Component
    {
        public virtual void Init(TrainCar car, LocoSim.Implementations.SimulationFlow simFlow) { }
        public virtual void Deinit() { }
    }
}
public class LayeredAudio : UnityEngine.Object
{
    public class Layer : NativeLayerPitchAdapter { public string name; public UnityEngine.AnimationCurve volumeCurve; }
    public Layer[] layers=new Layer[0]; public bool linearPitchLerp; public float minPitch=1f,maxPitch=1f;
    public float MasterVolume, Volume, Pitch; public int Stops, VolumeCalls, PitchCalls;
    private bool isInited;
    public int ResetCalls;
    public void Reset() { ResetCalls++; foreach(var layer in layers) { layer.source.volume=0f; if(!isInited) layer.startPitch=layer.source.pitch; } isInited=true; }
    public void Stop() { Stops++; }
    public void SetVolume(float value) { Volume = value; VolumeCalls++; }
    public void SetPitch(float value) { if(!isInited) Reset(); Pitch = value; PitchCalls++; foreach(var layer in layers) layer.SetPitch(value,linearPitchLerp,minPitch,maxPitch); }
}
public class Playback
{
    public UnityEngine.AudioClip[] Clips; public UnityEngine.Vector3 Position;
    public float Volume,Pitch,Spread,MinDistance,MaxDistance;
    public UnityEngine.Audio.AudioMixerGroup Mixer; public UnityEngine.Transform Parent; public AudioSourceCurves Curves;
}
public static class NAudio
{
    public static readonly List<Playback> Calls = new List<Playback>(); public static Exception InjectedException;
    public static void Reset() { Calls.Clear(); InjectedException=null; UnityEngine.AudioClip.ResetFixture(); }
    public static void Play(UnityEngine.Vector3 position, float volume, float pitch, float spread, float minDistance, float maxDistance,
        UnityEngine.Audio.AudioMixerGroup mixerGroup, UnityEngine.Transform parent, UnityEngine.AudioClip[] clips, AudioSourceCurves curves)
    {
        if (InjectedException!=null) throw InjectedException;
        Calls.Add(new Playback { Clips=clips, Position=position, Volume=volume, Pitch=pitch, Spread=spread, MinDistance=minDistance, MaxDistance=maxDistance, Mixer=mixerGroup,Parent=parent,Curves=curves });
    }
}
public static class ClipExtensions
{
    public static void Play(this UnityEngine.AudioClip[] clips, UnityEngine.Vector3 position, float volume, float pitch, float spread, float minDistance, float maxDistance,
        UnityEngine.Audio.AudioMixerGroup mixerGroup, UnityEngine.Transform parent, AudioSourceCurves curves)
    { NAudio.Play(position,volume,pitch,spread,minDistance,maxDistance,mixerGroup,parent,clips,curves); }
}
namespace UnityModManagerNet
{
    public static class UnityModManager
    {
        public class ModEntry
        {
            public ModInfo Info = new ModInfo(); public ModLogger Logger = new ModLogger();
            public class ModInfo { public string Id="fixture.multicylinderchufffix"; }
            public class ModLogger
            {
                public readonly List<string> Logs=new List<string>(), Warnings=new List<string>(), Errors=new List<string>();
                public void Log(string value) { Logs.Add(value); }
                public void Warning(string value) { Warnings.Add(value); }
                public void Error(string value) { Errors.Add(value); }
            }
        }
    }
}
