// The live check: a reference recorded after CarX put the official tune on, and the car's live values against it.
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
	}
}
