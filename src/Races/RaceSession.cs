using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>One driver in a race lobby, as this mod knows them (from the host's lobby and their own beats).</summary>
	public sealed class RaceDriver {
		public ulong Id;
		public string Name = "";

		/// <summary>In the race being run (heats and knockouts run only some of the lobby).</summary>
		public bool Racer = true;
		public int Slot = -1;
		public int EliminatedOnLap;

		public bool Ready;
		public bool Spec;
		public string TuneSet = "";
		public string Car = "";
		public bool HasCourse;
		public float Progress;
		public int LapsDone;
		public RacerState State;
		public double Time;
		public double Penalty;
		public string Reason = "";
		public double BestLap;
		public double BestQualifying;
		public int QualifyingRuns;
		public double PersonalBest;

		/// <summary>The race number their beats are about, when they last said.</summary>
		public int RaceNumber = -1;
		public double LastHeard;
		public double SentAt;
		public double SentHeardAt = -1;

		/// <summary>
		/// When they measured <see cref="Progress"/> (their clock: for working out their speed), when it got here (this
		/// clock: for how far they've gone since), and how fast it was growing.
		/// </summary>
		public double ProgressAt = -1;
		public double ProgressHeardAt = -1;
		public double Speed;
	}

	/// <summary>Where a driver is in the race right now: their place, and metres along it.</summary>
	public sealed class RaceStanding {
		public RaceDriver Driver;
		public int Position;
		public float Progress;
	}

	/// <summary>
	/// An open race between up to 16 TRC Tune Loader users, as one of them sees it. The host opens it, anyone in the
	/// room can join, and the host starts it (optionally after qualifying). Every mod times its own car with its own
	/// TimeAttackRun from a 3-2-1-GO that lands at the same moment on every screen (each driver keeps its clock in
	/// step with the host's), and tells everyone its progress and result, so positions show live and the results
	/// come in even if the host leaves. Nothing here knows about CarX or KSL, so the self-test can run a whole race.
	///
	/// Beats go out 5 times a second with up to 8 racers and twice a second with more, except where it matters:
	/// the countdown, the first seconds after GO and the last 150 m, where it's 5 times a second again.
	/// </summary>
	public sealed class RaceSession {
		private const double LobbySeconds = 2;
		private const double PresenceSeconds = 1;
		private const double FastSeconds = 0.2;
		private const double SlowSeconds = 0.5;
		private const int SlowAbove = 8;
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
		private double hostSentAt_;
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
				CourseCode = course.Code,
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
		public List<RaceDriver> Racers => Drivers.Where(d => d.Racer).ToList();
		public bool IAmRacing => MyDriver?.Racer == true && (Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running);

		// ---- Host actions ----

		/// <summary>Host: a different course for the next race (a tournament round on another road). Only between races.</summary>
		public void SetCourse(Course course) {
			if (!IsHost || Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running || Phase == RacePhase.Qualifying) {
				return;
			}
			Course = course;
			CourseCode = course.Code;
			foreach (RaceDriver d in Drivers.Where(d => d.Id != Me)) {
				d.HasCourse = false;
			}
			lobbyDirty_ = true;
			lastCourseSent_ = double.MinValue;
		}

		/// <summary>Host: changes the rules between races (laps, grid, jump starts...).</summary>
		public void SetSettings(RaceSettings settings) {
			if (!IsHost) {
				return;
			}
			Settings = settings.Copy();
			lobbyDirty_ = true;
		}

		/// <summary>Host: tournament lines everyone sees.</summary>
		public void SetLines(List<string> lines) {
			if (!IsHost) {
				return;
			}
			Lines = lines ?? new List<string>();
			lobbyDirty_ = true;
		}

		/// <summary>Host: calls a knockout battle between two drivers (0 to clear).</summary>
		public void CallMatch(uint matchId, ulong a, ulong b, BattleFormat format, int game, int laps) {
			if (!IsHost) {
				return;
			}
			MatchId = matchId;
			MatchA = a;
			MatchB = b;
			MatchFormat = format;
			MatchGame = game;
			MatchLaps = Math.Max(1, laps);
			lobbyDirty_ = true;
		}

		/// <summary>Host: everyone drives qualifying runs on their own for <see cref="RaceSettings.QualifyingMinutes"/>.</summary>
		public void StartQualifying(double now) {
			if (!IsHost || Phase != RacePhase.Lobby && Phase != RacePhase.Finished) {
				return;
			}
			foreach (RaceDriver d in Drivers) {
				d.BestQualifying = 0;
				d.QualifyingRuns = 0;
			}
			myBestQualifying_ = 0;
			myQualifyingRuns_ = 0;
			qualifyingEndsHost_ = now + Settings.QualifyingMinutes * 60.0;
			QualifyingEnds = qualifyingEndsHost_;
			Phase = RacePhase.Qualifying;
			lobbyDirty_ = true;
			lastHostBeat_ = double.MinValue;
			Message = "Qualifying";
		}

		/// <summary>Host: ends qualifying early.</summary>
		public void EndQualifying(double now) {
			if (IsHost && Phase == RacePhase.Qualifying) {
				qualifyingEndsHost_ = now;
				QualifyingEnds = now;
			}
		}

		/// <summary>
		/// Host: lines up a race. <paramref name="racers"/> picks who races (null: everyone in the lobby);
		/// <paramref name="order"/> sets the grid when the tournament decides it (else the race's grid order).
		/// </summary>
		public void StartRace(double now, IList<ulong> racers = null, IList<ulong> order = null) {
			if (!IsHost || Course == null || Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running) {
				return;
			}
			RaceNumber++;
			foreach (RaceDriver d in Drivers) {
				d.Racer = racers == null || racers.Contains(d.Id);
				d.Slot = -1;
				d.EliminatedOnLap = 0;
				d.State = RacerState.Racing;
				d.Ready = false;
				d.Progress = 0;
				d.LapsDone = 0;
				d.Time = 0;
				d.Penalty = 0;
				d.Reason = "";
				d.BestLap = 0;
				d.ProgressAt = -1;
			}
			List<RaceDriver> grid = GridOrderOf(Racers, order);
			for (int i = 0; i < grid.Count; i++) {
				grid[i].Slot = i;
			}
			if (Settings.Elimination && Course.IsCircuit) {
				// One lap per driver knocked out, so the last lap is between two.
				Settings.Laps = Math.Max(1, grid.Count - 1);
			}
			eliminations_ = 0;
			Results = null;
			ResultsFor = -1;
			endAtHost_ = -1;
			EndAt = -1;
			bothReadySince_ = -1;
			Phase = RacePhase.Gridding;
			lobbyDirty_ = true;
			lastHostBeat_ = double.MinValue;
			Message = "Line up in your grid spot";
		}

		/// <summary>Host: starts the countdown now, whoever is lined up.</summary>
		public void GoNow(double now) {
			if (IsHost && Phase == RacePhase.Gridding) {
				StartCountdown(now);
			}
		}

		/// <summary>Host: ends the race in <paramref name="seconds"/> (0: now). Anyone still driving then doesn't finish.</summary>
		public void EndRace(double now, double seconds) {
			if (!IsHost || Phase != RacePhase.Running && Phase != RacePhase.Countdown && Phase != RacePhase.Gridding) {
				return;
			}
			if (Phase != RacePhase.Running || seconds <= 0) {
				endAtHost_ = now;
			} else {
				endAtHost_ = endAtHost_ >= 0 ? Math.Min(endAtHost_, now + seconds) : now + seconds;
			}
			EndAt = endAtHost_;
			lastHostBeat_ = double.MinValue;
		}

		/// <summary>Host: back to the lobby after a race (results stay until the next one).</summary>
		public void BackToLobby() {
			if (IsHost && (Phase == RacePhase.Finished || Phase == RacePhase.Qualifying)) {
				Phase = RacePhase.Lobby;
				lobbyDirty_ = true;
			}
		}

		/// <summary>Host: removes a driver from the lobby (they can't join again).</summary>
		public void Kick(ulong id, double now) {
			if (!IsHost || id == Me) {
				return;
			}
			RaceDriver d = Drivers.FirstOrDefault(x => x.Id == id);
			if (d == null) {
				return;
			}
			kicked_.Add(id);
			if (Phase == RacePhase.Running && d.Racer && d.State == RacerState.Racing) {
				d.State = RacerState.Out;
				d.Reason = "removed by the host";
			}
			Drivers.Remove(d);
			lobbyDirty_ = true;
		}

		/// <summary>Closes the race (host) or leaves it (driver).</summary>
		public void Leave(double now) {
			if (Ended) {
				return;
			}
			if (IsHost) {
				Phase = RacePhase.Closed;
				SendLobby(now);
				Message = "You closed the race";
			} else {
				outbox_.Add(New(RaceKind.Leave));
				Message = "You left the race";
			}
			Run?.Cancel("You left the race.");
			Ended = true;
		}

		// ---- Receiving ----

		public void Receive(RaceMessage m, double now) {
			if (m == null || m.RaceId != Id || m.From == Me || Ended) {
				return;
			}
			if (m.From == HostId) {
				lastHeardHost_ = now;
				HostGone = false;
			}
			switch (m.Kind) {
				case RaceKind.Lobby:
					if (!IsHost && m.From == HostId) {
						ApplyLobby(m, now);
					}
					break;

				case RaceKind.Join:
					if (IsHost) {
						OnJoin(m, now);
					}
					break;

				case RaceKind.Leave:
					if (IsHost) {
						RaceDriver gone = Drivers.FirstOrDefault(d => d.Id == m.From);
						if (gone != null) {
							if (Phase == RacePhase.Running && gone.Racer && gone.State == RacerState.Racing) {
								gone.State = RacerState.Out;
								gone.Reason = "left the race";
							} else {
								Drivers.Remove(gone);
							}
							lobbyDirty_ = true;
						}
					}
					break;

				case RaceKind.Course:
					if (!IsHost && Course == null && m.From == HostId && m.Course != null && m.Course.Code == CourseCode) {
						Course = m.Course;
					}
					break;

				case RaceKind.Beat:
					OnBeat(m, now);
					break;

				case RaceKind.HostBeat:
					if (!IsHost && m.From == HostId) {
						OnHostBeat(m, now);
					}
					break;

				case RaceKind.Results:
					if (!IsHost && m.From == HostId && m.RaceNumber == RaceNumber) {
						Results = m.Rows;
						ResultsFor = m.RaceNumber;
						if (Phase == RacePhase.Running || Phase == RacePhase.Countdown || Phase == RacePhase.Gridding) {
							Phase = RacePhase.Finished;
						}
					}
					break;

				case RaceKind.MatchReport:
					if (IsHost) {
						Reports.Add(m);
					}
					break;
			}
		}

		private void ApplyLobby(RaceMessage m, double now) {
			HostName = BattleMessage.PlainName(m.HostName);
			Settings = m.Settings;
			if (m.CourseCode != CourseCode) {
				CourseCode = m.CourseCode;
				Course = null;
			}
			Lines = m.Lines;
			MatchId = m.MatchId;
			MatchA = m.MatchA;
			MatchB = m.MatchB;
			MatchFormat = m.MatchFormat;
			MatchGame = m.MatchGame;
			MatchLaps = m.MatchLaps;
			if (m.Kicked.Contains(Me)) {
				Ended = true;
				Run?.Cancel("Removed from the race by the host.");
				Message = "The host removed you from the race";
				return;
			}
			if (m.Phase == RacePhase.Closed) {
				Ended = true;
				Run?.Cancel("The host closed the race.");
				Message = "The host closed the race";
				return;
			}
			if (m.RaceNumber != RaceNumber) {
				RaceNumber = m.RaceNumber;
				Results = null;
			}
			if (!(Phase == RacePhase.Finished && Results != null && m.Phase == RacePhase.Running)) {
				Phase = m.Phase;
			}

			// The host's list decides who's in; what each driver said about themselves stays.
			var keep = Drivers.ToDictionary(d => d.Id);
			Drivers.Clear();
			foreach (LobbyEntry e in m.Entries) {
				if (!keep.TryGetValue(e.Id, out RaceDriver d)) {
					d = new RaceDriver { Id = e.Id, LastHeard = now };
				}
				d.Name = BattleMessage.PlainName(e.Name);
				d.Racer = e.Racer;
				d.Slot = e.Slot;
				d.EliminatedOnLap = e.EliminatedOnLap;
				Drivers.Add(d);
			}
			if (!Joined && m.Entries.Count >= m.Settings.MaxPlayers) {
				Message = "The race is full";
			}
		}

		private void OnJoin(RaceMessage m, double now) {
			if (kicked_.Contains(m.From)) {
				return;
			}
			RaceDriver d = Drivers.FirstOrDefault(x => x.Id == m.From);
			if (d == null) {
				if (Drivers.Count >= Settings.MaxPlayers) {
					return;
				}
				bool racing = Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running;
				d = new RaceDriver { Id = m.From, Racer = !racing, LastHeard = now };
				Drivers.Add(d);
			}
			d.Name = BattleMessage.PlainName(m.Name);
			d.HasCourse = m.HasCourse;
			d.LastHeard = now;
			lobbyDirty_ = true;
		}

		private void OnBeat(RaceMessage m, double now) {
			RaceDriver d = Drivers.FirstOrDefault(x => x.Id == m.From);
			if (d == null) {
				return;
			}
			d.LastHeard = now;
			d.SentAt = m.SentAt;
			d.SentHeardAt = now;
			d.Spec = m.Spec;
			d.TuneSet = m.TuneSet;
			d.Car = m.Car;
			d.HasCourse = m.HasCourse;
			d.BestQualifying = m.BestQualifying;
			d.QualifyingRuns = m.QualifyingRuns;
			d.PersonalBest = m.PersonalBest;
			if (m.RaceNumber != RaceNumber) {
				return;
			}
			d.RaceNumber = m.RaceNumber;
			d.Ready = m.Ready;
			if (d.ProgressAt >= 0 && m.SentAt > d.ProgressAt && m.SentAt - d.ProgressAt < 1.5) {
				double speed = Math.Max(0, (m.Progress - d.Progress) / (m.SentAt - d.ProgressAt));
				d.Speed += (speed - d.Speed) * 0.5;
			}
			if (d.ProgressAt < 0 || m.SentAt > d.ProgressAt) {
				d.Progress = m.Progress;
				d.ProgressAt = m.SentAt;
				d.ProgressHeardAt = now;
			}
			d.LapsDone = m.LapsDone;
			// The host decides who's eliminated or removed; anything else comes from the driver's own mod.
			if (d.State != RacerState.Eliminated && !(IsHost && d.State == RacerState.Out && m.State == RacerState.Racing)) {
				d.State = m.State;
			}
			d.Time = m.Time;
			d.Penalty = m.Penalty;
			d.Reason = m.Reason;
			d.BestLap = m.BestLap;
		}

		private void OnHostBeat(RaceMessage m, double now) {
			foreach (var (id, echo, hold) in m.Echoes) {
				if (id != Me) {
					continue;
				}
				double roundTrip = now - echo - hold;
				if (roundTrip >= 0 && roundTrip < 5) {
					offsets_.Add(m.SentAt + roundTrip / 2 - now);
					if (offsets_.Count > OffsetSamples) {
						offsets_.RemoveAt(0);
					}
					ClockOffset = offsets_.OrderBy(o => o).ElementAt(offsets_.Count / 2);
					RoundTrip = roundTrip;
				}
			}
			hostSentAt_ = m.SentAt;
			if (m.RaceNumber != RaceNumber) {
				return;
			}
			if (m.Phase == RacePhase.Countdown || m.Phase == RacePhase.Running) {
				goHostTime_ = m.GoHostTime;
				countdownId_ = m.CountdownId;
			}
			if (!(Phase == RacePhase.Finished && Results != null && m.Phase == RacePhase.Running)) {
				Phase = m.Phase;
			}
			endAtHost_ = m.EndAtHostTime;
			EndAt = endAtHost_ >= 0 ? endAtHost_ - ClockOffset : -1;
			qualifyingEndsHost_ = m.QualifyingEndsHostTime;
			QualifyingEnds = qualifyingEndsHost_ >= 0 ? qualifyingEndsHost_ - ClockOffset : -1;
		}

		// ---- Every frame ----

		/// <summary>Called every frame with this player's car (and, in a race, whether it's on the course's road).</summary>
		public void Tick(double now, Vector3 position, Vector3 forward, float speedKmh, float timeScale, bool? onRoad) {
			if (Ended) {
				return;
			}
			if (IsHost) {
				HostTick(now);
			} else if (now - lastHeardHost_ > TimeoutSeconds) {
				HostGone = true;
				if (Phase != RacePhase.Running) {
					Ended = true;
					Run?.Cancel("The host left.");
					Message = "The host left, so the race is over";
					return;
				}
			}
			if (!IsHost && !Joined && now - lastJoin_ >= JoinResendSeconds && Message != "The race is full") {
				SendJoin(now);
			}

			switch (Phase) {
				case RacePhase.Qualifying:
					TickQualifying(now, position, forward, speedKmh, timeScale, onRoad);
					break;
				case RacePhase.Gridding:
				case RacePhase.Countdown:
				case RacePhase.Running:
					TickRace(now, position, forward, speedKmh, timeScale, onRoad);
					break;
				default:
					if (Run != null && qualifyingRun_) {
						Run = null;
					}
					break;
			}
			if (!IsHost && Phase == RacePhase.Running && Racers.Count > 0 && Racers.All(d => d.State != RacerState.Racing)) {
				// Everyone's done but the host's results haven't come (the host left, or the message was lost): work them out here.
				if (doneSince_ < 0) {
					doneSince_ = now;
				} else if (HostGone || now - doneSince_ > ResultsRepeatSeconds * 2) {
					Results = ResultsFromDrivers();
					ResultsFor = RaceNumber;
					Phase = RacePhase.Finished;
				}
			} else {
				doneSince_ = -1;
			}
			UpdateMessage(now);
			Beat(now);
		}

		private double doneSince_ = -1;

		private void TickQualifying(double now, Vector3 position, Vector3 forward, float speed, float timeScale, bool? onRoad) {
			if (Course == null) {
				return;
			}
			bool open = QualifyingEnds < 0 || now < QualifyingEnds;
			bool runsLeft = Settings.QualifyingRuns == 0 || myQualifyingRuns_ < Settings.QualifyingRuns;
			if (Run == null || !qualifyingRun_) {
				if (!open || !runsLeft) {
					return;
				}
				NewQualifyingRun();
			}
			TimeAttackRun.Phase before = Run.State;
			// A run already going when qualifying ends may finish; one not started yet may not.
			if (!open && Run.State != TimeAttackRun.Phase.Running) {
				if (Run.Active) {
					Run.Cancel("Qualifying is over.");
				}
				return;
			}
			Run.Tick(now, position, forward, speed, timeScale, onRoad);
			if (before == TimeAttackRun.Phase.Running && !Run.Active) {
				if (Run.State == TimeAttackRun.Phase.Finished) {
					myQualifyingRuns_++;
					if (myBestQualifying_ <= 0 || Run.Result < myBestQualifying_) {
						myBestQualifying_ = Run.Result;
					}
				}
				qualifyingRestartAt_ = now + QualifyingRestartSeconds;
				lastBeat_ = double.MinValue;
			}
			if (!Run.Active && now >= qualifyingRestartAt_ && open && (Settings.QualifyingRuns == 0 || myQualifyingRuns_ < Settings.QualifyingRuns)) {
				NewQualifyingRun();
			}
		}

		private void NewQualifyingRun() {
			// A circuit qualifies on a flying lap; point to point from a standing start, like a time attack.
			Run = new TimeAttackRun(Course, 1, Course.IsCircuit);
			qualifyingRun_ = true;
		}

		private void TickRace(double now, Vector3 position, Vector3 forward, float speed, float timeScale, bool? onRoad) {
			RaceDriver me = MyDriver;
			if (me == null || !me.Racer || Course == null) {
				return;
			}
			if (runFor_ != RaceNumber) {
				if (me.Slot < 0) {
					return;
				}
				runFor_ = RaceNumber;
				qualifyingRun_ = false;
				Run = new TimeAttackRun(Course, Settings.Laps) {
					ExternalStart = true,
					AllowResets = true,
					OnJump = Settings.Jump == JumpRule.Disqualify ? JumpRule.Disqualify : JumpRule.Penalty,
					Spot = MySpot(),
				};
				myState_ = RacerState.Racing;
				myTime_ = 0;
				myPenalty_ = 0;
				myReason_ = "";
				myCar_ = null;
			}

			// Also when the countdown message was missed and the race is already running: GO is in the past, the clock runs from it.
			if ((Phase == RacePhase.Countdown || Phase == RacePhase.Running) && countdownId_ > 0 && countdownStarted_ != countdownId_ && Run.State == TimeAttackRun.Phase.ToStart) {
				countdownStarted_ = countdownId_;
				Run.StartCountdown(goHostTime_ - ClockOffset);
			}

			TimeAttackRun.Phase before = Run.State;
			if (myState_ == RacerState.Racing) {
				Run.Tick(now, position, forward, speed, timeScale, onRoad);
			}
			if (me.EliminatedOnLap > 0 && myState_ == RacerState.Racing) {
				Run.Cancel($"Eliminated on lap {me.EliminatedOnLap}.");
				myState_ = RacerState.Eliminated;
				myReason_ = $"eliminated on lap {me.EliminatedOnLap}";
			}
			if (EndAt >= 0 && now >= EndAt && myState_ == RacerState.Racing) {
				Run.Cancel("The race was ended.");
				myState_ = RacerState.Out;
				myReason_ = "the race was ended";
			}

			// The car is checked once each time it stops in its spot (not every frame), like battles.
			if (Run.State == TimeAttackRun.Phase.ToStart) {
				if (!Run.ReadyToStart) {
					myCar_ = null;
				} else if (myCar_ == null) {
					myCar_ = checkCar_?.Invoke() ?? new BattleCar();
				}
			}

			if (myState_ == RacerState.Racing && before != Run.State && !Run.Active) {
				if (Run.State == TimeAttackRun.Phase.Finished) {
					myState_ = RacerState.Finished;
					myPenalty_ = Run.JumpStarts > 0 && Settings.Jump == JumpRule.Penalty ? Settings.JumpPenaltySeconds : 0;
					myTime_ = Run.Result + myPenalty_;
				} else if (Run.JumpStarts > 0 && Settings.Jump == JumpRule.Disqualify) {
					myState_ = RacerState.Disqualified;
					myReason_ = "jump start";
				} else {
					myState_ = RacerState.Out;
					myReason_ = Run.Message.TrimEnd('.');
				}
				lastBeat_ = double.MinValue;
			}
			me.State = myState_;
			me.Time = myTime_;
			me.Penalty = myPenalty_;
			me.Reason = myReason_;
			me.Progress = Run.TotalProgress;
			me.LapsDone = Run.LapTimes.Count;
			me.BestLap = Run.BestLap?.Time ?? 0;
			me.Ready = MyReady;
		}

		private bool MyReady => Run != null && Run.State == TimeAttackRun.Phase.ToStart && Run.ReadyToStart && myCar_ != null && (Settings.Open || myCar_.Spec);

		/// <summary>This driver's grid spot (null without one yet).</summary>
		public GridSpot? MySpot() {
			RaceDriver me = MyDriver;
			if (Course == null || me == null || me.Slot < 0) {
				return null;
			}
			List<GridSpot> spots = Course.GridSpots(me.Slot + 1, Settings.PoleLeft, Settings.LayoutFromCourse ? (GridLayout?)null : Settings.Layout);
			return spots[me.Slot];
		}

		// ---- Host every frame ----

		private void HostTick(double now) {
			RaceDriver me = MyDriver;
			if (me != null) {
				me.LastHeard = now;
				me.BestQualifying = myBestQualifying_;
				me.QualifyingRuns = myQualifyingRuns_;
				me.PersonalBest = personalBest_?.Invoke() ?? 0;
				me.Spec = myCar_?.Spec ?? me.Spec;
				me.TuneSet = myCar_?.TuneSet ?? me.TuneSet;
				me.RaceNumber = RaceNumber;
			}

			// Drivers who stopped answering.
			foreach (RaceDriver d in Drivers.Where(d => d.Id != Me && now - d.LastHeard > TimeoutSeconds).ToList()) {
				if ((Phase == RacePhase.Running || Phase == RacePhase.Countdown) && d.Racer) {
					if (d.State == RacerState.Racing) {
						d.State = RacerState.Out;
						d.Reason = "stopped answering";
						lobbyDirty_ = true;
					}
				} else {
					Drivers.Remove(d);
					lobbyDirty_ = true;
				}
			}

			switch (Phase) {
				case RacePhase.Qualifying:
					if (now >= qualifyingEndsHost_ && (Run == null || Run.State != TimeAttackRun.Phase.Running)) {
						Phase = RacePhase.Lobby;
						qualifyingEndsHost_ = -1;
						QualifyingEnds = -1;
						lobbyDirty_ = true;
					}
					break;

				case RacePhase.Gridding: {
					List<RaceDriver> racers = Racers;
					bool sameSet = Settings.Open || racers.Select(r => r.TuneSet).Distinct().Count() == 1;
					if (racers.Count >= 1 && racers.All(r => r.Ready && r.RaceNumber == RaceNumber) && sameSet) {
						if (bothReadySince_ < 0) {
							bothReadySince_ = now;
						} else if (now - bothReadySince_ >= ReadyHoldSeconds) {
							StartCountdown(now);
						}
					} else {
						bothReadySince_ = -1;
					}
					break;
				}

				case RacePhase.Countdown:
					if (now >= goHostTime_) {
						Phase = RacePhase.Running;
						lobbyDirty_ = true;
					}
					break;

				case RacePhase.Running: {
					if (Settings.Elimination && Course.IsCircuit) {
						Eliminate(now);
					}
					if (endAtHost_ >= 0 && now >= endAtHost_) {
						foreach (RaceDriver d in Racers.Where(d => d.State == RacerState.Racing)) {
							d.State = RacerState.Out;
							d.Reason = "the race was ended";
						}
					}
					bool allDone = Racers.All(d => d.State != RacerState.Racing);
					if (allDone) {
						FinishRace(now);
					}
					break;
				}
			}

			if (lobbyDirty_ || now - lastLobby_ >= LobbySeconds) {
				SendLobby(now);
			}
			if (Course != null && !Course.Official && Drivers.Any(d => d.Id != Me && !d.HasCourse) && now - lastCourseSent_ >= CourseResendSeconds) {
				lastCourseSent_ = now;
				var course = New(RaceKind.Course);
				course.Course = Course;
				outbox_.Add(course);
			}
			if (resultsSentAt_ >= 0 && resultsSends_ < 3 && now - resultsSentAt_ >= ResultsRepeatSeconds / 3 * resultsSends_) {
				resultsSends_++;
				SendResults();
			}
			HostBeat(now);
		}

		private void StartCountdown(double now) {
			countdownId_++;
			goHostTime_ = now + CountdownLeadSeconds + TimeAttackRun.CountdownSeconds;
			Phase = RacePhase.Countdown;
			bothReadySince_ = -1;
			lastHostBeat_ = double.MinValue;
			lobbyDirty_ = true;
		}

		/// <summary>Elimination: once all but one still in have done lap k, the one who hasn't is out (laps before the last only).</summary>
		private void Eliminate(double now) {
			while (true) {
				int lap = eliminations_ + 1;
				if (lap >= Settings.Laps) {
					return;
				}
				List<RaceDriver> still = Racers.Where(d => d.State == RacerState.Racing || d.State == RacerState.Finished).ToList();
				if (still.Count <= 1) {
					return;
				}
				List<RaceDriver> behind = still.Where(d => d.LapsDone < lap).ToList();
				if (behind.Count != 1) {
					// Someone dropped out, which counts as this lap's elimination; otherwise wait for the lap to finish.
					if (behind.Count == 0) {
						eliminations_++;
						continue;
					}
					return;
				}
				RaceDriver last = behind[0];
				last.State = RacerState.Eliminated;
				last.EliminatedOnLap = lap;
				last.Reason = $"eliminated on lap {lap}";
				eliminations_++;
				lobbyDirty_ = true;
			}
		}

		private void FinishRace(double now) {
			Results = ResultsFromDrivers();
			ResultsFor = RaceNumber;
			Phase = RacePhase.Finished;
			resultsSentAt_ = now;
			resultsSends_ = 0;
			lobbyDirty_ = true;
			Message = "Race finished";
		}

		/// <summary>The results as the drivers' own mods reported them: finishers by time, then those knocked out later, then the rest.</summary>
		public List<RaceResultRow> ResultsFromDrivers() {
			var rows = new List<RaceResultRow>();
			IEnumerable<RaceDriver> order = Racers
				.OrderBy(d => d.State == RacerState.Finished ? 0 : d.State == RacerState.Eliminated ? 1 : d.State == RacerState.Disqualified ? 3 : 2)
				.ThenBy(d => d.State == RacerState.Finished ? d.Time : 0)
				.ThenByDescending(d => d.State == RacerState.Eliminated ? d.EliminatedOnLap : 0)
				.ThenByDescending(d => d.Progress);
			int position = 1;
			foreach (RaceDriver d in order) {
				rows.Add(new RaceResultRow {
					Id = d.Id, Name = d.Name, Position = position++, State = d.State == RacerState.Racing ? RacerState.Out : d.State,
					Time = d.Time, Penalty = d.Penalty, Reason = d.Reason, BestLap = d.BestLap,
				});
			}
			return rows;
		}

		/// <summary>Everyone racing, in their place right now (finished drivers by time, then by how far along each is).</summary>
		public List<RaceStanding> Standings(double now) {
			var list = new List<RaceStanding>();
			foreach (RaceDriver d in Racers) {
				float progress = d.Progress;
				if (d.Id != Me && d.State == RacerState.Racing && d.ProgressHeardAt >= 0) {
					// Every driver has their own clock, so their progress is aged by when it arrived here.
					double age = Math.Max(0, Math.Min(1, now - d.ProgressHeardAt));
					progress += (float)(d.Speed * age);
				}
				list.Add(new RaceStanding { Driver = d, Progress = progress });
			}
			list = list
				.OrderBy(s => s.Driver.State == RacerState.Finished ? 0 : s.Driver.State == RacerState.Racing ? 1 : s.Driver.State == RacerState.Eliminated ? 2 : 3)
				.ThenBy(s => s.Driver.State == RacerState.Finished ? s.Driver.Time : 0)
				.ThenByDescending(s => s.Driver.State == RacerState.Eliminated ? s.Driver.EliminatedOnLap : 0)
				.ThenByDescending(s => s.Progress)
				.ToList();
			for (int i = 0; i < list.Count; i++) {
				list[i].Position = i + 1;
			}
			return list;
		}

		/// <summary>The grid: the host's order when given, else by the race's grid order (with a stable tie-break by join order).</summary>
		private List<RaceDriver> GridOrderOf(List<RaceDriver> racers, IList<ulong> order) {
			if (order != null) {
				var given = order.Select(id => racers.FirstOrDefault(r => r.Id == id)).Where(r => r != null).ToList();
				given.AddRange(racers.Where(r => !given.Contains(r)));
				return given;
			}
			switch (Settings.Order) {
				case GridOrder.Random: {
					var random = new System.Random((int)(Id ^ (uint)RaceNumber));
					return racers.OrderBy(_ => random.Next()).ToList();
				}
				case GridOrder.BestTime:
					return racers.OrderBy(r => r.PersonalBest > 0 ? r.PersonalBest : double.MaxValue).ThenBy(r => racers.IndexOf(r)).ToList();
				case GridOrder.Qualifying:
					return racers.OrderBy(r => r.BestQualifying > 0 ? r.BestQualifying : double.MaxValue).ThenBy(r => racers.IndexOf(r)).ToList();
				default:
					return racers.ToList();
			}
		}

		// ---- Sending ----

		private void SendJoin(double now) {
			lastJoin_ = now;
			RaceMessage join = New(RaceKind.Join);
			join.Name = myName_;
			join.HasCourse = Course != null;
			outbox_.Add(join);
		}

		private void SendLobby(double now) {
			lastLobby_ = now;
			lobbyDirty_ = false;
			RaceMessage lobby = New(RaceKind.Lobby);
			lobby.HostName = HostName;
			lobby.Settings = Settings;
			lobby.CourseCode = CourseCode;
			lobby.Phase = Phase;
			lobby.Kicked = kicked_.ToList();
			lobby.Lines = Lines;
			lobby.MatchId = MatchId;
			lobby.MatchA = MatchA;
			lobby.MatchB = MatchB;
			lobby.MatchFormat = MatchFormat;
			lobby.MatchGame = MatchGame;
			lobby.MatchLaps = MatchLaps;
			foreach (RaceDriver d in Drivers) {
				lobby.Entries.Add(new LobbyEntry { Id = d.Id, Name = d.Name, Racer = d.Racer, Slot = d.Slot, EliminatedOnLap = d.EliminatedOnLap });
			}
			outbox_.Add(lobby);
		}

		private void SendResults() {
			RaceMessage results = New(RaceKind.Results);
			results.Rows = Results ?? new List<RaceResultRow>();
			outbox_.Add(results);
		}

		private double BeatSeconds(double now) {
			switch (Phase) {
				case RacePhase.Lobby:
				case RacePhase.Finished:
					return PresenceSeconds;
				case RacePhase.Qualifying:
				case RacePhase.Gridding:
					return SlowSeconds;
				case RacePhase.Countdown:
					return FastSeconds;
			}
			if (Racers.Count <= SlowAbove || Run == null) {
				return FastSeconds;
			}
			float distance = Course.Length * Math.Max(1, Run.Laps);
			bool nearFinish = Run.TotalProgress > distance - FastNearFinishMetres;
			bool justStarted = Run.State == TimeAttackRun.Phase.Running && Run.Elapsed < FastAfterGoSeconds;
			return nearFinish || justStarted ? FastSeconds : SlowSeconds;
		}

		private void Beat(double now) {
			RacerState state = MyDriver?.State ?? RacerState.Racing;
			if (state != lastBeatState_) {
				// A result goes out at once.
				lastBeatState_ = state;
				lastBeat_ = double.MinValue;
			}
			if (now - lastBeat_ < BeatSeconds(now)) {
				return;
			}
			lastBeat_ = now;
			RaceMessage beat = New(RaceKind.Beat);
			RaceDriver me = MyDriver;
			beat.SentAt = now;
			beat.Ready = MyReady;
			beat.Spec = myCar_?.Spec ?? false;
			beat.TuneSet = myCar_?.TuneSet ?? "";
			beat.Car = myCar_?.Car ?? "";
			beat.HasCourse = Course != null;
			beat.Progress = me?.Racer == true && Run != null && !qualifyingRun_ ? Run.TotalProgress : 0f;
			beat.LapsDone = Run != null && !qualifyingRun_ ? Run.LapTimes.Count : 0;
			beat.State = me?.State ?? RacerState.Racing;
			beat.Time = me?.Time ?? 0;
			beat.Penalty = me?.Penalty ?? 0;
			beat.Reason = me?.Reason ?? "";
			beat.BestLap = me?.BestLap ?? 0;
			beat.BestQualifying = myBestQualifying_;
			beat.QualifyingRuns = myQualifyingRuns_;
			beat.PersonalBest = personalBest_?.Invoke() ?? 0;
			outbox_.Add(beat);
		}

		private void HostBeat(double now) {
			if (now - lastHostBeat_ < BeatSeconds(now)) {
				return;
			}
			lastHostBeat_ = now;
			RaceMessage beat = New(RaceKind.HostBeat);
			beat.SentAt = now;
			beat.Phase = Phase;
			beat.GoHostTime = goHostTime_;
			beat.CountdownId = countdownId_;
			beat.EndAtHostTime = endAtHost_;
			beat.QualifyingEndsHostTime = qualifyingEndsHost_;
			foreach (RaceDriver d in Drivers.Where(d => d.Id != Me && d.SentHeardAt >= 0)) {
				beat.Echoes.Add((d.Id, d.SentAt, now - d.SentHeardAt));
			}
			outbox_.Add(beat);
		}

		private RaceMessage New(RaceKind kind) => new RaceMessage { Kind = kind, RaceId = Id, From = Me, RaceNumber = RaceNumber };

		private void UpdateMessage(double now) {
			RaceDriver me = MyDriver;
			if (!IsHost && !Joined) {
				return;
			}
			switch (Phase) {
				case RacePhase.Lobby:
					Message = Course == null ? "Getting the course from the host..." : IsHost ? $"{Drivers.Count} driver(s) in. Start when you're ready" : "Waiting for the host to start";
					break;
				case RacePhase.Qualifying: {
					string left = QualifyingEnds >= 0 ? $" ({Math.Max(0, QualifyingEnds - now) / 60.0:0.0} min left)" : "";
					string best = myBestQualifying_ > 0 ? $"your best {TimeAttackRun.FormatTime(myBestQualifying_)}" : "no time yet";
					Message = $"Qualifying{left}: {best}. {(Run?.Active == true ? Run.Message : "")}".Trim();
					break;
				}
				case RacePhase.Gridding:
					if (me == null || !me.Racer) {
						Message = "Watching this race";
					} else if (Run != null && Run.State == TimeAttackRun.Phase.ToStart && Run.ReadyToStart && myCar_ != null && !Settings.Open && !myCar_.Spec) {
						Message = "Your car isn't TRC spec, and this is a TRC spec race";
					} else if (Run != null && !Run.ReadyToStart) {
						Message = Run.Message;
					} else {
						int waiting = Racers.Count(r => !r.Ready);
						Message = waiting > 0 ? $"Ready. Waiting for {waiting} driver(s)" : "Everyone's ready";
					}
					break;
				case RacePhase.Countdown:
					Message = Run?.Message ?? "Get ready";
					break;
				case RacePhase.Running:
					Message = me?.Racer != true ? "Watching" : me.State == RacerState.Racing ? (EndAt >= 0 ? $"The race ends in {Math.Max(0, EndAt - now):0} s" : "")
						: me.State == RacerState.Finished ? $"Finished: {TimeAttackRun.FormatTime(me.Time)}{(me.Penalty > 0 ? $" (with +{me.Penalty:0} s jump start)" : "")}. Waiting for the others"
						: $"Out: {me.Reason}";
					break;
				case RacePhase.Finished:
					RaceResultRow mine = Results?.FirstOrDefault(r => r.Id == Me);
					Message = mine == null ? "Race finished" : mine.State == RacerState.Finished ? $"You finished P{mine.Position}" : $"Race finished: {mine.Reason}";
					break;
			}
		}
	}
}
