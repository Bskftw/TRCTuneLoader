using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The placement tool: the player stops on the start line and sets it, drives the course down the
	/// middle of the road while the route is recorded, then stops on the finish line and sets it. For a
	/// circuit there's no finish to set: driving back through the start line (after a real lap) closes the loop.
	/// Like TimeAttackRun, it's fed positions by the mod and knows nothing about CarX.
	/// </summary>
	public sealed class CourseRecorder {
		public enum Phase { Idle, Recording, Done }

		/// <summary>A route point is kept every this many metres.</summary>
		private const float RecordStep = 2f;

		/// <summary>A jump of more than this (metres) between frames is a reset, which spoils the route.</summary>
		private const float JumpMetres = 40f;

		public Phase State { get; private set; } = Phase.Idle;
		public CourseGate Start { get; private set; }
		public CourseGate Finish { get; private set; }
		public List<Vector3> Points { get; } = new List<Vector3>();

		/// <summary>How far the recorded route goes, in metres.</summary>
		public float Recorded { get; private set; }

		/// <summary>Why the recording stopped on its own ("" when it didn't).</summary>
		public string Problem { get; private set; } = "";

		/// <summary>Recording a circuit: it ends by itself back on the start line.</summary>
		public bool Circuit { get; private set; }

		/// <summary>How far the car is from the middle of the start line (for a circuit, how far it still has to go to close the loop once it's driven a lap).</summary>
		public float DistanceToStart { get; private set; }

		private Vector3 lastSeen_;

		public void Clear() {
			State = Phase.Idle;
			Start = null;
			Finish = null;
			Points.Clear();
			Recorded = 0f;
			Problem = "";
		}

		/// <summary>Sets the start line where the car is, across the way it faces, and starts recording.</summary>
		public bool SetStart(Vector3 position, Vector3 forward, float width, out string error, bool circuit = false) {
			Clear();
			Circuit = circuit;
			if (!CourseGate.TryMake(position, forward, width, out CourseGate gate)) {
				error = "Couldn't tell which way the car faces.";
				return false;
			}
			Start = gate;
			Points.Add(position);
			lastSeen_ = position;
			State = Phase.Recording;
			error = "";
			return true;
		}

		/// <summary>Called every frame while driving: keeps a route point every few metres.</summary>
		public void Tick(Vector3 position) {
			if (State != Phase.Recording) {
				return;
			}
			if (Vector3.Distance(position, lastSeen_) > JumpMetres) {
				Clear();
				Problem = "The car was reset or moved, so the route was lost. Set the start again.";
				return;
			}
			Vector3 previous = lastSeen_;
			lastSeen_ = position;
			DistanceToStart = Vector3.Distance(position, Start.Center);

			Vector3 last = Points[Points.Count - 1];
			float step = Vector3.Distance(position, last);
			if (step >= RecordStep) {
				Points.Add(position);
				Recorded += step;
			}

			// A circuit closes itself: back through the start line, the right way, after a real lap.
			if (Circuit && Recorded >= CourseGeometry.CircuitMinLength && CourseGeometry.Crossed(Start, previous, position, out _)) {
				Finish = new CourseGate { Center = Start.Center, Forward = Start.Forward, Width = Start.Width };
				Points.Add(Start.Center);
				State = Phase.Done;
			}
		}

		/// <summary>Sets the finish line where the car is and stops recording.</summary>
		public bool SetFinish(Vector3 position, Vector3 forward, float width, out string error) {
			if (State != Phase.Recording) {
				error = "Set the start first.";
				return false;
			}
			if (Circuit) {
				error = "A circuit has no finish to set: drive back through the start line and it closes by itself.";
				return false;
			}
			if (Recorded < CourseGeometry.MinLength) {
				error = $"Drive at least {CourseGeometry.MinLength:0} m from the start first.";
				return false;
			}
			if (!CourseGate.TryMake(position, forward, width, out CourseGate gate)) {
				error = "Couldn't tell which way the car faces.";
				return false;
			}
			if (Vector3.Dot(position - Start.Center, Start.Forward) < 0f && Vector3.Distance(position, Start.Center) < 30f) {
				error = Recorded >= CourseGeometry.CircuitMinLength
					? "You're back at the start: that's a loop. Start over and pick Circuit, and it closes by itself here."
					: "The finish can't be right behind the start.";
				return false;
			}
			Finish = gate;
			Points.Add(position);
			State = Phase.Done;
			error = "";
			return true;
		}

		/// <summary>The finished course (after <see cref="SetFinish"/>), ready to save.</summary>
		public Course Build(string name, CourseKind kind, string mapKey, string mapName, string author) {
			var course = new Course {
				Name = name,
				Kind = Circuit ? CourseKind.Circuit : kind,
				Source = CourseSource.Custom,
				MapKey = mapKey,
				MapName = mapName,
				Author = author,
				Start = Start,
				Finish = Finish,
				Path = new List<Vector3>(Points),
				HasPath = true,
			};
			course.Build();
			return course;
		}
	}

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
