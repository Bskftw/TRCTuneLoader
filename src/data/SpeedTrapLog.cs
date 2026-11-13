// Every speed caught by a trap, one line each in TimeAttack/speedtraps.csv, and reading them back.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>One speed caught by a trap, as saved in TimeAttack/speedtraps.csv.</summary>
	public sealed class SpeedTrapRecord {
		public DateTime When;
		public string TrapKey = "";
		public string TrapName = "";
		public string MapName = "";
		public float Kmh;
		public int CarId;
		public string Car = "";
		public string TuneSet = "";
		public bool Spec;

		/// <summary>"freeroam", or the course code of the run it was caught in.</summary>
		public string Context = "";

		/// <summary>0.16.1: the rules of the run or event it was caught in: "" TRC spec (or free roam), "open", or a regulation's code.</summary>
		public string Rule = "";
	}

	/// <summary>Every speed caught by a trap, one line each, only ever added to.</summary>
	public static class SpeedTrapLog {
		public const string FileName = "speedtraps.csv";
		private const string Header = "date,trap_key,trap,map,kmh,car_id,car,tune_set,spec,context,rules";

		public static void Append(string folder, SpeedTrapRecord r) {
			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, FileName);
			var text = new StringBuilder();
			if (!File.Exists(path)) {
				text.Append(Header).Append('\n');
			}
			string Q(string f) => (f ?? "").IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + f.Replace("\"", "\"\"") + "\"" : f ?? "";
			text.Append(string.Join(",", new[] {
				r.When.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Q(r.TrapKey), Q(r.TrapName), Q(r.MapName),
				r.Kmh.ToString("0.0", CultureInfo.InvariantCulture), r.CarId.ToString(CultureInfo.InvariantCulture), Q(r.Car), Q(r.TuneSet),
				r.Spec ? "spec" : "no", Q(r.Context), Q(r.Rule),
			})).Append('\n');
			File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
		}

		public static List<SpeedTrapRecord> Load(string folder) {
			var records = new List<SpeedTrapRecord>();
			string path = Path.Combine(folder, FileName);
			if (!File.Exists(path)) {
				return records;
			}
			foreach (string line in File.ReadAllLines(path, Encoding.UTF8).Skip(1)) {
				List<string> f = CsvFields(line);
				if (f.Count < 10 || !DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when) ||
					!float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float kmh)) {
					continue;
				}
				int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int car);
				records.Add(new SpeedTrapRecord {
					When = when, TrapKey = f[1], TrapName = f[2], MapName = f[3], Kmh = kmh, CarId = car, Car = f[6], TuneSet = f[7], Spec = f[8] == "spec", Context = f[9],
					Rule = f.Count >= 11 ? f[10] : "",
				});
			}
			return records;
		}

		private static List<string> CsvFields(string line) {
			var fields = new List<string>();
			var field = new StringBuilder();
			bool quoted = false;
			for (int i = 0; i < line.Length; i++) {
				char c = line[i];
				if (quoted) {
					if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') {
						field.Append('"');
						i++;
					} else if (c == '"') {
						quoted = false;
					} else {
						field.Append(c);
					}
				} else if (c == '"') {
					quoted = true;
				} else if (c == ',') {
					fields.Add(field.ToString());
					field.Clear();
				} else {
					field.Append(c);
				}
			}
			fields.Add(field.ToString().TrimEnd('\r'));
			return fields;
		}
	}
}
