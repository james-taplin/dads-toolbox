using System;
using System.Collections.Generic;
using System.Linq;
using DadsToolbox.LocoSound;

static class RulesTests
{
    static int passed;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; }
    static void Main()
    {
        var donorVotes = new Dictionary<int, int> { { 1, 5 }, { 2, 3 }, { 4, 10 } };
        Check(PitchRules.SelectDonor(0, 1, 1, donorVotes) == 0, "custom clip with no stock candidates has no donor");
        Check(PitchRules.SelectDonor(1, 2, 2, donorVotes) == 1, "unique exact clip donor wins over incompatible requested donor and module signature");
        Check(PitchRules.SelectDonor(2, 1, 1, donorVotes) == 2, "unique exact clip donor wins over more popular incompatible donor");
        Check(PitchRules.SelectDonor(3, 2, 1, donorVotes) == 2, "valid single requested donor resolves shared clip before module and votes");
        Check(PitchRules.SelectDonor(3, 3, 0, donorVotes) == 1, "multi-bit donor override is invalid and falls back to votes");
        Check(PitchRules.SelectDonor(3, 4, 0, donorVotes) == 1, "requested donor outside clip candidates is ignored");
        Check(PitchRules.SelectDonor(3, 0, 2, donorVotes) == 2, "unique matching module donor resolves shared clip before votes");
        Check(PitchRules.SelectDonor(7, 0, 3, donorVotes) == 1, "module narrowing excludes stronger votes from another donor");
        Check(PitchRules.SelectDonor(3, 0, 4, donorVotes) == 0, "incompatible module candidates cannot invent a donor");
        Check(PitchRules.SelectDonor(3, 0, 0, null) == 0, "ambiguous shared clip without votes remains neutral");
        Check(PitchRules.SelectDonor(3, 0, 0, new Dictionary<int, int> { { 1, 5 }, { 2, 5 } }) == 0, "tied positive donor votes remain ambiguous");
        Check(PitchRules.SelectDonor(3, 0, 0, new Dictionary<int, int> { { 1, 0 }, { 2, -1 } }) == 0, "zero and negative donor votes cannot resolve ambiguity");
        Check(PitchRules.SelectDonor(3, 0, 0, new Dictionary<int, int> { { 4, 100 } }) == 0, "votes outside clip candidate mask are ignored");
        Check(PitchRules.SelectDonor(3, 0, 0, new Dictionary<int, int> { { 3, 100 }, { 1, 1 } }) == 1, "multi-bit vote keys do not count as individual donors");
        Check(PitchRules.SelectDonor(7, 0, 0, new Dictionary<int, int> { { 1, 3 }, { 2, 3 }, { 4, 5 } }) == 4, "later higher vote resolves an earlier tie");
        Check(PitchRules.SelectDonor(7, 0, 3, new Dictionary<int, int> { { 1, 3 }, { 2, 3 }, { 4, 5 } }) == 0, "module narrowing preserves ambiguity among equally supported allowed donors");
        Check(PitchRules.SelectDonor(unchecked((int)0x80000000), 0, 0, null) == unchecked((int)0x80000000), "highest mask bit is a valid unique donor");
        Check(Math.Abs(PitchRules.Area(2, 5) - Math.PI * 10) < 1e-10, "barrel area in square metres");
        Check(PitchRules.Area(0, 5) == 0 && PitchRules.Area(-2, 5) == 0, "invalid dimensions");
        Check(PitchRules.Area(double.NaN, 5) == 0 && PitchRules.Area(2, double.PositiveInfinity) == 0, "nonfinite dimensions");
        Check(!PitchRules.Eligible(400, 50, 100, true, 100), "stock identity protected even if dimensions changed");
        Check(PitchRules.Eligible(50, 50, 100, false, 100), "custom S060-size boiler remains eligible for a different sound donor");
        Check(PitchRules.Eligible(100, 50, 100, false, 100), "custom S282-size boiler remains eligible for S060 clips");
        Check(PitchRules.Eligible(50.4, 50, 100, false, 100), "size tolerance is resolved per sound donor rather than excluding the whole locomotive");
        Check(!PitchRules.Eligible(400, 50, 100, false, 0), "custom-only audio protected");
        Check(!PitchRules.Eligible(400, 0, 100, false, 10), "missing baseline fails closed");
        Check(!PitchRules.Eligible(double.NaN, 50, 100, false, 10), "invalid boiler fails closed");
        Check(PitchRules.Eligible(150, 50, 100, false, 10), "larger mod with stock sounds eligible");
        Check(PitchRules.Multiplier(100, 100, 10, 0.1) == 1, "equal size unchanged even at extreme setting");
        Check(PitchRules.Multiplier(50, 100, 10, 0.1) == 2, "smaller boiler raises pitch up to default steam ceiling");
        Check(Math.Abs(PitchRules.Multiplier(50, 100, 1, 0.25) - 1.1486983550) < 1e-9, "half donor boiler area raises steam pitch at 100 percent strength");
        Check(PitchRules.Multiplier(99, 100, 10, .1) == 1 && PitchRules.Multiplier(101, 100, 10, .1) == 1, "equal-size tolerance is symmetric about actual boiler donor");
        Check(PitchRules.Multiplier(98.9, 100, 1, .1) > 1 && PitchRules.Multiplier(101.1, 100, 1, .1) < 1, "outside tolerance steam pitch changes in the requested direction");
        Check(PitchRules.Multiplier(50,100,0,.25,2)==1 && PitchRules.Multiplier(200,100,0,.25,2)==1,"zero-percent steam strength is neutral in both directions");
        Check(PitchRules.Multiplier(1,1000,10,.25,1.1)==1.1 && PitchRules.Multiplier(1000,1,10,.9,2)==.9,"independent steam upper and lower caps bound the effect");
        Check(PitchRules.Multiplier(44.5,47,6.4955945,.25,2)>1,"logged C48A boiler with S282 donor now pitches upward");
        Check(PitchRules.Multiplier(47,47,6.4955945,.25,2)==1 && PitchRules.Multiplier(47,18.06,1,.25,2)<1,"L74 is neutral for S282 sounds and deeper for S060 sounds");
        Check(PitchRules.Multiplier(1,1000,10,.1,double.NaN)==2 && PitchRules.Multiplier(1,1000,10,.1,double.PositiveInfinity)==2,"invalid steam ceiling uses default bound");
        Check(PitchRules.Multiplier(1,1000,10,.1,-10)==1 && PitchRules.Multiplier(1,1000,10,.1,10)==3,"steam ceiling clamps to supported interval");
        Check(PitchRules.Multiplier(double.MaxValue,double.Epsilon,10,.1,3)==.1 && PitchRules.Multiplier(double.Epsilon,double.MaxValue,10,.1,3)==3,"extreme steam ratios stay within finite pitch caps");
        Check(PitchRules.Multiplier(200, 100, 0, 0.1) == 1, "zero strength gives unmodified pitch");
        Check(Math.Abs(PitchRules.Multiplier(200, 100, 1, 0.25) - 0.8705505633) < 1e-9, "normal two-times size tuning");
        Check(Math.Abs(PitchRules.Multiplier(200, 100, 10, 0.1) - 0.25) < 1e-9, "extreme two-times size tuning");
        Check(PitchRules.Multiplier(1e9, 1, 10, 0.1) == 0.1, "extreme floor prevents zero pitch");
        Check(PitchRules.Multiplier(200, 100, 1, double.NaN) > 0, "corrupt floor remains safe");
        Check(PitchRules.Multiplier(200, 100, double.NaN, 0.1) == 1, "corrupt strength fails closed");
        Check(PitchRules.Multiplier(200, 0, 1, 0.1) == 1, "invalid donor fails closed");
        Check(Math.Abs(PitchRules.MassMultiplier(200, 100, 1, 0.25, 2) - 0.8705505633) < 1e-9, "twice donor mass lowers nonsteam pitch");
        Check(Math.Abs(PitchRules.MassMultiplier(50, 100, 1, 0.25, 2) - 1.1486983550) < 1e-9, "half donor mass raises nonsteam pitch");
        Check(PitchRules.MassMultiplier(100, 100, 10, 0.1, 3) == 1, "same donor mass stays neutral at full strength");
        Check(PitchRules.MassMultiplier(99, 100, 10, 0.1, 3) == 1 && PitchRules.MassMultiplier(101, 100, 10, 0.1, 3) == 1, "mass within one percent donor tolerance stays neutral in both directions");
        Check(PitchRules.MassMultiplier(98.9, 100, 1, 0.1, 3) > 1 && PitchRules.MassMultiplier(101.1, 100, 1, 0.1, 3) < 1, "mass outside donor tolerance shifts in the expected direction");
        Check(PitchRules.MassMultiplier(50, 100, 0, 0.25, 2) == 1 && PitchRules.MassMultiplier(200, 100, -1, 0.25, 2) == 1, "zero and negative mass strength disable shifting");
        Check(PitchRules.MassMultiplier(150, 100, 20, 0.1, 3) == PitchRules.MassMultiplier(150, 100, 10, 0.1, 3), "mass strength clamps at ten");
        foreach (double invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Check(PitchRules.MassMultiplier(invalid, 100, 1, 0.25, 2) == 1, "invalid target mass fails neutral");
            Check(PitchRules.MassMultiplier(100, invalid, 1, 0.25, 2) == 1, "invalid donor mass fails neutral");
        }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Check(PitchRules.MassMultiplier(200, 100, invalid, 0.25, 2) == 1, "nonfinite mass strength fails neutral");
        Check(PitchRules.MassMultiplier(1000, 1, 10, double.NaN, 2) == 0.25 && PitchRules.MassMultiplier(1000, 1, 10, double.PositiveInfinity, 2) == 0.25, "nonfinite mass floor defaults to quarter pitch");
        Check(PitchRules.MassMultiplier(1, 1000, 10, 0.25, double.NaN) == 2 && PitchRules.MassMultiplier(1, 1000, 10, 0.25, double.PositiveInfinity) == 2, "nonfinite mass ceiling defaults to twice pitch");
        Check(PitchRules.MassMultiplier(1000, 1, 10, -10, 3) == 0.1 && PitchRules.MassMultiplier(1000, 1, 10, 10, 3) == 1, "finite mass floor clamps to supported interval");
        Check(PitchRules.MassMultiplier(1, 1000, 10, 0.1, -10) == 1 && PitchRules.MassMultiplier(1, 1000, 10, 0.1, 10) == 3, "finite mass ceiling clamps to supported interval");
        Check(PitchRules.MassMultiplier(double.MaxValue, double.Epsilon, 10, 0.1, 3) == 0.1, "overflowing mass ratio stays at lower pitch bound");
        Check(PitchRules.MassMultiplier(double.Epsilon, double.MaxValue, 10, 0.1, 3) == 3, "underflowing mass ratio stays at upper pitch bound");
        for (double ratio = 0.1; ratio <= 10; ratio += 0.1)
        {
            double previous = 1;
            for (double strength = 0; strength <= 10; strength += 0.25)
            {
                double multiplier = PitchRules.MassMultiplier(ratio, 1, strength, 0.1, 3);
                Check(multiplier >= 0.1 && multiplier <= 3 && (ratio < 0.99 ? multiplier >= previous - 1e-12 : multiplier <= previous + 1e-12), "mass strength monotonically moves pitch in the expected direction within bounds");
                previous = multiplier;
            }
        }
        double previousMassPitch = 3;
        for (double ratio = 0.1; ratio <= 10; ratio += 0.01)
        {
            double multiplier = PitchRules.MassMultiplier(ratio, 1, 2, 0.1, 3);
            Check(multiplier <= previousMassPitch + 1e-12, "pitch falls monotonically as target mass increases");
            previousMassPitch = multiplier;
        }
        for (double ratio = .02; ratio < 20; ratio += 0.2)
        {
            double previous = 1;
            for (double strength = 0; strength <= 10; strength += 0.1)
            {
                double multiplier = PitchRules.Multiplier(ratio, 1, strength, 0.1, 3);
                Check(multiplier >= .1 && multiplier <= 3 && (ratio < .99 ? multiplier >= previous - 1e-12 : multiplier <= previous + 1e-12), "steam strength monotonically changes pitch in the expected direction within both caps");
                previous = multiplier;
            }
        }
        foreach (var name in new[] { "BrakeModule/Airflow/AirflowLayers", "BrakeModule/Brake/CylinderExhaust_Layered", "Steam/Lubricator", "Steam/Dynamo", "Steam/PrimingCrank", "Steam/AirPump", "Steam/Compressor", "Steam/Injector", "Steam/SafetyRelease", "Steam/FireboxWind", "BrakeCylinderManualRelease" })
            Check(PitchRules.Classify(name) == SoundKind.Ancillary, "exterior ancillary classification: " + name);
        foreach (var name in new[] { "CabFan", "Cab_Fan", "Cab Fan", "WiperMotor", "RadioClick", "ControlClick", "switch_click", "Switch Click", "ButtonClick", "Lever_Click", "CabDoor", "WindowSlide" })
            Check(PitchRules.Classify(name) == SoundKind.CabEquipment, "cabin equipment classification: " + name);
        foreach (var name in new[] { "DieselLoop", "TractionMotor", "Traction_Motor", "Traction Motor", "Gearbox", "Transmission", "EngineExhaust", "TurboLoop", "EngineStartup" })
            Check(PitchRules.Classify(name) == SoundKind.Propulsion, "propulsion classification: " + name);
        Check(PitchRules.Classify("Cab/Interior/TrainAudio/DieselLoop") == SoundKind.Propulsion, "cab and interior shared ancestry does not exclude exterior engine audio");
        Check(PitchRules.Classify("TrainAudio/RainAudio") == SoundKind.Ancillary, "rain remains recognized without confusing train with rain");
        Check(PitchRules.Classify("Diesel/Engine/Horn") == SoundKind.Whistle, "specific horn classification precedes generic propulsion");
        Check(PitchRules.Classify("Engine/Compressor") == SoundKind.Ancillary, "exterior compressor classification precedes generic propulsion");
        Check(PitchRules.Classify("[sim] Engine/SteamChuff/3ChuffsPerSecond") == SoundKind.Chuffs, "high speed chuffs");
        Check(PitchRules.Classify("Steam/ValveGearDamaged") == SoundKind.Gear, "damaged valve gear");
        Check(PitchRules.Classify("Steam/CylinderCock") == SoundKind.Cocks, "cylinder cocks distinct from brake exhaust");
        Check(PitchRules.Classify("BrakeModule/Brake/Brake_Layered") == SoundKind.Brakes, "braking friction");
        Check(PitchRules.Classify("RollingAudioDetailed") == SoundKind.Rolling, "rolling");
        Check(PitchRules.Classify("Steam/Whistle/Whistle_Layered") == SoundKind.Whistle, "whistle");
        Check(PitchRules.Classify("UnclassifiedSound") == SoundKind.Unknown, "unknown sound conservative");
        Check(PitchRules.SoundEligible(true, false), "untouched stock audio remains eligible");
        Check(!PitchRules.SoundEligible(false, false), "custom clip loaded before first scan exempt even with fallback");
        Check(!PitchRules.SoundEligible(true, true), "runtime source/clip change invalidates cached stock eligibility immediately");
        Check(PitchRules.NeedsRefresh(SoundKind.Ancillary, true, true, 0.5, 1), "exterior dynamo may shift using captured native input");
        Check(PitchRules.NeedsRefresh(SoundKind.Propulsion, true, true, 1.5, 1), "nonsteam propulsion may shift upward using captured native input");
        Check(!PitchRules.NeedsRefresh(SoundKind.CabEquipment, true, true, 0.5, 1), "cabin equipment remains excluded even when stock-owned and native input captured");
        Check(PitchRules.NeedsRefresh(SoundKind.Chuffs, true, true, PitchRules.Multiplier(.60,1,1,.25), 1), "smaller boiler using shared S282 sounds refreshes captured native pitch upward");
        Check(!PitchRules.NeedsRefresh(SoundKind.Chuffs, true, true, 1, 1), "only neutral donor scaling avoids manual pitch writes");
        Check(!PitchRules.NeedsRefresh(SoundKind.Chuffs, true, false, 0.8, 1), "first scan never invents a native pitch input");
        Check(!PitchRules.NeedsRefresh(SoundKind.Chuffs, false, true, 0.8, 0.8), "replacement ownership blocks restoration writes");
        Check(PitchRules.NeedsRefresh(SoundKind.Chuffs, true, true, 1, 0.8), "disable restores a previously adjusted sound");
        Check(PitchRules.NeedsRefresh(SoundKind.Chuffs, true, true, 0.7, 0.8), "strength edits replay captured native arguments for eligible sounds");
        Check(PitchRules.NeedsRefresh(SoundKind.Ancillary, true, true, 1, 0.8), "disabling restores previously shifted exterior ancillary sound");
        Check(!PitchRules.NeedsRefresh(SoundKind.CabEquipment, true, true, 1, 0.8), "cabin hard exclusion overrides restoration path");
        Check(!PitchRules.NeedsRefresh(SoundKind.Ancillary, false, true, 0.5, 0.8), "custom replacement ancillary clip stays unchanged");
        int writes = 0;
        double dynamoPitch = 0.96;
        for (int scan = 0; scan < 100; scan++)
            if (PitchRules.NeedsRefresh(SoundKind.Ancillary, true, false, 1, 1)) { writes++; dynamoPitch = 25; }
        Check(writes == 0 && dynamoPitch == 0.96, "100 discovery passes leave native dynamo pitch untouched instead of replaying a generic 90kph input");
        Check(PitchRules.Classify("dynamo.DYNAMO_FLOW_NORMALIZED") == SoundKind.Ancillary, "dynamo exterior equipment recognized by port even with renamed hierarchy");
        Console.WriteLine($"PASS: {passed} rule and boundary checks.");
    }
}
