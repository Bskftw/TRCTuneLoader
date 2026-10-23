using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>Where the player's car is and how fast it goes, while driving.</summary>
	public struct CarPose {
		public Vector3 Position;
		public Vector3 Forward;
		public float SpeedKmh;
		public int CarId;
	}

	/// <summary>The map the player is on.</summary>
	public sealed class MapInfo {
		/// <summary>
		/// "workshop:&lt;id&gt;" or "modio:&lt;id&gt;" for Workshop and mod.io maps, "carx:&lt;track id&gt;" for CarX's own,
		/// or a shared key like "project-dream" for a map uploaded more than once (see CourseLibrary.Recognize).
		/// </summary>
		public string Key = "";
		public string Name = "";
		public bool External;

		/// <summary>The upload's own key when <see cref="Key"/> is a shared one, else "".</summary>
		public string UploadKey = "";
	}

	/// <summary>The player's car as the mod sees it.</summary>
	public sealed class CarInfo {
		public int CarId;
		public string CarName = "";
		public int SlotId;
		public string SlotName = "";
		public bool StockEngine;

		/// <summary>True when one of the player's own named custom setups is installed on this car.</summary>
		public bool CustomSetup;

		/// <summary>True in the garage, where tunes can be applied. False on track or online.</summary>
		public bool InGarage;

		/// <summary>True while CarX's tuning screen (dyno) is open for this car, in the garage or in an online room.</summary>
		public bool InDyno;

		public string CarIdText => CarId.ToString(CultureInfo.InvariantCulture);
		public string SlotIdText => SlotId.ToString(CultureInfo.InvariantCulture);
		public string Describe() => $"{CarName} ({SlotName})";
	}

	/// <summary>A player in the online room, with the setup CarX shares with everyone in it.</summary>
	public sealed class RoomPlayer {
		public string Name = "";
		public ulong SteamId;
		public bool IsLocal;
		public int CarId;
		public string CarName = "";
		public int SlotId;
		public string SlotName = "";
		public int EngineId;

		/// <summary>The setup CarX sends to the other players: slider positions by setting name.</summary>
		public Dictionary<string, float> SharedTune = new Dictionary<string, float>();

		/// <summary>How their body parts change the car's stance (track width, axles, ride height), as CarX built their car here.</summary>
		public List<string> StanceDifferences = new List<string>();

		/// <summary>Steam Workshop (or mod.io) bodies on their car, also the ones CarX doesn't show here.</summary>
		public List<WorkshopItem> Workshop = new List<WorkshopItem>();

		/// <summary>Their engine, gearbox and drivetrain as CarX built their car here (null until it's built).</summary>
		public DrivetrainCheck Drivetrain;

		public string CarIdText => CarId.ToString(CultureInfo.InvariantCulture);
		public string SlotIdText => SlotId.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// A Steam Workshop (or mod.io) car item whose body parts are on a car. Workshop cars are bodies on top
	/// of a CarX car: they keep its physics and tune, and can only change how it drives by moving the wheels.
	/// </summary>
	public sealed class WorkshopItem {
		public ulong Id;
		public string Source = "Steam Workshop";

		/// <summary>The item's title, or its parts' names, when this PC has the item.</summary>
		public string Name = "";

		/// <summary>False when this PC doesn't have the item, so what its parts do is unknown here.</summary>
		public bool Installed;

		/// <summary>CarX put the item's parts on the car on this PC (always true for your own car).</summary>
		public bool ShownHere;

		/// <summary>Its parts move the wheels (track width, axle position, ride height), so the car isn't TRC spec.</summary>
		public bool MovesWheels;

		public string Describe() {
			string item = $"{Source} item {Id.ToString(CultureInfo.InvariantCulture)}";
			if (!Installed) {
				return $"Workshop body: {item}. This PC doesn't have it, so its stance can't be checked here.";
			}
			string name = string.IsNullOrEmpty(Name) ? item : $"{Name} ({item})";
			string hidden = ShownHere ? "" : " CarX shows the stock body on this PC.";
			return MovesWheels
				? $"Workshop body: {name}. It moves the wheels, so the car isn't TRC spec.{hidden}"
				: $"Workshop body: {name}. Looks only: it doesn't move the wheels.{hidden}";
		}
	}

	/// <summary>
	/// A car's engine, gearbox and drivetrain compared with what CarX's own data for the setup and the
	/// setup's tune give. CarX's engine swap and CarX's drivetrain setting are named; anything else that
	/// changed them (KINO's engine or gearbox swaps, another mod, an editor) shows as differences.
	/// </summary>
	public sealed class DrivetrainCheck {
		/// <summary>"Stock engine", or the CarX engine swap.</summary>
		public string Engine = "";

		/// <summary>A CarX engine swap is installed (not TRC spec; its values aren't compared).</summary>
		public bool EngineSwap;

		/// <summary>RWD, FWD, AWD or 4WD, as the car drives now.</summary>
		public string Drivetrain = "";

		/// <summary>The car's own drivetrain when CarX's drivetrain setting changes it, else "".</summary>
		public string StockDrivetrain = "";

		/// <summary>Values that differ from CarX's data and the tune: changed by another mod (like KINO) or an editor.</summary>
		public List<string> Differences = new List<string>();

		/// <summary>The physics engine's own values next to the car's description, for the log.</summary>
		public List<string> PhysicsLines = new List<string>();

		/// <summary>False when the values couldn't be compared (see <see cref="Note"/>).</summary>
		public bool Compared;
		public string Note = "";

		public bool Stock => !EngineSwap && Differences.Count == 0;

		public string Summary() {
			string drivetrain = string.IsNullOrEmpty(StockDrivetrain) ? Drivetrain : $"{Drivetrain} (CarX drivetrain setting; stock {StockDrivetrain})";
			string result = EngineSwap ? "not compared"
				: !Compared ? Note
				: Differences.Count == 0 ? "engine, gearbox, drivetrain and suspension match CarX's own data"
				: $"{Differences.Count} engine, gearbox, drivetrain or suspension value(s) changed outside CarX's tuning (a KINO swap or air suspension, or another mod)";
			return $"{Engine} · {drivetrain} · {result}";
		}
	}

	/// <summary>
	/// The bridge between TRC tune files and CarX itself.
	///
	/// Everything CarX-specific lives behind this interface, so the rest of the
	/// mod (tune files, codes, UI) works and can be tested before the game side
	/// is figured out. It also means the same loader could later target another
	/// game by swapping in a different adapter.
	/// </summary>
	public interface ICarTuneAdapter {
		/// <summary>False until the adapter can talk to the game.</summary>
		bool IsReady { get; }

		/// <summary>Short line shown in the mod menu explaining the adapter's state.</summary>
		string Status { get; }

		/// <summary>The car the player is in: the garage car, or the car they're driving.</summary>
		bool TryGetCurrentCar(out CarInfo car, out string error);

		/// <summary>
		/// The tune CarX applies to this car, as slider positions by setting name. Settings the saved
		/// tune leaves out are filled in with the car's defaults, as CarX does.
		/// </summary>
		bool TryReadCurrentTune(CarInfo car, out Dictionary<string, float> values, out string error);

		/// <summary>The default slider position of every setting a setup slot has.</summary>
		bool TryGetDefaults(int slotId, out Dictionary<string, float> defaults, out string error);

		/// <summary>The numbers CarX's tuning screen shows for these slider positions, by setting name.</summary>
		bool TryDescribe(int slotId, IDictionary<string, float> values, out Dictionary<string, string> text, out string error);

		/// <summary>CarX's name for a car ID.</summary>
		bool TryGetCarName(int carId, out string name);

		/// <summary>Whether a tune can be applied right now, and why not.</summary>
		bool CanApply(CarInfo car, out string reason);

		/// <summary>
		/// Saves the tune into the car's setup and puts it on the car, like CarX's own Setups screen,
		/// then records the car's live values as the reference for the live check.
		/// </summary>
		bool TryApplyTune(CarInfo car, TuneFile tune, out string error);

		/// <summary>
		/// While CarX's tuning screen (dyno) is open: puts the tune's values on its sliders, as if the player
		/// had moved them, and shows them on the car. Nothing is saved: CarX asks to save when the player
		/// leaves the tuning screen, and in an online room CarX itself shares the new setup.
		/// </summary>
		bool TryLoadIntoDyno(CarInfo car, TuneFile tune, out string error);

		/// <summary>
		/// Garage only: has CarX put the car's saved setup back on the car, then records the live values
		/// as the reference for this tune code. Call it only when the saved setup is the official one.
		/// </summary>
		bool TryRecordReference(CarInfo car, string tuneCode, out string error);

		/// <summary>
		/// Compares the car's live physics with the reference recorded for this tune code.
		/// False (with a note) when there's no reference yet.
		/// </summary>
		bool TryCheckLive(CarInfo car, string tuneCode, out List<string> differences, out string note);

		/// <summary>True when a live reference for this car, setup and tune code was recorded this session.</summary>
		bool HasReference(CarInfo car, string tuneCode);

		/// <summary>
		/// On the track: records the car CarX built there as the live reference for this tune code (for a car
		/// that went straight to the track, like joining a room or swapping car in one). Call it only when the
		/// saved setup is the official one, shortly after the car arrives.
		/// </summary>
		bool TryRecordReferenceOnTrack(CarInfo car, string tuneCode, out string error);

		/// <summary>Everyone in the online room, including the player, with the setups CarX shares.</summary>
		bool TryGetRoomPlayers(out List<RoomPlayer> players, out string error);

		/// <summary>
		/// How the car's body parts change its stance (track width, axle positions, ride height) compared
		/// with the stock body. Widebody kits do this without touching the tune. Empty when it's stock.
		/// </summary>
		bool TryCheckStance(CarInfo car, out List<string> differences, out string error);

		/// <summary>The Steam Workshop (or mod.io) bodies on the player's car. Empty when it has none.</summary>
		bool TryGetWorkshopItems(CarInfo car, out List<WorkshopItem> items, out string error);

		/// <summary>
		/// The player's live engine, gearbox and drivetrain against CarX's own data for the setup with this
		/// tune on it (slider positions by setting name, like <see cref="TryReadCurrentTune"/> gives).
		/// </summary>
		bool TryCheckDrivetrain(CarInfo car, IDictionary<string, float> tune, out DrivetrainCheck check, out string error);

		/// <summary>Where the player's own car is while driving (false in the garage and menus). Called every frame, so it never logs.</summary>
		bool TryGetCarPose(out CarPose pose);

		/// <summary>The map the player is driving on.</summary>
		bool TryGetMap(out MapInfo map, out string error);

		/// <summary>
		/// Moves the player's own car to a point on the map, facing <paramref name="forward"/>, with CarX's own
		/// placement (the one its respawn and teleport-to-player use), under the same rules as CarX's respawn.
		/// </summary>
		bool TryMoveCarTo(Vector3 position, Vector3 forward, out string error);

		/// <summary>
		/// Whether any part of the player's car (its middle or a corner) is above one of these road colliders
		/// (instance IDs), on the road or in the air over it. False when not driving. Called every frame.
		/// </summary>
		bool TryCheckOverRoad(ICollection<int> roadColliders, out bool overRoad);
	}

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

		public bool TryCheckDrivetrain(CarInfo car, IDictionary<string, float> tune, out DrivetrainCheck check, out string error) {
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

		public bool TryCheckOverRoad(ICollection<int> roadColliders, out bool overRoad) {
			overRoad = false;
			return false;
		}
	}
}
