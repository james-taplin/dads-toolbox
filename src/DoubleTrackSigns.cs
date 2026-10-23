using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DV.OriginShift;
using UnityEngine;
using UnityModManagerNet;

namespace DadsToolbox
{
    // Serialized by the parent's one UMM settings file.
    public sealed class SignClearanceOptions
    {
        public float MinimumClearance = 2.0f;
        public float MaximumMove = 8.0f;
        public float SearchStep = 0.05f;
        public bool VerboseLogging = true;

        public void Clamp()
        {
            if (float.IsNaN(MinimumClearance) || float.IsInfinity(MinimumClearance)) MinimumClearance = 2.0f;
            if (float.IsNaN(MaximumMove) || float.IsInfinity(MaximumMove)) MaximumMove = 8.0f;
            if (float.IsNaN(SearchStep) || float.IsInfinity(SearchStep)) SearchStep = 0.05f;
            MinimumClearance = Mathf.Clamp(MinimumClearance, 0.5f, 10.0f);
            MaximumMove = Mathf.Clamp(MaximumMove, 0.5f, 25.0f);
            SearchStep = Mathf.Clamp(SearchStep, 0.01f, 0.25f);
        }
    }

    // Parent-dispatched module: no ModEntry callbacks, settings writer, event
    // subscription, Harmony patches, or independent MonoBehaviour.
    public sealed class DoubleTrackSigns
    {
        public const string DependencyId = "DoubleTrack";
        public const string StandaloneId = "DoubleTrackSignClearance";
        private static readonly Version SupportedVersion = new Version(2, 1, 1);
        private readonly SignClearanceOptions options;
        private readonly Action<string> logger;
        private readonly SignClearanceEngine engine;
        private bool desired, parentActive, unloaded;
        private float nextScanTime;
        private string fault;

        public bool Active { get; private set; }
        public string Status { get; private set; } = "Inactive";
        public bool RequiresRestartForRestoration { get { return engine.HasMovedSigns; } }

        public DoubleTrackSigns(SignClearanceOptions options, Action<string> logger)
        {
            this.options = options ?? new SignClearanceOptions();
            this.logger = logger;
            this.options.Clamp();
            engine = new SignClearanceEngine(this.options, Log);
        }

        public void Configure(bool selected, bool toolboxActive)
        {
            desired = selected;
            parentActive = toolboxActive;
            options.Clamp();
            Reconcile();
        }

        public void Refresh()
        {
            if (unloaded) return;
            options.Clamp();
            fault = null;
            engine.Reset();
            nextScanTime = 0;
            Reconcile();
        }

        public void Tick(float realtime)
        {
            if (unloaded || realtime < nextScanTime) return;
            nextScanTime = realtime + 1.0f;
            Reconcile();
            if (!Active) return;
            try { engine.Scan(); }
            catch (Exception ex)
            {
                fault = ex.GetType().Name + ": " + ex.Message;
                Log("Sign scan exception: " + ex);
                Stop("Inactive: sign scan failed (" + fault + "). Re-scan to retry.");
            }
        }

        public void Unload()
        {
            unloaded = true;
            engine.Reset();
            Stop("Unloaded");
        }

        private void Reconcile()
        {
            if (unloaded) return;
            if (!parentActive) { Stop("Toolbox disabled"); return; }
            if (!desired) { Stop("Off"); return; }
            if (fault != null) { Stop("Inactive: sign scan failed (" + fault + "). Re-scan to retry."); return; }
            try
            {
                var standalone = UnityModManager.FindMod(StandaloneId);
                // The original 1.0.0 loader never stopped its watcher. Even an
                // inactive loaded entry can still move signs until a restart.
                if (standalone != null && (standalone.Loaded || standalone.Active))
                {
                    Stop("Inactive: standalone DoubleTrack Sign Clearance is loaded. Disable or remove it and restart.");
                    return;
                }
                var dependency = UnityModManager.FindMod(DependencyId);
                if (dependency == null || !dependency.Loaded || !dependency.Active)
                {
                    Stop("Inactive: DoubleTrack is absent or disabled.");
                    return;
                }
                if (dependency.Version != SupportedVersion)
                {
                    Stop("Inactive: supported DoubleTrack version is 2.1.1; found " + dependency.Version + ".");
                    return;
                }
                string reason;
                if (!engine.Bind(dependency.Assembly, out reason)) { Stop("Inactive: " + reason); return; }
                Active = true;
                SetStatus("Active: DoubleTrack 2.1.1 sign clearance.");
            }
            catch (Exception ex)
            {
                fault = ex.GetType().Name + ": " + ex.Message;
                Stop("Inactive: sign integration failed (" + fault + "). Re-scan to retry.");
            }
        }

