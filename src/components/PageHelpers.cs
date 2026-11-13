// Small drawing pieces many pages use: the side menu, a card of text lines, a divider, stat boxes, table rows, choice chips.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const float SideNavWidth = 158f;

		/// <summary>
		/// A tab's sections in a column down the left, like the car list in Tune sets: tab, section, content, never
		/// tabs inside tabs. <paramref name="notes"/> are small counts after each name ("" for none).
		/// </summary>
		private static int SideNav(string[] names, int picked, string[] notes = null) {
			if (sideNavHidden_) {
				return picked;
			}
			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(SideNavWidth), GUILayout.ExpandHeight(true));
			for (int i = 0; i < names.Length; i++) {
				string note = notes != null && i < notes.Length && notes[i].Length > 0 ? $"  <color={Theme.MutedHex}>{notes[i]}</color>" : "";
				if (NavButton($"{names[i]}{note}", i == picked ? Theme.RowSelected : Theme.Row)) {
					picked = i;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndVertical();
			return picked;
		}

		/// <summary>A scrolling card of text lines; lines starting with "---" become small pink headings.</summary>
		private static void DrawLines(ref Vector2 scroll, List<string> lines) {
			GUILayout.BeginVertical(Theme.Card, GUILayout.ExpandHeight(true));
			scroll = GUILayout.BeginScrollView(scroll);
			foreach (string line in lines) {
				if (line.StartsWith("---", StringComparison.Ordinal)) {
					GUILayout.Label(line.Trim('-', ' '), Theme.Group);
				} else {
					GUILayout.Label(line, Theme.Body);
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
		}

		private static void DividerLine() {
			Rect line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
			if (Event.current.type == EventType.Repaint) {
				GUI.DrawTexture(line, Theme.Divider);
			}
		}

		private static void StatBox(string label, string value, string hex, string below = null) {
			EnsureNavStyles();
			GUILayout.BeginVertical(statBox_, GUILayout.MinWidth(120));
			GUILayout.Label(label, statLabel_);
			GUILayout.Label(hex != null ? $"<color={hex}>{value}</color>" : value, statValue_);
			if (!string.IsNullOrEmpty(below)) {
				GUILayout.Label(below, statLabel_);
			}
			GUILayout.EndVertical();
		}

		/// <summary>A table's header row: the column names over a line.</summary>
		private static void TableHead(string[] names, float[] widths) {
			EnsureNavStyles();
			GUILayout.BeginHorizontal();
			for (int i = 0; i < names.Length; i++) {
				if (widths[i] > 0f) {
					GUILayout.Label(names[i], tableHead_, GUILayout.Width(widths[i]));
				} else {
					GUILayout.Label(names[i], tableHead_);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			DividerLine();
		}

		/// <summary>One cell of a table row, as wide as its column, the text cut off at the edge.</summary>
		private static void TableCell(string text, float width) {
			EnsureNavStyles();
			GUILayout.Label(text, tableCell_, GUILayout.Width(width), GUILayout.MinHeight(32));
		}

		/// <summary>A status with a coloured dot: green good, amber waiting or unsure, red not spec, grey unknown.</summary>
		private static string Dot(string hex, string text) => $"<color={hex}><b>•</b></color> {text}";

		/// <summary>A row of chips, one picked. Returns the picked index.</summary>
		private static int Choice(string[] labels, int picked) {
			GUILayout.BeginHorizontal();
			for (int i = 0; i < labels.Length; i++) {
				if (NavButton(labels[i], i == picked ? Theme.ChipActive : Theme.Chip)) {
					picked = i;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			return picked;
		}

		/// <summary>The same, <paramref name="perRow"/> to a row, for long lists (bskftw, Nov 3: "Highway cut-up" was cut off).</summary>
		private static int Choice(string[] labels, int picked, int perRow) {
			for (int start = 0; start < labels.Length; start += perRow) {
				GUILayout.BeginHorizontal();
				for (int i = start; i < labels.Length && i < start + perRow; i++) {
					if (NavButton(labels[i], i == picked ? Theme.ChipActive : Theme.Chip)) {
						picked = i;
					}
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			return picked;
		}

		private static bool OnOff(bool on) => Choice(new[] { "On", "Off" }, on ? 0 : 1) == 0;
	}
}
