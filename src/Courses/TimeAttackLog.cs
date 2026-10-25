using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>One finished time attack, or one lap of a circuit, as saved in the run log.</summary>
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

		/// <summary>Started at speed through the start line, not from a standstill at GO.</summary>
		public bool Flying;

		/// <summary>The laps the run was for: 1 for point to point, 0 for unlimited (circuits).</summary>
		public int Laps = 1;

		/// <summary>One lap of a circuit (its number), not a whole run (0).</summary>
		public int LapNumber;

		/// <summary>Milliseconds for each sector, in order (empty for runs from before sectors).</summary>
		public long[] SectorMs = new long[0];

		/// <summary>The speeds caught at the course's speed traps, in km/h, in the order driven (empty without traps).</summary>
		public float[] TrapKmh = new float[0];
		public float TrapTotal => TrapKmh.Sum();

		public bool IsSpec => Spec == TimeAttackLog.SpecWord && FlaggedMods == 0;
		public bool IsLap => LapNumber > 0;
		public double Seconds => TimeMs / 1000.0;
	}

	/// <summary>Which kind of time a record is: whole runs only compare with runs started the same way over the same laps; laps with laps.</summary>
	public struct TimeKind {
		public bool Lap;
		public bool Flying;
		public int Laps;

		public static TimeKind Of(TimeAttackRecord record) =>
			record.IsLap ? new TimeKind { Lap = true, Flying = record.Flying } : new TimeKind { Flying = record.Flying, Laps = record.Laps };

		public bool Matches(TimeAttackRecord record) =>
			record.IsLap == Lap && record.Flying == Flying && (Lap || record.Laps == Laps);
	}

	/// <summary>
	/// Every finished time attack, one line each in TimeAttack/runs.csv next to the mod, and every valid lap of a
	/// circuit. Lines are only ever added, so the file is the player's full history. Columns added later go at
	/// the end, so older lines still read (as standing-start, point-to-point runs).
	/// </summary>
	public static class TimeAttackLog {
		public const string SpecWord = "spec";
		public const string FileName = "runs.csv";

		private static readonly string[] Columns = {
			"date", "course_code", "course", "map", "map_name", "official_course", "car_id", "car", "setup",
			"tune_code", "tune_set", "spec", "flagged_mods", "time_ms", "loader_version",
			"start", "laps", "lap", "sectors_ms", "traps_kmh",
		};

		/// <summary>The first columns, which every line has (lines from before 0.11 have only these).</summary>
		private const int OldColumns = 15;

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
				record.Flying ? "flying" : "standing",
				record.Laps.ToString(CultureInfo.InvariantCulture),
				record.LapNumber.ToString(CultureInfo.InvariantCulture),
				string.Join("|", record.SectorMs.Select(ms => ms.ToString(CultureInfo.InvariantCulture))),
				string.Join("|", record.TrapKmh.Select(k => k.ToString("0.0", CultureInfo.InvariantCulture))),
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
				if (f.Count < OldColumns ||
					!DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when) ||
					!long.TryParse(f[13], NumberStyles.None, CultureInfo.InvariantCulture, out long ms)) {
					continue;
				}
				int.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int carId);
				int.TryParse(f[12], NumberStyles.None, CultureInfo.InvariantCulture, out int flagged);
				var record = new TimeAttackRecord {
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
				};
				// Columns added later are read when the line has them (0.11: start, laps, lap, sectors; 0.13: traps).
				if (f.Count >= 19) {
					record.Flying = f[15] == "flying";
					int.TryParse(f[16], NumberStyles.None, CultureInfo.InvariantCulture, out record.Laps);
					int.TryParse(f[17], NumberStyles.None, CultureInfo.InvariantCulture, out record.LapNumber);
					record.SectorMs = f[18].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
						.Select(t => long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out long v) ? v : -1)
						.ToArray();
					if (record.SectorMs.Any(v => v < 0)) {
						record.SectorMs = new long[0];
					}
				}
				if (f.Count >= 20) {
					record.TrapKmh = f[19].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
						.Select(t => float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : -1f)
						.ToArray();
					if (record.TrapKmh.Any(v => v < 0)) {
						record.TrapKmh = new float[0];
					}
				}
				records.Add(record);
			}
			return records;
		}

		/// <summary>The fastest whole run (or, on a circuit, lap) on this course that counts (TRC spec, nothing flagged), or null.</summary>
		public static TimeAttackRecord BestSpec(IEnumerable<TimeAttackRecord> records, string courseCode) =>
			Headline(records.Where(r => r.CourseCode == courseCode && r.IsSpec));

		/// <summary>The fastest run (or lap) on this course, spec or not, or null.</summary>
		public static TimeAttackRecord Best(IEnumerable<TimeAttackRecord> records, string courseCode) =>
			Headline(records.Where(r => r.CourseCode == courseCode));

		/// <summary>The time to show for a course: its best lap when it has laps (circuits), else its best standing-start run.</summary>
		private static TimeAttackRecord Headline(IEnumerable<TimeAttackRecord> records) {
			List<TimeAttackRecord> list = records.ToList();
			return list.Where(r => r.IsLap && r.Flying).OrderBy(r => r.TimeMs).FirstOrDefault()
				?? list.Where(r => !r.IsLap && !r.Flying).OrderBy(r => r.TimeMs).FirstOrDefault()
				?? list.OrderBy(r => r.TimeMs).FirstOrDefault();
		}

		/// <summary>
		/// The fastest time of this kind (<paramref name="kind"/>) on this course in this car with this TRC tune set,
		/// counted ones only when <paramref name="specOnly"/>, or null.
		/// </summary>
		public static TimeAttackRecord BestFor(IEnumerable<TimeAttackRecord> records, string courseCode, int carId, string tuneSet, bool specOnly, TimeKind kind) =>
			records.Where(r => r.CourseCode == courseCode && r.CarId == carId && r.TuneSet == tuneSet && (!specOnly || r.IsSpec) && kind.Matches(r))
				.OrderBy(r => r.TimeMs).FirstOrDefault();

		/// <summary>
		/// The best time each sector has been driven in (of this kind, in this car on this set), added up: what a
		/// run made of your best sectors would take. Null when no time of this kind has sectors (or they differ in count).
		/// </summary>
		public static long? TheoreticalBestMs(IEnumerable<TimeAttackRecord> records, string courseCode, int carId, string tuneSet, TimeKind kind, int sectors) {
			List<long[]> all = records.Where(r => r.CourseCode == courseCode && r.CarId == carId && r.TuneSet == tuneSet && kind.Matches(r) && r.SectorMs.Length == sectors && sectors > 0)
				.Select(r => r.SectorMs).ToList();
			if (all.Count == 0) {
				return null;
			}
			long sum = 0;
			for (int i = 0; i < sectors; i++) {
				sum += all.Min(s => s[i]);
			}
			return sum;
		}

		/// <summary>The best time for each sector so far (of this kind), for the colours of the splits: null where there's none.</summary>
		public static long?[] BestSectorsMs(IEnumerable<TimeAttackRecord> records, Func<TimeAttackRecord, bool> which, int sectors) {
			var best = new long?[sectors];
			foreach (TimeAttackRecord r in records.Where(which).Where(r => r.SectorMs.Length == sectors)) {
				for (int i = 0; i < sectors; i++) {
					if (best[i] == null || r.SectorMs[i] < best[i]) {
						best[i] = r.SectorMs[i];
					}
				}
			}
			return best;
		}

		/// <summary>
		/// The fastest standing-start run on this course in this car with this TRC tune set ("" for runs that matched
		/// none), counted runs only when <paramref name="specOnly"/>, or null. Each car and set has its own times.
		/// </summary>
		public static TimeAttackRecord BestFor(IEnumerable<TimeAttackRecord> records, string courseCode, int carId, string tuneSet, bool specOnly) =>
			BestFor(records, courseCode, carId, tuneSet, specOnly, new TimeKind { Laps = 1 });

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
