// The menu's colours worked out from the player's picks (Options > Look), and the ready-made looks. No textures, so the self-test can check them.
using System;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The redesign's colour layers (docs/archive/ui-redesign-plan.md, "Colour customization, reworked"): the player picks a
	/// background, accent, text (and optionally muted text and a button colour); everything else is fixed steps from
	/// those, toward white on a dark background and toward black on a light one, keeping the background's own tint.
	/// </summary>
	public sealed class LookTokens {
		public Color Night, Rail, Panel, Raised, Line;
		public Color Text, Muted;
		public Color Accent, AccentHover, AccentSoft, AccentText;
		public Color Button, ButtonHover, ButtonSoft, ButtonText;

		/// <summary>A ready-made look: one click on the Look page.</summary>
		public sealed class Preset {
			public string Name;
			public Color Background, Accent, Text, Muted;
		}

		public static readonly Preset[] Presets = {
			new Preset { Name = "TRC Night", Background = Rgb(0x1A1D27), Accent = Rgb(0xE0628F), Text = Rgb(0xE9EAF0), Muted = Rgb(0x9097AD) },
			new Preset { Name = "Carbon", Background = Rgb(0x1B1B1D), Accent = Rgb(0xE0628F), Text = Rgb(0xECECEC), Muted = Rgb(0x9C9CA2) },
			new Preset { Name = "Deep Blue", Background = Rgb(0x111A2E), Accent = Rgb(0x4FA3FF), Text = Rgb(0xE6EEFA), Muted = Rgb(0x8EA0BF) },
			new Preset { Name = "Sakura", Background = Rgb(0x241821), Accent = Rgb(0xF07AA5), Text = Rgb(0xF6E9EF), Muted = Rgb(0xB497A7) },
			new Preset { Name = "High Contrast", Background = Rgb(0x000000), Accent = Rgb(0xFFD23F), Text = Rgb(0xFFFFFF), Muted = Rgb(0xC8C8C8) },
		};

		public static Preset Default => Presets[0];

		/// <summary>Every layer from the picks. <paramref name="muted"/> and <paramref name="button"/> are worked out when null.</summary>
		public static LookTokens From(Color background, Color accent, Color text, Color? muted = null, Color? button = null) {
			background.a = accent.a = text.a = 1f;
			bool dark = Luminance(background) <= 0.5f;
			Color toward = dark ? Color.white : Color.black;
			var t = new LookTokens {
				Night = background,
				Rail = Color.Lerp(background, dark ? Color.black : Color.white, 0.18f),
				Panel = Color.Lerp(background, toward, 0.035f),
				Raised = Color.Lerp(background, toward, 0.08f),
				Line = Color.Lerp(background, toward, 0.14f),
				Text = text,
				Accent = accent,
				AccentHover = Color.Lerp(accent, Color.white, 0.12f),
				AccentSoft = Color.Lerp(background, accent, 0.22f),
				AccentText = Color.Lerp(accent, toward, 0.55f),
			};
			if (muted is Color own) {
				own.a = 1f;
				t.Muted = own;
			} else {
				t.Muted = Color.Lerp(text, background, 0.4f);
			}
			Color b = button ?? accent;
			b.a = 1f;
			t.Button = b;
			t.ButtonHover = Color.Lerp(b, Color.white, 0.12f);
			t.ButtonSoft = Color.Lerp(background, b, 0.22f);
			t.ButtonText = Contrast(Color.white, b) >= Contrast(Rgb(0x141414), b) ? Color.white : Rgb(0x141414);
			return t;
		}

		/// <summary>How readable one colour is on another (WCAG): 1 is invisible, 4.5 is the floor for text, 21 is black on white.</summary>
		public static float Contrast(Color a, Color b) {
			float la = Relative(a), lb = Relative(b);
			return (Math.Max(la, lb) + 0.05f) / (Math.Min(la, lb) + 0.05f);
		}

		/// <summary>
		/// A text colour moved toward white (or black) until it reads at 4.5:1 on <paramref name="background"/>: the Look
		/// page's "Fix it". Returns <paramref name="text"/> as it is when it already reads.
		/// </summary>
		public static Color Readable(Color text, Color background, float floor = 4.5f) {
			Color toward = Luminance(background) <= 0.5f ? Color.white : Color.black;
			for (int step = 0; step <= 20 && Contrast(text, background) < floor; step++) {
				text = Color.Lerp(text, toward, 0.1f);
			}
			return text;
		}

		public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

		private static float Relative(Color c) => 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);

		private static float Linear(float v) => v <= 0.03928f ? v / 12.92f : (float)Math.Pow((v + 0.055f) / 1.055f, 2.4f);

		public static Color Rgb(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
	}
}
