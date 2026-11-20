// Driver stats (docs/archive/IMPROVEMENT_PLAN.md F6): km per car, the favourite track, battle and race results, from the mod's own logs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// What the stats page shows, worked out from files the mod already keeps (runs.csv's records, battles.csv, races.csv)
	/// plus one new one, km_by_car.txt (each car's kilometres, from Nov 18 on). Game-free (SelfTest.Stage6.cs).
	/// </summary>
	public static class DriverStats {
		public const string KmFile = "km_by_car.txt";

		/// <summary>km_by_car.txt: "car [tab] km" a line. Missing or damaged lines are skipped.</summary>
		public static Dictionary<string, double> ReadKm(string text) {
			var km = new Dictionary<string, double>(StringComparer.Ordinal);
			foreach (string line in (text ?? "").Split('\n')) {
				string[] parts = line.TrimEnd('\r').Split('\t');
				if (parts.Length == 2 && parts[0].Length > 0 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value >= 0) {
					km[parts[0]] = value;
				}
			}
			return km;
		}

		public static string WriteKm(Dictionary<string, double> km) {
			var sb = new StringBuilder();
			foreach (var pair in km.OrderByDescending(p => p.Value)) {
				sb.Append(pair.Key.Replace("\t", " ").Replace("\n", " ")).Append('\t').Append(pair.Value.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
			}
			return sb.ToString();
		}

		public sealed class Results {
			public int Won, Lost, Other;
			public int Total => Won + Lost + Other;
			public string WinRate => Won + Lost == 0 ? "-" : $"{100.0 * Won / (Won + Lost):0}%";
		}

		/// <summary>battles.csv's lines: the results ("win", "loss", others), all together and by your car.</summary>
		public static (Results All, Dictionary<string, Results> ByCar) Battles(IEnumerable<List<string>> rows) {
			var all = new Results();
			var byCar = new Dictionary<string, Results>(StringComparer.Ordinal);
			List<string> header = null;
			foreach (List<string> row in rows) {
				if (header == null) {
					header = row;
					continue;
				}
				string result = Field(header, row, "result"), car = Field(header, row, "my_car");
				if (!byCar.TryGetValue(car, out Results mine)) {
					byCar[car] = mine = new Results();
				}
				foreach (Results r in new[] { all, mine }) {
					if (result == "win") {
						r.Won++;
					} else if (result == "loss") {
						r.Lost++;
					} else {
						r.Other++;
					}
				}
			}
			return (all, byCar);
		}

		/// <summary>races.csv's lines: races finished, won (position 1) and on the podium.</summary>
		public static (int Races, int Won, int Podiums) Races(IEnumerable<List<string>> rows) {
			int races = 0, won = 0, podiums = 0;
			List<string> header = null;
			foreach (List<string> row in rows) {
				if (header == null) {
					header = row;
					continue;
				}
				races++;
				if (int.TryParse(Field(header, row, "position"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int position) && position > 0) {
					won += position == 1 ? 1 : 0;
					podiums += position <= 3 ? 1 : 0;
				}
			}
			return (races, won, podiums);
		}

		private static string Field(List<string> header, List<string> row, string name) {
			int at = header.IndexOf(name);
			return at >= 0 && at < row.Count ? row[at] : "";
		}
	}
}
