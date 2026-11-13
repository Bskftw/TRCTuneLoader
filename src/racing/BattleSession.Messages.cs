// A battle's messages: receiving them, crossed challenges, and the heartbeats both ways (clocks, ready, progress, results).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class BattleSession {
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
						if (IsHighway) {
							State = Phase.Countdown;
						} else {
							Run.StartCountdown(goTime_);
							if (Run.State == TimeAttackRun.Phase.Countdown) {
								State = Phase.Countdown;
							}
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
			PeerCar = new BattleCar { Spec = message.Spec, TuneSet = message.TuneSet, TuneCode = message.TuneCode, Car = message.Car, RuleOk = message.RuleOk };
			if (message.HasRuleOk) {
				PeerElo = message.Elo;
			}
			PeerPosition = message.Position;
			PeerHeading = message.Heading;
			PeerKmh = message.Kmh;
			if (IsHighway && State == Phase.Running && message.RunIndex == RunIndex) {
				if (peerPhase_ == Phase.Running || peerPhase_ == Phase.Finished) {
					peerRan_ = true;
				}
				if (peerRan_) {
					PeerSp = message.Sp;
				}
			}
			if (sameRun) {
				double at = message.SentAt;
				double since = at - peerProgressAt_;
				if (peerProgressAt_ >= 0 && since > 0.05 && since < 1.5) {
					double speed = Math.Max(0, (message.Progress - PeerProgress) / since);
					peerSpeed_ += (speed - peerSpeed_) * 0.5;
				}
				if (since > 0 || peerProgressAt_ < 0) {
					PeerProgress = message.Progress;
					PeerTrapTotal = message.TrapTotal;
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
			beat.TrapTotal = Run?.TrapTotal ?? 0f;
			beat.RunIndex = RunIndex;
			beat.Phase = (byte)State;
			beat.Result = lastResult_;
			beat.Position = myPosition_;
			beat.Heading = myForward_;
			beat.Kmh = myKmh_;
			beat.Sp = MySp;
			beat.RuleOk = MyCar?.RuleOk ?? false;
			beat.Elo = MyElo;
			outbox_.Add(beat);
		}
	}
}
