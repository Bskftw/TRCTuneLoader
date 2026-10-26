using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TRC.TuneLoader {
	/// <summary>
	/// Every finished run, lap, battle and race, ready for the TRC server: one JSON object per line in
	/// TimeAttack/server/events.jsonl, in a fixed field order. Each line carries the hash of the line before
	/// ("prev") and its own ("hash", SHA-256 of the line without it), so the lines form a chain: once the server
	/// has a player's chain up to some line, changing or dropping any earlier line breaks it. That makes editing
	/// old results show; it doesn't prove a result is real (the server still checks times, codes and courses).
	/// </summary>
	public sealed class EventLedger {
		public const int Schema = 1;
		public const string FolderName = "server";
		public const string FileName = "events.jsonl";

		private static readonly Regex HashTail = new Regex("^(?<body>.*),\"hash\":\"(?<hash>[0-9a-f]{64})\"\\}$", RegexOptions.Compiled);
		private static readonly Regex PrevField = new Regex("\"prev\":\"(?<prev>[0-9a-f]{64}|)\"", RegexOptions.Compiled);
		private static readonly Regex SeqField = new Regex("^\\{\"v\":\\d+,\"seq\":(?<seq>\\d+),", RegexOptions.Compiled);

		private readonly string path_;
		private readonly Random random_ = new Random();
		public string Head { get; private set; } = "";
		public long Count { get; private set; }

		public string FilePath => path_;

		/// <summary>The ledger in <paramref name="timesFolder"/>/server, picking up where the file ends.</summary>
		public EventLedger(string timesFolder) {
			path_ = Path.Combine(timesFolder, FolderName, FileName);
			if (!File.Exists(path_)) {
				return;
			}
			string last = File.ReadLines(path_, Encoding.UTF8).LastOrDefault(l => l.Trim().Length > 0);
			Match m = last == null ? Match.Empty : HashTail.Match(last.Trim());
			Match seq = last == null ? Match.Empty : SeqField.Match(last.Trim());
			if (m.Success && seq.Success) {
				Head = m.Groups["hash"].Value;
				Count = long.Parse(seq.Groups["seq"].Value, CultureInfo.InvariantCulture);
			}
		}

		/// <summary>A field of an event: a name and a string, whole number, number, yes/no or list of numbers.</summary>
		public struct Field {
			public string Name;
			public object Value;

			public Field(string name, object value) {
				Name = name;
				Value = value;
			}
		}

		/// <summary>Adds one event (its type and fields, after the common ones) and returns the line written.</summary>
		public string Append(string type, ulong steamId, string version, DateTime utc, IEnumerable<Field> fields) {
			string id = Id();
			string line = Line(Count + 1, id, type, steamId, version, utc, fields, Head);
			Directory.CreateDirectory(Path.GetDirectoryName(path_) ?? ".");
			File.AppendAllText(path_, line + "\n", new UTF8Encoding(false));
			Count++;
			Head = HashTail.Match(line).Groups["hash"].Value;
			return line;
		}

		/// <summary>The whole line for an event: the common fields, the event's own, "prev", then "hash".</summary>
		public static string Line(long seq, string id, string type, ulong steamId, string version, DateTime utc, IEnumerable<Field> fields, string prev) {
			var body = new StringBuilder();
			body.Append("{\"v\":").Append(Schema.ToString(CultureInfo.InvariantCulture));
			body.Append(",\"seq\":").Append(seq.ToString(CultureInfo.InvariantCulture));
			body.Append(",\"id\":").Append(Quote(id));
			body.Append(",\"type\":").Append(Quote(type));
			body.Append(",\"at\":").Append(Quote(utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)));
			body.Append(",\"steam\":").Append(Quote(steamId.ToString(CultureInfo.InvariantCulture)));
			body.Append(",\"mod\":").Append(Quote(version));
			foreach (Field f in fields) {
				body.Append(',').Append(Quote(f.Name)).Append(':').Append(Value(f.Value));
			}
			body.Append(",\"prev\":").Append(Quote(prev ?? ""));
			string canonical = body.ToString();
			return $"{canonical},\"hash\":\"{Sha256(canonical)}\"}}";
		}

		/// <summary>Checks a chain of lines: each hash, each prev, and seq going up by one. Returns "" or what's wrong (with the line number).</summary>
		public static string Verify(IEnumerable<string> lines) {
			string prev = null;
			long seq = -1;
			int n = 0;
			foreach (string raw in lines) {
				n++;
				string line = raw.Trim();
				if (line.Length == 0) {
					continue;
				}
				Match m = HashTail.Match(line);
				if (!m.Success) {
					return $"line {n}: no hash";
				}
				string body = m.Groups["body"].Value;
				if (Sha256(body) != m.Groups["hash"].Value) {
					return $"line {n}: changed (hash doesn't match)";
				}
				Match p = PrevField.Match(body);
				Match s = SeqField.Match(body);
				if (!p.Success || !s.Success) {
					return $"line {n}: missing prev or seq";
				}
				long here = long.Parse(s.Groups["seq"].Value, CultureInfo.InvariantCulture);
				if (prev != null && (p.Groups["prev"].Value != prev || here != seq + 1)) {
					return $"line {n}: chain broken (a line before it was changed or removed)";
				}
				prev = m.Groups["hash"].Value;
				seq = here;
			}
			return "";
		}

		// ---- Events ----

		/// <summary>A time attack time (a whole run, or one lap of a circuit).</summary>
		public static List<Field> RunFields(TimeAttackRecord r) => new List<Field> {
			new Field("course", r.CourseCode),
			new Field("course_name", r.CourseName),
			new Field("map", r.MapKey),
			new Field("official_course", r.OfficialCourse),
			new Field("car", (long)r.CarId),
			new Field("car_name", r.Car),
			new Field("tune", r.TuneCode),
			new Field("tune_set", r.TuneSet),
			new Field("spec", r.IsSpec),
			new Field("flagged_mods", (long)r.FlaggedMods),
			new Field("start", r.Flying ? "flying" : "standing"),
			new Field("laps", (long)r.Laps),
			new Field("lap", (long)r.LapNumber),
			new Field("time_ms", r.TimeMs),
			new Field("sectors_ms", r.SectorMs),
			new Field("traps_kmh", r.TrapKmh),
		};

		/// <summary>A 1v1 battle that got as far as lining up.</summary>
		public static List<Field> BattleFields(BattleSession b, string myCar, string mySet) => new List<Field> {
			new Field("battle", (long)b.Id),
			new Field("course", b.IsHighway ? "" : b.Course.Code),
			new Field("map", b.Course.MapKey),
			new Field("format", b.Format == BattleFormat.CatAndMouse ? "cat_and_mouse" : b.Format == BattleFormat.Highway ? "highway" : "race"),
			new Field("open", b.Open),
			new Field("counted", b.Counted && b.Winner != 0),
			new Field("host", b.IsHost),
			new Field("opponent", b.Peer.ToString(CultureInfo.InvariantCulture)),
			new Field("result", BattleLog.ResultOf(b)),
			new Field("runs", (long)b.Runs.Count),
			new Field("my_times_ms", b.Runs.Select(r => r.Mine != null && r.Mine.Finished ? (long)Math.Round(r.Mine.Time * 1000) : -1L).ToArray()),
			new Field("their_times_ms", b.Runs.Select(r => r.Theirs != null && r.Theirs.Finished ? (long)Math.Round(r.Theirs.Time * 1000) : -1L).ToArray()),
			new Field("car_name", myCar),
			new Field("tune_set", mySet),
		};

		/// <summary>A race or room game this player was in, with everyone's place (the server can cross-check every driver's line).</summary>
		public static List<Field> RaceFields(RaceSession race, RaceResultRow mine) => new List<Field> {
			new Field("race", (long)race.Id),
			new Field("race_number", (long)race.RaceNumber),
			new Field("host_steam", race.HostId.ToString(CultureInfo.InvariantCulture)),
			new Field("course", race.CourseCode),
			new Field("mode", race.Settings.Mode.ToString()),
			new Field("open", race.Settings.Open),
			new Field("laps", (long)race.Settings.Laps),
			new Field("position", (long)mine.Position),
			new Field("drivers", (long)race.Results.Count),
			new Field("state", mine.State.ToString()),
			new Field("time_ms", mine.State == RacerState.Finished ? (long)Math.Round(mine.Time * 1000) : -1L),
			new Field("penalty_s", (double)mine.Penalty),
			new Field("points", (long)mine.Points),
			new Field("order", race.Results.Select(r => r.Id.ToString(CultureInfo.InvariantCulture)).ToArray()),
		};

		// ---- JSON ----

		private static string Value(object value) {
			switch (value) {
				case null: return "null";
				case string s: return Quote(s);
				case bool b: return b ? "true" : "false";
				case int i: return i.ToString(CultureInfo.InvariantCulture);
				case long l: return l.ToString(CultureInfo.InvariantCulture);
				case float f: return Number(f);
				case double d: return Number(d);
				case long[] ls: return "[" + string.Join(",", ls.Select(x => x.ToString(CultureInfo.InvariantCulture))) + "]";
				case float[] fs: return "[" + string.Join(",", fs.Select(x => Number(x))) + "]";
				case string[] ss: return "[" + string.Join(",", ss.Select(Quote)) + "]";
				default: return Quote(value.ToString());
			}
		}

		private static string Number(double d) =>
			double.IsNaN(d) || double.IsInfinity(d) ? "null" : Math.Round(d, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture);

		public static string Quote(string s) {
			var b = new StringBuilder("\"");
			foreach (char c in s ?? "") {
				switch (c) {
					case '"': b.Append("\\\""); break;
					case '\\': b.Append("\\\\"); break;
					case '\n': b.Append("\\n"); break;
					case '\r': b.Append("\\r"); break;
					case '\t': b.Append("\\t"); break;
					default:
						if (c < 0x20) {
							b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
						} else {
							b.Append(c);
						}
						break;
				}
			}
			return b.Append('"').ToString();
		}

		public static string Sha256(string text) {
			using (SHA256 sha = SHA256.Create()) {
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
				var b = new StringBuilder(64);
				foreach (byte x in hash) {
					b.Append(x.ToString("x2", CultureInfo.InvariantCulture));
				}
				return b.ToString();
			}
		}

		private string Id() {
			var bytes = new byte[8];
			random_.NextBytes(bytes);
			return string.Concat(bytes.Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
		}
	}
}
