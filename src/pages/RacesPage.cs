// The Battles tab's sections, and the open races and games to join.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>The Battles tab: 1v1 battles, races and tournaments, fun games (room games), and the player's ratings.</summary>
		private void DrawBattlesTab() {
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			int openRaces = openRaces_.Values.Count(v => v.Lobby.Settings.Mode == RoomMode.Race);
			int openGames = openRaces_.Count - openRaces;
			int openTournaments = openRaces_.Values.Count(v => SectionFor(v.Lobby) == TournamentsSection);
			openRaces -= openTournaments;
			int inSection = race_ != null ? SectionFor(race_) : -1;
			int view = SideNav(BattleSections, battleViewMode_, new[] {
				invites_.Count > 0 ? $"{invites_.Count} waiting" : battle_ != null && battle_.Active ? "on" : "",
				inSection == RacesSection ? "in one" : openRaces > 0 ? $"{openRaces} open" : "",
				inSection == TournamentsSection ? "in one" : openTournaments > 0 ? $"{openTournaments} open" : "",
				inSection == GamesSection ? "in one" : openGames > 0 ? $"{openGames} open" : "",
				"testing",
			});
			if (view != battleViewMode_) {
				battleViewMode_ = view;
				raceResult_ = "";
			}
			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			switch (battleViewMode_) {
				case 0: DrawBattleView(); break;
				case RacesSection:
				case TournamentsSection:
				case GamesSection:
					DrawRacesView(battleViewMode_);
					break;
				default:
					ratingsScroll_ = GUILayout.BeginScrollView(ratingsScroll_);
					DrawRatings();
					GUILayout.EndScrollView();
					break;
			}
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		/// <summary>Races, tournaments, or the fun games (cops and runners, tag and the rest): what's open, the lobby, and hosting one.</summary>
		private void DrawRacesView(int section) {
			if (!raceSync_.IsAvailable) {
				GUILayout.Label(raceSync_.Status, Theme.PillNeutral);
				return;
			}
			raceScroll_ = GUILayout.BeginScrollView(raceScroll_);
			if (!string.IsNullOrEmpty(raceResult_)) {
				GUILayout.Label(raceResult_, Theme.Body);
			}
			if (race_ != null && SectionFor(race_) == section) {
				DrawRaceLobby();
			} else if (race_ != null) {
				int theirs = SectionFor(race_);
				GUILayout.BeginVertical(Theme.Guide);
				GUILayout.Label(theirs == GamesSection ? "You're in a fun game." : theirs == TournamentsSection ? "You're in a tournament." : "You're in a race.", Theme.Body);
				if (NavButton($"Go to {BattleSections[theirs]}", Theme.Button)) {
					battleViewMode_ = theirs;
				}
				GUILayout.EndVertical();
			} else {
				DrawOpenRaces(section);
				DrawHostRaceForm(section);
			}
			GUILayout.EndScrollView();
		}

		private void DrawOpenRaces(int section) {
			bool games = section == GamesSection;
			string what = games ? "Games" : section == TournamentsSection ? "Tournaments" : "Open races";
			GUILayout.Label($"{what} in this room", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			List<(RaceMessage Lobby, float SeenAt, float FirstSeen)> listed = openRaces_.Values.Where(v => SectionFor(v.Lobby) == section).ToList();
			if (listed.Count == 0) {
				GUILayout.Label($"No {what.ToLowerInvariant()} right now. Host one below: everyone in the room with TRC Tune Loader sees it.", Theme.Small);
			}
			foreach (var (lobby, _, _) in listed) {
				Course course = courses_.ByCode(lobby.CourseCode);
				string where = lobby.Settings.Mode != RoomMode.Race ? RoomModeNames[(int)lobby.Settings.Mode] : course?.Name ?? lobby.CourseCode;
				bool full = lobby.Entries.Count >= lobby.Settings.MaxPlayers;
				bool running = lobby.Phase != RacePhase.Lobby && lobby.Phase != RacePhase.Qualifying && lobby.Phase != RacePhase.Finished;
				GUILayout.BeginHorizontal();
				int consoles = lobby.Entries.Count(e => e.Console);
				GUILayout.Label($"<b>{BattleMessage.PlainName(lobby.HostName)}</b>: {where}  <color={Theme.MutedHex}>{lobby.Entries.Count}/{MaxDriversText(lobby.Settings.MaxPlayers)} · " +
					$"{(lobby.Settings.Laps > 1 ? $"{lobby.Settings.Laps} laps · " : "")}{RuleText(lobby.Settings.Open, lobby.Settings.Regulation, lobby.Settings.Fun)}" +
					$"{(consoles > 0 ? $" · {consoles} on console" : "")}{(running ? " · racing now" : "")}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && !full && hasPose_;
				if (NavButton(full ? "Full" : running ? "Join (next race)" : "Join", Theme.ButtonPrimary, GUILayout.Width(130))) {
					JoinRace(lobby);
				}
				GUI.enabled = enabled;
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}
	}
}
