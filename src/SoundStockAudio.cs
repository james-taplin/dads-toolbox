// Adapted from Loco Sound Pitch 0.2.0 StockAudio.cs. Canonical SHA256: 4356499FE8E1035A9DB3309F8350CBC21FAA4FC2609DC2FEDF54BBC1D437E7BC
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using DV.ModularAudioCar;
using DV.Simulation.Ports;
using DV.ThingTypes;
using LocoSim.Definitions;
using UnityEngine;
using UnityEngine.Audio;
using UObject = UnityEngine.Object;

namespace DadsToolbox.LocoSound
{
    // References come only from native locomotive types. A clip's name is never
    // sufficient evidence that a custom/replacement clip is a stock sound.
    internal sealed class StockAudio
    {
        internal sealed class Donor
        {
            internal int Mask;
            internal string Id, Name;
            internal double Mass, Area;
            internal TrainCarType_v2 Type;
        }

        internal sealed class ClipInfo
        {
            internal int Donors;
            internal SoundKind Kind;
        }

        internal readonly List<Donor> Donors = new List<Donor>();
        internal readonly Dictionary<AudioClip, ClipInfo> Clips = new Dictionary<AudioClip, ClipInfo>();
        internal int AllDonors;
        internal double Area060, Area282;

        private readonly HashSet<AudioMixerGroup> cabGroups = new HashSet<AudioMixerGroup>();
        private readonly Dictionary<string, int> modules = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> cabinModules = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> exteriorModules = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<AudioManager> sharedManagers = new HashSet<AudioManager>();
        private static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, FieldInfo[]> effectFields = new Dictionary<Type, FieldInfo[]>();

        internal void Clear()
        {
            Donors.Clear(); Clips.Clear(); cabGroups.Clear(); modules.Clear();
            cabinModules.Clear(); exteriorModules.Clear(); sharedManagers.Clear();
            AllDonors = 0; Area060 = Area282 = 0;
        }

        internal bool Load()
        {
            Clear();

            // CCL_CarType derives from TrainCarType_v2. Exact type equality keeps
            // custom stock-derived configurations out of the native catalogue.
            // Globals loads its serialized type registry synchronously. A Resources
            // search can otherwise accept an early, incomplete subset of donors.
            var types = DV.Globals.G.Types.carTypes.Where(t => t && t.GetType() == typeof(TrainCarType_v2) && t.kind &&
                t.liveries != null && t.liveries.Any(l => l && (CarTypes.IsLocomotive(l) || CarTypes.IsSlug(l))))
                .OrderBy(t => t.id, StringComparer.Ordinal).Take(30).ToArray();
            var native = types.Select(t => t.liveries.Where(l => l && l.prefab).OrderBy(l => l.id, StringComparer.Ordinal).FirstOrDefault()).ToArray();
            if (native.Any(l => !l)) return false;

            foreach (var livery in native)
            {
                int mask = 1 << Donors.Count;
                var boiler = livery.prefab.GetComponentInChildren<BoilerDefinition>(true);
                double area = boiler ? PitchRules.Area(boiler.diameter, boiler.length) : 0;
                var donor = new Donor
                {
                    Mask = mask, Id = livery.id, Name = DisplayName(livery.id),
                    Mass = livery.parentType.mass, Area = area, Type = livery.parentType
                };
                Donors.Add(donor); AllDonors |= mask;
                if (livery.id == "LocoS060") Area060 = area;
                if (livery.id == "LocoS282A" || livery.id == "LocoS282B") Area282 = area;
                CaptureCabGroups(livery.parentType.audioPrefab);
            }

            foreach (var manager in UObject.FindObjectsOfType<AudioManager>())
                if (manager.cabGroup) cabGroups.Add(manager.cabGroup);

            for (int i = 0; i < native.Length; i++)
            {
                var visited = new HashSet<GameObject>();
                CaptureRoot(native[i].parentType.audioPrefab, Donors[i].Mask, visited);
                foreach (var livery in native[i].parentType.liveries)
                    if (livery)
                    {
                        CaptureRoot(livery.prefab, Donors[i].Mask, visited);
                        CaptureRoot(livery.externalInteractablesPrefab, Donors[i].Mask, visited);
                        CaptureRoot(livery.explodedExternalInteractablesPrefab, Donors[i].Mask, visited);
                    }
            }
            AddSharedReferences();
            return Donors.Count != 0 && Clips.Count != 0;
        }

