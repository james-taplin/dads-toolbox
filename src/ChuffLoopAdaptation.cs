using System;
using System.Collections.Generic;
using DV.ModularAudioCar;
using UnityEngine;
using DadsToolbox.LocoSound;

namespace DadsToolbox.Chuffs
{
    // An explicit per-layer agreement: exhaust cadence takes precedence over
    // size/tone scaling only while Chuff Fix owns that rhythmic loop.
    internal static class ChuffLoopOwnership
    {
        static readonly Dictionary<LayeredAudio.Layer, LoopAdaptation> owners = new Dictionary<LayeredAudio.Layer, LoopAdaptation>();
        static readonly HashSet<LoopAdaptation> adaptations = new HashSet<LoopAdaptation>();
        internal static void Register(LoopAdaptation owner) => adaptations.Add(owner);
        internal static void Completed(LoopAdaptation owner) => adaptations.Remove(owner);
        internal static bool OwnsLayer(LayeredAudio.Layer layer) => layer != null && owners.ContainsKey(layer);
        internal static bool Claim(LayeredAudio.Layer layer, LoopAdaptation owner)
        {
            if (owners.ContainsKey(layer)) return false;
            owners.Add(layer, owner);
            try { SoundEngine.SuspendRhythmicPitch(layer); }
            catch { owners.Remove(layer); throw; }
            return true;
        }
        internal static void Release(LayeredAudio.Layer layer, LoopAdaptation owner, bool refreshPitch = true)
        {
            if (!owners.TryGetValue(layer, out var current) || !ReferenceEquals(current, owner)) return;
            owners.Remove(layer);
            try { SoundEngine.ResumeRhythmicPitch(layer, refreshPitch); }
            catch { owners[layer] = owner; throw; }
        }
        internal static void RestoreAll()
        {
            var pending = new HashSet<LoopAdaptation>(adaptations);
            List<Exception> errors = null;
            foreach (var owner in pending)
                try { owner.Dispose(); }
                catch (Exception ex) { if (errors == null) errors = new List<Exception>(); errors.Add(ex); }
            if (errors != null) throw new AggregateException("Rhythmic loop restoration failed.", errors);
        }
    }
    // Adapt the known stock rhythmic-loop configuration; custom sound design remains
    // authoritative. Native ChuffLoop/LayeredAudio still own mixing and playback.
    internal sealed class LoopAdaptation : IDisposable
    {
        const float ExtendedCurveHz = 192f;
        const float SustainBaseHz = 16f;
        readonly List<LayerState> layers = new List<LayerState>();
        readonly Dictionary<AudioClip, float[]> pcm = new Dictionary<AudioClip, float[]>();
        readonly Dictionary<AudioClip, Dictionary<int, AudioClip>> generated = new Dictionary<AudioClip, Dictionary<int, AudioClip>>();
        readonly HashSet<string> reported = new HashSet<string>();
        readonly Action<string> warning;
        bool disposed;

        sealed class LayerState
        {
            internal LayeredAudio Audio;
            internal LayeredAudio.Layer Layer;
            internal AudioSource Source;
            internal AnimationCurve OriginalCurve;
            internal AnimationCurve AdaptedCurve;
            internal AudioClip OriginalClip;
            internal AudioClip CurrentClip;
            internal float OriginalStartPitch;
            internal float OriginalSourcePitch;
            internal float OriginalDesiredPitch, ExpectedSourcePitch, ExpectedDesiredPitch;
            internal bool HasDoppler, Released, Relinquished, Restored, ResumePitch;
            internal bool Sustained;
            internal float Slope;
            internal float CurveEndHz = ExtendedCurveHz;
            internal int Factor = 1;
            internal readonly HashSet<int> FailedFactors = new HashSet<int>();
        }

        LoopAdaptation(Action<string> warning) { this.warning = warning; ChuffLoopOwnership.Register(this); }

