// The stance check: the live wheel base against the stock body for the setup (widebody kits).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CarProfile;
using DB;
using DB.Meta;
using DI;
using GameOverlay;
using LocalCaching.Manager;
using PrefsModels;
using SyncMultiplayer;
using UnityEngine;
using ParameterType = Db.Meta.CarProfile.CarProfileParameterType;

namespace TRC.TuneLoader {
	public sealed partial class CarXTuneAdapter : ICarTuneAdapter {
		public bool TryCheckStance(CarInfo car, out List<string> differences, out string error) {
			differences = new List<string>();
			try {
				RaceCar raceCar = FindPlayerCar(out _);
				if (raceCar == null || CarIdOf(raceCar) != car.CarId) {
					error = "No car found.";
					return false;
				}

				differences = StanceDifferences(raceCar, car.SlotId, Prefs.carSettings.GetVisualForCar(car.CarId)?.parts);
				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		/// <summary>The wheel-base values body parts can change (CarPartCommand targets), in metres.</summary>
		private sealed class Stance {
			public float BaseY, FrontWidth, FrontZ, RearWidth, RearZ;

			public Stance(BaseCar.Desc desc) {
				BaseY = desc.baseY;
				FrontWidth = desc.frontBaseWidth;
				FrontZ = Math.Abs(desc.frontBaseZ);
				RearWidth = desc.rearBaseWidth;
				RearZ = Math.Abs(desc.rearBaseZ);
			}

			/// <summary>Changes of half a millimetre or more from the stock stance, as readable lines.</summary>
			public List<string> DifferencesFrom(Stance stock) {
				var differences = new List<string>();
				AddStanceDifference(differences, "Front track width", FrontWidth, stock.FrontWidth);
				AddStanceDifference(differences, "Rear track width", RearWidth, stock.RearWidth);
				AddStanceDifference(differences, "Front axle position", FrontZ, stock.FrontZ);
				AddStanceDifference(differences, "Rear axle position", RearZ, stock.RearZ);
				AddStanceDifference(differences, "Body ride height", BaseY, stock.BaseY);
				return differences;
			}

			/// <summary>What a body part's command does to these values (mirrors CarPartCommand.Apply).</summary>
			public void Apply(Customization.CarPartCommand command) {
				switch (command.target) {
					case Customization.CarPartCommand.CarPartCommandTarget.baseY: BaseY = Calculate(command, BaseY); break;
					case Customization.CarPartCommand.CarPartCommandTarget.frontBaseWidth: FrontWidth = Calculate(command, FrontWidth); break;
					case Customization.CarPartCommand.CarPartCommandTarget.frontBaseZ: FrontZ = Math.Abs(Calculate(command, FrontZ)); break;
					case Customization.CarPartCommand.CarPartCommandTarget.rearBaseWidth: RearWidth = Calculate(command, RearWidth); break;
					case Customization.CarPartCommand.CarPartCommandTarget.rearBaseZ: RearZ = Math.Abs(Calculate(command, RearZ)); break;
				}
			}

			public static bool MovesWheels(Customization.CarPartCommand command) {
				switch (command.target) {
					case Customization.CarPartCommand.CarPartCommandTarget.baseY:
					case Customization.CarPartCommand.CarPartCommandTarget.frontBaseWidth:
					case Customization.CarPartCommand.CarPartCommandTarget.frontBaseZ:
					case Customization.CarPartCommand.CarPartCommandTarget.rearBaseWidth:
					case Customization.CarPartCommand.CarPartCommandTarget.rearBaseZ:
						return true;
					default:
						return false;
				}
			}

			private static float Calculate(Customization.CarPartCommand command, float value) {
				switch (command.type) {
					case Customization.CarPartCommand.CarPartCommandType.Add: return value + command.valueF;
					case Customization.CarPartCommand.CarPartCommandType.Mul: return value * command.valueF;
					default: return command.valueF;
				}
			}
		}

		private static CarProfilesAsset profilesDb_;

		/// <summary>
		/// The car's live stance (as CarX built it, body parts included) against its stock body on this setup:
		/// CarX's base car data for the setup plus what the stock body parts do. Differences of half a
		/// millimetre or more are listed, with the installed parts that move the wheels.
		/// </summary>
		private static List<string> StanceDifferences(RaceCar raceCar, int slotId, ICollection<int> installedParts) {
			int carId = CarIdOf(raceCar);
			Stance stock = StockStance(carId, slotId);
			if (stock == null) {
				return new List<string>();
			}

			BaseCar.Desc live = raceCar.GetDesc();
			List<string> differences = new Stance(live).DifferencesFrom(stock);
			AddWheelHeights(differences, raceCar, live.baseY, stock.BaseY);
			if (differences.Count == 0) {
				return differences;
			}

			bool partFound = false;
			if (installedParts != null) {
				HashSet<int> defaultParts = DefaultParts(carId);
				foreach (var part in Customization.CarPartDatabase.ConvertToPartList(installedParts)) {
					if (part != null && !defaultParts.Contains(part.id) && part.cmds != null && part.cmds.Exists(Stance.MovesWheels)) {
						string name = Clean(string.IsNullOrEmpty(part.description) ? part.type : part.description);
						string workshop = part.external ? $", Workshop item {part.packetid}" : "";
						differences.Add($"Body kit part that moves the wheels: {name} ({part.type}{workshop})");
						partFound = true;
					}
				}
			}
			if (!partFound) {
				differences.Add("No body kit part on the car explains this, so it was changed outside CarX (KINO's air suspension or hydraulics, or another mod).");
			}
			return differences;
		}

		private static readonly (CarX.WheelIndex Index, string Name)[] Wheels = {
			(CarX.WheelIndex.FrontLeft, "front left"),
			(CarX.WheelIndex.FrontRight, "front right"),
			(CarX.WheelIndex.RearLeft, "rear left"),
			(CarX.WheelIndex.RearRight, "rear right"),
		};

		/// <summary>
		/// Each wheel's mounting height in the physics engine. CarX sets all four to the body's ride height (the
		/// description only reads it back from the rear left wheel), and body kits move all four together, which
		/// the body ride height line already covers. A wheel at another height than the rest, like KINO's air
		/// suspension or hydraulics can leave, is listed here, against the stock ride height.
		/// </summary>
		private static void AddWheelHeights(List<string> differences, RaceCar raceCar, float liveBaseY, float stockBaseY) {
			CarX.Car physics = raceCar.carX;
			if (physics == null) {
				return;
			}
			foreach (var (index, name) in Wheels) {
				float y = physics.GetWheel(index).topPos.y;
				float fromBody = (y - liveBaseY) * 1000f;
				float fromStock = (y - stockBaseY) * 1000f;
				if (Math.Abs(fromBody) >= 0.5f && Math.Abs(fromStock) >= 0.5f) {
					differences.Add($"Stance: ride height at the {name} wheel {(fromStock > 0 ? "+" : "")}{fromStock:0} mm ({y * 1000f:0} mm, stock {stockBaseY * 1000f:0} mm)");
				}
			}
		}

		/// <summary>The physics engine's own suspension values for each wheel, for the live check's reference.</summary>
		private static void AddWheelPhysics(IDictionary<string, object> values, RaceCar raceCar) {
			CarX.Car physics = raceCar.carX;
			if (physics == null) {
				return;
			}
			foreach (var (index, _) in Wheels) {
				CarX.Wheel wheel = physics.GetWheel(index);
				Vector3 top = wheel.topPos;
				string name = $"wheels.{index}";
				values[$"{name}.topPos.x"] = top.x;
				values[$"{name}.topPos.y"] = top.y;
				values[$"{name}.topPos.z"] = top.z;
				values[$"{name}.maxSpringLen"] = wheel.maxSpringLen;
				values[$"{name}.springCoef"] = wheel.springCoef;
			}
		}

		/// <summary>CarX's base car data for the setup plus what the stock body parts do to the wheel positions, or null.</summary>
		private static Stance StockStance(int carId, int slotId) {
			Stance stock = BaseStance(slotId);
			if (stock != null) {
				ApplyParts(stock, DefaultParts(carId));
			}
			return stock;
		}

		/// <summary>CarX's base car data for the setup, before any body part, or null.</summary>
		private static Stance BaseStance(int slotId) {
			PlayerCarProfile slot = Db.GetById<PlayerCarProfile>(slotId);
			if (slot == null) {
				return null;
			}

			if (profilesDb_ == null) {
				profilesDb_ = CarProfilesAsset.LoadDB();
			}
			BaseCar.Desc baseDesc = profilesDb_?.Find(slot.asset);
			return baseDesc == null ? null : new Stance(baseDesc);
		}

		/// <summary>Applies the body parts' commands, in the order CarX does (CarVisualManager.ApplyBodyParts).</summary>
		private static void ApplyParts(Stance stance, IEnumerable<int> partIds) {
			foreach (var part in Customization.CarPartDatabase.ConvertToPartList(partIds)) {
				if (part?.cmds != null) {
					foreach (var command in part.cmds) {
						stance.Apply(command);
					}
				}
			}
		}

		private static HashSet<int> DefaultParts(int carId) {
			var parts = new HashSet<int>();
			Customization.CarPartDatabase.EquipDefaults(carId, parts);
			return parts;
		}

		private static void AddStanceDifference(List<string> differences, string name, float live, float stock) {
			float deltaMm = (live - stock) * 1000f;
			if (Math.Abs(deltaMm) >= 0.5f) {
				differences.Add($"Stance: {name} {(deltaMm > 0 ? "+" : "")}{deltaMm:0} mm ({live * 1000f:0} mm, stock {stock * 1000f:0} mm)");
			}
		}
	}
}
