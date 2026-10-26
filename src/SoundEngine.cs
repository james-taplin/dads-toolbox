// Adapted from Loco Sound Pitch 0.2.0 Main.cs. Canonical SHA256: 25D7D33AC46C1DC0B144AABD6E6C4B320929ADE8E8F69996150D7C47FF721A8F
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.IO;
using DV.ModularAudioCar;
using DV.Simulation.Ports;
using DV.ThingTypes;
using HarmonyLib;
using LocoSim.Definitions;
using UnityEngine;
using UnityEngine.Audio;
using UObject = UnityEngine.Object;

namespace DadsToolbox.LocoSound
{
    internal static class SoundEngine
    {
        internal static SoundOptions Config;
        internal static Action<string> Logger;
        internal static string DiagnosticsDirectory;
        internal static bool Enabled;
        internal static bool Restoring;
        private static Vector2 scroll;
        private static bool showDetails;
        private static readonly FieldInfo CarField = AccessTools.Field(typeof(TrainAudio), "car");
        internal static readonly Dictionary<LayeredAudio.Layer, Binding> Layers = new Dictionary<LayeredAudio.Layer, Binding>();
        private static readonly Dictionary<Transform, Engine> Roots = new Dictionary<Transform, Engine>();
        private static readonly StockAudio Catalogue = new StockAudio();
        private static Dictionary<AudioClip, StockAudio.ClipInfo> Clips => Catalogue.Clips;
        private static readonly List<Engine> Engines = new List<Engine>();
        private static readonly HashSet<LayeredAudio> Layered = new HashSet<LayeredAudio>();
        private static readonly HashSet<string> Logged = new HashSet<string>();
        private static bool referencesReady;
        private static bool dirty = true;
        private static float nextReferenceRetry;
        private static float nextCleanup;
        private static readonly List<LayeredAudio.Layer> StaleLayers = new List<LayeredAudio.Layer>();
        private static readonly List<Component> StaleCallers = new List<Component>();
        private static readonly List<Engine> StaleEngines = new List<Engine>();
        // Per-car discovery: a car-audio setup event queues only that car (see ProcessPending / ScanCar).
        private static readonly Queue<TrainAudio> Pending = new Queue<TrainAudio>();
        private static readonly HashSet<TrainAudio> Queued = new HashSet<TrainAudio>();
        private static readonly Dictionary<TrainAudio, Engine> ByAudio = new Dictionary<TrainAudio, Engine>();
        private static float nextTrace;
        private static bool traceStarted;
        internal static bool ManualRefresh;
        private static string status = "Waiting for the stock locomotive prefabs.";
        internal static Engine Context;
        internal static SoundKind ContextKind;
        internal static int ContextDonors;

        internal static void Initialize(SoundOptions options, Action<string> logger, string diagnosticsDirectory)
        {
            Enabled = false;
            Clear();
            Catalogue.Clear(); Logged.Clear(); referencesReady = false;
            Config = options;
            Logger = logger;
            DiagnosticsDirectory = diagnosticsDirectory;
            status = "Waiting for the stock locomotive prefabs.";
            ContextKind = SoundKind.Unknown; Restoring = false;
            scroll = default(Vector2); showDetails = false;
            if (CarField == null) throw new MissingFieldException("DV audio layout has changed.");
        }
        internal static void Release()
        {
            Clear(); Catalogue.Clear(); Logged.Clear(); referencesReady = false;
            ContextKind = SoundKind.Unknown; Restoring = false;
            Config = null; Logger = null; DiagnosticsDirectory = null;
        }
        internal static string Status => status;
        private static void Log(string value) { try { Logger?.Invoke(value); } catch { } }
        internal static bool Draw()
        {
            bool before = GUI.changed;
            GUI.changed = false;
            GUILayout.Label("Steam: boiler barrel area. Smaller than the sound donor = higher pitch; larger = lower.");
            GUILayout.Label($"Steam effect strength: {Config.Strength * 100:F1}%   (0% = unchanged; 100% = normal)");
            Config.Strength = GUILayout.HorizontalSlider(Config.Strength, 0, 10);
            GUILayout.BeginHorizontal();
            Preset("Off", 0); Preset("Subtle", 0.15f); Preset("Normal", 1); Preset("Strong", 3); Preset("Extreme", 10);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Steam lower pitch cap: {Config.MinimumPitch * 100:F0}% of original");
            Config.MinimumPitch = GUILayout.HorizontalSlider(Config.MinimumPitch, 0.1f, 1);
            GUILayout.Label($"Steam upper pitch cap: {Config.MaximumPitch * 100:F0}% of original");
            Config.MaximumPitch = GUILayout.HorizontalSlider(Config.MaximumPitch, 1, 3);
            GUILayout.Label($"Half / twice donor boiler area: {PitchRules.Multiplier(.5, 1, Config.Strength, Config.MinimumPitch, Config.MaximumPitch):F3}x / {PitchRules.Multiplier(2, 1, Config.Strength, Config.MinimumPitch, Config.MaximumPitch):F3}x pitch");
            GUILayout.Space(8);
            GUILayout.Label("Diesel / hydraulic / mechanical / electric: configured empty locomotive mass.");
            GUILayout.Label($"Diesel / non-steam effect strength: {Config.NonSteamStrength * 100:F1}%   (heavier = lower; lighter = higher)");
            Config.NonSteamStrength = GUILayout.HorizontalSlider(Config.NonSteamStrength, 0, 10);
            GUILayout.Label($"Diesel / non-steam lower pitch cap: {Config.NonSteamMinimumPitch * 100:F0}% of original");
            Config.NonSteamMinimumPitch = GUILayout.HorizontalSlider(Config.NonSteamMinimumPitch, 0.1f, 1);
            GUILayout.Label($"Diesel / non-steam upper pitch cap: {Config.NonSteamMaximumPitch * 100:F0}% of original");
            Config.NonSteamMaximumPitch = GUILayout.HorizontalSlider(Config.NonSteamMaximumPitch, 1, 3);
            GUILayout.Label($"Half / twice donor mass: {PitchRules.MassMultiplier(0.5, 1, Config.NonSteamStrength, Config.NonSteamMinimumPitch, Config.NonSteamMaximumPitch):F3}x / {PitchRules.MassMultiplier(2, 1, Config.NonSteamStrength, Config.NonSteamMinimumPitch, Config.NonSteamMaximumPitch):F3}x pitch");
            GUILayout.Space(8);
            GUILayout.Label("Steam sound options (all default exterior groups are enabled for non-steam locomotives):");
            GUILayout.BeginHorizontal();
            Config.Chuffs = GUILayout.Toggle(Config.Chuffs, "Chuffs");
            Config.Gear = GUILayout.Toggle(Config.Gear, "Clanks / valve gear");
            Config.Rolling = GUILayout.Toggle(Config.Rolling, "Rolling");
            Config.Brakes = GUILayout.Toggle(Config.Brakes, "Brake squeal");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Config.Cocks = GUILayout.Toggle(Config.Cocks, "Cylinder cocks");
            Config.Whistle = GUILayout.Toggle(Config.Whistle, "Stock whistles");
            Config.ChuffLoops = GUILayout.Toggle(Config.ChuffLoops, "High-speed chuff loops");
            GUILayout.EndHorizontal();
            GUILayout.Label("Pitch also changes sample playback speed. Extreme settings stretch chuffs; loops can sound out of sync.");
            GUILayout.Label("Continuous sounds update live. One-shot sounds use the new settings on their next play.");
            GUILayout.Label("Default exterior auxiliaries and unclassified effects are included. Cab equipment and custom clips are excluded.");
            GUILayout.Label("Incompatible with Zeibach's Sounds (ZSounds). Use only one of these mods.");
            GUILayout.Label("Steam shared-clip fallback (per-locomotive donor choices below take priority):");
            Config.SharedDonor = GUILayout.SelectionGrid(Config.SharedDonor, new[] { "Auto (unresolved sounds unchanged)", "S060", "S282" }, 3);
            if (GUI.changed) Refresh();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan loaded locomotives")) { referencesReady = false; dirty = true; }
            if (GUILayout.Button("Write diagnostic report")) WriteReport();
            GUILayout.EndHorizontal();
            GUILayout.Label(status);
            Config.TraceAncillaries = GUILayout.Toggle(Config.TraceAncillaries, "Record dynamo pitch/volume and patch-write counts to SteamSizePitch-dynamo-trace.csv");
            showDetails = GUILayout.Toggle(showDetails, "Show loaded locomotives / shared sound donors");
            if (showDetails)
            {
                GUILayout.Label("Shared sounds: choose their vanilla donor if Auto reports ambiguity. Unique clip matches retain their own donor.");
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(240));
                foreach (var engine in Engines)
                {
                    GUILayout.Label(Describe(engine));
                    if (!engine.Stock) DrawDonorOverride(engine);
                }
                GUILayout.EndScrollView();
            }
            bool changed = GUI.changed;
            GUI.changed |= before;
            return changed;
        }

