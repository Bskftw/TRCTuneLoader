// The window's colour and text size settings: their defaults, loading them, applying them to the theme and saving them.
using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const string LookTextSizePref = "ui_text_size";
		private static readonly float[] TextSizes = { 0.9f, 1f, 1.15f, 1.3f, 1.5f };

		/// <summary>One colour the player can set.</summary>
		private sealed class LookColour {
			public string Key;
			public string Label;
			public string Note;
			public Color Default;

			/// <summary>Auto colours follow something else until the player picks one (null Own).</summary>
			public bool CanBeAuto;
			public Color? Own;

			public string Pref => "ui_" + Key;
		}

		private const string BackgroundKey = "background", TextKey = "text", MutedKey = "text_muted", AccentKey = "accent", ButtonKey = "button";

		/// <summary>The parts' colours, in the order of the menu's categories (TRC spec, Specs, Time attack, Battles, Free roam, Tools).</summary>
		private static readonly string[] PartKeys = { "part_trc", "part_specs", "part_time", "part_battles", "part_free", "part_tools" };

		private readonly LookColour[] lookColours_ = {
			new LookColour { Key = BackgroundKey, Label = "Background", Note = "the window", Default = Theme.DefaultBackground },
			new LookColour { Key = TextKey, Label = "Text", Note = "the main text", Default = Theme.DefaultText },
			new LookColour { Key = MutedKey, Label = "Second text", Note = "notes, labels, choices not picked", CanBeAuto = true },
			new LookColour { Key = AccentKey, Label = "Accent", Note = "page titles, note boxes, lines", Default = Theme.DefaultAccent },
			new LookColour { Key = ButtonKey, Label = "Buttons", Note = "main buttons, picked choices and rows", CanBeAuto = true },
			new LookColour { Key = PartKeys[0], Label = "TRC spec", Default = Rgb(0xF4C0D1) },
			new LookColour { Key = PartKeys[1], Label = "Specs", Default = Rgb(0x8FB8FF) },
			new LookColour { Key = PartKeys[2], Label = "Time attack", Default = Rgb(0xC9A7FF) },
			new LookColour { Key = PartKeys[3], Label = "Battles", Default = Rgb(0xFAC775) },
			new LookColour { Key = PartKeys[4], Label = "Free roam", Default = Rgb(0xC0DD97) },
			new LookColour { Key = PartKeys[5], Label = "Tools", Default = Rgb(0x40BAC4) },
		};

		/// <summary>The parts' colours as hex, for the menu (set whenever the look is applied).</summary>
		private static readonly string[] partHex_ = { "#F4C0D1", "#8FB8FF", "#C9A7FF", "#FAC775", "#C0DD97", "#40BAC4" };

		private float lookTextSize_ = 1f;
		private bool lookPending_;

		private LookColour LookOf(string key) => lookColours_.First(c => c.Key == key);

		/// <summary>The colour in use: the player's own, or the default (null for an Auto colour left on Auto).</summary>
		private Color? LookValue(string key) {
			LookColour c = LookOf(key);
			return c.Own ?? (c.CanBeAuto ? (Color?)null : c.Default);
		}

		/// <summary>What an Auto colour shows while on Auto: the second text from the text, buttons from the accent.</summary>
		private Color Shown(LookColour c) =>
			c.Own ?? (c.Key == MutedKey ? Color.Lerp(LookValue(TextKey).Value, LookValue(BackgroundKey).Value, 0.38f)
				: c.Key == ButtonKey ? LookValue(AccentKey).Value
				: c.Default);

		private void LoadLook() {
			foreach (LookColour c in lookColours_) {
				c.Own = Theme.TryHex(KSL.API.Kino.Prefs.Get(c.Pref, ""), out Color own) ? own : (Color?)null;
			}
			float size = KSL.API.Kino.Prefs.Get(LookTextSizePref, 1f);
			lookTextSize_ = TextSizes.OrderBy(s => Math.Abs(s - size)).First();
			ApplyLook();
		}

		private void ApplyLook() {
			Theme.Configure(LookValue(BackgroundKey).Value, LookValue(AccentKey).Value, LookValue(TextKey).Value, lookTextSize_, LookValue(MutedKey), LookValue(ButtonKey));
			for (int i = 0; i < PartKeys.Length; i++) {
				partHex_[i] = Theme.ToHex(LookValue(PartKeys[i]).Value);
			}
		}

		/// <summary>Called from Update: a changed look goes through here, between frames, and is saved.</summary>
		private void UpdateLook() {
			if (!lookPending_) {
				return;
			}
			lookPending_ = false;
			ApplyLook();
			foreach (LookColour c in lookColours_) {
				SavePref(c.Pref, c.Own is Color own ? Theme.ToHex(own) : "");
			}
			SavePref(LookTextSizePref, lookTextSize_);
		}
	}
}
