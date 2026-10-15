using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DV.CabControls;
using DV.HUD;
using DV.Interaction.Inputs;
using DV.KeyboardInput;
using DV.Simulation.Brake;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using DV.Simulation.Ports;
using DV.ThingTypes;
using HarmonyLib;
using LocoSim.Implementations;
using UnityEngine;


using DV.CabControls.Spec;

namespace DVCCLControlFix;

[HarmonyPatch(typeof(BaseControlsOverrider), "Init", new Type[]
{
	typeof(TrainCar),
	typeof(SimulationFlow)
})]
internal static class BaseControlsOverrider_Init_Patch
{
	private struct Candidate(string fieldName, string portId, Type controlType)
	{
		public readonly string FieldName = fieldName;

		public readonly string PortId = portId;

		public readonly Type ControlType = controlType;
	}

	private static readonly Candidate[] Candidates = new Candidate[]
	{
		new Candidate("throttle", "throttle.EXT_IN", typeof(ThrottleControl)),
		new Candidate("reverser", "reverser.CONTROL_EXT_IN", typeof(ReverserControl)),
		new Candidate("brake", "brake.EXT_IN", typeof(BrakeControl)),
		new Candidate("independentBrake", "indBrake.EXT_IN", typeof(IndependentBrakeControl)),
		new Candidate("brakeCutout", "brakeCutout.EXT_IN", typeof(BrakeCutoutControl)),
		new Candidate("sander", "sanders.EXT_IN", typeof(SanderControl)),
		new Candidate("horn", "horn.EXT_IN", typeof(HornControl)),
		new Candidate("horn", "whistle.EXT_IN", typeof(HornControl)),
		new Candidate("dynamo", "dynamoControl.EXT_IN", typeof(DynamoControl)),
		new Candidate("airPump", "compressorControl.EXT_IN", typeof(AirPumpControl)),
		new Candidate("dynamicBrake", "dynamicBrake.EXT_IN", typeof(DynamicBrakeControl)),
		new Candidate("sander", "sander.CONTROL_EXT_IN", typeof(SanderControl))
	};

	private static void Prefix(BaseControlsOverrider __instance, TrainCar car)
	{
		try
		{
			if (!Main.Enabled || __instance == null || car == null || !KeyboardInputFixer.IsCclLivery(car.carLivery))
			{
				return;
			}
			HashSet<string> hashSet = new HashSet<string>();
			CollectFeederPorts(car.carLivery.interiorPrefab, hashSet);
			CollectFeederPorts(car.carLivery.externalInteractablesPrefab, hashSet);
			if (hashSet.Count == 0)
			{
				return;
			}
			Candidate[] candidates = Candidates;
			for (int i = 0; i < candidates.Length; i++)
			{
				Candidate candidate = candidates[i];
				FieldInfo fieldInfo = AccessTools.Field(typeof(BaseControlsOverrider), candidate.FieldName);
				if (!(fieldInfo == null) && fieldInfo.GetValue(__instance) == null && hashSet.Contains(candidate.PortId))
				{
					OverridableBaseControl overridableBaseControl = __instance.GetComponent(candidate.ControlType) as OverridableBaseControl;
					OverridableBaseControl overridableBaseControl2 = ((overridableBaseControl != null) ? overridableBaseControl : ((OverridableBaseControl)__instance.gameObject.AddComponent(candidate.ControlType)));
					overridableBaseControl2.portId = candidate.PortId;
					fieldInfo.SetValue(__instance, overridableBaseControl2);
					Main.MarkMutation();
					if (Main.Logger != null)
					{
						Main.Logger.Log($"[DVCCLControlFix] {car.carLivery.id}: wired missing {candidate.ControlType.Name} -> port '{candidate.PortId}' (loco never assigned it itself)");
					}
				}
			}
		}
		catch (Exception ex)
		{
			if (Main.Logger != null)
			{
				Main.Logger.Error("[DVCCLControlFix] BaseControlsOverrider.Init prefix failed: " + ex);
			}
		}
	}