        private static float SafeSetting(float value, float fallback, float min, float max) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        private static void DrawDonorOverride(Engine engine)
        {
            string id = engine.Car.carLivery.id;
            var setting = Config.DonorOverrides.FirstOrDefault(x => x != null && x.LiveryId == id);
            var options = new[] { "Auto" }.Concat(Catalogue.Donors.Select(d => d.Name)).ToArray();
            int selected = setting == null ? 0 : Catalogue.Donors.FindIndex(d => d.Id == setting.DonorId) + 1;
            int next = GUILayout.SelectionGrid(selected, options, 4);
            if (next == selected) return;
            if (setting != null) Config.DonorOverrides.Remove(setting);
            if (next > 0) Config.DonorOverrides.Add(new DonorOverride { LiveryId = id, DonorId = Catalogue.Donors[next - 1].Id });
            Refresh();
        }

        private static void Preset(string name, float value)
        {
            if (GUILayout.Button(name)) { Config.Strength = value; GUI.changed = true; }
        }

        internal static void Tick()
        {
            if (!Enabled) return;
            try
            {
                // Retry only while stock prefabs are unavailable. Once initialized, discovery
                // is driven by SetupForCar, pooling, re-enable and the manual button.
                if (dirty || (!referencesReady && Time.unscaledTime >= nextReferenceRetry))
                {
                    dirty = false;
                    nextReferenceRetry = Time.unscaledTime + 5;
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    Scan();
                    if (referencesReady) Log($"Audio discovery: {Engines.Count} locomotive instances, {timer.Elapsed.TotalMilliseconds:F1} ms (event requested).");
                }
                if (Time.unscaledTime >= nextCleanup) { nextCleanup = Time.unscaledTime + 1; PruneDestroyed(); }
                if (Config.TraceAncillaries && Engines.Count != 0 && Time.unscaledTime >= nextTrace)
                { nextTrace = Time.unscaledTime + 5; TraceDynamos(); }
            }
            catch (Exception ex)
            {
                status = "Scan failed; see UMM log: " + ex.Message;
                if (Logged.Add(ex.ToString())) Log(ex.ToString());
            }
        }
        internal static void RequestScan() => dirty = true;

        private static void Clear()
        {
            DirectAudio.Clear();
            Layers.Clear(); Roots.Clear(); Engines.Clear(); Layered.Clear();
            Pending.Clear(); Queued.Clear(); ByAudio.Clear();
            Context = null;
            ContextDonors = 0;
            ManualRefresh = false; nextTrace = 0; traceStarted = false;
            dirty = true; nextReferenceRetry = 0; nextCleanup = 0;
            StaleLayers.Clear(); StaleCallers.Clear(); StaleEngines.Clear();
        }

