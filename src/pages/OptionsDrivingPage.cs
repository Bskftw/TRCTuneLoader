// Options > While driving: track check, manual sectors, best line, ghosts, gantries, map icons and speed trap signs (the HUD is Tools > HUD).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void DrawOptionsDriving() {
			BeginSection(null);
			SettingRow("The HUD", "Timer, sectors, pop-ups, the TRC spec badge, telemetry and the screen layout are in Tools > HUD", () => {
				if (NavButton("Open HUD", Theme.Button)) {
					OpenPage(Categories.SelectMany(c => c.Pages).First(p => p.Tab == HudTab));
				}
			}, first: true);
			EndSection();

			BeginSection("Checks");
			SettingRow("Check my car on the track", "A few seconds after you reach it and when it changes. A message only if it isn't TRC spec", () => {
				bool check = Switch(autoCheck_);
				if (check != autoCheck_) {
					autoCheck_ = check;
					SavePref(AutoCheckPref, autoCheck_);
				}
			}, first: true);
			DrawManualSectorsOption();
			EndSection();

			BeginSection("Time attack");
			SettingRow("Best line on the road", "Your best run's line in any car: red slowest, through amber and green, to blue fastest", () => {
				int lineMode = Choice(BestLineModes, bestLineMode_);
				if (lineMode != bestLineMode_) {
					bestLineMode_ = lineMode;
					SavePref(BestLinePref, bestLineMode_);
				}
			}, wide: true, first: true);
			SettingRow("Start and finish", "Gantries light up for 3, 2, 1, GO and show your time, or plain lines", () => {
				int lines = Choice(new[] { "Gantries", "Plain lines" }, CourseMarkers.UseGantries ? 0 : 1);
				if ((lines == 0) != CourseMarkers.UseGantries) {
					CourseMarkers.UseGantries = lines == 0;
					SavePref(GantriesPref, CourseMarkers.UseGantries);
				}
			});
			EndSection();

			BeginSection("Ghosts", "Recorded to race later or share (Time attack > Tracks > Ghosts). Ghosts you have and shared ones play either way.");
			SettingRow("Record", "", () => {
				int ghosts = Choice(GhostModes, ghostMode_);
				if (ghosts != ghostMode_) {
					ghostMode_ = ghosts;
					SavePref(GhostsPref, ghostMode_);
				}
			}, wide: true, first: true);
			SettingRow("How see-through", "", () => {
				int opacity = Choice(GhostOpacities, ghostOpacity_);
				if (opacity != ghostOpacity_) {
					ghostOpacity_ = opacity;
					SavePref(GhostOpacityPref, ghostOpacity_);
					CourseMarkers.SetGhostOpacity(GhostOpacityValues[ghostOpacity_]);
				}
			}, wide: true);
			EndSection();

			BeginSection("Map");
			SettingRow("Map icons", "Start, finish, sectors and traps on CarX's minimap and map", () => {
				bool icons = Switch(mapIconsOn_);
				if (icons != mapIconsOn_) {
					mapIconsOn_ = icons;
					SavePref(MapIconsPref, icons);
				}
			}, first: true);
			SettingRow("Speed trap signs show", "", () => {
				int signMode = Choice(new[] { "Your last speed", "Fastest in this car", "Fastest ever" }, trapSignMode_);
				if (signMode != trapSignMode_) {
					trapSignMode_ = signMode;
					SavePref(TrapSignPref, trapSignMode_);
				}
			}, wide: true);
			EndSection();
		}

		private void DrawManualSectorsOption() {
			SettingRow("Manual sectors", "Sectors you set on a track replace its automatic ones. Off keeps them but uses the automatic ones", () => {
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && run_ == null;
				bool on = Switch(Course.UseManualSectors);
				GUI.enabled = enabled;
				if (on != Course.UseManualSectors) {
					SetManualSectorsOn(on);
				}
			});
		}

		/// <summary>Tools > HUD: the telemetry's three parts, each off until switched on.</summary>
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