	private static void CollectFeederPorts(GameObject prefab, HashSet<string> into)
	{
		if (prefab == null)
		{
			return;
		}
		InteractablePortFeedersController component = prefab.GetComponent<InteractablePortFeedersController>();
		if (component == null || component.entries == null)
		{
			return;
		}
		InteractablePortFeeder[] entries = component.entries;
		foreach (InteractablePortFeeder interactablePortFeeder in entries)
		{
			if (interactablePortFeeder != null && !string.IsNullOrEmpty(interactablePortFeeder.portId))
			{
				into.Add(interactablePortFeeder.portId);
			}
		}
	}
}
internal static class KeyboardInputFixer
{
	private struct ActionPair(string incremental, string toggle)
	{
		public readonly string Incremental = incremental;

		public readonly string Toggle = toggle;
	}

	private static readonly Dictionary<InteriorControlsManager.ControlType, ActionPair> Actions = new Dictionary<InteriorControlsManager.ControlType, ActionPair>
	{
		{
			InteriorControlsManager.ControlType.Throttle,
			new ActionPair("ThrottleIncremental", null)
		},
		{
			InteriorControlsManager.ControlType.TrainBrake,
			new ActionPair("BrakeIncremental", null)
		},
		{
			InteriorControlsManager.ControlType.IndBrake,
			new ActionPair("IndependentBrakeIncremental", null)
		},
		{
			InteriorControlsManager.ControlType.Reverser,
			new ActionPair("ReverserIncremental", null)
		},
		{
			InteriorControlsManager.ControlType.DynamicBrake,
			new ActionPair("DynamicBrakeIncremental", null)
		},
		{
			InteriorControlsManager.ControlType.TrainBrakeCutout,
			new ActionPair("BrakeCutoutIncremental", "BrakeCutoutToggle")
		},
		{
			InteriorControlsManager.ControlType.Handbrake,
			new ActionPair("HandbrakeIncremental", "HandbrakeToggle")
		},
		{
			InteriorControlsManager.ControlType.Sander,
			new ActionPair("SandIncremental", "SandToggle")
		},
		{
			InteriorControlsManager.ControlType.Horn,
			new ActionPair("HornIncremental", null)
		},
		{
			InteriorControlsManager.ControlType.HeadlightsFront,
			new ActionPair("HeadlightFrontIncremental", null)
		},
		{
			InteriorControlsManager.ControlType.HeadlightsRear,
			new ActionPair("HeadlightRearIncremental", null)
		},
		{
			InteriorControlsManager.ControlType.CabLight,
			new ActionPair("CabLightIncremental", "CabLightToggle")
		},
		{
			InteriorControlsManager.ControlType.Wipers,
			new ActionPair("WiperIncremental", "WiperToggle")
		},
		{
			InteriorControlsManager.ControlType.CylCock,
			new ActionPair("CylCockIncremental", "CylCockToggle")
		},
		{
			InteriorControlsManager.ControlType.Injector,
			new ActionPair("InjectorIncremental", "InjectorToggle")
		},
		{
			InteriorControlsManager.ControlType.Firedoor,
			new ActionPair("FiredoorIncremental", "FiredoorToggle")
		},
		{
			InteriorControlsManager.ControlType.Blower,
			new ActionPair("BlowerIncremental", "BlowerToggle")
		},
		{
			InteriorControlsManager.ControlType.Damper,
			new ActionPair("DraftIncremental", "DraftToggle")
		},
		{
			InteriorControlsManager.ControlType.Blowdown,
			new ActionPair("BlowdownIncremental", "BlowdownToggle")
		},
		{
			InteriorControlsManager.ControlType.Bell,
			new ActionPair("BellIncremental", "BellToggle")
		}
	};