        private static void Scan()
        {
            if (referencesReady && (Catalogue.Donors.Any(d => !d.Type) || Clips.Keys.Any(c => !c))) referencesReady = false;
            if (!referencesReady && !LoadReferences()) return;
            AddRollingReferences();
            // Restore before replacing ownership, including objects returned to pools.
            Restoring = true;
            try { Refresh(); } finally { Restoring = false; }
            DirectAudio.Clear(false);
            var previousLayers = new Dictionary<LayeredAudio.Layer, Binding>(Layers);
            Layers.Clear(); Roots.Clear(); Engines.Clear(); Layered.Clear(); ByAudio.Clear();
            foreach (var audio in UObject.FindObjectsOfType<TrainAudio>()) BuildEngine(audio, previousLayers);
            // A full scan covers every car, so anything queued by setup events is already done.
            Pending.Clear(); Queued.Clear();
            UpdateStatus();
            Refresh();
        }

        // Ownership is car-wide: external hose/coupler modules are outside TrainAudio.
        private static Engine BuildEngine(TrainAudio audio, Dictionary<LayeredAudio.Layer, Binding> previousLayers)
        {
            var car = CarField.GetValue(audio) as TrainCar;
            if (!car || !car.gameObject.scene.IsValid() || !car.carLivery || !car.carLivery.parentType || !IsLocomotive(car)) return null;
            var engine = RegisterEngine(audio, car);
            var roots = AudioRoots(car, audio).ToArray();
            // Count exact stock clip matches. Custom clips never become stock just because of their names.
            var observed = roots.SelectMany(StockAudio.EnumerateClips).Distinct().Where(c => c).ToArray();
            foreach (var clip in observed)
            {
                if (Clips.TryGetValue(clip, out var info)) engine.StockClips++;
            }
            var roles = new Dictionary<LayeredAudio, SoundKind>();
            foreach (var root in roots)
                foreach (var role in ExplicitRoles(root)) SetRole(roles, role.Key, role.Value);
            foreach (var layered in roots.SelectMany(root => root.GetComponentsInChildren<LayeredAudio>(true)).Distinct())
            {
                Layered.Add(layered);
                engine.Nodes.Add(layered);
                SoundKind kind = Catalogue.IsCab(layered) ? SoundKind.CabEquipment : roles.TryGetValue(layered, out var explicitKind) ? explicitKind : KindOf(layered.transform, car.transform);
                int moduleDonors = Catalogue.MatchModule(layered);
                if (kind == SoundKind.Propulsion || SteamKind(kind))
                {
                    Vote(engine, moduleDonors, 4);
                    foreach (var clip in StockAudio.ComponentClips(layered).Distinct())
                        if (Clips.TryGetValue(clip, out var candidate)) Vote(engine, candidate.Donors, 1);
                }
                bool chuffLoop = layered.GetComponentInParent<ChuffClipsSimReader>() != null || kind == SoundKind.Chuffs;
                foreach (var layer in layered.layers ?? new LayeredAudio.Layer[0])
                {
                    if (layer == null || !layer.source) continue;
                    var binding = BindLayer(engine, layered, layer, kind, moduleDonors, chuffLoop);
                    var clip = layer.source.clip;
                    if (previousLayers.TryGetValue(layer, out var previous) && previous.Engine.Car == car && previous.Source == layer.source && previous.Clip == clip)
                    { binding.Input = previous.Input; binding.InputLinear = previous.InputLinear; binding.InputMin = previous.InputMin; binding.InputMax = previous.InputMax;
                      binding.AppliedMultiplier = previous.AppliedMultiplier; binding.ShiftWrites = previous.ShiftWrites; binding.RefreshWrites = previous.RefreshWrites; }
                }
            }
            if (!engine.Stock) DirectAudio.Discover(engine, Catalogue, roots);
            string key = car.GetInstanceID() + ":" + engine.StockClips + ":" + engine.Area + ":" + engine.Mass;
            if (Logged.Add(key))
            {
                Log(Describe(engine));
                foreach (var pair in Layers.Where(p => p.Value.Engine == engine && DynamoBinding(p.Value)))
                    Log($"Dynamo audio: {car.carLivery.id}; clip={pair.Value.Clip?.name}; stockClip={pair.Value.Donors != 0}; category={pair.Value.Kind}; pitch={pair.Key.source.pitch:F3}; volume={pair.Key.source.volume:F3}; patch writes={pair.Value.ShiftWrites}.");
            }

            return engine;
        }

        private static IEnumerable<GameObject> AudioRoots(TrainCar car, TrainAudio audio)
        {
            yield return car.gameObject;
            if (car.interior) yield return car.interior.gameObject;
            if (!audio.transform.IsChildOf(car.transform) && (!car.interior || !audio.transform.IsChildOf(car.interior))) yield return audio.gameObject;
        }

        private static Engine RegisterEngine(TrainAudio audio, TrainCar car)
        {
            var boiler = car.GetComponentInChildren<BoilerDefinition>(true);
            var engine = new Engine { Car = car, Audio = audio, Steam = boiler,
                Area = boiler ? PitchRules.Area(boiler.diameter, boiler.length) : 0,
                Mass = car.carLivery.parentType.mass, Stock = !IsCcl(car.carLivery.parentType) };
            ByAudio[audio] = engine; Roots[audio.transform] = engine; Roots[car.transform] = engine;
            if (car.interior) Roots[car.interior] = engine;
            Engines.Add(engine);
            return engine;
        }

        internal static void PrepareCar(TrainAudio audio, TrainCar car)
        {
            if (!Enabled || !audio || !car || !car.carLivery || !car.carLivery.parentType || !IsLocomotive(car) || !IsCcl(car.carLivery.parentType)) return;
            DetachCar(audio);
            // SetupForCar initializes native readers before its postfix. Register cheap
            // ownership first so the original SetPitch arguments can be captured.
            RegisterEngine(audio, car);
        }

        private static bool IsCcl(TrainCarType_v2 type) => type && (type.GetType().Namespace ?? "").StartsWith("CCL.", StringComparison.Ordinal);
        private static bool IsLocomotive(TrainCar car) => car.IsLoco || CarTypes.IsSlug(car.carLivery);
        private static bool SteamKind(SoundKind kind) => kind == SoundKind.Chuffs || kind == SoundKind.Gear || kind == SoundKind.Cocks;

