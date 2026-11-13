// Saves and loads manual sectors for courses the player can't edit (sectors.txt).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Manual sectors for courses the player can't edit (the map's and the official ones), by course code, in
	/// sectors.txt next to the courses folder. The player's own courses keep theirs in the .trccourse file's [sectors].
	///
	///   CRS-1A2B-3C4D = 12.3, 45.6, -78.9; 100.2, 40.1, -60.0
	///
	/// A sectors code (<see cref="ToCode"/>) carries one course's manual sectors to another player.
	/// </summary>
	public static class SectorOverlay {
		public const string FileName = "sectors.txt";
		private const string CodePrefix = "TRCSEC";

		private static readonly Dictionary<string, List<Vector3>> byCode_ = new Dictionary<string, List<Vector3>>(StringComparer.OrdinalIgnoreCase);
		private static string path_ = "";

		/// <summary>This course's manual sectors (a copy), or an empty list.</summary>
		public static List<Vector3> For(string code) =>
			code != null && byCode_.TryGetValue(code, out List<Vector3> points) ? new List<Vector3>(points) : new List<Vector3>();

		public static void Load(string path) {
			path_ = path;
			byCode_.Clear();
			if (!File.Exists(path)) {
				return;
			}
			foreach (string raw in File.ReadAllLines(path)) {
				string line = raw.Trim();
				int eq = line.IndexOf('=');
				if (line.Length == 0 || line[0] == '#' || eq <= 0) {
					continue;
				}
				List<Vector3> points = ParsePoints(line.Substring(eq + 1));
				if (points.Count > 0) {
					byCode_[line.Substring(0, eq).Trim()] = points;
				}
			}
		}

		/// <summary>Sets (or, with none, removes) a course's manual sectors and writes the file.</summary>
		public static void Set(string code, IList<Vector3> points) {
			if (points == null || points.Count == 0) {
				byCode_.Remove(code);
			} else {
				byCode_[code] = new List<Vector3>(points);
			}
			if (path_.Length == 0) {
				return;
			}
			var text = new StringBuilder("# Manual sectors for map and official courses, by course code (TRC Tune Loader).\n");
			foreach (var pair in byCode_.OrderBy(p => p.Key, StringComparer.Ordinal)) {
				text.Append(pair.Key).Append(" = ").Append(PointsText(pair.Value)).Append('\n');
			}
			Directory.CreateDirectory(Path.GetDirectoryName(path_));
			File.WriteAllText(path_, text.ToString(), new UTF8Encoding(false));
		}

		/// <summary>"TRCSEC:CRS-1A2B-3C4D:12.3,45.6,-78.9;...": one course's manual sectors, to copy and paste.</summary>
		public static string ToCode(string courseCode, IList<Vector3> points) =>
			$"{CodePrefix}:{courseCode}:{PointsText(points).Replace(" ", "")}";

		/// <summary>Reads a sectors code; false when it isn't one.</summary>
		public static bool TryParseCode(string text, out string courseCode, out List<Vector3> points) {
			courseCode = "";
			points = new List<Vector3>();
			string[] parts = (text ?? "").Trim().Split(new[] { ':' }, 3);
			if (parts.Length != 3 || !string.Equals(parts[0], CodePrefix, StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			courseCode = parts[1].Trim();
			points = ParsePoints(parts[2]);
			return courseCode.Length > 0 && points.Count > 0;
		}

		internal static string PointsText(IEnumerable<Vector3> points) =>
			string.Join("; ", points.Select(p => $"{CourseCode.Round(p.x, 2)}, {CourseCode.Round(p.y, 2)}, {CourseCode.Round(p.z, 2)}"));

		internal static List<Vector3> ParsePoints(string text) {
			var points = new List<Vector3>();
			foreach (string piece in (text ?? "").Split(';')) {
				string[] n = piece.Split(',');
				if (n.Length == 3 &&
					float.TryParse(n[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
					float.TryParse(n[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
					float.TryParse(n[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
					points.Add(new Vector3(x, y, z));
				}
			}
			return points;
		}
	}
}
