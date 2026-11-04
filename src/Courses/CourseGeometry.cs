using System;
using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>Where a car is along a course's path.</summary>
	public struct PathPosition {
		/// <summary>Metres along the path from the start line.</summary>
		public float Progress;

		/// <summary>How far the car is from the path, in metres.</summary>
		public float Offset;

		/// <summary>The path segment the car is closest to (between point Segment and Segment + 1).</summary>
		public int Segment;
	}

	/// <summary>The maths behind courses: evening out paths, putting map points in order, finding a car on a path, line crossings.</summary>
	public static class CourseGeometry {
		/// <summary>Start and finish lines are this wide (metres) unless the map object or the placement tool says otherwise.</summary>
		public const float DefaultWidth = 12f;

		/// <summary>Paths are evened out to a point every this many metres.</summary>
		public const float Step = 2f;

		/// <summary>Shorter courses than this (metres) can't be used.</summary>
		public const float MinLength = 50f;

		/// <summary>A circuit's lap must be at least this long (metres), so driving round the start line isn't a lap.</summary>
		public const float CircuitMinLength = 200f;

		/// <summary>Without sector objects, a course has this many sectors, plus one for every <see cref="MetresPerExtraSector"/> over that.</summary>
		public const int MinSectors = 3;
		public const float MetresPerExtraSector = 2000f;

		/// <summary>A sector object closer than this (metres) to the start or finish, or to another one, is left out.</summary>
		public const float MinSectorMetres = 20f;

		/// <summary>A car this far above or below a line (metres) isn't crossing it: it's on a bridge or a road underneath.</summary>
		public const float GateHeight = 6f;

		/// <summary>A car this much past either end of a line (metres) still counts as crossing it.</summary>
		public const float GateMargin = 1.5f;

		/// <summary>Points along the line through <paramref name="points"/>, every <paramref name="step"/> metres, from its first point to its last.</summary>
		public static List<Vector3> Resample(IList<Vector3> points, float step) {
			var result = new List<Vector3>();
			if (points == null || points.Count == 0) {
				return result;
			}

			result.Add(points[0]);
			float carried = 0f;
			for (int i = 1; i < points.Count; i++) {
				Vector3 a = points[i - 1];
				Vector3 b = points[i];
				float length = Vector3.Distance(a, b);
				if (length < 1e-4f) {
					continue;
				}

				float at = step - carried;
				while (at <= length) {
					result.Add(Vector3.Lerp(a, b, at / length));
					at += step;
				}
				carried = length - (at - step);
			}

			Vector3 last = points[points.Count - 1];
			if (Vector3.Distance(result[result.Count - 1], last) > step * 0.25f) {
				result.Add(last);
			} else {
				result[result.Count - 1] = last;
			}
			return result;
		}

		/// <summary>Distance along the path at each point.</summary>
		public static float[] Cumulative(IList<Vector3> path) {
			var distances = new float[path.Count];
			for (int i = 1; i < path.Count; i++) {
				distances[i] = distances[i - 1] + Vector3.Distance(path[i - 1], path[i]);
			}
			return distances;
		}

		/// <summary>
		/// True when moving from <paramref name="from"/> to <paramref name="to"/> crosses the line forwards
		/// (from behind it to past it), between its ends and not on a road above or below it.
		/// <paramref name="fraction"/> is how far between the two points the crossing is (0 to 1).
		/// </summary>
		public static bool Crossed(CourseGate gate, Vector3 from, Vector3 to, out float fraction) {
			fraction = 0f;
			float a = gate.Ahead(from);
			float b = gate.Ahead(to);
			if (!(a < 0f && b >= 0f)) {
				return false;
			}

			fraction = a / (a - b);
			Vector3 at = Vector3.Lerp(from, to, fraction);
			return Mathf.Abs(gate.Across(at)) <= gate.Width / 2f + GateMargin &&
				Mathf.Abs(at.y - gate.Center.y) <= GateHeight;
		}

		/// <summary>
		/// The point <paramref name="behind"/> metres back along <paramref name="back"/> (a line drawn from the start
		/// line backwards), and the way a car there faces (towards the start). Past its end, straight on back.
		/// </summary>
		public static void AlongBack(IList<Vector3> back, float behind, out Vector3 at, out Vector3 forward) {
			float left = behind;
			for (int i = 1; i < back.Count; i++) {
				Vector3 a = back[i - 1];
				Vector3 b = back[i];
				float length = Vector3.Distance(a, b);
				if (length < 1e-4f) {
					continue;
				}
				forward = Level(a - b);
				if (left <= length || i == back.Count - 1) {
					at = a + (b - a) * (left / length);
					if (forward == Vector3.zero) {
						forward = Vector3.forward;
					}
					return;
				}
				left -= length;
			}
			at = back.Count > 0 ? back[0] : Vector3.zero;
			forward = Vector3.forward;
		}

		/// <summary>
		/// Where each sector ends along the path, the last at its end. Sector objects (<paramref name="points"/>, in
		/// any order) are found on the path; without usable ones, the path is cut into equal parts, more for longer
		/// courses (3 up to 6 km, then one more every 2 km).
		/// </summary>
		public static float[] SectorEnds(IList<Vector3> path, float[] distances, IList<Vector3> points) {
			float length = distances.Length > 0 ? distances[distances.Length - 1] : 0f;
			if (length <= 0f) {
				return new float[0];
			}
			var ends = new List<float>();
			if (points != null && points.Count > 0 && path.Count >= 2) {
				foreach (Vector3 point in points) {
					PathPosition at = Project(path, distances, point, 0, path.Count);
					if (at.Offset < 40f && at.Progress > MinSectorMetres && at.Progress < length - MinSectorMetres) {
						ends.Add(at.Progress);
					}
				}
				ends.Sort();
				for (int i = ends.Count - 1; i > 0; i--) {
					if (ends[i] - ends[i - 1] < MinSectorMetres) {
						ends.RemoveAt(i);
					}
				}
			}
			if (ends.Count == 0) {
				int count = Math.Max(MinSectors, (int)Math.Ceiling(length / MetresPerExtraSector));
				for (int i = 1; i < count; i++) {
					ends.Add(length * i / count);
				}
			}
			ends.Add(length);
			return ends.ToArray();
		}

		/// <summary>
		/// The closest point on the path to <paramref name="point"/>, looking only at segments
		/// <paramref name="first"/> to <paramref name="last"/> (so a car is followed along the road, never
		/// jumped to another part of it that happens to be close, like the other side of a hairpin).
		/// </summary>
		public static PathPosition Project(IList<Vector3> path, float[] distances, Vector3 point, int first, int last) {
			var best = new PathPosition { Offset = float.MaxValue };
			if (path.Count < 2) {
				return best;
			}

			first = Mathf.Clamp(first, 0, path.Count - 2);
			last = Mathf.Clamp(last, first, path.Count - 2);
			float bestSquared = float.MaxValue;
			for (int i = first; i <= last; i++) {
				Vector3 a = path[i];
				Vector3 ab = path[i + 1] - a;
				float lengthSquared = ab.sqrMagnitude;
				float t = lengthSquared > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSquared) : 0f;
				Vector3 closest = a + ab * t;
				float squared = (point - closest).sqrMagnitude;
				if (squared < bestSquared) {
					bestSquared = squared;
					best.Segment = i;
					best.Progress = distances[i] + Mathf.Sqrt(lengthSquared) * t;
				}
			}
			best.Offset = Mathf.Sqrt(bestSquared);
			return best;
		}

		/// <summary>
		/// Puts loose points along a road (the vertices of a thin strip modelled down the middle of it) in
		/// driving order: points closer than <paramref name="merge"/> metres become one, then the chain starts
		/// at the point nearest the start line and keeps taking the next point ahead, preferring to go
		/// straight on, so it follows hairpins instead of jumping across them. Points it never reaches are
		/// counted in <paramref name="unused"/>.
		/// </summary>
		public static List<Vector3> OrderPoints(IEnumerable<Vector3> points, Vector3 start, Vector3 startForward, float merge, float maxGap, out int unused) =>
			OrderPoints(points, start, startForward, merge, maxGap, out unused, out _);

		/// <summary>
		/// The same, and also the chain going the other way from the start (<paramref name="behind"/>, from the point
		/// nearest the start backwards): a strip that runs on past the start line gives the road leading up to it.
		/// Points in either chain count as used.
		/// </summary>
		public static List<Vector3> OrderPoints(IEnumerable<Vector3> points, Vector3 start, Vector3 startForward, float merge, float maxGap, out int unused, out List<Vector3> behind) {
			List<Vector3> clusters = Merge(points, merge);
			var ordered = new List<Vector3>();
			behind = new List<Vector3>();
			unused = 0;
			if (clusters.Count == 0) {
				return ordered;
			}

			var grid = new Dictionary<long, List<int>>();
			float cell = Mathf.Max(maxGap / 2f, 1f);
			for (int i = 0; i < clusters.Count; i++) {
				long key = CellKey(clusters[i], cell);
				if (!grid.TryGetValue(key, out var list)) {
					grid[key] = list = new List<int>();
				}
				list.Add(i);
			}

			var used = new bool[clusters.Count];
			int current = 0;
			float nearest = float.MaxValue;
			for (int i = 0; i < clusters.Count; i++) {
				float d = (clusters[i] - start).sqrMagnitude;
				if (d < nearest) {
					nearest = d;
					current = i;
				}
			}

			int first = current;
			used[current] = true;
			int reach = Mathf.CeilToInt(maxGap / cell);
			ordered = Walk(clusters, grid, used, first, Level(startForward), cell, reach, maxGap);
			behind = Walk(clusters, grid, used, first, -Level(startForward), cell, reach, maxGap);

			foreach (bool u in used) {
				if (!u) {
					unused++;
				}
			}
			return Smooth(ordered);
		}

		/// <summary>The chain from <paramref name="current"/> on, starting off along <paramref name="direction"/>, taking points not yet used.</summary>
		private static List<Vector3> Walk(List<Vector3> clusters, Dictionary<long, List<int>> grid, bool[] used, int current, Vector3 direction, float cell, int reach, float maxGap) {
			var ordered = new List<Vector3> { clusters[current] };
			while (true) {
				Vector3 here = clusters[current];
				int cx = Mathf.FloorToInt(here.x / cell);
				int cz = Mathf.FloorToInt(here.z / cell);
				int next = -1;
				float bestScore = float.MaxValue;

				for (int dx = -reach; dx <= reach; dx++) {
					for (int dz = -reach; dz <= reach; dz++) {
						if (!grid.TryGetValue(CellKey(cx + dx, cz + dz), out var list)) {
							continue;
						}
						foreach (int i in list) {
							if (used[i]) {
								continue;
							}
							Vector3 step = clusters[i] - here;
							float distance = step.magnitude;
							if (distance > maxGap || distance < 1e-4f) {
								continue;
							}
							float turn = Vector3.Dot(Level(step), direction);
							if (direction != Vector3.zero && turn < -0.3f) {
								continue;
							}
							// Going straight on costs the plain distance; a right-angle turn three times as much.
							float score = distance * (1f + 2f * (1f - turn));
							if (score < bestScore) {
								bestScore = score;
								next = i;
							}
						}
					}
				}

				if (next < 0) {
					break;
				}
				Vector3 moved = Level(clusters[next] - here);
				direction = direction == Vector3.zero ? moved : Level(direction + moved);
				used[next] = true;
				ordered.Add(clusters[next]);
				current = next;
			}
			return ordered;
		}

		/// <summary>
		/// A line of points along the road cut at the start and finish lines (bskftw, Oct 30: a map's path may run on
		/// past both, so races can line up on it). <paramref name="runUp"/> is the part before the start line, from the
		/// line backwards (where the grid goes); <paramref name="runOut"/> the part after the finish, from the line on
		/// (the reverse direction's run-up). Both are empty when the line stops at the start or finish. Returns the
		/// course itself, from the start line to the finish line.
		/// </summary>
		public static List<Vector3> SplitAtLines(List<Vector3> line, CourseGate start, CourseGate finish, out List<Vector3> runUp, out List<Vector3> runOut) {
			runUp = new List<Vector3>();
			runOut = new List<Vector3>();
			var course = new List<Vector3>(line);
			for (int i = 1; i < course.Count; i++) {
				if (Crossed(start, course[i - 1], course[i], out float fraction)) {
					Vector3 at = Vector3.Lerp(course[i - 1], course[i], fraction);
					runUp.Add(at);
					for (int j = i - 1; j >= 0; j--) {
						runUp.Add(course[j]);
					}
					course.RemoveRange(0, i);
					course.Insert(0, at);
					break;
				}
				if (Vector3.Distance(course[0], course[i]) > RunUpSearchMetres) {
					break; // the start line is near the beginning or not crossed at all
				}
			}
			float along = 0f;
			for (int i = 1; i < course.Count; i++) {
				along += Vector3.Distance(course[i - 1], course[i]);
				if (along > RunUpSearchMetres && Crossed(finish, course[i - 1], course[i], out float fraction)) {
					Vector3 at = Vector3.Lerp(course[i - 1], course[i], fraction);
					runOut.Add(at);
					runOut.AddRange(course.GetRange(i, course.Count - i));
					course.RemoveRange(i, course.Count - i);
					course.Add(at);
					break;
				}
			}
			if (Length(runUp) < MinRunUpMetres) {
				runUp.Clear();
			}
			if (Length(runOut) < MinRunUpMetres) {
				runOut.Clear();
			}
			return course;
		}

		/// <summary>A run-up shorter than this isn't one: just the strip's end a little past the line.</summary>
		public const float MinRunUpMetres = 10f;

		/// <summary>The start line is looked for this far into the line; the finish only after this far.</summary>
		private const float RunUpSearchMetres = 400f;

		private static float Length(IList<Vector3> points) {
			float total = 0f;
			for (int i = 1; i < points.Count; i++) {
				total += Vector3.Distance(points[i - 1], points[i]);
			}
			return total;
		}

		/// <summary>Averages each point with its neighbours, which takes out the zigzag between the two edges of a strip.</summary>
		public static List<Vector3> Smooth(IList<Vector3> points) {
			var result = new List<Vector3>(points.Count);
			for (int i = 0; i < points.Count; i++) {
				if (i == 0 || i == points.Count - 1) {
					result.Add(points[i]);
				} else {
					result.Add((points[i - 1] + points[i] * 2f + points[i + 1]) / 4f);
				}
			}
			return result;
		}

		/// <summary>
		/// The middle of a strip of faces one face across, flat on the road or standing up like a fence (bskftw, Oct 30:
		/// a path may be the road's own centre faces duplicated, however wide they are). Every edge two faces share
		/// crosses the strip (the rungs and the quads' diagonals), so its midpoint is on the centre line. Vertices are
		/// joined by position, as Unity splits them at seams. Empty when no edge is shared (loose triangles).
		/// </summary>
		public static List<Vector3> StripCentre(IList<Vector3> vertices, IList<int> triangles) {
			var welded = new Dictionary<(int, int, int), int>();
			var at = new int[vertices.Count];
			for (int i = 0; i < vertices.Count; i++) {
				Vector3 v = vertices[i];
				var key = (Mathf.RoundToInt(v.x * 100f), Mathf.RoundToInt(v.y * 100f), Mathf.RoundToInt(v.z * 100f));
				if (!welded.TryGetValue(key, out int index)) {
					welded[key] = index = i;
				}
				at[i] = index;
			}
			var edges = new Dictionary<long, int>();
			for (int t = 0; t + 2 < triangles.Count; t += 3) {
				for (int e = 0; e < 3; e++) {
					int a = at[triangles[t + e]];
					int b = at[triangles[t + (e + 1) % 3]];
					if (a == b) {
						continue;
					}
					long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
					edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
				}
			}
			var centre = new List<Vector3>();
			foreach (var edge in edges) {
				if (edge.Value >= 2) {
					centre.Add((vertices[(int)(edge.Key >> 32)] + vertices[(int)(edge.Key & 0xffffffffL)]) / 2f);
				}
			}
			return centre;
		}

		/// <summary>The direction made level and one metre long (zero when it points straight up or down).</summary>
		public static Vector3 Level(Vector3 direction) {
			var level = new Vector3(direction.x, 0f, direction.z);
			float length = level.magnitude;
			return length < 1e-4f ? Vector3.zero : level / length;
		}

		private static List<Vector3> Merge(IEnumerable<Vector3> points, float merge) {
			var sums = new List<Vector3>();
			var counts = new List<int>();
			var grid = new Dictionary<long, List<int>>();
			float cell = Mathf.Max(merge, 0.01f);

			foreach (Vector3 point in points) {
				int cx = Mathf.FloorToInt(point.x / cell);
				int cz = Mathf.FloorToInt(point.z / cell);
				int found = -1;
				for (int dx = -1; dx <= 1 && found < 0; dx++) {
					for (int dz = -1; dz <= 1 && found < 0; dz++) {
						if (!grid.TryGetValue(CellKey(cx + dx, cz + dz), out var list)) {
							continue;
						}
						foreach (int i in list) {
							if ((sums[i] / counts[i] - point).sqrMagnitude <= merge * merge) {
								found = i;
								break;
							}
						}
					}
				}

				if (found >= 0) {
					sums[found] += point;
					counts[found]++;
				} else {
					long key = CellKey(cx, cz);
					if (!grid.TryGetValue(key, out var list)) {
						grid[key] = list = new List<int>();
					}
					list.Add(sums.Count);
					sums.Add(point);
					counts.Add(1);
				}
			}

			var clusters = new List<Vector3>(sums.Count);
			for (int i = 0; i < sums.Count; i++) {
				clusters.Add(sums[i] / counts[i]);
			}
			return clusters;
		}

		private static long CellKey(Vector3 point, float cell) => CellKey(Mathf.FloorToInt(point.x / cell), Mathf.FloorToInt(point.z / cell));

		private static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;
	}
}