	private static readonly KeyValuePair<string, InteriorControlsManager.ControlType>[] FallbackPorts = new KeyValuePair<string, InteriorControlsManager.ControlType>[]
	{
		new KeyValuePair<string, InteriorControlsManager.ControlType>("throttle.EXT_IN", InteriorControlsManager.ControlType.Throttle),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("reverser.CONTROL_EXT_IN", InteriorControlsManager.ControlType.Reverser),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("brake.EXT_IN", InteriorControlsManager.ControlType.TrainBrake),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("indBrake.EXT_IN", InteriorControlsManager.ControlType.IndBrake),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("brakeCutout.EXT_IN", InteriorControlsManager.ControlType.TrainBrakeCutout),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("dynamicBrake.EXT_IN", InteriorControlsManager.ControlType.DynamicBrake),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("sanders.EXT_IN", InteriorControlsManager.ControlType.Sander),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("sander.CONTROL_EXT_IN", InteriorControlsManager.ControlType.Sander),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("horn.EXT_IN", InteriorControlsManager.ControlType.Horn),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("whistle.EXT_IN", InteriorControlsManager.ControlType.Horn),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("cylinderCock.EXT_IN", InteriorControlsManager.ControlType.CylCock),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("injector.EXT_IN", InteriorControlsManager.ControlType.Injector),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("fireboxDoor.EXT_IN", InteriorControlsManager.ControlType.Firedoor),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("blower.EXT_IN", InteriorControlsManager.ControlType.Blower),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("damper.EXT_IN", InteriorControlsManager.ControlType.Damper),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("blowdown.EXT_IN", InteriorControlsManager.ControlType.Blowdown),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("coalDumpControl.EXT_IN", InteriorControlsManager.ControlType.CoalDump),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("lubricatorControl.EXT_IN", InteriorControlsManager.ControlType.Lubricator),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("dynamoControl.EXT_IN", InteriorControlsManager.ControlType.Dynamo),
		new KeyValuePair<string, InteriorControlsManager.ControlType>("compressorControl.EXT_IN", InteriorControlsManager.ControlType.AirPump)
	};

	private static readonly HashSet<int> ProcessedPrefabs = new HashSet<int>();

	private static readonly HashSet<string> ReportedLiveries = new HashSet<string>();

	internal static void ResetCaches()
	{
		ProcessedPrefabs.Clear();
		ReportedLiveries.Clear();
	}

	public static bool IsCclLivery(TrainCarLivery livery)
	{
		if (livery == null)
		{
			return false;
		}
		return livery.GetType().Namespace?.StartsWith("CCL", StringComparison.Ordinal) ?? false;
	}

	public static void EnsureFixed(TrainCar car)
	{
		if (Main.Enabled && !(car == null) && IsCclLivery(car.carLivery))
		{
			TrainCarLivery carLivery = car.carLivery;
			Process(carLivery, carLivery.interiorPrefab);
			Process(carLivery, carLivery.explodedInteriorPrefab);
			Process(carLivery, carLivery.externalInteractablesPrefab);
			Process(carLivery, carLivery.explodedExternalInteractablesPrefab);
		}
	}

