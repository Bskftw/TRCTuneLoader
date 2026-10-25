using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Laps, sectors and the live delta in time attack: the start options (standing or flying, how many laps),
	/// what happens as each sector and lap ends (the flashes, the laps saved, the best lap kept for the delta),
	/// the circuit summary, the timer over the track and the restart key.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private static readonly string[] StartChoices = { "Standing", "Flying" };
		private static readonly int[] LapChoices = { 1, 3, 5, 10, 0 };
		private static readonly string[] LapChoiceNames = { "1", "3", "5", "10", "Unlimited" };

		private const string StartPref = "time_attack_start";
		private const string LapsPref = "time_attack_laps";

		/// <summary>How long a sector split or a lap time flashes over the track.</summary>
		private const float FlashSeconds = 4f;

		private const string SectorsPref = "show_sectors";

		/// <summary>Sector times on the timer, with the result and in My times (Options).</summary>
		private bool showSectors_ = true;

		private int pickedStart_;
		private int pickedLaps_ = 1;
		private bool lapPrefsLoaded_;

		/// <summary>Laps and the split of the run already dealt with, so each is handled once.</summary>
		private int lapsSeen_;
		private string lapFlash_ = "";
		private float lapFlashUntil_;
		private bool referenceLoaded_;

		/// <summary>The best sector times so far for this run's kind of time (any car), and in this car on this set: for the colours.</summary>
		private long?[] sectorBestAll_ = new long?[0];
		private long?[] sectorBestMine_ = new long?[0];

		private bool PickedFlying => pickedStart_ == 1;
		private int PickedLaps => LapChoices[Mathf.Clamp(pickedLaps_, 0, LapChoices.Length - 1)];

		private void LoadLapPrefs() {
			if (lapPrefsLoaded_) {
				return;
			}
			lapPrefsLoaded_ = true;
			try {
				pickedStart_ = Mathf.Clamp(KSL.API.Kino.Prefs.Get(StartPref, 0), 0, StartChoices.Length - 1);
				pickedLaps_ = Mathf.Clamp(KSL.API.Kino.Prefs.Get(LapsPref, 1), 0, LapChoices.Length - 1);
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't read the time attack options: {e.Message}");
			}
		}

		/// <summary>Standing or flying, and (for circuits) how many laps: above the list of courses.</summary>
		private void DrawStartOptions(bool anyCircuit) {
			LoadLapPrefs();
			GUILayout.BeginHorizontal();
			GUILayout.Label("Start", Theme.SectionLabel, GUILayout.Width(60));
			int start = Choice(StartChoices, pickedStart_);
			GUILayout.EndHorizontal();
			if (start != pickedStart_) {
				pickedStart_ = start;
				SavePref(StartPref, start);
			}
			if (anyCircuit) {
				GUILayout.BeginHorizontal();
				GUILayout.Label("Laps", Theme.SectionLabel, GUILayout.Width(60));
				int laps = Choice(LapChoiceNames, pickedLaps_);
				GUILayout.EndHorizontal();
				if (laps != pickedLaps_) {
					pickedLaps_ = laps;
					SavePref(LapsPref, laps);
				}
			}
			GUILayout.Label(PickedFlying
				? "Flying: the clock starts when you drive through the start line, at any speed. Flying times are kept apart from standing ones."
				: "Standing: stop behind the start line and the clock starts at GO.", Theme.Small);
			if (anyCircuit && PickedLaps == 0) {
				GUILayout.Label("Unlimited laps (circuits) is practice: a lap with a shortcut or off the road just doesn't count, and you keep going. Only your best lap is kept.", Theme.Small);
			}
		}

		/// <summary>A new run (or the same one again): nothing from the last one carries over.</summary>
		private void ResetRunExtras() {
			lapsSeen_ = 0;
			lapFlash_ = "";
			lapFlashUntil_ = 0;
			referenceLoaded_ = false;
		}

		/// <summary>The kind of time a run's whole result is, and the kind its laps are (circuits).</summary>
		private static TimeKind RunKind(TimeAttackRun run) => new TimeKind { Flying = run.Flying, Laps = run.Laps };
		private static readonly TimeKind LapKind = new TimeKind { Lap = true, Flying = true };

		/// <summary>The time the live delta and the splits compare with: on a circuit the flying lap, else the whole run.</summary>
		private static TimeKind CompareKind(TimeAttackRun run) => run.Course.IsCircuit ? LapKind : RunKind(run);

		/// <summary>
		/// Called after every tick of a time attack: loads what this run compares with once the car is known, and
		/// deals with each sector and lap as it ends.
		/// </summary>
		private void HandleRunProgress() {
			if (!referenceLoaded_ && runStart_ != null) {
				referenceLoaded_ = true;
				TimeKind kind = CompareKind(run_);
				run_.Reference = TimeAttackTraces.Load(TimesFolder, TimeAttackTraces.Key(run_.Course.Code, runStart_.CarId, runStart_.Set, kind))?.Trace;
				int sectors = run_.Course.SectorEnds.Length;
				sectorBestAll_ = TimeAttackLog.BestSectorsMs(times_, r => r.CourseCode == run_.Course.Code && kind.Matches(r), sectors);
				int car = runStart_.CarId;
				string set = runStart_.Set;
				sectorBestMine_ = TimeAttackLog.BestSectorsMs(times_, r => r.CourseCode == run_.Course.Code && r.CarId == car && r.TuneSet == set && kind.Matches(r), sectors);
			}


			while (lapsSeen_ < run_.LapTimes.Count) {
				LapTime lap = run_.LapTimes[lapsSeen_++];
				if (run_.Course.IsCircuit) {
					LapDone(lap);
				}
			}
		}

		/// <summary>
		/// The colour of a sector time: purple (great pace) when it beats the best this sector has ever been driven,
		/// green (on pace) when it's as good as your best in this car on this set (or there's none yet), amber (below
		/// pace) when it's slower. <paramref name="orEqual"/>: for saved times, matching the best counts as the best.
		/// </summary>
		private static string PaceColour(long ms, long? all, long? mine, bool orEqual = false) {
			if (all != null && (ms < all || (orEqual && ms == all))) {
				return Theme.PurpleHex;
			}
			return mine == null || ms <= mine ? Theme.GoodHex : Theme.WarnHex;
		}

		/// <summary>A finished run's sectors in their pace colours, for the result (compared with the bests from before this run).</summary>
		private string ResultSectors(LapTime lap) {
			var parts = new List<string>();
			for (int i = 0; i < lap.Sectors.Length; i++) {
				long ms = (long)Math.Round(lap.Sectors[i] * 1000.0);
				long? all = i < sectorBestAll_.Length ? sectorBestAll_[i] : null;
				long? mine = i < sectorBestMine_.Length ? sectorBestMine_[i] : null;
				parts.Add($"<color={PaceColour(ms, all, mine)}>S{i + 1} {TimeAttackRun.FormatTime(lap.Sectors[i])}</color>");
			}
			return string.Join("  ", parts);
		}

		/// <summary>"S1 19.234  S2 21.002  S3 –": this lap's sectors in their pace colours, the ones still to come greyed (three to a line).</summary>
		private string SectorStrip(TimeAttackRun run) {
			List<double> done = run.CurrentSectors;
			int count = run.Course.SectorEnds.Length;
			var parts = new List<string>();
			for (int i = 0; i < count; i++) {
				if (i < done.Count) {
					long ms = (long)Math.Round(done[i] * 1000.0);
					long? all = i < sectorBestAll_.Length ? sectorBestAll_[i] : null;
					long? mine = i < sectorBestMine_.Length ? sectorBestMine_[i] : null;
					parts.Add($"<color={PaceColour(ms, all, mine)}>S{i + 1} {TimeAttackRun.FormatTime(done[i])}</color>");
				} else {
					parts.Add($"<color={Theme.MutedHex}>S{i + 1} –</color>");
				}
			}
			var lines = new List<string>();
			for (int i = 0; i < parts.Count; i += 3) {
				lines.Add(string.Join("  ", parts.Skip(i).Take(3)));
			}
			return string.Join("\n", lines);
		}

		/// <summary>A circuit lap ended: shown, saved when it counts, and kept for the delta when it's the best yet.</summary>
		private void LapDone(LapTime lap) {
			RunCheck start = runStart_ ?? new RunCheck { CarId = pose_.CarId };
			long ms = (long)Math.Round(lap.Time * 1000.0, MidpointRounding.AwayFromZero);
			if (!lap.Valid) {
				lapFlash_ = $"<color={Theme.BadHex}>Lap {lap.Number} {TimeAttackRun.FormatTime(lap.Time)}: doesn't count ({lap.Invalid})</color>";
				lapFlashUntil_ = Time.unscaledTime + FlashSeconds;
				TrcLog.Info($"Time attack on {run_.Course.Name}: lap {lap.Number} {TimeAttackRun.FormatTime(lap.Time)}, not counted ({lap.Invalid}).");
				return;
			}

			TimeAttackRecord best = TimeAttackLog.BestFor(times_, run_.Course.Code, start.CarId, start.Set, specOnly: false, LapKind);
			if (!lap.Standing) {
				// The car's state now is what the live check last saw; a full check here would cost a frame on the line.
				SpecState now = carStatus_ != null ? StateOf(carStatus_) : start.State;
				var record = RecordFor(start, now, ms);
				record.Flying = true;
				record.Laps = run_.Laps;
				record.LapNumber = lap.Number;
				record.SectorMs = lap.Sectors.Select(s => (long)Math.Round(s * 1000.0, MidpointRounding.AwayFromZero)).ToArray();
				SaveRecord(record);
				if (TimeAttackTraces.SaveIfFaster(TimesFolder, TimeAttackTraces.Key(run_.Course.Code, start.CarId, start.Set, LapKind), lap.Trace, ms)) {
					run_.Reference = lap.Trace;
				}
			}

			string compare = lap.Standing ? " (standing start)"
				: best == null ? " · first lap"
				: ms < best.TimeMs ? $" · <color={Theme.PurpleHex}>best lap by {TimeAttackRun.FormatTime((best.TimeMs - ms) / 1000.0)}</color>"
				: $" · {TimeAttackRun.FormatGap((ms - best.TimeMs) / 1000.0)}";
			lapFlash_ = $"<b>Lap {lap.Number} {TimeAttackRun.FormatTime(lap.Time)}</b>{compare}";
			lapFlashUntil_ = Time.unscaledTime + FlashSeconds;
			TrcLog.Info($"Time attack on {run_.Course.Name}: lap {lap.Number} {TimeAttackRun.FormatTime(lap.Time)} ({string.Join(" / ", lap.Sectors.Select(s => TimeAttackRun.FormatTime(s)))}).");
		}

		private TimeAttackRecord RecordFor(RunCheck start, SpecState end, long ms) {
			Course course = run_.Course;
			return new TimeAttackRecord {
				When = DateTime.Now,
				CourseCode = course.Code,
				CourseName = course.Name,
				MapKey = course.MapKey,
				MapName = course.MapName,
				OfficialCourse = course.Official,
				CarId = start.CarId,
				Car = start.Car,
				Setup = start.Setup,
				TuneCode = start.Code,
				TuneSet = start.Set,
				Spec = SpecWord(start.State, end),
				FlaggedMods = start.Flagged,
				TimeMs = ms,
				LoaderVersion = Version,
			};
		}

		private void SaveRecord(TimeAttackRecord record) {
			try {
				TimeAttackLog.Append(TimesFolder, record);
				times_.Add(record);
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the time: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>
		/// The laps of a circuit run, in a few words: how many, the best, the average of the valid ones and how even
		/// they were. "" when no lap was finished.
		/// </summary>
		private static string LapSummary(TimeAttackRun run) {
			if (run == null || !run.Course.IsCircuit || run.LapTimes.Count == 0) {
				return "";
			}
			List<LapTime> valid = run.LapTimes.Where(l => l.Valid).ToList();
			int invalid = run.LapTimes.Count - valid.Count;
			string laps = $"{run.LapTimes.Count} lap{(run.LapTimes.Count == 1 ? "" : "s")}{(invalid > 0 ? $" ({invalid} didn't count)" : "")}";
			if (valid.Count == 0) {
				return laps;
			}
			LapTime best = valid.OrderBy(l => l.Time).First();
			string text = $"{laps} · best {TimeAttackRun.FormatTime(best.Time)} (lap {best.Number})";
			List<LapTime> flying = valid.Where(l => !l.Standing).ToList();
			if (flying.Count >= 2) {
				double average = flying.Average(l => l.Time);
				double spread = Math.Sqrt(flying.Average(l => (l.Time - average) * (l.Time - average)));
				text += $" · average {TimeAttackRun.FormatTime(average)} · laps within ±{spread.ToString("0.000", CultureInfo.InvariantCulture)} s";
			}
			return text;
		}

		/// <summary>When a practice (or any circuit run) ends early, what was driven goes in the log and on screen.</summary>
		private void EndCircuitRunEarly(string why) {
			string summary = LapSummary(run_);
			if (summary.Length == 0) {
				return;
			}
			courseResult_ = $"{run_.Course.Name}: {summary}.";
			TrcLog.Info($"Time attack on {run_.Course.Name} {why}: {summary}.");
		}

		// ---- Over the track ----

		/// <summary>
		/// The timer while a time attack runs: the lap time big on a circuit (the run's time otherwise), then the lap
		/// count and total, the best lap, the live delta, this lap's sectors (they stay up), and for a few seconds the last lap.
		/// </summary>
		private void DrawRunTimer(float screenWidth, float screenHeight) {
			TimeAttackRun run = run_;
			var lines = new List<string>();
			if (run.Course.IsCircuit) {
				string laps = run.Laps == 0 ? $"Lap {run.Lap}" : $"Lap {run.Lap}/{run.Laps}";
				lines.Add($"{laps} · total {TimeAttackRun.FormatTime(run.Elapsed)}{(run.BestLap != null ? $" · best {TimeAttackRun.FormatTime(run.BestLap.Time)}" : "")}");
			}
			double? delta = run.Delta;
			if (delta.HasValue) {
				lines.Add($"<color={(delta.Value <= 0 ? Theme.GoodHex : Theme.BadHex)}>{TimeAttackRun.FormatGap(delta.Value)}</color> on your best");
			}
			if (!run.LapValid) {
				lines.Add($"<color={Theme.BadHex}>This lap doesn't count ({run.LapInvalid})</color>");
			}
			// The sectors stay on screen for the whole lap (Options can hide them).
			if (showSectors_ && run.Course.SectorEnds.Length > 1) {
				lines.Add(SectorStrip(run));
			}
			if (Time.unscaledTime < lapFlashUntil_ && lapFlash_.Length > 0) {
				lines.Add(lapFlash_);
			}
			DrawTimer(screenWidth, screenHeight, run, string.Join("\n", lines), run.Course.IsCircuit ? run.LapElapsed : run.Elapsed);
		}

		// ---- Restart key ----

		/// <summary>The restart key during a time attack: back to the start line, ready to go again.</summary>
		private void RestartFromKey() {
			if (run_ == null || InBattle) {
				return;
			}
			TeleportToStart();
		}
	}
}
