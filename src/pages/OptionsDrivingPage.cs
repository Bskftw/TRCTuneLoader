// Options > While driving: the badge, track check, sectors, best line, ghosts, speed trap signs, gantries, map icons.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void DrawOptionsDriving() {
			GUILayout.Label("While driving", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("<b>TRC spec badge</b>: a small tag in a corner of the screen. Green when your car is TRC spec, " +
				"amber when the car you're driving hasn't been checked yet (press Check my tune in the garage), red when something isn't spec. Off unless you turn it on.", Theme.Body);
			bool badge = OnOff(showBadge_);
			if (badge != showBadge_) {
				showBadge_ = badge;
				SavePref(BadgePref, showBadge_);
			}
			if (showBadge_) {
				int corner = Choice(Corners, badgeCorner_);
				if (corner != badgeCorner_) {
					badgeCorner_ = corner;
					SavePref(BadgeCornerPref, badgeCorner_);
				}
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Check my car on the track</b>: a few seconds after you reach the track, and whenever it changes while you drive, " +
				"your car is checked. A short message pops up only if something isn't TRC spec. Off unless you turn it on.", Theme.Body);
			bool check = OnOff(autoCheck_);
			if (check != autoCheck_) {
				autoCheck_ = check;
				SavePref(AutoCheckPref, autoCheck_);
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Sector times</b>: your sectors stay on the timer during a run (purple: great pace, green: on pace, amber: below pace), with the result and in My times.", Theme.Body);
			int sectors = Choice(new[] { "Show sector times", "Hide sector times" }, showSectors_ ? 0 : 1);
			if ((sectors == 0) != showSectors_) {
				showSectors_ = sectors == 0;
				SavePref(SectorsPref, showSectors_);
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Timing detail</b>: Simple compares every run with your best on the track in any car. Detailed also shows " +
				"your best in the car you're in and each sector's gaps, and at speed traps the fastest ever with the car that set it.", Theme.Body);
			int detail = Choice(new[] { "Simple", "Detailed" }, timingDetailed_ ? 1 : 0);
			if ((detail == 1) != timingDetailed_) {
				timingDetailed_ = detail == 1;
				SavePref(TimingDetailPref, timingDetailed_);
			}
			GUILayout.Space(8);
			DrawManualSectorsOption();
			GUILayout.Space(8);
			GUILayout.Label("<b>Best line</b>: during a time attack, where you drove on your best run (or best lap) on the track, in any car, is drawn on the road: " +
				"red where you were slowest, through amber and green to blue where you were fastest, or in one colour.", Theme.Body);
			int lineMode = Choice(BestLineModes, bestLineMode_);
			if (lineMode != bestLineMode_) {
				bestLineMode_ = lineMode;
				SavePref(BestLinePref, bestLineMode_);
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Ghosts</b>: your runs are recorded as ghosts to race later or share (Time attack, Tracks, Ghosts). " +
				"Keep your 3 best time attacks on each car and course, keep every run you finish (time attacks, battles and races), or record nothing. " +
				"Ghosts you already have, and ones others share, still play either way.", Theme.Body);
			int ghosts = Choice(GhostModes, ghostMode_);
			if (ghosts != ghostMode_) {
				ghostMode_ = ghosts;
				SavePref(GhostsPref, ghostMode_);
			}
			GUILayout.BeginHorizontal();
			GUILayout.Label("How see-through", Theme.Small, GUILayout.Width(110));
			int opacity = Choice(GhostOpacities, ghostOpacity_);
			GUILayout.EndHorizontal();
			if (opacity != ghostOpacity_) {
				ghostOpacity_ = opacity;
				SavePref(GhostOpacityPref, ghostOpacity_);
				CourseMarkers.SetGhostOpacity(GhostOpacityValues[ghostOpacity_]);
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Start and finish</b>: gantries with signs that light up for 3, 2, 1, GO and show your time at the finish, or plain lines on the road.", Theme.Body);
			int lines = Choice(new[] { "Gantries", "Plain lines" }, CourseMarkers.UseGantries ? 0 : 1);
			if ((lines == 0) != CourseMarkers.UseGantries) {
				CourseMarkers.UseGantries = lines == 0;
				SavePref(GantriesPref, CourseMarkers.UseGantries);
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Map icons</b>: the course's start (green), finish (pink), sectors (lime) and speed traps (amber) on CarX's minimap and map, for the course you're driving or the track open in Tracks.", Theme.Body);
			bool icons = OnOff(mapIconsOn_);
			if (icons != mapIconsOn_) {
				mapIconsOn_ = icons;
				SavePref(MapIconsPref, icons);
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Speed trap signs</b> show the speed you went through each trap at:", Theme.Body);
			int signMode = Choice(new[] { "Your last speed", "Fastest in this car", "Fastest ever" }, trapSignMode_);
			if (signMode != trapSignMode_) {
				trapSignMode_ = signMode;
				SavePref(TrapSignPref, trapSignMode_);
			}
			GUILayout.Space(8);
			DrawTelemetryOptions();
			GUILayout.Space(8);
			GUILayout.Label("<b>Screen layout</b>: move the timer (time attacks, races and battles), the speed trap flash, pop-up notices, the TRC spec badge, the suspension overlay and the telemetry " +
				"wherever you like, all at once, without starting anything first.", Theme.Body);
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

		private void DrawManualSectorsOption() {
			GUILayout.Label("<b>Manual sectors</b>: sectors you set yourself on a track (Tracks, open a track, Sectors) replace its automatic ones. Off keeps them but uses the automatic ones.", Theme.Body);
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && run_ == null;
			bool on = OnOff(Course.UseManualSectors);
			GUI.enabled = enabled;
			if (on != Course.UseManualSectors) {
				SetManualSectorsOn(on);
			}
		}

		/// <summary>Options > While driving: the three parts, each off until switched on.</summary>
		private void DrawTelemetryOptions() {
			GUILayout.Label("<b>Telemetry</b>: a small panel over the track with your own car's readings, from CarX's physics. " +
				"Each part is off until you turn it on. Slip compares each wheel with the slip its tyre grips best at: green still gripping, amber past it, red well past it.", Theme.Body);
			TelemetrySwitch("Inputs, gear and RPM", ref telemetryInputs_, TelemetryInputsPref);
			TelemetrySwitch("G-force", ref telemetryG_, TelemetryGPref);
			TelemetrySwitch("Wheel slip", ref telemetrySlip_, TelemetrySlipPref);
		}

		private void TelemetrySwitch(string name, ref bool part, string pref) {
			GUILayout.BeginHorizontal();
			GUILayout.Label(name, Theme.Small, GUILayout.Width(160));
			bool on = OnOff(part);
			GUILayout.EndHorizontal();
			SetTelemetryPart(ref part, on, pref, name);
		}
	}
}