        private void Stop(string reason)
        {
            if (Active) engine.Reset();
            Active = false;
            SetStatus(reason);
        }

        private void SetStatus(string value)
        {
            if (Status == value) return;
            Status = value;
            Log(value);
        }

        private void Log(string text)
        {
            // A logger failure must not escape a UMM callback or interrupt a
            // successful correction after its Transform has already moved.
            try { if (logger != null) logger(text); } catch { }
        }
    }

    internal sealed class SignClearanceEngine
    {
        private const float GridCellSize = 10.0f;
        private const float DistanceTolerance = 0.005f;
        private readonly Dictionary<long, List<int>> SegmentGrid = new Dictionary<long, List<int>>();
        private readonly List<TrackSegment> Segments = new List<TrackSegment>();
        private readonly HashSet<int> ProcessedSigns = new HashSet<int>();
        private readonly HashSet<int> QueryDeduplicator = new HashSet<int>();
        private readonly SignClearanceOptions options;
        private readonly Action<string> log;
        private Assembly boundAssembly;
        private FieldInfo signsField, signDebugTextField;
        private Type signDebugType;
        private GameObject railwayRoot;
        private RailTrack[] indexedTrackArray;
        private IList indexedSigns;
        internal bool HasMovedSigns { get; private set; }

        internal SignClearanceEngine(SignClearanceOptions options, Action<string> log)
        {
            this.options = options;
            this.log = log;
        }

        internal bool Bind(Assembly assembly, out string reason)
        {
            reason = "DoubleTrack's generated-sign API is incompatible.";
            if (assembly == null || assembly.GetName().Name != "DoubleTrack") return false;
            if (boundAssembly == assembly && signsField != null) return true;
            var type = assembly.GetType("DV.Signs.CustomSignPlacer", false);
            var field = type == null ? null : type.GetField("signs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || !typeof(IList).IsAssignableFrom(field.FieldType) ||
                !typeof(IList<GameObject>).IsAssignableFrom(field.FieldType)) return false;
            Reset();
            boundAssembly = assembly;
            signsField = field;
            signDebugType = assembly.GetType("DV.Signs.SignDebug", false);
            signDebugTextField = signDebugType == null ? null : signDebugType.GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (signDebugTextField != null && signDebugTextField.FieldType != typeof(string)) signDebugTextField = null;
            return true;
        }

        internal void Reset()
        {
            SegmentGrid.Clear();
            Segments.Clear();
            ProcessedSigns.Clear();
            QueryDeduplicator.Clear();
            indexedTrackArray = null;
            indexedSigns = null;
            railwayRoot = null;
        }

