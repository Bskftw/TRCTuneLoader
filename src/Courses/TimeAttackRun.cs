using System;
using System.Globalization;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// One time attack on a course, fed the car's position every frame.
	///
	/// Standing start: the car stops behind the start line, a 3-2-1-GO countdown runs, and the clock starts
	/// at GO. Moving during the countdown is a jump start. The run ends when the car crosses the finish line
	/// after following the path (the exact moment is worked out between frames), and is stopped by a
	/// shortcut, leaving the course, a reset, or a change of game speed.
	///
	/// It knows nothing about CarX: the mod feeds it positions, so the self-test can drive it too.
	/// </summary>
	public sealed class TimeAttackRun {
		public enum Phase { ToStart, Countdown, Running, Finished, Cancelled }

		public const float CountdownSeconds = 3f;

		/// <summary>How long the car must stand still behind the line before the countdown starts.</summary>
		private const float StillSeconds = 1f;
		private const float StoppedKmh = 0.6f;

		/// <summary>Faster than this, or further than <see cref="JumpMetres"/> from where it stood, during the countdown is a jump start.</summary>
		private const float JumpKmh = 2f;
		private const float JumpMetres = 0.4f;

		/// <summary>How far behind the start line (metres) the car can wait for the countdown.</summary>
		private const float GridDepth = 25f;
		private const float GridHeight = 4f;

		/// <summary>The finish only counts once the car is this far along the path, so it can't be reached the wrong way.</summary>
		private const float FinishShare = 0.85f;
		private const double MaxRunSeconds = 1800;

		/// <summary>Further from the path than this (metres) for longer than <see cref="OffCourseSeconds"/> stops the run.</summary>
		private const float OffCourseTouge = 25f;
		private const float OffCourseHighway = 40f;
		private const double OffCourseSeconds = 2;

		/// <summary>The whole car off the course's road meshes (TRC Main road) for longer than this stops the run.</summary>
		public const double OffRoadSeconds = 0.5;

		/// <summary>Getting further along the path than the car moved, by more than this (metres), is a shortcut.</summary>
		private const float ShortcutMargin = 10f;

		/// <summary>A jump of more than this (metres) in one frame, much more than the car's speed allows, is a reset.</summary>
		private const float ResetMetres = 25f;

		/// <summary>The car is looked for this many path segments (about 2 m each) behind and ahead of where it was.</summary>
		private const int LookBehind = 10;
		private const int LookAhead = 60;

		public Course Course { get; }
		public Phase State { get; private set; } = Phase.ToStart;

		/// <summary>What to tell the driver right now ("Drive to the start: 320 m", "Jump start!", ...).</summary>
		public string Message { get; private set; } = "Drive to the start";

		/// <summary>3, 2 or 1 during the countdown, else 0.</summary>
		public int CountdownNumber { get; private set; }

		/// <summary>Seconds since GO (the final time once finished).</summary>
		public double Elapsed { get; private set; }

		/// <summary>When GO was (or is), on the clock the run is fed.</summary>
		public double GoTime => goTime_;

		/// <summary>The time from GO to the finish line, in seconds, once finished.</summary>
		public double Result { get; private set; }

		/// <summary>Metres along the course.</summary>
		public float Progress { get; private set; }

		public float DistanceToStart { get; private set; }

		/// <summary>How many jump starts this run has had.</summary>
		public int JumpStarts { get; private set; }

		/// <summary>
		/// For battles: the countdown doesn't start by itself once the car stands still; it starts when
		/// <see cref="StartCountdown"/> is called (both drivers ready, GO at the same moment for both).
		/// </summary>
		public bool ExternalStart { get; set; }

		/// <summary>The car stands still in its start box, facing the right way (for battles, with <see cref="ExternalStart"/>).</summary>
		public bool ReadyToStart { get; private set; }

		/// <summary>The start box, in metres behind the start line: from <see cref="GridFront"/> back to <see cref="GridBack"/>.</summary>
		public float GridFront { get; set; }
		public float GridBack { get; set; } = GridDepth;

		private double stillSince_ = -1;
		private Vector3 lastSeen_;
		private double countdownStart_;
		private double goTime_;
		private double jumpUntil_ = -1;
		private double offSince_ = -1;
		private double offRoadSince_ = -1;
		private Vector3 anchor_;
		private Vector3 lastPosition_;
		private double lastTime_;
		private float lastSpeed_;
		private float lastProgress_;
		private int segment_;

		public TimeAttackRun(Course course) {
			Course = course;
		}

		public bool Active => State == Phase.ToStart || State == Phase.Countdown || State == Phase.Running;

		/// <summary>True for a second after GO, to show it on screen.</summary>
		public bool ShowGo(double now) => State == Phase.Running && now - goTime_ < 1.0;

		/// <param name="now">Game time in seconds (it stops while the game is paused).</param>
		/// <param name="timeScale">The game's speed: 1 normally, 0 while paused. Anything else stops a run.</param>
		/// <param name="onRoad">
		/// Whether any part of the car is over the course's road meshes (on them, or in the air above them); null
		/// when the course has none. The whole car off them for <see cref="OffRoadSeconds"/> stops the run.
		/// </param>
		public void Tick(double now, Vector3 position, Vector3 forward, float speedKmh, float timeScale, bool? onRoad = null) {
			speedKmh = Mathf.Abs(speedKmh);
			switch (State) {
				case Phase.ToStart:
					WaitAtStart(now, position, forward, speedKmh);
					break;
				case Phase.Countdown:
					CountDown(now, position, speedKmh);
					break;
				case Phase.Running:
					Run(now, position, speedKmh, timeScale, onRoad);
					break;
			}
		}

		public void Cancel(string reason) {
			if (!Active) {
				return;
			}
			State = Phase.Cancelled;
			CountdownNumber = 0;
			Message = reason;
		}

		/// <summary>
		/// For battles: starts the countdown so that GO is at <paramref name="goTime"/> (on this run's clock). The car
		/// must stay still from now until GO, even before the numbers show.
		/// </summary>
		public void StartCountdown(double goTime) {
			if (State != Phase.ToStart) {
				return;
			}
			State = Phase.Countdown;
			countdownStart_ = goTime - CountdownSeconds;
			goTime_ = goTime;
			anchor_ = lastSeen_;
			ReadyToStart = false;
			CountdownNumber = 0;
			Message = "Get ready";
		}

		/// <summary>Back to the start for another go, after finishing or being stopped.</summary>
		public void Restart() {
			State = Phase.ToStart;
			Message = "Drive to the start";
			ReadyToStart = false;
			CountdownNumber = 0;
			Elapsed = 0;
			Result = 0;
			Progress = 0;
			stillSince_ = -1;
			jumpUntil_ = -1;
			offSince_ = -1;
			offRoadSince_ = -1;
		}

		private void WaitAtStart(double now, Vector3 position, Vector3 forward, float speedKmh) {
			CourseGate gate = Course.Start;
			float ahead = gate.Ahead(position);
			float across = gate.Across(position);
			float up = position.y - gate.Center.y;
			DistanceToStart = Vector3.Distance(position, gate.Center);
			bool between = Mathf.Abs(across) <= gate.Width / 2f && Mathf.Abs(up) <= GridHeight;
			bool facing = Vector3.Dot(CourseGeometry.Level(forward), gate.Forward) > 0.5f;
			lastSeen_ = position;
			ReadyToStart = false;

			string hint;
			if (!between || ahead > -GridFront || ahead < -GridBack) {
				stillSince_ = -1;
				hint = DistanceToStart > GridBack + gate.Width ? $"Drive to the start: {Distance(DistanceToStart)}"
					: between && ahead > 0f ? "Back up behind the start line"
					: between && ahead > -GridFront ? $"Line up further back: at least {GridFront:0} m behind the start line"
					: between && ahead < -GridBack ? $"Move up: within {GridBack:0} m of the start line"
					: "Line up behind the start line, between its ends";
			} else if (!facing) {
				stillSince_ = -1;
				hint = "Turn around: face the way the course goes";
			} else if (speedKmh > StoppedKmh) {
				stillSince_ = -1;
				hint = "Stop behind the line";
			} else {
				if (stillSince_ < 0) {
					stillSince_ = now;
				}
				if (ExternalStart) {
					ReadyToStart = now - stillSince_ >= StillSeconds;
					hint = ReadyToStart ? "Ready" : "Hold still...";
				} else if (now - stillSince_ >= StillSeconds) {
					State = Phase.Countdown;
					countdownStart_ = now;
					anchor_ = position;
					CountdownNumber = 3;
					Message = "Get ready";
					return;
				} else {
					hint = "Hold still...";
				}
			}
			Message = now < jumpUntil_ ? "Jump start! Stop behind the line to try again" : hint;
		}

		private void CountDown(double now, Vector3 position, float speedKmh) {
			if (speedKmh > JumpKmh || Vector3.Distance(position, anchor_) > JumpMetres) {
				State = Phase.ToStart;
				stillSince_ = -1;
				CountdownNumber = 0;
				jumpUntil_ = now + 3;
				JumpStarts++;
				Message = "Jump start! Stop behind the line to try again";
				return;
			}

			if (now < countdownStart_) {
				// A battle's countdown starts a moment after both are ready, so it reaches both players in time.
				CountdownNumber = 0;
				Message = "Get ready";
				return;
			}

			double left = CountdownSeconds - (now - countdownStart_);
			if (left > 0) {
				CountdownNumber = Mathf.Clamp((int)Math.Ceiling(left), 1, 3);
				Message = "Get ready";
				return;
			}

			State = Phase.Running;
			CountdownNumber = 0;
			goTime_ = countdownStart_ + CountdownSeconds;
			Elapsed = now - goTime_;
			lastPosition_ = position;
			lastTime_ = now;
			lastSpeed_ = speedKmh;
			PathPosition at = CourseGeometry.Project(Course.Path, Course.Distances, position, 0, LookAhead);
			segment_ = at.Segment;
			lastProgress_ = Progress = at.Progress;
			offSince_ = -1;
			offRoadSince_ = -1;
			Message = "";
		}

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

			float moved = Vector3.Distance(position, lastPosition_);
			float possible = Mathf.Max(lastSpeed_, speedKmh) / 3.6f * (float)dt;
			if (moved > ResetMetres && moved > possible * 3f + 5f) {
				Cancel("The car was reset or moved, so the run was stopped.");
				return;
			}

			PathPosition at = CourseGeometry.Project(Course.Path, Course.Distances, position, segment_ - LookBehind, segment_ + LookAhead);
			if (Course.HasPath) {
				if (at.Progress - lastProgress_ > moved + ShortcutMargin) {
					Cancel("Shortcut: the car got ahead along the course faster than it drove.");
					return;
				}
				float limit = Course.Kind == CourseKind.Highway ? OffCourseHighway : OffCourseTouge;
				if (at.Offset > limit) {
					if (offSince_ < 0) {
						offSince_ = now;
					}
					if (now - offSince_ > OffCourseSeconds || at.Offset > limit * 3f) {
						Cancel("You left the course, so the run was stopped.");
						return;
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
					Cancel("You left the road, so the run was stopped.");
					return;
				}
			} else if (onRoad == true) {
				offRoadSince_ = -1;
			}

			bool farEnough = Mathf.Max(at.Progress, lastProgress_) >= Course.Length * FinishShare;
			if (farEnough && CourseGeometry.Crossed(Course.Finish, lastPosition_, position, out float fraction)) {
				Result = lastTime_ + fraction * dt - goTime_;
				Elapsed = Result;
				Progress = Course.Length;
				State = Phase.Finished;
				Message = $"Finished: {FormatTime(Result)}";
				return;
			}

			if (Elapsed > MaxRunSeconds) {
				Cancel("Over 30 minutes, so the run was stopped.");
				return;
			}

			segment_ = at.Segment;
			Progress = at.Progress;
			lastPosition_ = position;
			lastTime_ = now;
			lastSpeed_ = speedKmh;
			lastProgress_ = at.Progress;
		}

		/// <summary>"1:23.456", or "23.456" under a minute.</summary>
		public static string FormatTime(double seconds) {
			if (seconds < 0) {
				return "-" + FormatTime(-seconds);
			}
			long ms = (long)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero);
			long minutes = ms / 60000;
			double rest = (ms % 60000) / 1000.0;
			return minutes > 0
				? $"{minutes.ToString(CultureInfo.InvariantCulture)}:{rest.ToString("00.000", CultureInfo.InvariantCulture)}"
				: rest.ToString("0.000", CultureInfo.InvariantCulture);
		}

		/// <summary>"320 m" or "4.2 km".</summary>
		public static string Distance(float metres) =>
			metres >= 1000f
				? (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km"
				: metres.ToString("0", CultureInfo.InvariantCulture) + " m";
	}
}