        private static void Vote(Engine engine, int mask, int count)
        {
            if (mask == 0 || (mask & (mask - 1)) != 0) return;
            engine.Votes.TryGetValue(mask, out int before);
            engine.Votes[mask] = before + count;
        }

        private static Binding BindLayer(Engine engine, LayeredAudio audio, LayeredAudio.Layer layer, SoundKind kind, int moduleDonors, bool loop)
        {
            var clip = layer.source.clip;
            StockAudio.ClipInfo info = null;
            if (clip) Clips.TryGetValue(clip, out info);
            var binding = new Binding { Engine = engine, Audio = audio, Source = layer.source, Clip = clip,
                Kind = kind == SoundKind.Unknown ? info?.Kind ?? SoundKind.Unknown : kind,
                Donors = info?.Donors ?? 0, ModuleDonors = moduleDonors, Loop = loop };
            if (clip) binding.AllowedClips.Add(clip);
            foreach (var candidate in layer.clips ?? new AudioClip[0]) if (candidate) binding.AllowedClips.Add(candidate);
            Layers[layer] = binding;
            engine.LayerKeys.Add(layer);
            engine.LayerCount++;
            return binding;
        }

        internal static bool TryBinding(LayeredAudio.Layer layer, out Binding binding)
        {
            if (Layers.TryGetValue(layer, out binding)) return true;
            if (!Enabled || !layer.source) return false;
            var engine = Owner(layer.source.transform);
            if (engine == null || engine.Stock) return false;
            var audio = layer.source.GetComponentInParent<LayeredAudio>();
            if (!audio || !(audio.layers ?? new LayeredAudio.Layer[0]).Contains(layer)) return false;
            var kind = Catalogue.IsCab(audio) ? SoundKind.CabEquipment : KindOf(audio.transform, engine.Car.transform);
            foreach (var port in audio.GetComponents<LayeredAudioPortReader>())
                if (PitchRules.Classify(port.portId) == SoundKind.CabEquipment) kind = SoundKind.CabEquipment;
            binding = BindLayer(engine, audio, layer, kind, Catalogue.MatchModule(audio), kind == SoundKind.Chuffs);
            if (!engine.Nodes.Contains(audio)) { engine.Nodes.Add(audio); Layered.Add(audio); }
            return true;
        }

        private static bool LoadReferences()
        {
            referencesReady = Catalogue.Load();
            if (referencesReady) Log($"Stock references ready: {Catalogue.Donors.Count} vanilla locomotive donors; {Clips.Count} exact clip references.");
            else status = "Waiting for vanilla locomotive prefabs and sound clips. Load a game, then rescan.";
            return referencesReady;
        }

        private static void AddRollingReferences()
        {
            Catalogue.AddSharedReferences();
        }

        private static Dictionary<LayeredAudio, SoundKind> ExplicitRoles(GameObject root)
        {
            var result = new Dictionary<LayeredAudio, SoundKind>();
            foreach (var audio in root.GetComponentsInChildren<LayeredAudio>(true))
                if (Catalogue.IsCab(audio)) SetRole(result, audio, SoundKind.CabEquipment);
            // Port identity remains reliable even when a mod renames the sound hierarchy.
            foreach (var port in root.GetComponentsInChildren<LayeredAudioPortReader>(true))
            {
                var kind = PitchRules.Classify(port.portId);
                if (kind != SoundKind.Unknown) SetRole(result, port.GetComponent<LayeredAudio>(), kind);
            }
            foreach (var b in root.GetComponentsInChildren<BrakesAudioModule>(true))
            {
                SetRole(result, b.brakeAudio, SoundKind.Brakes); SetRole(result, b.brakeSquealAudio, SoundKind.Brakes);
                SetRole(result, b.wheelSlidingAudio, SoundKind.Brakes); SetRole(result, b.brakesOverheatingAudio, SoundKind.Brakes);
                SetRole(result, b.brakeCylinderExhaustAudio, SoundKind.Ancillary); SetRole(result, b.airflowAudio, SoundKind.Ancillary);
            }
            foreach (var c in root.GetComponentsInChildren<ChuffClipsSimReader>(true))
                foreach (var loop in (c.chuffLoops ?? new ChuffClipsSimReader.ChuffLoop[0]).Concat(c.waterChuffLoops ?? new ChuffClipsSimReader.ChuffLoop[0]).Concat(c.ashChuffLoops ?? new ChuffClipsSimReader.ChuffLoop[0]))
                    if (loop != null) SetRole(result, loop.chuffLoop, SoundKind.Chuffs);
            return result;
        }
        private static void SetRole(Dictionary<LayeredAudio, SoundKind> map, LayeredAudio audio, SoundKind role)
        { if (audio && (!map.TryGetValue(audio, out var existing) || existing != SoundKind.CabEquipment)) map[audio] = role; }
        internal static SoundKind KindOf(Transform transform, Transform stop = null)
        {
            string path = "";
            for (var t = transform; t && t != stop; t = t.parent) path = t.name + "/" + path;
            return PitchRules.Classify(path);
        }
        internal static Engine Owner(Transform transform)
        {
            for (var t = transform; t; t = t.parent) if (Roots.TryGetValue(t, out var engine)) return engine;
            return null;
        }
        private static StockAudio.Donor ResolveDonor(Engine engine, int donors, int moduleDonors = 0)
        {
            long key = ((long)(uint)donors << 32) | (uint)moduleDonors;
            if (engine.ResolvedDonors.TryGetValue(key, out var cached)) return cached;
            int requested = 0;
            var setting = Config.DonorOverrides.FirstOrDefault(x => x != null && x.LiveryId == engine.Car.carLivery.id);
            if (setting != null) requested = Catalogue.Donors.FirstOrDefault(d => d.Id == setting.DonorId)?.Mask ?? 0;
            if (requested == 0 && engine.Steam && Config.SharedDonor != 0)
                requested = Catalogue.Donors.FirstOrDefault(d => Config.SharedDonor == 1 ? d.Id == "LocoS060" : d.Id == "LocoS282A" || d.Id == "LocoS282B")?.Mask ?? 0;
            int selected = PitchRules.SelectDonor(donors, requested, moduleDonors, engine.Votes);
            if (selected != 0)
            {
                var found = Catalogue.Donors.FirstOrDefault(d => d.Mask == selected);
                engine.ResolvedDonors[key] = found;
                return found;
            }
            var matches = Catalogue.Donors.Where(d => (d.Mask & donors) != 0).ToArray();
            // Shared recordings with identical relevant baselines have an identical result.
            if (matches.Length > 0 && matches.All(d => (!engine.Steam || (d.Area > 0) == (matches[0].Area > 0)) && Math.Abs((engine.Steam && d.Area > 0 ? d.Area : d.Mass) -
                (engine.Steam && matches[0].Area > 0 ? matches[0].Area : matches[0].Mass)) < 0.0001))
            { engine.ResolvedDonors[key] = matches[0]; return matches[0]; }
            engine.Ambiguous = true;
            engine.ResolvedDonors[key] = null;
            return null;
        }

