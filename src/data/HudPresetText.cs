// HUD presets saved as text: each a name and its settings (key=value), all of them in one saved option. Game-free (self-tested).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// HUD presets (docs/archive/IMPROVEMENT_PLAN.md U4: "save the whole HUD layout and options under a name"). One line per preset:
	/// the name, a tab, then <c>key=value</c> pairs split by <c>;</c>. The keys and values are the mod's own (numbers,
	/// on/off, places as <c>x,y</c>), so only the name needs cleaning. A damaged line is skipped, not the whole list.
	/// </summary>
	public static class HudPresetText {
		public const int MostPresets = 12;
		public const int MostNameLength = 30;

		public sealed class Preset {
			public string Name = "";
			public Dictionary<string, string> Values = new Dictionary<string, string>();
		}

		/// <summary>A name as it can be saved: no tabs or line breaks, trimmed, at most <see cref="MostNameLength"/> letters.</summary>
		public static string CleanName(string name) {
			if (name == null) {
				return "";
			}
			var sb = new StringBuilder(name.Length);
			foreach (char c in name) {
				sb.Append(char.IsControl(c) ? ' ' : c);
			}
			string clean = sb.ToString().Trim();
			return clean.Length > MostNameLength ? clean.Substring(0, MostNameLength).TrimEnd() : clean;
		}

		public static string Save(IEnumerable<Preset> presets) {
			var sb = new StringBuilder();
			int n = 0;
			foreach (Preset p in presets) {
				string name = CleanName(p.Name);
				if (name.Length == 0 || n >= MostPresets) {
					continue;
				}
				if (sb.Length > 0) {
					sb.Append('\n');
				}
				sb.Append(name).Append('\t');
				bool first = true;
				foreach (var pair in p.Values) {
					if (!GoodPart(pair.Key) || !GoodPart(pair.Value)) {
						continue;
					}
					if (!first) {
						sb.Append(';');
					}
					sb.Append(pair.Key).Append('=').Append(pair.Value);
					first = false;
				}
				n++;
			}
			return sb.ToString();
		}

		/// <summary>The presets in a saved option: damaged lines skipped, a name seen twice kept once (the first), at most <see cref="MostPresets"/>.</summary>
		public static List<Preset> Parse(string text) {
			var presets = new List<Preset>();
			if (string.IsNullOrEmpty(text)) {
				return presets;
			}
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string line in text.Split('\n')) {
				int tab = line.IndexOf('\t');
				if (tab <= 0) {
					continue;
				}
				string name = CleanName(line.Substring(0, tab));
				if (name.Length == 0 || !names.Add(name)) {
					continue;
				}
				var preset = new Preset { Name = name };
				foreach (string part in line.Substring(tab + 1).Split(';')) {
					int eq = part.IndexOf('=');
					if (eq <= 0) {
						continue;
					}
					preset.Values[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
				}
				presets.Add(preset);
				if (presets.Count >= MostPresets) {
					break;
				}
			}
			return presets;
		}

		private static bool GoodPart(string s) => !string.IsNullOrEmpty(s) && s.IndexOfAny(new[] { '\t', '\n', '\r', ';', '=' }) < 0;

		public static string Place(Vector2 v) => v.x.ToString("R", CultureInfo.InvariantCulture) + "," + v.y.ToString("R", CultureInfo.InvariantCulture);

		public static bool TryPlace(string s, out Vector2 v) {
			v = Vector2.zero;
			if (string.IsNullOrEmpty(s)) {
				return false;
			}
			string[] xy = s.Split(',');
			if (xy.Length != 2 || !float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
				!float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
				float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y) || Math.Abs(x) > 100000f || Math.Abs(y) > 100000f) {
				return false;
			}
			v = new Vector2(x, y);
			return true;
		}

		public static string OnOff(bool on) => on ? "1" : "0";

		public static bool TryOnOff(string s, out bool on) {
			on = s == "1";
			return s == "1" || s == "0";
		}

		public static bool TryNumber(string s, out float f) =>
			float.TryParse(s ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out f) && !float.IsNaN(f) && !float.IsInfinity(f);
	}
}
