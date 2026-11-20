// The HUD's own settings (hidden, size, which pop-ups show) and its presets: saving the whole HUD under a name and putting it back.
using System;
using System.Collections.Generic;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const string HudHiddenPref = "hud_hidden";
		private const string HudSizePref = "hud_size";
		private const string SectorFlashOnPref = "hud_sector_flash";
		private const string TrapFlashOnPref = "hud_trap_flash";
		private const string HudPresetsPref = "hud_presets";

		/// <summary>Everything the mod draws over the track is hidden (Hide HUD; the window and the quick menu still show).</summary>
		private bool hudHidden_;

		/// <summary>How big the boxes over the track are, on top of the window size (the window itself doesn't change).</summary>
		private float hudSize_ = 1f;
		private static readonly float[] HudSizes = { 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };

		private bool sectorFlashOn_ = true;
		private bool trapFlashOn_ = true;

		private List<HudPresetText.Preset> hudPresets_ = new List<HudPresetText.Preset>();

		private void LoadHudOptions() {
			hudHidden_ = Kino.Prefs.Get(HudHiddenPref, false);
			float size = Kino.Prefs.Get(HudSizePref, 1f);
			hudSize_ = HudSizes.OrderBy(s => Math.Abs(s - size)).First();
			sectorFlashOn_ = Kino.Prefs.Get(SectorFlashOnPref, true);
			trapFlashOn_ = Kino.Prefs.Get(TrapFlashOnPref, true);
			trafficScoreOn_ = Kino.Prefs.Get(TrafficScorePref, true);
			LoadTandemOption();
			hudPresets_ = HudPresetText.Parse(Kino.Prefs.Get(HudPresetsPref, ""));
		}

		private void SetHudHidden(bool hidden) {
			if (hidden == hudHidden_) {
				return;
			}
			hudHidden_ = hidden;
			SavePref(HudHiddenPref, hidden);
			TrcLog.Info(hidden ? "HUD hidden." : "HUD shown again.");
		}

		/// <summary>
		/// The ready-made presets (docs/archive/IMPROVEMENT_PLAN.md U4: Racing, Cruising, Streaming). They switch things on and off and set
		/// the size; they never move anything (your own presets do, with what was where when you saved them).
		/// </summary>
		private static readonly HudPresetText.Preset[] ReadyPresets = {
			new HudPresetText.Preset { Name = "Racing", Values = new Dictionary<string, string> {
				["hidden"] = "0", ["size"] = "1", ["sectors"] = "1", ["detail"] = "1", ["sector_flash"] = "1", ["trap_flash"] = "1",
				["badge"] = "1", ["tel_inputs"] = "1", ["tel_g"] = "0", ["tel_slip"] = "0",
			} },
			new HudPresetText.Preset { Name = "Cruising", Values = new Dictionary<string, string> {
				["hidden"] = "0", ["size"] = "1", ["sectors"] = "1", ["detail"] = "0", ["sector_flash"] = "1", ["trap_flash"] = "1",
				["badge"] = "0", ["tel_inputs"] = "0", ["tel_g"] = "0", ["tel_slip"] = "0",
			} },
			new HudPresetText.Preset { Name = "Streaming", Values = new Dictionary<string, string> {
				["hidden"] = "0", ["size"] = "0.9", ["sectors"] = "0", ["detail"] = "0", ["sector_flash"] = "0", ["trap_flash"] = "1",
				["badge"] = "0", ["tel_inputs"] = "0", ["tel_g"] = "0", ["tel_slip"] = "0",
			} },
		};

		/// <summary>The whole HUD as it is now: what shows, its size, and where every box is.</summary>
		private Dictionary<string, string> HudState() {
			return new Dictionary<string, string> {
				["hidden"] = HudPresetText.OnOff(hudHidden_),
				["size"] = hudSize_.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
				["sectors"] = HudPresetText.OnOff(showSectors_),
				["detail"] = HudPresetText.OnOff(timingDetailed_),
				["sector_flash"] = HudPresetText.OnOff(sectorFlashOn_),
				["trap_flash"] = HudPresetText.OnOff(trapFlashOn_),
				["badge"] = HudPresetText.OnOff(showBadge_),
				["badge_corner"] = badgeCorner_.ToString(System.Globalization.CultureInfo.InvariantCulture),
				["tel_inputs"] = HudPresetText.OnOff(telemetryInputs_),
				["tel_g"] = HudPresetText.OnOff(telemetryG_),
				["tel_slip"] = HudPresetText.OnOff(telemetrySlip_),
				["traffic_score"] = HudPresetText.OnOff(trafficScoreOn_),
				["tandem"] = HudPresetText.OnOff(tandemOn_),
				["place_timer"] = HudPresetText.Place(hudPosition_),
				["place_trap"] = HudPresetText.Place(trapFlashPosition_),
				["place_sector"] = HudPresetText.Place(sectorFlashPosition_),
				["place_notice"] = HudPresetText.Place(noticePosition_),
				["place_badge"] = HudPresetText.Place(badgePosition_),
				["place_suspension"] = HudPresetText.Place(suspensionPosition_),
				["place_telemetry"] = HudPresetText.Place(telemetryPosition_),
			};
		}

		/// <summary>Puts a preset's settings in place and saves them. Anything it doesn't have (or that's damaged) stays as it is.</summary>
		private void ApplyHudState(Dictionary<string, string> values) {
			string Get(string key) => values.TryGetValue(key, out string v) ? v : null;
			void Switch(string key, ref bool field, string pref) {
				if (HudPresetText.TryOnOff(Get(key), out bool on) && on != field) {
					field = on;
					SavePref(pref, on);
				}
			}
			if (HudPresetText.TryOnOff(Get("hidden"), out bool hidden)) {
				SetHudHidden(hidden);
			}
			if (HudPresetText.TryNumber(Get("size"), out float size)) {
				hudSize_ = HudSizes.OrderBy(s => Math.Abs(s - size)).First();
				SavePref(HudSizePref, hudSize_);
			}
			Switch("sectors", ref showSectors_, SectorsPref);
			Switch("detail", ref timingDetailed_, TimingDetailPref);
			Switch("sector_flash", ref sectorFlashOn_, SectorFlashOnPref);
			Switch("trap_flash", ref trapFlashOn_, TrapFlashOnPref);
			Switch("badge", ref showBadge_, BadgePref);
			Switch("traffic_score", ref trafficScoreOn_, TrafficScorePref);
			Switch("tandem", ref tandemOn_, TandemPref);
			if (HudPresetText.TryNumber(Get("badge_corner"), out float corner)) {
				badgeCorner_ = Mathf.Clamp((int)corner, 0, Corners.Length - 1);
				SavePref(BadgeCornerPref, badgeCorner_);
			}
			if (HudPresetText.TryOnOff(Get("tel_inputs"), out bool inputs)) {
				SetTelemetryPart(ref telemetryInputs_, inputs, TelemetryInputsPref, "Inputs, gear and RPM");
			}
			if (HudPresetText.TryOnOff(Get("tel_g"), out bool g)) {
				SetTelemetryPart(ref telemetryG_, g, TelemetryGPref, "G-force");
			}
			if (HudPresetText.TryOnOff(Get("tel_slip"), out bool slip)) {
				SetTelemetryPart(ref telemetrySlip_, slip, TelemetrySlipPref, "Wheel slip");
			}
			bool moved = false;
			void Place(string key, ref Vector2 field) {
				if (HudPresetText.TryPlace(Get(key), out Vector2 at)) {
					field = at;
					moved = true;
				}
			}
			Place("place_timer", ref hudPosition_);
			Place("place_trap", ref trapFlashPosition_);
			Place("place_sector", ref sectorFlashPosition_);
			Place("place_notice", ref noticePosition_);
			Place("place_badge", ref badgePosition_);
			Place("place_suspension", ref suspensionPosition_);
			Place("place_telemetry", ref telemetryPosition_);
			if (moved) {
				SaveScreenPlaces();
			}
		}

		/// <summary>Saves the HUD as it is now under <paramref name="name"/> (replacing one of that name). False when the name is empty or there are too many.</summary>
		private bool SaveHudPreset(string name, out string result) {
			name = HudPresetText.CleanName(name);
			if (name.Length == 0) {
				result = "Give the preset a name first.";
				return false;
			}
			if (ReadyPresets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) {
				result = $"{name} is one of the ready-made presets: pick another name.";
				return false;
			}
			int at = hudPresets_.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			if (at < 0 && hudPresets_.Count >= HudPresetText.MostPresets) {
				result = $"You have {HudPresetText.MostPresets} presets already: delete one first.";
				return false;
			}
			var preset = new HudPresetText.Preset { Name = name, Values = HudState() };
			if (at >= 0) {
				hudPresets_[at] = preset;
			} else {
				hudPresets_.Add(preset);
			}
			SavePref(HudPresetsPref, HudPresetText.Save(hudPresets_));
			TrcLog.Info($"HUD preset saved: {name}.");
			result = at >= 0 ? $"Preset {name} updated." : $"Preset {name} saved.";
			return true;
		}

		private void DeleteHudPreset(string name) {
			if (hudPresets_.RemoveAll(p => p.Name == name) > 0) {
				SavePref(HudPresetsPref, HudPresetText.Save(hudPresets_));
				TrcLog.Info($"HUD preset deleted: {name}.");
			}
		}
	}
}
