// Places in the map (docs/archive/IMPROVEMENT_PLAN.md F14): boards at start lines, parking areas as meet points, and territory zones.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader {
	public static partial class CourseScanner {
		/// <summary>A "TRC Board &lt;track name&gt;" object: where that track's times are shown, over the object.</summary>
		public sealed class MapBoard {
			public string Name = "";
			public Vector3 Position;
		}

		/// <summary>A "TRC Parking &lt;name&gt;" or "TRC Territory &lt;region&gt;" object: the space it covers, and (territory) its meshes.</summary>
		public sealed class MapArea {
			public string Name = "";
			public Bounds Bounds;
			public Transform Transform;
		}

		public static List<MapBoard> Boards { get; } = new List<MapBoard>();
		public static List<MapArea> Parking { get; } = new List<MapArea>();
		public static List<MapArea> Territory { get; } = new List<MapArea>();

		private static void ClearPlaces() {
			Boards.Clear();
			Parking.Clear();
			Territory.Clear();
		}

		/// <summary>From the scan: "TRC Board", "TRC Parking" or "TRC Territory" and a name. True when it was one.</summary>
		private static bool ReadPlace(string[] words, int trcAt, string bare, Transform transform) {
			if (trcAt < 0 || trcAt + 2 >= words.Length) {
				return false;
			}
			string kind = words[trcAt + 1];
			if (kind != "board" && kind != "parking" && kind != "territory") {
				return false;
			}
			string name = DisplayName(bare, trcAt + 2);
			if (kind == "board") {
				Boards.Add(new MapBoard { Name = name, Position = transform.position });
				return true;
			}
			var area = new MapArea { Name = name, Transform = transform, Bounds = BoundsOf(transform) };
			(kind == "parking" ? Parking : Territory).Add(area);
			return true;
		}

		/// <summary>The space an object covers: its meshes and colliders, or (an empty object) its position and scale as a box.</summary>
		private static Bounds BoundsOf(Transform transform) {
			Bounds? all = null;
			foreach (Renderer r in transform.GetComponentsInChildren<Renderer>(true)) {
				if (all == null) {
					all = r.bounds;
				} else {
					Bounds b = all.Value;
					b.Encapsulate(r.bounds);
					all = b;
				}
			}
			foreach (Collider c in transform.GetComponentsInChildren<Collider>(true)) {
				if (all == null) {
					all = c.bounds;
				} else {
					Bounds b = all.Value;
					b.Encapsulate(c.bounds);
					all = b;
				}
			}
			if (all != null && all.Value.size.sqrMagnitude > 0.01f) {
				Bounds grown = all.Value;
				// Cars sit on it, so a flat area still counts a car a few metres above it.
				grown.Expand(new Vector3(0f, 6f, 0f));
				return grown;
			}
			Vector3 size = transform.lossyScale;
			return new Bounds(transform.position, new Vector3(Mathf.Max(4f, Mathf.Abs(size.x)), Mathf.Max(6f, Mathf.Abs(size.y)), Mathf.Max(4f, Mathf.Abs(size.z))));
		}
	}
}