        internal static float Factor(Engine engine, int donors, SoundKind kind, bool loop = false, int moduleDonors = 0)
        {
            if (!Enabled || Restoring || engine == null || !engine.Car || engine.Stock || kind == SoundKind.CabEquipment || donors == 0) return 1;
            if (engine.Steam && (!Selected(kind) || (loop && !Config.ChuffLoops))) return 1;
            var donor = ResolveDonor(engine, donors, moduleDonors);
            if (donor == null) return 1;
            if (engine.Steam)
            {
                if (donor.Area > 0)
                {
                    return (float)PitchRules.Multiplier(engine.Area, donor.Area, Config.Strength, Config.MinimumPitch, Config.MaximumPitch);
                }
                return (float)PitchRules.MassMultiplier(engine.Mass, donor.Mass, Config.Strength, Config.MinimumPitch, Config.MaximumPitch);
            }
            return (float)PitchRules.MassMultiplier(engine.Mass, donor.Mass, Config.NonSteamStrength, Config.NonSteamMinimumPitch, Config.NonSteamMaximumPitch);
        }
        private static bool Selected(SoundKind kind)
        {
            switch (kind)
            {
                case SoundKind.Chuffs: return Config.Chuffs;
                case SoundKind.Gear: return Config.Gear;
                case SoundKind.Rolling: return Config.Rolling;
                case SoundKind.Brakes: return Config.Brakes;
                case SoundKind.Cocks: return Config.Cocks;
                case SoundKind.Whistle: return Config.Whistle;
                default: return kind != SoundKind.CabEquipment;
            }
        }

        internal static float ShotFactor(AudioClip clip, Transform parent, float pitch, AudioMixerGroup mixerGroup)
        {
            var engine = Context ?? Owner(parent);
            if (engine == null || !clip || !PitchRules.Valid(pitch) || Catalogue.IsCab(mixerGroup)) return 1;
            Clips.TryGetValue(clip, out var info);
            if (!PitchRules.SoundEligible(info != null, false)) return 1;
            var kind = Context != null && ContextKind != SoundKind.Unknown ? ContextKind : info?.Kind ?? SoundKind.Unknown;
            int module = Context != null ? ContextDonors : 0;
            return kind == SoundKind.CabEquipment ? 1 : Factor(engine, info.Donors, kind, false, module);
        }

        internal static void Refresh(Engine only = null)
        {
            foreach (var engine in Engines)
                if (only == null || engine == only) { engine.ResolvedDonors.Clear(); engine.Ambiguous = false; }
            foreach (var layered in only == null ? Layered.ToArray() : only.Nodes.ToArray())
            {
                if (!layered || layered.type != LayeredAudio.Type.Continuous) continue;
                foreach (var layer in layered.layers ?? new LayeredAudio.Layer[0])
                    if (layer != null && Layers.TryGetValue(layer, out var binding) && (only == null || binding.Engine == only)) ReplayLayer(layer, binding);
            }
            DirectAudio.Refresh(only);
        }
        internal static void ReturnToPool(TrainAudio audio)
        {
            if (!audio || !Roots.TryGetValue(audio.transform, out var engine)) return;
            engine.Stock = true; // invalidate BEFORE the original returns this audio instance to a pool
            Refresh(engine);
            Roots.Remove(audio.transform);
            if (engine.Car) Roots.Remove(engine.Car.transform);
            Queued.Remove(audio);
            DetachCar(audio); // only this car's entries; no full rescan
        }

        // Called from the TrainAudio.SetupForCar postfix.
        internal static void QueueCar(TrainAudio audio)
        {
            if (!Enabled || !audio) return;
            if (Queued.Add(audio)) Pending.Enqueue(audio);
        }

        // Processes at most one queued car per frame. While the stock references are not ready the periodic
        // full scan in Tick() retries and, when it succeeds, consumes the queue.
        internal static void ProcessPending()
        {
            if (!Enabled || !referencesReady || Pending.Count == 0) return;
            while (Pending.Count > 0)
            {
                var audio = Pending.Dequeue();
                if (!Queued.Remove(audio) || !audio) continue;
                var watch = Stopwatch.StartNew();
                try
                {
                    var engine = ScanCar(audio);
                    watch.Stop();
                    // Quiet for ordinary cars: log only steam engines and anything unexpectedly slow.
                    if (engine != null || watch.Elapsed.TotalMilliseconds >= 5)
                    {
                        string id = engine != null && engine.Car && engine.Car.carLivery ? engine.Car.carLivery.id : "(not a locomotive)";
                        Log($"Audio discovery: single car {id}, {(engine != null ? engine.LayerCount : 0)} layers, {watch.Elapsed.TotalMilliseconds:F1} ms (setup event).");
                    }
                }
                catch (Exception ex)
                {
                    // One bad car must not stop discovery for the others: skip it, say so once, carry on.
                    if (Logged.Add(ex.ToString())) Log("Single-car discovery failed; this car is skipped: " + ex);
                }
                break;
            }
        }

        private static Engine ScanCar(TrainAudio audio)
        {
            var previous = DetachCar(audio, false);
            AddRollingReferences();
            var engine = BuildEngine(audio, previous);
            UpdateStatus();
            if (engine != null) Refresh(only: engine);
            return engine;
        }

