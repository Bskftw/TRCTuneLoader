// While driving: the car's pose, the road under it and its width, teleporting, the map, and the headlights.
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
			} catch (Exception e) {
				// Called every frame: a failure only means the road check is skipped.
				TrcLog.Once("road check", $"The check that the car is over the track's road isn't working ({e.Message}), so leaving the road doesn't stop a run; said once.");
				return false;
			}
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
			} catch (Exception e) {
				TrcLog.Once("road width", $"Couldn't measure the road's width ({e.Message}), so grids use the usual width; said once.");
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

		// CarX's own headlight switch (what the light key calls): it lights the car and shares it with the room.
		private static readonly MethodInfo UpdateHeadlightsMethod =
			typeof(PlayerCarControl).GetMethod("UpdateHeadlights", BindingFlags.Instance | BindingFlags.NonPublic);

		public bool HeadlightsOn {
			get {
				try {
					return States.mainCurrent is BaseRaceModeState race && race.userCar != null && !race.userCar.isNetworkCar && race.userCar.headlightsEnabled;
				} catch (Exception e) {
					TrcLog.Once("headlights", $"Couldn't read the headlights ({e.Message}), so the headlight start for console players won't work; said once.");
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
	}
}
