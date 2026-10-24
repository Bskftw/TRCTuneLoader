using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>Touge and highway run from a start line to a finish line; a circuit is a loop whose finish line is its start line.</summary>
	public enum CourseKind { Touge, Highway, Circuit }

	/// <summary>
	/// Where a course comes from. Official courses are built into the mod (only bskftw ships them);
	/// map courses come from TRC-named objects in the map itself; custom courses were made with the
	/// placement tool on this PC and only count for practice.
	/// </summary>
	public enum CourseSource { Official, Map, Custom }

	/// <summary>
	/// A start or finish line: a line across the road through <see cref="Center"/>, <see cref="Width"/>
	/// wide, crossed in the <see cref="Forward"/> direction.
	/// </summary>
	public sealed class CourseGate {
		public Vector3 Center;

		/// <summary>The way you drive through the line, level (no up or down) and one metre long.</summary>
		public Vector3 Forward = Vector3.forward;
		public float Width = CourseGeometry.DefaultWidth;

		/// <summary>Level and to the right of <see cref="Forward"/>.</summary>
		public Vector3 Right => new Vector3(Forward.z, 0f, -Forward.x);
		public Vector3 LeftEnd => Center - Right * (Width / 2f);
		public Vector3 RightEnd => Center + Right * (Width / 2f);

		/// <summary>How far past the line a point is, along <see cref="Forward"/>; negative = behind it.</summary>
		public float Ahead(Vector3 point) => Vector3.Dot(point - Center, Forward);

		/// <summary>How far right of the middle of the line a point is; negative = left.</summary>
		public float Across(Vector3 point) => Vector3.Dot(point - Center, Right);

		/// <summary>A gate whose direction is <paramref name="direction"/> made level. False when it points straight up or down.</summary>
		public static bool TryMake(Vector3 center, Vector3 direction, float width, out CourseGate gate) {
			var level = new Vector3(direction.x, 0f, direction.z);
			gate = new CourseGate { Center = center, Width = width > 0.5f ? width : CourseGeometry.DefaultWidth };
			if (level.magnitude < 0.1f) {
				return false;
			}
			gate.Forward = level.normalized;
			return true;
		}
	}

	/// <summary>
	/// A course for time attack and battles: a start line, a finish line and a path along the middle of the
	/// road between them. The path lets the mod tell how far along each car is, and catch shortcuts.
	/// </summary>
	public sealed class Course {
		public string Name = "";
		public CourseKind Kind;
		public CourseSource Source;

		/// <summary>The map the course is on, like "workshop:123456" or "carx:7" (see MapInfo).</summary>
		public string MapKey = "";
		public string MapName = "";
		public string Author = "";

		/// <summary>The .trccourse file (custom courses) or resource (official courses) it was read from.</summary>
		public string FilePath = "";

		public CourseGate Start = new CourseGate();
		public CourseGate Finish = new CourseGate();

		/// <summary>Points along the middle of the road from the start line to the finish line, every <see cref="CourseGeometry.Step"/> m.</summary>
		public List<Vector3> Path = new List<Vector3>();

		/// <summary>False when the course has no path of its own: a straight line stands in, so shortcuts can't be caught.</summary>
		public bool HasPath;

		/// <summary>Problems found while reading or building the course, in plain words.</summary>
		public List<string> Problems = new List<string>();

		/// <summary>Map courses only: the road it runs on, from "TRC Mainroad &lt;key&gt;" objects ("" when the map has none for it).</summary>
		public string RoadKey = "";

		/// <summary>The road objects' colliders (instance IDs, this session only). Driving off them stops a time attack.</summary>
		public HashSet<int> RoadColliders = new HashSet<int>();

		/// <summary>Map courses only: the start and finish objects themselves, so hidden signs can be shown during a run.</summary>
		public Transform StartObject;
		public Transform FinishObject;

		/// <summary>The path was recorded on this PC (Record path), not built into the mod or the map: practice only.</summary>
		public bool PathFromPlayer;

		/// <summary>An uphill course made from a downhill one by swapping its start and finish.</summary>
		public bool Reversed;

		/// <summary>
		/// Map courses only: where the map's "TRC &lt;course&gt; Sector N" objects are, in order. The sectors end there
		/// (and at the finish); without any, the course is split evenly (<see cref="SectorEnds"/>).
		/// </summary>
		public List<Vector3> SectorPoints = new List<Vector3>();

		/// <summary>A loop: the finish line is the start line, driven again after a lap.</summary>
		public bool IsCircuit => Kind == CourseKind.Circuit;

		/// <summary>Metres along the path where each sector ends, the last one at <see cref="Length"/> (worked out in Build).</summary>
		public float[] SectorEnds { get; private set; } = new float[0];

		/// <summary>Distance along the path at each path point, in metres.</summary>
		public float[] Distances { get; private set; } = new float[0];
		public float Length { get; private set; }

		/// <summary>A short code made from the map, the kind, both lines and the path, like "CRS-1A2B-3C4D".</summary>
		public string Code { get; private set; } = "";

		/// <summary>Usable for time attack: both lines are set and the course has some length.</summary>
		public bool Usable => Path.Count >= 2 && Length >= MinLength;

		/// <summary>The shortest a course of this kind can be: a circuit needs a real loop.</summary>
		public float MinLength => IsCircuit ? CourseGeometry.CircuitMinLength : CourseGeometry.MinLength;

		/// <summary>
		/// Counts for leaderboards: built into the mod, or objects in a map whose objects TRC trusts, with a real path
		/// that came with the mod or the map (not one recorded on this PC).
		/// </summary>
		public bool Official => HasPath && !PathFromPlayer &&
			(Source == CourseSource.Official || (Source == CourseSource.Map && CourseLibrary.OfficialMaps.Contains(MapKey)));

		public string KindName => KindNames[(int)Kind];

		public static readonly string[] KindNames = { "Touge", "Highway", "Circuit" };

		/// <summary>The lowercase word for a kind, as files and course codes write it.</summary>
		public static string KindWord(CourseKind kind) => kind == CourseKind.Highway ? "highway" : kind == CourseKind.Circuit ? "circuit" : "touge";

		/// <summary>
		/// The same course lines as <paramref name="other"/>: same map, start and finish within <paramref name="metres"/>,
		/// started the same way (so a reversed circuit, which starts on the same line, isn't the same).
		/// </summary>
		public bool SameLines(Course other, float metres = 1f) =>
			other != null && MapKey == other.MapKey &&
			Vector3.Distance(Start.Center, other.Start.Center) <= metres && Vector3.Distance(Finish.Center, other.Finish.Center) <= metres &&
			Vector3.Dot(Start.Forward, other.Start.Forward) > 0f;

		/// <summary>The lines of <paramref name="other"/> the other way round: what a reversed (uphill) twin of it has.</summary>
		public bool ReversedLinesOf(Course other, float metres = 1f) =>
			other != null && MapKey == other.MapKey &&
			Vector3.Distance(Start.Center, other.Finish.Center) <= metres && Vector3.Distance(Finish.Center, other.Start.Center) <= metres &&
			Vector3.Dot(Start.Forward, other.Finish.Forward) < 0f;

		public string SourceName =>
			Source == CourseSource.Official ? "Official TRC course"
			: Source == CourseSource.Custom ? "Your course (practice)"
			: Official ? "Official (map objects)"
			: CourseLibrary.OfficialMaps.Contains(MapKey) && HasPath ? "Map objects, your path (practice)"
			: CourseLibrary.OfficialMaps.Contains(MapKey) ? "Map objects, no path yet"
			: "Map objects (not official)";

		/// <summary>
		/// The same road the other way: the finish becomes the start and the start the finish, both crossed the
		/// other way, and the path runs backwards. Used for uphill courses made from downhill ones.
		/// </summary>
		public Course ReversedAs(string name) {
			var reversed = new Course {
				Name = name,
				Kind = Kind,
				Source = Source,
				MapKey = MapKey,
				MapName = MapName,
				Author = Author,
				FilePath = FilePath,
				Start = new CourseGate { Center = Finish.Center, Forward = -Finish.Forward, Width = Finish.Width },
				Finish = new CourseGate { Center = Start.Center, Forward = -Start.Forward, Width = Start.Width },
				HasPath = HasPath,
				RoadKey = RoadKey,
				RoadColliders = RoadColliders,
				StartObject = FinishObject,
				FinishObject = StartObject,
				PathFromPlayer = PathFromPlayer,
				Reversed = true,
				SectorPoints = new List<Vector3>(SectorPoints),
			};
			reversed.SectorPoints.Reverse();
			reversed.Path = new List<Vector3>(Path);
			reversed.Path.Reverse();
			reversed.Problems.AddRange(Problems);
			reversed.Build(resample: false);
			return reversed;
		}

		/// <summary>Gives a map course a path recorded by driving it (Record path), and works out its length and code again.</summary>
		public void UsePath(List<Vector3> path, bool fromPlayer) {
			Path = new List<Vector3>(path);
			HasPath = Path.Count >= 2;
			PathFromPlayer = fromPlayer;
			Problems.RemoveAll(p => p.StartsWith(NoPathProblem, StringComparison.Ordinal));
			Build(resample: false);
		}

		/// <summary>The start of the problem a map course without a path shows.</summary>
		public const string NoPathProblem = "No path yet";

		/// <summary>
		/// Evens out the path (from the start line to the finish line, every <see cref="CourseGeometry.Step"/> m,
		/// unless <paramref name="resample"/> is false because it already is, as in a saved file), then works out
		/// the distances, length and code. Without a path, a straight line stands in.
		///
		/// Every number is rounded the way a .trccourse file writes it first, so a course gives the same code
		/// before it's saved and after it's read back.
		/// </summary>
		public void Build(bool resample = true) {
			RoundGate(Start);
			if (IsCircuit) {
				Finish = new CourseGate { Center = Start.Center, Forward = Start.Forward, Width = Start.Width };
			}
			RoundGate(Finish);

			var points = new List<Vector3>();
			if (HasPath && Path.Count >= 2) {
				points.AddRange(Path);
			} else {
				HasPath = false;
			}

			// The path always runs from the middle of the start line to the middle of the finish line.
			if (points.Count == 0 || Vector3.Distance(points[0], Start.Center) > 0.05f) {
				points.Insert(0, Start.Center);
			}
			if (Vector3.Distance(points[points.Count - 1], Finish.Center) > 0.05f) {
				points.Add(Finish.Center);
			}

			Path = resample || !HasPath ? CourseGeometry.Resample(points, CourseGeometry.Step) : points;
			for (int i = 0; i < Path.Count; i++) {
				Path[i] = RoundPoint(Path[i]);
			}
			Distances = CourseGeometry.Cumulative(Path);
			Length = Distances.Length > 0 ? Distances[Distances.Length - 1] : 0f;
			SectorEnds = CourseGeometry.SectorEnds(Path, Distances, SectorPoints);
			Code = CourseCode.Compute(this);
		}

		private static void RoundGate(CourseGate gate) {
			gate.Center = RoundPoint(gate.Center);
			gate.Forward = new Vector3(Rounded(gate.Forward.x, 4), 0f, Rounded(gate.Forward.z, 4));
			gate.Width = Rounded(gate.Width, 1);
		}

		/// <summary>To the centimetre, as a .trccourse file writes positions.</summary>
		private static Vector3 RoundPoint(Vector3 point) => new Vector3(Rounded(point.x, 2), Rounded(point.y, 2), Rounded(point.z, 2));

		private static float Rounded(float value, int decimals) => (float)Math.Round((double)value, decimals, MidpointRounding.AwayFromZero);
	}

	/// <summary>
	/// The course code: SHA-256 of the map, the kind, both lines and the path, rounded so float noise
	/// doesn't change it. Two drivers with the same code are on exactly the same course.
	/// </summary>
	public static class CourseCode {
		public static string Compute(Course course) {
			var text = new StringBuilder();
			text.Append("map=").Append(course.MapKey ?? "").Append('\n');
			text.Append("kind=").Append(Course.KindWord(course.Kind)).Append('\n');
			AppendGate(text, "start", course.Start);
			AppendGate(text, "finish", course.Finish);
			text.Append("path=").Append(course.HasPath ? course.Path.Count.ToString(CultureInfo.InvariantCulture) : "none").Append('\n');
			if (course.HasPath) {
				foreach (Vector3 point in course.Path) {
					text.Append(Round(point.x, 1)).Append(',').Append(Round(point.y, 1)).Append(',').Append(Round(point.z, 1)).Append('\n');
				}
			}

			byte[] hash;
			using (var sha = SHA256.Create()) {
				hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
			}
			string hex = BitConverter.ToString(hash, 0, 4).Replace("-", "");
			return $"CRS-{hex.Substring(0, 4)}-{hex.Substring(4, 4)}";
		}

		private static void AppendGate(StringBuilder text, string name, CourseGate gate) {
			text.Append(name).Append('=')
				.Append(Round(gate.Center.x, 1)).Append(',').Append(Round(gate.Center.y, 1)).Append(',').Append(Round(gate.Center.z, 1)).Append(',')
				.Append(Round(gate.Forward.x, 3)).Append(',').Append(Round(gate.Forward.z, 3)).Append(',')
				.Append(Round(gate.Width, 1)).Append('\n');
		}

		/// <summary>Rounded half away from zero, "-0" printed as "0", invariant culture.</summary>
		public static string Round(float value, int decimals) {
			double rounded = Math.Round((double)value, decimals, MidpointRounding.AwayFromZero);
			if (rounded == 0) {
				rounded = 0;
			}
			return rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
		}
	}
}
