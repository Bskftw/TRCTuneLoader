// Reads and writes the player's own free-roam speed traps for a map (speedtraps.trctraps).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
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
}
