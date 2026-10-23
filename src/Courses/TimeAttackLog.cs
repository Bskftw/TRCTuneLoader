using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>One finished time attack, as saved in the run log.</summary>
	public sealed class TimeAttackRecord {
		public DateTime When;
		public string CourseCode = "";
		public string CourseName = "";
		public string MapKey = "";
		public string MapName = "";
		public bool OfficialCourse;
		public int CarId;
		public string Car = "";
		public string Setup = "";

		/// <summary>The car's tune code at the start, and the official set it matched ("" when none).</summary>
		public string TuneCode = "";
		public string TuneSet = "";

		/// <summary>"spec" when the car was TRC spec at the start and at the finish, else what it was.</summary>
		public string Spec = "";

		/// <summary>How many mods the mod check flagged during the run (like Unity Explorer).</summary>
		public int FlaggedMods;
		public long TimeMs;
		public string LoaderVersion = "";

		public bool IsSpec => Spec == TimeAttackLog.SpecWord && FlaggedMods == 0;
		public double Seconds => TimeMs / 1000.0;
	}

	/// <summary>
	/// Every finished time attack, one line each in TimeAttack/runs.csv next to the mod. Lines are only
	/// ever added, so the file is the player's full history.
	/// </summary>
	public static class TimeAttackLog {
		public const string SpecWord = "spec";
		public const string FileName = "runs.csv";

		private static readonly string[] Columns = {
			"date", "course_code", "course", "map", "map_name", "official_course", "car_id", "car", "setup",
			"tune_code", "tune_set", "spec", "flagged_mods", "time_ms", "loader_version",
		};

		public static void Append(string folder, TimeAttackRecord record) {
			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, FileName);
			var text = new StringBuilder();
			if (!File.Exists(path)) {
				text.Append(string.Join(",", Columns)).Append('\n');
			}
			string[] fields = {
				record.When.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
				record.CourseCode, record.CourseName, record.MapKey, record.MapName,
				record.OfficialCourse ? "yes" : "no",
				record.CarId.ToString(CultureInfo.InvariantCulture), record.Car, record.Setup,
				record.TuneCode, record.TuneSet, record.Spec,
				record.FlaggedMods.ToString(CultureInfo.InvariantCulture),
				record.TimeMs.ToString(CultureInfo.InvariantCulture),
				record.LoaderVersion,
			};
			text.Append(string.Join(",", fields.Select(Quote))).Append('\n');
			File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
		}

		/// <summary>Every run in the log, oldest first. Lines that can't be read are skipped.</summary>
		public static List<TimeAttackRecord> Load(string folder) {
			var records = new List<TimeAttackRecord>();
			string path = Path.Combine(folder, FileName);
			if (!File.Exists(path)) {
				return records;
			}

			bool header = true;
			foreach (string line in File.ReadAllLines(path, Encoding.UTF8)) {
				if (header) {
					header = false;
					continue;
				}
				List<string> f = Split(line);
				if (f.Count < Columns.Length ||
					!DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when) ||
					!long.TryParse(f[13], NumberStyles.None, CultureInfo.InvariantCulture, out long ms)) {
					continue;
				}
				int.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int carId);
				int.TryParse(f[12], NumberStyles.None, CultureInfo.InvariantCulture, out int flagged);
				records.Add(new TimeAttackRecord {
					When = when,
					CourseCode = f[1],
					CourseName = f[2],
					MapKey = f[3],
					MapName = f[4],
					OfficialCourse = f[5] == "yes",
					CarId = carId,
					Car = f[7],
					Setup = f[8],
					TuneCode = f[9],
					TuneSet = f[10],
					Spec = f[11],
					FlaggedMods = flagged,
					TimeMs = ms,
					LoaderVersion = f[14],
				});
			}
			return records;
		}

		/// <summary>The fastest run on this course that counts (TRC spec, nothing flagged), or null.</summary>
		public static TimeAttackRecord BestSpec(IEnumerable<TimeAttackRecord> records, string courseCode) =>
			records.Where(r => r.CourseCode == courseCode && r.IsSpec).OrderBy(r => r.TimeMs).FirstOrDefault();

		/// <summary>The fastest run on this course, spec or not, or null.</summary>
		public static TimeAttackRecord Best(IEnumerable<TimeAttackRecord> records, string courseCode) =>
			records.Where(r => r.CourseCode == courseCode).OrderBy(r => r.TimeMs).FirstOrDefault();

		/// <summary>
		/// The fastest run on this course in this car with this TRC tune set ("" for runs that matched none), counted
		/// runs only when <paramref name="specOnly"/>, or null. Each car and set has its own times.
		/// </summary>
		public static TimeAttackRecord BestFor(IEnumerable<TimeAttackRecord> records, string courseCode, int carId, string tuneSet, bool specOnly) =>
			records.Where(r => r.CourseCode == courseCode && r.CarId == carId && r.TuneSet == tuneSet && (!specOnly || r.IsSpec))
				.OrderBy(r => r.TimeMs).FirstOrDefault();

		private static string Quote(string field) {
			field = (field ?? "").Replace("\r", " ").Replace("\n", " ");
			return field.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
		}

		private static List<string> Split(string line) {
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
