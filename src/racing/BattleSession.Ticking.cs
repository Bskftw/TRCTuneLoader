// A battle every frame: the player's car fed in, and highway battles' SP and gap.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class BattleSession {
		/// <summary>Called every frame with the player's car (and, during a battle, whether it's on the course's road).</summary>
		public void Tick(double now, Vector3 position, Vector3 forward, float speedKmh, float timeScale, bool? onRoad) {
			switch (State) {
				case Phase.Inviting:
					if (now - createdAt_ > InviteLimit) {
						End(Phase.Ended, PeerSeen ? $"{PeerName} didn't answer" : $"The challenge never got to {PeerName}", now);
					} else if (!PeerSeen) {
						// Sent again until their mod says it's there; the pieces of each copy fill in the last one's gaps.
						if (now - lastChallengeSent_ >= ChallengeResendSeconds) {
							SendChallenge(now);
						}
						if (now - createdAt_ > NotSeenSeconds) {
							Message = SendsCourse
								? $"Still sending your course to {PeerName}: a long one can take up to a minute to get there"
								: $"Still sending: {PeerName}'s TRC Tune Loader hasn't confirmed the challenge yet";
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

			myPosition_ = position;
			myForward_ = CourseGeometry.Level(forward);
			myKmh_ = Mathf.Abs(speedKmh);
			ResendCountdown(now);
			if (IsHighway) {
				TickHighway(now);
				Heartbeat(now);
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

		/// <summary>
		/// A highway battle: line up side by side at speed, a rolling 3-2-1, then the one behind loses SP, faster the
		/// bigger the gap; a hit costs some too. Empty SP (or falling right out of sight) loses.
		/// </summary>
		private void TickHighway(double now) {
			double dt = lastHighwayTick_ < 0 ? 0 : Math.Min(0.5, now - lastHighwayTick_);
			lastHighwayTick_ = now;
			Vector3 along = CourseGeometry.Level(myForward_ + PeerHeading);
			if (along == Vector3.zero) {
				along = myForward_;
			}
			HighwayGap = -Vector3.Dot(PeerPosition - myPosition_, along);
			float apart = Vector3.Distance(PeerPosition, myPosition_);
			switch (State) {
				case Phase.Gridding: {
					bool together = apart <= HighwayStartMetres && myKmh_ >= HighwayStartKmh && PeerKmh >= HighwayStartKmh;
					if (!together) {
						MyCar = null;
					} else if (MyCar == null) {
						MyCar = checkMyCar_?.Invoke() ?? new BattleCar();
					}
					MyReady = together && CarOk(MyCar);
					bool sameSet = !IsTrcSpec || (MyCar != null && PeerCar.TuneSet == MyCar.TuneSet);
					Message = !together ? $"Drive alongside {PeerName}: within {HighwayStartMetres:0} m, both over {HighwayStartKmh:0} km/h ({apart:0} m apart now)"
						: !CarOk(MyCar) ? NotAllowedMessage
						: !PeerReady ? $"Alongside. Waiting for {PeerName}"
						: !sameSet ? "Different tune sets"
						: "Both ready: keep rolling";
					if (IsHost) {
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
					break;
				}
				case Phase.Countdown:
					Message = now < goTime_ ? $"{Math.Ceiling(goTime_ - now):0}" : "";
					if (now >= goTime_) {
						State = Phase.Running;
						MySp = FullSp;
						PeerSp = FullSp;
						Message = "";
					}
					break;
				case Phase.Running: {
					// The other side never started (the countdown got lost): call it off and line up again, like a course battle.
					if (!peerRan_ && peerRunIndex_ == RunIndex && peerPhase_ == Phase.Gridding && now - goTime_ > MissedGoSeconds) {
						BattleMessage abort = New(BattleKind.Abort);
						abort.CountdownId = countdownId_;
						abort.Reason = "the start didn't reach both of you";
						outbox_.Add(abort);
						BackToGrid("The start didn't reach both of you. Line up again");
						return;
					}
					if (HighwayGap < -DrainFromMetres) {
						float behind = -HighwayGap - DrainFromMetres;
						MySp = Math.Max(0f, MySp - Mathf.Min(MaxDrainPerSecond, behind / DrainMetresPerSp) * (float)dt);
					}
					if (apart > LostMetres && HighwayGap < 0f) {
						MySp = 0f;
					}
					bool timeUp = now - goTime_ > HighwayMaxSeconds;
					if ((MySp <= 0f || PeerSp <= 0f || timeUp) && !peerRan_) {
						// Only this side was running: nobody wins, nothing counts.
						Counted = false;
						End(Phase.Ended, $"{PeerName} never started this run, so it doesn't count", now);
						break;
					}
					if (MySp <= 0f || PeerSp <= 0f || timeUp) {
						bool iWin = MySp > 0f && (PeerSp <= 0f || (timeUp && MySp > PeerSp));
						bool draw = timeUp && Math.Abs(MySp - PeerSp) < 0.5f;
						Winner = draw ? 0 : iWin ? Me : Peer;
						string text = draw ? "Time's up, dead even: a draw"
							: iWin ? $"You gapped {PeerName}: you win" : $"{PeerName} gapped you: {PeerName} wins";
						var mine = new RunResult { Run = 0, Finished = true, Time = now - goTime_, SpecAtEnd = Open && Regulation == null || CarOk(checkMyCar_?.Invoke()) };
						Runs.Add(new BattleRun { Index = 0, Mine = mine, Theirs = new RunResult { Run = 0, Finished = true, Time = now - goTime_, SpecAtEnd = true }, Winner = Winner, Text = text });
						if (!Open || Regulation != null) {
							Counted &= mine.SpecAtEnd;
						}
						End(Phase.Finished, text, now);
					}
					break;
				}
			}
		}
	}
}
