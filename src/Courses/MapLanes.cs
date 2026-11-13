// Traffic lanes placed in the map (TRC Lane objects), read during the scan for the traffic.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public static partial class CourseScanner {
		/// <summary>
		/// A traffic lane placed in the map (bskftw, Nov 8: lanes stored in the map, so the traffic comes with it): a thin strip
		/// mesh named <c>TRC Lane &lt;name&gt; [speed N] [exit N]</c>, made by the Blender add-on's "Make map lane meshes", with
		/// Read/Write on. Its UVs' first number is the distance along the lane, which gives the order and the direction
		/// (Unity may reorder a mesh's vertices on import; the UVs stay with them).
		/// </summary>
		public sealed class MapLane {
			public string Name = "";
			public int SpeedKmh, ExitShare;
			public List<Vector3> Points = new List<Vector3>();
		}

		/// <summary>The lanes of the last map scanned (<see cref="LanesMapKey"/>), and what was wrong with any that couldn't be read.</summary>
		public static List<MapLane> Lanes { get; } = new List<MapLane>();
		public static List<string> LaneProblems { get; } = new List<string>();
		public static string LanesMapKey { get; private set; } = "";

		/// <summary>How many scans there have been: what was worked out from <see cref="Lanes"/> only needs doing again when it changes.</summary>
		public static int Scans { get; private set; }

		private static readonly System.Text.RegularExpressions.Regex LaneTag =
			new System.Text.RegularExpressions.Regex(@"\[\s*(speed|exit)\s+(\d+)\s*\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

		private static void ReadLanes(List<(string Name, int Copy, Transform Transform)> objects) {
			foreach (var (bare, copy, transform) in objects.OrderBy(o => o.Name, StringComparer.Ordinal).ThenBy(o => o.Copy)) {
				int at = bare.IndexOf("lane", bare.IndexOf("trc", StringComparison.OrdinalIgnoreCase) + 3, StringComparison.OrdinalIgnoreCase);
				string rest = at >= 0 ? bare.Substring(at + 4) : bare;
				var lane = new MapLane();
				foreach (System.Text.RegularExpressions.Match tag in LaneTag.Matches(rest)) {
					int value = int.Parse(tag.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
					if (tag.Groups[1].Value.Equals("speed", StringComparison.OrdinalIgnoreCase)) {
						lane.SpeedKmh = value;
					} else {
						lane.ExitShare = Math.Min(100, value);
					}
				}
				lane.Name = LaneTag.Replace(rest, "").Trim(' ', '_', '\t');
				if (copy > 0) {
					lane.Name += $" {copy + 1}";
				}
				// The middle of the strip at each distance along it: the two edge points that share a UV distance, averaged.
				var byDistance = new SortedDictionary<long, (Vector3 Sum, int Count)>();
				string problem = "";
				foreach (MeshFilter filter in transform.GetComponentsInChildren<MeshFilter>(true)) {
					Mesh mesh = filter.sharedMesh;
					if (mesh == null) {
						continue;
					}
					if (!mesh.isReadable) {
						problem = "turn on Read/Write in its model's import settings";
						continue;
					}
					Vector3[] vertices = mesh.vertices;
					Vector2[] uv = mesh.uv;
					if (uv == null || uv.Length != vertices.Length) {
						problem = "it has no UVs (make it with the Blender add-on's Make map lane meshes)";
						continue;
					}
					for (int i = 0; i < vertices.Length; i++) {
						long key = (long)Math.Round(uv[i].x * 100.0);
						Vector3 world = filter.transform.TransformPoint(vertices[i]);
						byDistance.TryGetValue(key, out var have);
						byDistance[key] = (have.Sum + world, have.Count + 1);
					}
				}
				lane.Points = byDistance.Values.Select(p => p.Sum / p.Count).ToList();
				if (lane.Points.Count < 2) {
					LaneProblems.Add($"'{transform.name}' couldn't be read as a lane: {(problem.Length > 0 ? problem : "it has no points")}.");
					continue;
				}
				Lanes.Add(lane);
			}
		}
	}
}
