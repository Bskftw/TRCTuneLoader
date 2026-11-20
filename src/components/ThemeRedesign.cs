// The redesign's styles and textures (docs/archive/ui-redesign-plan.md, Stage 1): sections, setting rows, switches, choices, stats, gradient tiles, the HUD plate.
using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader {
	internal static partial class Theme {
		public static GUIStyle PageTitle, PageLead, Section, SectionTitle, SectionNote;
		public static GUIStyle SettingLabel, SettingNote, StatValue, StatLabel, StatBox;
		public static GUIStyle SegBack, SegOn, SegOff, SwitchOn, SwitchOff;
		public static GUIStyle TileTitle, TileState, TileLink, Plate;
		public static Texture2D Knob, KnobOff, SegLine;

		/// <summary>The gradient tiles, one per colour, made when first asked for and dropped with the other textures.</summary>
		private static readonly Dictionary<int, GUIStyle> tiles_ = new Dictionary<int, GUIStyle>();

		private static void MakeRedesignStyles() {
			PageTitle = Strong(TextStyle(24, Theme.Text, FontStyle.Bold));
			PageLead = TextStyle(13, Muted);
			Section = Box(Rounded(Panel, Border, 8), 8, new RectOffset(16, 16, 12, 14));
			Section.margin = new RectOffset(0, 0, 0, 12);
			SectionTitle = Strong(TextStyle(15, Theme.Text, FontStyle.Bold));
			SectionNote = TextStyle(12, Muted);
			SectionNote.margin = new RectOffset(0, 0, 0, 8);

			SettingLabel = Strong(TextStyle(13, Theme.Text, FontStyle.Bold));
			SettingLabel.margin = new RectOffset(0, 0, 0, 0);
			SettingNote = TextStyle(12, Muted);
			SettingNote.margin = new RectOffset(0, 0, 0, 0);

			StatBox = Box(Rounded(Raised, Raised, 6), 6, new RectOffset(10, 10, 7, 8));
			StatBox.margin = new RectOffset(0, 8, 0, 0);
			StatValue = Strong(TextStyle(20, Theme.Text, FontStyle.Bold));
			StatValue.wordWrap = false;
			StatValue.margin = new RectOffset(0, 0, 0, 0);
			StatLabel = TextStyle(12, Muted);
			StatLabel.margin = new RectOffset(0, 0, 0, 0);

			// A choice of 2 to 4: a raised strip, the picked one darker with an accent line under it.
			SegBack = Box(Rounded(Raised, Border, 6), 6, new RectOffset(2, 2, 2, 2));
			SegOff = Strong(Clickable(Rounded(Color.clear, Color.clear, 4), Rounded(Color.Lerp(Raised, Theme.Text, 0.05f), Color.clear, 4), 13, Muted, new RectOffset(12, 12, 5, 6)));
			SegOff.hover.textColor = Theme.Text;
			SegOff.margin = new RectOffset(0, 0, 0, 0);
			SegOff.wordWrap = false;
			SegOn = Strong(Clickable(Rounded(Background, Background, 4), Rounded(Background, Background, 4), 13, Theme.Text, new RectOffset(12, 12, 5, 6)));
			SegOn.margin = new RectOffset(0, 0, 0, 0);
			SegOn.wordWrap = false;
			SegLine = Solid(Accent);

			// A switch: 40 x 22 track; the knob is drawn on top (Controls.cs).
			SwitchOff = Clickable(Rounded(Raised, Border, 11), Rounded(Color.Lerp(Raised, Theme.Text, 0.06f), Control, 11), 12, Theme.Text, new RectOffset(0, 0, 0, 0));
			SwitchOn = Clickable(Rounded(ButtonColour, ButtonColour, 11), Rounded(ButtonHover, ButtonHover, 11), 12, Theme.Text, new RectOffset(0, 0, 0, 0));
			foreach (GUIStyle s in new[] { SwitchOff, SwitchOn }) {
				s.fixedWidth = 40;
				s.fixedHeight = 22;
				s.margin = new RectOffset(0, 0, 4, 4);
			}
			Knob = Rounded(Color.white, Color.white, 8);
			KnobOff = Rounded(Muted, Muted, 8);

			TileTitle = Strong(TextStyle(19, Theme.Text, FontStyle.Bold));
			TileTitle.wordWrap = false;
			TileState = Strong(TextStyle(14, Theme.Text, FontStyle.Bold));
			TileLink = Clickable(Rounded(new Color(1f, 1f, 1f, 0.07f), new Color(1f, 1f, 1f, 0.1f), 5), Rounded(new Color(1f, 1f, 1f, 0.14f), new Color(1f, 1f, 1f, 0.18f), 5), 12, Theme.Text, new RectOffset(9, 9, 3, 4));
			TileLink.margin = new RectOffset(0, 6, 3, 3);

			// Over the track (Stage 5 moves the HUD onto it): the window colour at 86%, a faint light edge.
			Plate = new GUIStyle(HudPanel) { padding = new RectOffset(14, 14, 10, 10) };
		}

		/// <summary>
		/// A Home tile in <paramref name="colour"/> (bskftw, Nov 18: "similar to the gradients ... just better"): the colour
		/// as a wash from the top-left corner fading into the panel, an edge in the same colour, 10 px corners.
		/// </summary>
		public static GUIStyle Tile(Color colour) {
			int key = ToHex(colour).GetHashCode();
			if (tiles_.TryGetValue(key, out GUIStyle known) && known.normal.background != null) {
				return known;
			}
			Texture2D texture = Gradient(96, 72, 10, colour);
			var style = new GUIStyle {
				normal = { background = texture },
				border = new RectOffset(12, 12, 12, 12),
				padding = new RectOffset(14, 14, 10, 10),
				// No margin: the page sets the gaps (a margin here pushed Home's second tile off the right, Nov 19).
				margin = new RectOffset(0, 0, 0, 0),
			};
			tiles_[key] = style;
			return style;
		}

		private static Texture2D Gradient(int width, int height, int radius, Color colour) {
			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) {
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
			};
			Color top = Color.Lerp(Panel, Theme.Text, 0.03f), bottom = Color.Lerp(Panel, Color.black, 0.08f);
			Color edge = Color.Lerp(Border, colour, 0.45f);
			var half = new Vector2(width / 2f, height / 2f);
			var inner = half - new Vector2(radius, radius);
			for (int y = 0; y < height; y++) {
				for (int x = 0; x < width; x++) {
					// Texture rows go up from the bottom: y = height is the tile's top.
					float fromTop = 1f - (y + 0.5f) / height, fromLeft = (x + 0.5f) / width;
					float wash = Mathf.Clamp01(1f - Mathf.Sqrt(fromLeft * fromLeft * 0.7f + fromTop * fromTop) / 1.15f);
					// Softer on a light background, where a strong wash makes the text hard to read (the colour plan).
					float strength = LookTokens.Luminance(Background) > 0.5f ? 0.2f : 0.34f;
					Color fill = Color.Lerp(Color.Lerp(top, bottom, fromTop * 0.6f + fromLeft * 0.4f), colour, strength * wash * wash + 0.04f);
					Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - half;
					Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - inner;
					float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
					float depth = -outside;
					Color c = depth < 1.5f ? Color.Lerp(edge, fill, Mathf.Clamp01(depth - 0.5f)) : fill;
					c.a = Mathf.Clamp01(depth + 0.5f);
					texture.SetPixel(x, y, c);
				}
			}
			texture.Apply();
			made_.Add(texture);
			return texture;
		}
	}
}
