using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>HUD presets saved as text (Stage 2.3): a round trip, damaged lines, names, and places.</summary>
	internal static partial class SelfTest {
		private static void HudPresetTests() {
			var a = new HudPresetText.Preset { Name = "Race night", Values = new Dictionary<string, string> { ["badge"] = "1", ["place_timer"] = HudPresetText.Place(new Vector2(0.5f, 0.08f)) } };
			var b = new HudPresetText.Preset { Name = "Stream", Values = new Dictionary<string, string> { ["size"] = "0.9" } };
			List<HudPresetText.Preset> back = HudPresetText.Parse(HudPresetText.Save(new[] { a, b }));
			Expect(back.Count == 2 && back[0].Name == "Race night" && back[0].Values["badge"] == "1" && back[1].Values["size"] == "0.9", "HUD presets: saved and read back the same");
			Expect(HudPresetText.TryPlace(back[0].Values["place_timer"], out Vector2 place) && place == new Vector2(0.5f, 0.08f), "HUD presets: a place comes back exactly");

			Expect(HudPresetText.CleanName("  my\tpreset\n ") == "my preset", "HUD presets: tabs and line breaks in a name become spaces");
			Expect(HudPresetText.CleanName(new string('x', 50)).Length == HudPresetText.MostNameLength, "HUD presets: long names are cut");

			var odd = new HudPresetText.Preset { Name = "Odd", Values = new Dictionary<string, string> { ["good"] = "1", ["bad;key"] = "1", ["badvalue"] = "a=b" } };
			List<HudPresetText.Preset> oddBack = HudPresetText.Parse(HudPresetText.Save(new[] { odd }));
			Expect(oddBack.Count == 1 && oddBack[0].Values.Count == 1 && oddBack[0].Values.ContainsKey("good"), "HUD presets: a key or value that would break the line is left out");

			List<HudPresetText.Preset> damaged = HudPresetText.Parse("no tab here\n\tno name\nKeep\tbadge=1;junk\nkeep\tbadge=0\n");
			Expect(damaged.Count == 1 && damaged[0].Name == "Keep" && damaged[0].Values["badge"] == "1", "HUD presets: damaged lines skipped, the same name twice kept once (the first)");

			var many = new List<HudPresetText.Preset>();
			for (int i = 0; i < 20; i++) {
				many.Add(new HudPresetText.Preset { Name = "P" + i });
			}
			Expect(HudPresetText.Parse(HudPresetText.Save(many)).Count == HudPresetText.MostPresets, "HUD presets: at most 12 kept");

			Expect(!HudPresetText.TryPlace("1,NaN", out _) && !HudPresetText.TryPlace("1e9,0", out _) && !HudPresetText.TryPlace("0.5", out _), "HUD presets: broken places are refused");
			Expect(HudPresetText.Parse(null).Count == 0 && HudPresetText.Parse("").Count == 0, "HUD presets: nothing saved yet reads as none");
		}
	}
}
