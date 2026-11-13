// Challenging a player: the opponent, the course, the format, laps and rules.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void DrawNewChallenge() {
			GUILayout.BeginVertical(Theme.Card);
			if (!hasPose_ || map_ == null) {
				GUILayout.Label("Join a room and drive onto the map: you challenge players in the same room, on a course of that map.", Theme.Small);
				GUILayout.EndVertical();
				return;
			}

			List<PlayerRow> rivals = players_.Where(p => !p.Player.IsLocal && p.Report != null).ToList();
			List<PlayerRow> consoles = players_.Where(p => !p.Player.IsLocal && p.Player.IsConsole).ToList();
			if (rivals.Count == 0) {
				GUILayout.Label(string.IsNullOrEmpty(playersError_) ? "Nobody else in this room runs TRC Tune Loader yet." : playersError_, Theme.Small);
			} else {
				// A table like the picture bskftw picked (Nov 3): player, car, status, Elo and what you can do.
				Course pickedFor = CoursesHere().FirstOrDefault(c => c.Code == pickedCourse_);
				CourseKind kind = pickedFormat_ == (int)BattleFormat.Highway ? CourseKind.Highway : pickedFor?.Kind ?? CourseKind.Touge;
				float[] widths = { 150f, 140f, 190f, 60f, 0f };
				TableHead(new[] { "Player", "Car", "Status", "Elo", "" }, widths);
				foreach (PlayerRow rival in rivals) {
					bool canBattle = System.Version.TryParse(rival.Report.LoaderVersion, out System.Version theirs) && theirs >= BattleVersion;
					BattleMessage theirChallenge = invites_.Select(i => i.Challenge).LastOrDefault(c => c.From == rival.Player.SteamId);
					RowState state = StateOf(rival);
					string status = theirChallenge != null ? Dot(Theme.WarnHex, "Challenged you")
						: !canBattle ? Dot(Theme.MutedHex, $"needs {BattleVersion}+")
						: FlaggedMods(rival) > 0 ? Dot(Theme.BadHex, "Flagged mod")
						: state == RowState.Good ? Dot(Theme.GoodHex, "TRC spec")
						: state == RowState.Bad ? Dot(Theme.BadHex, "Not spec")
						: state == RowState.Warn ? Dot(Theme.WarnHex, "Spec, body unchecked")
						: Dot(Theme.MutedHex, "Not checked");
					float elo = rival.Report.EloFor(kind);
					GUILayout.BeginHorizontal();
					TableCell($"<b>{rival.Player.Name}</b>", widths[0]);
					TableCell(rival.Player.CarName, widths[1]);
					TableCell(status, widths[2]);
					TableCell(elo > 0 ? elo.ToString("0", CultureInfo.InvariantCulture) : "–", widths[3]);
					bool enabled = GUI.enabled;
					if (theirChallenge != null) {
						if (NavButton("Accept", Theme.Button, GUILayout.Width(110))) {
							AnswerChallenge(theirChallenge, true);
						}
					} else {
						GUI.enabled = enabled && canBattle;
						bool isPicked = pickedOpponent_ == rival.Player.SteamId;
						if (NavButton(isPicked ? "Picked" : "Challenge", isPicked ? Theme.Button : Theme.ButtonPrimary, GUILayout.Width(110))) {
							pickedOpponent_ = isPicked ? 0 : rival.Player.SteamId;
						}
						GUI.enabled = enabled;
					}
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
					DividerLine();
				}
			}
			if (consoles.Count > 0) {
				// Console players can't run the mod: a 1v1 with one is a two-car race this PC runs and times (bskftw, Nov 1).
				GUILayout.Label("Console players here (your mod times them; the start shows on your headlights)", Theme.SectionLabel);
				foreach (PlayerRow c in consoles) {
					if (NavButton($"<b>{c.Player.Name}</b>  <color={Theme.MutedHex}>{c.Player.Platform} · {c.Player.CarName} · {c.SetupText}</color>",
						pickedConsole_ == c.Player.Key ? Theme.RowSelected : Theme.Row)) {
						pickedConsole_ = pickedConsole_ == c.Player.Key ? "" : c.Player.Key;
						pickedOpponent_ = 0;
					}
				}
			}
			if (pickedOpponent_ != 0) {
				pickedConsole_ = "";
			}

			List<Course> courses = CoursesHere().Where(c => c.Usable).ToList();
			GUILayout.Label("Course", Theme.SectionLabel);
			if (courses.Count == 0) {
				GUILayout.Label("No courses on this map yet: make one with the Course builder.", Theme.Small);
			}
			pickedCourse_ = PickCourse(battlePicker_, courses, pickedCourse_,
				course => $"<b>{course.Name}</b>  <color={Theme.MutedHex}>{course.KindName} · {TimeAttackRun.Distance(course.Length)} · {course.SourceName}</color>");

			// The format is picked beside the page's title (Nav.DrawPageOptions); what it means is said here.
			GUILayout.Label($"Format: {BattleFormats[pickedFormat_]}", Theme.SectionLabel);
			GUILayout.Label(pickedFormat_ == 2
				? $"Tokyo Xtreme Racer style, anywhere on the map, no course: drive alongside each other at speed, a rolling 3-2-1, then whoever's behind loses SP (faster the bigger the gap; a hit costs {BattleSession.HitSp:0}). Empty SP, or {BattleSession.LostMetres:0} m behind, loses."
				: pickedFormat_ == 3
				? "For fun, like Most Wanted: side by side from a standing start, and the higher total of every speed trap on the course wins (every lap on a circuit). You have to finish for yours to count. A course without traps of its own uses the free-roam traps along its road. It doesn't change your Elo."
				: pickedFormat_ == 1
				? $"Runs swap who leads. The chaser wins a run by passing and staying ahead to the finish; the leader wins by finishing {BattleRules.LeaderGapSeconds:0.0} s or more ahead (1.0 s after both have led once, then 0.5 s). Otherwise swap and go again."
				: "Side by side from a standing start. The faster time to the finish wins.", Theme.Small);
			if (courses.FirstOrDefault(c => c.Code == pickedCourse_)?.IsCircuit == true) {
				GUILayout.Label("Laps", Theme.SectionLabel);
				pickedBattleLaps_ = Choice(BattleLapChoices.Select(l => l.ToString(CultureInfo.InvariantCulture)).ToArray(), pickedBattleLaps_);
			}
			DrawRulePicker(ref pickedMode_, ref pickedRule_, ref pickedFun_);

			PlayerRow opponent = rivals.FirstOrDefault(r => r.Player.SteamId == pickedOpponent_);
			PlayerRow console = consoles.FirstOrDefault(c => c.Player.Key == pickedConsole_);
			Course picked = courses.FirstOrDefault(c => c.Code == pickedCourse_);
			GUILayout.BeginHorizontal();
			bool ready = GUI.enabled;
			bool highway = pickedFormat_ == (int)BattleFormat.Highway;
			if (console != null) {
				GUI.enabled = ready && picked != null && !highway && pickedFormat_ == (int)BattleFormat.Race;
				if (NavButton($"Race {console.Player.Name}", Theme.ButtonPrimary)) {
					RaceConsolePlayer(console, picked);
				}
				GUI.enabled = ready;
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.Label("Against a console player it's a side-by-side race (they can't run the other formats without the mod). It opens as a two-car race in Races: line up and the host starts it.", Theme.Small);
				GUILayout.EndVertical();
				return;
			}
			GUI.enabled = ready && opponent != null && (picked != null || highway);
			if (NavButton(opponent == null ? "Challenge" : $"Challenge {opponent.Player.Name}", Theme.ButtonPrimary)) {
				ChallengePlayer(opponent, highway ? Course.FreeRoam(map_.Key) : picked);
			}
			GUI.enabled = ready;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}
	}
}
