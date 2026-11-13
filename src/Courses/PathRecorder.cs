// Record path: drives a map course between its own start and finish to record its path.
using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Record path: for a map course that has start and finish objects but no path, the player drives it once.
	/// Recording starts when the car crosses the start line and ends when it crosses the finish line, with a
	/// point every few metres in between. Fed positions by the mod, like CourseRecorder.
	/// </summary>
	public sealed class PathRecorder {
		public enum Phase { ToStart, Recording, Done }

		private const float RecordStep = 2f;
		private const float JumpMetres = 40f;

		public Course Course { get; }
		public Phase State { get; private set; } = Phase.ToStart;
		public List<Vector3> Points { get; } = new List<Vector3>();
		public float Recorded { get; private set; }

		/// <summary>What to tell the driver right now.</summary>
		public string Message { get; private set; } = "Drive through the start line";

		private Vector3 last_;
		private bool hasLast_;

		public PathRecorder(Course course) {
			Course = course;
		}

		public void Tick(Vector3 position) {
			if (State == Phase.Done) {
				return;
			}
			if (!hasLast_) {
				last_ = position;
				hasLast_ = true;
				return;
			}
			Vector3 previous = last_;
			last_ = position;

			if (Vector3.Distance(previous, position) > JumpMetres) {
				if (State == Phase.Recording) {
					State = Phase.ToStart;
					Points.Clear();
					Recorded = 0f;
					Message = "The car was reset or moved, so the path was lost. Drive through the start line again";
				}
				return;
			}

			if (State == Phase.ToStart) {
				if (CourseGeometry.Crossed(Course.Start, previous, position, out float at)) {
					State = Phase.Recording;
					Points.Clear();
					Points.Add(Course.Start.Center);
					Points.Add(Vector3.Lerp(previous, position, at));
					Recorded = 0f;
					Message = Course.IsCircuit
						? "Recording: drive once round down the middle of the road, back through the start line"
						: "Recording: drive down the middle of the road to the finish line";
				}
				return;
			}

			Vector3 lastPoint = Points[Points.Count - 1];
			float step = Vector3.Distance(position, lastPoint);
			if (step >= RecordStep) {
				Points.Add(position);
				Recorded += step;
			}
			if (Recorded >= Course.MinLength && CourseGeometry.Crossed(Course.Finish, previous, position, out _)) {
				Points.Add(Course.Finish.Center);
				State = Phase.Done;
				Message = "Path recorded";
			}
		}

		/// <summary>The recorded path as a course file to save: the map course's own lines, with the path.</summary>
		public Course Build(string author) {
			var course = new Course {
				Name = Course.Name,
				Kind = Course.Kind,
				Source = CourseSource.Custom,
				MapKey = Course.MapKey,
				MapName = Course.MapName,
				Author = author,
				Start = new CourseGate { Center = Course.Start.Center, Forward = Course.Start.Forward, Width = Course.Start.Width },
				Finish = new CourseGate { Center = Course.Finish.Center, Forward = Course.Finish.Forward, Width = Course.Finish.Width },
				Path = new List<Vector3>(Points),
				HasPath = true,
			};
			course.Build();
			return course;
		}
	}
}
