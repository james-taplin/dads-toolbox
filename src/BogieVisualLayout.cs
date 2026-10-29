using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DV.Util;
using UnityEngine;
using UnityEngine.Animations;

namespace DadsToolbox.BogieVisuals
{
    // Only discovery/identity data. No source object is modified.
    public static class VisualLayout
    {
        static readonly FieldInfo Provider=typeof(PositionSyncConsumer).GetField("providerTransform",BindingFlags.NonPublic|BindingFlags.Instance);
        public static Transform ProviderOf(PositionSyncConsumer consumer)
        { return Provider==null ? null : Provider.GetValue(consumer) as Transform; }

        public sealed class Watch
        {
            public Transform source, parent;
            public int children, fingerprint;
        }
        public sealed class Layout
        {
            public TrainCar car;
            public readonly List<Watch> watches=new List<Watch>();
            public readonly List<Renderer> renderers=new List<Renderer>();
            public readonly List<LODGroup> lods=new List<LODGroup>();
            public readonly List<VisualBinding> bindings=new List<VisualBinding>();
            public readonly List<Transform> nativeOnly=new List<Transform>();
        }
        sealed class ConstraintRoot
        {
            public Transform root;
            public Bogie owner,fallbackOwner;
            public bool position, rotation, ambiguous;
        }
        public static bool SupportedConstraint(IConstraint constraint,out bool position,out bool rotation)
        {
            position=constraint is PositionConstraint || constraint is ParentConstraint;
            rotation=constraint is AimConstraint || constraint is LookAtConstraint || constraint is RotationConstraint || constraint is ParentConstraint;
            return constraint.constraintActive && constraint.weight>0 && (position || rotation);
        }
        public static int Fingerprint(List<Component> components,VisualSmoother smoother)
        {
            unchecked
            {
                int hash=17;
                foreach(var component in components)
                {
                    if(!component) continue;
                    var constraint=component as IConstraint;
                    if(component is Bogie || component is Renderer || component is MeshFilter || component is LODGroup ||
                        component is PositionSyncConsumer || constraint!=null)
                        hash=hash*31+component.GetInstanceID();
                    var consumer=component as PositionSyncConsumer;
                    if(consumer) { var provider=ProviderOf(consumer); hash=hash*31+(provider ? provider.GetInstanceID() : 0); }
                    var renderer=component as Renderer;
                    if(renderer) hash=hash*31+((smoother==null || !smoother.Owns(renderer)) && renderer.forceRenderingOff ? 1 : 0);
                    if(constraint!=null)
                    {
                        hash=hash*31+(constraint.constraintActive && constraint.weight>0 ? 1 : 0);
                        for(int i=0;i<constraint.sourceCount;i++)
                        { var source=constraint.GetSource(i).sourceTransform; hash=hash*31+(source ? source.GetInstanceID() : 0); }
                    }
                }
                return hash;
            }
        }
        static int Depth(Transform node)
        { int depth=0; while(node.parent) { depth++; node=node.parent; } return depth; }
        static Bogie Owner(Transform node)
        {
            var bogie=node.GetComponentInParent<Bogie>();
            if(bogie) return bogie;
            var consumer=node.GetComponentInParent<PositionSyncConsumer>();
            var provider=consumer ? ProviderOf(consumer) : null;
            return provider ? provider.GetComponentInParent<Bogie>() : null;
        }
        public static IEnumerator Discover(Layout layout)
        {
            var stack=new Stack<Transform>();
            var components=new List<Component>();
            var consumers=new List<PositionSyncConsumer>();
            var constrained=new Dictionary<Transform,ConstraintRoot>();
            stack.Push(layout.car.transform);
            while(stack.Count>0)
            {
                var node=stack.Pop();
                if(!node) continue;
                // Nested TrainCars have separate native ownership.
                var nested=node.GetComponent<TrainCar>();
                if(nested && nested!=layout.car) continue;
                components.Clear(); node.GetComponents(components);
                layout.watches.Add(new Watch {source=node,parent=node.parent,children=node.childCount,fingerprint=Fingerprint(components,null)});
                foreach(var component in components)
                {
                    var bogie=component as Bogie;
                    if(bogie) layout.bindings.Add(new VisualBinding(node,node,false,bogie));
                    var consumer=component as PositionSyncConsumer;
                    if(consumer) consumers.Add(consumer);
                    var renderer=component as Renderer;
                    if(renderer) layout.renderers.Add(renderer);
                    var lod=component as LODGroup;
                    if(lod) layout.lods.Add(lod);
                    var constraint=component as IConstraint;
                    bool position,rotation;
                    if(constraint!=null && SupportedConstraint(constraint,out position,out rotation))
                    {
                        ConstraintRoot root;
                        if(!constrained.TryGetValue(node,out root))
                        { root=new ConstraintRoot {root=node,fallbackOwner=Owner(node)}; constrained.Add(node,root); }
                        root.position|=position; root.rotation|=rotation;
                        for(int i=0;i<constraint.sourceCount;i++)
                        {
                            var source=constraint.GetSource(i).sourceTransform;
                            var candidate=source ? Owner(source) : null;
                            if(candidate && candidate.GetComponentInParent<TrainCar>()==layout.car)
                            {
                                if(root.owner && root.owner!=candidate) root.ambiguous=true;
                                else root.owner=candidate;
                            }
                        }
                    }
                }
                for(int i=node.childCount-1;i>=0;i--) stack.Push(node.GetChild(i));
                yield return null;
            }
            foreach(var consumer in consumers)
            {
                if(!consumer) continue;
                var provider=ProviderOf(consumer); var owner=provider ? Owner(provider) : null;
                if(owner && owner.GetComponentInParent<TrainCar>()==layout.car)
                {
                    bool exists=false;
                    foreach(var binding in layout.bindings) if(binding.SourceRoot==consumer.transform) exists=true;
                    if(!exists) layout.bindings.Add(new VisualBinding(consumer.transform,provider,true,owner));
                }
                yield return null;
            }
            foreach(var pair in constrained)
            {
                var root=pair.Value;
                if(!root.owner) root.owner=root.fallbackOwner;
                if(root.ambiguous) layout.nativeOnly.Add(root.root);
                if(root.owner && !root.ambiguous && root.owner.GetComponentInParent<TrainCar>()==layout.car)
                {
                    // A distinct visual root is sampled after native constraints. The
                    // native physics root continues to use its fixed-step history.
                    VisualBinding existing=null;
                    foreach(var binding in layout.bindings) if(binding.SourceRoot==root.root) existing=binding;
                    // A consumer can also have an orientation constraint. Its
                    // final visual pose then takes priority over provider-only sampling.
                    if(existing==null || existing.TranslationOnly)
                    {
                        if(existing!=null) layout.bindings.Remove(existing);
                        layout.bindings.Add(new VisualBinding(root.root,root.root,false,root.owner)
                            {PresentationSample=true,FilterPosition=root.position || existing!=null,FilterRotation=root.rotation});
                    }
                }
                yield return null;
            }
            // Validate before Sort: comparer exceptions would wrap the expected
            // invalidation and send it through the genuine-failure retry path.
            foreach(var binding in layout.bindings)
                if(!binding.SourceRoot) throw new VisualLayoutChangedException("Visual owner replaced while mapping.");
            layout.bindings.Sort((a,b)=>Depth(a.SourceRoot).CompareTo(Depth(b.SourceRoot)));
            foreach(var binding in layout.bindings) binding.ReferenceFrame=layout.car.transform;
        }
    }
}
