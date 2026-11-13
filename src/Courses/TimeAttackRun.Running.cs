// A run under way: following the car along the course, sectors, laps, speed traps and the rules that stop or spoil it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class TimeAttackRun {
		private void Run(double now, Vector3 position, float speedKmh, float timeScale, bool? onRoad) {
			double dt = now - lastTime_;
			if (dt <= 0) {
				return; // paused
			}
			if (timeScale > 0f && Mathf.Abs(timeScale - 1f) > 0.001f) {
				Cancel("The game speed changed, so the time can't count.");
				return;
			}
			Elapsed = now - goTime_;

			if (BeforeLine) {
				if (CourseGeometry.Crossed(Course.Start, lastPosition_, position, out float lineFraction)) {
					BeforeLine = false;
					if (ClockFromLine) {
						goTime_ = lastTime_ + lineFraction * dt;
						Elapsed = now - goTime_;
						Message = "";
					}
					PathPosition from = CourseGeometry.Project(Course.Path, Course.Distances, position, 0, LookAhead);
					segment_ = from.Segment;
					lastProgress_ = Progress = from.Progress;
				} else if (Elapsed > MaxBeforeLineSeconds) {
					Cancel("Never crossed the start line, so the run was stopped.");
					return;
				} else if (ClockFromLine) {
					// Single file: the clock waits at 0 for this car to reach the line.
					Elapsed = 0;
					Message = "Single file: your clock starts at the start line";
				}
				lastPosition_ = position;
				lastTime_ = now;
				lastSpeed_ = speedKmh;
				return;
			}

			float moved = Vector3.Distance(position, lastPosition_);
			float possible = Mathf.Max(lastSpeed_, speedKmh) / 3.6f * (float)dt;
			bool reset = moved > ResetMetres && moved > possible * 3f + 5f;
			PathPosition at;
			if (reset) {
				// Found again near where the car was (resets put it back on the road behind), else anywhere on the path.
				at = CourseGeometry.Project(Course.Path, Course.Distances, position, segment_ - ResetLookBehind, segment_ + LookAhead);
				if (at.Offset > OffCourseLimit) {
					at = CourseGeometry.Project(Course.Path, Course.Distances, position, 0, Course.Path.Count);
				}
				if (KeepGoing && !Practice && at.Progress > lastProgress_ + TeleportMetres) {
					Cancel("The reset put the car further along the course, so the run was stopped.");
					return;
				} else if (Practice || KeepGoing) {
					Spoil("the car was reset");
					if (KeepGoing) {
						Resets++;
					}
				} else if (!AllowResets) {
					Cancel("The car was reset or moved, so the run was stopped.");
					return;
				} else if (at.Progress > lastProgress_ + ResetAheadMetres || at.Offset > OffCourseLimit) {
					Cancel("The reset put the car further along the course, so the run was stopped.");
					return;
				} else {
					Resets++;
				}
				offSince_ = -1;
				offRoadSince_ = -1;
				Moved(now, position, speedKmh, at, resetTo: true);
				return;
			}

			at = CourseGeometry.Project(Course.Path, Course.Distances, position, segment_ - LookBehind, segment_ + LookAhead);
			if ((Practice || KeepGoing) && at.Offset > OffCourseLimit * 3f) {
				// Far off the course in practice or a race: find the car again wherever it comes back to.
				PathPosition anywhere = CourseGeometry.Project(Course.Path, Course.Distances, position, 0, Course.Path.Count);
				if (!Practice && anywhere.Offset <= OffCourseLimit && anywhere.Progress > lastProgress_ + OffCourseLimit * 3f) {
					Cancel("Shortcut: the car came back to the course further along than it left it.");
					return;
				}
				if (anywhere.Offset < at.Offset) {
					at = anywhere;
					lastProgress_ = at.Progress;
				}
			}
			if (Course.HasPath) {
				if (at.Progress - lastProgress_ > moved + ShortcutMargin) {
					if (!Fail("Shortcut: the car got ahead along the course faster than it drove.", "shortcut", stopsAnyRun: true)) {
						return;
					}
				}
				if (at.Offset > OffCourseLimit) {
					if (offSince_ < 0) {
						offSince_ = now;
					}
					if (now - offSince_ > OffCourseSeconds || at.Offset > OffCourseLimit * 3f) {
						if (!Fail("You left the course, so the run was stopped.", "left the course")) {
							return;
						}
					}
				} else {
					offSince_ = -1;
				}
			}

			if (onRoad == false) {
				if (offRoadSince_ < 0) {
					offRoadSince_ = now;
				}
				if (now - offRoadSince_ > OffRoadSeconds) {
					if (!Fail("You left the road, so the run was stopped.", "left the road")) {
						return;
					}
				}
			} else if (onRoad == true) {
				offRoadSince_ = -1;
			}

			bool farEnough = Mathf.Max(at.Progress, lastProgress_) >= Course.Length * FinishShare;
			if (farEnough && CourseGeometry.Crossed(Course.Finish, lastPosition_, position, out float fraction)) {
				double crossed = lastTime_ + fraction * dt - goTime_;
				Track(crossed, Course.Length);
				EndLap(crossed);
				if (Laps != 0 && LapTimes.Count >= Laps) {
					Result = crossed;
					Elapsed = Result;
					Progress = Course.Length;
					State = Phase.Finished;
					Message = $"Finished: {FormatTime(Result)}";
					return;
				}
				// The next lap starts right here, at the start of the path.
				Lap++;
				BeginLap(crossed);
				at = CourseGeometry.Project(Course.Path, Course.Distances, position, 0, LookAhead);
				Moved(now, position, speedKmh, at, resetTo: true);
				return;
			}

			if (Elapsed > (Practice ? MaxPracticeSeconds : MaxRunSeconds)) {
				Cancel(Practice ? "Over 4 hours, so the practice was stopped." : "Over 30 minutes, so the run was stopped.");
				return;
			}

			Moved(now, position, speedKmh, at, resetTo: false);
		}

		private float OffCourseLimit => Course.Kind == CourseKind.Highway ? OffCourseHighway : Course.IsCircuit ? OffCourseCircuit : OffCourseTouge;

		/// <summary>The car moved on to <paramref name="at"/>: sectors, traps and the trace catch up, then this frame is the last one seen.</summary>
		private void Moved(double now, Vector3 position, float speedKmh, PathPosition at, bool resetTo) {
			if (!Course.HasPath) {
				FollowReferenceLine(position, anywhere: resetTo && LapElapsed > 1);
			}
			if (!resetTo && (line_.Count == 0 || Vector3.Distance(line_[line_.Count - 1], position) >= LineStep)) {
				line_.Add(position);
				lineKmh_.Add(speedKmh);
			}
			if (!resetTo) {
				// A speed trap catches the speed at the moment the car crosses it, worked out between frames (once a lap each).
				for (int i = 0; i < Course.Traps.Count; i++) {
					if (!trapsThisLap_.Contains(i) && CourseGeometry.Crossed(Course.Traps[i], lastPosition_, position, out float fraction)) {
						trapsThisLap_.Add(i);
						float kmh = Mathf.Lerp(lastSpeed_, speedKmh, fraction);
						TrapSpeeds.Add(kmh);
						LastTrap = (i, kmh);
					}
				}
			}
			if (!resetTo && at.Progress > lastProgress_) {
				// The moment the car passed each point is worked out along the way it moved this frame.
				double dt = now - lastTime_;
				float from = lastProgress_;
				float to = at.Progress;
				double TimeAt(float distance) => lastTime_ - goTime_ + dt * (distance - from) / (to - from);
				while (Sector < Course.SectorEnds.Length - 1 && Course.SectorEnds[Sector] <= to) {
					Split(TimeAt(Math.Max(from, Course.SectorEnds[Sector])));
				}
				while (trace_.Count * TraceStep <= to) {
					trace_.Add((float)(TimeAt(Math.Max(from, trace_.Count * TraceStep)) - lapStart_));
				}
			}
			segment_ = at.Segment;
			Progress = at.Progress;
			lastPosition_ = position;
			lastTime_ = now;
			lastSpeed_ = speedKmh;
			lastProgress_ = at.Progress;
		}

		/// <summary>Records <paramref name="crossed"/> for any sectors and trace points up to <paramref name="distance"/> not passed yet (at the line).</summary>
		private void Track(double crossed, float distance) {
			while (trace_.Count * TraceStep <= distance) {
				trace_.Add((float)(crossed - lapStart_));
			}
		}

		private void Split(double at) {
			double started = splits_.Count == 0 ? lapStart_ : splits_[splits_.Count - 1];
			splits_.Add(at);
			LastSplit = new SectorSplit { Index = Sector, Lap = Lap, Time = at - started, Delta = ReferenceDelta(at - lapStart_, Reference),
				CarDelta = ReferenceDelta(at - lapStart_, CarReference) };
			Sector++;
		}

		private void EndLap(double crossed) {
			while (splits_.Count < Course.SectorEnds.Length) {
				Split(crossed);
			}
			var sectors = new double[splits_.Count];
			for (int i = 0; i < splits_.Count; i++) {
				sectors[i] = splits_[i] - (i == 0 ? lapStart_ : splits_[i - 1]);
			}
			LapTimes.Add(new LapTime {
				Number = Lap,
				Time = crossed - lapStart_,
				Sectors = sectors,
				Valid = LapValid,
				Invalid = LapInvalid,
				Standing = Lap == 1 && !Flying,
				Trace = trace_.ToArray(),
				Line = new List<Vector3>(line_),
				LineKmh = new List<float>(lineKmh_),
			});
		}

		/// <summary>A rule was broken: in practice the lap is spoiled and the run goes on (true); otherwise the run stops (false).</summary>
		private bool Fail(string stop, string spoil, bool stopsAnyRun = false) {
			if (!Practice && (!KeepGoing || stopsAnyRun)) {
				Cancel(stop);
				return false;
			}
			Spoil(spoil);
			return true;
		}

		private void Spoil(string why) {
			if (LapValid) {
				LapValid = false;
				LapInvalid = why;
			}
		}
	}
}
