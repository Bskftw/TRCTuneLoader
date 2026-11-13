// The 1v1 battle view: invites, the battle card while one runs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void DrawBattleView() {
			if (!battleSync_.IsAvailable) {
				GUILayout.Label(battleSync_.Status, Theme.PillNeutral);
				return;
			}
			RefreshPlayersIfDue();
			battleScroll_ = GUILayout.BeginScrollView(battleScroll_);

			if (!string.IsNullOrEmpty(battleResult_)) {
				GUILayout.Label(battleResult_, Theme.Body);
			}
			foreach (var (challenge, _) in invites_.ToList()) {
				DrawInvite(challenge);
			}
			if (battle_ != null && (battle_.Active || battle_.Started)) {
				DrawBattleCard();
			}
			if (battle_ == null || !battle_.Active) {
				DrawNewChallenge();
			}

			GUILayout.EndScrollView();
		}

		/// <summary>
		/// A challenge waiting, at the top of every other tab: one came in while the window was open on something
		/// else (Oct 29: one went by unseen while the player was in the course builder).
		/// </summary>
		private void DrawInviteBanner() {
			if (invites_.Count == 0 || (tab_ == BattlesTab && battleViewMode_ == 0) || tourStep_ >= 0) {
				return;
			}
			BattleMessage challenge = invites_[invites_.Count - 1].Challenge;
			GUILayout.BeginHorizontal(Theme.Guide);
			GUILayout.Label($"<b>{BattleMessage.PlainName(challenge.FromName)} challenges you</b>  <color={Theme.MutedHex}>{BattleFormats[(int)challenge.Format]}" +
				$"{(invites_.Count > 1 ? $" · {invites_.Count} challenges waiting" : "")}</color>", Theme.Body);
			GUILayout.FlexibleSpace();
			if (NavButton("Answer", Theme.ButtonPrimary)) {
				GoToBattle(0);
			}
			GUILayout.EndHorizontal();
		}

		private void DrawInvite(BattleMessage challenge) {
			Course course = CourseForChallenge(challenge, out string problem);
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"{BattleMessage.PlainName(challenge.FromName)} challenges you", Theme.Heading);
			string courseName = challenge.Course?.Name ?? course?.Name ?? challenge.CourseCode;
			GUILayout.Label($"{courseName} · {BattleFormats[(int)challenge.Format]} · {RuleText(challenge.Open, challenge.Regulation, challenge.Fun)}", Theme.Body);
			if (course == null) {
				GUILayout.Label($"Can't accept: {problem}", Theme.PillWarn);
			}
			if (challenge.Regulation != null) {
				RegulationResult mine = challenge.Regulation.Check(MyCarFacts());
				GUILayout.Label($"{challenge.Regulation.Summary()}", Theme.Small);
				GUILayout.Label(mine.Passes ? "Your car meets this regulation." : $"Your car doesn't meet it yet: {string.Join("; ", mine.Broken.Take(3))}", mine.Passes ? Theme.PillGood : Theme.PillWarn);
				if (RegulationByCode(challenge.Regulation.Code) == null && NavButton("Save this regulation", Theme.Button)) {
					SaveRegulation(challenge.Regulation.Copy(), $"Saved {challenge.Regulation.Name}.");
				}
			}
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && course != null && hasPose_;
			if (NavButton("Accept", Theme.ButtonPrimary)) {
				AnswerChallenge(challenge, true);
			}
			GUI.enabled = enabled;
			if (NavButton("Decline", Theme.Button)) {
				AnswerChallenge(challenge, false);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		private void DrawBattleCard() {
			BattleSession b = battle_;
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"Battle vs {b.PeerName}", Theme.Heading);
			string role = b.Format == BattleFormat.CatAndMouse ? $" · run {b.RunIndex + 1}: you {(b.IAmLeader ? "lead" : "chase")}" : $" · run {b.RunIndex + 1}";
			string laps = b.Course.IsCircuit ? $" · {b.Laps} lap{(b.Laps == 1 ? "" : "s")}" : "";
			GUILayout.Label($"{b.Course.Name}{laps} · {BattleFormats[(int)b.Format]} · {RuleText(b.Open, b.Regulation, b.Fun)}{(b.Active ? role : "")}", Theme.Small);
			GUILayout.Label(b.Message, b.Active ? Theme.Body : b.Winner == b.Me ? Theme.PillGood : b.Winner == b.Peer ? Theme.PillBad : Theme.PillNeutral);

			foreach (BattleRun run in b.Runs) {
				string Shown(RunResult r) => !r.Finished ? "DNF" : b.IsSpeedTraps ? $"{r.TrapTotal:0} km/h at the traps" : TimeAttackRun.FormatTime(r.Time);
				string mine = Shown(run.Mine);
				string theirs = Shown(run.Theirs);
				string led = b.Format == BattleFormat.CatAndMouse ? (run.Leader == b.Me ? " (you led)" : " (you chased)") : "";
				GUILayout.Label($"Run {run.Index + 1}{led}: you {mine}, {b.PeerName} {theirs}. {run.Text}", Theme.Small);
			}
			if (!b.Active && b.Started) {
				string kind = b.Regulation != null ? $"{b.Regulation.Name} battle" : "TRC spec battle";
				string rule = b.Regulation != null ? "met the regulation" : "were spec";
				GUILayout.Label(b.Fun ? "Just for fun: no ratings changed." : b.Open && b.Regulation == null ? "Open battle: it doesn't count."
					: b.Counted && b.Winner != 0 ? $"{kind}: both cars {rule} the whole way." : $"Doesn't count: a car {(b.Regulation != null ? "didn't meet the regulation" : "wasn't TRC spec")} at a start or finish.", Theme.Small);
			}
			if (b.Active && b.RoundTrip > 0) {
				GUILayout.Label($"Connection: {b.RoundTrip * 1000:0} ms there and back", Theme.Small);
			}

			GUILayout.BeginHorizontal();
			if (b.State == BattleSession.Phase.Gridding && !b.IsHighway && NavButton("Teleport to my spot", Theme.ButtonPrimary)) {
				TeleportToBattleSpot();
			}
			if (b.Active) {
				if (NavButton(b.State == BattleSession.Phase.Inviting ? "Take the challenge back" : "Leave battle", Theme.Button)) {
					b.Leave(BattleNow);
				}
			} else {
				PlayerRow peer = players_.FirstOrDefault(p => p.Player.SteamId == b.Peer);
				if (peer != null && (b.Course.Usable || b.IsHighway) && NavButton("Run it back", Theme.ButtonPrimary)) {
					RunItBack(b);
				}
				// A course that came with the challenge (theirs, not one this PC has): keep it to practise on.
				if (!b.IsHost && !b.IsHighway && b.Course.Source == CourseSource.Custom && string.IsNullOrEmpty(b.Course.FilePath) &&
					NavButton("Save this course", Theme.Button)) {
					SaveChallengeCourse(b.Course, $"{b.PeerName}'s course");
				}
				if (NavButton("Close", Theme.Button)) {
					battle_ = null;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}
	}
}
