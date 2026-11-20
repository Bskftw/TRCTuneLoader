using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>UI redesign (Nov 18): every ready-made look is readable, the layers step the right way, "Fix it" fixes.</summary>
	internal static partial class SelfTest {
		private static void LookTests() {
			foreach (LookTokens.Preset p in LookTokens.Presets) {
				LookTokens t = LookTokens.From(p.Background, p.Accent, p.Text, p.Muted);
				float text = LookTokens.Contrast(t.Text, t.Panel), muted = LookTokens.Contrast(t.Muted, t.Panel), button = LookTokens.Contrast(t.ButtonText, t.Button);
				Expect(text >= 4.5f && muted >= 4.5f && button >= 4.5f,
					$"look {p.Name}: text {text:0.0}:1, muted {muted:0.0}:1 and button text {button:0.0}:1 on their backgrounds (4.5 or more)");
				Expect(LookTokens.Luminance(t.Rail) <= LookTokens.Luminance(t.Night) && LookTokens.Luminance(t.Night) <= LookTokens.Luminance(t.Panel) &&
					LookTokens.Luminance(t.Panel) < LookTokens.Luminance(t.Raised) && LookTokens.Luminance(t.Raised) < LookTokens.Luminance(t.Line),
					$"look {p.Name}: rail, window, panel, raised and line step lighter in order");
			}
			// A light background steps darker instead; a pale accent gets dark button text.
			LookTokens light = LookTokens.From(LookTokens.Rgb(0xF2F2F5), LookTokens.Rgb(0xFFE066), LookTokens.Rgb(0x202024));
			Expect(LookTokens.Luminance(light.Raised) < LookTokens.Luminance(light.Night) && light.ButtonText.r < 0.2f,
				"look: a light background steps darker, and a pale button gets dark text");
			// "Fix it": grey text on a dark grey background is brought up to readable.
			Color grey = LookTokens.Rgb(0x55555A), bg = LookTokens.Rgb(0x2A2A30);
			Expect(LookTokens.Contrast(grey, bg) < 4.5f && LookTokens.Contrast(LookTokens.Readable(grey, bg), bg) >= 4.5f, "look: Fix it makes hard-to-read text readable");
		}
	}
}
