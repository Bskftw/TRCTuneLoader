// The menu's own styles and each category's colours, made again when the look changes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private static readonly Dictionary<string, GUIStyle[]> categoryStyles_ = new Dictionary<string, GUIStyle[]>(StringComparer.Ordinal);
		private static GUIStyle courseTitle_, kicker_, statBox_, statLabel_, statValue_, keyCap_, cardTitle_, pageTitle_, tableHead_, tableCell_;

		private static Color HexColor(string hex) => Theme.TryHex(hex, out Color c) ? c : Theme.Accent;

		/// <summary>
		/// A category's looks: [0] its top button, [1] the same picked, [2] its Home card, [3] a primary button in its colour,
		/// [4] its live line, [5] a picked chip (filled), [6] a picked row (tinted).
		/// </summary>
		private static GUIStyle[] CategoryStyles(string hex) {
			EnsureNavStyles();
			if (categoryStyles_.TryGetValue(hex, out GUIStyle[] styles) && styles[2].normal.background != null) {
				return styles;
			}
			Color colour = HexColor(hex);
			Color dark = Theme.Luminance(Theme.Background) < 0.5f ? new Color(0.08f, 0.08f, 0.09f) : Color.white;
			var top = new GUIStyle(Theme.Tab) { normal = { textColor = colour }, hover = { textColor = Color.Lerp(colour, Color.white, 0.3f) }, fontStyle = FontStyle.Bold };
			var picked = new GUIStyle(top) {
				normal = { background = Theme.Rounded(colour, colour, 7), textColor = dark },
				hover = { background = Theme.Rounded(colour, colour, 7), textColor = dark },
				border = new RectOffset(7, 7, 7, 7),
			};
			var card = new GUIStyle(Theme.Card) {
				normal = { background = Theme.Rounded(Color.Lerp(Theme.Panel, colour, 0.10f), Color.Lerp(Theme.Border, colour, 0.55f), 10) },
				border = new RectOffset(10, 10, 10, 10),
				padding = new RectOffset(18, 18, 14, 14),
				margin = new RectOffset(0, 0, 0, 0),
			};
			var primary = new GUIStyle(Theme.ButtonPrimary) {
				normal = { background = Theme.Rounded(colour, colour, 6), textColor = dark },
				hover = { background = Theme.Rounded(Color.Lerp(colour, Color.white, 0.15f), Color.Lerp(colour, Color.white, 0.15f), 6), textColor = dark },
				active = { background = Theme.Rounded(colour, colour, 6), textColor = dark },
			};
			var live = new GUIStyle(Theme.Body) { normal = { textColor = colour } };
			var chip = new GUIStyle(Theme.ChipActive) {
				normal = { background = Theme.Rounded(colour, colour, 12), textColor = dark },
				hover = { background = Theme.Rounded(colour, colour, 12), textColor = dark },
				active = { background = Theme.Rounded(colour, colour, 12), textColor = dark },
				fontStyle = FontStyle.Bold,
			};
			var row = new GUIStyle(Theme.RowSelected) {
				normal = { background = Theme.Rounded(Color.Lerp(Theme.Panel, colour, 0.16f), Color.Lerp(Theme.Border, colour, 0.75f), 6), textColor = Theme.Text },
				hover = { background = Theme.Rounded(Color.Lerp(Theme.Panel, colour, 0.22f), colour, 6), textColor = Theme.Text },
			};
			styles = new[] { top, picked, card, primary, live, chip, row };
			categoryStyles_[hex] = styles;
			return styles;
		}

		/// <summary>The <see cref="Theme.Version"/> the menu's own styles were made for.</summary>
		private static int navStylesVersion_ = -1;

		/// <summary>
		/// Makes the menu's own styles (again when the look changed): titles, tables, stat boxes, and the parts' and
		/// cards' looks, which are cleared so they're made in the new colours.
		/// </summary>
		private static void EnsureNavStyles() {
			if (courseTitle_ != null && navStylesVersion_ == Theme.Version && statBox_?.normal.background != null) {
				return;
			}
			navStylesVersion_ = Theme.Version;
			categoryStyles_.Clear();
			foreach (GUIStyle old in cardStyles_.Values) {
				if (old.normal.background != null) {
					UnityEngine.Object.Destroy(old.normal.background);
				}
			}
			cardStyles_.Clear();
			cardTitle_ = new GUIStyle(Theme.Heading) { fontSize = Mathf.RoundToInt(18 * Theme.TextScale) };
			// Page titles and the "selected course" label are in the accent colour, so Look's accent shows inside the menu.
			pageTitle_ = new GUIStyle(Theme.Heading) { fontSize = Mathf.RoundToInt(20 * Theme.TextScale), wordWrap = false, margin = new RectOffset(0, 10, 4, 4), normal = { textColor = Theme.Accent } };
			tableHead_ = new GUIStyle(Theme.Small) { wordWrap = false, clipping = TextClipping.Clip, margin = new RectOffset(0, 0, 4, 4), padding = new RectOffset(4, 4, 0, 0) };
			tableCell_ = new GUIStyle(Theme.Body) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(4, 4, 0, 0) };
			courseTitle_ = new GUIStyle(Theme.Heading) { fontSize = Mathf.RoundToInt(28 * Theme.TextScale), fontStyle = FontStyle.BoldAndItalic, wordWrap = false };
			kicker_ = new GUIStyle(Theme.Small) { fontStyle = FontStyle.Bold, normal = { textColor = Theme.Accent } };
			statBox_ = new GUIStyle(Theme.Card) { padding = new RectOffset(12, 12, 8, 8), margin = new RectOffset(0, 8, 4, 4) };
			statLabel_ = new GUIStyle(Theme.Small) { margin = new RectOffset(0, 0, 0, 0) };
			statValue_ = new GUIStyle(Theme.Heading) { margin = new RectOffset(0, 0, 0, 0), wordWrap = false };
			keyCap_ = new GUIStyle(Theme.Small) {
				normal = { background = Theme.Rounded(Theme.Control, Theme.Muted, 4), textColor = Theme.Text },
				border = new RectOffset(4, 4, 4, 4),
				padding = new RectOffset(6, 6, 1, 2),
				margin = new RectOffset(0, 5, 3, 3),
				alignment = TextAnchor.MiddleCenter,
				wordWrap = false,
			};
		}
	}
}