        internal void Scan()
        {
            IList signs = signsField.GetValue(null) as IList;
            if (signs == null || signs.Count == 0) { Reset(); return; }
            // DoubleTrack assigns a new list when it regenerates signs.
            if (!ReferenceEquals(signs, indexedSigns)) { Reset(); indexedSigns = signs; }
            if (!EnsureTrackIndex()) return;
            for (int i = 0; i < signs.Count; i++)
            {
                GameObject sign = signs[i] as GameObject;
                if (sign == null) continue;
                int instanceId = sign.GetInstanceID();
                if (!ProcessedSigns.Contains(instanceId) && TryCorrectSign(sign)) ProcessedSigns.Add(instanceId);
            }
        }
        private bool EnsureTrackIndex()
        {
            var registry = RailTrackRegistryBase.Instance;
            // During world reload the registry can exist before [railway].
            // OrderedRailtracks lazily dereferences that root, so do not call
            // it until the scene is ready. Discard the previous world's index.
            if (registry == null)
            {
                Reset();
                return false;
            }
            // Cache the live root; avoid a scene-wide lookup on every scan.
            if (railwayRoot == null || !railwayRoot.activeInHierarchy)
                railwayRoot = GameObject.Find("[railway]");
            if (railwayRoot == null)
            {
                Reset();
                return false;
            }
            RailTrack[] currentTracks = registry.OrderedRailtracks;
            if (currentTracks == null || currentTracks.Length == 0)
            {
                Reset();
                return false;
            }

            if (ReferenceEquals(currentTracks, indexedTrackArray) && Segments.Count > 0)
            {
                return true;
            }

            indexedTrackArray = currentTracks;
            SegmentGrid.Clear();
            Segments.Clear();
            ProcessedSigns.Clear();

            int usableTracks = 0;
            for (int trackIndex = 0; trackIndex < currentTracks.Length; trackIndex++)
            {
                RailTrack track = currentTracks[trackIndex];
                if (track == null)
                {
                    continue;
                }

                DV.PointSet.EquiPointSet pointSet;
                try
                {
                    pointSet = track.GetKinkedPointSet();
                }
                catch
                {
                    continue;
                }

                if (pointSet == null || pointSet.points == null || pointSet.points.Length < 2)
                {
                    continue;
                }

                usableTracks++;
                for (int pointIndex = 0; pointIndex < pointSet.points.Length - 1; pointIndex++)
                {
                    Vector3 first = (Vector3)pointSet.points[pointIndex].position;
                    Vector3 second = (Vector3)pointSet.points[pointIndex + 1].position;
                    AddSegment(
                        new Vector2(first.x, first.z),
                        new Vector2(second.x, second.z),
                        track.name);
                }
            }

            log("Indexed " + Segments.Count + " track segments from " +
                usableTracks + " tracks for sign-clearance checks.");
            return Segments.Count > 0;
        }

