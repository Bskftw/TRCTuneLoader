using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// A speed trap: a line across the road where the car's speed is measured as it goes through, like the speed
	/// cameras in Need for Speed. Free-roam traps stand on their own (map objects "TRC Speed Trap &lt;name&gt;", or
	/// placed by the player); courses can have their own along the way (Course.Traps), counted in a run.
	/// </summary>
	public sealed class SpeedTrap {
		public string Name = "";
		public CourseGate Gate = new CourseGate();
		public string MapKey = "";
		public CourseSource Source;

		/// <summary>The same everywhere for the same trap: the map and where it is (to the metre).</summary>
		public string Key => $"{MapKey}@{Mathf.RoundToInt(Gate.Center.x)},{Mathf.RoundToInt(Gate.Center.y)},{Mathf.RoundToInt(Gate.Center.z)}";
	}

	/// <summary>A car going through a speed trap.</summary>
	public struct SpeedTrapHit {
		public SpeedTrap Trap;
		public float Kmh;
	}

	/// <summary>
	/// Watches the car in free roam: going through a trap's line (either way, between its ends, not on a road above
	/// or below) measures the speed at that moment, worked out between frames. A trap counts again after a few seconds.
	/// </summary>
	public sealed class SpeedTrapWatcher {
		private const float NearMetres = 80f;
		private const double AgainSeconds = 4;
		private const float JumpMetres = 40f;

		private Vector3 last_;
		private float lastSpeed_;
		private bool hasLast_;
		private readonly Dictionary<string, double> lastHit_ = new Dictionary<string, double>(StringComparer.Ordinal);

		public void Reset() {
			hasLast_ = false;
		}

		/// <summary>Called every frame; returns the trap the car just went through, if any.</summary>
		public SpeedTrapHit? Tick(double now, Vector3 position, float speedKmh, IList<SpeedTrap> traps) {
			speedKmh = Mathf.Abs(speedKmh);
			SpeedTrapHit? hit = null;
			if (hasLast_ && Vector3.Distance(last_, position) < JumpMetres && traps != null) {
				foreach (SpeedTrap trap in traps) {
					if ((trap.Gate.Center - position).sqrMagnitude > NearMetres * NearMetres) {
						continue;
					}
					if (!CrossedEitherWay(trap.Gate, last_, position, out float fraction)) {
						continue;
					}
					if (lastHit_.TryGetValue(trap.Key, out double at) && now - at < AgainSeconds) {
						continue;
					}
					lastHit_[trap.Key] = now;
					hit = new SpeedTrapHit { Trap = trap, Kmh = Mathf.Lerp(lastSpeed_, speedKmh, fraction) };
					break;
				}
			}
			last_ = position;
			lastSpeed_ = speedKmh;
			hasLast_ = true;
			return hit;
		}

		public static bool CrossedEitherWay(CourseGate gate, Vector3 from, Vector3 to, out float fraction) {
			if (CourseGeometry.Crossed(gate, from, to, out fraction)) {
				return true;
			}
			var back = new CourseGate { Center = gate.Center, Forward = -gate.Forward, Width = gate.Width };
			return CourseGeometry.Crossed(back, from, to, out fraction);
		}
	}

	/// <summary>
	/// The player's own free-roam speed traps for a map, in a small text file next to their courses
	/// (Courses/&lt;map&gt;/speedtraps.trctraps), one [trap] section each, like a .trccourse.
	/// </summary>
	public static class SpeedTrapFile {
		public const string FileName = "speedtraps.trctraps";

		public static List<SpeedTrap> Load(string path, string mapKey) {
			var traps = new List<SpeedTrap>();
			if (!File.Exists(path)) {
				return traps;
			}
			SpeedTrap current = null;
			foreach (string raw in File.ReadAllLines(path, Encoding.UTF8)) {
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == '#') {
					continue;
				}
				if (line == "[trap]") {
					traps.Add(current = new SpeedTrap { MapKey = mapKey, Source = CourseSource.Custom });
					continue;
				}
				int eq = line.IndexOf('=');
				if (current == null || eq <= 0) {
					continue;
				}
				string key = line.Substring(0, eq).Trim().ToLowerInvariant();
				string value = line.Substring(eq + 1).Trim();
				float[] n = value.Split(',').Select(v => float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : float.NaN).ToArray();
				switch (key) {
					case "name": current.Name = value; break;
					case "position": if (n.Length == 3 && n.All(x => !float.IsNaN(x))) { current.Gate.Center = new Vector3(n[0], n[1], n[2]); } break;
					case "direction": if (n.Length == 2 && n.All(x => !float.IsNaN(x))) { current.Gate.Forward = CourseGeometry.Level(new Vector3(n[0], 0f, n[1])); } break;
					case "width": if (n.Length == 1 && n[0] > 0.5f) { current.Gate.Width = n[0]; } break;
				}
			}
			traps.RemoveAll(t => t.Gate.Forward == Vector3.zero);
			return traps;
		}

		public static void Save(string path, IEnumerable<SpeedTrap> traps) {
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			var text = new StringBuilder("# TRC speed traps, written by the TRC Tune Loader.\n");
			foreach (SpeedTrap t in traps) {
				text.Append("\n[trap]\n");
				text.Append("name      = ").Append(t.Name.Replace("\n", " ")).Append('\n');
				text.Append("position  = ").Append(CourseCode.Round(t.Gate.Center.x, 2)).Append(", ").Append(CourseCode.Round(t.Gate.Center.y, 2)).Append(", ").Append(CourseCode.Round(t.Gate.Center.z, 2)).Append('\n');
				text.Append("direction = ").Append(CourseCode.Round(t.Gate.Forward.x, 4)).Append(", ").Append(CourseCode.Round(t.Gate.Forward.z, 4)).Append('\n');
				text.Append("width     = ").Append(CourseCode.Round(t.Gate.Width, 1)).Append('\n');
			}
			File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
		}
	}

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
	}

	/// <summary>Every speed caught by a trap, one line each, only ever added to.</summary>
	public static class SpeedTrapLog {
		public const string FileName = "speedtraps.csv";
		private const string Header = "date,trap_key,trap,map,kmh,car_id,car,tune_set,spec,context";

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
				r.Spec ? "spec" : "no", Q(r.Context),
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
