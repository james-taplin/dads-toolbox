// Adapted from Loco Sound Pitch 0.2.0 DirectAudio.cs. Canonical SHA256: CAF813364B48217D6F596E4EDD3A976381CE55EFBE6393B01D311E39C9706926
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DV.Simulation.Ports;
using UnityEngine;
using UnityEngine.Audio;

namespace DadsToolbox.LocoSound
{
    // Native effect prefabs can start an ordinary AudioSource in Awake without
    // passing through LayeredAudio or NAudio. Track only those authored sources.
    internal static class DirectAudio
    {
        private sealed class Binding
        {
            internal SoundEngine.Engine Engine;
            internal StockAudio Catalogue;
            internal AudioClip Clip;
            internal AudioMixerGroup Group;
            internal float BasePitch, ExpectedPitch, AppliedFactor = 1;
            internal int Donors;
            internal SoundKind Kind;
            internal bool Relinquished;
        }

        private static readonly Dictionary<AudioSource, Binding> bindings = new Dictionary<AudioSource, Binding>();
        private static readonly Dictionary<AudioSource, TrainCar> retired = new Dictionary<AudioSource, TrainCar>();
        private static readonly List<AudioSource> stale = new List<AudioSource>();

        internal static void Discover(SoundEngine.Engine engine, StockAudio catalogue, IEnumerable<GameObject> roots)
        {
            if (engine == null || !engine.Car || catalogue == null || roots == null) return;
            var scanRoots = roots.Where(root => root).Distinct().ToArray();
            var layeredSources = new HashSet<AudioSource>();
            foreach (var root in scanRoots)
                foreach (var audio in root.GetComponentsInChildren<LayeredAudio>(true))
                    foreach (var layer in audio.layers ?? new LayeredAudio.Layer[0])
                        if (layer != null && layer.source) layeredSources.Add(layer.source);

            foreach (var root in scanRoots)
                foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                {
                    // NAudio's pooled sources are created with playOnAwake=false.
                    // Retired bindings stay recorded until pool/removal cleanup so
                    // a repeat scan cannot adopt a changed pitch as a new baseline.
                    if (!source || !source.playOnAwake || bindings.ContainsKey(source) ||
                        !source.clip || !PitchRules.Valid(source.pitch) || layeredSources.Contains(source) ||
                        catalogue.IsCab(source.outputAudioMixerGroup)) continue;
                    if (retired.TryGetValue(source, out var retiredOwner))
                    {
                        if (retiredOwner == engine.Car) continue;
                        retired.Remove(source);
                    }
                    if (!catalogue.Clips.TryGetValue(source.clip, out var info) || info.Donors == 0) continue;
                    if (LayeredOwner(source) || CabinOwner(source.transform, engine.Car.transform)) continue;
                    var kind = SoundEngine.KindOf(source.transform, engine.Car.transform);
                    if (kind == SoundKind.CabEquipment) continue;
                    if (kind == SoundKind.Unknown) kind = info.Kind;
                    if (kind == SoundKind.CabEquipment) continue;
                    var binding = new Binding
                    {
                        Engine = engine, Catalogue = catalogue, Clip = source.clip,
                        Group = source.outputAudioMixerGroup, BasePitch = source.pitch,
                        ExpectedPitch = source.pitch, Donors = info.Donors, Kind = kind
                    };
                    bindings.Add(source, binding);
                    Apply(source, binding, false);
                }
        }

        internal static void Refresh(SoundEngine.Engine only = null)
        {
            foreach (var pair in bindings)
                if (only == null || ReferenceEquals(pair.Value.Engine, only)) Apply(pair.Key, pair.Value, false);
        }

        internal static void Remove(SoundEngine.Engine engine, bool endOwnership = true)
        {
            foreach (var pair in bindings.Where(pair => ReferenceEquals(pair.Value.Engine, engine)).ToArray())
            {
                Apply(pair.Key, pair.Value, true);
                if (!endOwnership && pair.Value.Relinquished) retired[pair.Key] = engine.Car;
                bindings.Remove(pair.Key);
            }
            if (endOwnership)
                foreach (var source in retired.Where(pair => pair.Value == engine.Car).Select(pair => pair.Key).ToArray()) retired.Remove(source);
        }

        internal static void Prune()
        {
            stale.Clear();
            foreach (var pair in bindings)
                if (!pair.Key || pair.Value.Engine == null || !pair.Value.Engine.Car) stale.Add(pair.Key);
            foreach (var source in stale)
            {
                Apply(source, bindings[source], true);
                bindings.Remove(source);
            }
            stale.Clear();
            foreach (var pair in retired)
                if (!pair.Key || !pair.Value) stale.Add(pair.Key);
            foreach (var source in stale) retired.Remove(source);
            stale.Clear();
        }

        internal static void Clear(bool endOwnership = true)
        {
            foreach (var pair in bindings)
            {
                Apply(pair.Key, pair.Value, true);
                if (!endOwnership && pair.Value.Relinquished) retired[pair.Key] = pair.Value.Engine.Car;
            }
            bindings.Clear();
            if (endOwnership) retired.Clear();
            stale.Clear();
        }

        internal static void AppendReport(StringBuilder report)
        {
            foreach (var pair in bindings)
                report.AppendLine($"Direct effect | {pair.Value.Engine.Car?.carLivery?.id} | clip={pair.Value.Clip?.name} | donors={pair.Value.Donors} | factor={pair.Value.AppliedFactor:F4} | relinquished={pair.Value.Relinquished}");
        }

        private static void Apply(AudioSource source, Binding binding, bool restore)
        {
            if (binding.Relinquished || !source || (!restore && (binding.Engine == null || !binding.Engine.Car))) return;
            // Another mod replacing a clip, mixer route or pitch owns the source
            // from that point onward. Never restore through foreign changes.
            if (!source.playOnAwake || source.clip != binding.Clip || source.outputAudioMixerGroup != binding.Group ||
                binding.Catalogue.IsCab(source.outputAudioMixerGroup) || !Mathf.Approximately(source.pitch, binding.ExpectedPitch))
            {
                binding.Relinquished = true;
                return;
            }
            float factor = restore ? 1 : SoundEngine.Factor(binding.Engine, binding.Donors, binding.Kind);
            if (!PitchRules.Valid(factor)) factor = 1;
            float target = Mathf.Min(3, binding.BasePitch * factor);
            if (!Mathf.Approximately(source.pitch, target)) source.pitch = target;
            binding.ExpectedPitch = source.pitch;
            binding.AppliedFactor = factor;
        }

        private static bool LayeredOwner(AudioSource source)
        {
            foreach (var audio in source.GetComponentsInParent<LayeredAudio>(true))
                foreach (var layer in audio.layers ?? new LayeredAudio.Layer[0])
                    if (layer != null && layer.source == source) return true;
            return false;
        }

        private static bool CabinOwner(Transform source, Transform stop)
        {
            for (var node = source; node && node != stop; node = node.parent)
                foreach (var component in node.GetComponents<MonoBehaviour>())
                {
                    if (!component) continue;
                    string type = component.GetType().Name;
                    if (type == "LeverAudio" || type == "PullerAudio" || type == "RotaryAudio" ||
                        type == "ToggleSwitchAudio" || type == "WiperAudio" || type == "LampControl" ||
                        type.IndexOf("radio", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (component is AudioClipPortReader clipPort && PitchRules.Classify(clipPort.portId) == SoundKind.CabEquipment) return true;
                    if (component is LayeredAudioPortReader layeredPort && PitchRules.Classify(layeredPort.portId) == SoundKind.CabEquipment) return true;
                }
            return false;
        }
    }
}