        internal static LoopAdaptation Apply(ChuffClipsSimReader reader, int beatsPerRevolution, Action<string> warning)
        {
            var adaptation = new LoopAdaptation(warning);
            if (reader == null || beatsPerRevolution <= 0) return adaptation;
            int initialFactor = 1;
            while ((long)initialFactor * 4 < beatsPerRevolution && initialFactor <= int.MaxValue / 2) initialFactor *= 2;
            var seenLayers = new HashSet<LayeredAudio.Layer>();
            var seenSources = new HashSet<AudioSource>();
            try
            {
                adaptation.Add(reader.chuffLoops, initialFactor, seenLayers, seenSources);
                adaptation.Add(reader.waterChuffLoops, initialFactor, seenLayers, seenSources);
                // Ash is a separate noise effect, not the ordinary exhaust cadence.
                return adaptation;
            }
            catch
            {
                adaptation.Dispose();
                throw;
            }
        }

        void Warn(string key, string message)
        {
            if (reported.Add(key) && warning != null) warning(message);
        }

        void Add(ChuffClipsSimReader.ChuffLoop[] loops, int initialFactor,
            HashSet<LayeredAudio.Layer> seenLayers, HashSet<AudioSource> seenSources)
        {
            if (loops == null) return;
            foreach (var loop in loops)
            {
                var audio = loop == null ? null : loop.chuffLoop;
                if (audio == null || audio.layers == null || audio.layers.Length != 1) continue;
                var layer = audio.layers[0];
                if (layer == null || !seenLayers.Add(layer)) continue;
                float slope;
                bool sustained;
                if (!RecognizedStock(layer, out slope, out sustained)) continue;
                if (!seenSources.Add(layer.source))
                {
                    Warn("shared-source", "A stock loop AudioSource is shared by different layers; its additional layer was left unchanged.");
                    continue;
                }

                if (!ChuffLoopOwnership.Claim(layer, this))
                {
                    Warn("owned-layer", "A rhythmic loop is already owned by another Chuff Fix reader; its additional binding was left unchanged.");
                    continue;
                }

                // Native Reset captures source.pitch as startPitch only on first use.
                // Calling it before changing either value prevents double division when
                // the first native SetPitch initializes an as-yet inactive loop.
                try { audio.Reset(); }
                catch { ChuffLoopOwnership.Release(layer, this); throw; }
                var state = new LayerState
                {
                    Audio = audio, Layer = layer, Source = layer.source,
                    OriginalCurve = layer.pitchCurve, OriginalClip = layer.source.clip,
                    CurrentClip = layer.source.clip, OriginalStartPitch = layer.startPitch,
                    OriginalSourcePitch = layer.source.pitch, Sustained = sustained, Slope = slope
                };
                state.HasDoppler = layer.useDoppler && layer.doppler;
                state.OriginalDesiredPitch = state.HasDoppler ? layer.doppler.desiredPitch : layer.source.pitch;
                state.ExpectedSourcePitch = state.OriginalSourcePitch;
                state.ExpectedDesiredPitch = state.OriginalDesiredPitch;
                // Register before any further edit so rollback can always find
                // the claim, including failures constructing a replacement curve.
                layers.Add(state);
                Keyframe[] keys = layer.pitchCurve.keys;
                var end = keys[keys.Length - 1];
                end.time = ExtendedCurveHz;
                end.value = ExtendedCurveHz * slope;
                end.inTangent = slope;
                end.outTangent = slope;
                keys[keys.Length - 1] = end;
                state.AdaptedCurve = new AnimationCurve(keys)
                {
                    preWrapMode = state.OriginalCurve.preWrapMode,
                    postWrapMode = state.OriginalCurve.postWrapMode
                };
                layer.pitchCurve = state.AdaptedCurve;
                if (sustained && initialFactor > 1)
                    TryBestFactor(state, Math.Min(initialFactor, MaximumFactor(state.OriginalClip)));
                ObservePitch(state);
            }
        }

        static bool Near(float a, float b)
        {
            return Math.Abs(a - b) <= 0.00005f * Math.Max(1f, Math.Abs(b));
        }

