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
		private static readonly Color[] Swatches = {
			Rgb(0xD4537E), Rgb(0xE0443E), Rgb(0xE8A33D), Rgb(0xE8D23D), Rgb(0x7FB843), Rgb(0x2FBF9A),
			Rgb(0x4FA3E0), Rgb(0x3D63E8), Rgb(0x9B7BEA), Rgb(0xFFFFFF), Rgb(0x9A9AA3), Rgb(0x000000),
		};

		/// <summary>The colour with its sliders open ("" none), and the hex being typed for it.</summary>
		private string lookEditing_ = "";
		private string lookHexTyped_ = "";

		/// <summary>
		/// Options > Look, for the redesign (docs/archive/ui-redesign-plan.md, "Colour customization, reworked"): ready-made
		/// looks, the fonts, a readability check with Fix it, then your own colours. The same prefs as before
		/// (ui_background, ui_accent...), so players' colours carry over. The whole window is the live sample.
		/// </summary>
		private void DrawLookOptions() {
			BeginSection("Ready-made looks", "The window and highlight colours. The parts keep theirs. Changes show on the whole window at once.");
			GUILayout.BeginHorizontal();
			foreach (LookTokens.Preset look in LookTokens.Presets) {
				bool isDefault = look == LookTokens.Default;
				Color? muted = isDefault ? (Color?)null : look.Muted;
				bool current = SameColour(look.Background, Shown(LookOf(BackgroundKey))) && SameColour(look.Accent, Shown(LookOf(AccentKey))) &&
					SameColour(look.Text, Shown(LookOf(TextKey))) && LookOf(ButtonKey).Own == null &&
					(LookOf(MutedKey).Own == null ? muted == null : muted != null && SameColour(muted.Value, LookOf(MutedKey).Own.Value));
				if (NavButton(look.Name, current ? Theme.ChipActive : Theme.Chip)) {
					LookOf(BackgroundKey).Own = look.Background;
					LookOf(AccentKey).Own = look.Accent;
					LookOf(TextKey).Own = look.Text;
					LookOf(MutedKey).Own = muted;
					LookOf(ButtonKey).Own = null;
					lookPending_ = true;
				}
			}
			GUILayout.FlexibleSpace();
			if (NavButton("Reset all colours", Theme.ButtonQuiet)) {
				foreach (LookColour c in lookColours_) {
					c.Own = null;
				}
				lookEditing_ = "";
				lookPending_ = true;
			}
			GUILayout.EndHorizontal();
			DrawReadability();
			EndSection();

			BeginSection("Font");
			SettingRow("Road sign fonts", Theme.FontNote, () => {
				bool on = Switch(Theme.RoadSign);
				if (on != Theme.RoadSign) {
					SavePref(LookFontPref, on ? "road" : "classic");
					Theme.UseRoadSign(on);
				}
			}, first: true);
			SettingRow("Heavier headings", "Bold headings and buttons", () => {
				bool on = Switch(Theme.HeavyHeadings);
				if (on != Theme.HeavyHeadings) {
					SavePref("ui_font_bold", on);
					Theme.UseHeavyHeadings(on);
				}
			});
			EndSection();

			BeginSection("Home tiles");
			SettingRow("Tile colours", "Each part in its own colour (set under Make your own), or all in your accent", () => {
				int accent = Choice(new[] { "Each part's colour", "My accent" }, homeTilesAccent_ ? 1 : 0);
				if ((accent == 1) != homeTilesAccent_) {
					homeTilesAccent_ = accent == 1;
					SavePref("home_tiles_accent", homeTilesAccent_);
				}
			}, first: true);
			EndSection();

			BeginSection("Over the track", "The boxes over the track: the timer, speed traps, tandem, score, the badge and pop-ups.");
			SettingRow("How solid", "Less lets more of the track show through", () => {
				float opacity = Slider(Theme.HudOpacity, 0.4f, 1f, 0.05f, $"{Theme.HudOpacity * 100f:0}%");
				if (Mathf.Abs(opacity - Theme.HudOpacity) > 0.001f) {
					SavePref("hud_opacity", opacity);
					Theme.UseHud(opacity, Theme.HudFollowsMenu);
				}
			}, wide: true, first: true);
			SettingRow("Colour", "A bright menu look doesn't have to put bright boxes over the track", () => {
				int follow = Choice(new[] { "Follow the menu", "Classic dark" }, Theme.HudFollowsMenu ? 0 : 1);
				if ((follow == 0) != Theme.HudFollowsMenu) {
					SavePref("hud_follow_menu", follow == 0);
					Theme.UseHud(Theme.HudOpacity, follow == 0);
				}
			});
			EndSection();

			BeginSection("Make your own");
			LookGroup("Window", BackgroundKey, TextKey, MutedKey);
			LookGroup("Highlights", AccentKey, ButtonKey);
			LookGroup("Parts (their icons in the menu and their tiles on Home)", PartKeys);
			EndSection();
		}

		/// <summary>
		/// Whether text reads on the background (4.5:1 or more, LookTokens.Contrast), and Fix it, which moves the text
		/// (and the muted text) toward white or black until it does (plan risk 8).
		/// </summary>
		private void DrawReadability() {
			Color background = Shown(LookOf(BackgroundKey)), text = Shown(LookOf(TextKey));
			LookTokens look = LookTokens.From(background, Shown(LookOf(AccentKey)), text, LookValue(MutedKey), LookValue(ButtonKey));
			float main = LookTokens.Contrast(look.Text, look.Panel), muted = LookTokens.Contrast(look.Muted, look.Panel);
			if (main >= 4.5f && muted >= 4.5f) {
				GUILayout.Label($"Reads well: text {main:0.0}:1, second text {muted:0.0}:1.", Theme.SectionNote);
				return;
			}
			GUILayout.BeginHorizontal();
			GUILayout.Label($"Hard to read: text {main:0.0}:1, second text {muted:0.0}:1 (4.5 or more reads well).", Theme.PillWarn);
			if (NavButton("Fix it", Theme.Button)) {
				if (main < 4.5f) {
					LookOf(TextKey).Own = LookTokens.Readable(text, look.Panel);
				}
				if (muted < 4.5f) {
					LookOf(MutedKey).Own = LookTokens.Readable(look.Muted, look.Panel);
				}
				lookPending_ = true;
			}
			GUILayout.EndHorizontal();
		}

		/// <summary>Text size, a row in Options > Look's Size section.</summary>
		private void DrawTextSizeRow() {
			int sizeIndex = Array.FindIndex(TextSizes, s => Math.Abs(s - lookTextSize_) < 0.001f);
			SettingRow("Text size", "On top of the window size", () => {
				int pickedSize = Choice(TextSizes.Select(s => $"{s * 100:0}%").ToArray(), sizeIndex);
				if (pickedSize >= 0 && pickedSize != sizeIndex) {
					lookTextSize_ = TextSizes[pickedSize];
					lookPending_ = true;
				}
			}, wide: true);
		}

		private void LookGroup(string title, params string[] keys) {
			GUILayout.Space(6);
			GUILayout.Label(title, Theme.SettingLabel);
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
			string auto = c.CanBeAuto && c.Own == null ? " (Auto)" : "";
			GUILayout.BeginVertical(GUILayout.Width(170));
			GUILayout.Label($"{c.Label}{auto}", Theme.SettingLabel);
			if (c.Note != null) {
				GUILayout.Label(c.Note, Theme.SettingNote);
			}
			GUILayout.EndVertical();
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
			if (NavButton(c.CanBeAuto ? "Auto" : "Reset", Theme.ButtonQuiet)) {
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
