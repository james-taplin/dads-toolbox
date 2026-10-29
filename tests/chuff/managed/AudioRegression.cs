using System;
using System.Linq;
using HarmonyLib;
using DV.ModularAudioCar;
using UnityEngine;
using UnityModManagerNet;
using ModMain = MultiCylinderChuffFix.Main;

public static partial class AudioRegression
{
    static int passed, failed, identity;
    static UnityModManager.ModEntry mod;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Near(float expected, float actual, string message) { Check(Math.Abs(expected-actual)<.00001f*Math.Max(1f,Math.Abs(expected)), message+": expected "+expected+", got "+actual); }
    static void Throws<T>(Action action) where T: Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected "+typeof(T).Name+" to propagate; no matching exception escaped.");
    }
    static void Run(string name, Action action)
    {
        NAudio.Reset();
        try { action(); passed++; Console.WriteLine("PASS "+name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL "+name+": "+ex); }
    }
    static ChuffClipsSimReader.OrderedChuffClips[] Bank(string label,int length)
    {
        return Enumerable.Range(0,length).Select(i=>new ChuffClipsSimReader.OrderedChuffClips { chuffVariations=new[] { new AudioClip(label+i) } }).ToArray();
    }
    static ChuffClipsSimReader Reader(int low=4,int medium=4,int high=4)
    {
        var r=new ChuffClipsSimReader { lowPressureClips=Bank("low",low), mediumPressureClips=Bank("medium",medium), highPressureClips=Bank("high",high) };
        r.FixtureCar=new TrainCar { carLivery=new Livery { id="fixture-car-"+(++identity) } };
        return r;
    }
    static void Played(ChuffClipsSimReader r,float input,AudioClip[] expected)
    {
        int before=NAudio.Calls.Count;
        r.Fire(input);
        Check(NAudio.Calls.Count==before+1,"Expected one regular playback; got "+(NAudio.Calls.Count-before));
        Check(object.ReferenceEquals(expected,NAudio.Calls[before].Clips),"Wrong bank/index selected for input "+input+" at pressure "+r.Pressure.Value);
    }
    static ChuffClipsSimReader.ChuffLoop Loop()
    {
        return new ChuffClipsSimReader.ChuffLoop { chuffLoop=new LayeredAudio(), chuffFrequencyToMasterVolume=new AnimationCurve(x=>.6f) };
    }
    public static int Main()
    {
        Console.WriteLine("Fixture integration: actual plugin source + real installed Harmony + native declarations extracted verbatim.");
        Console.WriteLine("CLR="+Environment.Version+"; OS="+Environment.OSVersion+"; 64-bit="+Environment.Is64BitProcess);
        Console.WriteLine("Unity audio/rendering/native Mathf implementation are stubbed. This is not gameplay or an audible timing test.");
        Run("unpatched native 3-cylinder index 4 reproduces IndexOutOfRangeException",()=>Throws<IndexOutOfRangeException>(()=>Reader().Fire(4f)));
        Run("unpatched native 6-cylinder index 11 reproduces IndexOutOfRangeException",()=>Throws<IndexOutOfRangeException>(()=>Reader().Fire(11f)));
        Run("unpatched native valid index plays exact clip array",()=> { var r=Reader(); Played(r,3f,r.lowPressureClips[3].chuffVariations); });
        mod=new UnityModManager.ModEntry();
        Run("actual Main.Load applies installed Harmony successfully",()=>Check(ModMain.Load(mod),"Main.Load returned false"));
        Run("Harmony registers one plugin prefix and no finalizer",()=> {
            var info=Harmony.GetPatchInfo(AccessTools.Method(typeof(ChuffClipsSimReader),"OnChuff",new[]{typeof(float)}));
            Check(info!=null && info.Prefixes.Count(p=>p.owner==mod.Info.Id)==1,"Actual plugin prefix missing/duplicated");
            Check(info.Finalizers.Count(p=>p.owner==mod.Info.Id)==0,"Global exception finalizer is still registered");
        });
        foreach(int cylinders in new[]{2,3,4,6})
        foreach(float pressure in new[]{5f,15f,25f})
        {
            int capturedCylinders=cylinders; float capturedPressure=pressure;
            Run(cylinders+" cylinders / "+(cylinders*2)+" events, pressure "+pressure+", copied four-entry bank",()=> {
                var r=Reader(); r.Pressure.Value=capturedPressure;
                var selected=capturedPressure==5f?r.lowPressureClips:capturedPressure==15f?r.mediumPressureClips:r.highPressureClips;
                int[] expected={0,1,2,3,0,1,2,3,0,1,2,3};
                for(int i=0;i<capturedCylinders*2;i++) Played(r,i,selected[expected[i]].chuffVariations);
            });
        }
        foreach(int length in new[]{4,6,12})
        {
            int captured=length;
            Run("native-valid "+length+"-entry selected bank keeps every existing index",()=> {
                var r=Reader(captured,1,2);
                for(int i=0;i<captured;i++) Played(r,i,r.lowPressureClips[i].chuffVariations);
            });
        }
        Run("low/medium/high threshold boundaries select native bank",()=> {
            var r=Reader(6,3,2);
            float[] inputs={1.0001f,9.999f,10f,19.999f,20f,20.001f};
            var banks=new[]{r.lowPressureClips,r.lowPressureClips,r.mediumPressureClips,r.mediumPressureClips,r.highPressureClips,r.highPressureClips};
            int[] expected={5,5,2,2,1,1};
            for(int i=0;i<inputs.Length;i++) { r.Pressure.Value=inputs[i]; Played(r,5f,banks[i][expected[i]].chuffVariations); }
        });
        Run("unequal unused banks do not truncate valid selected medium index",()=> {
            var r=Reader(1,4,2); r.Pressure.Value=15f; Played(r,3f,r.mediumPressureClips[3].chuffVariations);
        });
        Run("unequal unused banks do not truncate valid selected high index",()=> {
            var r=Reader(2,3,12); r.Pressure.Value=25f; Played(r,11f,r.highPressureClips[11].chuffVariations);
        });
        foreach(float pressure in new[]{5f,15f,25f})
        {
            float captured=pressure;
            Run("unused null and empty banks do not disable wrapping at pressure "+pressure,()=> {
                var r=Reader(); var selected=Bank("selected",4);
                if(captured==5f) { r.lowPressureClips=selected; r.mediumPressureClips=null; r.highPressureClips=new ChuffClipsSimReader.OrderedChuffClips[0]; }
                if(captured==15f) { r.lowPressureClips=new ChuffClipsSimReader.OrderedChuffClips[0]; r.mediumPressureClips=selected; r.highPressureClips=null; }
                if(captured==25f) { r.lowPressureClips=null; r.mediumPressureClips=new ChuffClipsSimReader.OrderedChuffClips[0]; r.highPressureClips=selected; }
                r.Pressure.Value=captured; Played(r,5f,selected[1].chuffVariations);
            });
        }
        Run("nearest-even midpoint and negative indices preserve native rounding before wrapping",()=> {
            var r=Reader();
            float[] inputs={0f,.49f,.5f,1.5f,2.5f,3.49f,-.5f,-1f,-1.5f,-2.5f,-3.5f,3.5f,4.5f,5.5f,7.5f};
            int[] expected={0,0,0,2,2,3,0,3,2,2,0,0,0,2,0};
            for(int i=0;i<inputs.Length;i++) Played(r,inputs[i],r.lowPressureClips[expected[i]].chuffVariations);
        });
        Run("regular playback parameters are retained after wrap",()=> {
            var r=Reader(); r.regularChuffConfig.pitch=.9f; r.regularChuffConfig.spread=47f; r.regularChuffConfig.minDistance=3f; r.regularChuffConfig.maxDistance=400f;
            r.regularChuffConfig.chuffFrequencyToMasterVolume=new AnimationCurve(x=>.8f);
            Played(r,5f,r.lowPressureClips[1].chuffVariations); var p=NAudio.Calls.Single();
            Near(.6f,p.Volume,"volume"); Near(.9f,p.Pitch,"pitch"); Near(47f,p.Spread,"spread"); Near(3f,p.MinDistance,"min distance"); Near(400f,p.MaxDistance,"max distance");
            Check(object.ReferenceEquals(r.regularChuffConfig.parent,p.Parent),"parent changed");
            Check(object.ReferenceEquals(r.regularChuffConfig.mixerGroup,p.Mixer),"mixer changed");
            Check(object.ReferenceEquals(r.regularChuffConfig.customCurves,p.Curves),"spatial curves changed");
            Near(1f,p.Position.x,"position x"); Near(2f,p.Position.y,"position y"); Near(3f,p.Position.z,"position z");
        });
        Run("native regular/water/ash playback and loop volume paths all remain",()=> {
            var r=Reader(); r.Water.Value=.6f; r.Cocks.Value=.4f; r.Ash.Value=.3f;
            r.waterChuffClips=new[]{new AudioClip("water")}; r.ashChuffClips=new[]{new AudioClip("ash")};
            r.waterChuffConfig.chuffFrequencyToMasterVolume=new AnimationCurve(x=>.7f);
            r.ashChuffConfig.chuffFrequencyToMasterVolume=new AnimationCurve(x=>.9f);
            var normal=Loop(); var water=Loop(); var ash=Loop();
            r.chuffLoops=new[]{normal}; r.waterChuffLoops=new[]{water}; r.ashChuffLoops=new[]{ash};
            r.Fire(11f);
            Check(NAudio.Calls.Count==3,"Expected regular, water, ash calls");
            Check(object.ReferenceEquals(r.lowPressureClips[3].chuffVariations,NAudio.Calls[0].Clips),"regular clip changed");
            Check(object.ReferenceEquals(r.waterChuffClips,NAudio.Calls[1].Clips),"water clip changed");
            Check(object.ReferenceEquals(r.ashChuffClips,NAudio.Calls[2].Clips),"ash clip changed");
            Near(.448f,NAudio.Calls[1].Volume,"water volume"); Near(.27f,NAudio.Calls[2].Volume,"ash volume");
            Check(r.AnyLoop,"native anyLoopOngoing was lost");
            Near(4f,normal.chuffLoop.Volume,"regular loop pressure input"); Near(.64f,water.chuffLoop.Volume,"water loop input"); Near(.3f,ash.chuffLoop.Volume,"ash loop input");
            foreach(var loop in new[]{normal,water,ash}) { Near(.6f,loop.chuffLoop.MasterVolume,"master loop volume"); Near(2f,loop.chuffLoop.Pitch,"loop pitch"); Check(loop.chuffLoop.Stops==1 && loop.chuffLoop.VolumeCalls==1,"native loop activation/update lost"); }
        });
        foreach(float pressure in new[]{-1f,0f,1f,float.NaN})
        {
            float captured=pressure;
            Run("native pressure gate retains water/ash and avoids invalid regular banks at "+pressure,()=> {
                var r=Reader(); r.Pressure.Value=captured; r.lowPressureClips=null; r.mediumPressureClips=null; r.highPressureClips=null;
                r.Water.Value=.5f; r.Ash.Value=.2f; r.waterChuffClips=new[]{new AudioClip("water")}; r.ashChuffClips=new[]{new AudioClip("ash")};
                int logCount=mod.Logger.Logs.Count; r.Fire(11f);
                Check(NAudio.Calls.Count==2,"Pressure gate should skip only regular playback");
                Check(mod.Logger.Logs.Count==logCount,"Pressure-gated index should not log a wrap");
            });
        }
        Run("native regular-frequency curve gate runs loops first and suppresses all one-shots",()=> {
            var r=Reader(); var loop=Loop(); r.chuffLoops=new[]{loop}; r.regularChuffConfig.chuffFrequencyToMasterVolume=new AnimationCurve(x=>0f);
            r.lowPressureClips=null; r.Water.Value=.5f; r.Ash.Value=.2f; r.waterChuffClips=new[]{new AudioClip("water")}; r.ashChuffClips=new[]{new AudioClip("ash")};
            r.Fire(11f); Check(NAudio.Calls.Count==0,"Frequency curve gate lost"); Check(loop.chuffLoop.VolumeCalls==1,"Loops did not update before gate");
        });
        Run("native water frequency gate at 0.1 retains zero-volume water call",()=> {
            var r=Reader(); r.Frequency.Value=.1f; r.Water.Value=.5f; r.waterChuffClips=new[]{new AudioClip("water")}; r.Fire(5f);
            Check(NAudio.Calls.Count==2,"Expected native regular and zero-volume water call"); Near(0f,NAudio.Calls[1].Volume,"water frequency gate");
        });
        Run("water and ash individual frequency curves still gate their own playback",()=> {
            var r=Reader(); r.Water.Value=.5f; r.Ash.Value=.3f; r.waterChuffClips=new[]{new AudioClip("water")}; r.ashChuffClips=new[]{new AudioClip("ash")};
            r.waterChuffConfig.chuffFrequencyToMasterVolume=new AnimationCurve(x=>0f); r.ashChuffConfig.chuffFrequencyToMasterVolume=new AnimationCurve(x=>-1f);
            Played(r,5f,r.lowPressureClips[1].chuffVariations);
        });
        foreach(float pressure in new[]{5f,15f,25f})
        {
            float captured=pressure;
            Run("selected null bank retains native NullReferenceException at pressure "+pressure,()=> {
                var r=Reader(); r.Pressure.Value=captured;
                if(captured==5f) r.lowPressureClips=null; else if(captured==15f) r.mediumPressureClips=null; else r.highPressureClips=null;
                Throws<NullReferenceException>(()=>r.Fire(5f));
            });
            Run("selected empty bank retains native IndexOutOfRangeException at pressure "+pressure,()=> {
                var r=Reader(); r.Pressure.Value=captured; var empty=new ChuffClipsSimReader.OrderedChuffClips[0];
                if(captured==5f) r.lowPressureClips=empty; else if(captured==15f) r.mediumPressureClips=empty; else r.highPressureClips=empty;
                Throws<IndexOutOfRangeException>(()=>r.Fire(5f));
            });
            Run("selected null bank entry retains native NullReferenceException at pressure "+pressure,()=> {
                var r=Reader(); r.Pressure.Value=captured;
                if(captured==5f) r.lowPressureClips[1]=null; else if(captured==15f) r.mediumPressureClips[1]=null; else r.highPressureClips[1]=null;
                Throws<NullReferenceException>(()=>r.Fire(5f));
            });
        }
        Run("null pressure port retains native error after prefix null guard",()=> { var r=Reader(); r.Pressure=null; Throws<NullReferenceException>(()=>r.Fire(5f)); });
        Run("missing regular curve propagates native configuration error",()=> { var r=Reader(); r.regularChuffConfig.chuffFrequencyToMasterVolume=null; Throws<NullReferenceException>(()=>r.Fire(5f)); });
        Run("unrelated pressure volume curve exception propagates same instance",()=> {
            var r=Reader(); var expected=new InvalidOperationException("fixture curve failure"); r.pressureToVolumeCurve=new AnimationCurve(x=>{throw expected;});
            try { r.Fire(5f); } catch(InvalidOperationException ex) { Check(object.ReferenceEquals(expected,ex),"Exception identity changed"); return; }
            throw new Exception("Unrelated curve exception was suppressed");
        });
        Run("unrelated NAudio playback exception propagates same instance",()=> {
            var r=Reader(); var expected=new ApplicationException("fixture playback failure"); NAudio.InjectedException=expected;
            try { r.Fire(5f); } catch(ApplicationException ex) { Check(object.ReferenceEquals(expected,ex),"Exception identity changed"); return; }
            throw new Exception("Unrelated playback exception was suppressed");
        });
        Run("null regular parent propagates native configuration error",()=> { var r=Reader(); r.regularChuffConfig.parent=null; Throws<NullReferenceException>(()=>r.Fire(5f)); });
        Run("native loop exception propagates before one-shot playback",()=> {
            var r=Reader(); r.chuffLoops=new ChuffClipsSimReader.ChuffLoop[]{null}; Throws<NullReferenceException>(()=>r.Fire(5f)); Check(NAudio.Calls.Count==0,"Playback ran despite failed loop");
        });
        Run("wrap logs once per car type across reader instances, separate car types log separately",()=> {
            ModMain.Reported.Clear(); mod.Logger.Logs.Clear(); var a=Reader(); var b=Reader(); b.FixtureCar.carLivery.id=a.FixtureCar.carLivery.id;
            Played(a,3f,a.lowPressureClips[3].chuffVariations); Check(mod.Logger.Logs.Count==0,"Native-valid index logged wrap");
            Played(a,4f,a.lowPressureClips[0].chuffVariations); Played(a,5f,a.lowPressureClips[1].chuffVariations); Played(b,11f,b.lowPressureClips[3].chuffVariations);
            Check(mod.Logger.Logs.Count==1,"Repeated same-car wrap logs were not bounded");
            var c=Reader(); Played(c,6f,c.lowPressureClips[2].chuffVariations); Check(mod.Logger.Logs.Count==2,"Separate car type did not get its first log");
        });
        Run("root-name car logging fallback works without TrainCar",()=> { var r=Reader(); r.FixtureCar=null; r.transform.name="fixture-root-fallback"; Played(r,5f,r.lowPressureClips[1].chuffVariations); Check(mod.Logger.Logs.Last().StartsWith("fixture-root-fallback:"),"Root-name fallback missing"); });
        RunAdaptation();
        Console.WriteLine("RESULT passed="+passed+" failed="+failed+" total="+(passed+failed));
        Console.WriteLine("UMM warnings="+mod.Logger.Warnings.Count+" errors="+mod.Logger.Errors.Count);
        return failed==0?0:1;
    }
}