        static bool RecognizedStock(LayeredAudio.Layer layer, out float slope, out bool sustained)
        {
            slope = 0f;
            sustained = false;
            if (!layer.usePitchCurve || layer.pitchCurve == null || layer.source == null
                || !layer.source.loop || layer.source.clip == null) return false;
            // Runtime WrapMode 2/4 means Loop/PingPong, unlike the asset file's
            // serialized infinity-mode numbering. Those modes are custom tuning.
            int before = (int)layer.pitchCurve.preWrapMode, after = (int)layer.pitchCurve.postWrapMode;
            if (before == 2 || before == 4 || after == 2 || after == 4) return false;
            float first = -16f, last = 16f;
            switch (layer.source.clip.name)
            {
                case "Steam_ChuffLoop2.67s_01": slope = 0.5f; break;
                case "Steam_ChuffLoop3s_01": slope = 0.375f; break;
                case "Steam_ChuffLoop4s_01": slope = 0.25f; break;
                case "Steam_ChuffLoop5.33s_01": slope = 0.1875f; break;
                case "Steam_ChuffLoop8s_01": slope = 0.125f; break;
                case "Steam_ChuffLoop10.67s_01": slope = 0.09375f; break;
                case "Steam_ChuffLoop16s_01": slope = 0.0625f; last = 32f; sustained = true; break;
                case "Steam_ChuffWaterLoop4s_01": slope = 0.25f; first = -32f; last = 32f; break;
                case "Steam_ChuffWaterLoop16s_01": slope = 0.0625f; first = -32f; last = 32f; sustained = true; break;
                default: return false;
            }
            var keys = layer.pitchCurve.keys;
            if (keys.Length != 2 || !Near(keys[0].time, first) || !Near(keys[1].time, last)) return false;
            foreach (var key in keys)
                if ((int)key.weightedMode != 0 || !Near(key.value, key.time * slope)
                    || !Near(key.inTangent, slope) || !Near(key.outTangent, slope)) return false;
            // A deliberate authored pitch multiplier is custom tuning too. Zero is the
            // native uninitialized startPitch; Reset above will capture source.pitch.
            return Near(layer.startPitch, 1f) || (Near(layer.startPitch, 0f) && Near(layer.source.pitch, 1f));
        }

        static int MaximumFactor(AudioClip original)
        {
            if (original == null || original.frequency <= 0) return 1;
            int factor = 1;
            while ((long)original.frequency * factor * 2 <= int.MaxValue) factor *= 2;
            return factor;
        }

        bool TryBestFactor(LayerState state, int desired)
        {
            for (int factor = desired; factor > state.Factor; factor /= 2)
                if (TrySetFactor(state, factor)) return true;
            return false;
        }

