// Lay out grid: drives the road leading up to the start line to record the race grid's path.
using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Lay out grid: for race grids, the player drives the road leading up to a course's start line, down the
	/// middle, and crosses the line. The points become the course's grid path, from the line backwards; cars
	/// line up along it. Fed positions by the mod, like the other recorders.
	/// </summary>
	public sealed class GridRecorder {
		public enum Phase { Recording, Done }

		private const float RecordStep = 2f;
		private const float JumpMetres = 40f;

		/// <summary>A grid path shorter than this (metres) isn't worth keeping: the straight line back does as well.</summary>
		public const float MinLength = 30f;

		/// <summary>Enough for 16 cars in a line (4 m + 15 × 8 m) and a little more; past this, older points are dropped.</summary>
		public const float MaxLength = 160f;

		public Course Course { get; }
		public Phase State { get; private set; } = Phase.Recording;
		public float Recorded { get; private set; }

		/// <summary>The points so far, oldest first (the way the car drove).</summary>
		public List<Vector3> Points { get; } = new List<Vector3>();
		public string Message { get; private set; } = "Drive down the middle of the road up to the start line and through it";

		private Vector3 last_;
		private bool hasLast_;

		public GridRecorder(Course course) {
			Course = course;
		}

		public void Tick(Vector3 position) {
			if (State == Phase.Done) {
				return;
			}
			if (!hasLast_) {
				hasLast_ = true;
				last_ = position;
				Points.Add(position);
				return;
			}
			Vector3 previous = last_;
			last_ = position;
			if (Vector3.Distance(previous, position) > JumpMetres) {
				Points.Clear();
				Points.Add(position);
				Recorded = 0f;
				Message = "The car was reset or moved: the grid starts again from here";
				return;
			}
			if (CourseGeometry.Crossed(Course.Start, previous, position, out _)) {
				if (Recorded >= MinLength) {
					State = Phase.Done;
					Message = "Grid laid out";
				} else {
					Points.Clear();
					Points.Add(position);
					Recorded = 0f;
					Message = $"Too short: start at least {MinLength:0} m before the start line";
				}
				return;
			}
			float step = Vector3.Distance(Points[Points.Count - 1], position);
			if (step >= RecordStep) {
				Points.Add(position);
				Recorded += step;
				while (Recorded > MaxLength && Points.Count > 2) {
					Recorded -= Vector3.Distance(Points[0], Points[1]);
					Points.RemoveAt(0);
				}
			}
		}

		/// <summary>The grid path: from the middle of the start line backwards along the way the car came.</summary>
		public List<Vector3> GridPath() {
			var path = new List<Vector3> { Course.Start.Center };
			for (int i = Points.Count - 1; i >= 0; i--) {
				if (CourseGeometry.Level(Points[i] - Course.Start.Center) != Vector3.zero && Course.Start.Ahead(Points[i]) < 0f) {
					path.Add(Points[i]);
				}
			}
			return path;
		}
	}
}
