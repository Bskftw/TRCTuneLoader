// A race every frame: qualifying, the race itself, jump starts and recalls, and where to line up.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class RaceSession {
		/// <summary>Called every frame with this player's car (and, in a race, whether it's on the course's road).</summary>
		public void Tick(double now, Vector3 position, Vector3 forward, float speedKmh, float timeScale, bool? onRoad) {
			if (Ended) {
				return;
			}
			myPosition_ = position;
			myForward_ = CourseGeometry.Level(forward);
			myKmh_ = Mathf.Abs(speedKmh);
			RaceDriver self = MyDriver;
			if (self != null) {
				self.Position = myPosition_;
				self.Heading = myForward_;
				self.Kmh = myKmh_;
				self.HasPosition = true;
				self.Score = myScore_;
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
					if (!IsGame) {
						TickRace(now, position, forward, speedKmh, timeScale, onRoad);
					}
					break;
				default:
					if (Run != null && qualifyingRun_) {
						Run = null;
					}
					break;
			}
			if (!IsHost && !IsGame && Phase == RacePhase.Running && Racers.Count > 0 && Racers.All(d => d.State != RacerState.Racing)) {
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
			// The status line is only text for the screen: written 4 times a second, and at once when the phase changes
			// (docs/archive/OPTIMIZATION_REPORT.md item 6), not every frame.
			if (Phase != messagePhase_ || now >= nextMessage_ || now < nextMessage_ - 1.0) {
				messagePhase_ = Phase;
				nextMessage_ = now + 0.25;
				UpdateMessage(now);
			}
			Beat(now);
		}

		private RacePhase? messagePhase_;
		private double nextMessage_ = double.MinValue;
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
					KeepGoing = true,
					OnJump = RunJumpRule(Settings.Jump),
					Spot = MySpot(),
					ClockFromLine = Settings.SingleFile,
				};
				myJumps_ = 0;
				recallsSeen_ = Recalls;
				myState_ = RacerState.Racing;
				myTime_ = 0;
				myPenalty_ = 0;
				myReason_ = "";
				myCar_ = null;
			}

			// The start was recalled (someone jumped): back to the grid spot, and the countdown starts again once everyone's ready.
			if (Recalls != recallsSeen_) {
				recallsSeen_ = Recalls;
				if (myState_ == RacerState.Racing && Run.State != TimeAttackRun.Phase.ToStart) {
					Run.Restart();
				}
				countdownStarted_ = countdownId_;
			}
			if (Run.JumpStarts > myJumps_) {
				myJumps_ = Run.JumpStarts;
				if (Settings.Jump == JumpRule.Recall) {
					myJumpedCountdown_ = countdownId_;
					lastBeat_ = double.MinValue;
					if (IsHost) {
						Recall(now, myName_);
					}
				}
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

			// The car is checked once each time it stops in its spot (not every frame), like battles; and again every few
			// seconds while it may not race, so a car fixed in the dyno without moving gets ready.
			if (Run.State == TimeAttackRun.Phase.ToStart) {
				if (!Run.ReadyToStart) {
					myCar_ = null;
				} else if (myCar_ == null || (!CarAllowed(myCar_) && now - myCarCheckedAt_ >= RecheckSeconds)) {
					myCar_ = checkCar_?.Invoke() ?? new BattleCar();
					myCarCheckedAt_ = now;
				}
			}

			if (myState_ == RacerState.Racing && before != Run.State && !Run.Active) {
				if (Run.State == TimeAttackRun.Phase.Finished) {
					myState_ = RacerState.Finished;
					myPenalty_ = Run.JumpStarts > 0 && Settings.Jump == JumpRule.Penalty && !Settings.NoPenalties ? Settings.JumpPenaltySeconds : 0;
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
			me.TrapTotal = qualifyingRun_ ? 0f : Run.TrapTotal;
			me.Ready = MyReady;
		}

		private bool MyReady => Run != null && Run.State == TimeAttackRun.Phase.ToStart && Run.ReadyToStart && myCar_ != null && CarAllowed(myCar_);

		/// <summary>The car may race: TRC spec in a TRC spec race, meeting the regulation in a regulated one, anything in an open one.</summary>
		private bool CarAllowed(BattleCar car) => Settings.Regulation != null ? car.RuleOk : Settings.Open || car.Spec;

		/// <summary>What a driver's own run does on a jump start; the race's penalty is added at the finish only for <see cref="JumpRule.Penalty"/>.</summary>
		public static JumpRule RunJumpRule(JumpRule race) =>
			race == JumpRule.Disqualify ? JumpRule.Disqualify : race == JumpRule.Recall ? JumpRule.Restart : JumpRule.Penalty;

		/// <summary>
		/// Host: a jump start under the recall rule. Everyone goes back to their grid spot, and the countdown starts
		/// again once they're all ready (only before GO, or just after it when the jump was heard late).
		/// </summary>
		private void Recall(double now, string who) {
			if (!IsHost || Settings.Jump != JumpRule.Recall) {
				return;
			}
			bool late = Phase == RacePhase.Running && now - goHostTime_ <= RecallAfterGoSeconds;
			if (Phase != RacePhase.Countdown && !late) {
				return;
			}
			Recalls++;
			RecallBy = BattleMessage.PlainName(who ?? "");
			Phase = RacePhase.Gridding;
			bothReadySince_ = -1;
			foreach (RaceDriver d in Racers) {
				d.Ready = false;
				d.Progress = 0;
				d.ProgressAt = -1;
			}
			foreach (TimeAttackRun run in consoleRuns_.Values) {
				run.Restart();
			}
			lobbyDirty_ = true;
			lastHostBeat_ = double.MinValue;
		}

		/// <summary>This driver's grid spot (null without one yet).</summary>
		public GridSpot? MySpot() {
			RaceDriver me = MyDriver;
			if (Course == null || me == null || me.Slot < 0) {
				return null;
			}
			List<GridSpot> spots = Course.GridSpots(me.Slot + 1, Settings.PoleLeft, Settings.GridShape);
			return spots[me.Slot];
		}
	}
}
