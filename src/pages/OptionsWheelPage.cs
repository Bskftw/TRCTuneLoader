// Options for the wheel and controller: picking the TRC action button and the window button, and the controller in the menu.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void DrawWheelOptions() {
			BeginSection("Wheel and controller buttons", "Buttons CarX doesn't use. CarX still sees every press.");
			WheelRow("TRC action", "Accepts a challenge, joins a race that just opened, runs it back, or restarts your time attack", wheelAction_, 1,
				() => { wheelAction_ = WheelButtons.Binding.None; SavePref(WheelActionPref, ""); }, first: true);
			WheelRow("Open the window", "", wheelWindow_, 2, () => { wheelWindow_ = WheelButtons.Binding.None; SavePref(WheelWindowPref, ""); });
			WheelRow("Quick menu", "Press again to move along; it happens once you stop pressing", wheelQuick_, 3,
				() => { wheelQuick_ = WheelButtons.Binding.None; SavePref(WheelQuickPref, ""); });
			if (wheel_.Problem.Length > 0) {
				GUILayout.Label(wheel_.Problem, Theme.PillWarn);
			} else if (capturingWheel_ != 0 && wheel_.Devices.Count == 0) {
				GUILayout.Label("CarX doesn't see a wheel or controller right now.", Theme.SectionNote);
			}
			EndSection();
			DrawPadOptions();
		}

		/// <summary>One wheel button: what it's set to, Pick a button (or Cancel while waiting) and Clear, as before.</summary>
		private void WheelRow(string label, string note, WheelButtons.Binding binding, int which, Action clear, bool first = false) {
			string now = capturingWheel_ == which ? "Press a button on your wheel or controller…" : binding.ToString();
			SettingRow(label, note.Length > 0 ? $"{now}. {note}" : now, () => {
				if (binding.IsSet && capturingWheel_ != which && NavButton("Clear", Theme.ButtonQuiet)) {
					clear();
				}
				if (NavButton(capturingWheel_ == which ? "Cancel" : "Pick a button", Theme.Button)) {
					if (capturingWheel_ == which) {
						capturingWheel_ = 0;
					} else {
						capturingWheel_ = which;
						captureWheelUntil_ = Time.unscaledTime + 10f;
						wheel_.Poll();
						optionsResult_ = "";
					}
				}
			}, first: first);
		}

		private void DrawPadOptions() {
			BeginSection("Controller in the menu", "The D-pad moves between buttons, A presses, B goes back, LB and RB change part.");
			SettingRow("Use the controller here", "Open the menu from the pad with a button above", () => {
				bool on = Switch(padMenuOn_);
				if (on != padMenuOn_) {
					padMenuOn_ = on;
					SavePref(PadMenuPref, on);
					if (!on) {
						ReleasePad();
					}
				}
			}, first: true);
			SettingRow("Keep the car still", "While the menu has the controller (CarX gets the pad back when it closes)", () => {
				bool hold = Switch(padHoldCar_);
				if (hold != padHoldCar_) {
					padHoldCar_ = hold;
					SavePref(PadHoldPref, hold);
					if (!hold) {
						ReleasePad();
					}
				}
			});
			if (pad_.Devices.Count > 0) {
				GUILayout.Label($"Controllers seen: {string.Join(", ", pad_.Devices)}{(padSeen_ ? "" : " (none with an A button yet)")}.", Theme.SectionNote);
			} else if (!pad_.Available && pad_.Problem.Length > 0) {
				GUILayout.Label(pad_.Problem, Theme.SectionNote);
			}
			EndSection();
		}
	}
}
