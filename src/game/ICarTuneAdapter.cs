// What the mod asks of CarX (the interface): everything goes through it, so a CarX update only ever breaks src/game/.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TRC.TuneLoader {
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
		/// Just who's in the room, with nothing checked (cheap enough to ask every second): each player's Steam ID (0 when
		/// they aren't on Steam) and what CarX calls them (the same text as <see cref="RoomPlayer.Key"/>). False when not in a room.
		/// </summary>
		bool TryGetRoomKeys(List<(ulong SteamId, string Key)> members);

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
		/// <paramref name="withPhysics"/>: also list the physics engine's own values for the log (only the Check button's
		/// check writes them; the comparison is the same either way).
		/// </summary>
		bool TryCheckDrivetrain(CarInfo car, IDictionary<string, float> tune, bool withPhysics, out DrivetrainCheck check, out string error);

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

		/// <summary>
		/// Nov 2: how wide these road colliders (instance IDs) are across <paramref name="centre"/>, along
		/// <paramref name="right"/> (metres), by looking straight down every 25 cm out to each side. False when the
		/// road isn't under the centre or the map isn't loaded.
		/// </summary>
		bool TryMeasureRoadWidth(Vector3 centre, Vector3 right, ICollection<int> roadColliders, out float width);

		/// <summary>
		/// Nov 3 (bskftw: "you can see where players on a map are, why cant we add icons for all of these too"): shows
		/// these icons on CarX's minimap and big map, the same way CarX shows its own (and takes away any shown before).
		/// False when this map has no CarX minimap.
		/// </summary>
		bool TryShowMapIcons(IList<MapIcon> icons);

		/// <summary>
		/// Every frame while icons show: sizes them for the map they're on (bskftw, Nov 3: smaller on the full map in
		/// the menu, a bit bigger on the minimap while driving). CarX gives an icon one size for every map.
		/// </summary>
		void SizeMapIcons();

		/// <summary>
		/// 0.16: the numbers CarX's tuning screen shows for these slider positions (mm, Nm, degrees...), as numbers, and how
		/// many decimals it shows each with (a tyre stretch of 10.3 shows as "10": 0 decimals).
		/// </summary>
		bool TryGetTuneNumbers(int slotId, IDictionary<string, float> values, out Dictionary<string, float> numbers, out Dictionary<string, int> decimals, out string error);

		/// <summary>0.16: where every other player's car is, as CarX shows it here. Called every frame while timing console players.</summary>
		bool TryGetRoomCarPoses(List<RoomCarPose> poses);

		/// <summary>
		/// 0.16: turns the player's own headlights on or off the way the light key does, so CarX shares it with the room
		/// (console players see the host's lights count the start down). Only the player's own car.
		/// </summary>
		bool TrySetHeadlights(bool on, out string error);

		/// <summary>The player's own headlights are on now (false when not driving).</summary>
		bool HeadlightsOn { get; }

		/// <summary>
		/// 0.16: a copy of the player's car body for a ghost: only meshes drawn with <paramref name="material"/>, with no
		/// collider, rigidbody or script, so it can't touch anything. Placed like the car's body; null when not driving.
		/// </summary>
		GameObject BuildGhostBody(Material material);

		/// <summary>
		/// 0.16: the player's own four wheels right now (compression, tyre load, contact, slip), for suspension logging;
		/// false when not driving. Called every frame while logging is on, so it never logs.
		/// </summary>
		bool TryGetSuspension(SuspensionFrame frame);

		/// <summary>0.16: the player's own name as CarX knows it (their Steam nickname), "" when unknown.</summary>
		string PlayerName { get; }

		/// <summary>0.16: the player's own car's suspension setup per corner (travel, spring, dampers and their limits, corner weight).</summary>
		bool TryGetSuspensionSetup(out CornerSetup[] corners);

		/// <summary>
		/// Stage 2.2: your own car's inputs, gear and RPM, g-force and wheel slip right now, for the telemetry overlay; false
		/// when not driving. Called every frame while the overlay is on, so it logs only its first failure.
		/// </summary>
		bool TryGetTelemetry(TelemetryFrame frame);
	}
}
