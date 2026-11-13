// Ratings in game: loading and saving the sealed ratings, and rating each run, battle and race.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void SetupRatings() {
			try {
				RatingSeal.LoadEmbeddedKey();
				if (!RatingSeal.HasPrivateKey) {
					TrcLog.Warning("This build has no private ratings key (ratings.key), so ratings files use the development key.");
				}
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			LoadRatings();
		}

		/// <summary>
		/// The ratings file is tied to the SteamID, so it's only read once KSL knows it: a file read with none would
		/// look like another player's and start again. Until then <see cref="ratings_"/> stays null (nothing is rated).
		/// </summary>
		private void LoadRatings() {
			if (ratings_ != null || MySteamId == 0) {
				return;
			}
			try {
				ratings_ = RatingBook.Load(TimesFolder, MySteamId);
				if (ratings_.WasChanged) {
					TrcLog.Warning($"The ratings file was changed outside the mod (or was another player's), so the ratings started again. The changed file is kept as {RatingBook.ChangedFileFor(MySteamId)}.");
				}
			} catch (Exception e) {
				ratings_ = new RatingBook();
				TrcLog.Error(e);
			}
		}

		private void SaveRatings() {
			if (ratings_ == null || MySteamId == 0) {
				return;
			}
			try {
				ratings_.Save(TimesFolder, MySteamId);
				sync_?.SendReportNow();
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private bool BattleRunning => battle_ != null && battle_.State == BattleSession.Phase.Running;

		/// <summary>
		/// A time attack ended. It doesn't move the Safety Rating any more (0.15.0): it's the player alone on the road,
		/// often just messing about, so resets and jumping off the road there shouldn't count.
		/// </summary>
		private void RateRun(TimeAttackRun run) {
			runPoints_ = 0;
			runKm_ = 0;
		}

		/// <summary>
		/// A battle ended: Elo when it counted (TRC spec, or a regulation's own Elo), and the Safety Rating when it was
		/// really driven. A "just for fun" battle changes nothing.
		/// </summary>
		private void RateBattle(BattleSession b) {
			if (ratings_ == null || !b.Started || ratedBattle_ == b.Id) {
				return;
			}
			ratedBattle_ = b.Id;
			if (b.Fun) {
				battlePoints_ = 0;
				battleKm_ = 0;
				return;
			}
			CourseKind kind = b.IsHighway ? CourseKind.Highway : b.Course.Kind;
			string rule = b.Regulation?.Code ?? "";
			bool driven = (b.IsTrcSpec || b.Regulation != null) && b.State == BattleSession.Phase.Finished && b.Runs.Count > 0;
			if (b.Counted && driven && !b.IsSpeedTraps) {
				// Speed trap battles are for fun: no Elo.
				if (b.Regulation != null) {
					ratings_.RuleNames[rule] = b.Regulation.Name;
				}
				double opponent = b.PeerElo > 0 ? b.PeerElo
					: rule.Length == 0 && sync_?.ReportFrom(b.Peer) is TrcSync.Report r && r.HasRatings ? r.EloFor(kind) : RatingBook.StartElo;
				double score = b.Winner == b.Me ? 1 : b.Winner == 0 ? 0.5 : 0;
				ratings_.Battle(kind, opponent, score, b.PeerName, DateTime.Now, rule);
			}
			if (driven) {
				int points = battlePoints_ + (b.Run?.Resets ?? 0) * RatingBook.ResetPoints;
				ratings_.Drive(battleKm_, points, $"Battle vs {b.PeerName}", DateTime.Now);
			}
			SaveRatings();
			battlePoints_ = 0;
			battleKm_ = 0;
		}

		/// <summary>A race finished: Elo against every other driver when it was TRC spec, and the Safety Rating.</summary>
		private void RateRace() {
			if (ratings_ == null || race_.Results == null || ratedRace_ == race_.ResultsFor || race_.IsGame || race_.Course == null) {
				return;
			}
			ratedRace_ = race_.ResultsFor;
			RaceResultRow mine = race_.Results.FirstOrDefault(r => r.Id == MySteamId);
			if (mine == null) {
				return;
			}
			visit_?.Race(mine.State == RacerState.Finished ? mine.Position : 0, race_.Results.Count);
			CourseKind kind = race_.Course.Kind;
			RaceSettings settings = race_.Settings;
			bool counts = !settings.Fun && (settings.IsTrcSpec || settings.Regulation != null) && race_.MyDriver?.Racer == true;
			string rule = settings.Regulation?.Code ?? "";
			if (counts && race_.Results.Count >= 2) {
				if (settings.Regulation != null) {
					ratings_.RuleNames[rule] = settings.Regulation.Name;
				}
				double EloOf(RaceResultRow r) {
					RaceDriver d = race_.Drivers.FirstOrDefault(x => x.Id == r.Id);
					if (d != null && d.Elo > 0) {
						return d.Elo;
					}
					return rule.Length == 0 && sync_?.ReportFrom(r.Id) is TrcSync.Report rep && rep.HasRatings ? rep.EloFor(kind) : RatingBook.StartElo;
				}
				var others = race_.Results.Where(r => r.Id != MySteamId).Select(r => (EloOf(r), r.Position)).ToList();
				ratings_.Race(kind, mine.Position, others, DateTime.Now, rule);
			}
			if (counts) {
				int points = racePoints_ + (race_.Run?.Resets ?? 0) * RatingBook.ResetPoints;
				ratings_.Drive(raceKm_, points, $"Race on {race_.Course.Name}", DateTime.Now);
			}
			SaveRatings();
			racePoints_ = 0;
			raceKm_ = 0;
		}
	}
}