        private void AddSegment(Vector2 start, Vector2 end, string trackName)
        {
            if ((end - start).sqrMagnitude < 0.000001f)
            {
                return;
            }

            int segmentIndex = Segments.Count;
            Segments.Add(new TrackSegment(start, end, trackName));

            int minX = CellFor(Mathf.Min(start.x, end.x));
            int maxX = CellFor(Mathf.Max(start.x, end.x));
            int minZ = CellFor(Mathf.Min(start.y, end.y));
            int maxZ = CellFor(Mathf.Max(start.y, end.y));

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    long key = GridKey(x, z);
                    List<int> bucket;
                    if (!SegmentGrid.TryGetValue(key, out bucket))
                    {
                        bucket = new List<int>();
                        SegmentGrid.Add(key, bucket);
                    }
                    bucket.Add(segmentIndex);
                }
            }
        }

        private bool TryCorrectSign(GameObject sign)
        {
            Vector3 originShift = OriginShift.currentMove;
            Vector3 originalWorld = sign.transform.position;
            Vector3 originalAbsolute3 = originalWorld - originShift;
            Vector2 originalAbsolute = new Vector2(originalAbsolute3.x, originalAbsolute3.z);

            NearestTrackPoint initialNearest;
            float clearance = options.MinimumClearance;
            if (!TryFindNearestWithin(originalAbsolute, clearance, out initialNearest))
            {
                return true;
            }

            Vector3 outward3 = -sign.transform.right;
            Vector2 outward = new Vector2(outward3.x, outward3.z);
            if (outward.sqrMagnitude < 0.5f)
            {
                log("Warning: " + "Could not determine an outward direction for sign " + GetSignLabel(sign) + ".");
                return true;
            }
            outward.Normalize();

            float step = options.SearchStep;
            float maximumMove = options.MaximumMove;
            for (float moved = step; moved <= maximumMove + DistanceTolerance; moved += step)
            {
                Vector2 candidate = originalAbsolute + (outward * moved);
                NearestTrackPoint candidateNearest;
                if (TryFindNearestWithin(candidate, clearance, out candidateNearest))
                {
                    continue;
                }

                Vector3 correctedWorld = originalWorld + new Vector3(outward.x * moved, 0.0f, outward.y * moved);
                sign.transform.position = correctedWorld;
                HasMovedSigns = true;

                if (options.VerboseLogging)
                {
                    log(
                        "Moved " + GetSignLabel(sign) + " outward " + moved.ToString("0.00") +
                        " m; nearest track was " + initialNearest.TrackName + " at " +
                        initialNearest.Distance.ToString("0.00") + " m.");
                }
                return true;
            }

            log("Warning: " + 
                "Could not give " + GetSignLabel(sign) + " the requested " +
                clearance.ToString("0.00") + " m clearance within the " +
                maximumMove.ToString("0.0") + " m movement limit. It was left in place.");
            return true;
        }

        private bool TryFindNearestWithin(
            Vector2 point,
            float radius,
            out NearestTrackPoint nearest)
        {
            float radiusSquared = radius * radius;
            float bestSquared = radiusSquared - DistanceTolerance;
            bool found = false;
            string bestTrackName = null;
            Vector2 bestPoint = default(Vector2);

            int minX = CellFor(point.x - radius);
            int maxX = CellFor(point.x + radius);
            int minZ = CellFor(point.y - radius);
            int maxZ = CellFor(point.y + radius);

            QueryDeduplicator.Clear();
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    List<int> bucket;
                    if (!SegmentGrid.TryGetValue(GridKey(x, z), out bucket))
                    {
                        continue;
                    }

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        int segmentIndex = bucket[i];
                        if (!QueryDeduplicator.Add(segmentIndex))
                        {
                            continue;
                        }

                        TrackSegment segment = Segments[segmentIndex];
                        Vector2 closest = ClosestPointOnSegment(point, segment.Start, segment.End);
                        float squared = (point - closest).sqrMagnitude;
                        if (squared < bestSquared)
                        {
                            found = true;
                            bestSquared = squared;
                            bestTrackName = segment.TrackName;
                            bestPoint = closest;
                        }
                    }
                }
            }

            nearest = new NearestTrackPoint(
                bestTrackName,
                bestPoint,
                found ? Mathf.Sqrt(bestSquared) : float.PositiveInfinity);
            return found;
        }

        private Vector2 ClosestPointOnSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.000001f)
            {
                return start;
            }

            float amount = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return start + (segment * amount);
        }

        private string GetSignLabel(GameObject sign)
        {
            string text = null;
            if (signDebugType != null && signDebugTextField != null)
            {
                Component debugComponent = sign.GetComponent(signDebugType);
                if (debugComponent != null)
                {
                    text = signDebugTextField.GetValue(debugComponent) as string;
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = sign.name;
            }
            return "sign '" + text.Replace("\r", " ").Replace("\n", " / ") + "'";
        }

        private int CellFor(float coordinate)
        {
            return Mathf.FloorToInt(coordinate / GridCellSize);
        }

        private long GridKey(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        private struct TrackSegment
        {
            internal readonly Vector2 Start;
            internal readonly Vector2 End;
            internal readonly string TrackName;

            internal TrackSegment(Vector2 start, Vector2 end, string trackName)
            {
                Start = start;
                End = end;
                TrackName = trackName;
            }
        }

        private struct NearestTrackPoint
        {
            internal readonly string TrackName;
            internal readonly Vector2 Point;
            internal readonly float Distance;

            internal NearestTrackPoint(string trackName, Vector2 point, float distance)
            {
                TrackName = trackName;
                Point = point;
                Distance = distance;
            }
        }
    }
}

