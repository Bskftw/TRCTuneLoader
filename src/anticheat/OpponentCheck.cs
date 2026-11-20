// Checking the other drivers in battles and races ourselves (what CarX shares of their car) against what their mod claims, and the result code.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// docs/archive/IMPROVEMENT_PLAN.md A1: their mod says "TRC spec" (or "meets the regulation"); this PC checks their car itself, from
	/// what CarX shares (the same check as the Players page), at the countdown and at the finish. A cheater's mod can lie
	/// about itself, but it can't answer for this check, because nothing asks it. bskftw, Nov 12: a disagreement marks the
	/// result unverified with no rating change, as a warning only for the first week (<see cref="DoubtStopsRatings"/>).
	/// </summary>
	public partial class TRCTuneLoaderMod {
		/// <summary>
		/// Stage 3.3: false shows the warning only; true also keeps a disagreeing battle or race from moving Elo. Turn on once
		/// a week of testing shows no false alarms (CarX's shared data can lag a car change).
		/// </summary>
		private static readonly bool DoubtStopsRatings = false;

		/// <summary>The battle checked (by id), and what the check found: null not checked, "" agrees, else what disagrees.</summary>
		private uint battleCheckFor_;
		private string battleCheck_;
		private bool battleStartChecked_;

		/// <summary>The race checked ("id:race number"), whether the countdown was checked, and the drivers whose car disagrees.</summary>
		private string raceCheckFor_ = "";
		private bool raceStartChecked_;
		private readonly Dictionary<ulong, string> raceDoubts_ = new Dictionary<ulong, string>();

		/// <summary>The last finished battle's or race's code for the season bot ("" none yet).</summary>
		private string lastResultCode_ = "";
		private string lastResultCodeWhat_ = "";

		/// <summary>
		/// Their car as CarX shares it, against the event's rules: null when it can't be told (Open, not in the room, setup
		/// unreadable, a Workshop body this PC doesn't have), "" when it agrees with their claim, else what's wrong.
		/// </summary>
		private string CheckDriver(ulong steamId, bool trcSpec, Regulation rule, bool claims) {
			if (!claims || (!trcSpec && rule == null) || !adapter_.TryGetRoomPlayers(out var players, out _)) {
				return null;
			}
			RoomPlayer player = players.FirstOrDefault(p => p.SteamId == steamId);
			if (player == null) {
				return null;
			}
			if (rule != null) {
				RegulationResult result = rule.Check(FactsFor(player));
				return result.Broken.Count > 0 ? $"breaks {rule.Name}: {string.Join(", ", result.Broken)}" : result.Unknown.Count > 0 ? null : "";
			}
			PlayerRow row = CheckPlayer(player);
			if (row.Setup == null || UnknownWorkshop(player)) {
				return null;
			}
			return row.Setup.Matches && player.EngineId <= 0 && !CarNotSpec(player) ? "" : row.SetupText;
		}

		/// <summary>Every frame of a battle: the opponent is checked once at the countdown, and once more at the end (<paramref name="atEnd"/>).</summary>
		private void CheckBattleOpponent(bool atEnd) {
			BattleSession b = battle_;
			if (b.Id != battleCheckFor_) {
				battleCheckFor_ = b.Id;
				battleCheck_ = null;
				battleStartChecked_ = false;
			}
			if (!atEnd && (battleStartChecked_ || b.State != BattleSession.Phase.Countdown)) {
				return;
			}
			battleStartChecked_ = true;
			if (atEnd && !b.Started) {
				return;
			}
			bool claims = b.PeerCar != null && (b.Regulation != null ? b.PeerCar.RuleOk : b.PeerCar.Spec);
			string found = CheckDriver(b.Peer, b.IsTrcSpec, b.Regulation, claims);
			if (found == null || battleCheck_ != null && battleCheck_.Length > 0) {
				return;
			}
			battleCheck_ = found;
			if (found.Length > 0) {
				WarnDoubt(b.PeerName, found);
			}
		}

		/// <summary>Every frame of a race: each other racer is checked at the countdown, and once more when the results are in.</summary>
		private void CheckRaceDrivers(bool atEnd) {
			string key = $"{race_.Id}:{race_.RaceNumber}";
			if (key != raceCheckFor_) {
				raceCheckFor_ = key;
				raceStartChecked_ = false;
				raceDoubts_.Clear();
			}
			if (!atEnd && (raceStartChecked_ || race_.Phase != RacePhase.Countdown)) {
				return;
			}
			raceStartChecked_ = true;
			RaceSettings settings = race_.Settings;
			foreach (RaceDriver d in race_.Drivers) {
				if (!d.Racer || d.Id == MySteamId || raceDoubts_.ContainsKey(d.Id)) {
					continue;
				}
				string found = CheckDriver(d.Id, settings.IsTrcSpec, settings.Regulation, settings.Regulation != null ? d.RuleOk : d.Spec);
				if (!string.IsNullOrEmpty(found)) {
					raceDoubts_[d.Id] = found;
					WarnDoubt(d.Name, found);
				}
			}
		}

		private void WarnDoubt(string who, string found) {
			string name = BattleMessage.PlainName(who);
			TrcLog.Warning($"Opponent check: {name}'s mod says their car is fine, but their car as CarX shares it disagrees: {found}." +
				(DoubtStopsRatings ? " The result is unverified: no rating change." : " Only a warning for now."));
			ShowNotice($"<b>{name}'s car doesn't look right</b>\n<color={Theme.MutedHex}>{found}{(DoubtStopsRatings ? " · this result won't change ratings" : "")}</color>", 8f);
		}

		/// <summary>What the battle's opponent check found, for the server events and the result code.</summary>
		private string BattleCheckText => battleCheck_ == null ? "unchecked" : battleCheck_.Length == 0 ? "agrees" : "differs: " + battleCheck_;

		private bool BattleDoubted(BattleSession b) => b.Id == battleCheckFor_ && !string.IsNullOrEmpty(battleCheck_);

		private bool RaceDoubted => raceDoubts_.Count > 0;

		/// <summary>The season bot's code for an event line just written (nothing when the line couldn't be written).</summary>
		private void KeepResultCode(string line, string what) {
			if (string.IsNullOrEmpty(line)) {
				return;
			}
			lastResultCode_ = ResultCode.Make(line);
			lastResultCodeWhat_ = what;
			TrcLog.Info($"Result code for the season bot ({what}{(ResultCode.HasPrivateKey ? "" : ", development key: the bot won't accept it")}): {lastResultCode_}");
		}

		/// <summary>The last result's code, with a button to copy it (Battles, and the race lobby's results).</summary>
		private void DrawResultCode() {
			if (lastResultCode_.Length == 0) {
				return;
			}
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<b>Result code</b> for the season bot's /report-race: {lastResultCodeWhat_}", Theme.Small);
			GUILayout.FlexibleSpace();
			if (NavButton("Copy result code", Theme.Button)) {
				GUIUtility.systemCopyBuffer = lastResultCode_;
				ShowNotice("<b>Result code copied</b>\nPaste it into /report-race with your clip or screenshot.", 3f);
			}
			GUILayout.EndHorizontal();
		}
	}
}