        // Removes everything one TrainAudio instance owns (after putting its pitches back) and returns its old
        // bindings so a rescan can keep their captured pitch inputs.
        private static Dictionary<LayeredAudio.Layer, Binding> DetachCar(TrainAudio audio, bool endOwnership = true)
        {
            var saved = new Dictionary<LayeredAudio.Layer, Binding>();
            if (ReferenceEquals(audio, null) || !ByAudio.TryGetValue(audio, out var engine)) return saved;
            DirectAudio.Remove(engine, endOwnership);
            bool wasRestoring = Restoring;
            Restoring = true; // Factor() == 1 while set, so replaying writes the original pitches back
            try
            {
                foreach (var layered in engine.Nodes)
                {
                    if (!layered || layered.type != LayeredAudio.Type.Continuous) continue;
                    foreach (var layer in layered.layers ?? new LayeredAudio.Layer[0])
                        if (layer != null && Layers.TryGetValue(layer, out var b) && b.Engine == engine) ReplayLayer(layer, b);
                }
            }
            finally { Restoring = wasRestoring; }
            foreach (var layer in engine.LayerKeys)
                if (Layers.TryGetValue(layer, out var b) && b.Engine == engine) { saved[layer] = b; Layers.Remove(layer); }
            foreach (var node in engine.Nodes) Layered.Remove(node);
            foreach (var key in Roots.Where(kv => kv.Value == engine).Select(kv => kv.Key).ToList()) Roots.Remove(key);
            Engines.Remove(engine);
            ByAudio.Remove(audio);
            return saved;
        }

        private static void PruneDestroyed()
        {
            StaleLayers.Clear();
            foreach (var pair in Layers)
                if (!pair.Value.Source || !pair.Value.Audio) StaleLayers.Add(pair.Key);
            foreach (var layer in StaleLayers)
            {
                var binding = Layers[layer]; Layers.Remove(layer);
                binding.Engine.LayerKeys.Remove(layer); binding.Engine.LayerCount--;
                if (!binding.Audio)
                {
                    Layered.Remove(binding.Audio); binding.Engine.Nodes.Remove(binding.Audio);
                    binding.Engine.Callers.Remove(binding.Audio);
                }
            }
            StaleLayers.Clear(); StaleEngines.Clear();
            foreach (var engine in Engines)
            {
                StaleCallers.Clear();
                foreach (var caller in engine.Callers.Keys) if (!caller) StaleCallers.Add(caller);
                foreach (var caller in StaleCallers) engine.Callers.Remove(caller);
                if (!engine.Car || !engine.Audio) StaleEngines.Add(engine);
            }
            foreach (var engine in StaleEngines) DetachCar(engine.Audio);
            StaleCallers.Clear(); StaleEngines.Clear();
            DirectAudio.Prune();
        }

