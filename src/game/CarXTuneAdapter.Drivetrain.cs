// The engine and drivetrain check: live engine, gearbox, suspension and weight against CarX's data plus the tune.
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
		public bool TryCheckDrivetrain(CarInfo car, IDictionary<string, float> tune, bool withPhysics, out DrivetrainCheck check, out string error) {
			check = null;
			try {
				RaceCar raceCar = FindPlayerCar(out _);
				if (raceCar == null || CarIdOf(raceCar) != car.CarId) {
					error = "No car found.";
					return false;
				}

				check = CheckDrivetrain(raceCar, car.SlotId, tune, withPhysics);
				if (car.InDyno && check.Compared) {
					// The car shows the tuning screen's sliders, not the saved setup.
					check.Compared = false;
					check.Differences.Clear();
					check.Note = "checked once you leave CarX's tuning screen";
				}
				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		private const string MaxTorqueKey = "Engine max torque";
		private const string MaxTorqueRpmKey = "Engine max torque RPM";
		private const string RevLimiterKey = "Rev limiter";
		private const string TurboKey = "Turbo";
		private const string TurboPressureKey = "Turbo pressure";
		private const string FinalDriveKey = "Final drive";
		private const string DrivetrainKey = "Drivetrain";
		private const string MassKey = "Weight";
		private const string WeightFrontKey = "Weight balance (front)";
		private const string WeightUpKey = "Centre of gravity height";
		private static string GearKey(int index) => $"Gear {index + 1} ratio";

		/// <summary>
		/// The car's live engine, gearbox and drivetrain against what CarX gives for the setup: its own data
		/// (CarProfilesAsset, as CarX's InitDesc loads it), then the tune's engine, gearbox and drivetrain
		/// settings on top, the way ApplyAdvancedProfile does for a stock engine. KINO's engine and gearbox
		/// swaps change these values without CarX's tuning, so they show up as differences. A CarX engine
		/// swap brings its own values, so those cars are only named. With <paramref name="withPhysics"/>,
		/// the physics engine's own values are listed next to the description for the log.
		/// </summary>
		private static DrivetrainCheck CheckDrivetrain(RaceCar raceCar, int slotId, IDictionary<string, float> tune, bool withPhysics) {
			CarX.CarDesc live = raceCar.GetDesc().carXDesc;
			var check = new DrivetrainCheck {
				EngineSwap = !raceCar.swap.isStockEngine,
				Drivetrain = DrivetrainName(live.transmission.gearType),
			};
			check.Engine = check.EngineSwap
				? $"CarX engine swap: {(string.IsNullOrEmpty(raceCar.swap.engineName) ? $"engine {raceCar.swap.engineId}" : Clean(raceCar.swap.engineName))}"
				: "Stock engine";
			if (withPhysics) {
				check.PhysicsLines = PhysicsLines(raceCar.carX, live);
			}
			try {
				// The power and weight CarX's own garage shows (max power, and its "power density").
				if (raceCar.carX != null) {
					raceCar.carX.CalcMaxPowerMaxTorque(out float hp, out _, out _, out _);
					check.Hp = hp;
				}
				check.Mass = live.weight.mass;
			} catch (Exception) {
				// Left unknown.
			}
			if (check.EngineSwap) {
				check.Note = "a CarX engine swap brings its own engine and gearbox values";
				return check;
			}

			PlayerCarProfile slot = Db.GetById<PlayerCarProfile>(slotId);
			if (profilesDb_ == null) {
				profilesDb_ = CarProfilesAsset.LoadDB();
			}
			BaseCar.Desc baseDesc = slot == null ? null : profilesDb_?.Find(slot.asset);
			if (baseDesc == null) {
				check.Note = "CarX's data for this setup wasn't found";
				return check;
			}

			CarX.CarDesc stock = baseDesc.carXDesc;
			DescSnapshot expected = DrivetrainSnapshot(stock);
			CarX.GearType expectedType = stock.transmission.gearType;
			int gears = stock.gearBox.gears?.Length ?? 0;

			foreach (CarProfileController controller in slot.dynoInfo.controllers) {
				if (controller.changers == null || controller.changers.Count == 0) {
					continue;
				}
				float value;
				if (tune == null || !tune.TryGetValue(controller.name, out value)) {
					if (controller.changers.Count != 1) {
						continue;
					}
					value = DefaultOf(controller);
				}

				foreach (CarProfileChanger changer in controller.changers) {
					var parameter = changer.parameter;
					if (parameter == null) {
						continue;
					}
					float real = changer.EvaluateOriginalValue(value);
					switch (parameter.type) {
						case ParameterType.MaxTorque: expected.Values[MaxTorqueKey] = real; break;
						case ParameterType.MaxTorqueRPM: expected.Values[MaxTorqueRpmKey] = real; break;
						case ParameterType.RevLimiter: expected.Values[RevLimiterKey] = real; break;
						case ParameterType.TurboCharged: expected.Values[TurboKey] = real > 0.5f; break;
						case ParameterType.TurboPressure: expected.Values[TurboPressureKey] = real; break;
						case ParameterType.FinalDrive: expected.Values[FinalDriveKey] = real; break;
						case ParameterType.Mass: expected.Values[MassKey] = real; break;
						case ParameterType.WeightFrontPercent: expected.Values[WeightFrontKey] = real; break;
						case ParameterType.WeightUpPercent: expected.Values[WeightUpKey] = real; break;
						case ParameterType.DrivetrainType:
							expectedType = DrivetrainSwitchDynoParamViewModel.GetDrivetrainType(Mathf.RoundToInt(real));
							expected.Values[DrivetrainKey] = DrivetrainName(expectedType);
							break;
						default:
							if (SuspensionKey(parameter.type) is string suspension) {
								expected.Values[suspension] = real;
								break;
							}
							int gear = (int)parameter.type - (int)ParameterType.Gear1;
							if (gear >= 0 && gear < 12 && gear < gears) {
								expected.Values[GearKey(gear)] = real;
							}
							break;
					}
				}
			}

			check.Differences = DrivetrainSnapshot(live).Differences(expected);
			check.StockDrivetrain = expectedType != stock.transmission.gearType ? DrivetrainName(stock.transmission.gearType) : "";
			check.Compared = true;
			return check;
		}

		/// <summary>The engine, gearbox and drivetrain values that a tune can't change except through the settings above.</summary>
		private static DescSnapshot DrivetrainSnapshot(CarX.CarDesc desc) {
			var snapshot = new DescSnapshot();
			var values = snapshot.Values;
			CarX.CarDesc.Engine engine = desc.engine;
			values[MaxTorqueKey] = engine.maxTorque;
			values[MaxTorqueRpmKey] = engine.maxTorqueRPM;
			values[RevLimiterKey] = engine.revLimiter;
			values[TurboKey] = engine.turboCharged;
			values[TurboPressureKey] = engine.turboPressure;
			values["Engine inertia"] = engine.inertiaRatio;
			values["Engine braking"] = engine.brakeTorqueRatio;
			values["Cut RPM"] = engine.cutRPM;
			values["Idle RPM"] = engine.idleRPM;

			var curve = engine.torqueCurveSamples ?? new CarX.CarDesc.TorqueCurveSample[0];
			values["Torque curve points"] = curve.Length;
			for (int i = 0; i < curve.Length; i++) {
				values[$"Torque curve point {i + 1} torque"] = curve[i]?.torque ?? 0f;
				values[$"Torque curve point {i + 1} RPM"] = curve[i]?.rpm ?? 0f;
			}

			float[] gears = desc.gearBox.gears ?? new float[0];
			values[FinalDriveKey] = desc.gearBox.finalDrive;
			values["Reverse gear ratio"] = desc.gearBox.gearR;
			values["Number of gears"] = gears.Length;
			for (int i = 0; i < gears.Length; i++) {
				values[GearKey(i)] = gears[i];
			}

			values[DrivetrainKey] = DrivetrainName(desc.transmission.gearType);
			values["Clutch max torque"] = desc.transmission.clutchMaxTorque;
			values["Transmission loss"] = desc.transmission.transmissionLossCoef;

			AddSuspension(values, "Front", desc.frontSuspension);
			AddSuspension(values, "Rear", desc.rearSuspension);

			// KINO's weight and distribution menu changes these (a tester lowered the centre of gravity and added 50 kg).
			CarX.CarDesc.Weight weight = desc.weight;
			values[MassKey] = weight.mass;
			values[WeightFrontKey] = weight.frontPercent;
			values[WeightUpKey] = weight.upPercent;
			values["Weight balance (right)"] = weight.rightPercent;
			values["Front balance from the wheels"] = weight.calcFrontCenterMassFromWheels;
			values["Side balance from the wheels"] = weight.calcRightCenterMassFromWheels;
			values["Body inertia"] = weight.bodyInertiaMultiplier;
			return snapshot;
		}

		/// <summary>
		/// The suspension values CarX sets from the tune (ApplyAdvancedProfile). KINO's air suspension and hydraulics
		/// change these (seen in game: front stiffness, fast bump, fast rebound and slow bump), so they're compared too.
		/// </summary>
		private static void AddSuspension(IDictionary<string, object> values, string axle, CarX.CarDesc.Suspension suspension) {
			values[$"{axle} spring travel"] = suspension.springLength;
			values[$"{axle} spring stiffness"] = suspension.stiffness;
			values[$"{axle} fast bump"] = suspension.fastBump;
			values[$"{axle} fast rebound"] = suspension.fastRebound;
			values[$"{axle} slow bump"] = suspension.slowBump;
			values[$"{axle} slow rebound"] = suspension.slowRebound;
		}

		/// <summary>The suspension value a tune setting sets, as named in <see cref="AddSuspension"/>, or null.</summary>
		private static string SuspensionKey(ParameterType type) {
			switch (type) {
				case ParameterType.FrontSpringWay: return "Front spring travel";
				case ParameterType.FrontSpringStiffness: return "Front spring stiffness";
				case ParameterType.FrontFastBump: return "Front fast bump";
				case ParameterType.FrontFastRebound: return "Front fast rebound";
				case ParameterType.FrontSlowBump: return "Front slow bump";
				case ParameterType.FrontSlowRebound: return "Front slow rebound";
				case ParameterType.RearSpringWay: return "Rear spring travel";
				case ParameterType.RearSpringStiffness: return "Rear spring stiffness";
				case ParameterType.RearFastBump: return "Rear fast bump";
				case ParameterType.RearFastRebound: return "Rear fast rebound";
				case ParameterType.RearSlowBump: return "Rear slow bump";
				case ParameterType.RearSlowRebound: return "Rear slow rebound";
				default: return null;
			}
		}

		private static string DrivetrainName(CarX.GearType type) {
			switch (type) {
				case CarX.GearType.GEAR_RWD: return "RWD";
				case CarX.GearType.GEAR_FWD: return "FWD";
				case CarX.GearType.GEAR_AWD: return "AWD";
				case CarX.GearType.GEAR_4WD: return "4WD";
				default: return type.ToString();
			}
		}

		/// <summary>
		/// What the physics engine itself runs with, next to the car's description ("physics / description"),
		/// for the log. In the 0.7 test these agreed on an untouched car and with KINO's swaps (KINO changes
		/// the description, which the check compares), so a mod changing only the physics would show here.
		/// The physics engine's clutch torque is a different quantity from the description's, so it isn't listed.
		/// </summary>
		private static List<string> PhysicsLines(CarX.Car physics, CarX.CarDesc desc) {
			var lines = new List<string>();
			if (physics == null) {
				return lines;
			}

			void Add(string name, Func<object> fromPhysics, object fromDesc) {
				try {
					lines.Add($"{name}: {Format(fromPhysics())} / {Format(fromDesc)}");
				} catch (Exception e) {
					lines.Add($"{name}: couldn't read ({e.GetType().Name})");
				}
			}

			Add("Drivetrain", () => DrivetrainName(physics.gearType), DrivetrainName(desc.transmission.gearType));
			Add("Max torque", () => physics.engineMaxTorque, desc.engine.maxTorque);
			Add("Max torque RPM", () => physics.engineRPMAtMaxTorque, desc.engine.maxTorqueRPM);
			Add("Rev limiter", () => physics.engineRevLimiter, desc.engine.revLimiter);
			Add("Engine inertia", () => physics.engineInertiaRatio, desc.engine.inertiaRatio);
			Add("Engine braking", () => physics.engineBrakeTorqueRatio, desc.engine.brakeTorqueRatio);
			Add("Cut RPM", () => physics.engineCutRPM, desc.engine.cutRPM);
			Add("Idle RPM", () => physics.engineIdleRPM, desc.engine.idleRPM);
			Add("Final drive", () => physics.finaldrive, desc.gearBox.finalDrive);
			float[] gears = desc.gearBox.gears ?? new float[0];
			Add("Number of gears", () => physics.numGears, gears.Length);
			for (int i = 0; i < gears.Length; i++) {
				int gear = i + 1;
				Add($"Gear {gear} ratio", () => physics.GetGearRatio(gear), gears[i]);
			}

			// CarX sets the body's mass and centre of mass from the description (Car.cs): logged to learn whether
			// KINO's weight menu changes the physics directly too.
			try {
				Rigidbody body = physics.getRigidbody;
				CarX.CarDesc.Weight weight = desc.weight;
				Vector3 centre = physics.CalcCenterMassLocalSpace(weight.rightPercent, weight.calcRightCenterMassFromWheels,
					weight.frontPercent, weight.calcFrontCenterMassFromWheels, weight.upPercent, weight.bounds);
				lines.Add($"Mass: {Format(body.mass)} / {Format(weight.mass)}");
				lines.Add($"Centre of mass: {Format(body.centerOfMass.x)}, {Format(body.centerOfMass.y)}, {Format(body.centerOfMass.z)} / " +
					$"{Format(centre.x)}, {Format(centre.y)}, {Format(centre.z)}");
			} catch (Exception e) {
				lines.Add($"Mass: couldn't read ({e.GetType().Name})");
			}

			// No description value to set these against: logged to learn what KINO's air suspension and hydraulics change.
			foreach (var (index, name) in Wheels) {
				try {
					CarX.Wheel wheel = physics.GetWheel(index);
					lines.Add($"Wheel {name}: top {Format(wheel.topPos.x)}, {Format(wheel.topPos.y)}, {Format(wheel.topPos.z)}; " +
						$"spring length {Format(wheel.maxSpringLen)}, spring {Format(wheel.springCoef)}, helper length {Format(wheel.maxHelperLength)}, helper {Format(wheel.helperCoef)}, radius {Format(wheel.radius)}");
				} catch (Exception e) {
					lines.Add($"Wheel {name}: couldn't read ({e.GetType().Name})");
				}
			}
			return lines;
		}

		private static string Format(object value) =>
			value is float f ? f.ToString("0.####", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);
	}
}
