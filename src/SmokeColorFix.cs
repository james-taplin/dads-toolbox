using System;
using System.Linq;
using DV.Simulation.Controllers;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using UnityEngine;
using ColorReader = DV.Simulation.Controllers.ParticlesPortReadersController.ParticleColorPortReader;

namespace CCLSmokeFix
{
    // CCL steam locos ship the stack-smoke colour readers with the wrong alpha on the
    // SteamSmokeThick exhaust-flow reader: min alpha 1.0 where the vanilla S282 has 0.0.
    // At idle/low exhaust flow vanilla thick smoke is therefore nearly invisible, while on
    // CCL locos it is ~96% opaque, producing the dense dark plume that hides fog and
    // overdraws other smoke. Before a CCL loco's controller initialises, copy the vanilla
    // S282 reader colours/curve onto any matching stack-smoke reader.
    internal static class SmokeColorFix
    {
        // Explicitly registered by SmokeFixModule; never discovered by PatchAll.
        internal static void Prefix(ParticlesPortReadersController __instance)
        {
            var module = SmokeFixModule.Current;
            if (module == null || !module.CanApply() || ReferenceEquals(__instance, null)) return;
            try { Apply(__instance, module); }
            catch (Exception ex) { module.Fault(ex); }
        }

        private static void Apply(ParticlesPortReadersController controller, SmokeFixModule module)
        {
            var car = TrainCar.Resolve(controller.gameObject);
            if (car == null || car.carLivery == null || car.carLivery.GetType() == typeof(TrainCarLivery)) return; // vanilla car
            if (controller.particleColorPortReaders == null) return;

            var vanilla = GetVanillaReaders(module);
            if (vanilla == null) return;

            foreach (var reader in controller.particleColorPortReaders)
            {
                if (reader == null) continue;
                string system = StackSystemName(reader.particlesParent);
                if (system == null) continue;
                var source = vanilla.FirstOrDefault(v =>
                    v != null && v.changeType == reader.changeType &&
                    PortSuffix(v.portId) == PortSuffix(reader.portId) &&
                    StackSystemName(v.particlesParent) == system);
                if (source == null) continue;
                if (reader.startColorMin == source.startColorMin && reader.startColorMax == source.startColorMax) continue;

                var curve = new AnimationCurve(source.colorLerpCurve.keys);
                module.Log($"{car.carLivery.id} {system} {reader.portId} {reader.changeType}: min {reader.startColorMin} -> {source.startColorMin}, max {reader.startColorMax} -> {source.startColorMax}");
                reader.startColorMin = source.startColorMin;
                reader.startColorMax = source.startColorMax;
                reader.colorLerpCurve = curve;
                module.ReaderCorrected();
            }
        }

        private static ColorReader[] GetVanillaReaders(SmokeFixModule module)
        {
            // Init runs once per controller, not per frame. Resolve the native
            // prefab here so an early miss or a world reload cannot poison a cache.
            var prefab = TrainCarType.LocoSteamHeavy.ToV2()?.prefab;
            var ctrl = prefab ? prefab.GetComponentInChildren<ParticlesPortReadersController>(true) : null;
            if (ctrl == null || ctrl.particleColorPortReaders == null)
            {
                module.WaitingForTemplate();
                return null;
            }
            module.TemplateAvailable();
            return ctrl.particleColorPortReaders.ToArray();
        }

        // "SteamSmoke" or "SteamSmokeThick" if the reader drives exactly that stack system, else null.
        private static string StackSystemName(GameObject parent)
        {
            if (parent == null) return null;
            var systems = parent.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length != 1) return null;
            string name = systems[0].name.Replace("(Clone)", "").Trim();
            return name == "SteamSmoke" || name == "SteamSmokeThick" ? name : null;
        }

        private static string PortSuffix(string portId)
        {
            if (string.IsNullOrEmpty(portId)) return "";
            int dot = portId.LastIndexOf('.');
            return dot < 0 ? portId : portId.Substring(dot + 1);
        }
    }
}