	private static void Process(TrainCarLivery livery, GameObject prefab)
	{
		if (prefab == null || ProcessedPrefabs.Contains(prefab.GetInstanceID()))
		{
			return;
		}
		try
		{
			Dictionary<GameObject, InteriorControlsManager.ControlType> dictionary = FindControls(livery, prefab);
			// The keyboard and F4 must identify the same existing controls. Repair
			// null reader slots from exact semantic port matches, never object names.
			var reader = prefab.GetComponent<LocoControlsReader>();
			foreach (var control in dictionary)
			{
				var field = typeof(LocoControlsReader).GetFields().FirstOrDefault(f =>
				    f.FieldType == typeof(GameObject) && string.Equals(f.Name,
				    control.Value.ToString(), StringComparison.OrdinalIgnoreCase));
				if (field == null) continue;
				if (reader == null) reader = prefab.AddComponent<LocoControlsReader>();
				if (field.GetValue(reader) == null) { field.SetValue(reader, control.Key); Main.MarkMutation(); }
			}
			List<AKeyboardInput> list = new List<AKeyboardInput>();
			List<string> list2 = new List<string>();
			// Keep authored toggle semantics across the shared port. Each locally
			// missing action still needs a candidate for cab/reach transfer;
			// ConnectedValueKeyboardInput arbitrates actual dispatch at runtime.
			var portActions = CollectPortActions(prefab);
			foreach (KeyValuePair<GameObject, InteriorControlsManager.ControlType> item in dictionary)
			{
				GameObject key = item.Key;
				string port = key.GetComponent<InteractablePortFeeder>()?.portId;
				var lever = key.GetComponent<Lever>();
				if (lever != null && Mathf.Abs(lever.scrollWheelHoverScroll) < 0.1f &&
				    key.GetComponent<SmallScrollConnection>() == null)
				    key.AddComponent<SmallScrollConnection>();
				if (!Actions.TryGetValue(item.Value, out var value)) continue;
				bool missingIncremental = !HasAction(key, value.Incremental);
				bool missingToggle = value.Toggle != null && !HasAction(key, value.Toggle);
				bool missingSharedToggle = missingToggle && !HasPortAction(portActions, port, value.Toggle);
				// An on/off switch with a toggle action on either shared-port
				// representation must not also receive an incremental axis action.
				missingIncremental = KeyPlan.AddIncremental(missingIncremental, key.GetComponent<ToggleSwitch>() != null, value.Toggle != null, missingSharedToggle);
				if (missingIncremental || missingToggle)
				{
					var input = key.AddComponent<ConnectedValueKeyboardInput>();
					Main.MarkMutation();
					input.sharedPortId = port;
					if (!string.IsNullOrEmpty(port))
						input.existingSharedInputs = prefab.GetComponentsInChildren<AKeyboardInput>(true)
							.Where(e => !(e is ConnectedValueKeyboardInput) &&
								e.GetComponent<InteractablePortFeeder>()?.portId == port).ToArray();
					input.incremental = !missingIncremental ? null : new AKeyboardInput.ActionReference
					{
						name = value.Incremental,
						flip = false
					};
					list.Add(input);
					if (missingToggle)
					{
						input.toggle = new AKeyboardInput.ActionReference
						{
							name = value.Toggle,
							flip = false
						};
					}
					list2.Add(string.Concat(item.Value, " (", key.name, ")"));
				}
			}
			// Reconcile the scheduler with actual components. A pre-existing key
			// listener can also be missing from the author's serialized entries.
			var discovered = prefab.GetComponentsInChildren<AKeyboardInput>(true);
			if (discovered.Length == 0) { ProcessedPrefabs.Add(prefab.GetInstanceID()); return; }
			InteractablesKeyboardControl interactablesKeyboardControl = prefab.GetComponent<InteractablesKeyboardControl>();
			if (interactablesKeyboardControl == null)
			{
				interactablesKeyboardControl = prefab.AddComponent<InteractablesKeyboardControl>();
			}
			List<AKeyboardInput> list3 = new List<AKeyboardInput>();
			if (interactablesKeyboardControl.entries != null)
			{
				list3.AddRange(interactablesKeyboardControl.entries.Where((AKeyboardInput e) => e != null));
			}
			list3.AddRange(discovered);
			interactablesKeyboardControl.entries = list3.Distinct().ToArray();
			Main.MarkMutation();
			ProcessedPrefabs.Add(prefab.GetInstanceID());
			if (list2.Count > 0) Log(string.Format("{0}: added keyboard input to {1} control(s) on '{2}': {3}", livery.id, list2.Count, prefab.name, string.Join(", ", list2.ToArray())));
		}
		catch (Exception ex)
		{
			if (Main.Logger != null)
			{
				Main.Logger.Error("[DVCCLControlFix] keyboard fix failed on " + prefab.name + ": " + ex);
			}
		}
	}

