// A course's race grid: where each car lines up behind the start line, and whether the grid path is long enough.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class Course {
		/// <summary>Grid rows: the first car this far (metres) behind the start line, then one every <see cref="GridStep"/> (two-wide: alternating sides).</summary>
		public const float GridFirst = 4f;
		public const float GridStep = 4f;
		public const float LineUpStep = 8f;

		/// <summary>
		/// The spots for <paramref name="count"/> cars, pole first: the map's grid objects when it has them, else
		/// along the grid path (or straight back from the start line). Two-wide grids put pole on the left or the
		/// right (<paramref name="poleLeft"/>), the next car on the other side a little further back, and so on.
		/// </summary>
		public List<GridSpot> GridSpots(int count, bool poleLeft, GridLayout? layout = null) {
			var spots = new List<GridSpot>();
			GridLayout shape = layout ?? GridLayout;
			for (int i = 0; i < count && i < GridObjects.Count; i++) {
				spots.Add(GridObjects[i]);
			}
			if (spots.Count == count) {
				return spots;
			}
			if (spots.Count > 0) {
				// More cars than spots: on behind the last one, the way it faces.
				GridSpot last = spots[spots.Count - 1];
				for (int i = spots.Count, n = 1; i < count; i++, n++) {
					spots.Add(new GridSpot { Position = last.Position - last.Forward * (shape == GridLayout.LineUp ? LineUpStep : GridStep * 2) * n, Forward = last.Forward });
				}
				return spots;
			}

			List<Vector3> back = GridLine();
			float side = SideBySideGap / 2f;
			for (int i = 0; i < count; i++) {
				float behind = GridFirst + i * (shape == GridLayout.LineUp ? LineUpStep : GridStep);
				CourseGeometry.AlongBack(back, behind, out Vector3 at, out Vector3 forward);
				if (shape == GridLayout.Grid) {
					bool left = (i % 2 == 0) == poleLeft;
					Vector3 right = new Vector3(forward.z, 0f, -forward.x);
					at += right * (left ? -side : side);
				}
				spots.Add(new GridSpot { Position = at, Forward = forward });
			}
			return spots;
		}

		/// <summary>
		/// The line the grid is laid along, from the start line backwards: the grid path; on a circuit without one, the
		/// end of the lap leading up to the line; else straight back from the line.
		/// </summary>
		private List<Vector3> GridLine() {
			if (GridPath.Count >= 2) {
				return GridPath;
			}
			if (IsCircuit && Path.Count >= 2) {
				var back = new List<Vector3> { Start.Center };
				for (int i = Path.Count - 1; i >= 0; i--) {
					back.Add(Path[i]);
				}
				return back;
			}
			return new List<Vector3> { Start.Center, Start.Center - Start.Forward * 10f };
		}

		/// <summary>A full grid, for the map maker's grid path check.</summary>
		public const int FullGrid = 16;

		/// <summary>How far (metres) the grid path runs back from the start line, or 0 without one.</summary>
		public float GridPathLength {
			get {
				if (!ReferenceEquals(gridLengthOf_, GridPath) || gridLengthCount_ != GridPath.Count) {
					gridLengthOf_ = GridPath;
					gridLengthCount_ = GridPath.Count;
					gridLength_ = 0f;
					for (int i = 1; i < GridPath.Count; i++) {
						gridLength_ += Vector3.Distance(GridPath[i - 1], GridPath[i]);
					}
				}
				return gridLength_;
			}
		}

		private List<Vector3> gridLengthOf_;
		private int gridLengthCount_;
		private float gridLength_;

		/// <summary>How far back (metres) the last of <paramref name="cars"/> lines up, two-wide or single file.</summary>
		public static float GridDepthFor(int cars, GridLayout shape) => GridFirst + Math.Max(0, cars - 1) * (shape == GridLayout.LineUp ? LineUpStep : GridStep);

		/// <summary>
		/// How many cars fit on the grid path (more line up straight back from its end, which may not follow the road).
		/// No limit with the map's own grid spots, or without a grid path (the grid isn't laid along one then).
		/// </summary>
		public int GridRoom(GridLayout shape) {
			if (GridObjects.Count > 0 || GridPath.Count < 2) {
				return int.MaxValue;
			}
			float length = GridPathLength;
			return length < GridFirst ? 0 : 1 + (int)((length - GridFirst) / (shape == GridLayout.LineUp ? LineUpStep : GridStep));
		}

		/// <summary>
		/// bskftw, Nov 2: a map path that doesn't run far enough back from the start line for a full grid. Races still
		/// use it (as many cars as fit, the rest straight back from its end) until the map maker runs it on further.
		/// "" when it fits a full grid single file, or there's no grid path.
		/// </summary>
		public string GridWarning() {
			int two = GridRoom(GridLayout.Grid), line = GridRoom(GridLayout.LineUp);
			if (line >= FullGrid) {
				return "";
			}
			return $"The path runs {GridPathLength:0} m back from the start line: room for {Math.Min(two, FullGrid)} cars two-wide and {line} single file. " +
				$"Run it on to {GridDepthFor(FullGrid, GridLayout.Grid):0} m behind the line for a full grid of {FullGrid} ({GridDepthFor(FullGrid, GridLayout.LineUp):0} m single file, " +
				"for narrow roads). Until then, more cars line up straight back from its end.";
		}
	}
}
