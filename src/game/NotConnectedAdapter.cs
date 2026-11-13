// The stand-in when CarX can't be reached: every call says so, so the menu still opens.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Used when the CarX connection can't start (for example after a CarX update changed
	/// something the mod relies on). Tunes can still be browsed and their codes shown.
	/// </summary>
	public sealed class NotConnectedAdapter : ICarTuneAdapter {
		private readonly string message_;

		public NotConnectedAdapter(string reason = null) {
			message_ = string.IsNullOrEmpty(reason)
				? "Not connected to CarX: tunes can be browsed, but not applied or checked."
				: $"Not connected to CarX ({reason}): tunes can be browsed, but not applied or checked.";
		}

		public bool IsReady => false;
		public string Status => message_;

		public bool TryGetCurrentCar(out CarInfo car, out string error) {
			car = null;
			error = message_;
			return false;
		}

		public bool TryReadCurrentTune(CarInfo car, out Dictionary<string, float> values, out string error) {
			values = new Dictionary<string, float>();
			error = message_;
			return false;
		}

		public bool TryGetDefaults(int slotId, out Dictionary<string, float> defaults, out string error) {
			defaults = new Dictionary<string, float>();
			error = message_;
			return false;
		}

		public bool TryDescribe(int slotId, IDictionary<string, float> values, out Dictionary<string, string> text, out string error) {
			text = new Dictionary<string, string>();
			error = message_;
			return false;
		}

		public bool TryGetCarName(int carId, out string name) {
			name = "";
			return false;
		}

		public bool CanApply(CarInfo car, out string reason) {
			reason = message_;
			return false;
		}

		public bool TryApplyTune(CarInfo car, TuneFile tune, out string error) {
			error = message_;
			return false;
		}

		public bool TryLoadIntoDyno(CarInfo car, TuneFile tune, out string error) {
			error = message_;
			return false;
		}

		public bool TryRecordReference(CarInfo car, string tuneCode, out string error) {
			error = message_;
			return false;
		}

		public bool TryCheckLive(CarInfo car, string tuneCode, out List<string> differences, out string note) {
			differences = new List<string>();
			note = message_;
			return false;
		}

		public bool HasReference(CarInfo car, string tuneCode) => false;

		public bool TryRecordReferenceOnTrack(CarInfo car, string tuneCode, out string error) {
			error = message_;
			return false;
		}

		public bool TryGetRoomPlayers(out List<RoomPlayer> players, out string error) {
			players = new List<RoomPlayer>();
			error = message_;
			return false;
		}

		public bool TryGetRoomKeys(List<(ulong SteamId, string Key)> members) => false;

		public bool TryCheckStance(CarInfo car, out List<string> differences, out string error) {
			differences = new List<string>();
			error = message_;
			return false;
		}

		public bool TryGetWorkshopItems(CarInfo car, out List<WorkshopItem> items, out string error) {
			items = new List<WorkshopItem>();
			error = message_;
			return false;
		}

		public bool TryCheckDrivetrain(CarInfo car, IDictionary<string, float> tune, bool withPhysics, out DrivetrainCheck check, out string error) {
			check = null;
			error = message_;
			return false;
		}

		public bool TryGetCarPose(out CarPose pose) {
			pose = default;
			return false;
		}

		public bool TryGetMap(out MapInfo map, out string error) {
			map = null;
			error = message_;
			return false;
		}

		public bool TryMoveCarTo(Vector3 position, Vector3 forward, out string error) {
			error = message_;
			return false;
		}

		public bool TryGetTuneNumbers(int slotId, IDictionary<string, float> values, out Dictionary<string, float> numbers, out Dictionary<string, int> decimals, out string error) {
			numbers = new Dictionary<string, float>();
			decimals = new Dictionary<string, int>();
			error = Status;
			return false;
		}

		public bool TryGetRoomCarPoses(List<RoomCarPose> poses) => false;

		public bool TrySetHeadlights(bool on, out string error) {
			error = Status;
			return false;
		}

		public bool HeadlightsOn => false;

		public GameObject BuildGhostBody(Material material) => null;

		public bool TryGetSuspension(SuspensionFrame frame) => false;

		public bool TryGetTelemetry(TelemetryFrame frame) => false;

		public string PlayerName => "";

		public bool TryGetSuspensionSetup(out CornerSetup[] corners) {
			corners = null;
			return false;
		}

		public bool TryCheckOverRoad(ICollection<int> roadColliders, out bool overRoad) {
			overRoad = false;
			return false;
		}

		public bool TryMeasureRoadWidth(Vector3 centre, Vector3 right, ICollection<int> roadColliders, out float width) {
			width = 0f;
			return false;
		}

		public bool TryShowMapIcons(IList<MapIcon> icons) => false;

		public void SizeMapIcons() {
		}
	}
}