        internal void AddSharedReferences()
        {
            if (AllDonors == 0) return;
            if (sharedManagers.Any(manager => manager)) return;
            foreach (var manager in UObject.FindObjectsOfType<AudioManager>())
            {
                sharedManagers.Add(manager);
                if (manager.cabGroup) cabGroups.Add(manager.cabGroup);
                // Includes collision/derailment, joints, coupling and splash
                // clips as well as rolling templates; ownership is checked by
                // the caller before any shared clip can affect a locomotive.
                foreach (var clip in ComponentClips(manager)) AddClip(clip, AllDonors, SoundKind.Unknown);
                var visited = new HashSet<GameObject>();
                foreach (var template in ReferencedLayered(manager))
                    if (template) CaptureRoot(template.gameObject, AllDonors, visited);
            }
        }

        internal bool IsCab(AudioMixerGroup group) => group && cabGroups.Contains(group);

        internal bool IsCab(LayeredAudio audio)
        {
            if (!audio) return false;
            if (IsCab(audio.audioMixerGroup) || CabinMetadata(audio)) return true;
            foreach (var layer in audio.layers ?? new LayeredAudio.Layer[0])
                if (layer != null && layer.source && IsCab(layer.source.outputAudioMixerGroup)) return true;
            // CCL can rename a copied CabFan and route it to a different mixer.
            // A signature also used by exterior machinery cannot prove cabin use.
            string signature = Signature(audio, true);
            return cabinModules.Contains(signature) && !exteriorModules.Contains(signature);
        }

        internal int MatchModule(LayeredAudio audio)
        {
            if (!audio) return 0;
            return modules.TryGetValue(Signature(audio, true), out int donors) ? donors : 0;
        }

        private static string DisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Unknown";
            return id.StartsWith("Loco", StringComparison.Ordinal) ? id.Substring(4) : id;
        }

        private void CaptureCabGroups(GameObject root)
        {
            if (!root) return;
            foreach (var audio in root.GetComponentsInChildren<LayeredAudio>(true))
            {
                string name = audio.name.Replace("_", "").Replace(" ", "");
                if (name.IndexOf("cabfan", StringComparison.OrdinalIgnoreCase) >= 0 && audio.audioMixerGroup)
                    cabGroups.Add(audio.audioMixerGroup);
            }
        }

