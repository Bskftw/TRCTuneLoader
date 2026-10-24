using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>A driver's car as their own mod checked it, for a battle.</summary>
	public sealed class BattleCar {
		public bool Spec;
		public string TuneSet = "";
		public string TuneCode = "";
		public string Car = "";
	}

	/// <summary>
	/// One battle between two TRC Tune Loader users, as one of them sees it. Both run the same logic on their
	/// own PC and talk through <see cref="BattleMessage"/>s; the mod passes messages in and out and feeds the car.
	///
	///  1. The host challenges until the guest's mod says it got the challenge (Seen); the guest accepts (both on
	///     the same course, the same map). Two drivers who challenge each other at once to the same battle get
	///     one battle (<see cref="YieldsTo"/>).
	///  2. Both drive to their start box (side by side for a race; the leader in front for cat and mouse) and
	///     stop. Each mod reports ready; in a TRC spec battle only with a TRC spec car, both on the same set.
	///  3. The host picks GO a moment ahead on its clock; the guest turns it into its own clock with the clock
	///     sync the heartbeats keep up, so 3-2-1-GO lands at the same moment on both screens. Moving before GO
	///     is a jump start: the countdown is called off for both.
	///  4. Each mod times its own car from its own GO to the finish (TimeAttackRun: shortcuts, leaving the road
	///     and resets stop it), and sends its result. With both results, both decide the run the same way
	///     (BattleRules): a winner ends the battle, otherwise they line up again.
	///
	/// A driver who stops answering for <see cref="TimeoutSeconds"/> has left. Nothing here knows about CarX or
	/// KSL, so the self-test can play a whole battle between two simulated players.
	/// </summary>
	public sealed class BattleSession {
		public enum Phase : byte { Inviting, Invited, Gridding, Countdown, Running, Finished, Ended }

		private const double HeartbeatSeconds = 0.2;
		public const double TimeoutSeconds = 10;
		private const double InviteSeconds = 45;
		private const double ChallengeResendSeconds = 3;

		/// <summary>After this long without a Seen, the challenger is told it hasn't got there yet.</summary>
		private const double NotSeenSeconds = 6;

		/// <summary>Two challenges are the same battle when their lines are this close (each driver may have recorded their own path).</summary>
		private const float SameLineMetres = 1f;

		/// <summary>Both must stay ready this long before the host starts the countdown.</summary>
		private const double ReadyHoldSeconds = 1;

		/// <summary>The countdown starts this long after the host calls it, so the message reaches the guest first.</summary>
		private const double CountdownLeadSeconds = 1;

		/// <summary>If GO passed this long ago and the other side never started, the countdown is called off (a lost message).</summary>
		private const double MissedGoSeconds = 3;

		/// <summary>How long to wait for the other driver's result once one is in.</summary>
		private const double ResultWaitSeconds = 180;

		/// <summary>
		/// After this many runs without a winner, the battle is a draw. Cat and mouse goes on "until someone wins"
		/// (the lead needed shrinks to 0.5 s), so this is only a backstop.
		/// </summary>
		public const int MaxRuns = 10;

		/// <summary>
		/// Start boxes, in metres behind the start line. In cat and mouse the leader's box ends where the chaser's
		/// begins (TRC, Oct 24: the chaser lines up from 5 m back), so the chaser can never be in front.
		/// </summary>
		private const float RaceBoxBack = 12f;
		private const float LeaderBoxBack = 5f;
		private const float ChaserBoxFront = 5f;
		private const float ChaserBoxBack = 30f;

		/// <summary>Teleport spots: metres behind the start line, and for a race how far to each side of the middle.</summary>
		public const float FrontSpot = 4f;
		public const float LeaderSpot = 2.5f;
		public const float ChaserSpot = 10f;

		/// <summary>A race's two spots are this far (metres) in from the middle of each half of the line (TRC: closer together).</summary>
		private const float RaceSpotsCloser = 1f;
		private const float MinRaceSpotSide = 1.2f;

		private const int OffsetSamples = 15;

		public uint Id { get; }
		public bool IsHost { get; }
		public ulong Me { get; }
		public ulong Peer { get; }
		public string PeerName { get; }
		public BattleFormat Format { get; }

		/// <summary>Laps per run on a circuit (1 on any other course).</summary>
		public int Laps { get; }

		/// <summary>An Open battle: cars don't have to be TRC spec, and it never counts.</summary>
		public bool Open { get; }
		public Course Course { get; }

		public Phase State { get; private set; }

		/// <summary>What to tell the player right now.</summary>
		public string Message { get; private set; } = "";
		public int RunIndex { get; private set; }

		/// <summary>Cat and mouse: who leads the current run.</summary>
		public ulong Leader => LeaderFor(RunIndex);
		public bool IAmLeader => Leader == Me;

		/// <summary>This driver's own run (the current one).</summary>
		public TimeAttackRun Run { get; private set; }
		public List<BattleRun> Runs { get; } = new List<BattleRun>();

		/// <summary>Who won (0 = nobody: a draw, or the battle ended early).</summary>
		public ulong Winner { get; private set; }

		/// <summary>The battle ended because the other driver left or stopped answering.</summary>
		public bool PeerLeft { get; private set; }

		/// <summary>The battle ended because this driver left it.</summary>
		public bool ILeft { get; private set; }

		/// <summary>The challenge got to the other driver's mod (host only).</summary>
		public bool PeerSeen { get; private set; }

		/// <summary>Why the other driver can't accept the challenge, when their mod said so (host only).</summary>
		public string PeerProblem { get; private set; } = "";

		public bool MyReady { get; private set; }
		public bool PeerReady { get; private set; }
		public BattleCar MyCar { get; private set; }
		public BattleCar PeerCar { get; private set; } = new BattleCar();

		/// <summary>The other driver's run stopped before the finish (and why): this driver only has to finish to win it. "" otherwise.</summary>
		public string PeerOut { get; private set; } = "";

		/// <summary>The other driver's metres along the course in the current run, as of their last heartbeat.</summary>
		public float PeerProgress { get; private set; }

		/// <summary>
		/// Where the other driver is now: their last progress moved on by their speed for as long as it took to
		/// get here, so it can be compared with this driver's live position (heartbeats come 5 times a second,
		/// late by the connection).
		/// </summary>
		public float PeerProgressAt(double now) {
			if (peerProgressAt_ < 0 || offsets_.Count == 0) {
				return PeerProgress;
			}
			double age = Math.Max(0, Math.Min(1, now - (peerProgressAt_ - ClockOffset)));
			return PeerProgress + (float)(peerSpeed_ * age);
		}

		/// <summary>A TRC spec battle where both cars were TRC spec at every start and finish.</summary>
		public bool Counted { get; private set; }

		/// <summary>The other driver's clock minus this one's, in seconds (from the heartbeats).</summary>
		public double ClockOffset { get; private set; }

		/// <summary>Time for a message to go there and back, in seconds.</summary>
		public double RoundTrip { get; private set; }

		/// <summary>The battle is still going (not finished or ended).</summary>
		public bool Active => State != Phase.Finished && State != Phase.Ended;

		/// <summary>Both drivers agreed and lined up at least once: worth saving to the battle log.</summary>
		public bool Started { get; private set; }

		private readonly Func<BattleCar> checkMyCar_;
		private readonly List<BattleMessage> outbox_ = new List<BattleMessage>();
		private readonly List<double> offsets_ = new List<double>();
		private readonly string myName_;
		private readonly double createdAt_;
		private double lastHeartbeat_ = double.MinValue;
		private double lastHeard_;
		private double lastChallengeSent_;
		private double peerSentAt_;
		private double peerSentHeardAt_ = -1;
		private int countdownId_;
		private double goTime_;
		private double bothReadySince_ = -1;
		private RunResult myResult_;
		private RunResult peerResult_;
		private RunResult lastResult_;
		private double firstResultAt_ = -1;
		private double endedAt_ = -1;
		private Phase peerPhase_;
		private int peerRunIndex_ = -1;
		/// <summary>When the other driver measured <see cref="PeerProgress"/>, on their clock.</summary>
		private double peerProgressAt_ = -1;
		private double peerSpeed_;

		private BattleSession(uint id, bool host, ulong me, string myName, ulong peer, string peerName, Course course,
			BattleFormat format, bool open, int laps, Func<BattleCar> checkMyCar, double now) {
			Id = id;
			IsHost = host;
			Me = me;
			myName_ = BattleMessage.PlainName(myName);
			Peer = peer;
			peerName = BattleMessage.PlainName(peerName);
			PeerName = peerName.Length == 0 ? "the other driver" : peerName;
			Course = course;
			Format = format;
			Laps = course.IsCircuit ? Math.Max(1, laps) : 1;
			Open = open;
			Counted = !open;
			checkMyCar_ = checkMyCar;
			createdAt_ = now;
			lastHeard_ = now;
		}

		/// <summary>Challenges <paramref name="peer"/>. The course goes with the challenge unless it's official (they have it too).</summary>
		public static BattleSession Challenge(uint id, ulong me, string myName, ulong peer, string peerName, Course course,
			BattleFormat format, bool open, Func<BattleCar> checkMyCar, double now, int laps = 1) {
			var session = new BattleSession(id, true, me, myName, peer, peerName, course, format, open, laps, checkMyCar, now) {
				State = Phase.Inviting,
			};
			session.SendChallenge(now);
			session.Message = $"Sending the challenge to {session.PeerName}";
			return session;
		}

		/// <summary>A challenge someone sent this player, with the course it's on (found here, or sent with it).</summary>
		public static BattleSession FromChallenge(BattleMessage challenge, ulong me, string myName, Course course, Func<BattleCar> checkMyCar, double now) {
			var session = new BattleSession(challenge.BattleId, false, me, myName, challenge.From, challenge.FromName, course,
				challenge.Format, challenge.Open, challenge.Laps, checkMyCar, now) {
				State = Phase.Invited,
			};
			session.Message = $"{session.PeerName} challenges you";
			return session;
		}

		/// <summary>
		/// Whether this challenge is the other driver challenging this one to the same battle at the same time: same
		/// course lines, format and mode, while this one still waits for an answer.
		/// </summary>
		public bool Crosses(BattleMessage challenge) =>
			IsHost && State == Phase.Inviting && challenge != null && challenge.Kind == BattleKind.Challenge &&
			challenge.From == Peer && challenge.To == Me && challenge.Format == Format && challenge.Open == Open &&
			(challenge.Course != null ? SameLines(challenge.Course, Course) : challenge.CourseCode == Course.Code);

		/// <summary>
		/// For crossed challenges, the one from the lower Steam ID stands, so both sides agree without asking:
		/// true when this driver should take their own back and accept <paramref name="challenge"/>.
		/// </summary>
		public bool YieldsTo(BattleMessage challenge) => Crosses(challenge) && challenge.From < Me;

		private static bool SameLines(Course a, Course b) =>
			a.MapKey == b.MapKey &&
			Vector3.Distance(a.Start.Center, b.Start.Center) < SameLineMetres && Vector3.Distance(a.Finish.Center, b.Finish.Center) < SameLineMetres;

		/// <summary>Messages to send now (taken out of the session).</summary>
		public List<BattleMessage> TakeOutbox() {
			var messages = new List<BattleMessage>(outbox_);
			outbox_.Clear();
			return messages;
		}

		public void Accept(double now) {
			if (State != Phase.Invited) {
				return;
			}
			BattleMessage answer = New(BattleKind.Answer);
			answer.Accept = true;
			outbox_.Add(answer);
			BeginGridding(now);
		}

		public void Decline(string reason, double now) {
			if (State != Phase.Invited) {
				return;
			}
			BattleMessage answer = New(BattleKind.Answer);
			answer.Reason = reason ?? "";
			outbox_.Add(answer);
			End(Phase.Ended, "You declined", now);
		}

		/// <summary>Leaves the battle (or takes back a challenge).</summary>
		public void Leave(double now) {
			if (!Active) {
				return;
			}
			BattleMessage leave = New(BattleKind.Leave);
			outbox_.Add(leave);
			ILeft = Started;
			End(Phase.Ended, State == Phase.Inviting ? "You took the challenge back" : "You left the battle", now);
		}

		public void Receive(BattleMessage message, double now) {
			if (message == null || message.BattleId != Id || message.From != Peer || message.To != Me) {
				return;
			}
			lastHeard_ = now;

			switch (message.Kind) {
				case BattleKind.Seen:
					if (IsHost && State == Phase.Inviting) {
						PeerSeen = true;
						PeerProblem = message.Accept ? "" : message.Reason;
						Message = PeerProblem.Length > 0
							? $"{PeerName} got your challenge but can't accept it: {PeerProblem}"
							: $"{PeerName} got your challenge. Waiting for them to accept";
					}
					break;

				case BattleKind.Answer:
					if (IsHost && State == Phase.Inviting) {
						if (message.Accept) {
							BeginGridding(now);
						} else {
							End(Phase.Ended, $"{PeerName} declined{(message.Reason.Length > 0 ? $": {message.Reason}" : "")}", now);
						}
					}
					break;

				case BattleKind.Leave:
					if (Active) {
						PeerLeft = Started;
						End(Phase.Ended, State == Phase.Invited ? $"{PeerName} took the challenge back" : $"{PeerName} left the battle", now);
					}
					break;

				case BattleKind.Heartbeat:
					// The guest only sends these once it accepted: covers a lost answer.
					if (IsHost && State == Phase.Inviting) {
						BeginGridding(now);
					}
					OnHeartbeat(message, now);
					break;

				case BattleKind.Countdown:
					if (!IsHost && message.CountdownId > countdownId_ && message.RunIndex == RunIndex && State == Phase.Gridding) {
						countdownId_ = message.CountdownId;
						goTime_ = message.GoHostTime - ClockOffset;
						Run.StartCountdown(goTime_);
						if (Run.State == TimeAttackRun.Phase.Countdown) {
							State = Phase.Countdown;
						}
					}
					break;

				case BattleKind.Abort:
					if (message.CountdownId == countdownId_ && (State == Phase.Countdown || State == Phase.Running) && myResult_ == null) {
						BackToGrid($"{PeerName}: {message.Reason}. Line up again");
					}
					break;
			}
		}

		/// <summary>Called every frame with the player's car (and, during a battle, whether it's on the course's road).</summary>
		public void Tick(double now, Vector3 position, Vector3 forward, float speedKmh, float timeScale, bool? onRoad) {
			switch (State) {
				case Phase.Inviting:
					if (now - createdAt_ > InviteSeconds) {
						End(Phase.Ended, PeerSeen ? $"{PeerName} didn't answer" : $"The challenge never got to {PeerName}", now);
					} else if (!PeerSeen) {
						// Sent again until their mod says it's there; the pieces of each copy fill in the last one's gaps.
						if (now - lastChallengeSent_ >= ChallengeResendSeconds) {
							SendChallenge(now);
						}
						if (now - createdAt_ > NotSeenSeconds) {
							Message = $"Still sending: {PeerName}'s TRC Tune Loader hasn't confirmed the challenge yet";
						}
					}
					return;

				case Phase.Invited:
					if (now - createdAt_ > InviteSeconds) {
						End(Phase.Ended, "The challenge ran out", now);
					}
					return;

				case Phase.Finished:
				case Phase.Ended:
					// A few more heartbeats after a finish, so the other side surely has the last result.
					if (State == Phase.Finished && now - endedAt_ < 3) {
						Heartbeat(now);
					}
					return;
			}

			if (now - lastHeard_ > TimeoutSeconds) {
				PeerLeft = true;
				End(Phase.Ended, $"{PeerName} stopped answering (left the room?)", now);
				return;
			}

			TimeAttackRun.Phase before = Run.State;
			Run.Tick(now, position, forward, speedKmh, timeScale, onRoad);

			switch (State) {
				case Phase.Gridding:
					UpdateGrid(now);
					break;

				case Phase.Countdown:
					if (Run.State == TimeAttackRun.Phase.ToStart && before == TimeAttackRun.Phase.Countdown) {
						BattleMessage abort = New(BattleKind.Abort);
						abort.CountdownId = countdownId_;
						abort.Reason = "jump start";
						outbox_.Add(abort);
						BackToGrid("Jump start! Line up again");
					} else if (Run.State == TimeAttackRun.Phase.Running) {
						State = Phase.Running;
						Message = "";
					}
					break;

				case Phase.Running:
					UpdateRunning(now);
					break;
			}

			Heartbeat(now);
		}

		/// <summary>Where this driver should line up: the middle of their start box, facing the course.</summary>
		public Vector3 MySpot() {
			CourseGate start = Course.Start;
			if (Format == BattleFormat.CatAndMouse) {
				return start.Center - start.Forward * (IAmLeader ? LeaderSpot : ChaserSpot);
			}
			// Race: the host on the left half, the guest on the right.
			float side = Mathf.Max(MinRaceSpotSide, start.Width / 4f - RaceSpotsCloser);
			return start.Center - start.Forward * FrontSpot + start.Right * side * (IsHost ? -1f : 1f);
		}

		private void SendChallenge(double now) {
			lastChallengeSent_ = now;
			BattleMessage challenge = New(BattleKind.Challenge);
			challenge.FromName = myName_;
			challenge.Format = Format;
			challenge.Open = Open;
			challenge.Laps = Laps;
			challenge.CourseCode = Course.Code;
			challenge.Course = Course.Official ? null : Course;
			outbox_.Add(challenge);
		}

		private void BeginGridding(double now) {
			Started = true;
			lastHeard_ = now;
			NewRun();
			State = Phase.Gridding;
		}

		private void NewRun() {
			// A reset doesn't end a battle run: a driver who messes up can still win if the other does too.
			Run = new TimeAttackRun(Course, Laps) { ExternalStart = true, AllowResets = true };
			if (Format == BattleFormat.CatAndMouse && !IAmLeader) {
				Run.GridFront = ChaserBoxFront;
				Run.GridBack = ChaserBoxBack;
			} else {
				Run.GridBack = Format == BattleFormat.CatAndMouse ? LeaderBoxBack : RaceBoxBack;
			}
			myResult_ = null;
			peerResult_ = null;
			firstResultAt_ = -1;
			bothReadySince_ = -1;
			MyCar = null;
			MyReady = false;
			PeerProgress = 0f;
			PeerOut = "";
			peerProgressAt_ = -1;
			peerSpeed_ = 0;
		}

		private void BackToGrid(string message) {
			Run.Restart();
			State = Phase.Gridding;
			bothReadySince_ = -1;
			Message = message;
		}

		private void UpdateGrid(double now) {
			bool inPlace = Run.ReadyToStart;
			if (!inPlace) {
				MyCar = null;
			} else if (MyCar == null) {
				// Checked once each time the car stops in its box, not every frame.
				MyCar = checkMyCar_?.Invoke() ?? new BattleCar();
			}
			MyReady = inPlace && (Open || MyCar.Spec);
			bool sameSet = Open || (MyCar != null && PeerCar.TuneSet == MyCar.TuneSet);

			if (!inPlace) {
				Message = Run.Message;
			} else if (!Open && !MyCar.Spec) {
				Message = "Your car isn't TRC spec, and this is a TRC spec battle";
			} else if (!PeerReady) {
				Message = $"Ready. Waiting for {PeerName}";
			} else if (!sameSet) {
				Message = $"Different tune sets: you're on {SetName(MyCar)}, {PeerName} is on {SetName(PeerCar)}";
			} else {
				Message = "Both ready";
			}

			if (!IsHost) {
				return;
			}
			if (MyReady && PeerReady && sameSet) {
				if (bothReadySince_ < 0) {
					bothReadySince_ = now;
				} else if (now - bothReadySince_ >= ReadyHoldSeconds) {
					StartCountdown(now);
				}
			} else {
				bothReadySince_ = -1;
			}
		}

		private static string SetName(BattleCar car) => car == null || car.TuneSet.Length == 0 ? "no TRC tune set" : car.TuneSet;

		private void StartCountdown(double now) {
			countdownId_++;
			goTime_ = now + CountdownLeadSeconds + TimeAttackRun.CountdownSeconds;
			BattleMessage countdown = New(BattleKind.Countdown);
			countdown.CountdownId = countdownId_;
			countdown.RunIndex = RunIndex;
			countdown.GoHostTime = goTime_;
			outbox_.Add(countdown);
			Run.StartCountdown(goTime_);
			State = Phase.Countdown;
			bothReadySince_ = -1;
			Message = "Get ready";
		}

		private void UpdateRunning(double now) {
			// The other side never started (its countdown message got lost): call this one off and line up again.
			if (myResult_ == null && peerRunIndex_ == RunIndex && peerPhase_ == Phase.Gridding && now - goTime_ > MissedGoSeconds) {
				BattleMessage abort = New(BattleKind.Abort);
				abort.CountdownId = countdownId_;
				abort.Reason = "the start didn't reach both of you";
				outbox_.Add(abort);
				BackToGrid("The start didn't reach both of you. Line up again");
				return;
			}

			if (!Run.Active && myResult_ == null) {
				myResult_ = new RunResult {
					Run = RunIndex,
					Finished = Run.State == TimeAttackRun.Phase.Finished,
					Time = Run.Result,
					Reason = Run.State == TimeAttackRun.Phase.Finished ? "" : Run.Message,
					SpecAtEnd = Open || (checkMyCar_?.Invoke()?.Spec ?? false),
				};
				lastResult_ = myResult_;
				if (firstResultAt_ < 0) {
					firstResultAt_ = now;
				}
				lastHeartbeat_ = double.MinValue; // send it right away
			}

			if (firstResultAt_ >= 0 && now - firstResultAt_ > ResultWaitSeconds) {
				if (myResult_ == null) {
					Run.Cancel("Took too long");
					return;
				}
				if (peerResult_ == null) {
					peerResult_ = new RunResult { Run = RunIndex, Reason = "no result arrived" };
				}
			}

			if (myResult_ != null && peerResult_ != null) {
				Decide(now);
			} else if (myResult_ != null) {
				Message = myResult_.Finished
					? $"Your time: {TimeAttackRun.FormatTime(myResult_.Time)}. Waiting for {PeerName}"
					: $"{myResult_.Reason} Waiting for {PeerName}";
			}
		}

		private void Decide(double now) {
			BattleRun run = BattleRules.Decide(Format, RunIndex, Leader, Me, Peer, PeerName, myResult_, peerResult_);
			Runs.Add(run);
			if (!Open) {
				Counted &= myResult_.SpecAtEnd && peerResult_.SpecAtEnd;
			}

			if (run.Winner != 0) {
				Winner = run.Winner;
				End(Phase.Finished, run.Text, now);
			} else if (RunIndex + 1 >= MaxRuns) {
				End(Phase.Finished, $"{run.Text}. No winner after {MaxRuns} runs: a draw", now);
			} else {
				RunIndex++;
				NewRun();
				State = Phase.Gridding;
				string lead = BattleRules.LeaderGapFor(RunIndex) < BattleRules.LeaderGapFor(RunIndex - 1) ? $" (the leader now needs {BattleRules.LeaderGapFor(RunIndex):0.0} s)" : "";
				string next = Format == BattleFormat.CatAndMouse ? (IAmLeader ? $"You lead the next run{lead}" : $"You chase in the next run{lead}") : "Line up for the next run";
				Message = $"{run.Text}. {next}";
			}
		}

		private void OnHeartbeat(BattleMessage message, double now) {
			// Clock sync: our clock when we sent what they echo, how long they held it, and their clock now.
			if (message.EchoHold >= 0 && message.Echo > double.MinValue) {
				double roundTrip = now - message.Echo - message.EchoHold;
				if (roundTrip >= 0 && roundTrip < 5) {
					offsets_.Add(message.SentAt + roundTrip / 2 - now);
					if (offsets_.Count > OffsetSamples) {
						offsets_.RemoveAt(0);
					}
					ClockOffset = offsets_.OrderBy(o => o).ElementAt(offsets_.Count / 2);
					RoundTrip = roundTrip;
				}
			}
			peerSentAt_ = message.SentAt;
			peerSentHeardAt_ = now;
			peerPhase_ = (Phase)message.Phase;
			peerRunIndex_ = message.RunIndex;

			bool sameRun = message.RunIndex == RunIndex;
			PeerReady = sameRun && message.Ready;
			PeerCar = new BattleCar { Spec = message.Spec, TuneSet = message.TuneSet, TuneCode = message.TuneCode, Car = message.Car };
			if (sameRun) {
				double at = message.SentAt;
				double since = at - peerProgressAt_;
				if (peerProgressAt_ >= 0 && since > 0.05 && since < 1.5) {
					double speed = Math.Max(0, (message.Progress - PeerProgress) / since);
					peerSpeed_ += (speed - peerSpeed_) * 0.5;
				}
				if (since > 0 || peerProgressAt_ < 0) {
					PeerProgress = message.Progress;
					peerProgressAt_ = at;
				}
			}
			if (message.Result != null && message.Result.Run == RunIndex && peerResult_ == null && (State == Phase.Running || State == Phase.Countdown)) {
				peerResult_ = message.Result;
				if (firstResultAt_ < 0) {
					firstResultAt_ = now;
				}
				// Told straight away, not only once this driver finishes too.
				if (!peerResult_.Finished && myResult_ == null) {
					string why = peerResult_.Reason.TrimEnd('.');
					PeerOut = $"{PeerName} is out{(why.Length > 0 ? $" ({why})" : "")}: finish to win";
				}
			}
		}

		private void Heartbeat(double now) {
			if (now - lastHeartbeat_ < HeartbeatSeconds) {
				return;
			}
			lastHeartbeat_ = now;
			BattleMessage beat = New(BattleKind.Heartbeat);
			beat.SentAt = now;
			beat.Echo = peerSentHeardAt_ >= 0 ? peerSentAt_ : double.MinValue;
			beat.EchoHold = peerSentHeardAt_ >= 0 ? now - peerSentHeardAt_ : -1;
			beat.Ready = MyReady;
			beat.Spec = MyCar?.Spec ?? false;
			beat.TuneSet = MyCar?.TuneSet ?? "";
			beat.TuneCode = MyCar?.TuneCode ?? "";
			beat.Car = MyCar?.Car ?? "";
			beat.Progress = Run?.TotalProgress ?? 0f;
			beat.RunIndex = RunIndex;
			beat.Phase = (byte)State;
			beat.Result = lastResult_;
			outbox_.Add(beat);
		}

		private void End(Phase phase, string message, double now) {
			State = phase;
			Message = message;
			endedAt_ = now;
			MyReady = false;
		}

		private BattleMessage New(BattleKind kind) => new BattleMessage { Kind = kind, BattleId = Id, From = Me, To = Peer };

		/// <summary>The leader of a run: the first run's leader is picked by the battle ID (so both sides agree), then they swap.</summary>
		private ulong LeaderFor(int run) {
			ulong host = IsHost ? Me : Peer;
			ulong guest = IsHost ? Peer : Me;
			ulong first = (Id & 1) == 0 ? host : guest;
			return run % 2 == 0 ? first : (first == host ? guest : host);
		}
	}
}
