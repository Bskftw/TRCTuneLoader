// A battle's runs: where to line up, gridding, the countdown, running, and deciding each run.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class BattleSession {
		/// <summary>Where this driver should line up: the middle of their start box, facing the course.</summary>
		public Vector3 MySpot() {
			if (IsHighway) {
				return myPosition_;
			}
			CourseGate start = Course.Start;
			if (Format == BattleFormat.CatAndMouse) {
				return start.Center - start.Forward * (IAmLeader ? LeaderSpot : ChaserSpot);
			}
			if (LinedUp) {
				// Single file: the challenger in front, the other driver one car length and a bit behind.
				return start.Center - start.Forward * (IsHost ? FrontSpot : FrontSpot + Course.LineUpStep);
			}
			// Race: side by side, close together (bskftw, Nov 2: cars were too far apart), the host on the left.
			return start.Center - start.Forward * FrontSpot + start.Right * (Course.SideBySideGap / 2f) * (IsHost ? -1f : 1f);
		}

		/// <summary>The course goes with the challenge (not official, so the other driver may not have it).</summary>
		private bool SendsCourse => !IsHighway && Course != null && !Course.Official;

		private double InviteLimit => SendsCourse ? InviteWithCourseSeconds : InviteSeconds;

		private void SendChallenge(double now) {
			lastChallengeSent_ = now;
			BattleMessage challenge = New(BattleKind.Challenge);
			challenge.FromName = myName_;
			challenge.Format = Format;
			challenge.Open = Open;
			challenge.Laps = Laps;
			challenge.MatchId = MatchId;
			challenge.MatchGame = MatchGame;
			challenge.CourseCode = IsHighway ? "" : Course.Code;
			challenge.Course = IsHighway || Course.Official ? null : Course;
			if (IsSpeedTraps) {
				challenge.Traps = new List<CourseGate>(Course.Traps);
			}
			challenge.Fun = Fun;
			challenge.Regulation = Regulation;
			challenge.SingleFile = SingleFile;
			outbox_.Add(challenge);
		}

		private void BeginGridding(double now) {
			Started = true;
			lastHeard_ = now;
			NewRun();
			State = Phase.Gridding;
		}

		private void NewRun() {
			if (IsHighway) {
				// No course: the battle is on SP, driven anywhere.
				Run = null;
				MySp = FullSp;
				PeerSp = FullSp;
			} else {
				// A reset doesn't end a battle run: a driver who messes up can still win if the other does too.
				Run = new TimeAttackRun(Course, Laps) { ExternalStart = true, AllowResets = true, KeepGoing = true, ClockFromLine = LinedUp };
				if (Format == BattleFormat.CatAndMouse && !IAmLeader) {
					Run.GridFront = ChaserBoxFront;
					Run.GridBack = ChaserBoxBack;
				} else {
					Run.GridBack = Format == BattleFormat.CatAndMouse ? LeaderBoxBack : LinedUp ? RaceBoxBack + Course.LineUpStep : RaceBoxBack;
				}
			}
			myResult_ = null;
			peerResult_ = null;
			firstResultAt_ = -1;
			bothReadySince_ = -1;
			MyCar = null;
			MyReady = false;
			PeerProgress = 0f;
			PeerTrapTotal = 0f;
			PeerOut = "";
			peerProgressAt_ = -1;
			peerSpeed_ = 0;
			peerRan_ = false;
		}

		private void BackToGrid(string message) {
			Run?.Restart();
			if (IsHighway) {
				MySp = FullSp;
				PeerSp = FullSp;
				MyCar = null;
				MyReady = false;
				peerRan_ = false;
			}
			State = Phase.Gridding;
			bothReadySince_ = -1;
			Message = message;
		}

		/// <summary>
		/// The host's countdown goes again (same countdown, same GO) until the other side's heartbeat shows it got
		/// it: one lost packet shouldn't leave one driver racing alone.
		/// </summary>
		private void ResendCountdown(double now) {
			if (!IsHost || State != Phase.Countdown || countdownId_ == 0 || now >= goTime_ - 0.2 || now - lastCountdownSent_ < CountdownResendSeconds) {
				return;
			}
			if (peerRunIndex_ == RunIndex && peerPhase_ != Phase.Gridding) {
				return;
			}
			lastCountdownSent_ = now;
			BattleMessage countdown = New(BattleKind.Countdown);
			countdown.CountdownId = countdownId_;
			countdown.RunIndex = RunIndex;
			countdown.GoHostTime = goTime_;
			outbox_.Add(countdown);
		}

		private void UpdateGrid(double now) {
			bool inPlace = Run.ReadyToStart;
			if (!inPlace) {
				MyCar = null;
			} else if (MyCar == null) {
				// Checked once each time the car stops in its box, not every frame.
				MyCar = checkMyCar_?.Invoke() ?? new BattleCar();
			}
			MyReady = inPlace && CarOk(MyCar);
			bool sameSet = !IsTrcSpec || (MyCar != null && PeerCar.TuneSet == MyCar.TuneSet);

			if (!inPlace) {
				Message = Run.Message;
			} else if (!CarOk(MyCar)) {
				Message = NotAllowedMessage;
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
			lastCountdownSent_ = now;
			Run?.StartCountdown(goTime_);
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
					SpecAtEnd = Open && Regulation == null || CarOk(checkMyCar_?.Invoke()),
					TrapTotal = Run.TrapTotal,
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
				Message = myResult_.Finished && IsSpeedTraps
					? $"Your traps: {myResult_.TrapTotal:0} km/h. Waiting for {PeerName}"
					: myResult_.Finished
					? $"Your time: {TimeAttackRun.FormatTime(myResult_.Time)}. Waiting for {PeerName}"
					: $"{myResult_.Reason} Waiting for {PeerName}";
			}
		}

		private void Decide(double now) {
			BattleRun run = BattleRules.Decide(Format, RunIndex, Leader, Me, Peer, PeerName, myResult_, peerResult_);
			Runs.Add(run);
			if (!Open || Regulation != null) {
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
	}
}