        private void CaptureRoot(GameObject root, int donor, HashSet<GameObject> visited)
        {
            if (!root || !visited.Add(root)) return;
            var roles = Roles(root);
            foreach (var audio in root.GetComponentsInChildren<LayeredAudio>(true))
            {
                string signature = Signature(audio, false);
                modules.TryGetValue(signature, out int previous);
                modules[signature] = previous | donor;
                bool cabin = IsCab(audio.audioMixerGroup) || CabinMetadata(audio);
                (cabin ? cabinModules : exteriorModules).Add(signature);
            }
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component) continue;
                SoundKind kind = ComponentKind(component, root.transform, roles);
                foreach (var clip in ComponentClips(component)) AddClip(clip, donor, kind);
                // Some native templates (notably hose airflow and rolling) are
                // referenced assets, rather than children of the stock prefab.
                foreach (var template in ReferencedLayered(component))
                    if (template) CaptureRoot(template.gameObject, donor, visited);
                foreach (var effect in ReferencedEffects(component)) CaptureRoot(effect, donor, visited);
            }
            foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                AddClip(source.clip, donor, PitchRules.Classify(Path(source.transform, root.transform)));
        }

        private void AddClip(AudioClip clip, int donors, SoundKind kind)
        {
            if (!clip) return;
            // Cabin eligibility belongs to a playback occurrence. The same exact
            // Fan_01 clip may also be used for exterior machinery.
            if (kind == SoundKind.CabEquipment) kind = SoundKind.Unknown;
            if (!Clips.TryGetValue(clip, out var info))
                Clips.Add(clip, info = new ClipInfo { Kind = kind });
            info.Donors |= donors;
            if (info.Kind == SoundKind.Unknown) info.Kind = kind;
        }

        private static bool CabinMetadata(LayeredAudio audio)
        {
            if (PitchRules.Classify(audio.name) == SoundKind.CabEquipment) return true;
            foreach (var port in audio.GetComponents<LayeredAudioPortReader>())
                if (PitchRules.Classify(port.portId) == SoundKind.CabEquipment) return true;
            return false;
        }

        private static SoundKind ComponentKind(MonoBehaviour component, Transform root,
            Dictionary<LayeredAudio, SoundKind> roles)
        {
            if (component is ChuffClipsSimReader) return SoundKind.Chuffs;
            if (component is LayeredAudio audio && roles.TryGetValue(audio, out var role)) return role;
            if (component is AudioClipPortReader clipPort)
            {
                var kind = PitchRules.Classify(clipPort.portId);
                if (kind != SoundKind.Unknown) return kind;
            }
            if (component is LayeredAudioPortReader layerPort)
            {
                var kind = PitchRules.Classify(layerPort.portId);
                if (kind != SoundKind.Unknown) return kind;
            }
            return PitchRules.Classify(Path(component.transform, root));
        }

        private static Dictionary<LayeredAudio, SoundKind> Roles(GameObject root)
        {
            var roles = new Dictionary<LayeredAudio, SoundKind>();
            foreach (var port in root.GetComponentsInChildren<LayeredAudioPortReader>(true))
            {
                var kind = PitchRules.Classify(port.portId);
                if (kind != SoundKind.Unknown) SetRole(roles, port.GetComponent<LayeredAudio>(), kind);
            }
            foreach (var brakes in root.GetComponentsInChildren<BrakesAudioModule>(true))
            {
                SetRole(roles, brakes.brakeAudio, SoundKind.Brakes);
                SetRole(roles, brakes.brakeSquealAudio, SoundKind.Brakes);
                SetRole(roles, brakes.wheelSlidingAudio, SoundKind.Brakes);
                SetRole(roles, brakes.brakesOverheatingAudio, SoundKind.Brakes);
                SetRole(roles, brakes.brakeCylinderExhaustAudio, SoundKind.Ancillary);
                SetRole(roles, brakes.airflowAudio, SoundKind.Ancillary);
            }
            foreach (var chuffs in root.GetComponentsInChildren<ChuffClipsSimReader>(true))
                foreach (var loop in (chuffs.chuffLoops ?? new ChuffClipsSimReader.ChuffLoop[0])
                    .Concat(chuffs.waterChuffLoops ?? new ChuffClipsSimReader.ChuffLoop[0])
                    .Concat(chuffs.ashChuffLoops ?? new ChuffClipsSimReader.ChuffLoop[0]))
                    if (loop != null) SetRole(roles, loop.chuffLoop, SoundKind.Chuffs);
            return roles;
        }

        private static void SetRole(Dictionary<LayeredAudio, SoundKind> roles, LayeredAudio audio, SoundKind kind)
        {
            if (audio && (!roles.TryGetValue(audio, out var previous) || previous != SoundKind.CabEquipment))
                roles[audio] = kind;
        }

        private static string Path(Transform child, Transform stop)
        {
            string result = "";
            for (var t = child; t && t != stop; t = t.parent) result = t.name + "/" + result;
            return result;
        }

        private static FieldInfo[] Fields(Type type)
        {
            if (!fields.TryGetValue(type, out var result))
            {
                result = type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                    .Where(f => f.FieldType == typeof(AudioClip) || f.FieldType == typeof(AudioClip[]) ||
                        f.FieldType == typeof(AudioSource) || f.FieldType == typeof(AudioSource[]) ||
                        f.FieldType == typeof(LayeredAudio) || f.FieldType == typeof(LayeredAudio[])).ToArray();
                fields.Add(type, result);
            }
            return result;
        }

        private static IEnumerable<LayeredAudio> ReferencedLayered(MonoBehaviour component)
        {
            foreach (var field in Fields(component.GetType()))
            {
                if (field.FieldType == typeof(LayeredAudio))
                {
                    var audio = field.GetValue(component) as LayeredAudio;
                    if (audio) yield return audio;
                }
                else if (field.FieldType == typeof(LayeredAudio[]))
                    foreach (var audio in (LayeredAudio[])field.GetValue(component) ?? new LayeredAudio[0])
                        if (audio) yield return audio;
            }
        }

        private static IEnumerable<GameObject> ReferencedEffects(MonoBehaviour component)
        {
            var type = component.GetType();
            if (!effectFields.TryGetValue(type, out var entries))
            {
                var selected = new List<FieldInfo>();
                for (var current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
                    foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (field.FieldType == typeof(GameObject) && (field.Name == "explosionPrefab" || field.Name == "sparksPrefab" ||
                            field.Name == "firePrefab" || field.Name == "tmBlowPrefab" || field.Name == "blowbackParticlesPrefab")) selected.Add(field);
                entries = selected.ToArray(); effectFields[type] = entries;
            }
            foreach (var field in entries) { var effect = field.GetValue(component) as GameObject; if (effect) yield return effect; }
        }

        internal static IEnumerable<AudioClip> ComponentClips(MonoBehaviour component)
        {
            if (!component) yield break;
            if (component is LayeredAudio layered)
                foreach (var layer in layered.layers ?? new LayeredAudio.Layer[0])
                {
                    if (layer == null) continue;
                    if (layer.source && layer.source.clip) yield return layer.source.clip;
                    foreach (var clip in layer.clips ?? new AudioClip[0]) if (clip) yield return clip;
                }
            foreach (var field in Fields(component.GetType()))
            {
                if (field.FieldType == typeof(AudioClip))
                {
                    var clip = field.GetValue(component) as AudioClip;
                    if (clip) yield return clip;
                }
                else if (field.FieldType == typeof(AudioClip[]))
                {
                    foreach (var clip in (AudioClip[])field.GetValue(component) ?? new AudioClip[0])
                        if (clip) yield return clip;
                }
                else if (field.FieldType == typeof(AudioSource))
                {
                    var source = field.GetValue(component) as AudioSource;
                    if (source && source.clip) yield return source.clip;
                }
                else if (field.FieldType == typeof(AudioSource[]))
                {
                    foreach (var source in (AudioSource[])field.GetValue(component) ?? new AudioSource[0])
                        if (source && source.clip) yield return source.clip;
                }
            }
            if (component is ChuffClipsSimReader chuffs)
                foreach (var group in (chuffs.lowPressureClips ?? new ChuffClipsSimReader.OrderedChuffClips[0])
                    .Concat(chuffs.mediumPressureClips ?? new ChuffClipsSimReader.OrderedChuffClips[0])
                    .Concat(chuffs.highPressureClips ?? new ChuffClipsSimReader.OrderedChuffClips[0]))
                    foreach (var clip in group?.chuffVariations ?? new AudioClip[0]) if (clip) yield return clip;
        }

        internal static IEnumerable<AudioClip> EnumerateClips(GameObject root)
        {
            if (!root) yield break;
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (component) foreach (var clip in ComponentClips(component)) yield return clip;
            foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                if (source.clip) yield return source.clip;
        }

        private static string Signature(LayeredAudio audio, bool runtime)
        {
            var text = new StringBuilder();
            text.Append((int)audio.type).Append('|').Append(audio.linearPitchLerp).Append('|');
            Number(text, audio.minPitch); Number(text, audio.maxPitch);
            text.Append(audio.randomizeStartTime).Append('|');
            var layers = audio.layers ?? new LayeredAudio.Layer[0];
            text.Append(layers.Length).Append('|');
            foreach (var layer in layers)
            {
                if (layer == null) { text.Append("null|"); continue; }
                string name = layer.name ?? "";
                text.Append(name.Length).Append(':').Append(name).Append('|');
                var clips = layer.clips ?? new AudioClip[0];
                var sourceClip = layer.source ? layer.source.clip : null;
                // Reset may select any member of clips. That native random choice
                // does not change donor identity or the authored module signature.
                text.Append(sourceClip && !clips.Contains(sourceClip) ? sourceClip.GetInstanceID() : 0).Append('|');
                text.Append(clips.Length).Append('|');
                foreach (var clip in clips) text.Append(clip ? clip.GetInstanceID() : 0).Append('|');
                // Native prefab startPitch is not initialized. Reset captures it
                // from source.pitch; current runtime source.pitch is not stable.
                float start = runtime && layer.startPitch != 0 ? layer.startPitch : layer.source ? layer.source.pitch : 0;
                Number(text, start);
                Number(text, layer.pitchRange.x); Number(text, layer.pitchRange.y);
                text.Append(layer.usePitchCurve).Append('|').Append(layer.inertialPitch).Append('|');
                Number(text, layer.inertia);
                Curve(text, layer.volumeCurve); Curve(text, layer.pitchCurve);
            }
            return text.ToString();
        }

        private static void Number(StringBuilder text, float value)
            => text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');

        private static void Curve(StringBuilder text, AnimationCurve curve)
        {
            if (curve == null) { text.Append("null|"); return; }
            text.Append((int)curve.preWrapMode).Append('|').Append((int)curve.postWrapMode).Append('|');
            var keys = curve.keys;
            text.Append(keys.Length).Append('|');
            foreach (var key in keys)
            {
                Number(text, key.time); Number(text, key.value);
                Number(text, key.inTangent); Number(text, key.outTangent);
                Number(text, key.inWeight); Number(text, key.outWeight);
                text.Append((int)key.weightedMode).Append('|');
            }
        }
    }
}
