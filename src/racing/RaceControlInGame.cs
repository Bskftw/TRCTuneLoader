// Race control in game: the cars nearby, incidents (hits and contact), highway cut-up points and the kilometres driven.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Race control and ratings in game: incidents this car has (hits and contact) in runs, battles and
	/// races; highway cut-up points; the kilometres driven; and the player's Elo, Safety Rating and license
	/// (kept on this PC, see RatingBook) moved after every run, battle and race. The ratings part is
	/// RatingsInGame.cs; the My times page for them is src/pages/RatingsPage.cs.
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
					visit_?.Drove(km, Mathf.Abs(pose_.SpeedKmh));
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
				visit_?.CutUp(cutUp_.Points);
			}
		}

		private bool CutUpFlashVisible => Time.unscaledTime < cutUpFlashUntil_;
	}
}
