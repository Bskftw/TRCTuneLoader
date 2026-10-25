using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Reads and writes .trccourse files: plain text, like .trctune files, so a course can be looked at
	/// and shared. The placement tool writes them; official ones are built into the mod.
	///
	///   # comments start with #
	///   [info]
	///   name     = Akina Downhill
	///   kind     = touge              (touge, highway or circuit)
	///   map      = workshop:123456    (the map's key: see MapInfo)
	///   map_name = Project Dream
	///   author   = bskftw
	///   code     = CRS-1A2B-3C4D      (written for reference; the mod works it out again)
	///
	///   [start]
	///   position  = 12.3, 45.6, -78.9 (metres, in the map's own coordinates)
	///   direction = 0.6, -0.8         (the way you drive through the line, x and z)
	///   width     = 12                (metres)
	///
	///   [finish]
	///   ... same as [start] (not for a circuit: its finish line is its start line)
	///
	///   [path]
	///   12.3, 45.6, -78.9             (points along the middle of the road, start to finish; a circuit's go
	///                                  once round, back to the start line)
	///   ...
	///
	///   [traps]                       (optional: speed traps along the course, in order)
	///   12.3, 45.6, -78.9, 0.6, -0.8, 12   (position, direction x and z, width)
	///
	///   [grid]                        (optional: where race grids line up)
	///   layout = grid                 (grid: two-wide, staggered; lineup: one behind the other)
	///   12.3, 45.6, -78.9             (points down the middle of the road, from the start line backwards)
	///   ...
	/// </summary>
	public static class CourseFile {
		public const string Extension = ".trccourse";

		public static Course Load(string path) {
			Course course = Parse(File.ReadAllText(path), CourseSource.Custom);
			course.FilePath = path;
			if (string.IsNullOrEmpty(course.Name)) {
				course.Name = Path.GetFileNameWithoutExtension(path);
			}
			return course;
		}

		/// <summary>Reads a course from its text and builds it (path, length and code).</summary>
		public static Course Parse(string text, CourseSource source) {
			var course = new Course { Source = source };
			var path = new List<Vector3>();
			var grid = new List<Vector3>();
			bool startSet = false, finishSet = false;
			string section = "";
			string written = "";
			int lineNumber = 0;

			foreach (string rawLine in (text ?? "").Split('\n')) {
				lineNumber++;
				int hash = rawLine.IndexOf('#');
				string line = TuneRules.Trim(hash >= 0 ? rawLine.Substring(0, hash) : rawLine);
				if (line.Length == 0) {
					continue;
				}

				if (line[0] == '[' && line[line.Length - 1] == ']') {
					section = TuneRules.Key(line.Substring(1, line.Length - 2));
					continue;
				}

				if (section == "traps") {
					string[] parts = line.Split(',');
					float[] n = parts.Select(p => TuneRules.TryParseValue(TuneRules.Trim(p), out float v) ? v : float.NaN).ToArray();
					if (n.Length == 6 && n.All(x => !float.IsNaN(x)) && CourseGate.TryMake(new Vector3(n[0], n[1], n[2]), new Vector3(n[3], 0f, n[4]), n[5], out CourseGate trap)) {
						course.Traps.Add(trap);
					} else {
						course.Problems.Add($"Line {lineNumber}: a speed trap should be six numbers: position, direction (x and z) and width");
					}
					continue;
				}
				if (section == "grid" && line.IndexOf('=') < 0) {
					if (TryParseVector(line, 3, out Vector3 point)) {
						grid.Add(point);
					} else {
						course.Problems.Add($"Line {lineNumber}: a grid point should be three numbers, like 12.3, 45.6, -78.9");
					}
					continue;
				}
				if (section == "path") {
					if (TryParseVector(line, 3, out Vector3 point)) {
						path.Add(point);
					} else {
						course.Problems.Add($"Line {lineNumber}: a path point should be three numbers, like 12.3, 45.6, -78.9");
					}
					continue;
				}

				int eq = line.IndexOf('=');
				if (eq <= 0) {
					course.Problems.Add($"Line {lineNumber}: expected 'name = value'");
					continue;
				}
				string key = TuneRules.Key(line.Substring(0, eq));
				string value = TuneRules.Trim(line.Substring(eq + 1));

				switch (section) {
					case "info":
						switch (key) {
							case "name": course.Name = value; break;
							case "kind": course.Kind = ParseKind(value); break;
							case "map": course.MapKey = value; break;
							case "map_name": course.MapName = value; break;
							case "author": course.Author = value; break;
							case "code": written = value; break;
							default: course.Problems.Add($"Line {lineNumber}: unknown info field '{key}'"); break;
						}
						break;
					case "start":
						startSet |= SetGate(course.Start, key, value, lineNumber, course.Problems);
						break;
					case "finish":
						finishSet |= SetGate(course.Finish, key, value, lineNumber, course.Problems);
						break;
					case "grid":
						if (key == "layout") {
							course.GridLayout = TuneRules.Key(value) == "lineup" ? GridLayout.LineUp : GridLayout.Grid;
						} else {
							course.Problems.Add($"Line {lineNumber}: unknown grid field '{key}'");
						}
						break;
					default:
						course.Problems.Add($"Line {lineNumber}: '{key}' is outside an [info], [start], [finish] or [grid] section");
						break;
				}
			}

			if (!startSet) {
				course.Problems.Add("No [start] position");
			}
			if (!finishSet && !course.IsCircuit) {
				course.Problems.Add("No [finish] position");
			}
			if (string.IsNullOrEmpty(course.MapKey)) {
				course.Problems.Add("No map in [info]");
			}

			course.Path = path;
			course.HasPath = path.Count >= 2;
			course.GridPath = grid.Count >= 2 ? grid : new List<Vector3>();
			course.Build(resample: false);

			if (written.Length > 0 && !string.Equals(written, course.Code, StringComparison.OrdinalIgnoreCase)) {
				course.Problems.Add($"The file says code {written}, but its lines and path give {course.Code}: it was changed after it was made.");
			}
			return course;
		}

		public static string ToText(Course course) {
			var text = new StringBuilder();
			text.Append("# TRC course, written by the TRC Tune Loader.\n");
			text.Append("# Positions are in metres, in the map's own coordinates. Direction is the way you drive through the line.\n\n");

			text.Append("[info]\n");
			AppendField(text, "name", course.Name);
			AppendField(text, "kind", Course.KindWord(course.Kind));
			AppendField(text, "map", course.MapKey);
			AppendField(text, "map_name", course.MapName);
			AppendField(text, "author", course.Author);
			AppendField(text, "code", course.Code);

			AppendGate(text, "start", course.Start);
			if (!course.IsCircuit) {
				AppendGate(text, "finish", course.Finish);
			}

			if (course.HasPath) {
				text.Append("\n[path]\n");
				foreach (Vector3 point in course.Path) {
					text.Append(Number(point.x, 2)).Append(", ").Append(Number(point.y, 2)).Append(", ").Append(Number(point.z, 2)).Append('\n');
				}
			}
			if (course.Traps.Count > 0) {
				text.Append("\n[traps]\n");
				foreach (CourseGate t in course.Traps) {
					text.Append(Number(t.Center.x, 2)).Append(", ").Append(Number(t.Center.y, 2)).Append(", ").Append(Number(t.Center.z, 2)).Append(", ")
						.Append(Number(t.Forward.x, 4)).Append(", ").Append(Number(t.Forward.z, 4)).Append(", ").Append(Number(t.Width, 1)).Append('\n');
				}
			}
			if (course.GridPath.Count >= 2) {
				text.Append("\n[grid]\n");
				text.Append("layout = ").Append(course.GridLayout == GridLayout.LineUp ? "lineup" : "grid").Append('\n');
				foreach (Vector3 point in course.GridPath) {
					text.Append(Number(point.x, 2)).Append(", ").Append(Number(point.y, 2)).Append(", ").Append(Number(point.z, 2)).Append('\n');
				}
			}
			return text.ToString();
		}

		public static void Save(Course course, string path) {
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, ToText(course), new UTF8Encoding(false));
			course.FilePath = path;
		}

		private static CourseKind ParseKind(string value) {
			switch (TuneRules.Key(value)) {
				case "highway": return CourseKind.Highway;
				case "circuit": return CourseKind.Circuit;
				default: return CourseKind.Touge;
			}
		}

		private static bool SetGate(CourseGate gate, string key, string value, int lineNumber, List<string> problems) {
			switch (key) {
				case "position":
					if (TryParseVector(value, 3, out Vector3 position)) {
						gate.Center = position;
						return true;
					}
					problems.Add($"Line {lineNumber}: position should be three numbers, like 12.3, 45.6, -78.9");
					return false;
				case "direction":
					if (TryParseVector(value, 2, out Vector3 direction) && CourseGateDirection(direction, out Vector3 forward)) {
						gate.Forward = forward;
					} else {
						problems.Add($"Line {lineNumber}: direction should be two numbers (x and z), like 0.6, -0.8");
					}
					return false;
				case "width":
					if (TuneRules.TryParseValue(value, out float width) && width > 0.5f) {
						gate.Width = width;
					} else {
						problems.Add($"Line {lineNumber}: width should be a number of metres");
					}
					return false;
				default:
					problems.Add($"Line {lineNumber}: unknown field '{key}'");
					return false;
			}
		}

		/// <summary>The direction as written (x and z), not made exactly one metre long again, so the code stays the same.</summary>
		private static bool CourseGateDirection(Vector3 xz, out Vector3 forward) {
			forward = new Vector3(xz.x, 0f, xz.y);
			return forward.magnitude > 0.5f && forward.magnitude < 1.5f;
		}

		/// <summary>Two or three comma-separated numbers. With two, they're x and z, returned as (x, z, 0).</summary>
		private static bool TryParseVector(string text, int count, out Vector3 vector) {
			vector = Vector3.zero;
			string[] parts = text.Split(',');
			if (parts.Length != count) {
				return false;
			}
			var numbers = new float[count];
			for (int i = 0; i < count; i++) {
				if (!TuneRules.TryParseValue(TuneRules.Trim(parts[i]), out numbers[i])) {
					return false;
				}
			}
			vector = count == 3 ? new Vector3(numbers[0], numbers[1], numbers[2]) : new Vector3(numbers[0], numbers[1], 0f);
			return true;
		}

		private static void AppendGate(StringBuilder text, string name, CourseGate gate) {
			text.Append('\n').Append('[').Append(name).Append("]\n");
			text.Append("position  = ").Append(Number(gate.Center.x, 2)).Append(", ").Append(Number(gate.Center.y, 2)).Append(", ").Append(Number(gate.Center.z, 2)).Append('\n');
			text.Append("direction = ").Append(Number(gate.Forward.x, 4)).Append(", ").Append(Number(gate.Forward.z, 4)).Append('\n');
			text.Append("width     = ").Append(Number(gate.Width, 1)).Append('\n');
		}

		private static void AppendField(StringBuilder text, string key, string value) {
			if (!string.IsNullOrEmpty(value)) {
				// '#' would start a comment and a line break would end the line, so neither can be kept.
				string clean = value.Replace("#", "").Replace("\r", " ").Replace("\n", " ").Trim();
				text.Append($"{key,-8} = {clean}\n");
			}
		}

		private static string Number(float value, int decimals) => CourseCode.Round(value, decimals);
	}
}
