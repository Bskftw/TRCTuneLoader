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
			GUILayout.Label("Wheel and controller buttons", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Pick buttons CarX doesn't use: CarX still sees every press. The <b>TRC action</b> button accepts a challenge, joins a race that just opened, " +
				"runs the last battle or race back, or restarts your time attack, whichever fits.", Theme.Small);
			void Row(string label, ref WheelButtons.Binding binding, int which, string pref) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{label}</b>  <color={Theme.MutedHex}>{(capturingWheel_ == which ? "press a button on your wheel or controller…" : binding.ToString())}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
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
				if (binding.IsSet && NavButton("Clear", Theme.Button)) {
					binding = WheelButtons.Binding.None;
					SavePref(pref, "");
				}
				GUILayout.EndHorizontal();
			}
			Row("TRC action", ref wheelAction_, 1, WheelActionPref);
			Row("Open the window", ref wheelWindow_, 2, WheelWindowPref);
			if (wheel_.Problem.Length > 0) {
				GUILayout.Label(wheel_.Problem, Theme.PillWarn);
			} else if (capturingWheel_ != 0 && wheel_.Devices.Count == 0) {
				GUILayout.Label("CarX doesn't see a wheel or controller right now.", Theme.Small);
			}
			GUILayout.Space(6);
			DrawPadOptions();
			GUILayout.EndVertical();
		}

		private void DrawPadOptions() {
			GUILayout.Label("<b>Controller in the menu</b>: the D-pad moves between buttons, A presses, B goes back (or closes on Home), LB and RB change category. " +
				"Open the menu from the pad with a button set under Wheel and controller buttons.", Theme.Body);
			bool on = OnOff(padMenuOn_);
			if (on != padMenuOn_) {
				padMenuOn_ = on;
				SavePref(PadMenuPref, on);
				if (!on) {
					ReleasePad();
				}
			}
			GUILayout.Label("While the menu has the controller, keep the car still (CarX doesn't get the pad's buttons until the menu closes):", Theme.Small);
			bool hold = OnOff(padHoldCar_);
			if (hold != padHoldCar_) {
				padHoldCar_ = hold;
				SavePref(PadHoldPref, hold);
				if (!hold) {
					ReleasePad();
				}
			}
			if (pad_.Devices.Count > 0) {
				GUILayout.Label($"Controllers seen: {string.Join(", ", pad_.Devices)}{(padSeen_ ? "" : " (none with an A button yet)")}.", Theme.Small);
			} else if (!pad_.Available && pad_.Problem.Length > 0) {
				GUILayout.Label(pad_.Problem, Theme.Small);
			}
		}
	}
}
