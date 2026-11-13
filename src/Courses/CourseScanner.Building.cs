// Builds a course from the map pieces found: roads, start and finish gates, and the path in driving order.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public static partial class CourseScanner {
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
			foreach (Transform t in parts.Traps.Values) {
				float trapWidth = Mathf.Abs(t.lossyScale.x);
				if (CourseGate.TryMake(t.position, t.forward, trapWidth > 2f ? trapWidth : CourseGeometry.DefaultWidth, out CourseGate trap)) {
					course.Traps.Add(trap);
				}
			}
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
				if (parts.PathPieces.Count == 0) {
					course.Problems.Add(course.IsCircuit
						? $"{Course.NoPathProblem}: drive it once round with Record path, so the mod knows the loop. A circuit can't be used until then."
						: $"{Course.NoPathProblem}: drive it once with Record path, so the mod knows the road and can catch shortcuts. Until then it's practice only.");
				} else {
					List<Vector3> path = ReadPath(parts.PathPieces.OrderBy(p => p.Key).Select(p => p.Value).ToList(), parts.Name, course);
					if (!course.IsCircuit && path.Count >= 2) {
						// A path that runs on past the start line is where the race grid lines up (no grid objects needed),
						// and past the finish line, where the reverse twin's grid lines up.
						path = CourseGeometry.SplitAtLines(path, course.Start, course.Finish, out List<Vector3> runUp, out List<Vector3> runOut);
						if (course.GridObjects.Count == 0) {
							course.GridPath = runUp;
						}
						course.RunOut = runOut;
					}
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

		/// <summary>
		/// The path's points in driving order: a LineRenderer's positions, a strip's middle, or child objects in order.
		/// <paramref name="pieces"/> are "TRC Path Akina", "TRC Path Akina.001" and so on, the unnumbered one first.
		/// </summary>
		/// <summary>
		/// Each path mesh's middle line (world space), read once per scan: a shared path ("TRC Path Akina") serves several
		/// courses, and reading it again for each was most of a long scan (bskftw, Nov 3: 7.6 s on Project Dream).
		/// </summary>
		private static readonly Dictionary<MeshFilter, List<Vector3>> stripCentres_ = new Dictionary<MeshFilter, List<Vector3>>();
		private static readonly Stopwatch pathTimer_ = new Stopwatch();

		private static List<Vector3> ReadPath(IList<Transform> pieces, string name, Course course) {
			pathTimer_.Start();
			try {
				// The same path as an earlier load (same meshes, lines and mod version): used as it was, with its problems.
				string key = saved_ != null ? PathKey(pieces, name, course) : null;
				if (key != null && saved_.TryGet(key, out SavedPaths.Entry entry)) {
					course.Problems.AddRange(entry.Problems);
					return entry.Path;
				}
				int problemsBefore = course.Problems.Count;
				List<Vector3> path = ReadPathTimed(pieces, name, course);
				if (key != null) {
					saved_.Put(key, path, course.Problems.Skip(problemsBefore).ToList());
				}
				return path;
			} finally {
				pathTimer_.Stop();
			}
		}

		private static List<Vector3> ReadPathTimed(IList<Transform> pieces, string name, Course course) {
			Vector3 start = course.Start.Center;
			// A circuit's finish is its start line (its Finish is only filled in by Build, after this).
			Vector3 finish = course.IsCircuit ? start : course.Finish.Center;

			var linePoints = new List<Vector3>();
			foreach (Transform piece in pieces) {
				LineRenderer line = piece.GetComponent<LineRenderer>();
				if (line != null && line.positionCount >= 2) {
					var positions = new Vector3[line.positionCount];
					line.GetPositions(positions);
					linePoints.AddRange(positions.Select(p => line.useWorldSpace ? p : piece.TransformPoint(p)));
				}
			}
			if (linePoints.Count >= 2) {
				return InDrivingOrder(linePoints, start, finish);
			}

			var vertices = new List<Vector3>();
			var unreadable = new List<string>();
			foreach (MeshFilter filter in pieces.SelectMany(p => p.GetComponentsInChildren<MeshFilter>(true))) {
				Mesh mesh = filter.sharedMesh;
				if (mesh == null) {
					continue;
				}
				if (!mesh.isReadable) {
					unreadable.Add(mesh.name);
					continue;
				}
				if (!stripCentres_.TryGetValue(filter, out List<Vector3> middle)) {
					centreTimer_.Start();
					Transform owner = filter.transform;
					List<Vector3> world = mesh.vertices.Select(v => owner.TransformPoint(v)).ToList();
					// The middle of the strip, whether it lies flat (the road's centre faces) or stands up (a line extruded up or
					// down); a mesh with no faces joined up falls back to its vertices, merged by distance.
					List<Vector3> centre = CourseGeometry.StripCentre(world, mesh.triangles);
					middle = centre.Count >= 2 ? centre : world;
					stripCentres_[filter] = middle;
					centreTimer_.Stop();
				}
				vertices.AddRange(middle);
			}
			if (unreadable.Count > 0) {
				course.Problems.Add($"Can't read the path: turn on Read/Write in the import settings of its model ({string.Join(", ", unreadable)}).");
			}

			if (vertices.Count >= 2) {
				orderTimer_.Start();
				List<Vector3> ordered = CourseGeometry.OrderPoints(vertices, start, course.Start.Forward, MergeDistance, MaxPathGap, out int unused, out List<Vector3> behind);
				orderTimer_.Stop();
				if (behind.Count >= 2) {
					// The strip runs on behind the start line: put that part in front, so it's cut off as the grid run-up.
					List<Vector3> before = CourseGeometry.Smooth(behind);
					before.Reverse();
					before.RemoveAt(before.Count - 1);
					ordered.InsertRange(0, before);
				}
				if (unused > ordered.Count / 10 + 2) {
					course.Problems.Add($"{unused} of the path's points weren't used: check the strip has no gaps over {MaxPathGap:0} m and nothing else in 'TRC Path {name}'.");
				}
				if (course.IsCircuit) {
					// A loop: it should come back round to the start line (bskftw, Nov 3: every circuit said it stopped 8 km short).
					float gap = ordered.Count >= 2 ? Vector3.Distance(ordered[ordered.Count - 1], start) : 0f;
					if (gap > MaxPathGap) {
						course.Problems.Add($"The path's loop stops {gap:0} m before it gets back to the start line: it may have a gap over {MaxPathGap:0} m.");
					}
				} else if (ordered.Count >= 2 && !ordered.Any(p => Vector3.Distance(p, finish) <= MaxPathGap)) {
					course.Problems.Add($"The path stops {Vector3.Distance(ordered[ordered.Count - 1], finish):0} m before the finish: it may have a gap over {MaxPathGap:0} m.");
				}
				return ordered;
			}

			var children = new List<Vector3>();
			foreach (Transform piece in pieces) {
				for (int i = 0; i < piece.childCount; i++) {
					children.Add(piece.GetChild(i).position);
				}
			}
			if (children.Count >= 2) {
				return InDrivingOrder(children, start, finish);
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
	}
}
