// Tools > HUD: everything the mod shows over the track in one place, with its size, where it goes, presets and the quick menu.
using System;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// One HUD page (docs/archive/IMPROVEMENT_PLAN.md U5): every box over the track with its on and off, one size for them all, Arrange the
	/// screen beside them, and presets (U4). These used to be spread over Options, While driving and their own pages.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int HudTab = 16;

		private Vector2 hudScroll_;
		private string hudPresetName_ = "";
		private string hudResult_ = "";

		private void DrawHudTab() {
			hudScroll_ = GUILayout.BeginScrollView(hudScroll_);
			DrawHudShowing();
			GUILayout.Space(10);
			DrawHudParts();
			GUILayout.Space(10);
			DrawHudPresets();
			GUILayout.EndScrollView();
		}

		private void DrawHudShowing() {
			GUILayout.Label("HUD", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Everything the mod shows over the track: the timer, pop-ups, the TRC spec badge, telemetry and the suspension overlay. " +
				$"<b>Hide HUD</b> takes it all away (for clips and streams); the quick menu (<b>{quickShortcut_}</b>, or a button set in Options, Keys and buttons) brings it back.", Theme.Body);
			bool shown = OnOff(!hudHidden_);
			if (shown == hudHidden_) {
				SetHudHidden(!shown);
			}
			GUILayout.Space(6);
			GUILayout.Label("<b>Size</b> of everything over the track (the window has its own size in Options, Look):", Theme.Body);
			int sizeIndex = Array.IndexOf(HudSizes, hudSize_);
			int pickedSize = Choice(HudSizes.Select(s => $"{s * 100:0}%").ToArray(), sizeIndex);
			if (pickedSize != sizeIndex) {
				hudSize_ = HudSizes[pickedSize];
				SavePref(HudSizePref, hudSize_);
			}
			GUILayout.Space(6);
			GUILayout.Label("<b>Where things go</b>: drag every box where you want it, all at once, without starting anything first.", Theme.Body);
			GUILayout.BeginHorizontal();
			if (NavButton("Arrange the screen", Theme.ButtonPrimary)) {
				StartArranging();
			}
			if (NavButton("Put everything back", Theme.Button)) {
				ResetLayout();
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		/// <summary>One row: a name and what it is on the left, its switch on the right.</summary>
		private bool HudSwitch(string name, string what, bool on) {
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<b>{name}</b>  <color={Theme.MutedHex}>{what}</color>", Theme.Body);
			GUILayout.FlexibleSpace();
			bool now = OnOff(on);
			GUILayout.EndHorizontal();
			return now;
		}

		private void DrawHudParts() {
			GUILayout.Label("What shows", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("<b>Timer</b> (time attacks, races and battles) always shows while you drive one.", Theme.Body);
			bool sectors = HudSwitch("Sector times on the timer", "purple great pace, green on pace, amber below", showSectors_);
			if (sectors != showSectors_) {
				showSectors_ = sectors;
				SavePref(SectorsPref, sectors);
			}
			GUILayout.Label("<b>Timing detail</b>: Simple compares every run with your best on the track in any car. Detailed also shows " +
				"your best in the car you're in and each sector's gaps, and at speed traps the fastest ever with the car that set it.", Theme.Body);
			int detail = Choice(new[] { "Simple", "Detailed" }, timingDetailed_ ? 1 : 0);
			if ((detail == 1) != timingDetailed_) {
				timingDetailed_ = detail == 1;
				SavePref(TimingDetailPref, timingDetailed_);
			}
			GUILayout.Space(6);
			bool sectorFlash = HudSwitch("Sector pop-up", "your time and gap at each sector", sectorFlashOn_);
			if (sectorFlash != sectorFlashOn_) {
				sectorFlashOn_ = sectorFlash;
				SavePref(SectorFlashOnPref, sectorFlash);
			}
			bool trapFlash = HudSwitch("Speed trap pop-up", "your speed through a trap", trapFlashOn_);
			if (trapFlash != trapFlashOn_) {
				trapFlashOn_ = trapFlash;
				SavePref(TrapFlashOnPref, trapFlash);
			}
			GUILayout.Label($"<b>Pop-up notices</b>  <color={Theme.MutedHex}>what a key or button did, shared courses: always on (Hide HUD hides them too)</color>", Theme.Body);
			GUILayout.Space(6);
			bool badge = HudSwitch("TRC spec badge", "green spec, amber not checked, red not spec", showBadge_);
			if (badge != showBadge_) {
				showBadge_ = badge;
				SavePref(BadgePref, badge);
			}
			if (showBadge_) {
				int corner = Choice(Corners, badgeCorner_);
				if (corner != badgeCorner_) {
					badgeCorner_ = corner;
					SavePref(BadgeCornerPref, badgeCorner_);
				}
			}
			bool tandem = HudSwitch("Tandem aids", "the gap to the car next to you, both drift angles, how long you held it close", tandemOn_);
			if (tandem != tandemOn_) {
				tandemOn_ = tandem;
				SavePref(TandemPref, tandem);
			}
			bool places = HudSwitch("Map places", "boards with a track's times, parking areas, territory colours (on maps that have them)", placesOn_);
			if (places != placesOn_) {
				placesOn_ = places;
				SavePref(PlacesPref, places);
			}
			bool trafficScore = HudSwitch("Traffic score", "near misses and the combo, while traffic runs", trafficScoreOn_);
			if (trafficScore != trafficScoreOn_) {
				trafficScoreOn_ = trafficScore;
				SavePref(TrafficScorePref, trafficScore);
			}
			GUILayout.Space(6);
			DrawTelemetryOptions();
			GUILayout.Space(6);
			bool overlay = HudSwitch("Suspension overlay", "while the suspension logger runs (Tools, Suspension logger)", suspensionOverlay_);
			if (overlay != suspensionOverlay_) {
				suspensionOverlay_ = overlay;
				SavePref(SuspensionOverlayPref, overlay);
			}
			GUILayout.EndVertical();
		}

		private void DrawHudPresets() {
			GUILayout.Label("Presets", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Ready-made: they switch things on and off and set the size, and leave everything where you put it.", Theme.Small);
			GUILayout.BeginHorizontal();
			foreach (HudPresetText.Preset ready in ReadyPresets) {
				if (NavButton(ready.Name, Theme.Button)) {
					ApplyHudState(ready.Values);
					hudResult_ = $"{ready.Name} preset on.";
					TrcLog.Info($"HUD preset used: {ready.Name}.");
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(6);
			GUILayout.Label("Your own: everything on this page as it is now, and where every box is.", Theme.Small);
			HudPresetText.Preset use = null, delete = null;
			foreach (HudPresetText.Preset preset in hudPresets_) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{preset.Name}</b>", Theme.Body);
				GUILayout.FlexibleSpace();
				if (NavButton("Use", Theme.Button)) {
					use = preset;
				}
				if (NavButton("Delete", Theme.Button)) {
					delete = preset;
				}
				GUILayout.EndHorizontal();
			}
			if (use != null) {
				ApplyHudState(use.Values);
				hudResult_ = $"{use.Name} preset on.";
				TrcLog.Info($"HUD preset used: {use.Name}.");
			}
			if (delete != null) {
				DeleteHudPreset(delete.Name);
				hudResult_ = $"Deleted {delete.Name}.";
			}
			GUILayout.BeginHorizontal();
			hudPresetName_ = GUILayout.TextField(hudPresetName_ ?? "", HudPresetText.MostNameLength, Theme.Field, GUILayout.Width(220));
			if (NavButton("Save as a preset", Theme.ButtonPrimary) && SaveHudPreset(hudPresetName_, out hudResult_)) {
				hudPresetName_ = "";
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (hudResult_.Length > 0) {
				GUILayout.Label(hudResult_, Theme.Small);
			}
			GUILayout.EndVertical();
		}
	}
}
