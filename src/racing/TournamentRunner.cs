// Running a tournament as its host: starting it, each next race or round, and its knockout battles.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>Host, every frame: counts finished races and knockout reports into the tournament, and keeps its lines up to date.</summary>
		private void RunTournament(double now) {
			if (tournament_ == null) {
				return;
			}
			bool changed = false;
			if (race_.Phase == RacePhase.Finished && race_.Results != null && race_.ResultsFor == race_.RaceNumber && tournamentRaceCounted_ != race_.RaceNumber) {
				tournamentRaceCounted_ = race_.RaceNumber;
				if (tournament_.Settings.Kind == TournamentKind.Points) {
					tournament_.AddRaceResult(race_.Results);
				} else if (tournament_.Settings.Kind == TournamentKind.Heats) {
					tournament_.AddHeatResult(race_.Results);
				}
				changed = true;
			}
			if (shootoutRunning_ && race_.Phase == RacePhase.Lobby) {
				shootoutRunning_ = false;
				tournament_.SetShootout(race_.Drivers.Select(d => (d.Id, d.Name, d.BestQualifying)));
				changed = true;
			}
			foreach (RaceMessage report in race_.Reports.ToList()) {
				race_.Reports.Remove(report);
				string key = $"{report.MatchId}:{report.MatchGame}";
				if (report.MatchId != race_.MatchId || report.MatchGame != race_.MatchGame || !matchReportsSeen_.Add(key)) {
					continue;
				}
				tournament_.Report(report.MatchId, report.Winner);
				KnockoutMatch m = tournament_.Matches.FirstOrDefault(x => x.Id == report.MatchId);
				if (m != null && m.Winner == 0) {
					// Not decided yet: the next game of the match.
					race_.CallMatch(m.Id, m.A, m.B, m.Format, race_.MatchGame + 1, tournament_.Settings.Laps);
				} else {
					race_.CallMatch(0, 0, 0, BattleFormat.Race, 0, 1);
				}
				changed = true;
			}
			if (changed) {
				race_.SetLines(tournament_.Lines());
				foreach (string line in tournament_.Lines().Take(4)) {
					TrcLog.Info($"Tournament: {line}");
				}
			}
		}

		/// <summary>Host: starts the tournament with everyone in the lobby, seeded by the race's grid order (qualifying, best time...).</summary>
		private void StartTournament() {
			var settings = new TournamentSettings {
				Kind = (TournamentKind)(hostTournament_ - 1),
				Name = race_.Settings.Name,
				Rounds = hostRounds_.Select(r => new TournamentRound { CourseCode = r.CourseCode, CourseName = r.CourseName, Laps = r.Laps, ReverseGrid = r.ReverseGrid }).ToList(),
				Format = (BattleFormat)koFormat_,
				BestOf = BestOfChoices[koBestOf_],
				FinalFormat = (BattleFormat)koFinalFormat_,
				FinalBestOf = BestOfChoices[koFinalBestOf_],
				ThirdPlace = koThird_,
				Laps = race_.Settings.Laps,
				HeatSize = HeatSizes[heatSize_],
				Advance = AdvanceChoices[heatAdvance_],
			};
			if (settings.Kind == TournamentKind.Points && settings.Rounds.Count == 0) {
				settings.Rounds.Add(new TournamentRound { CourseCode = race_.CourseCode, CourseName = race_.Course?.Name ?? "", Laps = race_.Settings.Laps });
			}
			List<RaceDriver> seeded = race_.Settings.Order == GridOrder.Qualifying
				? race_.Drivers.OrderBy(d => d.BestQualifying > 0 ? d.BestQualifying : double.MaxValue).ToList()
				: race_.Settings.Order == GridOrder.BestTime
				? race_.Drivers.OrderBy(d => d.PersonalBest > 0 ? d.PersonalBest : double.MaxValue).ToList()
				: race_.Settings.Order == GridOrder.Random ? race_.Drivers.OrderBy(_ => raceIds_.Next()).ToList()
				: race_.Drivers.ToList();
			if (settings.Kind == TournamentKind.Knockout || settings.Kind == TournamentKind.Shootout) {
				// 1v1 battles and time attacks need the mod: console players can't take part in these.
				seeded = seeded.Where(d => !d.Console).ToList();
			}
			if (!race_.Settings.HostDrives) {
				seeded = seeded.Where(d => d.Id != MySteamId).ToList();
			}
			tournament_ = new Tournament(settings, seeded.Select(d => (d.Id, d.Name)).ToList());
			tournamentRaceCounted_ = race_.RaceNumber;
			race_.SetLines(tournament_.Lines());
			TrcLog.Info($"Tournament started: {TournamentKinds[hostTournament_]}, {seeded.Count} drivers.");
		}

		/// <summary>Host: the tournament's next step (a points round, a heat or the final, the next battle, the shootout).</summary>
		private void NextTournamentStep() {
			double now = BattleNow;
			switch (tournament_.Settings.Kind) {
				case TournamentKind.Points: {
					TournamentRound round = tournament_.CurrentRound;
					Course course = round != null ? courses_.ByCode(round.CourseCode) ?? courses_.ForMap(map_?.Key ?? "").FirstOrDefault(c => c.Code == round.CourseCode) : null;
					if (course == null) {
						raceResult_ = "This round's course isn't on this map.";
						return;
					}
					race_.SetCourse(course);
					RaceSettings s = race_.Settings.Copy();
					s.Laps = course.IsCircuit ? round.Laps : 1;
					race_.SetSettings(s);
					race_.StartRace(now, null, tournament_.NextGrid());
					break;
				}
				case TournamentKind.Heats:
					race_.StartRace(now, tournament_.NextHeatRacers(), tournament_.NextHeatRacers());
					break;
				case TournamentKind.Knockout: {
					KnockoutMatch m = tournament_.NextMatch();
					if (m == null) {
						raceResult_ = "No battle to call right now.";
						return;
					}
					race_.CallMatch(m.Id, m.A, m.B, m.Format, m.Games + 1, tournament_.Settings.Laps);
					break;
				}
				case TournamentKind.Shootout:
					shootoutRunning_ = true;
					race_.StartQualifying(now);
					break;
			}
			race_.SetLines(tournament_.Lines());
		}

		// ---- Knockout battles ----

		/// <summary>
		/// A knockout battle the host called with this player in it: the higher seed's mod challenges by itself (the
		/// other one accepts by itself, in RouteChallenge), and when it's over the winner goes back to the host.
		/// </summary>
		private void CallMatchBattle() {
			if (race_.MatchId == 0 || race_.Course == null) {
				return;
			}
			string key = $"{race_.Id}:{race_.MatchId}:{race_.MatchGame}";
			if (race_.MatchA == MySteamId && matchChallenged_ != key && (battle_ == null || !battle_.Active) && hasPose_) {
				matchChallenged_ = key;
				RaceDriver other = race_.Drivers.FirstOrDefault(d => d.Id == race_.MatchB);
				StartBattle(race_.MatchB, other?.Name ?? "", race_.Course, race_.MatchFormat, race_.Settings.Open && race_.Settings.Regulation == null, race_.MatchLaps, race_.MatchId, race_.MatchGame,
					race_.Settings.Regulation, race_.Settings.Fun);
			}
		}

		/// <summary>A tournament battle ended: who won goes to the host (both drivers send it; the host counts it once).</summary>
		private void ReportMatchBattle(BattleSession b) {
			if (race_ == null || b.MatchId == 0) {
				return;
			}
			string key = $"{b.MatchId}:{b.MatchGame}";
			if (matchReported_ == key) {
				return;
			}
			matchReported_ = key;
			ulong winner = b.Winner != 0 ? b.Winner : b.PeerLeft ? b.Me : b.ILeft ? b.Peer : 0;
			var report = new RaceMessage { Kind = RaceKind.MatchReport, RaceId = race_.Id, From = MySteamId, RaceNumber = race_.RaceNumber, MatchId = b.MatchId, MatchGame = b.MatchGame, Winner = winner };
			if (race_.IsHost) {
				race_.Reports.Add(report);
			} else {
				raceSync_.Send(report);
			}
			TrcLog.Info($"Race {race_.Id}: tournament battle {b.MatchId} game {b.MatchGame}, {(winner == 0 ? "no winner (run again)" : winner == b.Me ? "you won" : $"{b.PeerName} won")}.");
		}

		/// <summary>True when this challenge is the knockout battle the host called, from the right driver: it's accepted by itself.</summary>
		private bool IsCalledMatch(BattleMessage challenge) =>
			race_ != null && challenge.MatchId != 0 && challenge.MatchId == race_.MatchId && challenge.MatchGame == race_.MatchGame &&
			challenge.From == race_.MatchA && race_.MatchB == MySteamId;
	}
}