        bool TrySetFactor(LayerState state, int factor)
        {
            if (factor <= state.Factor || factor > MaximumFactor(state.OriginalClip) || state.FailedFactors.Contains(factor)) return false;
            if (state.Source == null || state.Source.clip != state.CurrentClip) return false;
            AudioClip previousClip = state.CurrentClip;
            int previousFactor = state.Factor;
            float previousPitch = state.Source.pitch;
            float previousStartPitch = state.Layer.startPitch;
            var doppler = state.Layer.useDoppler ? state.Layer.doppler : null;
            float previousDesiredPitch = doppler ? doppler.desiredPitch : 0f;
            int previousPosition = state.Source.timeSamples;
            bool previousPlaying = state.Source.isPlaying;
            bool handedOff = false;
            bool uncachedClip = false;
            AudioClip rebased = null;
            try
            {
                Dictionary<int, AudioClip> byFactor;
                if (!generated.TryGetValue(state.OriginalClip, out byFactor))
                {
                    byFactor = new Dictionary<int, AudioClip>();
                    generated.Add(state.OriginalClip, byFactor);
                }
                if (!byFactor.TryGetValue(factor, out rebased))
                {
                    var original = state.OriginalClip;
                    if (original.loadType != AudioClipLoadType.DecompressOnLoad
                        || (original.loadState != AudioDataLoadState.Loaded && !original.LoadAudioData())
                        || original.loadState != AudioDataLoadState.Loaded)
                        throw new InvalidOperationException("the stock clip's decoded PCM is not available");
                    float[] samples;
                    if (!pcm.TryGetValue(original, out samples))
                    {
                        samples = new float[checked(original.samples * original.channels)];
                        if (!original.GetData(samples, 0)) throw new InvalidOperationException("AudioClip.GetData refused the stock clip");
                        pcm.Add(original, samples);
                    }
                    int requestedFrequency = checked(original.frequency * factor);
                    rebased = AudioClip.Create(original.name + "_MCCF_rate" + factor,
                        original.samples, original.channels, requestedFrequency, false);
                    uncachedClip = rebased != null;
                    if (rebased == null || rebased.frequency != requestedFrequency
                        || rebased.samples != original.samples || rebased.channels != original.channels
                        || !rebased.SetData(samples, 0))
                    {
                        if (rebased != null) UnityEngine.Object.Destroy(rebased);
                        rebased = null;
                        throw new InvalidOperationException("AudioClip.Create/SetData did not accept the requested sample frequency and PCM");
                    }
                    byFactor.Add(factor, rebased);
                    uncachedClip = false;
                }

                float pitch = state.Source.pitch * state.Factor / factor;
                if (previousPlaying) state.Source.Stop();
                handedOff = true;
                state.Source.clip = rebased;
                state.Source.pitch = pitch;
                state.Layer.startPitch = state.OriginalStartPitch / factor;
                // Doppler applies its pending request later. Compensate that
                // request too, so it cannot undo the PCM handoff before the
                // first native loop update supplies a fresh frequency.
                if (doppler) doppler.SetDesiredPitch(previousDesiredPitch * previousFactor / factor);
                state.Source.timeSamples = Math.Max(0, Math.Min(previousPosition, rebased.samples - 1));
                state.CurrentClip = rebased;
                state.Factor = factor;
                if (previousPlaying) state.Source.Play();
                return true;
            }
            catch (Exception exception)
            {
                if (uncachedClip && rebased != null) UnityEngine.Object.Destroy(rebased);
                if (handedOff && state.Source != null)
                {
                    state.Source.Stop();
                    state.Source.clip = previousClip;
                    state.Source.pitch = previousPitch;
                    state.Layer.startPitch = previousStartPitch;
                    if (doppler) doppler.SetDesiredPitch(previousDesiredPitch);
                    state.Source.timeSamples = Math.Max(0, Math.Min(previousPosition, previousClip.samples - 1));
                    state.CurrentClip = previousClip;
                    state.Factor = previousFactor;
                    if (previousPlaying) state.Source.Play();
                }
                // A bounded native PCM operation has failed. Keep the original clip
                // configuration usable and report its remaining pitch range explicitly.
                // This is not an exception finalizer for native chuff/simulation code.
                state.FailedFactors.Add(factor);
                Warn("rebase:" + state.OriginalClip.GetInstanceID() + ":" + factor,
                    "Stock chuff loop rate adaptation could not create a " + factor
                    + "x sample-rate clip (" + exception.Message + "). Its current loop cadence is supported only up to "
                    + (SustainBaseHz * state.Factor * 3f) + " Hz; no fallback sound was substituted.");
                return false;
            }
        }

        internal void Refresh(float nativeHz)
        {
            if (disposed || float.IsNaN(nativeHz) || float.IsInfinity(nativeHz) || nativeHz < 0f) return;
            foreach (var state in layers)
            {
                if (state.Released || state.Relinquished || state.Source == null) continue;
                if (state.Layer.source != state.Source || state.Layer.pitchCurve != state.AdaptedCurve ||
                    state.Source.clip != state.CurrentClip || !Near(state.Layer.startPitch, state.OriginalStartPitch / state.Factor))
                {
                    state.Relinquished = true;
                    Warn("changed-ownership", "Another audio change replaced an adapted loop's clip, curve or pitch baseline; further adaptation was skipped.");
                    continue;
                }
                bool curveChanged = false;
                if (nativeHz > state.CurveEndHz)
                {
                    var keys = state.AdaptedCurve.keys;
                    var end = keys[keys.Length - 1];
                    end.time = nativeHz <= float.MaxValue / 2f ? nativeHz * 2f : nativeHz;
                    end.value = end.time * state.Slope;
                    keys[keys.Length - 1] = end;
                    state.AdaptedCurve.keys = keys;
                    state.CurveEndHz = end.time;
                    curveChanged = true;
                }
                if (!state.Sustained) continue;
                if (state.Source.clip != state.CurrentClip)
                {
                    Warn("changed-clip", "Another audio change replaced an adapted stock loop clip; further rate changes for that source were skipped.");
                    continue;
                }
                int factor = state.Factor;
                int maximum = MaximumFactor(state.OriginalClip);
                while (factor < maximum && nativeHz > SustainBaseHz * factor * 2.5f) factor *= 2;
                bool factorChanged = factor > state.Factor && TryBestFactor(state, factor);
                if (curveChanged || factorChanged)
                {
                    // Bypass LayeredAudio's input cache after changing startPitch. The
                    // native per-layer method also updates a configured Doppler source.
                    state.Layer.SetPitch(nativeHz, state.Audio.linearPitchLerp, state.Audio.minPitch, state.Audio.maxPitch);
                }
                if (nativeHz > SustainBaseHz * state.Factor * 3f)
                    Warn("range:" + state.OriginalClip.GetInstanceID(),
                        "Stock chuff loop input " + nativeHz + " Hz exceeds this source's native pitch range of "
                        + (SustainBaseHz * state.Factor * 3f) + " Hz. The candidate needs a higher-rate recording for that range.");
            }
        }

