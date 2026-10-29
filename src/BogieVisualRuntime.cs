using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityModManagerNet;

namespace DadsToolbox.BogieVisuals
{
    public sealed class VisualRuntime : MonoBehaviour
    {
        sealed class LiveBinding
        {
            public VisualBinding visual;
            public Rigidbody lastBody,lastCarBody;
            public Vector3 lastWorldPosition;
            public bool wasPaused,wasAvailable;
        }
        sealed class LiveCar : IDisposable
        {
            public TrainCar car;
            public VisualSmoother smoother;
            public readonly List<LiveBinding> bindings=new List<LiveBinding>();
            public bool loggedPresentation;
            public void Dispose() { if(smoother!=null) smoother.Dispose(); }
        }
        readonly Dictionary<TrainCar,LiveCar> cars=new Dictionary<TrainCar,LiveCar>();
        readonly List<LiveCar> active=new List<LiveCar>();
        readonly Dictionary<TrainCar,float> retryAfter=new Dictionary<TrainCar,float>();
        readonly Queue<TrainCar> pending=new Queue<TrainCar>();
        readonly HashSet<TrainCar> queued=new HashSet<TrainCar>();
        readonly List<TrainCar> refreshScratch=new List<TrainCar>();
        CarSpawner spawner;
        int registryIndex,maintenanceIndex,presentedFrame=-1;
        Camera[] views=new Camera[0]; int viewCount;
        float camerasAt;
        Vector3 origin;
        TrainCar buildingCar;
        VisualLayout.Layout buildingLayout;
        VisualSmoother buildingProxy;
        IEnumerator build;
        bool proxyStage;
        double lastMaintenanceMs,peakMaintenanceMs;
        const double BudgetSeconds=0.00075;
        const float NearbyDistanceSquared=250f*250f;
        public UnityModManager.ModEntry.ModLogger Logger;
        public string ReportPath;
        public string Status { get { return "Mapped cars: "+active.Count+"; queued: "+pending.Count+
            "; maintenance last/peak: "+lastMaintenanceMs.ToString("F2")+"/"+peakMaintenanceMs.ToString("F2")+" ms (0.75 ms work budget)."; } }
        static bool PresentationCamera(Camera camera)
        { return camera && camera.isActiveAndEnabled && (camera.cameraType==CameraType.Game || camera.stereoEnabled); }
        bool Nearby(TrainCar car)
        {
            if(!car) return false;
            for(int i=0;i<viewCount;i++)
                if(PresentationCamera(views[i]) && (car.transform.position-views[i].transform.position).sqrMagnitude<=NearbyDistanceSquared) return true;
            return false;
        }
        void OnEnable()
        { Camera.onPreCull+=BeforeCamera; origin=WorldMover.currentMove; StartCoroutine(SampleAfterPhysics()); }
        // The Toolbox host must restore native rendering synchronously, including
        // EditMode hosts where setting enabled does not dispatch OnDisable.
        public void Shutdown() { enabled=false; OnDisable(); }
        void OnDisable()
        { Camera.onPreCull-=BeforeCamera; StopAllCoroutines(); ResetWorld(); }
        void OnDestroy() { OnDisable(); }
        void AttachSpawner(CarSpawner next)
        {
            if(spawner==next) return;
            ResetWorld(); spawner=next;
            if(spawner) { spawner.CarSpawned+=OnSpawn; spawner.CarAboutToBeDeleted+=OnDelete; }
        }
        void ResetWorld()
        {
            if(!ReferenceEquals(spawner,null))
            { spawner.CarSpawned-=OnSpawn; spawner.CarAboutToBeDeleted-=OnDelete; }
            spawner=null; CancelBuild();
            foreach(var live in active) live.Dispose();
            cars.Clear(); active.Clear(); pending.Clear(); queued.Clear(); retryAfter.Clear();
            registryIndex=maintenanceIndex=0;
        }
        void OnSpawn(TrainCar car) { if(Nearby(car)) QueueBuild(car); }
        void OnDelete(TrainCar car)
        { Remove(car); retryAfter.Remove(car); queued.Remove(car); if(buildingCar==car) CancelBuild(); }
        void Remove(TrainCar car)
        {
            LiveCar live;
            if(cars.TryGetValue(car,out live)) { live.Dispose(); active.Remove(live); cars.Remove(car); }
        }
        void QueueBuild(TrainCar car)
        {
            if(car && car!=buildingCar && queued.Add(car)) pending.Enqueue(car);
        }
        void CancelBuild()
        {
            var disposable=build as IDisposable; if(disposable!=null) disposable.Dispose();
            build=null;
            if(buildingProxy!=null) buildingProxy.Dispose();
            buildingProxy=null; buildingLayout=null; buildingCar=null; proxyStage=false;
        }
        void Update()
        {
            // The game owns this registry and these events. Never re-search all
            // scene objects or all mapped hierarchies on a periodic timer.
            if(!WorldStreamingInit.IsLoaded)
            { if(spawner || active.Count>0 || build!=null) ResetWorld(); return; }
            AttachSpawner(CarSpawner.Instance);
            if(!spawner || spawner.AllCars==null) return;
            if(Time.unscaledTime>=camerasAt)
            {
                camerasAt=Time.unscaledTime+0.5f;
                int count=Camera.allCamerasCount;
                if(views.Length<count) views=new Camera[count];
                viewCount=count==0 ? 0 : Camera.GetAllCameras(views);
            }
            long start=Stopwatch.GetTimestamp();
            long deadline=start+(long)(Stopwatch.Frequency*BudgetSeconds);
            int registryChecks=0;
            for(int operation=0;operation<64 && Stopwatch.GetTimestamp()<deadline;operation++)
            {
                if(operation%3==0 && registryChecks<8)
                { InspectRegistryCar(); registryChecks++; }
                else if(operation%3==1 && (build!=null || pending.Count>0)) AdvanceBuild();
                else if(active.Count>0) MaintainCar();
                else if(build==null && pending.Count==0 && registryChecks>=8) break;
            }
            lastMaintenanceMs=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            peakMaintenanceMs=Math.Max(peakMaintenanceMs,lastMaintenanceMs);
        }
        void InspectRegistryCar()
        {
            var list=spawner.AllCars;
            if(list.Count==0) return;
            if(registryIndex>=list.Count) registryIndex=0;
            var car=list[registryIndex++];
            if(!car) return;
            if(!car.gameObject.activeInHierarchy || !car.rb || !Nearby(car))
            { Remove(car); return; }
            float retry;
            if(!cars.ContainsKey(car) && (!retryAfter.TryGetValue(car,out retry) || Time.unscaledTime>=retry))
            { retryAfter.Remove(car); QueueBuild(car); }
        }
        void AdvanceBuild()
        {
            if(build==null)
            {
                if(pending.Count==0) return;
                var car=pending.Dequeue(); queued.Remove(car);
                if(!car || cars.ContainsKey(car) || !car.rb || !car.gameObject.activeInHierarchy || !Nearby(car)) return;
                buildingCar=car; buildingLayout=new VisualLayout.Layout {car=car};
                build=VisualLayout.Discover(buildingLayout); proxyStage=false;
            }
            try
            {
                if(!buildingCar || !buildingCar.rb || !buildingCar.gameObject.activeInHierarchy || !Nearby(buildingCar))
                { CancelBuild(); return; }
                if(build.MoveNext()) return;
                var disposable=build as IDisposable; if(disposable!=null) disposable.Dispose();
                build=null;
                if(!proxyStage)
                {
                    if(buildingLayout.bindings.Count==0)
                    { retryAfter[buildingCar]=Time.unscaledTime+15f; CancelBuild(); return; }
                    buildingProxy=new VisualSmoother(buildingLayout);
                    build=buildingProxy.BuildSteps(); proxyStage=true; return;
                }
                var live=new LiveCar {car=buildingCar,smoother=buildingProxy};
                foreach(var visual in buildingLayout.bindings) live.bindings.Add(new LiveBinding {visual=visual});
                cars.Add(live.car,live); active.Add(live);
                if(Logger!=null) Logger.Log("Mapped "+live.car.name+": "+live.smoother.MeshCount+" copied meshes, "+live.bindings.Count+
                    " visual owners; "+live.smoother.UnsupportedCount+" native-only entries.");
                buildingProxy=null; CancelBuild();
            }
            catch(VisualLayoutChangedException exception)
            {
                var car=buildingCar;
                CancelBuild();
                RetryChangedLayout(car,exception.Message);
            }
            catch(Exception exception)
            {
                var car=buildingCar;
                if(car) retryAfter[car]=Time.unscaledTime+15f;
                CancelBuild();
                if(Logger!=null) Logger.Error("Mapping left native; retry scheduled: "+exception);
            }
        }
        void RetryChangedLayout(TrainCar car,string reason)
        {
            if(!car) return;
            retryAfter[car]=Time.unscaledTime+0.25f;
            if(Logger!=null) Logger.Log("Visual layout changed on "+car.name+"; native rendering retained, remapping shortly: "+reason);
        }
        void MaintainCar()
        {
            if(maintenanceIndex>=active.Count) maintenanceIndex=0;
            var live=active[maintenanceIndex++];
            try
            {
                if(live.car && live.smoother.MaintainOne()) return;
            }
            catch(VisualLayoutChangedException exception)
            {
                var changedCar=live.car; Remove(changedCar); RetryChangedLayout(changedCar,exception.Message); return;
            }
            catch(Exception exception)
            { if(Logger!=null) Logger.Error("Visual refresh required: "+exception.Message); }
            var car=live.car; Remove(car); if(car) QueueBuild(car);
        }
        public void RefreshMapped()
        {
            refreshScratch.Clear(); foreach(var live in active) refreshScratch.Add(live.car);
            foreach(var car in refreshScratch) { Remove(car); QueueBuild(car); }
            refreshScratch.Clear();
        }
        public void WriteCoverage()
        {
            if(string.IsNullOrEmpty(ReportPath)) return;
            try
            {
                var report=new StringBuilder("DVLocoBogieVisuals 0.4.2 coverage — "+DateTime.UtcNow.ToString("O")+"\n");
                report.AppendLine(Status);
                report.AppendLine("Manual snapshot; enabled flags are not proof of a GPU draw or smooth movement.");
                foreach(var live in active)
                {
                    report.AppendLine("\nCAR "+live.car.name+" / "+live.car.GetInstanceID());
                    foreach(var state in live.bindings)
                    {
                        var binding=state.visual;
                        report.AppendLine("OWNER "+binding.SourceRoot.name+" / "+binding.SourceRoot.GetInstanceID()+
                            " mode="+(binding.PresentationSample ? "final-constrained-pose" : binding.TranslationOnly ? "provider-translation" : "native-bogie-pose")+
                            " available="+binding.Available+" ready="+binding.History.Ready+" samples="+binding.History.Samples+
                            " resets="+binding.History.Resets+" radius="+binding.Radius.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                            "m position/rotation-clamps="+binding.PositionClamps+"/"+binding.RotationClamps);
                    }
                    live.smoother.AppendCoverage(report);
                }
                File.WriteAllText(ReportPath,report.ToString());
                if(Logger!=null) Logger.Log("Manual coverage snapshot saved: "+ReportPath);
            }
            catch(Exception exception) { if(Logger!=null) Logger.Error("Coverage report: "+exception.Message); }
        }
        void CheckOrigin()
        {
            var current=WorldMover.currentMove; if(current==origin) return; origin=current;
            foreach(var live in active) foreach(var state in live.bindings) state.visual.History.Reset();
        }
        static bool UpdateState(LiveCar live,LiveBinding state)
        {
            var visual=state.visual; var bogie=visual.Bogie;
            var body=bogie ? bogie.rb : null; var carBody=live.car ? live.car.rb : null;
            bool available=bogie && body && carBody && !bogie.HasDerailed && bogie.gameObject.activeInHierarchy && visual.SourceRoot && visual.Anchor;
            bool paused=!available || body.isKinematic || carBody.isKinematic || Time.timeScale==0;
            if(body!=state.lastBody || carBody!=state.lastCarBody || paused!=state.wasPaused || available!=state.wasAvailable ||
                (body && Vector3.Distance(body.position,state.lastWorldPosition)>4f)) visual.History.Reset();
            state.lastBody=body; state.lastCarBody=carBody; state.wasPaused=paused; state.wasAvailable=available;
            if(body) state.lastWorldPosition=body.position;
            visual.Available=available && !paused; return visual.Available;
        }
        IEnumerator SampleAfterPhysics()
        {
            var wait=new WaitForFixedUpdate();
            while(true)
            {
                yield return wait; CheckOrigin();
                foreach(var live in active) foreach(var state in live.bindings)
                {
                    if(!UpdateState(live,state) || state.visual.PresentationSample) continue;
                    var visual=state.visual; var body=state.lastBody; var carBody=state.lastCarBody;
                    var position=body.position;
                    if(visual.TranslationOnly) position+=body.rotation*visual.Bogie.transform.InverseTransformPoint(visual.Anchor.position);
                    var inverse=Quaternion.Inverse(carBody.rotation);
                    visual.History.Sample(inverse*(position-carBody.position),inverse*body.rotation,Time.fixedTime,Time.fixedDeltaTime);
                }
            }
        }
        void BeforeCamera(Camera camera)
        {
            if(!PresentationCamera(camera) || presentedFrame==Time.frameCount) return;
            // Manual icon/texture cameras can render inside Update, before the
            // native constraint outputs are final. Use the actual player view.
            if(camera!=PlayerManager.ActiveCamera && !camera.stereoEnabled) return;
            presentedFrame=Time.frameCount; CheckOrigin();
            for(int i=active.Count-1;i>=0;i--)
            {
                var live=active[i];
                if(!live.car || !live.car.rb || !live.car.gameObject.activeInHierarchy) { Remove(live.car); continue; }
                try
                {
                    foreach(var state in live.bindings) UpdateState(live,state);
                    live.smoother.Present(Time.time,Time.timeScale==0 || live.car.rb.isKinematic);
                    if(!live.loggedPresentation)
                    {
                        int visible=0;
                        foreach(var renderer in live.smoother.ProxyRenderers)
                            if(renderer && renderer.enabled && renderer.gameObject.activeInHierarchy) visible++;
                        if(visible>0)
                        {
                            live.loggedPresentation=true;
                            if(Logger!=null) Logger.Log("Presenting "+live.car.name+": "+visible+" meshes. Native physics unchanged.");
                        }
                    }
                }
                catch(VisualLayoutChangedException exception)
                {
                    var car=live.car; Remove(car); RetryChangedLayout(car,exception.Message);
                }
                catch(Exception exception)
                {
                    var car=live.car; Remove(car); retryAfter[car]=Time.unscaledTime+15f;
                    if(Logger!=null) Logger.Error("Restored native rendering; retry scheduled: "+exception.Message);
                }
            }
        }
    }
}
