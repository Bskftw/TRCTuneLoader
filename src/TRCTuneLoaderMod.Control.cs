using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Race control and ratings in game: incidents this car has (hits and contact) in runs, battles and
	/// races; highway cut-up points; the kilometres driven; and the player's Elo, Safety Rating and license
	/// (kept on this PC, see RatingBook) moved after every run, battle and race.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private readonly ContactWatcher contact_ = new ContactWatcher();
		private readonly CutUpScorer cutUp_ = new CutUpScorer();
		private RatingBook ratings_;
		private int cutUpFor_ = -1;
		private string cutUpFlash_ = "";
		private float cutUpFlashUntil_;

		/// <summary>Incident points and kilometres in the time attack, battle and race going on, for the Safety Rating.</summary>
		private int runPoints_;
		private int battlePoints_;
		private int racePoints_;
		private double runKm_;
		private double battleKm_;
		private double raceKm_;
		private Vector3 lastOdometer_;
		private bool hasOdometer_;
		private int ratedRace_ = -1;
		private uint ratedBattle_;

		private void SetupRatings() {
			try {
				ratings_ = RatingBook.Load(TimesFolder);
			} catch (Exception e) {
				ratings_ = new RatingBook();
				TrcLog.Error(e);
			}
		}

		private void SaveRatings() {
			try {
				ratings_.Save(TimesFolder);
				sync_?.SendReportNow();
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private bool BattleRunning => battle_ != null && battle_.State == BattleSession.Phase.Running;
		private bool RaceRunning => race_ != null && !race_.Ended && race_.Phase == RacePhase.Running && race_.MyDriver?.Racer == true;
		private bool RunRunning => run_ != null && run_.State == TimeAttackRun.Phase.Running;

		/// <summary>The other cars this mod knows the place of: the race's drivers, or the battle's other driver.</summary>
		private List<NearbyCar> NearbyCars() {
			var cars = new List<NearbyCar>();
			if (race_ != null && !race_.Ended) {
				cars.AddRange(race_.Drivers.Where(d => d.Id != MySteamId && d.HasPosition)
					.Select(d => new NearbyCar { Id = d.Id, Name = d.Name, Position = d.Position, Heading = d.Heading, Kmh = d.Kmh }));
			}
			if (battle_ != null && battle_.Active && battle_.Started) {
				cars.Add(new NearbyCar { Id = battle_.Peer, Name = battle_.PeerName, Position = battle_.PeerPosition, Heading = battle_.PeerHeading, Kmh = battle_.PeerKmh });
			}
			return cars;
		}

		/// <summary>Called from Update every frame, after races and battles.</summary>
		private void UpdateControl() {
			if (!hasPose_) {
				contact_.Reset();
				hasOdometer_ = false;
				return;
			}
			// Kilometres driven in whatever's going on (jumps from a teleport or a reset don't count).
			if (hasOdometer_) {
				float moved = Vector3.Distance(lastOdometer_, pose_.Position);
				if (moved < 30f) {
					double km = moved / 1000.0;
					if (RunRunning) {
						runKm_ += km;
					}
					if (BattleRunning) {
						battleKm_ += km;
					}
					if (RaceRunning) {
						raceKm_ += km;
					}
				}
			}
			lastOdometer_ = pose_.Position;
			hasOdometer_ = true;

			List<NearbyCar> nearby = NearbyCars();
			bool watching = RunRunning || BattleRunning || RaceRunning;
			string incident = watching ? contact_.Tick(BattleNow, pose_.Position, pose_.SpeedKmh, nearby) : null;
			if (!watching) {
				contact_.Reset();
			}
			if (incident != null && incident.StartsWith("light contact", StringComparison.Ordinal)) {
				// Close racing: noted, nothing more.
				TrcLog.Info($"Incident: {incident} (no points).");
				incident = null;
			}
			if (incident != null) {
				int points = incident.StartsWith("contact", StringComparison.Ordinal) ? RatingBook.ContactPoints : RatingBook.HitPoints;
				if (RaceRunning && race_.MyDriver.State == RacerState.Racing) {
					race_.AddIncident(incident);
					racePoints_ += points;
				}
				if (BattleRunning) {
					battle_.Hit();
					battlePoints_ += points;
				}
				if (RunRunning) {
					runPoints_ += points;
				}
				cutUp_.Break();
				TrcLog.Info($"Incident: {incident}.");
			}

			// Highway cut-up: this player's own passes, counted here and told to the host.
			if (race_ != null && !race_.Ended && race_.Settings.Mode == RoomMode.CutUp && race_.Phase == RacePhase.Running) {
				if (cutUpFor_ != race_.RaceNumber) {
					cutUpFor_ = race_.RaceNumber;
					cutUp_.Reset();
				}
				float scored = cutUp_.Tick(BattleNow, pose_.Position, pose_.Forward, pose_.SpeedKmh, nearby);
				if (scored > 0) {
					cutUpFlash_ = $"<b>+{scored:0}</b>  passed {cutUp_.LastPassed}{(cutUp_.Combo > 2 ? $"  <color={Theme.PurpleHex}>×{cutUp_.Combo - 1} combo</color>" : "")}";
					cutUpFlashUntil_ = Time.unscaledTime + 2.5f;
				}
				race_.SetScore(cutUp_.Points);
			}
		}

		private bool CutUpFlashVisible => Time.unscaledTime < cutUpFlashUntil_;

		// ---- Ratings ----

		/// <summary>
		/// A time attack ended. It doesn't move the Safety Rating any more (0.15.0): it's the player alone on the road,
		/// often just messing about, so resets and jumping off the road there shouldn't count.
		/// </summary>
		private void RateRun(TimeAttackRun run) {
			runPoints_ = 0;
			runKm_ = 0;
		}

		/// <summary>A battle ended: Elo when it counted (TRC spec), and the Safety Rating when it was a TRC spec battle that was really driven.</summary>
		private void RateBattle(BattleSession b) {
			if (ratings_ == null || !b.Started || ratedBattle_ == b.Id) {
				return;
			}
			ratedBattle_ = b.Id;
			CourseKind kind = b.IsHighway ? CourseKind.Highway : b.Course.Kind;
			bool driven = !b.Open && b.State == BattleSession.Phase.Finished && b.Runs.Count > 0;
			if (b.Counted && driven && !b.IsSpeedTraps) {
				// Speed trap battles are for fun: no Elo.
				double opponent = sync_?.ReportFrom(b.Peer) is TrcSync.Report r && r.HasRatings ? r.EloFor(kind) : RatingBook.StartElo;
				double score = b.Winner == b.Me ? 1 : b.Winner == 0 ? 0.5 : 0;
				ratings_.Battle(kind, opponent, score, b.PeerName, DateTime.Now);
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
			CourseKind kind = race_.Course.Kind;
			if (!race_.Settings.Open && race_.Results.Count >= 2) {
				var others = race_.Results.Where(r => r.Id != MySteamId)
					.Select(r => (sync_?.ReportFrom(r.Id) is TrcSync.Report rep && rep.HasRatings ? (double)rep.EloFor(kind) : RatingBook.StartElo, r.Position)).ToList();
				ratings_.Race(kind, mine.Position, others, DateTime.Now);
			}
			if (!race_.Settings.Open) {
				int points = racePoints_ + (race_.Run?.Resets ?? 0) * RatingBook.ResetPoints;
				ratings_.Drive(raceKm_, points, $"Race on {race_.Course.Name}", DateTime.Now);
			}
			SaveRatings();
			racePoints_ = 0;
			raceKm_ = 0;
		}

		// ---- My times: Ratings ----

		private void DrawRatings() {
			RatingBook r = ratings_ ?? new RatingBook();
			GUILayout.Label(RatingBook.TestingNote, Theme.PillWarn);
			GUILayout.Label("Kept on this PC until the TRC server is up: then they'll be checked and shared. Other TRC Tune Loader players see your license and Safety Rating.", Theme.Small);
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"<b>{RatingBook.LicenseName(r.License)} license</b> · Safety Rating {r.Safety:0.00} · {r.Events} event(s)", Theme.Heading);
			string next = r.License == License.A ? "Top license." : $"Next license: Safety Rating {RatingBook.PromoteSafety:0.00} or more and enough events driven.";
			GUILayout.Label($"{next} Below {RatingBook.DemoteSafety:0.00} you go down a license.", Theme.Small);
			GUILayout.EndVertical();
			GUILayout.BeginVertical(Theme.Card);
			for (int i = 0; i < 3; i++) {
				GUILayout.Label($"<b>{Course.KindNames[i]}</b>  Elo {r.Elo[i]:0}", Theme.Body);
			}
			GUILayout.Label("Elo moves only in TRC spec battles and races. The Safety Rating moves only in TRC spec battles and races too: up with clean kilometres, " +
				$"down with hard contact, hard hits and resets (at most {RatingBook.MaxDropPerEvent:0.00} an event). Light touches in close racing, time attack, " +
				"leaving the road and jump starts don't count. No blame: like iRacing, both cars in a contact get it.", Theme.Small);
			GUILayout.EndVertical();
			GUILayout.Label("Recent", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (r.History.Count == 0) {
				GUILayout.Label("Nothing yet: drive a time attack, a battle or a race.", Theme.Small);
			}
			foreach (string line in Enumerable.Reverse(r.History).Take(30)) {
				GUILayout.Label(line, Theme.Small);
			}
			GUILayout.EndVertical();
		}

		/// <summary>"B 3.12 · 1530": another player's license, Safety Rating and Elo for a kind of course, from their report ("" without).</summary>
		private string RatingOf(ulong id, CourseKind kind) {
			TrcSync.Report r = id == MySteamId ? null : sync_?.ReportFrom(id);
			if (id == MySteamId && ratings_ != null) {
				return $"{ratings_.Badge} · {ratings_.Elo[(int)kind]:0}";
			}
			return r != null && r.HasRatings ? $"{r.Badge} · {r.EloFor(kind):0}" : "";
		}
	}
}