	private static bool HasAction(GameObject target, string name)
	{
		if (name == null) return true;
		foreach (var input in target.GetComponents<AKeyboardInput>())
			foreach (var field in input.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
				if (field.FieldType == typeof(AKeyboardInput.ActionReference) &&
				    (field.GetValue(input) as AKeyboardInput.ActionReference)?.name == name) return true;
		return false;
	}

	private static HashSet<string> CollectPortActions(GameObject prefab)
	{
		var actions = new HashSet<string>(StringComparer.Ordinal);
		foreach (var feeder in prefab.GetComponentsInChildren<InteractablePortFeeder>(true))
		{
			if (feeder == null || string.IsNullOrEmpty(feeder.portId)) continue;
			foreach (var input in feeder.GetComponents<AKeyboardInput>())
			{
				if (input is ConnectedValueKeyboardInput) continue;
				foreach (var field in input.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
					if (field.FieldType == typeof(AKeyboardInput.ActionReference) &&
						field.GetValue(input) is AKeyboardInput.ActionReference action && !string.IsNullOrEmpty(action.name))
						AddPortAction(actions, feeder.portId, action.name);
			}
		}
		return actions;
	}

	private static bool HasPortAction(HashSet<string> actions, string port, string action)
		=> !string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(action) && actions.Contains(port + "\0" + action);

	private static void AddPortAction(HashSet<string> actions, string port, string action)
	{
		if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(action)) actions.Add(port + "\0" + action);
	}

	private static Dictionary<GameObject, InteriorControlsManager.ControlType> FindControls(TrainCarLivery livery, GameObject prefab)
	{
		Dictionary<GameObject, InteriorControlsManager.ControlType> dictionary = new Dictionary<GameObject, InteriorControlsManager.ControlType>();
		InteractablePortFeedersController component = prefab.GetComponent<InteractablePortFeedersController>();
		if (component != null && component.entries != null)
		{
			Dictionary<string, InteriorControlsManager.ControlType> dictionary2 = BuildPortMap(livery);
			InteractablePortFeeder[] entries = component.entries;
			foreach (InteractablePortFeeder interactablePortFeeder in entries)
			{
				if (interactablePortFeeder != null && interactablePortFeeder.portId != null && dictionary2.TryGetValue(interactablePortFeeder.portId, out var value) && !dictionary.ContainsKey(interactablePortFeeder.gameObject))
				{
					dictionary[interactablePortFeeder.gameObject] = value;
				}
			}
		}
		LocoControlsReader component2 = prefab.GetComponent<LocoControlsReader>();
		if (component2 != null)
		{
			FieldInfo[] fields = typeof(LocoControlsReader).GetFields();
			foreach (FieldInfo fieldInfo in fields)
			{
				if (!(fieldInfo.FieldType != typeof(GameObject)))
				{
					GameObject gameObject = fieldInfo.GetValue(component2) as GameObject;
					if (!(gameObject == null) && !dictionary.ContainsKey(gameObject) && TryParseControlType(fieldInfo.Name, out var type))
					{
						dictionary[gameObject] = type;
					}
				}
			}
		}
		HandbrakeFeedersController component3 = prefab.GetComponent<HandbrakeFeedersController>();
		if (component3 != null && component3.entries != null && component3.entries.Length > 0 && component3.entries[0] != null && !dictionary.ContainsKey(component3.entries[0].gameObject))
		{
			dictionary[component3.entries[0].gameObject] = InteriorControlsManager.ControlType.Handbrake;
		}
		return dictionary;
	}

	private static Dictionary<string, InteriorControlsManager.ControlType> BuildPortMap(TrainCarLivery livery)
	{
		Dictionary<string, InteriorControlsManager.ControlType> dictionary = new Dictionary<string, InteriorControlsManager.ControlType>();
		BaseControlsOverrider baseControlsOverrider = ((livery.prefab != null) ? livery.prefab.GetComponentInChildren<BaseControlsOverrider>(includeInactive: true) : null);
		if (baseControlsOverrider != null)
		{
			AddPort(dictionary, baseControlsOverrider.Throttle, InteriorControlsManager.ControlType.Throttle);
			AddPort(dictionary, baseControlsOverrider.Brake, InteriorControlsManager.ControlType.TrainBrake);
			AddPort(dictionary, baseControlsOverrider.IndependentBrake, InteriorControlsManager.ControlType.IndBrake);
			AddPort(dictionary, baseControlsOverrider.DynamicBrake, InteriorControlsManager.ControlType.DynamicBrake);
			AddPort(dictionary, baseControlsOverrider.Reverser, InteriorControlsManager.ControlType.Reverser);
			AddPort(dictionary, baseControlsOverrider.Sander, InteriorControlsManager.ControlType.Sander);
			AddPort(dictionary, baseControlsOverrider.Horn, InteriorControlsManager.ControlType.Horn);
			AddPort(dictionary, baseControlsOverrider.HeadlightsFront, InteriorControlsManager.ControlType.HeadlightsFront);
			AddPort(dictionary, baseControlsOverrider.HeadlightsRear, InteriorControlsManager.ControlType.HeadlightsRear);
			AddPort(dictionary, baseControlsOverrider.BrakeCutout, InteriorControlsManager.ControlType.TrainBrakeCutout);
			AddPort(dictionary, baseControlsOverrider.CabLight, InteriorControlsManager.ControlType.CabLight);
			AddPort(dictionary, baseControlsOverrider.Wipers, InteriorControlsManager.ControlType.Wipers);
			AddPort(dictionary, baseControlsOverrider.Dynamo, InteriorControlsManager.ControlType.Dynamo);
			AddPort(dictionary, baseControlsOverrider.AirPump, InteriorControlsManager.ControlType.AirPump);
		}
		HashSet<InteriorControlsManager.ControlType> hashSet = new HashSet<InteriorControlsManager.ControlType>(dictionary.Values);
		KeyValuePair<string, InteriorControlsManager.ControlType>[] fallbackPorts = FallbackPorts;
		for (int i = 0; i < fallbackPorts.Length; i++)
		{
			KeyValuePair<string, InteriorControlsManager.ControlType> keyValuePair = fallbackPorts[i];
			if (!hashSet.Contains(keyValuePair.Value) && !dictionary.ContainsKey(keyValuePair.Key))
			{
				dictionary[keyValuePair.Key] = keyValuePair.Value;
			}
		}
		return dictionary;
	}

	private static void AddPort(Dictionary<string, InteriorControlsManager.ControlType> map, OverridableBaseControl control, InteriorControlsManager.ControlType type)
	{
		if (control != null && !string.IsNullOrEmpty(control.portId))
		{
			map[control.portId] = type;
		}
	}

	private static bool TryParseControlType(string name, out InteriorControlsManager.ControlType type)
	{
		try
		{
			type = (InteriorControlsManager.ControlType)Enum.Parse(typeof(InteriorControlsManager.ControlType), name, ignoreCase: true);
			return true;
		}
		catch (ArgumentException)
		{
			type = InteriorControlsManager.ControlType.None;
			return false;
		}
	}

	public static void ReportWhenReady(TrainCar car)
	{
		if (!Main.Enabled || car == null || !IsCclLivery(car.carLivery) || car.loadedInterior == null || ReportedLiveries.Contains(car.carLivery.id))
		{
			return;
		}
		InteriorControlsManager component = car.loadedInterior.GetComponent<InteriorControlsManager>();
		if (component == null)
		{
			Log(car.carLivery.id + ": interior has no InteriorControlsManager - F4/keyboard cannot work");
			return;
		}
		ReportedLiveries.Add(car.carLivery.id);
		string id = car.carLivery.id;
		Action<InteriorControlsManager> audit = (InteriorControlsManager m) =>
		{
			if (!Main.Enabled) return;
			try
			{
				IDictionary dictionary = AccessTools.Field(typeof(InteriorControlsManager), "controls").GetValue(m) as IDictionary;
				List<string> list = new List<string>();
				if (dictionary != null)
				{
					foreach (object key in dictionary.Keys)
					{
						var type = (InteriorControlsManager.ControlType)key;
						if (m.TryGetControl(type, out var reference))
						{
							var impl = reference.controlImplBase;
							list.Add(type + "[cab=" + (impl != null) + ", scroll=" +
							    (reference.scrollable != null) + ", keys=" +
							    (impl != null ? impl.GetComponents<AKeyboardInput>().Length : 0) + "]");
						}
					}
				}
				list.Sort();
				Log(string.Format("{0}: registered {1} control(s), connection audit: {2}", id, list.Count, string.Join(", ", list.ToArray())));
			}
			catch (Exception ex)
			{
				Log(id + ": could not read registered controls: " + ex.Message);
			}
		};
		component.OnInitialized += audit;
		Main.TrackAudit(component, audit);
	}

	private static void Log(string msg)
	{
		if (Main.Logger != null)
		{
			Main.Logger.Log("[DVCCLControlFix] " + msg);
		}
	}
}
[HarmonyPatch(typeof(TrainCar), "LoadInterior")]
internal static class TrainCar_LoadInterior_Patch
{
	private static void Prefix(TrainCar __instance)
	{
		KeyboardInputFixer.EnsureFixed(__instance);
	}

	private static void Postfix(TrainCar __instance)
	{
		KeyboardInputFixer.ReportWhenReady(__instance);
	}
}
[HarmonyPatch(typeof(TrainCar), "LoadExternalInteractables")]
internal static class TrainCar_LoadExternalInteractables_Patch
{
	private static void Prefix(TrainCar __instance)
	{
		KeyboardInputFixer.EnsureFixed(__instance);
	}
}
