using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DadsToolbox.BogieVisuals
{
    // A yielded build or live copy can outlive a wheel-arrangement change. A
    // stale source snapshot is discarded and rediscovered, preserving native visuals.
    internal sealed class VisualLayoutChangedException : InvalidOperationException
    {
        internal VisualLayoutChangedException(string message) : base(message) { }
    }

    // This core never writes a source Transform, Rigidbody, Collider or Joint.
    // Its only source mutation is Renderer.forceRenderingOff, restored on disposal.
    public sealed class PoseHistory
    {
        Vector3 previousPosition, currentPosition;
        Quaternion previousRotation, currentRotation;
        Vector3 firstPosition, rawPosition;
        Quaternion firstRotation, rawRotation;
        float stamp, step;
        bool ready, interpolateSamples;
        // Two causal low-pass stages round changes in native velocity/steering.
        // Inputs are car-relative, so the car's forward travel is never delayed.
        const float HalfLife=0.035f;
        const float MaxPositionOffset=0.03f;
        const float MaxAngleOffset=1.5f;
        public int Resets { get; private set; }
        public int Samples { get; private set; }
        public bool Ready { get { return ready; } }
        public void Reset() { ready = false; Resets++; }
        public void Sample(Vector3 position, Quaternion rotation, float time, float delta)
        { SampleCore(position,rotation,time,delta,true); }
        public void SampleRendered(Vector3 position,Quaternion rotation,float time,float delta)
        { SampleCore(position,rotation,time,delta,false); }
        void SampleCore(Vector3 position,Quaternion rotation,float time,float delta,bool fixedSamples)
        {
            Samples++;
            if (!ready || fixedSamples!=interpolateSamples || delta<=0 || time <= stamp ||
                (fixedSamples && Mathf.Abs(delta-step)>0.000001f) ||
                time-stamp>(fixedSamples ? delta*1.5f : 0.25f) || Vector3.Distance(position,rawPosition)>4f ||
                Quaternion.Angle(rotation,rawRotation)>45f)
            {
                previousPosition=currentPosition=position;
                previousRotation=currentRotation=rotation;
                firstPosition=position; firstRotation=rotation;
                ready=true; Resets++;
            }
            else
            {
                previousPosition=currentPosition; previousRotation=currentRotation;
                float blend=1f-Mathf.Exp(-0.69314718f*delta/HalfLife);
                firstPosition=Vector3.Lerp(firstPosition,position,blend);
                firstRotation=Quaternion.Slerp(firstRotation,rotation,blend);
                currentPosition=Vector3.Lerp(currentPosition,firstPosition,blend);
                currentRotation=Quaternion.Slerp(currentRotation,firstRotation,blend);
                // Bound visual displacement from the authoritative native pose.
                // These limits apply at physics samples, before render interpolation.
                currentPosition=position+Vector3.ClampMagnitude(currentPosition-position,MaxPositionOffset);
                float angle=Quaternion.Angle(rotation,currentRotation);
                if(angle>MaxAngleOffset)
                    currentRotation=Quaternion.Slerp(rotation,currentRotation,MaxAngleOffset/angle);
            }
            rawPosition=position; rawRotation=rotation;
            stamp=time; step=delta; interpolateSamples=fixedSamples;
        }
        public bool Evaluate(float time, bool paused, out Vector3 position, out Quaternion rotation)
        {
            float alpha=paused || !interpolateSamples || step<=0 ? 1f : Mathf.Clamp01((time-stamp)/step);
            position=Vector3.Lerp(previousPosition,currentPosition,alpha);
            rotation=Quaternion.Slerp(previousRotation,currentRotation,alpha);
            if(paused) { position=rawPosition; rotation=rawRotation; }
            return ready;
        }
    }

    public sealed class VisualBinding
    {
        public readonly Transform SourceRoot;
        public readonly Transform Anchor;
        public readonly bool TranslationOnly;
        public readonly Bogie Bogie;
        public bool PresentationSample, FilterPosition=true, FilterRotation=true;
        public float Radius=0.5f;
        public int PositionClamps, RotationClamps;
        // Null retains the legacy world-pose contract for other callers.
        public Transform ReferenceFrame;
        public readonly PoseHistory History=new PoseHistory();
        public bool Available;
        public VisualBinding(Transform root, Transform anchor, bool translationOnly,Bogie bogie=null)
        { SourceRoot=root; Anchor=anchor; TranslationOnly=translationOnly; Bogie=bogie ? bogie : anchor.GetComponentInParent<Bogie>(); }

        public void LimitPresentation(ref Vector3 position,ref Quaternion rotation)
        {
            // Combined envelope at the farthest mapped mesh bound, compared
            // with this frame's native visual root, not a delayed physics pose.
            var nativePosition=SourceRoot.position; var nativeRotation=SourceRoot.rotation;
            var offset=position-nativePosition;
            if(offset.sqrMagnitude>0.02f*0.02f) { position=nativePosition+Vector3.ClampMagnitude(offset,0.02f); PositionClamps++; }
            float remaining=Mathf.Max(0,0.05f-Vector3.Distance(position,nativePosition));
            float angularLimit=Mathf.Min(1.5f,2f*Mathf.Asin(Mathf.Clamp01(remaining/(2f*Mathf.Max(Radius,0.1f))))*Mathf.Rad2Deg);
            float angle=Quaternion.Angle(nativeRotation,rotation);
            if(angle>angularLimit && angle>0) { rotation=Quaternion.Slerp(nativeRotation,rotation,angularLimit/angle); RotationClamps++; }
        }
    }

    public sealed class VisualSmoother : IDisposable
    {
        sealed class Node { public Transform source, copy, originalParent; }
        sealed class MeshCopy
        {
            public Renderer source, copy;
            public MeshFilter sourceFilter, copyFilter;
            public SkinnedMeshRenderer sourceSkin, copySkin;
            public Transform[] nativeBones; public Transform nativeRootBone;
            public bool hadPropertyBlock;
            public VisualBinding owner;
            public Material[] materials;
            public bool originalForceOff;
            public readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        }
        sealed class LodCopy { public LODGroup source,copy; public int fingerprint; }
        readonly List<Node> nodes=new List<Node>();
        readonly List<MeshCopy> meshes=new List<MeshCopy>();
        readonly List<LodCopy> lods=new List<LodCopy>();
        readonly List<string> unsupported=new List<string>();
        readonly HashSet<Renderer> owned=new HashSet<Renderer>();
        readonly Dictionary<Transform,Transform> copies=new Dictionary<Transform,Transform>();
        readonly List<VisualBinding> bindings;
        readonly GameObject container;
        bool disposed;
        public int MeshCount { get { return meshes.Count; } }
        public int UnsupportedCount { get { return unsupported.Count; } }
        public bool Owns(Renderer renderer) { return owned.Contains(renderer); }
        public int NodeCount { get { return nodes.Count; } }
        public GameObject Container { get { return container; } }
        public IEnumerable<Renderer> ProxyRenderers
        { get { foreach(var m in meshes) yield return m.copy; } }
        public Transform CopyOf(Transform source)
        { Transform copy; return source && copies.TryGetValue(source,out copy) ? copy : null; }

        readonly VisualLayout.Layout layout;
        readonly List<Component> componentScratch=new List<Component>();
        readonly List<Material> materialScratch=new List<Material>();
        int watchIndex,meshIndex,lodIndex,maintenancePhase;

        public VisualSmoother(VisualLayout.Layout sourceLayout)
        {
            layout=sourceLayout; bindings=layout.bindings;
            if(!layout.car) throw new ArgumentNullException("sourceLayout.car");
            container=new GameObject("Bogie visual copies: "+layout.car.name);
            container.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(container);
        }

        // The runtime advances this iterator under its maintenance budget. Native
        // renderers stay visible until a complete proxy is ready for presentation.
        public IEnumerator BuildSteps()
        {
            foreach(var binding in bindings)
            {
                if(!binding.SourceRoot || !binding.Anchor) throw new VisualLayoutChangedException("Visual anchor replaced while mapping.");
                EnsureNode(binding.SourceRoot);
                yield return null;
            }
            foreach(var renderer in layout.renderers)
            {
                if(!renderer) throw new VisualLayoutChangedException("Source renderer replaced while mapping.");
                var binding=OwnerOf(renderer.transform);
                if(binding==null) continue;
                bool ambiguous=false;
                foreach(var root in layout.nativeOnly) if(renderer.transform==root || renderer.transform.IsChildOf(root)) ambiguous=true;
                if(ambiguous)
                { unsupported.Add("AMBIGUOUS_CONSTRAINT_OWNER "+PathOf(renderer.transform)); yield return null; continue; }
                var meshRenderer=renderer as MeshRenderer;
                var skin=renderer as SkinnedMeshRenderer;
                if(renderer.forceRenderingOff)
                { unsupported.Add("NATIVE_HIDDEN "+PathOf(renderer.transform)); yield return null; continue; }
                var entry=new MeshCopy {source=renderer,owner=binding,materials=renderer.sharedMaterials,
                    originalForceOff=renderer.forceRenderingOff};
                if(meshRenderer)
                {
                    var filter=renderer.GetComponent<MeshFilter>();
                    if(!filter || !filter.sharedMesh)
                    { unsupported.Add("NO_MESH "+PathOf(renderer.transform)); yield return null; continue; }
                    var target=EnsureNode(renderer.transform);
                    entry.sourceFilter=filter;
                    entry.copyFilter=target.gameObject.AddComponent<MeshFilter>();
                    entry.copyFilter.sharedMesh=filter.sharedMesh;
                    entry.copy=target.gameObject.AddComponent<MeshRenderer>();
                }
                else if(skin)
                {
                    var bones=skin.bones;
                    bool contained=skin.sharedMesh && bones.Length>0;
                    foreach(var bone in bones)
                        if(bone && !bone.IsChildOf(binding.SourceRoot) && bone!=binding.SourceRoot) contained=false;
                    if(!contained)
                    { unsupported.Add("SKIN_EXTERNAL_OR_MISSING_BONES "+PathOf(renderer.transform)); yield return null; continue; }
                    entry.nativeBones=(Transform[])bones.Clone(); entry.nativeRootBone=skin.rootBone;
                    for(int i=0;i<bones.Length;i++)
                    { if(bones[i]) bones[i]=EnsureNode(bones[i]); yield return null; }
                    if(!skin) throw new VisualLayoutChangedException("Skinned renderer replaced while mapping.");
                    var currentBones=skin.bones;
                    if(currentBones.Length!=entry.nativeBones.Length || skin.rootBone!=entry.nativeRootBone)
                        throw new VisualLayoutChangedException("Skin skeleton replaced while mapping.");
                    for(int i=0;i<currentBones.Length;i++)
                        if(currentBones[i]!=entry.nativeBones[i]) throw new VisualLayoutChangedException("Skin bones replaced while mapping.");
                    entry.sourceSkin=skin;
                    entry.copySkin=EnsureNode(renderer.transform).gameObject.AddComponent<SkinnedMeshRenderer>();
                    entry.copySkin.sharedMesh=skin.sharedMesh;
                    entry.copySkin.bones=bones;
                    entry.copySkin.rootBone=skin.rootBone ? EnsureNode(skin.rootBone) : null;
                    entry.copy=entry.copySkin;
                }
                else
                { unsupported.Add("NATIVE_RENDERER "+renderer.GetType().Name+" "+PathOf(renderer.transform)); yield return null; continue; }
                entry.copy.enabled=false; entry.copy.sharedMaterials=entry.materials;
                meshes.Add(entry); owned.Add(renderer);
                UpdateRadius(entry); CopyRendererState(entry);
                yield return null;
            }
            foreach(var source in layout.lods)
            {
                if(!source) throw new VisualLayoutChangedException("LOD source replaced while mapping.");
                var levels=source.GetLODs(); bool relevant=false;
                foreach(var level in levels) foreach(var renderer in level.renderers)
                    if(renderer && owned.Contains(renderer)) relevant=true;
                if(!relevant) continue;
                var entry=new LodCopy {source=source,fingerprint=LodFingerprint(levels)};
                entry.copy=EnsureNode(source.transform).gameObject.AddComponent<LODGroup>();
                for(int i=0;i<levels.Length;i++)
                {
                    var renderers=new List<Renderer>();
                    foreach(var renderer in levels[i].renderers)
                        foreach(var mesh in meshes) if(renderer==mesh.source) renderers.Add(mesh.copy);
                    levels[i].renderers=renderers.ToArray();
                }
                entry.copy.SetLODs(levels); CopyLod(source,entry.copy); lods.Add(entry);
                yield return null;
            }
            Present(0,true,false);
            container.SetActive(true);
        }

        static void UpdateRadius(MeshCopy mesh)
        {
            if(!mesh.owner.SourceRoot || !mesh.source || (!mesh.sourceSkin && (!mesh.sourceFilter || !mesh.sourceFilter.sharedMesh)))
                throw new VisualLayoutChangedException("Visual mesh replaced before bounds refresh.");
            // A world bounds sphere is conservative for the mesh at mapping time;
            // a margin covers ordinary child animation. Maintenance only grows it.
            var bounds=mesh.sourceSkin ? mesh.sourceSkin.localBounds : mesh.sourceFilter.sharedMesh.bounds;
            var matrix=mesh.source.transform.localToWorldMatrix;
            float radius=0;
            for(int corner=0;corner<8;corner++)
            {
                var offset=new Vector3((corner&1)==0 ? -bounds.extents.x : bounds.extents.x,
                    (corner&2)==0 ? -bounds.extents.y : bounds.extents.y,(corner&4)==0 ? -bounds.extents.z : bounds.extents.z);
                radius=Mathf.Max(radius,Vector3.Distance(mesh.owner.SourceRoot.position,matrix.MultiplyPoint3x4(bounds.center+offset)));
            }
            mesh.owner.Radius=Mathf.Max(mesh.owner.Radius,radius*1.05f+0.1f);
        }
        static int LodFingerprint(LOD[] levels)
        {
            unchecked
            {
                int hash=17;
                foreach(var level in levels)
                {
                    hash=hash*31+level.screenRelativeTransitionHeight.GetHashCode();
                    hash=hash*31+level.fadeTransitionWidth.GetHashCode();
                    foreach(var renderer in level.renderers) hash=hash*31+(renderer ? renderer.GetInstanceID() : 0);
                }
                return hash;
            }
        }
        // One cached node, mesh or LOD per call. No global searches or recursive
        // hierarchy checks in the recurring Update pass.
        public bool MaintainOne()
        {
            if(disposed) return false;
            if(maintenancePhase==0)
            {
                if(watchIndex<layout.watches.Count)
                {
                    var watch=layout.watches[watchIndex++];
                    if(!watch.source || watch.source.parent!=watch.parent || watch.source.childCount!=watch.children) return false;
                    componentScratch.Clear(); watch.source.GetComponents(componentScratch);
                    return VisualLayout.Fingerprint(componentScratch,this)==watch.fingerprint;
                }
                watchIndex=0; maintenancePhase=1;
            }
            if(maintenancePhase==1)
            {
                if(meshIndex<meshes.Count)
                {
                    var mesh=meshes[meshIndex++];
                    if(!mesh.source || !mesh.copy) return false;
                    RefreshMaterials(mesh);
                    if(mesh.sourceSkin)
                    {
                        if(mesh.sourceSkin.rootBone!=mesh.nativeRootBone) return false;
                        var bones=mesh.sourceSkin.bones;
                        if(bones.Length!=mesh.nativeBones.Length) return false;
                        for(int i=0;i<bones.Length;i++) if(bones[i]!=mesh.nativeBones[i]) return false;
                    }
                    UpdateRadius(mesh); CopyRendererState(mesh);
                    return true;
                }
                meshIndex=0; maintenancePhase=2;
            }
            if(lodIndex<lods.Count)
            {
                var lod=lods[lodIndex++];
                return lod.source && LodFingerprint(lod.source.GetLODs())==lod.fingerprint;
            }
            lodIndex=0; maintenancePhase=0; return true;
        }

        static void CopyRendererState(MeshCopy mesh)
        {
            mesh.copy.shadowCastingMode=mesh.source.shadowCastingMode;
            mesh.copy.receiveShadows=mesh.source.receiveShadows;
            mesh.copy.lightProbeUsage=mesh.source.lightProbeUsage;
            mesh.copy.reflectionProbeUsage=mesh.source.reflectionProbeUsage;
            mesh.copy.probeAnchor=mesh.source.probeAnchor;
            mesh.copy.sortingLayerID=mesh.source.sortingLayerID;
            mesh.copy.sortingOrder=mesh.source.sortingOrder;
            mesh.copy.motionVectorGenerationMode=mesh.source.motionVectorGenerationMode;
        }

        void RefreshMaterials(MeshCopy mesh)
        {
            materialScratch.Clear(); mesh.source.GetSharedMaterials(materialScratch);
            bool changed=materialScratch.Count!=mesh.materials.Length;
            if(!changed) for(int i=0;i<materialScratch.Count;i++) if(materialScratch[i]!=mesh.materials[i]) changed=true;
            if(changed) { mesh.materials=materialScratch.ToArray(); mesh.copy.sharedMaterials=mesh.materials; }
        }

        VisualBinding OwnerOf(Transform transform)
        {
            VisualBinding result=null;
            foreach(var binding in bindings)
                if(transform==binding.SourceRoot || transform.IsChildOf(binding.SourceRoot))
                    if(result==null || binding.SourceRoot.IsChildOf(result.SourceRoot)) result=binding;
            return result;
        }

        static string PathOf(Transform transform)
        {
            string path=transform.name;
            while(transform.parent) { transform=transform.parent; path=transform.name+"/"+path; }
            return path;
        }

        public void AppendCoverage(StringBuilder report)
        {
            foreach(var mesh in meshes)
            {
                if(!mesh.source || !mesh.copy) { report.AppendLine("STALE_MESH awaiting rebuild"); continue; }
                report.AppendLine("MESH "+PathOf(mesh.source.transform)+" type="+mesh.source.GetType().Name+
                    " owner="+mesh.owner.SourceRoot.GetInstanceID()+" radius="+mesh.owner.Radius.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"m sourceEnabled="+mesh.source.enabled+
                    " proxyEnabled="+mesh.copy.enabled+" active="+mesh.copy.gameObject.activeInHierarchy+
                    " sourceHidden="+mesh.source.forceRenderingOff);
            }
            foreach(var reason in unsupported) report.AppendLine(reason);
        }

        Transform EnsureNode(Transform source)
        {
            if(!source) throw new VisualLayoutChangedException("Visual source transform replaced while mapping.");
            Transform copy;
            if (copies.TryGetValue(source,out copy)) return copy;
            Transform parent=source.parent ? EnsureNode(source.parent) : container.transform;
            var obj=new GameObject(source.name);
            obj.layer=source.gameObject.layer;
            copy=obj.transform; copy.SetParent(parent,false);
            copies.Add(source,copy); nodes.Add(new Node {source=source,copy=copy,originalParent=source.parent});
            return copy;
        }

        public void Present(float time,bool paused,bool takeOver=true)
        {
            if(disposed) return;
            // Parents precede children. The source hierarchy, including animation and scale,
            // is copied exactly; only the proxy bogie/consumer root gets a pose adjustment.
            foreach(var node in nodes)
            {
                if(!node.source || node.source.parent!=node.originalParent)
                    throw new VisualLayoutChangedException("Visual source hierarchy was replaced.");
                if(!node.copy) throw new InvalidOperationException("Visual proxy hierarchy was destroyed.");
                node.copy.localPosition=node.source.localPosition;
                node.copy.localRotation=node.source.localRotation;
                node.copy.localScale=node.source.localScale;
                node.copy.gameObject.layer=node.source.gameObject.layer;
                if(node.copy.gameObject.activeSelf!=node.source.gameObject.activeSelf) node.copy.gameObject.SetActive(node.source.gameObject.activeSelf);
            }
            foreach(var binding in bindings)
            {
                Vector3 position; Quaternion rotation;
                if(binding.PresentationSample && binding.Available && !paused)
                {
                    var frame=binding.ReferenceFrame;
                    var inverse=Quaternion.Inverse(frame.rotation);
                    binding.History.SampleRendered(inverse*(binding.SourceRoot.position-frame.position),
                        inverse*binding.SourceRoot.rotation,time,Time.deltaTime);
                }
                if(!binding.Available || !binding.SourceRoot || !binding.Anchor ||
                    !binding.History.Evaluate(time,paused,out position,out rotation)) continue;
                if(binding.ReferenceFrame)
                {
                    position=binding.ReferenceFrame.position+binding.ReferenceFrame.rotation*position;
                    rotation=binding.ReferenceFrame.rotation*rotation;
                }
                var copy=copies[binding.SourceRoot];
                if(binding.TranslationOnly)
                { position=binding.SourceRoot.position+(position-binding.Anchor.position); rotation=binding.SourceRoot.rotation; }
                else if(binding.PresentationSample)
                {
                    if(!binding.FilterPosition) position=copy.position;
                    if(!binding.FilterRotation) rotation=copy.rotation;
                }
                binding.LimitPresentation(ref position,ref rotation);
                copy.SetPositionAndRotation(position,rotation);
            }
            foreach(var lod in lods)
            {
                if(!lod.source) throw new VisualLayoutChangedException("LOD source was destroyed.");
                CopyLod(lod.source,lod.copy);
            }
            foreach(var mesh in meshes)
            {
                if(!mesh.source || (!mesh.sourceSkin && !mesh.sourceFilter))
                    throw new VisualLayoutChangedException("Visual mesh was replaced.");
                if(!mesh.copy) throw new InvalidOperationException("Visual proxy renderer was destroyed.");
                bool show=takeOver && mesh.owner.Available && mesh.owner.History.Ready && mesh.source.gameObject.activeInHierarchy;
                if(mesh.sourceSkin)
                {
                    var skin=mesh.sourceSkin; var copy=mesh.copySkin;
                    if(copy.sharedMesh!=skin.sharedMesh) copy.sharedMesh=skin.sharedMesh;
                    copy.localBounds=skin.localBounds;
                    copy.quality=skin.quality;
                    copy.updateWhenOffscreen=skin.updateWhenOffscreen;
                    copy.skinnedMotionVectors=skin.skinnedMotionVectors;
                    if(skin.sharedMesh) for(int i=0;i<skin.sharedMesh.blendShapeCount;i++)
                        copy.SetBlendShapeWeight(i,skin.GetBlendShapeWeight(i));
                }
                else if(mesh.copyFilter.sharedMesh!=mesh.sourceFilter.sharedMesh) mesh.copyFilter.sharedMesh=mesh.sourceFilter.sharedMesh;
                // Most renderers have no overrides. Avoid retrieving one empty
                // block for every material on every copied mesh each frame.
                bool hasBlock=mesh.source.HasPropertyBlock();
                if(hasBlock || mesh.hadPropertyBlock)
                {
                    // A renderer may replace its material slots before its next
                    // maintenance turn. Indexed block reads must use current slots.
                    RefreshMaterials(mesh);
                    mesh.source.GetPropertyBlock(mesh.block);
                    mesh.copy.SetPropertyBlock(mesh.block.isEmpty ? null : mesh.block);
                    for(int i=0;i<mesh.materials.Length;i++)
                    {
                        mesh.source.GetPropertyBlock(mesh.block,i);
                        mesh.copy.SetPropertyBlock(mesh.block.isEmpty ? null : mesh.block,i);
                    }
                }
                mesh.hadPropertyBlock=hasBlock;
                bool enabled=show && mesh.source.enabled;
                if(mesh.copy.enabled!=enabled) mesh.copy.enabled=enabled;
                bool forceOff=show || mesh.originalForceOff;
                if(mesh.source.forceRenderingOff!=forceOff) mesh.source.forceRenderingOff=forceOff;
            }
        }

        static void CopyLod(LODGroup source,LODGroup target)
        {
            target.enabled=source.enabled;
            target.localReferencePoint=source.localReferencePoint;
            target.size=source.size;
            target.fadeMode=source.fadeMode;
            target.animateCrossFading=source.animateCrossFading;
        }

        public void Dispose()
        {
            if(disposed) return;
            foreach(var mesh in meshes)
            {
                if(mesh.source) mesh.source.forceRenderingOff=mesh.originalForceOff;
                if(mesh.copy) mesh.copy.enabled=false;
            }
            if(container) { container.SetActive(false); UnityEngine.Object.Destroy(container); }
            nodes.Clear(); meshes.Clear(); lods.Clear(); copies.Clear();
            disposed=true; // A failed restoration remains retryable by the Toolbox host.
        }
    }
}
