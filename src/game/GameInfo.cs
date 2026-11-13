// What the CarX connection reports: the car, its pose, the map and its icons, room players, Workshop bodies, the drivetrain check.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>Where the player's car is and how fast it goes, while driving.</summary>
	/// <summary>Nov 3: an icon on CarX's minimap and big map (a start, finish, sector or speed trap).</summary>
	public enum MapIconShape {
		Dot,
		/// <summary>A bar across the road (start and finish lines), turned to <see cref="MapIcon.Heading"/>.</summary>
		Line,
		/// <summary>A little camera (speed traps).</summary>
		Camera,
	}

	public struct MapIcon {
		public Vector3 Position;
		public Color Color;

		/// <summary>In the map's pixels.</summary>
		public int Size;
		public MapIconShape Shape;

		/// <summary>The way you drive through it (lines); zero for none.</summary>
		public Vector3 Heading;
	}

	public struct CarPose {
		public Vector3 Position;
		public Vector3 Forward;
		public float SpeedKmh;
		public int CarId;

		/// <summary>The car's rotation (for ghosts).</summary>
		public Quaternion Rotation;
	}

	/// <summary>Where another player's car is, as CarX shows it here (for console players the host times).</summary>
	public struct RoomCarPose {
		/// <summary>What CarX calls the player ("&lt;account id&gt;|&lt;platform&gt;").</summary>
		public string Key;
		public string Name;
		public string Platform;
		public bool IsConsole;
		public CarPose Pose;
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

		/// <summary>0.16: what CarX calls them ("&lt;account id&gt;|&lt;platform&gt;"), their platform ("Steam", "PS4", "Xbox"...), and whether that's a console.</summary>
		public string Key = "";
		public string Platform = "";
		public bool IsConsole;

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
	/// changed them (KINO's engine or gearbox swaps, air suspension or weight menu, another mod, an editor) shows as differences.
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

		/// <summary>0.16: the car's peak power (hp) and weight (kg) as built, from CarX's physics; 0 when unknown.</summary>
		public float Hp;
		public float Mass;

		/// <summary>False when the values couldn't be compared (see <see cref="Note"/>).</summary>
		public bool Compared;
		public string Note = "";

		public bool Stock => !EngineSwap && Differences.Count == 0;

		public string Summary() {
			string drivetrain = string.IsNullOrEmpty(StockDrivetrain) ? Drivetrain : $"{Drivetrain} (CarX drivetrain setting; stock {StockDrivetrain})";
			string result = EngineSwap ? "not compared"
				: !Compared ? Note
				: Differences.Count == 0 ? "engine, gearbox, drivetrain, suspension and weight match CarX's own data"
				: $"{Differences.Count} engine, gearbox, drivetrain, suspension or weight value(s) changed outside CarX's tuning (a KINO swap, air suspension or weight menu, or another mod)";
			return $"{Engine} · {drivetrain} · {result}";
		}
	}
}
