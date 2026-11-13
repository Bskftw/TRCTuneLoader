// The real CarX connection: the player's car, reading, describing and applying tunes, the dyno, and tune numbers.
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
	/// <summary>
	/// The real CarX connection.
	///
	/// It only touches the player's own car, only through CarX's own public functions (the same
	/// ones CarX's Setups screen and dyno use), applies tunes only in the garage, and never sends
	/// anything online. CarX itself shares the setup with other players when they join a room.
	/// Which CarX classes are involved and why: docs/carx-tuning-notes.md.
	///
	/// Every call into the game is wrapped, so if a CarX update changes something the mod
	/// relies on, the menu shows a message instead of breaking.
	/// </summary>
	public sealed partial class CarXTuneAdapter : ICarTuneAdapter {
		private const string DiffTypeKey = "diff_type";
		private const int DrivetrainParameterId = 63;
		private const int SequentialGearboxParameterId = 65;

		private static readonly Type DiffTypeEnum = Type.GetType("CarX.DiffType, CarX.Plugins.Core");

		/// <summary>Live values recorded right after CarX put the official tune on each car ("car:slot"), for this session.</summary>
		private readonly Dictionary<string, DescSnapshot> references_ = new Dictionary<string, DescSnapshot>(StringComparer.Ordinal);

		public bool IsReady => true;
		public string Status => "Connected to CarX.";

		public bool TryGetCurrentCar(out CarInfo car, out string error) {
			car = null;
			try {
				RaceCar raceCar = FindPlayerCar(out bool inGarage);
				if (raceCar == null) {
					error = "No car found. Open the garage or start driving.";
					return false;
				}

				PrefsCarSlotsModel settings = Prefs.carSettings;
				int carId = raceCar.metaInfo != null ? raceCar.metaInfo.id : raceCar.carId;
				int slotId = settings.GetProfileIdForCar(carId);
				PlayerCarProfile slot = Db.GetById<PlayerCarProfile>(slotId);

				UIDynostandContext dyno = FindOpenDyno();
				car = new CarInfo {
					CarId = carId,
					CarName = raceCar.metaInfo?.name ?? $"car {carId}",
					SlotId = slotId,
					SlotName = slot?.name ?? "",
					StockEngine = raceCar.swap.isStockEngine && settings.GetEngineIdForCar(carId) <= 0,
					CustomSetup = settings.GetInstalledCustomProfile(carId) != null,
					InGarage = inGarage,
					InDyno = dyno != null && dyno.carId == carId,
				};
				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public bool TryReadCurrentTune(CarInfo car, out Dictionary<string, float> values, out string error) {
			values = new Dictionary<string, float>(StringComparer.Ordinal);
			try {
				PrefsCarSlotsModel settings = Prefs.carSettings;
				PlayerCarProfile slot = Db.GetById<PlayerCarProfile>(car.SlotId);
				if (slot == null) {
					error = $"CarX doesn't know setup {car.SlotId}.";
					return false;
				}

				// The same tune CarX applies to the player's car (CarVisualManager.ApplyProfile).
				CustomProfile custom = settings.GetInstalledCustomProfile(car.CarId);
				ProfilesInfo.Profile profile = settings.GetProfileInfo(car.SlotId, car.CarId, custom?.id ?? "");

				foreach (CarProfileController controller in slot.dynoInfo.controllers) {
					if (controller?.changers == null || controller.changers.Count == 0) {
						continue;
					}

					if (profile != null && profile.controllers.TryGetValue(controller.name, out float value)) {
						values[controller.name] = value;
					} else if (controller.changers.Count == 1) {
						values[controller.name] = DefaultOf(controller);
					}
				}

				// For the player's own car (no custom setup, stock engine), CarX takes engine and
				// gearbox values from the saved engine record instead (ApplyAdvancedProfile,
				// updateFromCarSpecsTable), so read them from there too.
				if (custom == null && slot.CanSwap() && car.StockEngine) {
					EngineSwapData engine = settings.GetEngineSwapDataForCar(car.CarId, 0);
					if (engine != null && engine.id <= 0 && engine.controllers != null) {
						foreach (EngineSwapData.SwapController saved in engine.controllers) {
							if (values.ContainsKey(saved.key)) {
								values[saved.key] = saved.value;
							}
						}
					}
				}

				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public bool TryGetDefaults(int slotId, out Dictionary<string, float> defaults, out string error) {
			defaults = new Dictionary<string, float>(StringComparer.Ordinal);
			try {
				PlayerCarProfile slot = Db.GetById<PlayerCarProfile>(slotId);
				if (slot == null) {
					error = $"CarX doesn't know setup {slotId}.";
					return false;
				}

				foreach (CarProfileController controller in slot.dynoInfo.controllers) {
					if (controller?.changers != null && controller.changers.Count == 1) {
						defaults[controller.name] = DefaultOf(controller);
					}
				}

				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public bool TryDescribe(int slotId, IDictionary<string, float> values, out Dictionary<string, string> text, out string error) {
			text = new Dictionary<string, string>(StringComparer.Ordinal);
			try {
				PlayerCarProfile slot = Db.GetById<PlayerCarProfile>(slotId);
				if (slot == null) {
					error = $"CarX doesn't know setup {slotId}.";
					return false;
				}

				foreach (CarProfileController controller in slot.dynoInfo.controllers) {
					if (controller?.changers != null && controller.changers.Count > 0 &&
						values.TryGetValue(controller.name, out float phase)) {
						text[controller.name] = Describe(controller, phase);
					}
				}

				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public bool TryGetCarName(int carId, out string name) {
			try {
				name = Db.GetById<PlayerCar>(carId)?.name ?? "";
				return name.Length > 0;
			} catch (Exception e) {
				TrcLog.Once("car name", $"Couldn't read a car's name from CarX ({e.Message}); said once.");
				name = "";
				return false;
			}
		}

		public bool CanApply(CarInfo car, out string reason) {
			try {
				if (car == null) {
					reason = "No car found. Open the garage first.";
				} else if (!car.InGarage) {
					reason = "On track or in a room, open CarX's tuning screen (dyno) and load the tune there. Checking works anywhere.";
				} else if (NetworkController.IsActive) {
					reason = "Leave the online room first.";
				} else if (GarageScreenProblem() is string screen) {
					reason = screen;
				} else if (car.CustomSetup) {
					reason = $"One of your own named setups is installed. Switch back to the normal {car.SlotName} setup in CarX's Setups screen first.";
				} else if (!car.StockEngine) {
					reason = "An engine swap is installed. TRC tunes are for the stock engine.";
				} else {
					reason = "";
					return true;
				}
				return false;
			} catch (Exception e) {
				return Failed(e, out reason);
			}
		}

		public bool TryApplyTune(CarInfo car, TuneFile tune, out string error) {
			if (!CanApply(car, out error)) {
				return false;
			}

			if (!tune.IsFor(car.CarId, car.SlotId)) {
				error = $"This tune is for car {Or(tune.CarId)}, setup {Or(tune.SlotId)} ({Or(tune.Slot)}). You're in {car.Describe()}, car {car.CarId}, setup {car.SlotId}.";
				return false;
			}

			if (tune.EngineId.Length > 0 && tune.EngineId != "0") {
				error = "This tune is for an engine swap. TRC tunes are for the stock engine.";
				return false;
			}

			try {
				RaceCar raceCar = FindPlayerCar(out bool inGarage);
				int carId = raceCar == null ? -1 : (raceCar.metaInfo != null ? raceCar.metaInfo.id : raceCar.carId);
				if (!inGarage || carId != car.CarId) {
					error = "The garage car changed. Try again.";
					return false;
				}

				PrefsCarSlotsModel settings = Prefs.carSettings;
				PlayerCarProfile slot = Db.GetById<PlayerCarProfile>(car.SlotId);

				// 1. Save the tune into the car's setup slot, like the dyno does when you keep changes.
				//    Settings the tune doesn't list fall back to the car's defaults, as in CarX.
				ProfilesInfo.Profile profile = settings.GetProfileInfo(car.SlotId, car.CarId).Copy();
				profile.controllers = new Dictionary<string, float>(tune.Values, StringComparer.Ordinal);
				settings.SetProfileInfo(car.CarId, car.SlotId, profile);

				// 2. Save the engine and gearbox values into the engine record, with the dyno's own helper.
				if (slot.CanSwap()) {
					var engineSaver = new CustomSetupSaveHelper();
					engineSaver.InitHelper(raceCar, slot, profile, car.SlotId, car.CarId, swapApplied: false);
					engineSaver.SaveSwapIfNeeded();
				}

				// 3. Put the saved setup on the car the way the garage does, and save to disk.
				CarProfileCacheManager.ClearCarProfile(car.SlotId);
				PutSavedSetupOnGarageCar(raceCar, car.SlotId, profile);
				BasePrefsModel.SaveAllQueries(inOneFrame: false);

				// 4. What CarX just put on the car is what the live check expects from now on.
				RecordReference(car, raceCar, tune.Fingerprint);

				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public bool TryLoadIntoDyno(CarInfo car, TuneFile tune, out string error) {
			try {
				UIDynostandContext dyno = FindOpenDyno();
				if (dyno?.userCar == null || dyno.profileInfoPreview == null) {
					error = "Open CarX's tuning screen (dyno) first.";
					return false;
				}
				if (dyno.carId != car.CarId || dyno.profileId != car.SlotId) {
					error = "CarX's tuning screen is showing a different car or setup.";
					return false;
				}
				if (!tune.IsFor(car.CarId, car.SlotId)) {
					error = $"This tune is for car {Or(tune.CarId)}, setup {Or(tune.SlotId)}. You're in {car.Describe()}, car {car.CarId}, setup {car.SlotId}.";
					return false;
				}
				if (car.CustomSetup) {
					error = $"One of your own named setups is installed. Switch back to the normal {car.SlotName} setup first.";
					return false;
				}
				if (!car.StockEngine || (tune.EngineId.Length > 0 && tune.EngineId != "0")) {
					error = "TRC tunes are for the stock engine.";
					return false;
				}

				// Exactly what moving every slider by hand would do: change the dyno's preview, rebuild its
				// slider list from it, and let CarX put it on the car. Saving stays CarX's job.
				dyno.profileInfoPreview.controllers = new Dictionary<string, float>(tune.Values, StringComparer.Ordinal);
				if (dyno.currentCategory != CarProfileControllerType.None) {
					DynoFillLists?.Invoke(dyno, new object[] { dyno.currentCategory });
					DynoUpdateToeCamber?.Invoke(dyno, null);
				}
				dyno.ApplyChanges();

				RecordReference(car, dyno.userCar, tune.Fingerprint);
				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public bool TryGetTuneNumbers(int slotId, IDictionary<string, float> values, out Dictionary<string, float> numbers, out Dictionary<string, int> decimals, out string error) {
			numbers = new Dictionary<string, float>(StringComparer.Ordinal);
			decimals = new Dictionary<string, int>(StringComparer.Ordinal);
			try {
				PlayerCarProfile slot = Db.GetById<PlayerCarProfile>(slotId);
				if (slot == null) {
					error = $"CarX doesn't know setup {slotId}.";
					return false;
				}
				foreach (CarProfileController controller in slot.dynoInfo.controllers) {
					if (controller?.changers == null || controller.changers.Count == 0 || !values.TryGetValue(controller.name, out float phase)) {
						continue;
					}
					CarProfileChanger changer = controller.changers[0];
					if (controller.isGroup || controller.isPercent) {
						numbers[controller.name] = phase * 100f;
						decimals[controller.name] = 0;
					} else if (changer.parameter != null && changer.parameter.isBool) {
						numbers[controller.name] = phase > 0.5f ? 1f : 0f;
						decimals[controller.name] = 0;
					} else {
						numbers[controller.name] = changer.EvaluateValue(phase) * changer.multiplier;
						decimals[controller.name] = DecimalsOf(changer.parameter?.format);
					}
				}
				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public string PlayerName {
			get {
				try {
					return BattleMessage.PlainName(Overlay.instance?.player?.nickName ?? "");
				} catch (Exception) {
					return "";
				}
			}
		}
	}
}