        internal void AfterNativeLoops()
        {
            if (disposed) return;
            foreach (var state in layers)
                if (!state.Released && !state.Relinquished && state.Source && state.Layer.source == state.Source &&
                    state.Layer.pitchCurve == state.AdaptedCurve && state.Source.clip == state.CurrentClip &&
                    Near(state.Layer.startPitch, state.OriginalStartPitch / state.Factor)) ObservePitch(state);
        }

        static void ObservePitch(LayerState state)
        {
            state.ExpectedSourcePitch = state.Source.pitch;
            state.ExpectedDesiredPitch = state.HasDoppler && state.Layer.doppler ? state.Layer.doppler.desiredPitch : state.Source.pitch;
        }

        public void Dispose()
        {
            if (disposed) return;
            foreach (var state in layers)
            {
                if (state.Released) continue;
                if (state.Restored)
                {
                    ChuffLoopOwnership.Release(state.Layer, this, state.ResumePitch);
                    state.Released = true;
                    continue;
                }
                bool ownsSource = state.Source && state.Layer.source == state.Source;
                bool ownsClip = ownsSource && state.Source.clip == state.CurrentClip;
                bool ownsCurve = ReferenceEquals(state.Layer.pitchCurve, state.AdaptedCurve);
                bool ownsStart = Near(state.Layer.startPitch, state.OriginalStartPitch / state.Factor);
                bool sourcePitchUnchanged = ownsSource && Near(state.Source.pitch, state.ExpectedSourcePitch);
                bool ownsPitch = ownsSource && (state.HasDoppler && state.Layer.doppler
                    ? Near(state.Layer.doppler.desiredPitch, state.ExpectedDesiredPitch)
                    : Near(state.Source.pitch, state.ExpectedSourcePitch));
                if (ownsCurve) state.Layer.pitchCurve = state.OriginalCurve;
                if (ownsStart) state.Layer.startPitch = state.OriginalStartPitch;
                if (ownsClip)
                {
                    int position = state.Source.timeSamples;
                    bool wasPlaying = state.Source.isPlaying;
                    if (wasPlaying) state.Source.Stop();
                    state.Source.clip = state.OriginalClip;
                    if (ownsPitch && ownsStart && ownsCurve)
                    {
                        // Doppler may have updated the audible source after the
                        // last native loop callback. Restore our requested pitch,
                        // but do not overwrite an unrecognized source-pitch edit.
                        if (sourcePitchUnchanged) state.Source.pitch = state.OriginalSourcePitch;
                        if (state.HasDoppler && state.Layer.doppler) state.Layer.doppler.SetDesiredPitch(state.OriginalDesiredPitch);
                    }
                    if (state.OriginalClip != null && state.OriginalClip.samples > 0)
                        state.Source.timeSamples = Math.Max(0, Math.Min(position, state.OriginalClip.samples - 1));
                    if (wasPlaying) state.Source.Play();
                    if (ownsPitch && ownsStart && ownsCurve && state.Audio != null) state.Audio.Reset();
                }
                state.ResumePitch = ownsSource && ownsClip && ownsPitch && ownsStart && ownsCurve && sourcePitchUnchanged;
                state.Restored = true;
                ChuffLoopOwnership.Release(state.Layer, this, state.ResumePitch);
                state.Released = true;
            }
            foreach (var byFactor in generated.Values)
                foreach (var clip in byFactor.Values)
                    if (clip != null) UnityEngine.Object.Destroy(clip);
            generated.Clear();
            pcm.Clear();
            disposed = true; // Failed cleanup retains state and claims for retry.
            ChuffLoopOwnership.Completed(this);
        }
    }
}
