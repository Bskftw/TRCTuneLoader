// One player's side of a race lobby and race: its state, hosting or joining, incidents, scores and penalties.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// An open race between TRC Tune Loader users (no limit since 0.16), as one of them sees it. The host opens it, anyone in the
	/// room can join, and the host starts it (optionally after qualifying). Every mod times its own car with its own
	/// TimeAttackRun from a 3-2-1-GO that lands at the same moment on every screen (each driver keeps its clock in
	/// step with the host's), and tells everyone its progress and result, so positions show live and the results
	/// come in even if the host leaves. Nothing here knows about CarX or KSL, so the self-test can run a whole race.
	///
	/// Beats go out 5 times a second with up to 8 racers, twice a second with more and once a second past 16 (0.16,
	/// custom servers with bigger rooms), except where it matters: the countdown, the first seconds after GO and the
	/// last 150 m, where it's 5 times a second again (twice past 16).
	///
	/// 0.16: a race can be run to a regulation (each mod checks its own car), or just for fun; a jump start can recall
	/// the start for everyone; the host may only run it, not drive; and the host can add console players, whose
	/// runs the host's mod times from where CarX shows their car (<see cref="AddConsole"/>, <see cref="SetConsolePose"/>).
	/// </summary>
	public sealed partial class RaceSession {
		private const double LobbySeconds = 2;
		private const double PresenceSeconds = 1;
		private const double FastSeconds = 0.2;
		private const double SlowSeconds = 0.5;
		private const double SlowestSeconds = 1.0;
		private const int SlowAbove = 8;

		/// <summary>A jump start this long after GO (heard late) still recalls the start.</summary>
		public const double RecallAfterGoSeconds = 3;
		private const float FastNearFinishMetres = 150f;
		private const double FastAfterGoSeconds = 3;
		private const double CourseResendSeconds = 4;
		public const double TimeoutSeconds = 12;
		private const double ReadyHoldSeconds = 1;
		private const double CountdownLeadSeconds = 1.5;
		private const double JoinResendSeconds = 2;
		private const double QualifyingRestartSeconds = 3;
		private const double ResultsRepeatSeconds = 3;
		private const int OffsetSamples = 15;

		public uint Id { get; }
		public bool IsHost { get; }
		public ulong Me { get; }
		public ulong HostId { get; }
		public string HostName { get; private set; }
		public RaceSettings Settings { get; private set; }
		public Course Course { get; private set; }
		public string CourseCode { get; private set; }
		public RacePhase Phase { get; private set; }
		public int RaceNumber { get; private set; }
		public List<RaceDriver> Drivers { get; } = new List<RaceDriver>();
		public string Message { get; private set; } = "";

		/// <summary>This driver's run: a qualifying run, or the race.</summary>
		public TimeAttackRun Run { get; private set; }

		/// <summary>The host's clock minus this one's (0 for the host).</summary>
		public double ClockOffset { get; private set; }
		public double RoundTrip { get; private set; }

		/// <summary>Tournament lines from the host (standings, what's next).</summary>
		public List<string> Lines { get; private set; } = new List<string>();

		/// <summary>Knockout: the battle the host is calling now (0 = none).</summary>
		public uint MatchId { get; private set; }
		public ulong MatchA { get; private set; }
		public ulong MatchB { get; private set; }
		public BattleFormat MatchFormat { get; private set; }
		public int MatchGame { get; private set; }
		public int MatchLaps { get; private set; } = 1;

		/// <summary>The results of the last race (the host's when they arrived, else worked out here), or null.</summary>
		public List<RaceResultRow> Results { get; private set; }
		public int ResultsFor { get; private set; } = -1;

		/// <summary>Messages for the host from drivers (match reports), for the tournament.</summary>
		public List<RaceMessage> Reports { get; } = new List<RaceMessage>();

		/// <summary>The session is over for this driver (left, removed, or the host closed it).</summary>
		public bool Ended { get; private set; }
		public bool HostGone { get; private set; }

		/// <summary>When the race ends (this clock), or -1. When qualifying ends, or -1.</summary>
		public double EndAt { get; private set; } = -1;
		public double QualifyingEnds { get; private set; } = -1;
		public bool Joined => Drivers.Any(d => d.Id == Me);

		private readonly string myName_;
		private readonly Func<BattleCar> checkCar_;
		private readonly Func<double> personalBest_;
		private readonly List<RaceMessage> outbox_ = new List<RaceMessage>();
		private readonly List<double> offsets_ = new List<double>();
		private readonly HashSet<ulong> kicked_ = new HashSet<ulong>();
		private double lastLobby_ = double.MinValue;
		private bool lobbyDirty_;
		private double lastBeat_ = double.MinValue;
		private double lastHostBeat_ = double.MinValue;
		private double lastCourseSent_ = double.MinValue;
		private double lastJoin_ = double.MinValue;
		private double lastHeardHost_;
		private double goHostTime_;
		private int countdownId_;
		private int countdownStarted_;
		private double endAtHost_ = -1;
		private double qualifyingEndsHost_ = -1;
		private double bothReadySince_ = -1;
		private int runFor_ = -1;
		private RacerState myState_;
		private double myTime_;
		private double myPenalty_;
		private string myReason_ = "";
		private double myBestQualifying_;
		private int myQualifyingRuns_;
		private double qualifyingRestartAt_ = -1;
		private bool qualifyingRun_;
		private BattleCar myCar_;
		private double resultsSentAt_ = -1;
		private int resultsSends_;
		private RacerState lastBeatState_;
		private int eliminations_;
		private double myCarCheckedAt_;
		private const double RecheckSeconds = 3;
		private int myJumps_;
		private int myJumpedCountdown_;
		private int recallsSeen_;

		/// <summary>How many times this race's start was recalled (a jump start under the recall rule), and who jumped last.</summary>
		public int Recalls { get; private set; }
		public string RecallBy { get; private set; } = "";

		/// <summary>This driver's Elo for the race's ratings, said in the beats (the mod sets it).</summary>
		public float MyElo { get; set; }

		/// <summary>When GO is (this clock) for the countdown running now, or -1.</summary>
		public double GoAt => (Phase == RacePhase.Countdown || Phase == RacePhase.Running) && countdownId_ > 0 ? goHostTime_ - ClockOffset : -1;
		public int CountdownId => countdownId_;

		/// <summary>The host's headlights should show the start: asked for, or a console player is racing.</summary>
		public bool NeedsLightStart => IsHost && !IsGame && (Settings.LightStart || Racers.Any(d => d.Console));

		private RaceSession(uint id, bool host, ulong me, string myName, ulong hostId, Func<BattleCar> checkCar, Func<double> personalBest, double now) {
			Id = id;
			IsHost = host;
			Me = me;
			myName_ = BattleMessage.PlainName(myName);
			HostId = hostId;
			checkCar_ = checkCar;
			personalBest_ = personalBest;
			lastHeardHost_ = now;
		}

		/// <summary>Opens a race on <paramref name="course"/>: the host is its first driver.</summary>
		public static RaceSession Host(uint id, ulong me, string myName, Course course, RaceSettings settings, Func<BattleCar> checkCar, Func<double> personalBest, double now) {
			var s = new RaceSession(id, true, me, myName, me, checkCar, personalBest, now) {
				HostName = BattleMessage.PlainName(myName),
				Settings = settings.Copy(),
				Course = course,
				CourseCode = course?.Code ?? "",
				Phase = RacePhase.Lobby,
			};
			s.Drivers.Add(new RaceDriver { Id = me, Name = s.HostName, HasCourse = true, LastHeard = now });
			s.lobbyDirty_ = true;
			s.Message = "Race open: waiting for drivers to join";
			return s;
		}

		/// <summary>Joins the race in <paramref name="lobby"/>; <paramref name="course"/> is this PC's copy of it, or null until the host sends it.</summary>
		public static RaceSession Join(RaceMessage lobby, ulong me, string myName, Course course, Func<BattleCar> checkCar, Func<double> personalBest, double now) {
			var s = new RaceSession(lobby.RaceId, false, me, myName, lobby.From, checkCar, personalBest, now) {
				Course = course,
				CourseCode = lobby.CourseCode,
			};
			s.ApplyLobby(lobby, now);
			s.SendJoin(now);
			s.Message = "Joining...";
			return s;
		}

		public List<RaceMessage> TakeOutbox() {
			var messages = new List<RaceMessage>(outbox_);
			outbox_.Clear();
			return messages;
		}

		public RaceDriver MyDriver => Drivers.FirstOrDefault(d => d.Id == Me);

		/// <summary>A room game (anywhere on the map), not a race on a course.</summary>
		public bool IsGame => Settings.Mode != RoomMode.Race;

		/// <summary>King of the hill: the hill (where the host was at the start) and how far it reaches.</summary>
		public Vector3 Zone { get; private set; }
		public float ZoneRadius { get; private set; }
		public const float HillRadius = 25f;

		/// <summary>Called by the mod when it notices an incident of this car's (a hit, a reset...): counted and told to the host.</summary>
		public void AddIncident(string what) {
			myIncidents_++;
			myLastIncident_ = what ?? "";
			lastBeat_ = double.MinValue;
		}

		/// <summary>Cut-up: this player's points so far, as their own mod counts them.</summary>
		public void SetScore(float score) {
			myScore_ = score;
		}

		/// <summary>
		/// Host, after a race: a time penalty (seconds, added to what's there) or a disqualification for a driver. The
		/// results are worked out again and sent to everyone.
		/// </summary>
		public void Penalize(ulong id, double seconds, bool disqualify, double now) {
			RaceDriver d = Drivers.FirstOrDefault(x => x.Id == id);
			if (!IsHost || d == null || Results == null || (Settings.NoPenalties && (seconds != 0 || disqualify))) {
				return;
			}
			if (disqualify) {
				d.HostDisqualified = true;
			} else if (seconds == 0) {
				d.HostPenalty = 0;
				d.HostDisqualified = false;
			} else {
				d.HostPenalty += seconds;
			}
			Results = ResultsFromDrivers();
			resultsSentAt_ = now;
			resultsSends_ = 0;
		}

		private int myIncidents_;
		private string myLastIncident_ = "";
		private float myScore_;
		private Vector3 myPosition_;
		private Vector3 myForward_;
		private float myKmh_;
		public List<RaceDriver> Racers => Drivers.Where(d => d.Racer).ToList();
		public bool IAmRacing => MyDriver?.Racer == true && (Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running);
	}
}
