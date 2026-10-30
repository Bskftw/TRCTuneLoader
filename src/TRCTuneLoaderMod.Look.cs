using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Options > Look (0.15.0): the window's colours and text size, for players who find the dark window hard to
	/// read. Ready-made looks, or the player's own background, accent and text colours, saved in Kino.Prefs. The
	/// change goes through in Update, so the window isn't rebuilt halfway through drawing.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string LookBackgroundPref = "ui_background";
		private const string LookAccentPref = "ui_accent";
		private const string LookTextPref = "ui_text";
		private const string LookTextSizePref = "ui_text_size";

		private static readonly float[] TextSizes = { 0.9f, 1f, 1.15f, 1.3f, 1.5f };

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

		private Color lookBackground_ = Theme.DefaultBackground;
		private Color lookAccent_ = Theme.DefaultAccent;
		private Color lookText_ = Theme.DefaultText;
		private float lookTextSize_ = 1f;
		private bool lookPending_;

		/// <summary>Which colour has its sliders open (0 none, 1 background, 2 accent, 3 text).</summary>
		private int lookEditing_;

		private void LoadLook() {
			if (Theme.TryHex(KSL.API.Kino.Prefs.Get(LookBackgroundPref, ""), out Color background)) {
				lookBackground_ = background;
			}
			if (Theme.TryHex(KSL.API.Kino.Prefs.Get(LookAccentPref, ""), out Color accent)) {
				lookAccent_ = accent;
			}
			if (Theme.TryHex(KSL.API.Kino.Prefs.Get(LookTextPref, ""), out Color text)) {
				lookText_ = text;
			}
			float size = KSL.API.Kino.Prefs.Get(LookTextSizePref, 1f);
			lookTextSize_ = TextSizes.OrderBy(s => Math.Abs(s - size)).First();
			Theme.Configure(lookBackground_, lookAccent_, lookText_, lookTextSize_);
		}

		/// <summary>Called from Update: a changed look goes through here, between frames.</summary>
		private void UpdateLook() {
			if (!lookPending_) {
				return;
			}
			lookPending_ = false;
			Theme.Configure(lookBackground_, lookAccent_, lookText_, lookTextSize_);
			SavePref(LookBackgroundPref, Theme.ToHex(lookBackground_));
			SavePref(LookAccentPref, Theme.ToHex(lookAccent_));
			SavePref(LookTextPref, Theme.ToHex(lookText_));
			SavePref(LookTextSizePref, lookTextSize_);
		}

		private void DrawLookOptions() {
			GUILayout.Label("Text size (on top of the window size)", Theme.Body);
			int sizeIndex = Array.FindIndex(TextSizes, s => Math.Abs(s - lookTextSize_) < 0.001f);
			int pickedSize = Choice(TextSizes.Select(s => $"{s * 100:0}%").ToArray(), sizeIndex);
			if (pickedSize >= 0 && pickedSize != sizeIndex) {
				lookTextSize_ = TextSizes[pickedSize];
				lookPending_ = true;
			}

			GUILayout.Space(6);
			GUILayout.Label("Colours: pick a look, or your own colours below.", Theme.Body);
			GUILayout.BeginHorizontal();
			foreach (Look look in Looks) {
				bool current = SameColour(look.Background, lookBackground_) && SameColour(look.Accent, lookAccent_) && SameColour(look.Text, lookText_);
				if (GUILayout.Button(look.Name, current ? Theme.ChipActive : Theme.Chip)) {
					lookBackground_ = look.Background;
					lookAccent_ = look.Accent;
					lookText_ = look.Text;
					lookPending_ = true;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			ColourRow("Background", ref lookBackground_, 1);
			ColourRow("Accent (buttons, tabs)", ref lookAccent_, 2);
			ColourRow("Text", ref lookText_, 3);
			if (Mathf.Abs(Theme.Luminance(lookBackground_) - Theme.Luminance(lookText_)) < 0.35f) {
				GUILayout.Label("The text and the background are close: the text may be hard to read.", Theme.PillWarn);
			} else if (Theme.Luminance(lookBackground_) > 0.5f) {
				GUILayout.Label("On a light background some coloured notes (green, amber) are harder to read.", Theme.Small);
			}
		}

		/// <summary>A colour: its swatch and hex, a row of ready colours, and (Custom) sliders for hue, colour and brightness.</summary>
		private void ColourRow(string label, ref Color colour, int which) {
			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<b>{label}</b>  <color={Theme.MutedHex}>{Theme.ToHex(colour)}</color>", Theme.Body, GUILayout.Width(230));
			Swatch(colour, 22f, current: true);
			GUILayout.Space(8);
			foreach (Color swatch in Swatches) {
				if (Swatch(swatch, 18f, current: SameColour(swatch, colour))) {
					colour = swatch;
					lookPending_ = true;
				}
			}
			GUILayout.FlexibleSpace();
			if (GUILayout.Button(lookEditing_ == which ? "Done" : "Custom", Theme.Button)) {
				lookEditing_ = lookEditing_ == which ? 0 : which;
			}
			GUILayout.EndHorizontal();
			if (lookEditing_ != which) {
				return;
			}
			Color.RGBToHSV(colour, out float h, out float s, out float v);
			float nh = SliderRow("Hue", h);
			float ns = SliderRow("Colour", s);
			float nv = SliderRow("Brightness", v);
			if (Math.Abs(nh - h) > 0.001f || Math.Abs(ns - s) > 0.001f || Math.Abs(nv - v) > 0.001f) {
				colour = Color.HSVToRGB(nh, ns, nv);
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
