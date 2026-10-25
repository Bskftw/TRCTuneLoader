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
	///   TRC Path Akina Downhill     optional: a thin strip (under 1 m wide) modelled down the middle of the road
	///                               from start to finish, with Read/Write enabled on its model and its renderer
	///                               off, or child objects in driving order. Without one, the course gets its
	///                               path from driving it once with Record path (a .trccourse file).
	///   TRC Mainroad Akina          the road's mesh (with its collider); split roads are numbered like
	///   TRC Mainroad Akina.001      "TRC Mainroad Akina.001". Every course whose name starts with "Akina" runs
	///                               on it, and driving off it stops a time attack.
	///   TRC Akina Downhill Sector 1 optional: where sector 1 ends (sector 2 at "... Sector 2", and so on; the
	///                               last sector ends at the finish). Without them the course is split evenly.
	///   TRC Start Tsukuba Circuit   a circuit: a loop whose start line is also its finish line, so it has no
	///                               finish object. Its path comes from Record path (once round).
	///   TRC Akina Downhill Grid 1   optional: race grid spots, 1 (pole) to 16, each in the middle of where a car
	///                               lines up, its blue (Z) arrow pointing the way the cars face.
	///
	/// A course whose name ends in "Downhill" also gets an uphill twin ("Akina Uphill") with the start and
	/// finish swapped, unless the map has its own "TRC Start Akina Uphill". A circuit gets a reverse twin
	/// ("Tsukuba Circuit Reverse") the same way. A course with "Circuit" in its name is a circuit, one with
	/// "Highway" a highway course; any other is touge.
	/// </summary>
	public static class CourseScanner {
		private const string StartWord = "start";
		private const string FinishWord = "finish";
		private const string PathWord = "path";
		private const string RoadWord = "mainroad";
		private const string DownhillWord = "downhill";
		private const string UphillWord = "uphill";
		private const string SectorWord = "sector";
		private const string GridWord = "grid";
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
			public Transform Path;
			public SortedDictionary<int, Transform> Sectors = new SortedDictionary<int, Transform>();
			public SortedDictionary<int, Transform> Grid = new SortedDictionary<int, Transform>();
			public List<string> Duplicates = new List<string>();
		}

		/// <summary>Every TRC course in the loaded map. <paramref name="summary"/> says what was found, for the log and the Courses tab.</summary>
		public static List<Course> Scan(string mapKey, string mapName, out string summary) {
			var timer = Stopwatch.StartNew();
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

				string[] words = Words(name);
				foreach (string numbered in new[] { SectorWord, GridWord }) {
					if (!TryParseNumbered(words, numbered, out string ownerKey, out int number)) {
						continue;
					}
					if (!found.TryGetValue(ownerKey, out Parts owner)) {
						found[ownerKey] = owner = new Parts { Name = ownerKey };
					}
					SortedDictionary<int, Transform> list = numbered == SectorWord ? owner.Sectors : owner.Grid;
					if (list.ContainsKey(number)) {
						owner.Duplicates.Add($"Two objects are named '{name}': the first one found is used.");
					} else {
						list[number] = transform;
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
					found[key] = parts = new Parts { Name = DisplayName(name, rest) };
				}
				if (role == StartWord) {
					Keep(ref parts.Start, transform, parts);
					parts.Name = DisplayName(name, rest);
				} else if (role == FinishWord) {
					Keep(ref parts.Finish, transform, parts);
				} else {
					Keep(ref parts.Path, transform, parts);
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

			timer.Stop();
			string others = unmatched.Count > 0 ? $" {unmatched.Count} other object(s) have TRC in the name but weren't understood (listed in the log)." : "";
			summary = courses.Count == 0
				? $"No TRC course objects in this map ({objects} objects looked at in {timer.ElapsedMilliseconds} ms).{others}"
				: $"{courses.Count} course(s) and {roads.Count} TRC road(s) from TRC objects in this map ({objects} objects looked at in {timer.ElapsedMilliseconds} ms).{others}";
			return courses;
		}

		/// <summary>Details of the last scan for the log: the TRC roads found and TRC names that weren't understood.</summary>
		public static List<string> Lines { get; } = new List<string>();

		/// <summary>
		/// Finds "trc" and the word after it: start, finish, path, or the road ("mainroad", "main road" or "road").
		/// <paramref name="rest"/> is where the course or road name starts. "TRC" doesn't have to be the first word.
		/// </summary>
		private static bool TryParse(string[] words, out string role, out int rest) {
			role = "";
			rest = 0;
			int trc = Array.IndexOf(words, "trc");
			if (trc < 0 || trc + 2 >= words.Length) {
				return false;
			}
			string next = words[trc + 1];
			if (next == StartWord || next == FinishWord || next == PathWord) {
				role = next;
				rest = trc + 2;
			} else if (next == RoadWord || next == "road" || next == "mainroads") {
				role = RoadWord;
				rest = trc + 2;
			} else if (next == "main" && words[trc + 2] == "road" && trc + 3 < words.Length) {
				role = RoadWord;
				rest = trc + 3;
			} else {
				return false;
			}
			return true;
		}

		/// <summary>"trc akina downhill sector 2" → course "akina downhill", sector 2 (the same for "grid 2").</summary>
		private static bool TryParseNumbered(string[] words, string word, out string key, out int number) {
			key = "";
			number = 0;
			int trc = Array.IndexOf(words, "trc");
			int last = words.Length - 1;
			if (trc < 0 || last - trc < 3 || words[last - 1] != word ||
				!int.TryParse(words[last], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)) {
				return false;
			}
			key = string.Join(" ", words.Skip(trc + 1).Take(last - 1 - (trc + 1)));
			return key.Length > 0;
		}

		/// <summary>"trc mainroad akina.001" → "akina": the road's name without the number Unity or Blender add to split meshes.</summary>
		private static string RoadKey(string[] words, int rest) {
			var key = words.Skip(rest).ToList();
			string last = key[key.Count - 1];
			int dot = last.LastIndexOf('.');
			if (dot > 0 && dot < last.Length - 1 && last.Substring(dot + 1).All(char.IsDigit)) {
				key[key.Count - 1] = last.Substring(0, dot);
			}
			if (key.Count > 1 && key[key.Count - 1].Trim('(', ')').All(char.IsDigit)) {
				key.RemoveAt(key.Count - 1); // "TRC Mainroad Akina (1)", from duplicating in Unity
			}
			return string.Join(" ", key);
		}

		private static void AddRoad(Dictionary<string, HashSet<int>> roads, List<(string Name, string Key)> withoutCollider, string key, Transform transform) {
			if (!roads.TryGetValue(key, out var colliders)) {
				roads[key] = colliders = new HashSet<int>();
			}
			int before = colliders.Count;
			foreach (Collider collider in transform.GetComponentsInChildren<Collider>(true)) {
				colliders.Add(collider.GetInstanceID());
			}
			if (colliders.Count == before) {
				withoutCollider.Add((transform.name, key));
			}
		}

		/// <summary>Gives a course the TRC road whose name its own name starts with (the longest match: "Akina Lake" before "Akina").</summary>
		private static void AttachRoad(Course course, string courseKey, Dictionary<string, HashSet<int>> roads, List<(string Name, string Key)> withoutCollider) {
			string best = roads.Keys
				.Where(k => courseKey == k || courseKey.StartsWith(k + " ", StringComparison.Ordinal))
				.OrderByDescending(k => k.Length)
				.FirstOrDefault();
			if (best == null) {
				return;
			}
			course.RoadKey = best;
			course.RoadColliders = roads[best];
			if (course.RoadColliders.Count == 0) {
				course.Problems.Add($"'TRC Mainroad {best}' has no collider, so leaving the road can't be checked.");
			}
			foreach (string name in withoutCollider.Where(n => n.Key == best).Select(n => n.Name)) {
				course.Problems.Add($"'{name}' has no collider: driving on it counts as off the road.");
			}
		}

		private static Course Build(Parts parts, string mapKey, string mapName) {
			var course = new Course {
				Name = parts.Name,
				Kind = parts.Name.IndexOf(CircuitWord, StringComparison.OrdinalIgnoreCase) >= 0 ? CourseKind.Circuit
					: parts.Name.IndexOf("highway", StringComparison.OrdinalIgnoreCase) >= 0 ? CourseKind.Highway
					: CourseKind.Touge,
				Source = CourseSource.Map,
				MapKey = mapKey,
				MapName = mapName,
				Author = "map objects",
			};
			course.Problems.AddRange(parts.Duplicates);
			course.StartObject = parts.Start;
			course.FinishObject = parts.Finish;

			course.SectorPoints = parts.Sectors.Values.Select(t => t.position).ToList();
			course.GridObjects = parts.Grid.Values
				.Select(t => new GridSpot { Position = t.position, Forward = CourseGeometry.Level(t.forward) == Vector3.zero ? Vector3.forward : CourseGeometry.Level(t.forward) })
				.ToList();
			bool startOk = parts.Start != null && MakeGate(parts.Start, "start", course, out course.Start);
			bool finishOk;
			if (course.IsCircuit) {
				// The start line is the finish line.
				finishOk = startOk;
				course.FinishObject = null;
				if (parts.Finish != null) {
					course.Problems.Add($"A circuit needs no 'TRC Finish {parts.Name}': its start line is the finish line, so that object is ignored.");
				}
			} else {
				finishOk = parts.Finish != null && MakeGate(parts.Finish, "finish", course, out course.Finish);
			}
			if (parts.Start == null) {
				course.Problems.Add($"No 'TRC Start {parts.Name}' object.");
			}
			if (parts.Finish == null && !course.IsCircuit) {
				course.Problems.Add($"No 'TRC Finish {parts.Name}' object.");
			}

			if (startOk && finishOk) {
				if (parts.Path == null) {
					course.Problems.Add(course.IsCircuit
						? $"{Course.NoPathProblem}: drive it once round with Record path, so the mod knows the loop. A circuit can't be used until then."
						: $"{Course.NoPathProblem}: drive it once with Record path, so the mod knows the road and can catch shortcuts. Until then it's practice only.");
				} else {
					List<Vector3> path = ReadPath(parts.Path, parts.Name, course);
					course.HasPath = path.Count >= 2;
					course.Path = path;
				}
			}

			course.Build();
			if (!startOk || !finishOk) {
				course.Path.Clear();
			} else if (course.HasPath && course.Length < course.MinLength) {
				course.Problems.Add($"The course is only {course.Length:0} m long.");
			}
			return course;
		}

		private static bool MakeGate(Transform transform, string what, Course course, out CourseGate gate) {
			float width = Mathf.Abs(transform.lossyScale.x);
			if (!CourseGate.TryMake(transform.position, transform.forward, width > 2f ? width : CourseGeometry.DefaultWidth, out gate)) {
				course.Problems.Add($"The {what} object's blue (Z) arrow points up or down: turn it to point along the road, the way you drive.");
				return false;
			}
			return true;
		}

		/// <summary>The path's points in driving order: a LineRenderer's positions, a strip's vertices, or child objects in order.</summary>
		private static List<Vector3> ReadPath(Transform path, string name, Course course) {
			Vector3 start = course.Start.Center;
			Vector3 finish = course.Finish.Center;

			LineRenderer line = path.GetComponent<LineRenderer>();
			if (line != null && line.positionCount >= 2) {
				var positions = new Vector3[line.positionCount];
				line.GetPositions(positions);
				List<Vector3> points = positions.Select(p => line.useWorldSpace ? p : path.TransformPoint(p)).ToList();
				return InDrivingOrder(points, start, finish);
			}

			var vertices = new List<Vector3>();
			var unreadable = new List<string>();
			foreach (MeshFilter filter in path.GetComponentsInChildren<MeshFilter>(true)) {
				Mesh mesh = filter.sharedMesh;
				if (mesh == null) {
					continue;
				}
				if (!mesh.isReadable) {
					unreadable.Add(mesh.name);
					continue;
				}
				Transform owner = filter.transform;
				vertices.AddRange(mesh.vertices.Select(v => owner.TransformPoint(v)));
			}
			if (unreadable.Count > 0) {
				course.Problems.Add($"Can't read the path: turn on Read/Write in the import settings of its model ({string.Join(", ", unreadable)}).");
			}

			if (vertices.Count >= 2) {
				List<Vector3> ordered = CourseGeometry.OrderPoints(vertices, start, course.Start.Forward, MergeDistance, MaxPathGap, out int unused);
				if (unused > ordered.Count / 10 + 2) {
					course.Problems.Add($"{unused} of the path's points weren't used: check the strip has no gaps over {MaxPathGap:0} m and nothing else in 'TRC Path {name}'.");
				}
				if (ordered.Count >= 2 && Vector3.Distance(ordered[ordered.Count - 1], finish) > MaxPathGap) {
					course.Problems.Add($"The path stops {Vector3.Distance(ordered[ordered.Count - 1], finish):0} m before the finish: it may have a gap over {MaxPathGap:0} m.");
				}
				return ordered;
			}

			if (path.childCount >= 2) {
				var points = new List<Vector3>();
				for (int i = 0; i < path.childCount; i++) {
					points.Add(path.GetChild(i).position);
				}
				return InDrivingOrder(points, start, finish);
			}

			if (unreadable.Count == 0) {
				course.Problems.Add($"'TRC Path {name}' has no strip, LineRenderer or child objects to read the road from.");
			}
			return new List<Vector3>();
		}

		/// <summary>Points already in order along the road: turned around if they run from the finish to the start.</summary>
		private static List<Vector3> InDrivingOrder(List<Vector3> points, Vector3 start, Vector3 finish) {
			float forwards = Vector3.Distance(points[0], start) + Vector3.Distance(points[points.Count - 1], finish);
			float backwards = Vector3.Distance(points[0], finish) + Vector3.Distance(points[points.Count - 1], start);
			if (backwards < forwards) {
				points.Reverse();
			}
			return points;
		}

		private static void Keep(ref Transform slot, Transform transform, Parts parts) {
			if (slot == null) {
				slot = transform;
			} else {
				parts.Duplicates.Add($"Two objects are named '{transform.name}': the first one found is used.");
			}
		}

		/// <summary>The name in lowercase words: spaces, underscores and dashes all split words.</summary>
		private static string[] Words(string name) =>
			TuneRules.Lower(name).Split(new[] { ' ', '_', '-', '\t' }, StringSplitOptions.RemoveEmptyEntries);

		/// <summary>"TRC_Start_Akina_Downhill" → "Akina Downhill", keeping the map maker's capitals.</summary>
		private static string DisplayName(string name, int rest) {
			string[] words = name.Split(new[] { ' ', '_', '-', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			var text = new StringBuilder();
			for (int i = rest; i < words.Length; i++) {
				text.Append(i > rest ? " " : "").Append(words[i]);
			}
			return text.ToString();
		}
	}
}
