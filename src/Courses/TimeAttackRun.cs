// One time attack run: its settings, where it stands now, and the tick that moves it on each frame.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
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
	public sealed partial class TimeAttackRun {
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

		/// <summary>
		/// Single file on a narrow road (bskftw, Nov 2): the countdown and GO are shared, but each driver's clock starts
		/// when their car crosses the start line, so lining up further back costs nothing. Results go by time.
		/// </summary>
		public bool ClockFromLine { get; set; }

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
	}

	/// <summary>What a jump start does: start again (time attack, battles), or in a race a time penalty or disqualification.</summary>
	/// <summary>
	/// What a jump start does. Restart: time attack goes back to the start. Races pick a penalty, disqualification, a
	/// recall (0.16: everyone lines up again and the countdown starts over; the run itself goes back like Restart), or
	/// none (0.16, tournaments' "no penalties"). 0.15 copies read Recall and None as a penalty that's never added.
	/// </summary>
	public enum JumpRule : byte { Restart, Penalty, Disqualify, Recall, None }
}
