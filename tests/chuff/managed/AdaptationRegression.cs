using System;
using System.Linq;
using DV.ModularAudioCar;
using HarmonyLib;
using LocoSim.Implementations;
using UnityEngine;

public static partial class AudioRegression
{
    sealed class LocoFixture
    {
        public ChuffClipsSimReader Reader;
        public ReciprocatingSteamEngine Engine;
        public SimulationFlow Flow;
        public Port AuthoredFrequency;
        public LocoFixture(int cylinders, string engineId="arbitraryValveGear")
        {
            Reader=AudioRegression.Reader();
            Engine=new ReciprocatingSteamEngine(engineId,cylinders);
            Engine.chuffEventReadOut.Value=-1f;
            Flow=new SimulationFlow(Engine);
            ConfigurePorts(Reader, Flow, Engine);
            AuthoredFrequency=Flow.Ports[Reader.chuffFrequencyPortId];
        }
        public void Init() { Reader.Init(Reader.FixtureCar,Flow); }
    }
    static void ConfigurePorts(ChuffClipsSimReader r, SimulationFlow flow, ReciprocatingSteamEngine engine)
    {
        r.chuffEventPortId=engine.chuffEventReadOut.id;
        r.chuffFrequencyPortId=engine.id+".authoredScaledFrequency";
        flow.Add(new Port(r.chuffFrequencyPortId,2f));
        r.exhaustPressurePortId=engine.id+".pressure"; flow.Add(new Port(r.exhaustPressurePortId,5f));
        r.cylinderWaterNormalizedPortId=engine.id+".water"; flow.Add(new Port(r.cylinderWaterNormalizedPortId,.5f));
        r.cylinderCockControlPortId=engine.id+".cocks"; flow.Add(new Port(r.cylinderCockControlPortId,.2f));
        r.ashesInPipesPortId=engine.id+".ash"; flow.Add(new Port(r.ashesInPipesPortId,.3f));
    }
    static void ExpandedBank(ChuffClipsSimReader.OrderedChuffClips[] original, ChuffClipsSimReader.OrderedChuffClips[] actual, int size)
    {
        Check(actual.Length==size,"Expected "+size+" slots, got "+actual.Length);
        for(int i=0;i<size;i++) Check(object.ReferenceEquals(original[i%original.Length],actual[i]),"Authored slot order/variation identity changed at "+i);
    }
    static ChuffClipsSimReader.ChuffLoop StockLoop(StockLoopCase stock)
    {
        var clip=new AudioClip(stock.ClipName,32,2,44100);
        for(int i=0;i<clip.Data.Length;i++) clip.Data[i]=(float)Math.Sin(i*.31);
        // Native startPitch is non-serialized and begins at zero until Reset captures source.pitch.
        var layer=new LayeredAudio.Layer { name="chuffing", startPitch=0f, pitchCurve=stock.NativeCurve(), source=new AudioSource { pitch=stock.SourcePitch, clip=clip } };
        return new ChuffClipsSimReader.ChuffLoop { chuffLoop=new LayeredAudio { layers=new[]{layer} }, chuffFrequencyToMasterVolume=new AnimationCurve(x=>.6f) };
    }
    // Routing adapter for S060's installed AudioInitPrefix/Prepare and End:
    // remap all six IDs in a default-priority prefix, restore them in Deinit's
    // postfix. Real installed Harmony executes both hook sets around native Init.
    // This intentionally does not pretend to instantiate the entire S060 mod.
    static ChuffClipsSimReader routedReader;
    static string routedFrom, routedTo;
    static string[] routedOriginal;
    static string[] RoutingIds(ChuffClipsSimReader r) => new[] { r.chuffEventPortId,
        r.exhaustPressurePortId,r.chuffFrequencyPortId,r.cylinderWaterNormalizedPortId,
        r.cylinderCockControlPortId,r.ashesInPipesPortId };
    static void RoutingSet(ChuffClipsSimReader r,string[] ids)
    {
        r.chuffEventPortId=ids[0]; r.exhaustPressurePortId=ids[1]; r.chuffFrequencyPortId=ids[2];
        r.cylinderWaterNormalizedPortId=ids[3]; r.cylinderCockControlPortId=ids[4]; r.ashesInPipesPortId=ids[5];
    }
    static void RoutingPrefix(ChuffClipsSimReader __instance,SimulationFlow simFlow)
    {
        if(!object.ReferenceEquals(__instance,routedReader)) return;
        routedOriginal=RoutingIds(__instance);
        RoutingSet(__instance,routedOriginal.Select(id=>id.Replace(routedFrom+".",routedTo+".")).ToArray());
        foreach(string id in RoutingIds(__instance)) Check(simFlow.TryGetPort(id,out var port),"Routing port absent: "+id);
    }
    static void RoutingDeinit(ChuffClipsSimReader __instance)
    {
        if(object.ReferenceEquals(__instance,routedReader)&&routedOriginal!=null) RoutingSet(__instance,routedOriginal);
    }
    static void RunRoutedInitialization()
    {
        var init=AccessTools.Method(typeof(ChuffClipsSimReader),"Init",new[]{typeof(TrainCar),typeof(SimulationFlow)});
        var deinit=AccessTools.Method(typeof(ChuffClipsSimReader),"Deinit",Type.EmptyTypes);
        var router=new Harmony("fixture.default-port-routing");
        foreach(bool routerFirst in new[]{false,true})
        foreach(int cylinders in new[]{2,3,4})
        {
            bool first=routerFirst; int count=cylinders;
            Run("default port-routing prefix / router registered first="+first+" / target cylinders="+count,()=> {
                new Harmony(mod.Info.Id).UnpatchAll(mod.Info.Id);
                router.UnpatchAll(router.Id);
                Action patchRouter=()=> {
                    router.Patch(init,prefix:new HarmonyMethod(typeof(AudioRegression),nameof(RoutingPrefix)));
                    router.Patch(deinit,postfix:new HarmonyMethod(typeof(AudioRegression),nameof(RoutingDeinit)));
                };
                if(first) patchRouter();
                Check(MultiCylinderChuffFix.Main.Load(mod),"Plugin reload failed");
                if(!first) patchRouter();
                var f=new LocoFixture(2,"primaryStage");
                var target=new ReciprocatingSteamEngine("secondaryStage",count);
                var originalFlow=f.Flow; f.Flow=new SimulationFlow(f.Engine,target);
                foreach(var port in originalFlow.Ports.Values) f.Flow.Add(port);
                var scratch=Reader(); ConfigurePorts(scratch,f.Flow,target);
                target.chuffFrequencyReadOut.Value=19f; f.Engine.chuffFrequencyReadOut.Value=7f;
                var original=RoutingIds(f.Reader); var low=f.Reader.lowPressureClips;
                var loop=Loop(); f.Reader.chuffLoops=new[]{loop};
                routedReader=f.Reader; routedFrom=f.Engine.id; routedTo=target.id;
                bool initialized=false;
                try
                {
                    for(int cycle=0;cycle<2;cycle++)
                    {
                        int warnings=mod.Logger.Warnings.Count;
                        initialized=true; f.Init();
                        Check(mod.Logger.Warnings.Count==warnings,"Valid redirected Init produced a warning");
                        Check(object.ReferenceEquals(f.Reader.Frequency,target.chuffFrequencyReadOut),"Native frequency bound to wrong engine");
                        Check(f.Reader.chuffFrequencyPortId==target.chuffFrequencyReadOut.id,"Public frequency ID disagrees with bound port");
                        ExpandedBank(low,f.Reader.lowPressureClips,count*2);
                        f.Flow.Tick(.02f); Near(19f,loop.chuffLoop.Pitch,"Routed loop tick refresh");
                        f.Reader.Deinit(); initialized=false;
                        Check(object.ReferenceEquals(low,f.Reader.lowPressureClips),"Deinit leaked expanded bank");
                        Check(RoutingIds(f.Reader).SequenceEqual(original),"Deinit did not restore routed IDs");
                        int updates=loop.chuffFrequencyToMasterVolume.Calls; f.Flow.Tick(.02f);
                        Check(loop.chuffFrequencyToMasterVolume.Calls==updates,"Deinit leaked tick refresh");
                    }
                }
                finally { if(initialized) f.Reader.Deinit(); routedReader=null; routedOriginal=null; }
            });
        }
        router.UnpatchAll(router.Id);
        new Harmony(mod.Info.Id).UnpatchAll(mod.Info.Id);
        Check(MultiCylinderChuffFix.Main.Load(mod),"Final plugin reload failed");
    }
    static void RunAdaptation()
    {
        RunRoutedInitialization();
        Run("Harmony registers Init and Deinit lifecycle hooks",()=> {
            var init=Harmony.GetPatchInfo(AccessTools.Method(typeof(ChuffClipsSimReader),"Init",new[]{typeof(TrainCar),typeof(SimulationFlow)}));
            var deinit=Harmony.GetPatchInfo(AccessTools.Method(typeof(ChuffClipsSimReader),"Deinit",Type.EmptyTypes));
            Check(init!=null && init.Prefixes.Any(p=>p.owner==mod.Info.Id),"Init prefix missing");
            Check(deinit!=null && (deinit.Prefixes.Any(p=>p.owner==mod.Info.Id)||deinit.Postfixes.Any(p=>p.owner==mod.Info.Id)),"Deinit restore hook missing");
        });
        foreach(int count in new[]{2,3,4,6})
        {
            int captured=count;
            Run(count+" cylinders: native Init selects exact engine frequency before first event",()=> {
                var f=new LocoFixture(captured); f.Engine.chuffFrequencyReadOut.Value=13.5f;
                string authoredId=f.Reader.chuffFrequencyPortId;
                var low=f.Reader.lowPressureClips; var med=f.Reader.mediumPressureClips; var high=f.Reader.highPressureClips;
                f.Init();
                Check(f.Reader.chuffFrequencyPortId==f.Engine.chuffFrequencyReadOut.id,"Reader frequency ID was not rebound to matched native engine");
                Check(object.ReferenceEquals(f.Reader.Frequency,f.Engine.chuffFrequencyReadOut),"Native Init subscribed/bound authored scaled frequency");
                ExpandedBank(low,f.Reader.lowPressureClips,Math.Max(4,captured*2));
                ExpandedBank(med,f.Reader.mediumPressureClips,Math.Max(4,captured*2));
                ExpandedBank(high,f.Reader.highPressureClips,Math.Max(4,captured*2));
                for(int i=0;i<captured*2;i++) { int before=NAudio.Calls.Count; f.Engine.chuffEventReadOut.Emit(i); Check(NAudio.Calls.Count==before+1,"Event was lost or duplicated"); Check(object.ReferenceEquals(low[i%4].chuffVariations,NAudio.Calls.Last().Clips),"Wrong event sample"); }
                Near(13.5f,f.Reader.regularChuffConfig.chuffFrequencyToMasterVolume.LastInput,"First event native frequency");
                f.Reader.Deinit();
                Check(f.Engine.chuffEventReadOut.Subscribers==0,"Deinit retained event subscription");
                Check(f.Reader.chuffFrequencyPortId==authoredId,"Deinit did not restore authored frequency ID");
                Check(object.ReferenceEquals(low,f.Reader.lowPressureClips)&&object.ReferenceEquals(med,f.Reader.mediumPressureClips)&&object.ReferenceEquals(high,f.Reader.highPressureClips),"Deinit did not restore exact authored bank references");
            });
        }
        Run("three pressure banks expand independently and preserve larger authored banks",()=> {
            var f=new LocoFixture(6); var r=f.Reader;
            r.lowPressureClips=Bank("short",3); r.mediumPressureClips=Bank("full",12); r.highPressureClips=Bank("custom",16);
            var low=r.lowPressureClips; var med=r.mediumPressureClips; var high=r.highPressureClips;
            f.Init(); ExpandedBank(low,r.lowPressureClips,12);
            Check(object.ReferenceEquals(med,r.mediumPressureClips),"Full authored 12-slot bank replaced");
            Check(object.ReferenceEquals(high,r.highPressureClips),"Larger custom 16-slot bank replaced/truncated"); r.Deinit();
        });
        Run("explicit six-entry three-cylinder banks remain authored arrays",()=> {
            var f=new LocoFixture(3); f.Reader.lowPressureClips=Bank("full-six",6); var original=f.Reader.lowPressureClips;
            f.Init(); Check(object.ReferenceEquals(original,f.Reader.lowPressureClips),"Explicit six-slot array replaced"); f.Reader.Deinit();
        });
        Run("shared authored array expands per instance without shared mutation",()=> {
            var a=new LocoFixture(3,"leftUnit"); var b=new LocoFixture(6,"rightUnit");
            var shared=Bank("shared",4); var originals=(ChuffClipsSimReader.OrderedChuffClips[])shared.Clone();
            a.Reader.lowPressureClips=shared; b.Reader.lowPressureClips=shared; a.Init(); b.Init();
            ExpandedBank(shared,a.Reader.lowPressureClips,6); ExpandedBank(shared,b.Reader.lowPressureClips,12);
            Check(!object.ReferenceEquals(a.Reader.lowPressureClips,b.Reader.lowPressureClips),"Instances share expanded array");
            Check(shared.Length==4 && shared.SequenceEqual(originals),"Shared authored source mutated");
            a.Reader.lowPressureClips[0]=null;
            Check(b.Reader.lowPressureClips[0]!=null && shared[0]!=null,"Array slot edit leaked between instances");
            a.Reader.Deinit(); b.Reader.Deinit();
            Check(object.ReferenceEquals(shared,a.Reader.lowPressureClips)&&object.ReferenceEquals(shared,b.Reader.lowPressureClips),"Shared original references not restored");
        });
        Run("two engines and readers bind by exact event port rather than order or ID convention",()=> {
            var a=new LocoFixture(3,"front.power.unit"); var second=new ReciprocatingSteamEngine("rearWeirdID",6);
            var flow=new SimulationFlow(second,a.Engine,new SimComponent("unrelatedCompressor"));
            ConfigurePorts(a.Reader,flow,a.Engine); var b=Reader(); ConfigurePorts(b,flow,second);
            a.Engine.chuffFrequencyReadOut.Value=6f; second.chuffFrequencyReadOut.Value=12f;
            a.Reader.Init(a.Reader.FixtureCar,flow); b.Init(b.FixtureCar,flow);
            Check(object.ReferenceEquals(a.Reader.Frequency,a.Engine.chuffFrequencyReadOut),"First reader matched wrong engine");
            Check(object.ReferenceEquals(b.Frequency,second.chuffFrequencyReadOut),"Second reader matched wrong engine");
            Check(a.Reader.lowPressureClips.Length==6 && b.lowPressureClips.Length==12,"Reader cylinder counts crossed");
            a.Reader.Deinit(); b.Deinit();
        });
        Run("similarly named event ID never guesses a steam engine match",()=> {
            var f=new LocoFixture(6); string id=f.Engine.chuffEventReadOut.id+".proxy";
            f.Reader.chuffEventPortId=id; f.Flow.Add(new Port(id)); var low=f.Reader.lowPressureClips; string freq=f.Reader.chuffFrequencyPortId;
            f.Init(); Check(object.ReferenceEquals(low,f.Reader.lowPressureClips),"Unmatched reader banks changed");
            Check(f.Reader.chuffFrequencyPortId==freq && object.ReferenceEquals(f.AuthoredFrequency,f.Reader.Frequency),"Unmatched reader frequency changed"); f.Reader.Deinit();
        });
        Run("unrelated simulation with no steam engine remains native",()=> {
            var f=new LocoFixture(6); var flow=new SimulationFlow(new SimComponent("airPump")); foreach(var p in f.Flow.Ports.Values) flow.Add(p);
            var low=f.Reader.lowPressureClips; var freq=f.Reader.chuffFrequencyPortId;
            f.Reader.Init(f.Reader.FixtureCar,flow); Check(object.ReferenceEquals(low,f.Reader.lowPressureClips)&&freq==f.Reader.chuffFrequencyPortId,"Non-steam flow adapted"); f.Reader.Deinit();
        });
        Run("Deinit then pooled reuse reads new cylinder count and restores original template each cycle",()=> {
            var f=new LocoFixture(3); var low=f.Reader.lowPressureClips; string authored=f.Reader.chuffFrequencyPortId;
            foreach(int cylinders in new[]{3,6,4,2,6}) {
                // Real engines cache chuffsPerCycle at construction. Pooling creates a new engine/flow;
                // this fixture therefore changes engine instances rather than mutating a running engine.
                f.Engine=new ReciprocatingSteamEngine(f.Engine.id,cylinders); f.Flow=new SimulationFlow(f.Engine);
                ConfigurePorts(f.Reader,f.Flow,f.Engine); f.Init(); ExpandedBank(low,f.Reader.lowPressureClips,Math.Max(4,cylinders*2));
                f.Reader.Deinit(); Check(object.ReferenceEquals(low,f.Reader.lowPressureClips)&&f.Reader.chuffFrequencyPortId==authored,"Pooled reader retained previous adaptation");
            }
        });
        Run("repeated Init does not compound expanded sample order or snapshot adapted state",()=> {
            var f=new LocoFixture(3); var low=f.Reader.lowPressureClips; string authored=f.Reader.chuffFrequencyPortId;
            f.Init(); f.Init(); ExpandedBank(low,f.Reader.lowPressureClips,6); f.Reader.Deinit();
            Check(object.ReferenceEquals(low,f.Reader.lowPressureClips)&&f.Reader.chuffFrequencyPortId==authored,"Repeated Init replaced original restoration snapshot");
            // Native repeated Init itself duplicates event handlers. This discarded reader fixture
            // checks plugin snapshot idempotence, not an invented replacement lifecycle.
        });
        Run("null and empty malformed banks stay visible after Init",()=> {
            var f=new LocoFixture(6); f.Reader.lowPressureClips=null; f.Reader.mediumPressureClips=new ChuffClipsSimReader.OrderedChuffClips[0];
            f.Init(); Check(f.Reader.lowPressureClips==null && f.Reader.mediumPressureClips.Length==0,"Malformed banks were invented or hidden");
            Throws<NullReferenceException>(()=>f.Engine.chuffEventReadOut.Emit(0)); f.Reader.Deinit();
        });
        Run("missing authored frequency can be repaired before native port binding",()=> {
            var f=new LocoFixture(6); f.Reader.chuffFrequencyPortId="does.not.exist";
            f.Init(); Check(object.ReferenceEquals(f.Engine.chuffFrequencyReadOut,f.Reader.Frequency),"Missing authored frequency was not repaired");
            f.Reader.Deinit(); Check(f.Reader.chuffFrequencyPortId=="does.not.exist","Authored ID not restored after repair");
        });
        Run("native partial Init restores plugin changes and leaves bank errors visible",()=> {
            var f=new LocoFixture(6); string authored=f.Reader.chuffFrequencyPortId; var low=f.Reader.lowPressureClips;
            f.Flow.Ports.Remove(f.Reader.ashesInPipesPortId); f.Init();
            Check(f.Reader.chuffFrequencyPortId==authored && object.ReferenceEquals(low,f.Reader.lowPressureClips),"Partial Init retained adaptation");
            Check(f.Flow.TickSubscribers==0,"Partial Init retained update callback"); f.Reader.Deinit();
        });
        Run("native Init exception propagates unchanged after adaptation cleanup",()=> {
            var f=new LocoFixture(6); string authored=f.Reader.chuffFrequencyPortId; var low=f.Reader.lowPressureClips;
            f.Reader.regularChuffConfig.spatialCurve=null; Throws<NullReferenceException>(()=>f.Init());
            Check(f.Reader.chuffFrequencyPortId==authored && object.ReferenceEquals(low,f.Reader.lowPressureClips),"Failed Init retained adaptation");
            Check(f.Flow.TickSubscribers==0,"Failed Init retained callback");
        });
        Run("no-event simulation tick refreshes all native loop families with current exhaust frequency",()=> {
            var f=new LocoFixture(6); var r=f.Reader; var regular=Loop(); var water=Loop(); var ash=Loop();
            r.chuffLoops=new[]{regular}; r.waterChuffLoops=new[]{water}; r.ashChuffLoops=new[]{ash}; f.Init();
            f.Engine.chuffFrequencyReadOut.Value=18f; f.Flow.Tick(.02f);
            foreach(var loop in new[]{regular,water,ash}) { Check(loop.chuffLoop.PitchCalls==1,"No-event loop update missing/duplicated"); Near(18f,loop.chuffLoop.Pitch,"No-event frequency"); }
            Check(NAudio.Calls.Count==0,"Fallback tick generated a one-shot chuff");
            f.Engine.chuffFrequencyReadOut.Value=0f; f.Flow.Tick(.02f); Near(0f,regular.chuffLoop.Pitch,"Stopping frequency not refreshed");
            f.Reader.Deinit(); Check(f.Flow.TickSubscribers==0,"Deinit retained fallback callback");
            int calls=regular.chuffLoop.PitchCalls; f.Flow.Tick(.02f); Check(calls==regular.chuffLoop.PitchCalls,"Disposed reader still received tick refresh");
        });
        Run("native event update during tick suppresses duplicate fallback update",()=> {
            var f=new LocoFixture(3); var loop=Loop(); f.Reader.chuffLoops=new[]{loop}; f.Init();
            f.Engine.chuffFrequencyReadOut.Value=9f; f.Engine.TickAction=delta=>f.Engine.chuffEventReadOut.Emit(1f);
            f.Flow.Tick(.02f); Check(loop.chuffLoop.PitchCalls==1,"One event caused duplicate loop update");
            f.Flow.Tick(.02f); Check(loop.chuffLoop.PitchCalls==2,"Unchanged event index prevented fallback refresh");
            Check(NAudio.Calls.Count==1,"Unchanged index invented a one-shot event"); f.Reader.Deinit();
        });
        Run("coroutine loop refresh between ticks cannot mask the next simulation refresh",()=> {
            var f=new LocoFixture(4); var loop=Loop(); f.Reader.chuffLoops=new[]{loop}; f.Init();
            f.Flow.Tick(.02f); f.Reader.RunLoops(); int before=loop.chuffLoop.PitchCalls;
            f.Engine.chuffFrequencyReadOut.Value=12f; f.Flow.Tick(.02f);
            Check(loop.chuffLoop.PitchCalls==before+1,"Previous coroutine update masked next tick"); Near(12f,loop.chuffLoop.Pitch,"Next tick did not see changed frequency"); f.Reader.Deinit();
        });
        Run("destroyed reader without Deinit restores plugin state and removes its tick callback",()=> {
            var f=new LocoFixture(6); var low=f.Reader.lowPressureClips; string authored=f.Reader.chuffFrequencyPortId; f.Init();
            Check(f.Flow.TickSubscribers==1,"Expected one tick callback before destruction");
            UnityEngine.Object.Destroy(f.Reader); f.Flow.Tick(.02f);
            Check(f.Flow.TickSubscribers==0,"Destroyed reader retained callback");
            Check(object.ReferenceEquals(low,f.Reader.lowPressureClips)&&f.Reader.chuffFrequencyPortId==authored,"Destroyed reader did not restore managed configuration");
        });
        foreach(int count in new[]{2,3,4,6})
        foreach(float rpm in new[]{30f,90f,180f})
        {
            int cylinders=count; float capturedRpm=rpm;
            Run(count+" cylinders at "+rpm+" rpm: all native audio paths and loops use engine exhaust Hz",()=> {
                var f=new LocoFixture(cylinders); var r=f.Reader;
                float hz=capturedRpm/60f*cylinders*2;
                f.Engine.chuffFrequencyReadOut.Value=hz; f.AuthoredFrequency.Value=hz*.173f;
                r.waterChuffClips=new[]{new AudioClip("water")}; r.ashChuffClips=new[]{new AudioClip("ash")};
                var regular=Loop(); var water=Loop(); var ash=Loop(); r.chuffLoops=new[]{regular}; r.waterChuffLoops=new[]{water}; r.ashChuffLoops=new[]{ash};
                f.Init(); f.Engine.chuffEventReadOut.Emit(cylinders*2-1);
                Near(hz,r.regularChuffConfig.chuffFrequencyToMasterVolume.LastInput,"regular one-shot frequency");
                Near(hz,r.waterChuffConfig.chuffFrequencyToMasterVolume.LastInput,"water one-shot frequency");
                Near(hz,r.ashChuffConfig.chuffFrequencyToMasterVolume.LastInput,"ash one-shot frequency");
                foreach(var loop in new[]{regular,water,ash}) { Near(hz,loop.chuffFrequencyToMasterVolume.LastInput,"loop crossfade frequency"); Near(hz,loop.chuffLoop.Pitch,"loop pitch input"); }
                f.Engine.chuffFrequencyReadOut.Value=hz*1.5f; f.Engine.chuffEventReadOut.Emit(0f);
                Near(hz*1.5f,regular.chuffLoop.Pitch,"running speed changed native loop pitch"); r.Deinit();
            });
        }
        foreach(var stock in StockLoopMetadata.Cases)
        {
            var captured=stock;
            Run("stock native Layer.SetPitch maps true exhaust frequency: "+stock.Label,()=> {
                var f=new LocoFixture(6); var loop=StockLoop(captured);
                var layer=loop.chuffLoop.layers[0]; var authoredCurve=layer.pitchCurve; var originalClip=layer.source.clip;
                f.Reader.chuffLoops=new[]{loop}; f.Init();
                int eventIndex=0;
                float baseHz=1f/((captured.Points[1][1]-captured.Points[0][1])/(captured.Points[1][0]-captured.Points[0][0]));
                foreach(float hz in new[]{baseHz*.5f,baseHz,baseHz*1.5f}) {
                    f.Engine.chuffFrequencyReadOut.Value=hz; f.Engine.chuffEventReadOut.Emit(eventIndex++);
                    float bufferRateFactor=(float)layer.source.clip.frequency/originalClip.frequency;
                    Near(hz,baseHz*bufferRateFactor*layer.source.pitch,"inferred stock loop exhaust rate"); Near(hz,layer.pitchCurve.LastInput,"stock pitch curve input");
                }
                Check(!object.ReferenceEquals(authoredCurve,layer.pitchCurve),"Recognized stock curve was not copied for extension");
                Near(captured.EvaluateLinear(36f),layer.pitchCurve.Evaluate(36f),"Stock pitch curve still clamps at authored endpoint");
                f.Reader.Deinit();
                Check(object.ReferenceEquals(authoredCurve,layer.pitchCurve),"Authored curve reference not restored");
                Check(object.ReferenceEquals(originalClip,layer.source.clip),"Authored loop clip not restored"); Near(captured.SourcePitch,layer.startPitch,"Authored startPitch not restored");
            });
        }
        Run("sustained stock loop rebases lossless PCM to keep source pitch within native range",()=> {
            var stock=StockLoopMetadata.Cases.First(c=>c.ClipName.Contains("16s"));
            var f=new LocoFixture(2); var loop=StockLoop(stock); var layer=loop.chuffLoop.layers[0]; var original=layer.source.clip;
            layer.source.isPlaying=true; layer.source.timeSamples=11; var curve=layer.pitchCurve;
            f.Reader.chuffLoops=new[]{loop}; f.Init(); int index=0;
            foreach(float hz in new[]{16f,36f,60f,96f,120f,180f,200f,400f}) {
                f.Engine.chuffFrequencyReadOut.Value=hz; f.Engine.chuffEventReadOut.Emit(index++);
                float factor=(float)layer.source.clip.frequency/original.frequency;
                Near(hz,16f*factor*layer.source.pitch,"Rebased sustained loop rate"); Check(Math.Abs(layer.source.pitch)<=3f,"Rebased source exceeds Unity pitch limit");
                Check(layer.source.clip.samples==original.samples && layer.source.clip.channels==original.channels,"Rebase changed PCM shape");
                Check(Enumerable.SequenceEqual(layer.source.clip.Data,original.Data),"Rebase changed PCM sample values");
                Check(layer.source.timeSamples==11,"Rebase lost playback sample phase");
            }
            var generated=layer.source.clip; Check(!object.ReferenceEquals(original,generated),"High-speed clip was not rebased");
            f.Reader.Deinit(); Check(object.ReferenceEquals(original,layer.source.clip)&&object.ReferenceEquals(curve,layer.pitchCurve),"Deinit did not restore sustained loop");
            Check(generated.Destroyed,"Generated PCM clip was not released");
        });
        Run("cached native loop delegate traverses Harmony preparation and completion hooks",()=> {
            var stock=StockLoopMetadata.Cases.First(c=>c.ClipName.Contains("16s")); var f=new LocoFixture(2); var loop=StockLoop(stock);
            var layer=loop.chuffLoop.layers[0]; var original=layer.source.clip; f.Reader.chuffLoops=new[]{loop}; f.Init();
            f.Engine.chuffFrequencyReadOut.Value=60f;
            var field=AccessTools.Field(typeof(MultiCylinderChuffFix.Main),"UpdateNativeLoops");
            Check(field!=null,"Cached native loop delegate missing");
            var update=(Action<ChuffClipsSimReader>)field.GetValue(null);
            f.Engine.TickAction=delta=>update(f.Reader);
            f.Flow.Tick(.02f);
            Check(layer.source.clip.frequency==original.frequency*2,"Cached delegate bypassed UpdateLoops preparation prefix");
            Check(loop.chuffLoop.PitchCalls==1,"Cached delegate bypassed completion postfix; tick fallback duplicated native update");
            f.Reader.Deinit();
        });
        Run("unreadable stock PCM retains original clip and warns once per attempted factor",()=> {
            var stock=StockLoopMetadata.Cases.First(c=>c.ClipName.Contains("16s")); var f=new LocoFixture(6); var loop=StockLoop(stock);
            var layer=loop.chuffLoop.layers[0]; var original=layer.source.clip; original.ReadSucceeds=false;
            f.Reader.chuffLoops=new[]{loop}; int before=mod.Logger.Warnings.Count; f.Init(); int after=mod.Logger.Warnings.Count;
            Check(after>before,"PCM failure was not reported"); Check(object.ReferenceEquals(original,layer.source.clip),"PCM failure replaced source clip");
            f.Engine.chuffFrequencyReadOut.Value=120f; f.Flow.Tick(.02f); int firstRange=mod.Logger.Warnings.Count; f.Flow.Tick(.02f);
            Check(mod.Logger.Warnings.Count==firstRange,"Repeated failed factor/range reports were not bounded"); f.Reader.Deinit();
            Check(object.ReferenceEquals(original,layer.source.clip)&&!original.Destroyed,"PCM failure damaged authored clip");
        });
        Run("native sample-rate refusal falls back to supported PCM and never retries rejected factors",()=> {
            AudioClip.MaximumCreateFrequency=88200;
            var stock=StockLoopMetadata.Cases.First(c=>c.ClipName.Contains("16s")); var f=new LocoFixture(6); var loop=StockLoop(stock);
            var layer=loop.chuffLoop.layers[0]; var original=layer.source.clip; f.Reader.chuffLoops=new[]{loop}; f.Init();
            Check(layer.source.clip.frequency==88200,"Rejected four-times rate did not fall back to supported two-times PCM");
            f.Engine.chuffFrequencyReadOut.Value=400f; f.Flow.Tick(.02f); int firstAttempts=AudioClip.CreateAttempts.Count;
            f.Flow.Tick(.02f); f.Flow.Tick(.02f);
            Check(AudioClip.CreateAttempts.Count==firstAttempts,"Repeated ticks retried rejected sample frequencies");
            Check(layer.source.clip.frequency==88200,"Failed high rate replaced supported PCM");
            Check(AudioClip.CreateAttempts.Distinct().Count()==AudioClip.CreateAttempts.Count,"A failed factor was attempted more than once");
            Check(Enumerable.SequenceEqual(original.Data,layer.source.clip.Data),"Rate refusal damaged retained PCM");
            f.Reader.Deinit(); Check(object.ReferenceEquals(original,layer.source.clip),"Rate-refusal Deinit did not restore original clip");
        });
        Run("stock water loop curves adapt and sustained water PCM restores exactly",()=> {
            var f=new LocoFixture(6); var four=StockLoop(StockLoopMetadata.Cases.First(c=>c.ClipName=="Steam_ChuffLoop4s_01"));
            var sixteen=StockLoop(StockLoopMetadata.Cases.First(c=>c.ClipName=="Steam_ChuffLoop16s_01"));
            four.chuffLoop.layers[0].source.clip.name="Steam_ChuffWaterLoop4s_01";
            sixteen.chuffLoop.layers[0].source.clip.name="Steam_ChuffWaterLoop16s_01";
            four.chuffLoop.layers[0].pitchCurve=AnimationCurve.Linear(-32f,-8f,32f,8f);
            sixteen.chuffLoop.layers[0].pitchCurve=AnimationCurve.Linear(-32f,-2f,32f,2f);
            var clips=new[]{four,sixteen}.Select(l=>l.chuffLoop.layers[0].source.clip).ToArray();
            var curves=new[]{four,sixteen}.Select(l=>l.chuffLoop.layers[0].pitchCurve).ToArray();
            f.Reader.waterChuffLoops=new[]{four,sixteen}; f.Init(); f.Engine.chuffFrequencyReadOut.Value=36f; f.Flow.Tick(.02f);
            Near(9f,four.chuffLoop.layers[0].pitchCurve.Evaluate(36f),"Water four-Hz curve extension");
            var layer=sixteen.chuffLoop.layers[0]; float factor=(float)layer.source.clip.frequency/clips[1].frequency;
            Near(36f,16f*factor*layer.source.pitch,"Sustained water inferred exhaust rate");
            f.Reader.Deinit(); int i=0; foreach(var loop in new[]{four,sixteen}) { Check(object.ReferenceEquals(clips[i],loop.chuffLoop.layers[0].source.clip)&&object.ReferenceEquals(curves[i],loop.chuffLoop.layers[0].pitchCurve),"Water loop authored state not restored"); i++; }
        });
        Run("authored custom, nonlinear and ash pitch curves retain their native configuration",()=> {
            var stock=StockLoopMetadata.Cases.Last(); var f=new LocoFixture(6);
            var custom=StockLoop(stock); custom.chuffLoop.layers[0].source.clip.name="My_custom_recorded_loop";
            var nonlinear=StockLoop(stock); nonlinear.chuffLoop.layers[0].pitchCurve=new AnimationCurve(new Keyframe(-16f,-1f),new Keyframe(0f,.5f),new Keyframe(32f,2f));
            var wrapped=StockLoop(stock); wrapped.chuffLoop.layers[0].pitchCurve.postWrapMode=WrapMode.Loop;
            var ash=StockLoop(stock); var originals=new[]{custom,nonlinear,wrapped,ash}.Select(l=>l.chuffLoop.layers[0].pitchCurve).ToArray();
            var clips=new[]{custom,nonlinear,wrapped,ash}.Select(l=>l.chuffLoop.layers[0].source.clip).ToArray();
            f.Reader.chuffLoops=new[]{custom,nonlinear,wrapped}; f.Reader.ashChuffLoops=new[]{ash}; f.Init(); f.Engine.chuffFrequencyReadOut.Value=36f; f.Flow.Tick(.02f);
            int i=0; foreach(var loop in new[]{custom,nonlinear,wrapped,ash}) { Check(object.ReferenceEquals(originals[i],loop.chuffLoop.layers[0].pitchCurve),"Custom/ash curve replaced"); Check(object.ReferenceEquals(clips[i],loop.chuffLoop.layers[0].source.clip),"Custom/ash PCM replaced"); i++; }
            f.Reader.Deinit();
        });
    }
}
