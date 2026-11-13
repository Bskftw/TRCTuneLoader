// Finds the courses a map maker placed in the map (TRC Start, TRC Finish, TRC Path... objects). Names are in MAP_MAKERS.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Finds courses a map maker placed in the map itself, as objects named like this (case, spaces and
	/// underscores don't matter):
	///
	///   TRC Start Akina Downhill    the start line: the middle of the road on the line, its blue (Z) arrow
	///                               pointing the way you drive, its X scale the road width in metres
	///   TRC Finish Akina Downhill   the finish line, set up the same way
	///   TRC Path Akina              optional: a strip of faces one face across down the middle of the road (flat,
	///                               or a line extruded up or down), with Read/Write enabled on its model and its renderer
	///                               off, or child objects in order. Without one, the course gets its path from
	///                               driving it once with Record path (a .trccourse file). Named without the
	///                               direction, it's every "Akina ..." course's path, read from that course's
	///                               start line; "TRC Path Akina Downhill" is that course's own.
	///   TRC Mainroad Akina          the road's mesh (with its collider); split roads are numbered like
	///   TRC Mainroad Akina.001      "TRC Mainroad Akina.001". Every course whose name starts with "Akina" runs
	///                               on it, and driving off it stops a time attack.
	///   TRC Akina Sector 1          optional: where a sector ends (the last one ends at the finish). They're put
	///                               in order along each course's path, so the numbers only have to differ, and
	///                               without the direction they're shared by both directions, like the path.
	///                               "TRC Akina Downhill Sector 1" is that course's own. Without any, the course
	///                               is split evenly.
	///   TRC Start Tsukuba Circuit   a circuit: a loop whose start line is also its finish line, so it has no
	///                               finish object. Its path: "TRC Path Tsukuba Circuit" (a loop), or Record path
	///                               (once round). A map with more layouts numbers them: "TRC Start Okutama Circuit 1",
	///                               "TRC Path Okutama Circuit 2". A path named without "Circuit" is the circuit's
	///                               only when no downhill or uphill on that road uses it.
	///   TRC Akina Downhill Grid 1   optional: race grid spots, 1 (pole) to 16, each in the middle of where a car
	///                               lines up, its blue (Z) arrow pointing the way the cars face.
	///   TRC Akina Downhill Trap 1   optional: speed traps along the course, numbered in driving order, set up like a
	///                               start line (blue arrow the way you drive, X scale the road width).
	///   TRC Speed Trap Akina Bridge a free-roam speed trap anywhere on the map, set up the same way (either way counts).
	///
	/// A course whose name ends in "Downhill" also gets an uphill twin ("Akina Uphill") with the start and
	/// finish swapped, unless the map has its own "TRC Start Akina Uphill". A circuit gets a reverse twin
	/// ("Tsukuba Circuit Reverse") the same way. A course with "Circuit" in its name is a circuit, one with
	/// "Highway" a highway course; any other is touge.
	/// </summary>
	public static partial class CourseScanner {
		private const string StartWord = "start";
		private const string FinishWord = "finish";
		private const string PathWord = "path";
		private const string RoadWord = "mainroad";
		private const string DownhillWord = "downhill";
		private const string UphillWord = "uphill";
		private const string SectorWord = "sector";
		private const string GridWord = "grid";
		private const string TrapWord = "trap";
		private const string CircuitWord = "circuit";
		private const string ReverseWord = "reverse";

		/// <summary>Path points closer than this (metres) are one point: the two edges of the strip.</summary>
		private const float MergeDistance = 1.2f;

		/// <summary>The path can't have a gap bigger than this (metres) between two of its points.</summary>
		private const float MaxPathGap = 25f;

		private sealed class Parts {
			public string Name = "";
			public Transform Start;
			public Transform Finish;
			public int StartCopy;
			public int FinishCopy;
			/// <summary>The path's pieces with their copy numbers ("TRC Path Akina.001" is 1): all of them are read.</summary>
			public List<KeyValuePair<int, Transform>> PathPieces = new List<KeyValuePair<int, Transform>>();
			/// <summary>The copy number of each numbered object kept (sectors, grid spots, traps).</summary>
			public Dictionary<Transform, int> Copies = new Dictionary<Transform, int>();
			public SortedDictionary<int, Transform> Sectors = new SortedDictionary<int, Transform>();
			public SortedDictionary<int, Transform> Grid = new SortedDictionary<int, Transform>();
			public SortedDictionary<int, Transform> Traps = new SortedDictionary<int, Transform>();
			public List<string> Duplicates = new List<string>();
		}

		/// <summary>
		/// Every TRC course in the loaded map. <paramref name="summary"/> says what was found, for the log and the Courses tab.
		/// <paramref name="saved"/>: paths remembered from earlier loads, used where nothing changed and given the new ones
		/// (null reads every path).
		/// </summary>
		public static List<Course> Scan(string mapKey, string mapName, out string summary, SavedPaths saved = null) {
			var timer = Stopwatch.StartNew();
			Scans++;
			stripCentres_.Clear();
			StartPathTimers(saved);
			SpeedTraps.Clear();
			Lanes.Clear();
			LaneProblems.Clear();
			LanesMapKey = mapKey;
			var laneObjects = new List<(string Name, int Copy, Transform Transform)>();
			var trapCopies = new List<(string Name, int Copy, SpeedTrap Trap)>();
			var found = new Dictionary<string, Parts>(StringComparer.Ordinal);
			var roads = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
			var roadNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			var roadsWithoutCollider = new List<(string Name, string Key)>();
			var unmatched = new List<string>();
			int objects = 0;

			foreach (Transform transform in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
				objects++;
				string name = transform.name;
				if (name.Length < 7 || (name.IndexOf("trc", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("road", StringComparison.OrdinalIgnoreCase) < 0)) {
					continue;
				}

				// "TRC Mainroad Akina.001" or "TRC Path Akina (1)": a copy, from Blender or Unity. The name without the number is
				// the same object; the unnumbered one comes first (bskftw, Oct 30).
				string bare = WithoutCopyNumber(name, out int copy);
				string[] words = Words(bare);
				// "TRC Speed Trap Akina Bridge": a free-roam speed trap.
				int trcAt = Array.IndexOf(words, "trc");
				if (trcAt >= 0 && trcAt + 3 < words.Length && words[trcAt + 1] == "speed" && words[trcAt + 2] == TrapWord) {
					float trapWidth = Mathf.Abs(transform.lossyScale.x);
					if (CourseGate.TryMake(transform.position, transform.forward, trapWidth > 2f ? trapWidth : CourseGeometry.DefaultWidth, out CourseGate gate)) {
						var trap = new SpeedTrap { Name = DisplayName(bare, trcAt + 3), Gate = gate, MapKey = mapKey, Source = CourseSource.Map };
						SpeedTraps.Add(trap);
						trapCopies.Add((trap.Name, copy, trap));
					}
					continue;
				}
				// "TRC Lane Highway - Left 1 [speed 80]": a traffic lane (Nov 8), read after the loop.
				if (trcAt >= 0 && trcAt + 2 < words.Length && words[trcAt + 1] == "lane") {
					laneObjects.Add((bare, copy, transform));
					continue;
				}
				foreach (string numbered in new[] { SectorWord, GridWord, TrapWord }) {
					if (!TryParseNumbered(words, numbered, out string ownerKey, out int number)) {
						continue;
					}
					if (!found.TryGetValue(ownerKey, out Parts owner)) {
						found[ownerKey] = owner = new Parts { Name = ownerKey };
					}
					SortedDictionary<int, Transform> list = numbered == SectorWord ? owner.Sectors : numbered == GridWord ? owner.Grid : owner.Traps;
					if (!list.TryGetValue(number, out Transform kept)) {
						list[number] = transform;
						owner.Copies[transform] = copy;
					} else {
						bool replace = copy < owner.Copies[kept];
						owner.Duplicates.Add($"'{(replace ? kept.name : name)}' is a second '{bare}': '{(replace ? name : kept.name)}' is used.");
						if (replace) {
							list[number] = transform;
							owner.Copies[transform] = copy;
						}
					}
					name = null;
					break;
				}
				if (name == null) {
					continue;
				}
				if (!TryParse(words, out string role, out int rest)) {
					// Anything with TRC in its name that isn't understood goes to the log, to help find a naming slip.
					if (name.IndexOf("trc", StringComparison.OrdinalIgnoreCase) >= 0) {
						unmatched.Add(name);
					}
					continue;
				}

				if (role == RoadWord) {
					string roadKey = RoadKey(words, rest);
					AddRoad(roads, roadsWithoutCollider, roadKey, transform);
					if (!roadNames.TryGetValue(roadKey, out var names)) {
						roadNames[roadKey] = names = new List<string>();
					}
					names.Add(name);
					continue;
				}

				string key = string.Join(" ", words.Skip(rest));
				if (!found.TryGetValue(key, out Parts parts)) {
					found[key] = parts = new Parts { Name = DisplayName(bare, rest) };
				}
				if (role == StartWord) {
					Keep(ref parts.Start, ref parts.StartCopy, transform, copy, bare, parts);
					parts.Name = DisplayName(bare, rest);
				} else if (role == FinishWord) {
					Keep(ref parts.Finish, ref parts.FinishCopy, transform, copy, bare, parts);
				} else {
					// Every piece of the path is read: a long road can be split into "TRC Path Akina", "TRC Path Akina.001", ...
					parts.PathPieces.Add(new KeyValuePair<int, Transform>(copy, transform));
				}
			}

			// What was found, for the log: every TRC road and its pieces, and TRC names that weren't understood.
			Lines.Clear();
			foreach (var road in roadNames.OrderBy(r => r.Key, StringComparer.Ordinal)) {
				Lines.Add($"TRC road '{road.Key}': {road.Value.Count} object(s), {roads[road.Key].Count} collider(s): {string.Join(", ", road.Value.Take(10))}{(road.Value.Count > 10 ? ", ..." : "")}");
			}
			if (unmatched.Count > 0) {
				Lines.Add($"{unmatched.Count} other object(s) with TRC in the name, not understood: {string.Join(", ", unmatched.Distinct().Take(40).Select(n => $"'{n}'"))}");
			}

			NumberCopies(trapCopies);
			ShareUndirected(found);

			var courses = new List<Course>();
			foreach (var pair in found.OrderBy(p => p.Value.Name, StringComparer.OrdinalIgnoreCase)) {
				Course course = Build(pair.Value, mapKey, mapName);
				AttachRoad(course, pair.Key, roads, roadsWithoutCollider);
				courses.Add(course);

				// "Akina Downhill" also gives "Akina Uphill": the same lines the other way round.
				string[] words = pair.Key.Split(' ');
				if (words.Length >= 2 && words[words.Length - 1] == DownhillWord) {
					string uphillKey = string.Join(" ", words.Take(words.Length - 1)) + " " + UphillWord;
					if (!found.ContainsKey(uphillKey)) {
						string name = course.Name.Substring(0, course.Name.Length - DownhillWord.Length) + "Uphill";
						courses.Add(course.ReversedAs(name));
					}
				}
				// "Tsukuba Circuit" also gives "Tsukuba Circuit Reverse": the same line, driven round the other way.
				if (course.IsCircuit && words[words.Length - 1] != ReverseWord && !found.ContainsKey(pair.Key + " " + ReverseWord)) {
					courses.Add(course.ReversedAs(course.Name + " Reverse"));
				}
			}

			foreach (Course course in courses) {
				string grid = course.GridWarning();
				if (grid.Length > 0) {
					course.Problems.Add(grid);
				}
			}

			ReadLanes(laneObjects);
			if (Lanes.Count > 0 || LaneProblems.Count > 0) {
				Lines.Add($"  {Lanes.Count} traffic lane(s) from TRC Lane objects.");
				foreach (string problem in LaneProblems) {
					Lines.Add($"    {problem}");
				}
			}

			timer.Stop();
			stripCentres_.Clear();
			Lines.Add($"  Reading paths took {pathTimer_.ElapsedMilliseconds} ms of the {timer.ElapsedMilliseconds} ms ({PathTimeDetails()}).");
			EndPathTimers();
			string others = unmatched.Count > 0 ? $" {unmatched.Count} other object(s) have TRC in the name but weren't understood (listed in the log)." : "";
			summary = courses.Count == 0
				? $"No TRC course objects in this map ({objects} objects looked at in {timer.ElapsedMilliseconds} ms).{others}"
				: $"{courses.Count} course(s) and {roads.Count} TRC road(s) from TRC objects in this map ({objects} objects looked at in {timer.ElapsedMilliseconds} ms).{others}";
			return courses;
		}

		/// <summary>The map's free-roam speed traps ("TRC Speed Trap &lt;name&gt;") from the last scan.</summary>
		public static List<SpeedTrap> SpeedTraps { get; } = new List<SpeedTrap>();

		/// <summary>Details of the last scan for the log: the TRC roads found and TRC names that weren't understood.</summary>
		public static List<string> Lines { get; } = new List<string>();
	}
}
