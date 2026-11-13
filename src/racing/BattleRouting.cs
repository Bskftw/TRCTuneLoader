// Battle messages to the right battle: routing them, crossed challenges, closing invites, sending and logging them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void Route(BattleMessage message, double now) {
			if (message.To != MySteamId) {
				return;
			}
			if (message.Kind == BattleKind.Challenge) {
				RouteChallenge(message);
				return;
			}
			LogReceived(message);
			if (message.Kind == BattleKind.Leave) {
				int taken = invites_.FindIndex(i => i.Challenge.BattleId == message.BattleId && i.Challenge.From == message.From);
				if (taken >= 0) {
					string name = BattleMessage.PlainName(invites_[taken].Challenge.FromName);
					invites_.RemoveAt(taken);
					closedChallenges_.Add(message.BattleId);
					bool inBattleWithThem = battle_ != null && battle_.Active && battle_.Started && battle_.Peer == message.From;
					if (!inBattleWithThem) {
						battleResult_ = $"{name} took their challenge back.";
					}
				}
			}
			battle_?.Receive(message, now);
		}

		/// <summary>
		/// A challenge for this player (every copy: the host sends it until our Seen gets there). It's answered
		/// "already in a battle" only during a battle; while this player's own challenge waits, it's shown like
		/// any other, and when it's the same battle the other way round, the two become one.
		/// </summary>
		private void RouteChallenge(BattleMessage challenge) {
			if (closedChallenges_.Contains(challenge.BattleId) || (battle_ != null && battle_.Id == challenge.BattleId)) {
				return;
			}
			string name = BattleMessage.PlainName(challenge.FromName);
			if (InRace) {
				closedChallenges_.Add(challenge.BattleId);
				SendBattle(new BattleMessage {
					Kind = BattleKind.Answer, BattleId = challenge.BattleId, From = MySteamId, To = challenge.From, Reason = "in a race",
				});
				return;
			}
			if (battle_ != null && battle_.Active && battle_.Started) {
				closedChallenges_.Add(challenge.BattleId);
				SendBattle(new BattleMessage {
					Kind = BattleKind.Answer, BattleId = challenge.BattleId, From = MySteamId, To = challenge.From, Reason = "already in a battle",
				});
				TrcLog.Info($"Battle {challenge.BattleId}: challenge from {name} declined by itself, you're in a battle.");
				return;
			}

			Course course = CourseForChallenge(challenge, out string problem);
			SendBattle(new BattleMessage {
				Kind = BattleKind.Seen, BattleId = challenge.BattleId, From = MySteamId, To = challenge.From, Accept = course != null, Reason = problem,
			});
			if (invites_.Exists(i => i.Challenge.BattleId == challenge.BattleId)) {
				return;
			}
			invites_.Add((challenge, Time.unscaledTime));
			string courseName = challenge.Course?.Name ?? course?.Name ?? "";
			TrcLog.Info($"Battle {challenge.BattleId}: challenge from {name} on {courseName} ({challenge.CourseCode}), " +
				$"{BattleFormats[(int)challenge.Format]}, {RuleText(challenge.Open, challenge.Regulation, challenge.Fun)}.{(course == null ? $" Can't accept: {problem}." : "")}");

			if (course != null && IsCalledMatch(challenge) && (battle_ == null || !battle_.Active)) {
				TrcLog.Info($"Battle {challenge.BattleId}: the tournament battle the host called, accepted by itself.");
				AnswerChallenge(challenge, true);
				return;
			}
			if (course != null && battle_ != null && battle_.YieldsTo(challenge)) {
				TrcLog.Info($"Battle {challenge.BattleId}: you and {name} challenged each other to the same battle, so it goes ahead on {name}'s challenge.");
				AnswerChallenge(challenge, true);
			}
		}

		/// <summary>
		/// A battle started: challenges still waiting are answered "already in a battle" (the opponent's own,
		/// left over from challenging each other, just goes), so none pops up during the battle.
		/// </summary>
		private void CloseInvitesForBattle() {
			foreach (var (challenge, _) in invites_) {
				closedChallenges_.Add(challenge.BattleId);
				if (challenge.From != battle_.Peer) {
					SendBattle(new BattleMessage {
						Kind = BattleKind.Answer, BattleId = challenge.BattleId, From = MySteamId, To = challenge.From, Reason = "already in a battle",
					});
				}
			}
			invites_.Clear();
		}

		private void SendBattle(BattleMessage message) {
			int packets = battleSync_.Send(message);
			if (message.Kind != BattleKind.Heartbeat && loggedSteps_.Add($"sent {message.BattleId} {message.Kind}")) {
				string how = packets == 0 ? " (KSL didn't send it)" : packets > 1 ? $" ({packets} packets)" : "";
				TrcLog.Info($"Battle {message.BattleId}: sent {message.Kind}{how}.");
			}
		}

		/// <summary>The first message of each kind in a battle goes in the log.</summary>
		private void LogReceived(BattleMessage message) {
			if (!loggedSteps_.Add($"got {message.BattleId} {message.Kind}")) {
				return;
			}
			string detail = message.Kind == BattleKind.Answer ? (message.Accept ? ": accepted" : $": declined{(message.Reason.Length > 0 ? $" ({message.Reason})" : "")}")
				: message.Kind == BattleKind.Seen ? (message.Accept ? ": the challenge got there" : $": the challenge got there, but they can't accept it ({message.Reason})")
				: message.Kind == BattleKind.Heartbeat ? " (the battle connection works)"
				: "";
			TrcLog.Info($"Battle {message.BattleId}: got {message.Kind}{detail}.");
		}
	}
}
