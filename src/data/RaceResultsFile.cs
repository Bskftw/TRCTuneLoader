// A finished race in the log and in TimeAttack/races.csv, and in the server-ready events.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void LogRace() {
			string state = $"{race_.Id} {race_.Phase} {race_.RaceNumber}";
			if (state != raceLoggedState_) {
				raceLoggedState_ = state;
				TrcLog.Info($"Race {race_.Id}: {race_.Phase}, race {race_.RaceNumber}, {race_.Drivers.Count} driver(s). {race_.Message}");
			}
			if (race_.Results != null && race_.ResultsFor != raceLoggedResults_) {
				raceLoggedResults_ = race_.ResultsFor;
				foreach (RaceResultRow r in race_.Results) {
					TrcLog.Info($"  P{r.Position} {r.Name}: {RowText(r)}");
				}
				SaveRaceResults();
				RateRace();
			}
			if (race_.Ended) {
				TrcLog.Info($"Race {race_.Id}: {race_.Message}.");
				raceResult_ = race_.Message;
				race_ = null;
			}
		}

		private string RowText(RaceResultRow r) =>
			race_ != null && race_.IsGame ? $"{r.Points} pts{(r.State == RacerState.Out ? $" ({r.Reason})" : "")}"
			: r.State == RacerState.Finished && race_ != null && race_.Settings.SpeedTrapScoring ? $"{r.TrapTotal:0} km/h at the traps · {TimeAttackRun.FormatTime(r.Time)}"
			: r.State == RacerState.Finished ? $"{TimeAttackRun.FormatTime(r.Time)}{(r.Penalty > 0 ? $" (+{r.Penalty:0} s)" : "")}"
			: r.State == RacerState.Disqualified ? $"disqualified ({r.Reason})"
			: r.State == RacerState.Eliminated ? $"eliminated{(r.Reason.Length > 0 ? $" ({r.Reason})" : "")}"
			: $"didn't finish{(r.Reason.Length > 0 ? $" ({r.Reason})" : "")}";

		/// <summary>Every race result this player was in, one line each in TimeAttack/races.csv.</summary>
		private void SaveRaceResults() {
			RaceResultRow mine = race_.Results.FirstOrDefault(r => r.Id == MySteamId);
			if (mine == null) {
				return;
			}
			try {
				Directory.CreateDirectory(TimesFolder);
				string path = Path.Combine(TimesFolder, "races.csv");
				var text = new StringBuilder();
				if (!File.Exists(path)) {
					text.Append("date,race,host,course_code,course,laps,mode,position,drivers,state,time_ms,penalty_s,winner,loader_version\n");
				}
				string Q(string f) => f.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + f.Replace("\"", "\"\"") + "\"" : f;
				text.Append(string.Join(",", new[] {
					DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Q(race_.Settings.Name), Q(race_.HostName), race_.CourseCode,
					Q(race_.Course?.Name ?? ""), race_.Settings.Laps.ToString(CultureInfo.InvariantCulture), race_.Settings.Open ? "open" : "spec",
					mine.Position.ToString(CultureInfo.InvariantCulture), race_.Results.Count.ToString(CultureInfo.InvariantCulture), mine.State.ToString().ToLowerInvariant(),
					mine.State == RacerState.Finished ? ((long)Math.Round(mine.Time * 1000)).ToString(CultureInfo.InvariantCulture) : "",
					mine.Penalty.ToString("0.###", CultureInfo.InvariantCulture), Q(race_.Results[0].Name), Version,
				})).Append('\n');
				File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			LedgerEvent(race_.IsGame ? "game" : "race", EventLedger.RaceFields(race_, mine));
		}
	}
}
