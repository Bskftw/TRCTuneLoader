// The live delta: how far ahead or behind a reference lap (or, without a path, a reference driving line) the run is.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class TimeAttackRun {
		/// <summary>
		/// The lap to compare with for the live delta: lap time at every <see cref="TraceStep"/> m (a best lap's
		/// or best run's <see cref="LapTime.Trace"/>). Null for none.
		/// </summary>
		public float[] Reference { get; set; }

		/// <summary>Your best in this car on this tune set, the same way (Detailed timing shows the gap to it too). Null for none.</summary>
		public float[] CarReference { get; set; }

		/// <summary>
		/// For a course without a path: the driving line of the run to compare with (a point every
		/// <see cref="LineStep"/> m, with its speed) and its time. The car is followed along that line instead of the
		/// path (a straight start-to-finish line goes back and forth through every hairpin, which made the delta
		/// jump to tens of seconds), and the time at each point is worked out from the speeds.
		/// </summary>
		public void SetReferenceLine(IList<Vector3> line, IList<float> kmh, double seconds) {
			refLine_ = null;
			refLineTimes_ = null;
			lineRefTime_ = -1;
			refSegment_ = 0;
			if (line == null || line.Count < 2 || seconds <= 0) {
				return;
			}
			refLine_ = new List<Vector3>(line);
			var times = new float[line.Count];
			for (int i = 1; i < line.Count; i++) {
				float a = i - 1 < kmh?.Count ? kmh[i - 1] : 0f;
				float b = i < kmh?.Count ? kmh[i] : 0f;
				float speed = Mathf.Max(1.5f, (a + b) / 2f / 3.6f);
				times[i] = times[i - 1] + Vector3.Distance(line[i - 1], line[i]) / speed;
			}
			// Speeds every 2 m are close, not exact: scaled so the line ends on the run's real time.
			float scale = times[line.Count - 1] > 0 ? (float)seconds / times[line.Count - 1] : 1f;
			for (int i = 0; i < times.Length; i++) {
				times[i] *= scale;
			}
			refLineTimes_ = times;
			refDistances_ = CourseGeometry.Cumulative(refLine_);
		}

		private List<Vector3> refLine_;
		private float[] refLineTimes_;
		private float[] refDistances_;
		private int refSegment_;
		private double lineRefTime_ = -1;

		/// <summary>Where the car is along the reference line, a few points either side of the last place (so a hairpin's other side is never taken).</summary>
		private void FollowReferenceLine(Vector3 position, bool anywhere) {
			if (refLine_ == null) {
				return;
			}
			PathPosition at = CourseGeometry.Project(refLine_, refDistances_, position, anywhere ? 0 : refSegment_ - 5, anywhere ? refLine_.Count : refSegment_ + 25);
			if (!anywhere && at.Offset > 30f) {
				at = CourseGeometry.Project(refLine_, refDistances_, position, refSegment_ - 50, refSegment_ + 100);
			}
			if (at.Offset > 60f) {
				lineRefTime_ = -1;
				return;
			}
			refSegment_ = at.Segment;
			float along = at.Progress - refDistances_[at.Segment];
			float length = refDistances_[at.Segment + 1] - refDistances_[at.Segment];
			float t = length > 1e-4f ? Mathf.Clamp01(along / length) : 0f;
			lineRefTime_ = refLineTimes_[at.Segment] + (refLineTimes_[at.Segment + 1] - refLineTimes_[at.Segment]) * t;
		}

		/// <summary>
		/// Seconds ahead (negative) or behind (positive) the <see cref="Reference"/> lap at this point of the lap,
		/// or null without one.
		/// </summary>
		public double? Delta {
			get {
				if (State != Phase.Running) {
					return null;
				}
				if (!Course.HasPath) {
					// No path to measure along (a map course nobody recorded yet): compared on the best run's own line.
					return lineRefTime_ >= 0 ? LapElapsed - lineRefTime_ : (double?)null;
				}
				return TraceDelta(Reference);
			}
		}

		/// <summary>The same against <see cref="CarReference"/> (courses with a path only), or null.</summary>
		public double? CarDelta => State == Phase.Running && Course.HasPath ? TraceDelta(CarReference) : null;

		private double? TraceDelta(float[] reference) {
			if (reference == null || reference.Length < 2) {
				return null;
			}
			float at = Progress / TraceStep;
			int i = Mathf.FloorToInt(at);
			if (i < 0 || i + 1 >= reference.Length) {
				return null;
			}
			double then = reference[i] + (reference[i + 1] - reference[i]) * (at - i);
			return LapElapsed - then;
		}

		/// <summary>The lap time so far against <paramref name="reference"/>'s at the sector now ending, or null.</summary>
		private double? ReferenceDelta(double lapSoFar, float[] reference) {
			if (reference == null || reference.Length < 2 || Sector >= Course.SectorEnds.Length) {
				return null;
			}
			float at = Course.SectorEnds[Sector] / TraceStep;
			int i = Mathf.FloorToInt(at);
			if (i < 0 || i >= reference.Length) {
				return null;
			}
			double then = i + 1 < reference.Length ? reference[i] + (reference[i + 1] - reference[i]) * (at - i) : reference[i];
			return lapSoFar - then;
		}
	}
}
