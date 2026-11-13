// The time attack timer over the track: the lap or run time, laps, best lap, live delta and this lap's sectors.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>"S1 19.234  S2 21.002  S3 –": this lap's sectors in their pace colours, the ones still to come greyed (three to a line).</summary>
		private string SectorStrip(TimeAttackRun run) {
			List<double> done = run.CurrentSectors;
			int count = run.Course.SectorEnds.Length;
			// Made again when a sector is done or the bests change, and at least every second; not on every GUI pass
			// (docs/archive/OPTIMIZATION_REPORT.md item 6). A done sector's time never changes, so the text is the same.
			if (ReferenceEquals(stripRun_, run) && ReferenceEquals(stripDone_, done) && stripDoneCount_ == done.Count && stripCount_ == count &&
				ReferenceEquals(stripAll_, sectorBestAll_) && ReferenceEquals(stripMine_, sectorBestMine_) && stripDetailed_ == timingDetailed_ &&
				Time.unscaledTime - stripAt_ < 1f) {
				return strip_;
			}
			stripRun_ = run;
			stripDone_ = done;
			stripDoneCount_ = done.Count;
			stripCount_ = count;
			stripAll_ = sectorBestAll_;
			stripMine_ = sectorBestMine_;
			stripAt_ = Time.unscaledTime;
			stripDetailed_ = timingDetailed_;
			strip_ = MakeSectorStrip(done, count);
			return strip_;
		}

		private TimeAttackRun stripRun_;
		private List<double> stripDone_;
		private int stripDoneCount_ = -1, stripCount_ = -1;
		private long?[] stripAll_, stripMine_;
		private float stripAt_;
		private bool stripDetailed_;
		private string strip_ = "";

		private string MakeSectorStrip(List<double> done, int count) {
			if (timingDetailed_) {
				return MakeDetailedStrip(done, count);
			}
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

		/// <summary>Detailed timing: each done sector on its own line with its gaps, the ones to come on one line.</summary>
		private string MakeDetailedStrip(List<double> done, int count) {
			var lines = new List<string>();
			for (int i = 0; i < done.Count && i < count; i++) {
				long? all = i < sectorBestAll_.Length ? sectorBestAll_[i] : null;
				long? mine = i < sectorBestMine_.Length ? sectorBestMine_[i] : null;
				lines.Add(DetailedSector(i, (long)Math.Round(done[i] * 1000.0), all, mine));
			}
			if (done.Count < count) {
				lines.Add($"<color={Theme.MutedHex}>{string.Join("  ", Enumerable.Range(done.Count, count - done.Count).Select(i => $"S{i + 1} –"))}</color>");
			}
			return string.Join("\n", lines);
		}

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
			AddDeltaLines(run, lines);
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
			DrawTimer(screenWidth, screenHeight, run, string.Join("\n", lines), run.Course.IsCircuit ? run.LapElapsed : run.Elapsed, run.Delta);
		}
	}
}
