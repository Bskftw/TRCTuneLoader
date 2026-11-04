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

		public bool HasReference(CarInfo car, string tuneCode) =>
			car != null && references_.TryGetValue(ReferenceKey(car), out DescSnapshot reference) && reference.TuneCode == tuneCode;

		public bool TryRecordReferenceOnTrack(CarInfo car, string tuneCode, out string error) {
			try {
				RaceCar raceCar = FindPlayerCar(out bool inGarage);
				if (inGarage || raceCar == null || CarIdOf(raceCar) != car.CarId) {
					error = "Not on the track with this car.";
					return false;
				}
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
					note = "The live car is recorded a few seconds after it reaches the garage or the track: give it a moment.";
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
						Name = BattleMessage.PlainName(string.IsNullOrEmpty(player.FilteredNickName) ? shared.Name ?? "" : player.FilteredNickName),
						SteamId = SteamIdOf(shared.PlayerID),
						Key = shared.PlayerID.ToString(),
						Platform = PlatformName(shared.PlayerID.platform),
						IsConsole = IsConsole(shared.PlayerID.platform),
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

		private bool poseFailureLogged_;

		public bool TryGetCarPose(out CarPose pose) {
			pose = default;
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar) {
					return false;
				}

				RaceCar raceCar = race.userCar;
				Transform body = raceCar.carX != null ? raceCar.carX.transform : raceCar.transform;
				pose.Position = body.position;
				pose.Forward = body.forward;
				pose.SpeedKmh = raceCar.carX != null ? Mathf.Abs(raceCar.carX.speedKMH) : 0f;
				pose.CarId = CarIdOf(raceCar);
				pose.Rotation = body.rotation;
				return true;
			} catch (Exception e) {
				// Called every frame: log the first failure only.
				if (!poseFailureLogged_) {
					poseFailureLogged_ = true;
					TrcLog.Error(e);
				}
				return false;
			}
		}

		/// <summary>How far down (metres) the road is looked for under the car: deep enough for any jump.</summary>
		private const float RoadRayLength = 60f;

		private readonly RaycastHit[] roadHits_ = new RaycastHit[32];
		private readonly Vector3[] footprintPoints_ = new Vector3[5];
		private int footprintCar_ = -1;

		/// <summary>Half the car's width and length (metres), a little inside its body, for the corners checked over the road.</summary>
		private Vector2 footprint_ = new Vector2(0.8f, 1.8f);

		public bool TryCheckOverRoad(ICollection<int> roadColliders, out bool overRoad) {
			overRoad = false;
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar) {
					return false;
				}
				RaceCar raceCar = race.userCar;
				Transform body = raceCar.carX != null ? raceCar.carX.transform : raceCar.transform;

				// The car's size, once per car (its description is costly to read every frame).
				int carId = CarIdOf(raceCar);
				if (carId != footprintCar_) {
					footprintCar_ = carId;
					Vector3 extents = raceCar.GetDesc().carXDesc.weight.bounds.extents;
					footprint_ = extents.x > 0.4f && extents.z > 1f ? new Vector2(extents.x * 0.9f, extents.z * 0.9f) : new Vector2(0.8f, 1.8f);
				}

				// The middle and the four corners, each straight down, through anything in between (a tree, a sign):
				// any of them above the course's road counts, so being in the air over the road is still on it.
				Vector3 middle = body.position;
				Vector3 right = body.right * footprint_.x;
				Vector3 ahead = body.forward * footprint_.y;
				footprintPoints_[0] = middle;
				footprintPoints_[1] = middle + right + ahead;
				footprintPoints_[2] = middle - right + ahead;
				footprintPoints_[3] = middle + right - ahead;
				footprintPoints_[4] = middle - right - ahead;
				foreach (Vector3 point in footprintPoints_) {
					int count = Physics.RaycastNonAlloc(point + Vector3.up * 2f, Vector3.down, roadHits_, RoadRayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
					for (int i = 0; i < count; i++) {
						Collider collider = roadHits_[i].collider;
						if (collider != null && roadColliders.Contains(collider.GetInstanceID())) {
							overRoad = true;
							return true;
						}
					}
				}
				return true;
			} catch (Exception) {
				// Called every frame: a failure only means the road check is skipped.
				return false;
			}
		}

		// ---- Map icons ----

		private readonly List<TrcMinimapItem> mapIcons_ = new List<TrcMinimapItem>();
		private int mapIconType_ = -1;
		private bool mapIconsFailed_;
		private Minimap iconsMinimap_;
		private static readonly FieldInfo IconColour = typeof(MinimapItem).GetField("m_color", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly FieldInfo IconSize = typeof(MinimapItem).GetField("m_size", BindingFlags.Instance | BindingFlags.NonPublic);

		public bool TryShowMapIcons(IList<MapIcon> icons) {
			try {
				mapIcons_.RemoveAll(i => i == null); // a new map took them with the old scene
				if (Minimap.instance == null || IconColour == null || IconSize == null) {
					HideMapIcons(0);
					return false;
				}
				if (mapIconType_ < 0) {
					mapIconType_ = PickMapIconType();
				}
				if (!ReferenceEquals(Minimap.instance, iconsMinimap_)) {
					// A new map cleared CarX's list of icons: take ours off so they're put back on it.
					iconsMinimap_ = Minimap.instance;
					HideMapIcons(0);
				}
				for (int i = 0; i < icons.Count; i++) {
					if (i == mapIcons_.Count) {
						var go = new GameObject("TRC map icon") { hideFlags = HideFlags.DontSave };
						go.SetActive(false);
						TrcMinimapItem added = go.AddComponent<TrcMinimapItem>();
						added.typeIndex = mapIconType_;
						mapIcons_.Add(added);
					}
					TrcMinimapItem item = mapIcons_[i];
					item.transform.position = icons[i].Position;
					item.Shape = icons[i].Shape;
					Vector3 heading = new Vector3(icons[i].Heading.x, 0f, icons[i].Heading.z);
					// The map turns an icon with its object, so a line lies across the road.
					item.transform.rotation = heading.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(heading) : Quaternion.identity;
					IconColour.SetValue(item, (Color?)icons[i].Color);
					int? size = icons[i].Size;
					bool resized = !Equals(IconSize.GetValue(item), size);
					IconSize.SetValue(item, size);
					if (resized && item.gameObject.activeSelf) {
						// The map sizes an icon when it's added: take it off and put it back.
						item.gameObject.SetActive(false);
					}
					if (!item.gameObject.activeSelf) {
						item.gameObject.SetActive(true);
					}
				}
				HideMapIcons(icons.Count);
				return true;
			} catch (Exception e) {
				if (!mapIconsFailed_) {
					mapIconsFailed_ = true;
					TrcLog.Warning($"Map icons: {e.Message}");
				}
				return false;
			}
		}

		/// <summary>Our icons on the driving minimap are this much bigger than their size.</summary>
		private const float MinimapIconScale = 1.3f;

		/// <summary>
		/// On the full map (menu) they're smaller the bigger the map is (bskftw, Nov 3: Project Dream's is 30 km, and a lot
		/// of icons at the minimap's size would cover it): their size times this many metres over the map's width, at
		/// most <see cref="FullMapMostScale"/> of it, and never under a few pixels so they don't vanish. Zooming in on the
		/// full map spreads them apart (it stretches the map, not the icons).
		/// </summary>
		private const float FullMapMetres = 4000f;
		private const float FullMapMostScale = 0.55f;

		private static float FullMapIconSize(int size) {
			Vector2 bounds = Minimap.instance != null ? Minimap.instance.boundsSize : Vector2.zero;
			float width = Mathf.Max(bounds.x, bounds.y);
			float scale = width > 1f ? Mathf.Min(FullMapMostScale, FullMapMetres / width) : FullMapMostScale;
			return Mathf.Max(size >= 20 ? 8f : 5f, size * scale);
		}
		private static readonly FieldInfo VisualizerIcons = typeof(Minimaps.BaseMinimapVisualizer).GetField("m_iconsList", BindingFlags.Instance | BindingFlags.NonPublic);
		private Minimaps.BaseMinimapVisualizer[] visualizers_ = new Minimaps.BaseMinimapVisualizer[0];
		private float visualizersAt_;

		public void SizeMapIcons() {
			try {
				if (VisualizerIcons == null || !mapIcons_.Exists(i => i != null && i.gameObject.activeSelf)) {
					return;
				}
				if (Time.unscaledTime >= visualizersAt_) {
					// The maps on screen (the minimap, the menu's full map), looked for again every couple of seconds.
					visualizersAt_ = Time.unscaledTime + 2f;
					visualizers_ = UnityEngine.Object.FindObjectsByType<Minimaps.BaseMinimapVisualizer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
				}
				foreach (Minimaps.BaseMinimapVisualizer map in visualizers_) {
					if (map == null || !(VisualizerIcons.GetValue(map) is List<HUD.UIMinimapItem> shown)) {
						continue;
					}
					bool full = map is Minimaps.GlobalMinimapVisualizer || map is Minimaps.UIMinimapPauseMenuVisualizer;
					foreach (HUD.UIMinimapItem icon in shown) {
						if (icon != null && icon.item is TrcMinimapItem ours) {
							float size = full ? FullMapIconSize(ours.size) : ours.size * MinimapIconScale;
							if (Mathf.Abs(icon.size.x - size) > 0.5f) {
								icon.size = new Vector2(size, size);
							}
							Sprite shape = MapIconSprites.For(ours.Shape);
							if (shape != null && icon.image != shape) {
								icon.image = shape;
							}
						}
					}
				}
			} catch (Exception) {
				// Every frame: a failure only leaves the icons at CarX's size.
			}
		}

		private void HideMapIcons(int from) {
			for (int i = from; i < mapIcons_.Count; i++) {
				if (mapIcons_[i] != null && mapIcons_[i].gameObject.activeSelf) {
					mapIcons_[i].gameObject.SetActive(false);
				}
			}
		}

		/// <summary>CarX's round icon (the one network cars use), logged with the others the first time.</summary>
		private static int PickMapIconType() {
			Minimap.IconType[] types = ScriptableSingleton<MinimapIconSettings>.instance?.iconTypes ?? new Minimap.IconType[0];
			TrcLog.Info($"Map icon types: {string.Join(", ", types.Select((t, i) => $"{i} {t?.name} ({t?.size} px)"))}.");
			int round = Array.FindIndex(types, t => t != null && t.icon != null && (t.name ?? "").IndexOf("circle", StringComparison.OrdinalIgnoreCase) >= 0);
			if (round < 0) {
				round = Array.FindIndex(types, t => t != null && t.icon != null && (t.name ?? "").IndexOf("point", StringComparison.OrdinalIgnoreCase) >= 0);
			}
			return round >= 0 ? round : Math.Min(1, Math.Max(0, types.Length - 1));
		}

		public bool TryMeasureRoadWidth(Vector3 centre, Vector3 right, ICollection<int> roadColliders, out float width) {
			width = 0f;
			try {
				if (roadColliders == null || roadColliders.Count == 0) {
					return false;
				}
				right = new Vector3(right.x, 0f, right.z).normalized;
				bool OnRoad(Vector3 point) {
					int count = Physics.RaycastNonAlloc(point + Vector3.up * 3f, Vector3.down, roadHits_, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
					for (int i = 0; i < count; i++) {
						Collider collider = roadHits_[i].collider;
						if (collider != null && roadColliders.Contains(collider.GetInstanceID())) {
							return true;
						}
					}
					return false;
				}
				if (!OnRoad(centre)) {
					return false;
				}
				const float step = 0.25f, most = 15f;
				float Edge(float side) {
					float reached = 0f;
					for (float d = step; d <= most; d += step) {
						if (!OnRoad(centre + right * side * d)) {
							break;
						}
						reached = d;
					}
					return reached;
				}
				width = Edge(1f) + Edge(-1f) + step;
				return true;
			} catch (Exception) {
				return false;
			}
		}

		public bool TryMoveCarTo(Vector3 position, Vector3 forward, out string error) {
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar) {
					error = "Drive onto the map first.";
					return false;
				}
				// The same rules as CarX's own respawn key (PlayerCarControl.TryRespawnCar).
				PlayerCarControl control = PlayerCarControl.instance;
				if (!race.allowCarRespawnInRace || (control != null && !control.canSpawnCarInMPRace)) {
					error = "CarX doesn't allow moving the car right now.";
					return false;
				}
				RaceCar raceCar = race.userCar;
				if (raceCar.lockState || raceCar.carPlacementModel == null) {
					error = "The car can't be moved right now.";
					return false;
				}

				// CarX's own placement: puts the car on the road below the point, stops it and tells the room.
				raceCar.carPlacementModel.ResetToPoint(position + Vector3.up, Quaternion.LookRotation(forward, Vector3.up));
				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public bool TryGetMap(out MapInfo map, out string error) {
			map = null;
			try {
				DB.Meta.Track track = (States.mainCurrent as BaseRaceState)?.trackMeta;
				if (track == null) {
					error = "Not on a map.";
					return false;
				}

				if (track.external) {
					bool modIo = track.ugcServiceId == (byte)Services.UGC.Interfaces.IUGCManager.ServiceProvider.ModIO;
					string id = track.externalId.ToString(CultureInfo.InvariantCulture);
					string name = Clean(track.name);
					map = new MapInfo {
						Key = $"{(modIo ? "modio" : "workshop")}:{id}",
						Name = name.Length > 0 ? name : $"{(modIo ? "mod.io" : "Steam Workshop")} map {id}",
						External = true,
					};
				} else {
					string id = track.id.ToString(CultureInfo.InvariantCulture);
					string name = Clean(track.name);
					map = new MapInfo { Key = $"carx:{id}", Name = name.Length > 0 ? name : $"CarX track {id}" };
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

		private static void AddStanceDifference(List<string> differences, string name, float live, float stock) {
			float deltaMm = (live - stock) * 1000f;
			if (Math.Abs(deltaMm) >= 0.5f) {
				differences.Add($"Stance: {name} {(deltaMm > 0 ? "+" : "")}{deltaMm:0} mm ({live * 1000f:0} mm, stock {stock * 1000f:0} mm)");
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

		private static DescSnapshot SnapshotOf(RaceCar raceCar) {
			var extra = new Dictionary<string, object> {
				["frontBaseOffset"] = raceCar.frontBaseOffset,
				["rearBaseOffset"] = raceCar.rearBaseOffset,
			};
			// The physics engine's own wheel values too: a mod that changes the suspension there (not in the
			// description) shows up as a difference.
			AddWheelPhysics(extra, raceCar);
			return DescSnapshot.Of(raceCar.GetDesc(), extra);
		}

		private static string ReferenceKey(CarInfo car) => $"{car.CarId}:{car.SlotId}";

		private static int CarIdOf(RaceCar raceCar) => raceCar.metaInfo != null ? raceCar.metaInfo.id : raceCar.carId;

		/// <summary>How many decimals a .NET number format shows ("N0" none, "N2" or "0.00" two), as Describe prints them.</summary>
		private static int DecimalsOf(string format) {
			if (string.IsNullOrEmpty(format)) {
				return 0;
			}
			char kind = char.ToUpperInvariant(format[0]);
			if ((kind == 'N' || kind == 'F') && format.Length > 1 && int.TryParse(format.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int digits)) {
				return Math.Min(digits, 6);
			}
			int dot = format.IndexOf('.');
			return dot < 0 ? 0 : Math.Min(6, format.Skip(dot + 1).TakeWhile(c => c == '0' || c == '#').Count());
		}

		private static string PlatformName(UserPlatform.Id platform) {
			switch (platform) {
				case UserPlatform.Id.PS4: return "PlayStation";
				case UserPlatform.Id.Xbox: return "Xbox";
				case UserPlatform.Id.Switch: return "Switch";
				case UserPlatform.Id.EpicGames: return "Epic";
				case UserPlatform.Id.MyGames: return "MY.GAMES";
				case UserPlatform.Id.Steam: return "Steam";
				default: return platform.ToString();
			}
		}

		private static bool IsConsole(UserPlatform.Id platform) =>
			platform == UserPlatform.Id.PS4 || platform == UserPlatform.Id.Xbox || platform == UserPlatform.Id.Switch;

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

		private bool posesFailureLogged_;

		public bool TryGetRoomCarPoses(List<RoomCarPose> poses) {
			poses.Clear();
			try {
				NetworkGame game = NetworkController.InstanceGame;
				if (!NetworkController.IsActive || game?.Players == null) {
					return false;
				}
				foreach (NetworkPlayer player in game.Players) {
					SmartfoxPlayerProperties shared = player?.PlayerProperties;
					RaceCar car = player?.userCar;
					if (shared == null || car == null || player.IsLocal) {
						continue;
					}
					Transform body = car.carX != null ? car.carX.transform : car.transform;
					poses.Add(new RoomCarPose {
						Key = shared.PlayerID.ToString(),
						Name = BattleMessage.PlainName(string.IsNullOrEmpty(player.FilteredNickName) ? shared.Name ?? "" : player.FilteredNickName),
						Platform = PlatformName(shared.PlayerID.platform),
						IsConsole = IsConsole(shared.PlayerID.platform),
						Pose = new CarPose {
							Position = body.position, Forward = body.forward, Rotation = body.rotation, CarId = shared.CarId,
							SpeedKmh = car.carX != null ? Mathf.Abs(car.carX.speedKMH) : 0f,
						},
					});
				}
				return true;
			} catch (Exception e) {
				if (!posesFailureLogged_) {
					posesFailureLogged_ = true;
					TrcLog.Error(e);
				}
				return false;
			}
		}

		// CarX's own headlight switch (what the light key calls): it lights the car and shares it with the room.
		private static readonly MethodInfo UpdateHeadlightsMethod =
			typeof(PlayerCarControl).GetMethod("UpdateHeadlights", BindingFlags.Instance | BindingFlags.NonPublic);

		public bool HeadlightsOn {
			get {
				try {
					return States.mainCurrent is BaseRaceModeState race && race.userCar != null && !race.userCar.isNetworkCar && race.userCar.headlightsEnabled;
				} catch (Exception) {
					return false;
				}
			}
		}

		public bool TrySetHeadlights(bool on, out string error) {
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar) {
					error = "Not driving.";
					return false;
				}
				RaceCar car = race.userCar;
				if (car.headlightsEnabled == on) {
					error = "";
					return true;
				}
				PlayerCarControl control = car.GetComponentInParent<PlayerCarControl>() ?? UnityEngine.Object.FindAnyObjectByType<PlayerCarControl>();
				if (control != null && UpdateHeadlightsMethod != null) {
					UpdateHeadlightsMethod.Invoke(control, new object[] { on, false });
				} else {
					car.SetHeadlightsEnabled(on, false, true);
					NetworkController.InstanceGame?.systems?.Get<NetGameSyncCarLightsSystem>()?.SendToAll(car.headlightsEnabled);
				}
				error = car.headlightsEnabled == on ? "" : "CarX didn't switch the lights (is the engine running?).";
				return error.Length == 0;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		public GameObject BuildGhostBody(Material material) {
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar || material == null) {
					return null;
				}
				RaceCar car = race.userCar;
				Transform body = car.carX != null ? car.carX.transform : car.transform;
				var root = new GameObject("TRC ghost") { hideFlags = HideFlags.DontSave };
				int count = 0;
				var skipped = new List<string>();
				foreach (MeshFilter filter in car.GetComponentsInChildren<MeshFilter>(false)) {
					MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
					if (filter.sharedMesh == null || renderer == null || !renderer.enabled || count >= 300) {
						continue;
					}
					if (NotCarBody(filter, renderer, body)) {
						// A fake shadow or decal plane under the car (bskftw, testing: a square on the ground round the ghost).
						skipped.Add(filter.name);
						continue;
					}
					var part = new GameObject("part") { hideFlags = HideFlags.DontSave };
					part.transform.SetParent(root.transform, false);
					part.transform.localPosition = body.InverseTransformPoint(filter.transform.position);
					part.transform.localRotation = Quaternion.Inverse(body.rotation) * filter.transform.rotation;
					part.transform.localScale = filter.transform.lossyScale;
					part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
					MeshRenderer copy = part.AddComponent<MeshRenderer>();
					var materials = new Material[Math.Max(1, filter.sharedMesh.subMeshCount)];
					for (int i = 0; i < materials.Length; i++) {
						materials[i] = material;
					}
					copy.sharedMaterials = materials;
					copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					copy.receiveShadows = false;
					count++;
				}
				if (count == 0) {
					UnityEngine.Object.Destroy(root);
					return null;
				}
				if (skipped.Count > 0) {
					TrcLog.Info($"Ghost: left out {skipped.Count} flat or shadow part(s) of the car: {string.Join(", ", skipped.Take(8))}.");
				}
				return root;
			} catch (Exception e) {
				TrcLog.Error(e);
				return null;
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

		private bool suspensionFailureLogged_;

		/// <summary>
		/// The four wheels as CarX's own D-BOX motion-seat support reads them (CarXDboxManager): each wheel's height under
		/// its top mount against the spring's full length. Fully extended is the full length below the mount, fully
		/// compressed is up at it.
		/// </summary>
		public bool TryGetSuspension(SuspensionFrame frame) {
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar || race.userCar.carX == null) {
					return false;
				}
				CarX.Car car = race.userCar.carX;
				for (int i = 0; i < 4; i++) {
					var index = (CarX.WheelIndex)i;
					CarX.Wheel wheel = car.GetWheel(index);
					Transform t = car.GetWheelTransform(index);
					if (!wheel.isValid || t == null) {
						return false;
					}
					float full = Mathf.Max(0.01f, wheel.maxSpringLen);
					float below = wheel.topPos.y - t.localPosition.y;
					frame.Wheels[i] = new WheelFrame {
						Compression = Mathf.Clamp01(1f - below / full),
						Load = wheel.vertForce.magnitude,
						Contact = wheel.HasPatch(),
						SlipAngle = wheel.sa,
						SlipRatio = wheel.sr,
					};
				}
				frame.Position = car.transform.position;
				frame.Kmh = Mathf.Abs(car.speedKMH);
				return true;
			} catch (Exception e) {
				if (!suspensionFailureLogged_) {
					suspensionFailureLogged_ = true;
					TrcLog.Error(e);
				}
				return false;
			}
		}

		public bool TryGetSuspensionSetup(out CornerSetup[] corners) {
			corners = null;
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar || race.userCar.carX == null) {
					return false;
				}
				CarX.Car car = race.userCar.carX;
				CarX.CarDesc.Weight weight = race.userCar.GetDesc().carXDesc.weight;
				float front = Mathf.Clamp01(weight.frontPercent / 100f);
				float right = Mathf.Clamp01(weight.rightPercent / 100f);
				corners = new CornerSetup[4];
				for (int i = 0; i < 4; i++) {
					CarX.Wheel wheel = car.GetWheel((CarX.WheelIndex)i);
					float axle = i < 2 ? front : 1f - front;
					float side = i % 2 == 1 ? right : 1f - right;
					corners[i] = new CornerSetup {
						Travel = wheel.maxSpringLen, Spring = wheel.springCoef,
						SlowBump = wheel.slowBump, FastBump = wheel.fastBump, SlowRebound = wheel.slowRebound, FastRebound = wheel.fastRebound,
						BumpSlowLimit = wheel.bumpSlowLimit, BumpFastLimit = wheel.bumpFastLimit,
						ReboundSlowLimit = wheel.reboundSlowLimit, ReboundFastLimit = wheel.reboundFastLimit,
						CornerMass = weight.mass * axle * side,
					};
				}
				return true;
			} catch (Exception e) {
				TrcLog.Error(e);
				return false;
			}
		}

		/// <summary>
		/// Not part of the car's body: a flat plane (no thicker than 3 cm, over a metre across, like the fake shadow under
		/// a car), or something named or drawn like a shadow, decal or projector.
		/// </summary>
		private static bool NotCarBody(MeshFilter filter, MeshRenderer renderer, Transform body) {
			string name = (filter.name + " " + filter.sharedMesh.name).ToLowerInvariant();
			string[] words = { "shadow", "decal", "projector", "blob", "ground", "occlusion" };
			if (words.Any(w => name.Contains(w))) {
				return true;
			}
			foreach (Material material in renderer.sharedMaterials) {
				string shader = material != null && material.shader != null ? material.shader.name.ToLowerInvariant() : "";
				if (words.Any(w => shader.Contains(w)) || (material != null && material.name.ToLowerInvariant().Contains("shadow"))) {
					return true;
				}
			}
			Bounds bounds = filter.sharedMesh.bounds;
			Vector3 size = Vector3.Scale(bounds.size, filter.transform.lossyScale);
			// Measured along the car's up: thin and wide is a plane lying under it.
			Vector3 up = body.InverseTransformDirection(filter.transform.up);
			float thickness = Mathf.Abs(up.y) > 0.9f ? Mathf.Abs(size.y) : Mathf.Min(Mathf.Abs(size.x), Mathf.Min(Mathf.Abs(size.y), Mathf.Abs(size.z)));
			float across = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z));
			return thickness < 0.03f && across > 1f;
		}

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
			TrcLog.Error(e);
			return false;
		}

		private static string Or(string value) => string.IsNullOrEmpty(value) ? "?" : value;
	}

	/// <summary>
	/// One of the mod's icons on CarX's minimap and big map. CarX's own icons wait for a car to belong to before they
	/// show (in online rooms); these belong to no car, so they go on the map straight away, like CarX's waypoint does.
	/// </summary>
	internal sealed class TrcMinimapItem : MinimapItem {
		public MapIconShape Shape;

		protected override Cysharp.Threading.Tasks.UniTask WaitCar() {
			Minimaps.MinimapProvider.AddIcon(this);
			return Cysharp.Threading.Tasks.UniTask.CompletedTask;
		}
	}
}
