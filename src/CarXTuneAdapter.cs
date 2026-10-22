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
	public sealed class CarXTuneAdapter : ICarTuneAdapter {
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
			} catch (Exception) {
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

		public bool TryRecordReference(CarInfo car, string tuneCode, out string error) {
			try {
				if (GarageScreenProblem() is string screen) {
					error = screen;
					return false;
				}

				RaceCar raceCar = FindPlayerCar(out bool inGarage);
				if (!inGarage || raceCar == null || CarIdOf(raceCar) != car.CarId) {
					error = "The live car can only be recorded in the garage.";
					return false;
				}

				// Have CarX put the saved setup back on the car, exactly as it does when you enter the garage,
				// so the recording is CarX's own result and not something changed since.
				ProfilesInfo.Profile profile = Prefs.carSettings.GetProfileInfo(car.SlotId, car.CarId);
				PutSavedSetupOnGarageCar(raceCar, car.SlotId, profile);
				RecordReference(car, raceCar, tuneCode);

				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public bool TryCheckLive(CarInfo car, string tuneCode, out List<string> differences, out string note) {
			differences = new List<string>();
			try {
				if (!references_.TryGetValue(ReferenceKey(car), out DescSnapshot reference) || reference.TuneCode != tuneCode) {
					note = "No live reference for this car yet: press Check once in the garage with it.";
					return false;
				}

				RaceCar raceCar = FindPlayerCar(out _);
				if (raceCar == null || CarIdOf(raceCar) != car.CarId) {
					note = "No car found.";
					return false;
				}

				DescSnapshot live = SnapshotOf(raceCar);
				differences = live.Differences(reference, UnusedDiffFields(live));
				note = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out note);
			}
		}

		public bool TryGetRoomPlayers(out List<RoomPlayer> players, out string error) {
			players = new List<RoomPlayer>();
			try {
				NetworkGame game = NetworkController.InstanceGame;
				if (!NetworkController.IsActive || game?.Players == null) {
					error = "Join an online room to see the other players.";
					return false;
				}

				foreach (NetworkPlayer player in game.Players.ToArray()) {
					SmartfoxPlayerProperties shared = player?.PlayerProperties;
					if (shared == null) {
						continue;
					}

					var info = new RoomPlayer {
						Name = string.IsNullOrEmpty(player.FilteredNickName) ? shared.Name ?? "" : player.FilteredNickName,
						SteamId = SteamIdOf(shared.PlayerID),
						IsLocal = player.IsLocal,
						CarId = shared.CarId,
						CarName = Db.GetById<PlayerCar>(shared.CarId)?.name ?? $"car {shared.CarId}",
						SlotId = shared.ProfileId,
						SlotName = Db.GetById<PlayerCarProfile>(shared.ProfileId)?.name ?? "",
						EngineId = shared.SwapId,
					};

					if (shared.Profile?.controllers != null) {
						foreach (var pair in shared.Profile.controllers) {
							info.SharedTune[pair.Key] = pair.Value;
						}
					}

					bool builtHere = player.userCar != null;
					if (builtHere) {
						info.StanceDifferences = StanceDifferences(player.userCar, shared.ProfileId, shared.VisualSettings?.parts);
					}

					info.Workshop = WorkshopItems(shared.CarId, shared.ProfileId, shared.VisualSettings?.parts, shared.VisualSettings?.networkparts, out var workshopStance);
					foreach (WorkshopItem item in info.Workshop) {
						if (builtHere && item.ShownHere) {
							// CarX built their car here with this body, so the live stance is what counts.
							item.MovesWheels &= info.StanceDifferences.Count > 0;
						}
					}
					if (info.StanceDifferences.Count == 0 && info.Workshop.Exists(w => w.MovesWheels)) {
						// The live car here doesn't have their Workshop body, so use the stance worked out from its parts.
						info.StanceDifferences = workshopStance.Select(d => $"{d}, worked out from their Workshop body's parts").ToList();
					}

					if (builtHere) {
						info.Drivetrain = CheckDrivetrain(player.userCar, shared.ProfileId, info.SharedTune, withPhysics: false);
					}

					players.Add(info);
				}

				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

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

		public bool TryGetWorkshopItems(CarInfo car, out List<WorkshopItem> items, out string error) {
			items = new List<WorkshopItem>();
			try {
				CarVisualManager.VisualSettings visual = Prefs.carSettings.GetVisualForCar(car.CarId);
				items = WorkshopItems(car.CarId, car.SlotId, visual?.parts, visual?.networkparts, out _);
				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public bool TryCheckDrivetrain(CarInfo car, IDictionary<string, float> tune, out DrivetrainCheck check, out string error) {
			check = null;
			try {
				RaceCar raceCar = FindPlayerCar(out _);
				if (raceCar == null || CarIdOf(raceCar) != car.CarId) {
					error = "No car found.";
					return false;
				}

				check = CheckDrivetrain(raceCar, car.SlotId, tune, withPhysics: true);
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

		/// <summary>
		/// Has CarX put a saved setup on the garage car, then lets the car settle on it. CarX applies a
		/// tune on the stock wheel positions and then lets body parts move them (widebody kits), so the
		/// car is dressed again afterwards, like CarX does when you return to the garage. The main garage
		/// screen holds the car still with physics off, so without the last step the car keeps its old
		/// stance until CarX's Setups or tuning screen wakes the physics up.
		/// </summary>
		private static void PutSavedSetupOnGarageCar(RaceCar raceCar, int slotId, ProfilesInfo.Profile profile) {
			DependencyInjector.Resolve<PlayerCarFactory>().visualManager.ApplyProfile(
				raceCar, slotId, null, resetCarDesc: true, isLocalPlayerCar: true, forceProfileApply: true);

			var view = UnityEngine.Object.FindAnyObjectByType<GarageCar3dViewContext>();
			view?.ApplyDefaultCustomization();
			raceCar.AlignCarPositionAtSpawn(adjustRotation: false);

			if (view != null) {
				view.ApplyWheelSizeUpdate(profile);
				// Physics on so the car settles, then off again after 3 seconds, as CarX's own screens do.
				view.SetGarageCarStatePhysAndHoldPosition(disablePhysics: false, holdPosition: false);
				view.SetGarageCarStatePhysAndHoldPosition(disablePhysics: true, holdPosition: false);
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

			List<string> differences = new Stance(raceCar.GetDesc()).DifferencesFrom(stock);
			if (differences.Count > 0 && installedParts != null) {
				HashSet<int> defaultParts = DefaultParts(carId);
				foreach (var part in Customization.CarPartDatabase.ConvertToPartList(installedParts)) {
					if (part != null && !defaultParts.Contains(part.id) && part.cmds != null && part.cmds.Exists(Stance.MovesWheels)) {
						string name = Clean(string.IsNullOrEmpty(part.description) ? part.type : part.description);
						string workshop = part.external ? $", Workshop item {part.packetid}" : "";
						differences.Add($"Body kit part that moves the wheels: {name} ({part.type}{workshop})");
					}
				}
			}

			return differences;
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

		/// <summary>
		/// The Workshop (or mod.io) items among a car's body parts. <paramref name="parts"/> are the parts CarX
		/// put on the car here (Workshop parts are "external"); <paramref name="networkParts"/> are another
		/// player's Workshop parts CarX doesn't show here (Workshop ID to part IDs), because this PC doesn't
		/// have the item or the option to show other players' Workshop cars is off. CarX then shows the car
		/// with its stock body, so the live stance check can't see them. When this PC has the item, the car's
		/// stance with all its parts is worked out from the parts' commands, the way CarX builds it
		/// (<paramref name="stance"/>: the changes from the stock stance).
		/// </summary>
		private static List<WorkshopItem> WorkshopItems(int carId, int slotId, ICollection<int> parts, IDictionary<ulong, HashSet<int>> networkParts, out List<string> stance) {
			var items = new List<WorkshopItem>();
			var partsOf = new Dictionary<WorkshopItem, List<Customization.CarPart>>();
			var allParts = parts != null ? new HashSet<int>(parts) : new HashSet<int>();

			WorkshopItem ItemFor(ulong id) {
				WorkshopItem found = items.Find(i => i.Id == id);
				if (found == null) {
					found = new WorkshopItem { Id = id };
					items.Add(found);
					partsOf[found] = new List<Customization.CarPart>();
				}
				return found;
			}

			if (parts != null) {
				foreach (var part in Customization.CarPartDatabase.ConvertToPartList(parts)) {
					if (part != null && part.external) {
						WorkshopItem item = ItemFor(part.packetid);
						item.Installed = true;
						item.ShownHere = true;
						item.Source = SourceName(part.ugcServiceProvider);
						partsOf[item].Add(part);
					}
				}
			}

			if (networkParts != null) {
				foreach (var pair in networkParts) {
					WorkshopItem item = ItemFor(pair.Key);
					if (!Customization.CarPartDatabase.externalDatabases.TryGetValue(pair.Key, out var database) || pair.Value == null) {
						continue;
					}
					foreach (int partId in pair.Value) {
						if (database.nativeParts.TryGetValue(partId, out var part) && part != null) {
							item.Installed = true;
							item.Source = SourceName(part.ugcServiceProvider);
							partsOf[item].Add(part);
							allParts.Add(part.id);
						}
					}
				}
			}

			stance = new List<string>();
			if (items.Count == 0) {
				return items;
			}

			// The car as CarX builds it with all these parts: base car data, then every part's commands.
			Stance stock = StockStance(carId, slotId);
			Stance built = BaseStance(slotId);
			if (stock != null && built != null) {
				ApplyParts(built, allParts.Count > 0 ? allParts : DefaultParts(carId));
				stance = built.DifferencesFrom(stock);
			}

			foreach (WorkshopItem item in items) {
				List<Customization.CarPart> itemParts = partsOf[item];
				item.Name = WorkshopTitle(item.Id, itemParts.Count > 0 ? itemParts[0].ugcServiceProvider : Services.UGC.Interfaces.IUGCManager.ServiceProvider.Steamworkshop);
				if (string.IsNullOrEmpty(item.Name)) {
					item.Name = Clean(string.Join(", ", itemParts.Select(p => p.description).Where(d => !string.IsNullOrEmpty(d)).Distinct()));
				}
				item.MovesWheels = stance.Count > 0 && itemParts.Exists(p => p.cmds != null && p.cmds.Exists(Stance.MovesWheels));
			}

			return items;
		}

		private static string SourceName(Services.UGC.Interfaces.IUGCManager.ServiceProvider provider) =>
			provider == Services.UGC.Interfaces.IUGCManager.ServiceProvider.ModIO ? "mod.io" : "Steam Workshop";

		/// <summary>The Workshop item's title when this PC is subscribed to it, else "".</summary>
		private static string WorkshopTitle(ulong id, Services.UGC.Interfaces.IUGCManager.ServiceProvider provider) {
			try {
				var manager = Services.UGC.Utils.UGCServiceProvider.instance.GetManagerBy(Services.UGC.Interfaces.IUGCManager.ContentType.Cars, provider);
				return Clean(manager?.TryGetInstalledItem(id)?.title);
			} catch (Exception) {
				return "";
			}
		}

		/// <summary>Text from Workshop authors, made safe to show: no rich-text tags, one line, at most 60 characters.</summary>
		private static string Clean(string text) {
			if (string.IsNullOrEmpty(text)) {
				return "";
			}
			string clean = new string(text.Where(c => c != '<' && c != '>' && !char.IsControl(c)).ToArray()).Trim();
			return clean.Length > 60 ? clean.Substring(0, 60) + "..." : clean;
		}

		private const string MaxTorqueKey = "Engine max torque";
		private const string MaxTorqueRpmKey = "Engine max torque RPM";
		private const string RevLimiterKey = "Rev limiter";
		private const string TurboKey = "Turbo";
		private const string TurboPressureKey = "Turbo pressure";
		private const string FinalDriveKey = "Final drive";
		private const string DrivetrainKey = "Drivetrain";
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
						case ParameterType.DrivetrainType:
							expectedType = DrivetrainSwitchDynoParamViewModel.GetDrivetrainType(Mathf.RoundToInt(real));
							expected.Values[DrivetrainKey] = DrivetrainName(expectedType);
							break;
						default:
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
			return snapshot;
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
			return lines;
		}

		private static string Format(object value) =>
			value is float f ? f.ToString("0.####", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);

		private static void AddStanceDifference(List<string> differences, string name, float live, float stock) {
			float deltaMm = (live - stock) * 1000f;
			if (Math.Abs(deltaMm) >= 0.5f) {
				differences.Add($"Body kit: {name} {(deltaMm > 0 ? "+" : "")}{deltaMm:0} mm ({live * 1000f:0} mm, stock {stock * 1000f:0} mm)");
			}
		}

		private void RecordReference(CarInfo car, RaceCar raceCar, string tuneCode) {
			DescSnapshot snapshot = SnapshotOf(raceCar);
			snapshot.TuneCode = tuneCode;
			references_[ReferenceKey(car)] = snapshot;
		}

		/// <summary>
		/// The diff a car's drivetrain doesn't drive (front on RWD, rear on FWD) has no effect on how it drives.
		/// CarX copies the rear diff's settings onto the front one when it applies a tune, and an unused diff's
		/// values can change on track without meaning anything (seen on an RWD AE86 after a dyno load in a room),
		/// so the live check leaves it out. AWD and 4WD cars keep both diffs checked.
		/// </summary>
		private static List<string> UnusedDiffFields(DescSnapshot live) {
			var ignored = new List<string>();
			if (live.Values.TryGetValue("carXDesc.transmission.gearType", out object gearType)) {
				switch (Convert.ToString(gearType, CultureInfo.InvariantCulture)) {
					case "GEAR_RWD": ignored.Add("carXDesc.transmission.frontDiff"); break;
					case "GEAR_FWD": ignored.Add("carXDesc.transmission.rearDiff"); break;
				}
			}
			return ignored;
		}

		private static DescSnapshot SnapshotOf(RaceCar raceCar) =>
			DescSnapshot.Of(raceCar.GetDesc(), new Dictionary<string, object> {
				["frontBaseOffset"] = raceCar.frontBaseOffset,
				["rearBaseOffset"] = raceCar.rearBaseOffset,
			});

		private static string ReferenceKey(CarInfo car) => $"{car.CarId}:{car.SlotId}";

		private static int CarIdOf(RaceCar raceCar) => raceCar.metaInfo != null ? raceCar.metaInfo.id : raceCar.carId;

		private static ulong SteamIdOf(PlayerId id) {
			if (id.platform != UserPlatform.Id.Steam) {
				return 0;
			}

			// PlayerId prints as "<account id>|<platform>"; for Steam the account ID is the SteamID64.
			string text = id.ToString();
			int bar = text.IndexOf('|');
			return ulong.TryParse(bar >= 0 ? text.Substring(0, bar) : text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId)
				? steamId
				: 0;
		}

		// The tuning screen's own routines that rebuild its slider list from the preview (private in CarX).
		private static readonly MethodInfo DynoFillLists =
			typeof(UIDynostandContext).GetMethod("FillParamsListsByCategory", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly MethodInfo DynoUpdateToeCamber =
			typeof(UIDynostandContext).GetMethod("UpdateToeCamberTopAndBottomLists", BindingFlags.Instance | BindingFlags.NonPublic);

		/// <summary>CarX's tuning screen if it's open (in the garage or in an online room), else null.</summary>
		private static UIDynostandContext FindOpenDyno() {
			var dyno = UnityEngine.Object.FindAnyObjectByType<UIDynostandContext>();
			return dyno != null && dyno.isActive ? dyno : null;
		}

		private static GamePrefs Prefs => DependencyInjector.Resolve<GamePrefs>();
		private static BaseModel Db => DependencyInjector.Resolve<BaseModel>();

		/// <summary>The player's car: the one on show in the garage, or the one they're driving.</summary>
		private static RaceCar FindPlayerCar(out bool inGarage) {
			inGarage = false;
			BaseState main = States.mainCurrent;

			if (main is GarageGUIState) {
				inGarage = true;
				var view = UnityEngine.Object.FindAnyObjectByType<GarageCar3dViewContext>();
				return view != null ? view.currentCar : null;
			}

			if (main is BaseRaceModeState race && race.userCar != null && !race.userCar.isNetworkCar) {
				return race.userCar;
			}

			return null;
		}

		/// <summary>
		/// Null on the main garage screen. Elsewhere in the garage the car on show may be one the player
		/// is only browsing, or a CarX screen holds its own copy of the setup and would overwrite ours.
		/// </summary>
		private static string GarageScreenProblem() {
			if (States.instance != null && States.current is DynoMenuState) {
				return "Close CarX's tuning screen first.";
			}

			if (!(States.mainCurrent is BaseGameState state) || state.contextOwner == null) {
				return "Go to the main garage screen first.";
			}

			ContextOwner screens = state.contextOwner;
			if (IsOpen(screens.Get<UISetupsContext>()) || IsOpen(screens.Get<UIDynostandContext>())) {
				return "Close CarX's Setups or tuning screen first.";
			}
			if (IsOpen(screens.Get<UICarSelectorContext>())) {
				return "Close the car list first.";
			}
			if (!IsOpen(screens.Get<UIMainMenuContext>()) && !IsOpen(screens.Get<UIGarageContext>())) {
				return "Go to the main garage screen first.";
			}

			return null;
		}

		private static bool IsOpen(BaseContext screen) => screen != null && screen.isActive;

		private static float DefaultOf(CarProfileController controller) {
			CarProfileChanger changer = controller.changers[0];
			return changer.EvaluatePhase(changer.defaultValue);
		}

		/// <summary>Formats a slider position the way CarX's tuning screen does (DynoParamsItemViewModel).</summary>
		private static string Describe(CarProfileController controller, float phase) {
			CarProfileChanger changer = controller.changers[0];
			var parameter = changer.parameter;

			if (controller.name == DiffTypeKey) {
				int type = Mathf.RoundToInt(changer.EvaluateValue(phase));
				return DiffTypeEnum != null ? (Enum.GetName(DiffTypeEnum, type) ?? type.ToString(CultureInfo.InvariantCulture)) : type.ToString(CultureInfo.InvariantCulture);
			}

			if (parameter.id == DrivetrainParameterId) {
				return DrivetrainSwitchDynoParamViewModel.GetDrivetrainTitle(Mathf.RoundToInt(changer.EvaluateValue(phase)));
			}

			if (parameter.id == SequentialGearboxParameterId) {
				return phase > 0.5f ? "Sequential" : "Standard";
			}

			if (controller.isGroup || controller.isPercent) {
				return (phase * 100f).ToString("#,0", CultureInfo.InvariantCulture) + "%";
			}

			if (parameter.isBool) {
				return phase > 0.5f ? "Yes" : "No";
			}

			float value = changer.EvaluateValue(phase) * changer.multiplier;
			string text = value.ToString(string.IsNullOrEmpty(parameter.format) ? "N0" : parameter.format, CultureInfo.InvariantCulture);
			return parameter.units == null || parameter.units == "none" ? text : $"{text} {parameter.units}";
		}

		private static bool Failed(Exception e, out string error) {
			error = $"CarX connection problem: {e.GetType().Name}: {e.Message}";
			KSL.API.Kino.Log.Error(e);
			return false;
		}

		private static string Or(string value) => string.IsNullOrEmpty(value) ? "?" : value;
	}
}
