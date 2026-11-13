// Counts what happened on one visit to a map (kilometres, top speed, runs...).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TRC.TuneLoader {
	/// <summary>
	/// What happened on one visit to a map (IMPROVEMENT_PLAN.md F5): the kilometres, top speed, runs, laps, speed traps,
	/// battles and races, kept only to show a short summary on Home when the player leaves the map. Nothing is saved or
	/// sent. Nothing here knows about the game, so the self-test can fill one in.
	/// </summary>
	public sealed class SessionStats {
		public readonly string Map;
		private readonly double startedAt_;

		public double Km { get; private set; }
		public float TopKmh { get; private set; }
		public int Runs { get; private set; }
		public int Laps { get; private set; }
		public int Battles { get; private set; }
		public int BattlesWon { get; private set; }
		public int Races { get; private set; }
		private double bestRun_, bestLap_;
		private string bestRunOn_ = "", bestLapOn_ = "", topTrap_ = "", bestRace_ = "";
		private float topTrapKmh_, cutUp_;
		private int traps_;

		public SessionStats(string map, double now) {
			Map = map ?? "";
			startedAt_ = now;
		}

		/// <summary>Driven a little further, at this speed (a jump from a teleport isn't driving: the caller leaves those out).</summary>
		public void Drove(double km, float kmh) {
			Km += Math.Max(0, km);
			// Faster than any car goes is a glitch (a reset, a teleport), not a top speed.
			if (kmh > TopKmh && kmh < 600f) {
				TopKmh = kmh;
			}
		}

		public void Run(string course, double seconds) {
			Runs++;
			if (seconds > 0 && (bestRun_ <= 0 || seconds < bestRun_)) {
				bestRun_ = seconds;
				bestRunOn_ = course ?? "";
			}
		}

		public void Lap(string course, double seconds) {
			Laps++;
			if (seconds > 0 && (bestLap_ <= 0 || seconds < bestLap_)) {
				bestLap_ = seconds;
				bestLapOn_ = course ?? "";
			}
		}

		public void Trap(string name, float kmh) {
			traps_++;
			if (kmh > topTrapKmh_) {
				topTrapKmh_ = kmh;
				topTrap_ = name ?? "";
			}
		}

		public void Battle(bool won) {
			Battles++;
			if (won) {
				BattlesWon++;
			}
		}

		/// <summary>A race finished, <paramref name="place"/> of <paramref name="of"/> (0: no place, like a game).</summary>
		public void Race(int place, int of) {
			Races++;
			if (place > 0 && bestRace_.Length == 0) {
				bestRace_ = $"{Ordinal(place)} of {of}";
			}
		}

		public void CutUp(float points) {
			cutUp_ = Math.Max(cutUp_, points);
		}

		/// <summary>Worth a summary: a real drive, or anything timed or scored.</summary>
		public bool Worth => Km >= 1.0 || Runs + Laps + Battles + Races + traps_ > 0;

		private static string Ordinal(int n) => n == 1 ? "1st" : n == 2 ? "2nd" : n == 3 ? "3rd" : $"{n}th";

		/// <summary>The summary's lines, the map and how long first.</summary>
		public List<string> Lines(double now, Func<double, string> time) {
			var lines = new List<string>();
			int minutes = (int)Math.Round(Math.Max(0, now - startedAt_) / 60.0);
			lines.Add($"{Map}: {(minutes >= 90 ? $"{minutes / 60} h {minutes % 60} min" : $"{Math.Max(1, minutes)} min")}, {Km.ToString("0.0", CultureInfo.InvariantCulture)} km driven" +
				(TopKmh >= 1f ? $", top speed {TopKmh.ToString("0", CultureInfo.InvariantCulture)} km/h" : ""));
			if (Runs > 0) {
				lines.Add($"{Runs} time attack run{(Runs == 1 ? "" : "s")} finished" + (bestRun_ > 0 ? $", best {time(bestRun_)} on {bestRunOn_}" : ""));
			}
			if (Laps > 0) {
				lines.Add($"{Laps} lap{(Laps == 1 ? "" : "s")}" + (bestLap_ > 0 ? $", best {time(bestLap_)} on {bestLapOn_}" : ""));
			}
			if (traps_ > 0) {
				lines.Add($"{traps_} speed trap pass{(traps_ == 1 ? "" : "es")}, fastest {topTrapKmh_.ToString("0.0", CultureInfo.InvariantCulture)} km/h at {topTrap_}");
			}
			if (Battles > 0) {
				lines.Add($"{Battles} battle{(Battles == 1 ? "" : "s")}, {BattlesWon} won");
			}
			if (Races > 0) {
				lines.Add($"{Races} race{(Races == 1 ? "" : "s")}" + (bestRace_.Length > 0 ? $", the first one {bestRace_}" : ""));
			}
			if (cutUp_ > 0) {
				lines.Add($"Best cut-up score {cutUp_.ToString("0", CultureInfo.InvariantCulture)}");
			}
			return lines;
		}
	}
}
