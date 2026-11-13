// What a time attack compares with: your best on the track in any car (the live delta, the PB pop-ups, the best line) and your best in this car.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const string TimingDetailPref = "timing_detail";

		/// <summary>
		/// Options > Driving, Timing detail (bskftw, Nov 12): Simple compares with your best on the track in any car;
		/// Detailed also shows your best in this car, each sector's gaps, and at speed traps the fastest ever with its car.
		/// </summary>
		private bool timingDetailed_;

		/// <summary>The time the live delta compares with (ms, 0 for none), and its car when it wasn't this car on this set ("" when it was).</summary>
		private long bestAnyCarMs_;
		private string bestAnyCarName_ = "";

		/// <summary>Your best of this kind in this car on this set (ms, 0 for none).</summary>
		private long bestThisCarMs_;

		/// <summary>
		/// Loads what this run compares with. bskftw, Nov 12: "Sector PBs should carry over no matter which car you're in."
		/// Before, everything compared only with this car on this set, so a car without a time had no delta and no PB.
		/// Now the delta, the sector pop-ups and the best line use the fastest saved time of this kind on the course in
		/// any car, and this car's own best is kept beside it.
		/// </summary>
		private void LoadRunReferences(TimeKind kind) {
			string code = run_.Course.Code;
			string ownKey = TimeAttackTraces.Key(code, runStart_.CarId, runStart_.Set, kind);
			var own = TimeAttackTraces.Load(TimesFolder, ownKey);
			run_.CarReference = own?.Trace;
			bestThisCarMs_ = own?.TimeMs ?? 0;

			string bestKey = own != null ? ownKey : null;
			long bestMs = own?.TimeMs ?? long.MaxValue;
			float[] bestTrace = own?.Trace;
			string bestCar = "";
			var tried = new HashSet<string>(StringComparer.Ordinal) { ownKey };
			// Fastest first: the first car and set with a saved trace that beats this car's is the best in any car.
			foreach (TimeAttackRecord r in times_.Where(r => r.CourseCode == code && kind.Matches(r) && r.TimeMs < bestMs).OrderBy(r => r.TimeMs).ToList()) {
				string key = TimeAttackTraces.Key(code, r.CarId, r.TuneSet, kind);
				if (!tried.Add(key)) {
					continue;
				}
				var saved = TimeAttackTraces.Load(TimesFolder, key);
				if (saved != null && saved.Value.TimeMs < bestMs) {
					bestKey = key;
					bestMs = saved.Value.TimeMs;
					bestTrace = saved.Value.Trace;
					bestCar = r.CarId == runStart_.CarId ? $"{r.Car} (another tune)" : r.Car ?? "";
					break;
				}
			}
			run_.Reference = bestTrace;
			bestAnyCarMs_ = bestKey != null ? bestMs : 0;
			bestAnyCarName_ = bestCar;
			var line = bestKey != null ? TimeAttackTraces.LoadLine(TimesFolder, bestKey) : null;
			bestLine_ = line?.Line;
			bestLineKmh_ = line?.Kmh;
			run_.SetReferenceLine(line?.Line, line?.Kmh, bestAnyCarMs_ / 1000.0);
		}

		/// <summary>A circuit lap that's this car's best: kept as this car's reference, and as the main one when it beats every car.</summary>
		private void KeepLapAsBest(LapTime lap, string key, long ms) {
			TimeAttackTraces.SaveLine(TimesFolder, key, lap.Line, lap.LineKmh, ms);
			run_.CarReference = lap.Trace;
			bestThisCarMs_ = ms;
			if (bestAnyCarMs_ == 0 || ms < bestAnyCarMs_) {
				run_.Reference = lap.Trace;
				run_.SetReferenceLine(lap.Line, lap.LineKmh, lap.Time);
				bestLine_ = lap.Line;
				bestLineKmh_ = lap.LineKmh;
				bestAnyCarMs_ = ms;
				bestAnyCarName_ = "";
			}
		}

		/// <summary>A car's name cut to fit the timer.</summary>
		private static string ShortCar(string car) => car == null ? "" : car.Length <= 18 ? car : car.Substring(0, 17) + "…";

		/// <summary>The timer's delta lines: the gap to your best (any car) with the predicted time, and in Detailed the gap to this car's best.</summary>
		private void AddDeltaLines(TimeAttackRun run, List<string> lines) {
			double? delta = run.Delta;
			if (delta.HasValue) {
				string predicted = bestAnyCarMs_ > 0 ? $" · predicted {TimeAttackRun.FormatTime(bestAnyCarMs_ / 1000.0 + delta.Value)}" : "";
				lines.Add($"<color={(delta.Value <= 0 ? Theme.GoodHex : Theme.BadHex)}>{TimeAttackRun.FormatGap(delta.Value)}</color> on your best{predicted}");
			}
			if (timingDetailed_ && bestAnyCarName_.Length > 0 && run.Course.HasPath) {
				double? mine = run.CarDelta;
				lines.Add(mine.HasValue
					? $"<color={(mine.Value <= 0 ? Theme.GoodHex : Theme.BadHex)}>{TimeAttackRun.FormatGap(mine.Value)}</color> this car · best in {ShortCar(bestAnyCarName_)}"
					: $"<color={Theme.MutedHex}>No time in this car yet · best in {ShortCar(bestAnyCarName_)}</color>");
			}
		}

		/// <summary>One sector in Detailed timing: its time, then the gaps to the best in any car and in this car.</summary>
		private static string DetailedSector(int index, long ms, long? all, long? mine) {
			string text = $"<color={PaceColour(ms, all, mine)}>S{index + 1} {TimeAttackRun.FormatTime(ms / 1000.0)}</color>";
			if (all != null) {
				text += $"  <color={Theme.MutedHex}>best {TimeAttackRun.FormatGap((ms - all.Value) / 1000.0)}</color>";
			}
			if (mine != null && mine != all) {
				text += $"  <color={Theme.MutedHex}>car {TimeAttackRun.FormatGap((ms - mine.Value) / 1000.0)}</color>";
			}
			return text;
		}
	}
}
