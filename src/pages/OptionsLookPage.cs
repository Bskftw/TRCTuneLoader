// Options > Look: the page for the window's colours and text size (ready-made looks, colour rows, sliders).
using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Options > Look, rebuilt for the 0.17 menu (bskftw, Nov 3: "the colour customization is a bit buggy... lets just
	/// redo the menu customization entirely"). One list of colours, each saved in Kino.Prefs as hex:
	///   Window: background, text, second text (Auto: worked out from the text).
	///   Highlights: accent (page titles, notes, lines), buttons (Auto: the accent).
	///   Parts: the six parts of the menu (their names across the top, their Home cards).
	/// Ready-made looks set the window and highlight colours; Reset all puts every colour back. A change is applied in
	/// Update (not halfway through drawing), bumps <see cref="Theme.Version"/>, and every style is made again from it.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private sealed class Look {
			public string Name;
			public Color Background;
			public Color Accent;
			public Color Text;
		}

		private static readonly Look[] Looks = {
			new Look { Name = "TRC", Background = Theme.DefaultBackground, Accent = Theme.DefaultAccent, Text = Theme.DefaultText },
			new Look { Name = "Darker", Background = Rgb(0x0B0B0D), Accent = Theme.DefaultAccent, Text = Rgb(0xF4F4F4) },
			new Look { Name = "Midnight", Background = Rgb(0x0F1626), Accent = Rgb(0x4FA3E0), Text = Rgb(0xE8EEF7) },
			new Look { Name = "Grey", Background = Rgb(0x2B2B30), Accent = Theme.DefaultAccent, Text = Rgb(0xF2F2F2) },
			new Look { Name = "High contrast", Background = Rgb(0x000000), Accent = Rgb(0xFF5C9A), Text = Rgb(0xFFFFFF) },
			new Look { Name = "Light", Background = Rgb(0xF1F1F4), Accent = Rgb(0xC2416C), Text = Rgb(0x16161A) },
		};

		private static readonly Color[] Swatches = {
			Rgb(0xD4537E), Rgb(0xE0443E), Rgb(0xE8A33D), Rgb(0xE8D23D), Rgb(0x7FB843), Rgb(0x2FBF9A),
			Rgb(0x4FA3E0), Rgb(0x3D63E8), Rgb(0x9B7BEA), Rgb(0xFFFFFF), Rgb(0x9A9AA3), Rgb(0x000000),
		};

		/// <summary>The colour with its sliders open ("" none), and the hex being typed for it.</summary>
		private string lookEditing_ = "";
		private string lookHexTyped_ = "";

		private void DrawLookOptions() {
			GUILayout.Label("Text size (on top of the window size)", Theme.Body);
			int sizeIndex = Array.FindIndex(TextSizes, s => Math.Abs(s - lookTextSize_) < 0.001f);
			int pickedSize = Choice(TextSizes.Select(s => $"{s * 100:0}%").ToArray(), sizeIndex);
			if (pickedSize >= 0 && pickedSize != sizeIndex) {
				lookTextSize_ = TextSizes[pickedSize];
				lookPending_ = true;
			}

			GUILayout.Space(8);
			GUILayout.Label("Ready-made looks (the window and highlight colours; the parts keep theirs)", Theme.Body);
			GUILayout.BeginHorizontal();
			foreach (Look look in Looks) {
				bool current = SameColour(look.Background, Shown(LookOf(BackgroundKey))) && SameColour(look.Accent, Shown(LookOf(AccentKey))) &&
					SameColour(look.Text, Shown(LookOf(TextKey))) && LookOf(MutedKey).Own == null && LookOf(ButtonKey).Own == null;
				if (NavButton(look.Name, current ? Theme.ChipActive : Theme.Chip)) {
					LookOf(BackgroundKey).Own = look.Background;
					LookOf(AccentKey).Own = look.Accent;
					LookOf(TextKey).Own = look.Text;
					LookOf(MutedKey).Own = null;
					LookOf(ButtonKey).Own = null;
					lookPending_ = true;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			LookGroup("Window", BackgroundKey, TextKey, MutedKey);
			LookGroup("Highlights", AccentKey, ButtonKey);
			LookGroup("Parts (their names across the top and their cards on Home)", PartKeys);

			Color background = Shown(LookOf(BackgroundKey)), text = Shown(LookOf(TextKey));
			if (Mathf.Abs(Theme.Luminance(background) - Theme.Luminance(text)) < 0.35f) {
				GUILayout.Label("The text and the background are close: the text may be hard to read.", Theme.PillWarn);
			} else if (Theme.Luminance(background) > 0.5f) {
				GUILayout.Label("On a light background some coloured notes (green, amber) are harder to read.", Theme.Small);
			}
			GUILayout.Space(6);
			GUILayout.BeginHorizontal();
			if (NavButton("Reset all colours", Theme.Button)) {
				foreach (LookColour c in lookColours_) {
					c.Own = null;
				}
				lookEditing_ = "";
				lookPending_ = true;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void LookGroup(string title, params string[] keys) {
			GUILayout.Space(8);
			GUILayout.Label(title, Theme.SectionLabel);
			foreach (string key in keys) {
				LookRow(LookOf(key));
			}
		}

		/// <summary>
		/// A colour: its name, what it colours, the colour now, a row of ready colours, its hex (type one in), Auto or
		/// Reset, and Custom for hue, colour and brightness sliders.
		/// </summary>
		private void LookRow(LookColour c) {
			Color now = Shown(c);
			GUILayout.BeginHorizontal();
			string note = c.Note != null ? $"  <color={Theme.MutedHex}>{c.Note}</color>" : "";
			string auto = c.CanBeAuto && c.Own == null ? $"  <color={Theme.MutedHex}>(Auto)</color>" : "";
			GUILayout.Label($"<b>{c.Label}</b>{auto}{note}", Theme.Body, GUILayout.Width(330));
			Swatch(now, 22f, current: true);
			GUILayout.Space(8);
			foreach (Color swatch in Swatches) {
				if (Swatch(swatch, 18f, current: SameColour(swatch, now))) {
					c.Own = swatch;
					lookPending_ = true;
				}
			}
			GUILayout.FlexibleSpace();
			bool own = c.Own != null;
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && own;
			if (NavButton(c.CanBeAuto ? "Auto" : "Reset", Theme.Button)) {
				c.Own = null;
				lookPending_ = true;
			}
			GUI.enabled = enabled;
			if (NavButton(lookEditing_ == c.Key ? "Done" : "Custom", Theme.Button)) {
				lookEditing_ = lookEditing_ == c.Key ? "" : c.Key;
				lookHexTyped_ = Theme.ToHex(now);
			}
			GUILayout.EndHorizontal();
			if (lookEditing_ != c.Key) {
				return;
			}

			GUILayout.BeginHorizontal();
			GUILayout.Label("Hex", Theme.Small, GUILayout.Width(80));
			string typed = GUILayout.TextField(lookHexTyped_ ?? "", 7, Theme.Field, GUILayout.Width(100));
			if (typed != lookHexTyped_) {
				lookHexTyped_ = typed;
				if (Theme.TryHex(typed, out Color fromHex)) {
					c.Own = fromHex;
					lookPending_ = true;
				}
			}
			GUILayout.Label("like #D4537E", Theme.Small);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			Color.RGBToHSV(now, out float h, out float s, out float v);
			float nh = SliderRow("Hue", h);
			float ns = SliderRow("Colour", s);
			float nv = SliderRow("Brightness", v);
			if (Math.Abs(nh - h) > 0.001f || Math.Abs(ns - s) > 0.001f || Math.Abs(nv - v) > 0.001f) {
				c.Own = Color.HSVToRGB(nh, ns, nv);
				lookHexTyped_ = Theme.ToHex(c.Own.Value);
				lookPending_ = true;
			}
		}

		private static float SliderRow(string label, float value) {
			GUILayout.BeginHorizontal();
			GUILayout.Label(label, Theme.Small, GUILayout.Width(80));
			value = GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.ExpandWidth(true));
			GUILayout.Label(value.ToString("0.00", CultureInfo.InvariantCulture), Theme.Small, GUILayout.Width(36));
			GUILayout.EndHorizontal();
			return value;
		}

		/// <summary>A small square of colour; true when clicked.</summary>
		private static bool Swatch(Color colour, float size, bool current) {
			Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
			rect.y += 3f;
			bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
			if (Event.current.type == EventType.Repaint) {
				Color before = GUI.color;
				GUI.color = current ? Theme.Text : Theme.Border;
				GUI.DrawTexture(rect, Texture2D.whiteTexture);
				GUI.color = colour;
				GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), Texture2D.whiteTexture);
				GUI.color = before;
			}
			GUILayout.Space(3);
			return clicked;
		}

		private static bool SameColour(Color a, Color b) => Theme.ToHex(a) == Theme.ToHex(b);

		private static Color Rgb(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
	}
}
