// Adapted from Loco Sound Pitch 0.2.0 PitchRules.cs. Canonical SHA256: 712733BB79234574D4286112EF5854DFB8B4B502CC0EBB97C5C48A879D533049
using System;

namespace DadsToolbox.LocoSound
{
    // Unity-independent rules shared by the plugin and its executable tests.
    internal static class PitchRules
    {
        public static bool SoundEligible(bool stockClip, bool changedClip) => stockClip && !changedClip;
        public static int SelectDonor(int candidates, int requested, int moduleCandidates, System.Collections.Generic.IDictionary<int, int> votes)
        {
            if (SingleBit(candidates)) return candidates;
            if (candidates == 0) return 0;
            if (SingleBit(requested) && (candidates & requested) == requested) return requested;
            int eligible = moduleCandidates != 0 ? candidates & moduleCandidates : candidates;
            if (SingleBit(eligible)) return eligible;
            if (eligible == 0 || votes == null) return 0;
            int donor = 0, highest = 0;
            bool tied = false;
            foreach (var vote in votes)
            {
                if (!SingleBit(vote.Key) || (eligible & vote.Key) != vote.Key || vote.Value <= 0) continue;
                if (vote.Value > highest) { donor = vote.Key; highest = vote.Value; tied = false; }
                else if (vote.Value == highest) tied = true;
            }
            return tied ? 0 : donor;
        }
        private static bool SingleBit(int value) => value != 0 && (value & unchecked(value - 1)) == 0;
        public static bool NeedsRefresh(SoundKind kind, bool eligible, bool capturedNativeInput, double targetMultiplier, double appliedMultiplier)
            => kind != SoundKind.CabEquipment && eligible && capturedNativeInput && (targetMultiplier != 1 || appliedMultiplier != 1);
        public static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        public static double Area(double diameter, double length) => Valid(diameter) && Valid(length) ? Math.PI * diameter * length : 0;
        public static bool Matches(double area, double baseline) => Valid(area) && Valid(baseline) && Math.Abs(area - baseline) <= baseline * 0.01;
        public static bool Eligible(double area, double stock060, double stock282, bool stockIdentity, int stockSteamClips)
            => !stockIdentity && stockSteamClips > 0 && Valid(area) && Valid(stock060) && Valid(stock282) && !Matches(area, stock060) && !Matches(area, stock282);
        public static double Multiplier(double area, double baseline, double strength, double floor)
        {
            if (!Valid(area) || !Valid(baseline) || double.IsNaN(strength) || double.IsInfinity(strength)) return 1;
            if (area <= baseline * 1.01 || strength <= 0) return 1;
            floor = double.IsNaN(floor) || double.IsInfinity(floor) ? 0.25 : Math.Max(0.1, Math.Min(1, floor));
            return Math.Max(floor, Math.Min(1, Math.Pow(area / baseline, -0.2 * Math.Min(10, strength))));
        }
        public static double MassMultiplier(double mass, double baseline, double strength, double floor, double ceiling)
        {
            if (!Valid(mass) || !Valid(baseline) || double.IsNaN(strength) || double.IsInfinity(strength)) return 1;
            if (Matches(mass, baseline) || strength <= 0) return 1;
            floor = double.IsNaN(floor) || double.IsInfinity(floor) ? 0.25 : Math.Max(0.1, Math.Min(1, floor));
            ceiling = double.IsNaN(ceiling) || double.IsInfinity(ceiling) ? 2 : Math.Max(1, Math.Min(3, ceiling));
            return Math.Max(floor, Math.Min(ceiling, Math.Pow(mass / baseline, -0.2 * Math.Min(10, strength))));
        }
        public static SoundKind Classify(string name)
        {
            string s = (name ?? "").ToLowerInvariant();
            // Use equipment names, never the shared cab/interior ancestry of TrainAudio.
            if (Contains(s, "cabfan", "cab_fan", "cab fan", "wiper", "radio", "controlclick", "control_click", "control click", "switchclick", "switch_click", "switch click", "buttonclick", "button_click", "button click", "leverclick", "lever_click", "lever click", "cabdoor", "cab_door", "cab door", "firedoor", "cabwindow", "cab_window", "cab window", "sidewindow") || WordPrefix(s, "door") || WordPrefix(s, "window")) return SoundKind.CabEquipment;
            if (Contains(s, "lubricator", "dynamo", "priming", "compressor", "airpump", "air_pump", "airflow", "air_flow", "hose", "cylinderexhaust", "cylinder_exhaust", "manualrelease", "manual_release", "injector", "safety", "blowdown", "coaldump", "coal_dump", "firebox", "bell", "sandflow", "sand_flow") || WordPrefix(s, "rain")) return SoundKind.Ancillary;
            if (Contains(s, "chuff")) return SoundKind.Chuffs;
            if (Contains(s, "whistle", "horn")) return SoundKind.Whistle;
            if (Contains(s, "cylindercock", "cylinder_cock", "cylinder cock")) return SoundKind.Cocks;
            if (Contains(s, "valvegear", "valve_gear", "valve gear", "clank", "rod", "chestadmission", "chest_admission")) return SoundKind.Gear;
            if (Contains(s, "brake", "squeal", "sliding")) return SoundKind.Brakes;
            if (Contains(s, "rolling", "wheel", "joint", "flange")) return SoundKind.Rolling;
            if (Contains(s, "diesel", "tractionmotor", "traction_motor", "traction motor", "gearbox", "transmission", "exhaust", "turbo", "engine")) return SoundKind.Propulsion;
            return SoundKind.Unknown;
        }
        private static bool Contains(string s, params string[] terms)
        {
            foreach (string term in terms) if (s.Contains(term)) return true;
            return false;
        }
        private static bool WordPrefix(string s, string term)
        {
            for (int start = 0; start < s.Length;)
            {
                int index = s.IndexOf(term, start, StringComparison.Ordinal);
                if (index < 0) return false;
                if (index == 0 || !char.IsLetterOrDigit(s[index - 1])) return true;
                start = index + term.Length;
            }
            return false;
        }
    }
    internal enum SoundKind { Unknown, Chuffs, Gear, Rolling, Brakes, Cocks, Whistle, Ancillary, CabEquipment, Propulsion }
}
