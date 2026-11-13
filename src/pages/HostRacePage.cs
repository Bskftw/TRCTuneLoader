// Hosting a race, a room game or a tournament: the forms.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void DrawHostRaceForm(int section) {
			bool games = section == GamesSection;
			bool tournament = section == TournamentsSection;
			GUILayout.Label(games ? "Host a game" : tournament ? "Host a tournament" : "Host a race", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (!hasPose_ || map_ == null) {
				GUILayout.Label("Join a room and drive onto the map first.", Theme.Small);
				GUILayout.EndVertical();
				return;
			}
			if (games) {
				GUILayout.Label("What to play", Theme.SectionLabel);
				hostGame_ = Math.Max(1, hostGame_);
				hostGame_ = Choice(RoomModeNames.Skip(1).ToArray(), hostGame_ - 1, 4) + 1;
				DrawGameForm();
				GUILayout.EndVertical();
				return;
			}
			hostGame_ = 0;
			if (tournament) {
				GUILayout.Label("Tournament", Theme.SectionLabel);
				hostTournament_ = Choice(TournamentKinds.Skip(1).ToArray(), Math.Max(0, hostTournament_ - 1)) + 1;
			} else {
				hostTournament_ = 0;
			}
			GUILayout.Label("Race for", Theme.SectionLabel);
			hostTrapScoring_ = Choice(new[] { "Fastest time", "Speed traps" }, hostTrapScoring_ ? 1 : 0) == 1;
			GUILayout.Label(hostTrapScoring_
				? "Like Need for Speed Most Wanted: the highest total of every speed trap on the course wins (on a circuit, every lap's). You have to finish for yours to count. The course's own traps count, or the free-roam traps along it."
				: "The fastest to the finish wins.", Theme.Small);
			List<Course> courses = CoursesHere().Where(c => c.Usable).ToList();
			if (courses.Count == 0) {
				GUILayout.Label("No courses on this map yet: make one with the Course builder.", Theme.Small);
				GUILayout.EndVertical();
				return;
			}
			GUILayout.Label("Course", Theme.SectionLabel);
			// Only the picked course's road is measured (measuring all of them at once was a lag spike).
			MeasureRoad(courses.FirstOrDefault(c => c.Code == hostCourse_));
			hostCourse_ = PickCourse(racePicker_, courses, hostCourse_, course => {
				string grid = course.GridObjects.Count > 0 ? "map grid" : course.GridPath.Count >= 2 ? "laid-out grid" : "grid straight back";
				if (course.SingleFile) {
					grid += $" · <color={Theme.WarnHex}>single file: the road is {course.RoadWidth:0.0} m wide</color>";
				}
				int room = course.GridRoom(course.SingleFile || hostLayout_ == 2 ? GridLayout.LineUp : GridLayout.Grid);
				if (room < Course.FullGrid) {
					grid += $" · <color={Theme.WarnHex}>its grid path fits {room} car(s), more line up straight back</color>";
				}
				string traps = "";
				if (hostTrapScoring_) {
					int count = RaceTrapCount(course);
					traps = count == 0 ? $" · <color={Theme.WarnHex}>no speed traps on it</color>" : $" · {count} speed trap(s)";
				}
				return $"<b>{course.Name}</b>  <color={Theme.MutedHex}>{course.KindName} · {TimeAttackRun.Distance(course.Length)} · {grid}</color>{traps}";
			});
			Course picked = courses.FirstOrDefault(c => c.Code == hostCourse_);
			if (picked?.IsCircuit == true) {
				GUILayout.Label("Laps", Theme.SectionLabel);
				hostLaps_ = Choice(RaceLapChoices.Select(l => l.ToString(CultureInfo.InvariantCulture)).ToArray(), hostLaps_);
				GUILayout.Label("Elimination (the last car each lap is out)", Theme.SectionLabel);
				hostElimination_ = Choice(new[] { "Off", "On" }, hostElimination_ ? 1 : 0) == 1;
			}
			GUILayout.Label("Grid", Theme.SectionLabel);
			hostOrder_ = Choice(GridOrders, hostOrder_);
			hostPole_ = Choice(PoleSides, hostPole_);
			hostLayout_ = Choice(Layouts, hostLayout_);
			if (hostOrder_ == (int)GridOrder.Qualifying || hostTournament_ == 4) {
				GUILayout.Label("Qualifying: minutes and runs each", Theme.SectionLabel);
				hostQualMinutes_ = Choice(QualifyingMinuteChoices.Select(m => $"{m} min").ToArray(), hostQualMinutes_);
				hostQualRuns_ = Choice(QualifyingRunChoices, hostQualRuns_);
			}
			GUILayout.Label("Jump start", Theme.SectionLabel);
			hostJump_ = Choice(JumpRules, Math.Min(hostJump_, JumpRules.Length - 1));
			GUILayout.Label(JumpRuleValues[hostJump_] == JumpRule.Recall ? "Anyone moving before GO stops the start: everyone lines up again and the countdown starts over."
				: JumpRuleValues[hostJump_] == JumpRule.None ? "Jump starts aren't punished." : JumpRuleValues[hostJump_] == JumpRule.Disqualify ? "A jump start is out of the race."
				: "A jump start adds 5 s at the finish.", Theme.Small);
			DrawRulePicker(ref hostMode_, ref hostRule_, ref hostFun_);
			GUILayout.Label("Drivers (most)", Theme.SectionLabel);
			hostMax_ = Choice(MaxDriverChoices.Select(MaxDriversText).ToArray(), Math.Min(hostMax_, MaxDriverChoices.Length - 1));
			if (MaxDriverChoices[hostMax_] > RaceSettings.ManyDrivers) {
				GUILayout.Label("Over 16 (custom servers with bigger rooms): positions go out less often, so the live gaps are rougher.", Theme.Small);
			}
			GUILayout.Label("You", Theme.SectionLabel);
			hostDrives_ = Choice(new[] { "Drive in it", "Only run it (don't drive)" }, hostDrives_ ? 0 : 1) == 0;
			GUILayout.Label(hostDrives_ ? "You line up and race too." : "You run the race without driving in it: good for console players' races. Park facing the grid: your headlights show the start.", Theme.Small);
			GUILayout.Label("Headlight start", Theme.SectionLabel);
			hostLights_ = Choice(new[] { "When console players race", "Always" }, hostLights_ ? 1 : 0) == 1;
			GUILayout.Label("Your headlights count the start down for players without the mod: on, then off and on at 3, 2 and 1, and off at GO.", Theme.Small);

			if (tournament) {
				GUILayout.Label("Penalties", Theme.SectionLabel);
				hostNoPenalties_ = Choice(new[] { "Host can give penalties", "No penalties" }, hostNoPenalties_ ? 1 : 0) == 1;
				DrawTournamentOptions(courses, picked);
			}

			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && picked != null;
			if (NavButton(tournament ? "Open the tournament" : "Open the race", Theme.ButtonPrimary)) {
				HostRace(picked);
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		/// <summary>The room game's options: how long, and its own rules.</summary>
		private void DrawGameForm() {
			GUILayout.Label(RoomModeRules[hostGame_], Theme.Small);
			if (hostGame_ != (int)RoomMode.Convoy) {
				GUILayout.Label("Round length", Theme.SectionLabel);
				hostRoundMinutes_ = Choice(RoundMinuteChoices.Select(m => $"{m} min").ToArray(), hostRoundMinutes_);
			}
			if (hostGame_ == (int)RoomMode.CopsAndRunners) {
				GUILayout.Label("Cops at the start", Theme.SectionLabel);
				hostCops_ = Choice(CopChoices.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToArray(), hostCops_);
			}
			if (hostGame_ == (int)RoomMode.KingOfTheHill) {
				GUILayout.Label("Seconds on the hill to win", Theme.SectionLabel);
				hostHill_ = Choice(HillSecondChoices.Select(s => $"{s} s").ToArray(), hostHill_);
			}
			if (hostGame_ == (int)RoomMode.KeepUp) {
				GUILayout.Label("Gap allowed", Theme.SectionLabel);
				hostKeepUp_ = Choice(KeepUpChoices.Select(m => $"{m} m").ToArray(), hostKeepUp_);
			}
			GUILayout.Label("Drivers (most)", Theme.SectionLabel);
			hostMax_ = Choice(MaxDriverChoices.Select(MaxDriversText).ToArray(), Math.Min(hostMax_, MaxDriverChoices.Length - 1));
			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			if (NavButton($"Open {RoomModeNames[hostGame_].ToLowerInvariant()}", Theme.ButtonPrimary)) {
				HostRace(null);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void DrawTournamentOptions(List<Course> courses, Course picked) {
			switch (hostTournament_) {
				case 1:
					GUILayout.Label($"Races score {string.Join("-", TournamentSettings.DefaultPoints.Take(10))}... down to 16th, +1 for the fastest lap on circuits. Most points wins.", Theme.Small);
					GUILayout.Label("Rounds (pick a course above, then add it)", Theme.SectionLabel);
					for (int i = 0; i < hostRounds_.Count; i++) {
						TournamentRound r = hostRounds_[i];
						GUILayout.BeginHorizontal();
						GUILayout.Label($"{i + 1}. {r.CourseName}{(r.Laps > 1 ? $", {r.Laps} laps" : "")}{(r.ReverseGrid ? ", reverse grid" : "")}", Theme.Body);
						GUILayout.FlexibleSpace();
						if (NavButton("Remove", Theme.Button)) {
							hostRounds_.RemoveAt(i);
						}
						GUILayout.EndHorizontal();
					}
					GUILayout.BeginHorizontal();
					roundReverse_ = Choice(new[] { "Normal grid", "Reverse grid" }, roundReverse_ ? 1 : 0) == 1;
					bool enabled = GUI.enabled;
					GUI.enabled = enabled && picked != null && hostRounds_.Count < 12;
					if (NavButton("Add round", Theme.Button)) {
						hostRounds_.Add(new TournamentRound { CourseCode = picked.Code, CourseName = picked.Name, Laps = picked.IsCircuit ? RaceLapChoices[hostLaps_] : 1, ReverseGrid = roundReverse_ });
					}
					GUI.enabled = enabled;
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
					break;
				case 2:
					GUILayout.Label("1v1 battles in a bracket seeded by the grid order (qualifying is best). Top seeds get byes when the field isn't full.", Theme.Small);
					GUILayout.Label("Battles", Theme.SectionLabel);
					koFormat_ = Choice(KnockoutFormats, koFormat_);
					koBestOf_ = Choice(BestOfChoices.Select(b => $"Best of {b}").ToArray(), koBestOf_);
					GUILayout.Label("Final", Theme.SectionLabel);
					koFinalFormat_ = Choice(KnockoutFormats, koFinalFormat_);
					koFinalBestOf_ = Choice(BestOfChoices.Select(b => $"Best of {b}").ToArray(), koFinalBestOf_);
					koThird_ = Choice(new[] { "Race for 3rd", "No race for 3rd" }, koThird_ ? 0 : 1) == 0;
					break;
				case 3:
					GUILayout.Label("Heats race first; the top of each heat goes through to one final race.", Theme.Small);
					GUILayout.Label("Drivers per heat", Theme.SectionLabel);
					heatSize_ = Choice(HeatSizes.Select(h => h.ToString(CultureInfo.InvariantCulture)).ToArray(), heatSize_);
					GUILayout.Label("Through to the final from each heat", Theme.SectionLabel);
					heatAdvance_ = Choice(AdvanceChoices.Select(h => $"Top {h}").ToArray(), heatAdvance_);
					break;
				case 4:
					GUILayout.Label("Everyone drives time attacks on the course for the qualifying time; the best time wins. No traffic, so lag doesn't matter.", Theme.Small);
					break;
			}
		}
	}
}
