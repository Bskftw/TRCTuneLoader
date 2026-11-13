// Reading the map's object names: the TRC word, the course or road name, numbers and copy numbers.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public static partial class CourseScanner {
		/// <summary>
		/// Finds "trc" and the word after it: start, finish, path, or the road ("mainroad", "main road" or "road").
		/// <paramref name="rest"/> is where the course or road name starts. "TRC" doesn't have to be the first word.
		/// </summary>
		private static bool TryParse(string[] words, out string role, out int rest) {
			role = "";
			rest = 0;
			int trc = Array.IndexOf(words, "trc");
			if (trc < 0 || trc + 2 >= words.Length) {
				return false;
			}
			string next = words[trc + 1];
			if (next == StartWord || next == FinishWord || next == PathWord) {
				role = next;
				rest = trc + 2;
			} else if (next == RoadWord || next == "road" || next == "mainroads") {
				role = RoadWord;
				rest = trc + 2;
			} else if (next == "main" && words[trc + 2] == "road" && trc + 3 < words.Length) {
				role = RoadWord;
				rest = trc + 3;
			} else {
				return false;
			}
			return true;
		}

		/// <summary>"trc akina downhill sector 2" → course "akina downhill", sector 2 (the same for "grid 2").</summary>
		private static bool TryParseNumbered(string[] words, string word, out string key, out int number) {
			key = "";
			number = 0;
			int trc = Array.IndexOf(words, "trc");
			int last = words.Length - 1;
			if (trc < 0 || last - trc < 3 || words[last - 1] != word ||
				!int.TryParse(words[last], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)) {
				return false;
			}
			key = string.Join(" ", words.Skip(trc + 1).Take(last - 1 - (trc + 1)));
			return key.Length > 0;
		}

		/// <summary>"trc mainroad akina.001" → "akina": the road's name without the number Unity or Blender add to split meshes.</summary>
		private static string RoadKey(string[] words, int rest) {
			var key = words.Skip(rest).ToList();
			string last = key[key.Count - 1];
			int dot = last.LastIndexOf('.');
			if (dot > 0 && dot < last.Length - 1 && last.Substring(dot + 1).All(char.IsDigit)) {
				key[key.Count - 1] = last.Substring(0, dot);
			}
			if (key.Count > 1 && key[key.Count - 1].Trim('(', ')').All(char.IsDigit)) {
				key.RemoveAt(key.Count - 1); // "TRC Mainroad Akina (1)", from duplicating in Unity
			}
			return string.Join(" ", key);
		}

		/// <summary>Keeps one start or finish: the unnumbered one before ".001", ".001" before ".002".</summary>
		private static void Keep(ref Transform slot, ref int slotCopy, Transform transform, int copy, string bare, Parts parts) {
			if (slot == null) {
				slot = transform;
				slotCopy = copy;
				return;
			}
			bool replace = copy < slotCopy;
			parts.Duplicates.Add($"'{(replace ? slot.name : transform.name)}' is a second '{bare}': '{(replace ? transform.name : slot.name)}' is used.");
			if (replace) {
				slot = transform;
				slotCopy = copy;
			}
		}

		/// <summary>
		/// "TRC Path Akina.001" → "TRC Path Akina", copy 1 (Blender's numbering when you duplicate or split); "TRC Path
		/// Akina (2)" → copy 2 (Unity's). A name without either is copy 0, the first.
		/// </summary>
		public static string WithoutCopyNumber(string name, out int copy) {
			copy = 0;
			string trimmed = name.TrimEnd();
			int dot = trimmed.LastIndexOf('.');
			if (dot > 0 && dot < trimmed.Length - 1 && trimmed.Substring(dot + 1).All(c => c >= '0' && c <= '9') &&
				int.TryParse(trimmed.Substring(dot + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out copy)) {
				return trimmed.Substring(0, dot).TrimEnd();
			}
			copy = 0;
			if (trimmed.EndsWith(")", StringComparison.Ordinal)) {
				int open = trimmed.LastIndexOf('(');
				if (open > 0 && trimmed[open - 1] == ' ' && open < trimmed.Length - 2 &&
					int.TryParse(trimmed.Substring(open + 1, trimmed.Length - open - 2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out copy)) {
					return trimmed.Substring(0, open).TrimEnd();
				}
			}
			copy = 0;
			return name;
		}

		/// <summary>The name in lowercase words: spaces, underscores and dashes all split words.</summary>
		private static string[] Words(string name) =>
			TuneRules.Lower(name).Split(new[] { ' ', '_', '-', '\t' }, StringSplitOptions.RemoveEmptyEntries);

		/// <summary>"TRC_Start_Akina_Downhill" → "Akina Downhill", keeping the map maker's capitals.</summary>
		private static string DisplayName(string name, int rest) {
			string[] words = name.Split(new[] { ' ', '_', '-', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			var text = new StringBuilder();
			for (int i = rest; i < words.Length; i++) {
				text.Append(i > rest ? " " : "").Append(words[i]);
			}
			return text.ToString();
		}
	}
}
