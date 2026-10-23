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
		public static List<Vector3> OrderPoints(IEnumerable<Vector3> points, Vector3 start, Vector3 startForward, float merge, float maxGap, out int unused) {
			List<Vector3> clusters = Merge(points, merge);
			var ordered = new List<Vector3>();
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

			Vector3 direction = Level(startForward);
			used[current] = true;
			ordered.Add(clusters[current]);
			int reach = Mathf.CeilToInt(maxGap / cell);

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

			foreach (bool u in used) {
				if (!u) {
					unused++;
				}
			}
			return Smooth(ordered);
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
