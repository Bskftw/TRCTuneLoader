using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// The best run (or, on a circuit, the best lap) as a time every 10 m along the course, for the live delta:
	/// one small text file per course, car, TRC tune set and kind of time, in TimeAttack/traces. A faster time
	/// replaces it. Only this PC reads them.
	/// </summary>
	public static class TimeAttackTraces {
		public const string FolderName = "traces";
		private const string Extension = ".trace";

		/// <summary>The file name for these: safe on Windows, and the same every time for the same course, car, set and kind.</summary>
		public static string Key(string courseCode, int carId, string tuneSet, TimeKind kind) {
			string set = new string((tuneSet ?? "").Where(char.IsLetterOrDigit).ToArray());
			string what = kind.Lap ? "lap" : $"{(kind.Flying ? "flying" : "standing")}{kind.Laps}";
			return $"{courseCode}_{carId.ToString(CultureInfo.InvariantCulture)}_{(set.Length > 0 ? set : "none")}_{what}";
		}

		/// <summary>The saved trace and its time (milliseconds), or null.</summary>
		public static (float[] Trace, long TimeMs)? Load(string timesFolder, string key) {
			try {
				string path = Path.Combine(timesFolder, FolderName, key + Extension);
				if (!File.Exists(path)) {
					return null;
				}
				string[] lines = File.ReadAllLines(path, Encoding.UTF8);
				if (lines.Length < 3 || !long.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out long time)) {
					return null;
				}
				var trace = new List<float>(lines.Length - 1);
				for (int i = 1; i < lines.Length; i++) {
					if (lines[i].Length == 0) {
						continue;
					}
					if (!long.TryParse(lines[i], NumberStyles.None, CultureInfo.InvariantCulture, out long ms)) {
						return null;
					}
					trace.Add(ms / 1000f);
				}
				return (trace.ToArray(), time);
			} catch (Exception) {
				return null;
			}
		}

		/// <summary>Keeps <paramref name="trace"/> when it's faster than the saved one (or there's none). True when it was saved.</summary>
		public static bool SaveIfFaster(string timesFolder, string key, float[] trace, long timeMs) {
			if (trace == null || trace.Length < 2) {
				return false;
			}
			var saved = Load(timesFolder, key);
			if (saved != null && saved.Value.TimeMs <= timeMs) {
				return false;
			}
			string folder = Path.Combine(timesFolder, FolderName);
			Directory.CreateDirectory(folder);
			var text = new StringBuilder();
			text.Append(timeMs.ToString(CultureInfo.InvariantCulture)).Append('\n');
			foreach (float seconds in trace) {
				text.Append(((long)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)).Append('\n');
			}
			File.WriteAllText(Path.Combine(folder, key + Extension), text.ToString(), new UTF8Encoding(false));
			return true;
		}
	}
}
