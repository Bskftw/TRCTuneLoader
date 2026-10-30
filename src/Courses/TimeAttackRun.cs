using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>One lap of a run (a point-to-point run is one lap): its time, its sectors, and whether it counts.</summary>
	public sealed class LapTime {
		/// <summary>1 for the first lap.</summary>
		public int Number;
		public double Time;

		/// <summary>Seconds for each sector of the lap, in order.</summary>
		public double[] Sectors = new double[0];

		/// <summary>False when the lap was spoiled (practice only: a shortcut, leaving the road...), with why.</summary>
		public bool Valid = true;
		public string Invalid = "";

		/// <summary>The lap began at a standing start (the first lap of a standing run), so it's slower than a flying lap.</summary>
		public bool Standing;

		/// <summary>Lap time at every <see cref="TimeAttackRun.TraceStep"/> metres along the lap: what the live delta compares with.</summary>
		public float[] Trace = new float[0];

		/// <summary>Where the car was, every <see cref="TimeAttackRun.LineStep"/> m of the lap, and its speed there (km/h): the driving line.</summary>
		public List<Vector3> Line = new List<Vector3>();
		public List<float> LineKmh = new List<float>();
	}

	/// <summary>What a jump start does: start again (time attack, battles), or in a race a time penalty or disqualification.</summary>
	public enum JumpRule : byte { Restart, Penalty, Disqualify }

	/// <summary>A sector just finished, to flash on screen.</summary>
	public struct SectorSplit {
		/// <summary>0 for the first sector.</summary>
		public int Index;
		public int Lap;

		/// <summary>Seconds for the sector itself.</summary>
		public double Time;
	}

	/// <summary>
	/// One time attack on a course, fed the car's position every frame.
	///
	/// Standing start: the car stops behind the start line, a 3-2-1-GO countdown runs, and the clock starts
	/// at GO. Moving during the countdown is a jump start. Flying start: the clock starts when the car crosses
	/// the start line, at any speed. The run ends when the car crosses the finish line after following the
	/// path (the exact moment is worked out between frames); on a circuit, crossing the start line again after
	/// a lap starts the next lap, until the laps are done (or for as long as the driver likes, with unlimited
	/// laps). Each lap is split into sectors (Course.SectorEnds).
	///
	/// A shortcut, leaving the course, a reset or a change of game speed stops the run. With unlimited laps on a
	/// circuit (practice) only the lap is spoiled: it's marked invalid and the run goes on. In battles
	/// (<see cref="AllowResets"/>) a reset is fine as long as it doesn't put the car further along the course.
	/// In races and battles since 0.15.1 (<see cref="KeepGoing"/>) only a shortcut or the game speed stops it.
	///
	/// It knows nothing about CarX: the mod feeds it positions, so the self-test can drive it too.
	/// </summary>
	public sealed class TimeAttackRun {
		public enum Phase { ToStart, Countdown, Running, Finished, Cancelled }

		public const float CountdownSeconds = 3f;

		/// <summary>The lap time is kept every this many metres along the lap, for the live delta.</summary>
		public const float TraceStep = 10f;

		/// <summary>The car's position and speed are kept every this many metres it drives, for the best line drawn on the road.</summary>
		public const float LineStep = 2f;

		/// <summary>How long the car must stand still behind the line before the countdown starts.</summary>
		private const float StillSeconds = 1f;
		private const float StoppedKmh = 0.6f;

		/// <summary>Faster than this, or further than <see cref="JumpMetres"/> from where it stood, during the countdown is a jump start.</summary>
		private const float JumpKmh = 2f;
		private const float JumpMetres = 0.4f;

		/// <summary>How far behind the start line (metres) the car can wait for the countdown.</summary>
		private const float GridDepth = 25f;
		private const float GridHeight = 4f;

		/// <summary>The finish (or the end of a lap) only counts once the car is this far along the path, so it can't be reached the wrong way.</summary>
		private const float FinishShare = 0.85f;
		private const double MaxRunSeconds = 1800;
		private const double MaxPracticeSeconds = 4 * 3600;

		/// <summary>Further from the path than this (metres) for longer than <see cref="OffCourseSeconds"/> stops the run.</summary>
		private const float OffCourseTouge = 25f;
		private const float OffCourseHighway = 40f;
		private const float OffCourseCircuit = 30f;
		private const double OffCourseSeconds = 2;

		/// <summary>The whole car off the course's road meshes (TRC Main road) for longer than this stops the run.</summary>
		public const double OffRoadSeconds = 0.5;

		/// <summary>Getting further along the path than the car moved, by more than this (metres), is a shortcut.</summary>
		private const float ShortcutMargin = 10f;

		/// <summary>A jump of more than this (metres) in one frame, much more than the car's speed allows, is a reset.</summary>
		private const float ResetMetres = 25f;

		/// <summary>In a battle, a reset may put the car this far (metres) further along the course, no more.</summary>
		public const float ResetAheadMetres = 15f;

		/// <summary>
		/// In a race or battle (<see cref="KeepGoing"/>), any reset is fine except one that puts the car this far (metres)
		/// further along: that's a teleport, not CarX's respawn, which puts the car back on the road where it was.
		/// </summary>
		public const float TeleportMetres = 100f;

		/// <summary>The car is looked for this many path segments (about 2 m each) behind and ahead of where it was.</summary>
		private const int LookBehind = 10;
		private const int LookAhead = 60;

		/// <summary>After a reset, the car is first looked for this far back along the path (segments), where resets put it.</summary>
		private const int ResetLookBehind = 200;

		/// <summary>A car this close (metres) to its race grid spot, facing the right way and stopped, is lined up.</summary>
		public const float SpotRadius = 3.5f;

		/// <summary>A car still behind the start line this long (seconds) after GO is out.</summary>
		private const double MaxBeforeLineSeconds = 90;

		public Course Course { get; }
		public Phase State { get; private set; } = Phase.ToStart;

		/// <summary>Laps to drive: 1 for a point-to-point course, 0 for unlimited (circuits).</summary>
		public int Laps { get; }

		/// <summary>The clock starts when the car crosses the start line, instead of at GO from a standstill.</summary>
		public bool Flying { get; }

		/// <summary>Unlimited laps on a circuit: a spoiled lap is marked invalid and the run goes on.</summary>
		public bool Practice => Course.IsCircuit && Laps == 0;

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

		/// <summary>Metres along the course (along this lap, on a circuit).</summary>
		public float Progress { get; private set; }

		/// <summary>
		/// Metres driven along the course in the whole run, over every lap: what battles and races compare. Before
		/// the start line (the back of a race grid), how far behind it, as a negative number.
		/// </summary>
		public float TotalProgress => BeforeLine ? Math.Min(0f, Course.Start.Ahead(lastPosition_)) : (Lap - 1) * Course.Length + Progress;

		public float DistanceToStart { get; private set; }

		/// <summary>How many jump starts this run has had.</summary>
		public int JumpStarts { get; private set; }

		/// <summary>How many resets a battle let through.</summary>
		public int Resets { get; private set; }

		/// <summary>The lap being driven: 1 for the first.</summary>
		public int Lap { get; private set; } = 1;

		/// <summary>Seconds since the lap began.</summary>
		public double LapElapsed => Elapsed - lapStart_;

		/// <summary>Every finished lap, in order.</summary>
		public List<LapTime> LapTimes { get; } = new List<LapTime>();

		/// <summary>The fastest valid lap so far, or null.</summary>
		public LapTime BestLap => LapTimes.Where(l => l.Valid).OrderBy(l => l.Time).FirstOrDefault();

		/// <summary>Whether the lap being driven still counts, and why not.</summary>
		public bool LapValid { get; private set; } = true;
		public string LapInvalid { get; private set; } = "";

		/// <summary>Seconds for each sector of this lap finished so far (all of them once a point-to-point run is over).</summary>
		public List<double> CurrentSectors {
			get {
				var sectors = new List<double>(splits_.Count);
				for (int i = 0; i < splits_.Count; i++) {
					sectors.Add(splits_[i] - (i == 0 ? lapStart_ : splits_[i - 1]));
				}
				return sectors;
			}
		}

		/// <summary>The speed (km/h) caught at each of the course's speed traps so far, in the order driven (every lap).</summary>
		public List<float> TrapSpeeds { get; } = new List<float>();

		/// <summary>The trap just driven through (its number on the course, from 0) and the speed, for the flash.</summary>
		public (int Index, float Kmh)? LastTrap { get; private set; }

		/// <summary>All the speeds caught at traps added up: what a speed trap race is won on.</summary>
		public float TrapTotal => TrapSpeeds.Sum();

		private readonly HashSet<int> trapsThisLap_ = new HashSet<int>();
		private readonly List<Vector3> line_ = new List<Vector3>();
		private readonly List<float> lineKmh_ = new List<float>();

		/// <summary>The sector being driven (0 for the first) and the last one finished.</summary>
		public int Sector { get; private set; }
		public SectorSplit? LastSplit { get; private set; }

		/// <summary>
		/// The lap to compare with for the live delta: lap time at every <see cref="TraceStep"/> m (a best lap's
		/// or best run's <see cref="LapTime.Trace"/>). Null for none.
		/// </summary>
		public float[] Reference { get; set; }

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
		/// For battles: the countdown doesn't start by itself once the car stands still; it starts when
		/// <see cref="StartCountdown"/> is called (both drivers ready, GO at the same moment for both).
		/// </summary>
		public bool ExternalStart { get; set; }

		/// <summary>For battles: a reset doesn't stop the run, unless it puts the car further along the course.</summary>
		public bool AllowResets { get; set; }

		/// <summary>
		/// For races and battles (bskftw, 0.15.1: "the race should just continue"): a reset, leaving the course or
		/// leaving the road never stops the run; the lap is only marked invalid. A shortcut still stops it.
		/// </summary>
		public bool KeepGoing { get; set; }

		/// <summary>The car stands still in its start box, facing the right way (for battles, with <see cref="ExternalStart"/>).</summary>
		public bool ReadyToStart { get; private set; }

		/// <summary>The start box, in metres behind the start line: from <see cref="GridFront"/> back to <see cref="GridBack"/>.</summary>
		public float GridFront { get; set; }
		public float GridBack { get; set; } = GridDepth;

		/// <summary>For races: the car's own grid spot. Set, it's lined up only there (instead of anywhere in the start box).</summary>
		public GridSpot? Spot { get; set; }

		/// <summary>What a jump start does (races pick penalty or disqualification).</summary>
		public JumpRule OnJump { get; set; } = JumpRule.Restart;

		/// <summary>The car hasn't crossed the start line since GO (it started behind it, like the back of a race grid).</summary>
		public bool BeforeLine { get; private set; }

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
		private bool hasLast_;

		/// <summary>When the lap began, in seconds since GO.</summary>
		private double lapStart_;
		private readonly List<double> splits_ = new List<double>();
		private readonly List<float> trace_ = new List<float>();

		public TimeAttackRun(Course course, int laps = 1, bool flying = false) {
			Course = course;
			Laps = course.IsCircuit ? Math.Max(0, laps) : 1;
			Flying = flying;
			Message = flying ? "Drive through the start line to start the clock" : "Drive to the start";
		}

		public bool Active => State == Phase.ToStart || State == Phase.Countdown || State == Phase.Running;

		/// <summary>True for a second after GO, to show it on screen.</summary>
		public bool ShowGo(double now) => State == Phase.Running && !Flying && now - goTime_ < 1.0;

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
				if (Reference == null || Reference.Length < 2) {
					return null;
				}
				float at = Progress / TraceStep;
				int i = Mathf.FloorToInt(at);
				if (i < 0 || i + 1 >= Reference.Length) {
					return null;
				}
				double then = Reference[i] + (Reference[i + 1] - Reference[i]) * (at - i);
				return LapElapsed - then;
			}
		}

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
					if (Flying) {
						WaitForFlyingStart(now, position, speedKmh);
					} else {
						WaitAtStart(now, position, forward, speedKmh);
					}
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
			Message = Flying ? "Drive through the start line to start the clock" : "Drive to the start";
			ReadyToStart = false;
			CountdownNumber = 0;
			Elapsed = 0;
			Result = 0;
			Progress = 0;
			Lap = 1;
			LapTimes.Clear();
			TrapSpeeds.Clear();
			LastTrap = null;
			trapsThisLap_.Clear();
			LapValid = true;
			LapInvalid = "";
			Sector = 0;
			LastSplit = null;
			Resets = 0;
			stillSince_ = -1;
			jumpUntil_ = -1;
			offSince_ = -1;
			offRoadSince_ = -1;
			hasLast_ = false;
			jumped_ = false;
			BeforeLine = false;
		}

		private bool jumped_;

		private void WaitAtStart(double now, Vector3 position, Vector3 forward, float speedKmh) {
			if (Spot.HasValue) {
				WaitInSpot(now, position, forward, speedKmh, Spot.Value);
				return;
			}
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

		/// <summary>Races: lined up when stopped in the car's own grid spot, facing the way the grid faces.</summary>
		private void WaitInSpot(double now, Vector3 position, Vector3 forward, float speedKmh, GridSpot spot) {
			lastSeen_ = position;
			lastPosition_ = position;
			ReadyToStart = false;
			float away = Vector3.Distance(new Vector3(position.x, spot.Position.y, position.z), spot.Position);
			DistanceToStart = away;
			bool facing = Vector3.Dot(CourseGeometry.Level(forward), spot.Forward) > 0.5f;
			string hint;
			if (away > SpotRadius || Mathf.Abs(position.y - spot.Position.y) > GridHeight) {
				stillSince_ = -1;
				hint = $"Drive to your grid spot: {Distance(away)}";
			} else if (!facing) {
				stillSince_ = -1;
				hint = "Turn around: face the way the grid faces";
			} else if (speedKmh > StoppedKmh) {
				stillSince_ = -1;
				hint = "Stop in your spot";
			} else {
				if (stillSince_ < 0) {
					stillSince_ = now;
				}
				ReadyToStart = now - stillSince_ >= StillSeconds;
				hint = ReadyToStart ? "Ready" : "Hold still...";
			}
			Message = hint;
		}

		/// <summary>Flying start: the clock starts at the moment the car crosses the start line (worked out between frames).</summary>
		private void WaitForFlyingStart(double now, Vector3 position, float speedKmh) {
			DistanceToStart = Vector3.Distance(position, Course.Start.Center);
			if (hasLast_ && Vector3.Distance(position, lastPosition_) <= ResetMetres &&
				CourseGeometry.Crossed(Course.Start, lastPosition_, position, out float fraction)) {
				goTime_ = lastTime_ + fraction * (now - lastTime_);
				BeginRunning(now, position, speedKmh);
				return;
			}
			hasLast_ = true;
			lastPosition_ = position;
			lastTime_ = now;
			Message = DistanceToStart > 60f
				? $"Drive through the start line to start the clock: {Distance(DistanceToStart)}"
				: "Drive through the start line to start the clock";
		}

		private void CountDown(double now, Vector3 position, float speedKmh) {
			bool moved = speedKmh > JumpKmh || Vector3.Distance(position, anchor_) > JumpMetres;
			if (moved && OnJump == JumpRule.Disqualify) {
				JumpStarts++;
				Cancel("Jump start: disqualified.");
				return;
			}
			if (moved && OnJump == JumpRule.Penalty) {
				// The countdown carries on; the penalty goes on the time at the finish.
				if (!jumped_) {
					jumped_ = true;
					JumpStarts++;
				}
				moved = false;
			}
			if (moved) {
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
				Message = jumped_ ? "Jump start! Time penalty" : "Get ready";
				return;
			}

			goTime_ = countdownStart_ + CountdownSeconds;
			BeginRunning(now, position, speedKmh);
		}

		private void BeginRunning(double now, Vector3 position, float speedKmh) {
			State = Phase.Running;
			CountdownNumber = 0;
			Elapsed = now - goTime_;
			lastPosition_ = position;
			lastTime_ = now;
			lastSpeed_ = speedKmh;
			PathPosition at = CourseGeometry.Project(Course.Path, Course.Distances, position, 0, LookAhead);
			segment_ = at.Segment;
			lastProgress_ = Progress = at.Progress;
			offSince_ = -1;
			offRoadSince_ = -1;
			Lap = 1;
			BeginLap(0);
			// Well behind the line (the back of a race grid): nothing counts until the car crosses it.
			BeforeLine = !Flying && Course.Start.Ahead(position) < -GridDepth;
			if (BeforeLine) {
				lastProgress_ = Progress = 0f;
				segment_ = 0;
			}
			Message = "";
		}

		/// <summary>A new lap starting <paramref name="at"/> seconds after GO.</summary>
		private void BeginLap(double at) {
			lapStart_ = at;
			refSegment_ = 0;
			lineRefTime_ = refLine_ != null ? 0 : -1;
			LapValid = true;
			LapInvalid = "";
			Sector = 0;
			splits_.Clear();
			trace_.Clear();
			trace_.Add(0f);
			trapsThisLap_.Clear();
			line_.Clear();
			lineKmh_.Clear();
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

			if (BeforeLine) {
				if (CourseGeometry.Crossed(Course.Start, lastPosition_, position, out _)) {
					BeforeLine = false;
					PathPosition from = CourseGeometry.Project(Course.Path, Course.Distances, position, 0, LookAhead);
					segment_ = from.Segment;
					lastProgress_ = Progress = from.Progress;
				} else if (Elapsed > MaxBeforeLineSeconds) {
					Cancel("Never crossed the start line, so the run was stopped.");
					return;
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
			LastSplit = new SectorSplit { Index = Sector, Lap = Lap, Time = at - started };
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

		/// <summary>"+0.125" or "-1.200": a gap in seconds, with its sign.</summary>
		public static string FormatGap(double seconds) =>
			(seconds < 0 ? "-" : "+") + Math.Abs(seconds).ToString("0.000", CultureInfo.InvariantCulture);

		/// <summary>"320 m" or "4.2 km".</summary>
		public static string Distance(float metres) =>
			metres >= 1000f
				? (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km"
				: metres.ToString("0", CultureInfo.InvariantCulture) + " m";
	}
}