        internal static void DiscoverEffects(Component component)
        {
            if (!Enabled || !referencesReady || !component) return;
            var engine = Owner(component.transform);
            if (engine != null && !engine.Stock) DirectAudio.Discover(engine, Catalogue, AudioRoots(engine.Car, engine.Audio));
        }
        private static string Describe(Engine e)
        {
            string name = e.Car && e.Car.carLivery ? e.Car.carLivery.id : "destroyed";
            string metric = e.Steam ? $"barrel area {e.Area:F2} m²" : $"configured mass {e.Mass / 1000:F2} t";
            string reason = e.Stock ? "protected: vanilla / non-CCL" : !PitchRules.Valid(e.Steam ? e.Area : e.Mass) ? "invalid metric: unchanged" :
                e.StockClips == 0 ? "no stock clips discovered yet" : e.Ambiguous ? "shared donor ambiguous: choose a donor below" : "default exterior audio eligible";
            return $"{name}: {metric}; {e.StockClips} stock clip matches, {e.LayerCount} layers; {reason}.";
        }
        private static void UpdateStatus() => status = $"Vanilla sound donors: {Catalogue.Donors.Count}; loaded locomotives: {Engines.Count}. Stock locomotives are protected.";
        private static void WriteReport()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("O")).AppendLine(status);
                sb.AppendLine("Incompatible with Zeibach's Sounds (ZSounds).");
                sb.AppendLine($"Enabled={Enabled}; strength={Config.Strength}; floor={Config.MinimumPitch}; ceiling={Config.MaximumPitch}; allSounds={Config.AllSounds}; sharedDonor={Config.SharedDonor}");
                sb.AppendLine($"NonSteamStrength={Config.NonSteamStrength}; floor={Config.NonSteamMinimumPitch}; ceiling={Config.NonSteamMaximumPitch}");
                foreach (var donor in Catalogue.Donors) sb.AppendLine($"Donor {donor.Mask}: {donor.Id}; mass={donor.Mass:F2}kg; barrelArea={donor.Area:F2}m²");
                foreach (var engine in Engines) sb.AppendLine(Describe(engine));
                foreach (var item in Layers)
                {
                    var layer = item.Key; var b = item.Value;
                    if (!layer.source) continue;
                    float factor = LayerFactor(layer, b);
                    var donor = b.Donors == 0 || b.Kind == SoundKind.CabEquipment ? null : ResolveDonor(b.Engine, b.Donors, b.ModuleDonors);
                    sb.AppendLine($"{b.Engine.Car?.carLivery?.id} | {layer.source.name} | clip={layer.source.clip?.name} | {b.Kind} | donors={b.Donors} | selectedDonor={donor?.Id ?? "ambiguous/custom/cab"} | moduleDonors={b.ModuleDonors} | loop={b.Loop} | factor={factor:F4} | sourcePitch={layer.source.pitch:F4} | volume={layer.source.volume:F4} | nativeInput={b.Input} | applied={b.AppliedMultiplier:F4} | shiftWrites={b.ShiftWrites} | refreshWrites={b.RefreshWrites}");
                }
                DirectAudio.AppendReport(sb);
                string path = Path.Combine(DiagnosticsDirectory, "SteamSizePitch-diagnostics.txt");
                File.WriteAllText(path, sb.ToString());
                Log("Wrote " + path);
            }
            catch (Exception ex) { Log("Diagnostic report: " + ex.Message); }
        }

        internal sealed class Engine
        {
            public TrainCar Car; public TrainAudio Audio; public double Area, Mass;
            public bool Stock, Steam, Ambiguous, PitchLogged; public int StockClips, LayerCount;
            public readonly Dictionary<int, int> Votes = new Dictionary<int, int>();
            public readonly Dictionary<long, StockAudio.Donor> ResolvedDonors = new Dictionary<long, StockAudio.Donor>();
            public readonly Dictionary<Component, ContextMetadata> Callers = new Dictionary<Component, ContextMetadata>();
            public readonly List<LayeredAudio> Nodes = new List<LayeredAudio>();
            public readonly List<LayeredAudio.Layer> LayerKeys = new List<LayeredAudio.Layer>();
        }
        internal sealed class Binding { public Engine Engine; public LayeredAudio Audio; public AudioSource Source; public AudioClip Clip; public SoundKind Kind; public int Donors, ModuleDonors; public bool Loop; public float? Input; public bool InputLinear; public float InputMin, InputMax; public float AppliedMultiplier = 1; public long ShiftWrites, RefreshWrites; public readonly HashSet<AudioClip> AllowedClips = new HashSet<AudioClip>(); }

        internal static float LayerFactor(LayeredAudio.Layer layer, Binding binding)
        {
            if (!CanTouchLayer(layer, binding)) return 1;
            return Factor(binding.Engine, binding.Donors, binding.Kind, binding.Loop, binding.ModuleDonors);
        }
        internal static void LogAppliedPitch(LayeredAudio.Layer layer)
        {
            if (!Enabled || Restoring || !Layers.TryGetValue(layer, out var b) ||
                b.Engine.PitchLogged || b.AppliedMultiplier == 1 || !layer.source) return;
            b.Engine.PitchLogged = true;
            Log($"Applied pitch: {b.Engine.Car?.carLivery?.id}; clip={layer.source.clip?.name}; multiplier={b.AppliedMultiplier:F4}x; AudioSource pitch={layer.source.pitch:F4}.");
        }
        private static bool CanTouchLayer(LayeredAudio.Layer layer, Binding binding)
        {
            if (binding.Kind == SoundKind.CabEquipment || !layer.source || layer.source != binding.Source ||
                Catalogue.IsCab(layer.source.outputAudioMixerGroup) || !layer.source.clip || !binding.AllowedClips.Contains(layer.source.clip)) return false;
            if (!Clips.TryGetValue(layer.source.clip, out var info)) return false;
            binding.Clip = layer.source.clip;
            binding.Donors = info.Donors;
            return true;
        }
        private static void ReplayLayer(LayeredAudio.Layer layer, Binding binding)
        {
            // lastPitchInput is NOT a Layer.SetPitch input: Set() can use source.volume
            // (inertialPitch) or Abs(level). Only replay arguments captured from the real call.
            if (!PitchRules.NeedsRefresh(binding.Kind, CanTouchLayer(layer, binding), binding.Input.HasValue,
                LayerFactor(layer, binding), binding.AppliedMultiplier)) return;
            bool previous = ManualRefresh;
            ManualRefresh = true;
            try
            {
                binding.RefreshWrites++;
                layer.SetPitch(binding.Input.Value, binding.InputLinear, binding.InputMin, binding.InputMax);
            }
            finally { ManualRefresh = previous; }
        }
        private static bool DynamoBinding(Binding b) =>
            ((b.Audio ? b.Audio.name : "") + " " + (b.Clip ? b.Clip.name : "")).IndexOf("dynamo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (b.Audio && b.Audio.GetComponents<LayeredAudioPortReader>().Any(p => (p.portId ?? "").IndexOf("dynamo", StringComparison.OrdinalIgnoreCase) >= 0));
        private static void TraceDynamos()
        {
            string path = Path.Combine(DiagnosticsDirectory, "SteamSizePitch-dynamo-trace.csv");
            if (!traceStarted)
            {
                File.WriteAllText(path, "time,loco,speed_kph,clip,kind,stock_clip,playing,source_pitch,source_volume,master_volume,target_multiplier,applied_multiplier,native_input,shift_writes,refresh_writes\n");
                traceStarted = true;
                Log("Dynamo diagnostic trace: " + path);
            }
            var sb = new StringBuilder();
            foreach (var pair in Layers)
            {
                var b = pair.Value; var source = pair.Key.source;
                if (!source || !b.Engine.Car || !DynamoBinding(b)) continue;
                sb.AppendLine(string.Join(",", DateTime.Now.ToString("O"), Csv(b.Engine.Car.carLivery.id),
                    Number(Math.Abs(b.Engine.Car.GetForwardSpeed()) * 3.6f), Csv(source.clip ? source.clip.name : ""), b.Kind.ToString(),
                    (b.Donors != 0).ToString(), source.isPlaying.ToString(), Number(source.pitch), Number(source.volume),
                    Number(b.Audio.MasterVolume), Number(LayerFactor(pair.Key, b)), Number(b.AppliedMultiplier),
                    b.Input.HasValue ? Number(b.Input.Value) : "", b.ShiftWrites.ToString(), b.RefreshWrites.ToString()));
            }
            if (sb.Length > 0 && new FileInfo(path).Length < 2 * 1024 * 1024) File.AppendAllText(path, sb.ToString());
        }
        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        private static string Number(float value) => value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

        internal struct AudioContext { internal Engine Engine; internal SoundKind Kind; internal int Donors; }
        internal struct ContextMetadata { internal SoundKind Kind; internal int Donors; }
        internal static AudioContext BeginContext(Component component)
        {
            var previous = new AudioContext { Engine = Context, Kind = ContextKind, Donors = ContextDonors };
            Context = Owner(component.transform);
            ContextKind = SoundKind.Unknown; ContextDonors = 0;
            if (!Enabled || Context == null) return previous;
            if (!Context.Callers.TryGetValue(component, out var metadata))
            {
                metadata.Kind = component is ChuffClipsSimReader ? SoundKind.Chuffs : KindOf(component.transform, Context.Car.transform);
                if (component is LayeredAudio audio)
                {
                    if (Catalogue.IsCab(audio)) metadata.Kind = SoundKind.CabEquipment;
                    metadata.Donors = Catalogue.MatchModule(audio);
                }
                else if (component is AudioClipPortReader reader)
                {
                    var role = PitchRules.Classify(reader.portId);
                    if (role != SoundKind.Unknown) metadata.Kind = role;
                }
                Context.Callers[component] = metadata;
            }
            ContextKind = metadata.Kind; ContextDonors = metadata.Donors;
            return previous;
        }
        internal static void EndContext(AudioContext previous)
        { Context = previous.Engine; ContextKind = previous.Kind; ContextDonors = previous.Donors; }
    }

    // Multiply the game's own pitch calculation, preserving its curves and Doppler path.
    // Restore the stored baseline even if another patch/the original throws; no cumulative pitch changes.
    [HarmonyPatch(typeof(LayeredAudio.Layer), nameof(LayeredAudio.Layer.SetPitch))]
    internal static class LayerPitchPatch
    {
        private static void Prefix(LayeredAudio.Layer __instance, float value, bool linearPitchLerp, float minPitch, float maxPitch, out float? __state)
        {
            __state = null;
            if (SoundEngine.TryBinding(__instance, out var binding))
            {
                if (!SoundEngine.ManualRefresh)
                { binding.Input = value; binding.InputLinear = linearPitchLerp; binding.InputMin = minPitch; binding.InputMax = maxPitch; }
                float factor = SoundEngine.LayerFactor(__instance, binding);
                binding.AppliedMultiplier = factor;
                if (factor == 1) return;
                binding.ShiftWrites++;
                __state = __instance.startPitch;
                __instance.startPitch *= factor;
            }
        }
        private static void Postfix(LayeredAudio.Layer __instance, float? __state, bool __runOriginal)
        { if (__state.HasValue && __runOriginal) SoundEngine.LogAppliedPitch(__instance); }
        private static void Finalizer(LayeredAudio.Layer __instance, float? __state) { if (__state.HasValue) __instance.startPitch = __state.Value; }
    }
    [HarmonyPatch]
    internal static class OneShotPatch
    {
        private static MethodBase TargetMethod() => typeof(NAudio).GetMethods().Single(m => m.Name == "Play" && m.GetParameters()[0].ParameterType == typeof(AudioClip));
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(AudioClip clip, Transform parent, AudioMixerGroup mixerGroup, ref float pitch)
        {
            float factor = SoundEngine.ShotFactor(clip, parent, pitch, mixerGroup);
            // NAudio schedules return using this argument. Match Unity's positive
            // AudioClip pitch limit before it calculates the deadline.
            if (factor != 1) pitch = Mathf.Min(3, pitch * factor);
        }
    }
    [HarmonyPatch(typeof(TrainAudio), nameof(TrainAudio.SetupForCar))]
    internal static class SetupPatch
    {
        private static void Prefix(TrainAudio __instance, TrainCar __0) => SoundEngine.PrepareCar(__instance, __0);
        private static void Postfix(TrainAudio __instance) => SoundEngine.QueueCar(__instance);
    }
    [HarmonyPatch(typeof(TrainAudio), "ReturnToPool")]
    internal static class PoolPatch { private static void Prefix(TrainAudio __instance) => SoundEngine.ReturnToPool(__instance); }

    // A few one-shots deliberately pass parent=null. Scope them to the caller, never to the nearest loco.
    [HarmonyPatch]
    internal static class ContextPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(AudioClipPortReader), nameof(AudioClipPortReader.OnValueUpdate));
            yield return AccessTools.Method(typeof(LayeredAudio), nameof(LayeredAudio.PlayOnce));
            yield return AccessTools.Method(typeof(ChuffClipsSimReader), "OnChuff");
            yield return AccessTools.Method(typeof(CarCollisionAudioModule), "OnCollidedEnter");
            yield return AccessTools.Method(typeof(TrainDerailAudioModule), "PlayDerailAudio");
            yield return AccessTools.Method(typeof(Coupler), nameof(Coupler.CoupleTo));
            yield return AccessTools.Method(typeof(Coupler), nameof(Coupler.Uncouple));
            yield return AccessTools.Method(typeof(TrainBuoyancyController), "PlaySplash");
            yield return AccessTools.Method(typeof(PlugSocket), nameof(PlugSocket.NotifyPlugged));
            yield return AccessTools.Method(typeof(PlugSocket), nameof(PlugSocket.NotifyUnplugged));
        }
        private static void Prefix(Component __instance, out SoundEngine.AudioContext __state) => __state = SoundEngine.BeginContext(__instance);
        private static void Finalizer(SoundEngine.AudioContext __state) => SoundEngine.EndContext(__state);
    }

    // Native failure effects instantiate ordinary playOnAwake sources. Discover only
    // after their supported creation callbacks; no recurring scene audio scan.
    [HarmonyPatch]
    internal static class EffectSpawnPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DV.Simulation.Controllers.ExplosionActivationOnSignal), "OnExplosionSignalChanged");
            yield return AccessTools.Method(typeof(DV.Simulation.Controllers.DeadTractionMotorsController), "OnPoweredWheelKilled");
            yield return AccessTools.Method(typeof(DV.Simulation.Controllers.DeadTractionMotorsController), "OnPoweredWheelSetOnFire");
            yield return AccessTools.Method(typeof(DV.Simulation.Controllers.DeadTractionMotorsController), "OnOverheatFuseOffPortChanged");
            yield return AccessTools.Method(typeof(ResourceExplosionBase), nameof(ResourceExplosionBase.ExplodeResource));
            yield return AccessTools.Method(typeof(DV.Simulation.Controllers.BlowbackParticlePortReader), "OnTriggerExit");
        }
        private static void Postfix(Component __instance) => SoundEngine.DiscoverEffects(__instance);
    }
}
