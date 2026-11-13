// The controller in the menu: reading the pad's buttons, moving, pressing, going back, and the button bar.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const string PadMenuPref = "pad_menu";
		private const string PadHoldPref = "pad_hold_car";
		private bool padMenuOn_ = true;
		private bool padHoldCar_ = true;
		private readonly WheelButtons pad_ = new WheelButtons();
		private bool padSeen_;
		private bool padHolding_;

		private void LoadPadOptions() {
			padMenuOn_ = Kino.Prefs.Get(PadMenuPref, true);
			padHoldCar_ = Kino.Prefs.Get(PadHoldPref, true);
		}

		/// <summary>The menu's buttons, by what CarX's controller library calls them (Xbox and PlayStation pads).</summary>
		private static readonly string[][] PadNames = {
			new[] { "d-pad up", "dpad up", "up" },
			new[] { "d-pad down", "dpad down", "down" },
			new[] { "d-pad left", "dpad left", "left" },
			new[] { "d-pad right", "dpad right", "right" },
			new[] { "a", "cross", "button a" },
			new[] { "b", "circle", "button b" },
			new[] { "left shoulder", "left bumper", "l1", "lb" },
			new[] { "right shoulder", "right bumper", "r1", "rb" },
		};

		private bool PadPressed(int action) {
			foreach (WheelButtons.Binding pressed in pad_.Pressed) {
				if (pad_.ButtonNames.TryGetValue(pressed.Device, out string[] names) && pressed.Button < names.Length &&
					PadNames[action].Contains(names[pressed.Button].Trim().ToLowerInvariant())) {
					return true;
				}
			}
			return false;
		}

		/// <summary>Called from Update while the window is open: reads the pad and moves, presses or goes back.</summary>
		private void UpdatePadMenu() {
			if (!windowOpen_ || !padMenuOn_ || arranging_) {
				ReleasePad();
				return;
			}
			pad_.Poll();
			if (!padSeen_ && pad_.ButtonNames.Values.Any(names => names.Any(n => PadNames[4].Contains(n.Trim().ToLowerInvariant())))) {
				padSeen_ = true;
				TrcLog.Info($"Controller in the menu: {string.Join(", ", pad_.Devices)}.");
			}
			if (padSeen_ && padHoldCar_ && !padHolding_) {
				padHolding_ = pad_.SetDriving(false);
			}
			if (pad_.Pressed.Count == 0) {
				return;
			}
			bool any = true;
			if (PadPressed(0)) {
				navMove_ = Vector2.down;
			} else if (PadPressed(1)) {
				navMove_ = Vector2.up;
			} else if (PadPressed(2)) {
				navMove_ = Vector2.left;
			} else if (PadPressed(3)) {
				navMove_ = Vector2.right;
			} else if (PadPressed(4)) {
				if (navFocus_ >= 0 && Time.unscaledTime < navShownUntil_) {
					navPress_ = true;
				} else {
					navMove_ = Vector2.down;
				}
			} else if (PadPressed(5)) {
				if (CurrentCategory >= 0) {
					GoHome();
				} else {
					CloseWindow();
				}
			} else if (PadPressed(6) || PadPressed(7)) {
				int count = Categories.Length;
				int current = CurrentCategory;
				int next = current < 0 ? (PadPressed(7) ? 0 : count - 1) : (current + (PadPressed(7) ? 1 : count - 1)) % count;
				OpenCategory(next);
			} else {
				any = false;
			}
			if (any) {
				navShownUntil_ = Time.unscaledTime + 30f;
			}
		}

		/// <summary>Gives the pad back to CarX (window closed, or the option turned off).</summary>
		private void ReleasePad() {
			if (padHolding_) {
				pad_.SetDriving(true);
				padHolding_ = false;
			}
			navPress_ = false;
			navMove_ = Vector2.zero;
		}

		/// <summary>The bottom bar with the controller's buttons, like a console menu, once a pad has been seen.</summary>
		private void DrawPadBar() {
			if (!padMenuOn_ || !padSeen_) {
				return;
			}
			EnsureNavStyles();
			DividerLine();
			GUILayout.BeginHorizontal(TabArea);
			GUILayout.FlexibleSpace();
			PadHint("A", "Select");
			PadHint("B", CurrentCategory >= 0 ? "Home" : "Close");
			PadHint("LB", null);
			PadHint("RB", "Category");
			PadHint(shortcut_.ToString().Replace(" ", ""), "Close");
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private static void PadHint(string key, string what) {
			GUILayout.Label(key, keyCap_);
			if (what != null) {
				GUILayout.Label(what, Theme.Small);
				GUILayout.Space(14);
			}
		}
	}
}
