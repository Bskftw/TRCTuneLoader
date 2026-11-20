// The Options page: its four pages in the menu's list, and the Keys and Look pages.
using System;
using System.Collections.Generic;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const int OptionsTab = 9;

		private string optionsResult_ = "";

		private Vector2 optionsScroll_;

		/// <summary>
		/// Options is four short pages in the menu's list since Stage 1 (docs/archive/IMPROVEMENT_PLAN.md U6: it was one long page), each
		/// drawing the same settings as before: 0 keys and buttons, 1 look, 2 while driving, 3 sharing and backups.
		/// Report a bug has its own page in Tools, so it's no longer repeated at the bottom here.
		/// </summary>
		private int optionsView_;

		private void DrawOptionsTab() {
			optionsScroll_ = GUILayout.BeginScrollView(optionsScroll_);
			switch (optionsView_) {
				case 1: DrawOptionsLook(); break;
				case 2: DrawOptionsDriving(); break;
				case 3: DrawOptionsSharing(); break;
				default: DrawOptionsKeys(); break;
			}
			GUILayout.EndScrollView();
		}

		private void DrawOptionsKeys() {
			BeginSection("Keys", "Any key, or one with Ctrl, Shift or Alt. Esc cancels while picking.");
			KeyRow("Open and close this window", "", shortcut_.ToString(), capturingShortcut_,
				() => { capturingShortcut_ = true; capturingRestart_ = false; capturingQuick_ = false; optionsResult_ = ""; },
				() => { capturingShortcut_ = false; optionsResult_ = "Shortcut not changed."; },
				!shortcut_.SameAs(Shortcut.Default), () => SetShortcut(Shortcut.Default), first: true);
			KeyRow("Restart a time attack", "Back to the start line, ready to go again", restartShortcut_.ToString(), capturingRestart_,
				() => { capturingRestart_ = true; capturingShortcut_ = false; capturingQuick_ = false; optionsResult_ = ""; },
				() => { capturingRestart_ = false; optionsResult_ = "Restart key not changed."; },
				!restartShortcut_.SameAs(Shortcut.RestartDefault), () => SetRestartShortcut(Shortcut.RestartDefault));
			DrawStartKeyOption();
			DrawQuickKeyOption();
			if (!string.IsNullOrEmpty(optionsResult_)) {
				GUILayout.Label(optionsResult_, Theme.SectionNote);
			}
			EndSection();
			DrawWheelOptions();
		}

		/// <summary>
		/// A key setting: its name and what it does on the left, the key itself as a button (press to pick another) and
		/// Reset on the right. While picking, it says so and offers Cancel. The same steps as the buttons it replaced.
		/// </summary>
		private void KeyRow(string label, string note, string key, bool capturing, Action change, Action cancel, bool canReset, Action reset, bool first = false) {
			SettingRow(label, capturing ? "Press the new key now. Esc cancels." : note, () => {
				if (capturing) {
					if (NavButton("Cancel", Theme.ButtonQuiet)) {
						cancel();
					}
					return;
				}
				if (canReset && NavButton("Reset", Theme.ButtonQuiet)) {
					reset();
				}
				if (NavButton(key, Theme.Button)) {
					change();
				}
			}, first: first);
		}

		private void DrawOptionsLook() {
			DrawLookOptions();
			BeginSection("Size");
			int scaleIndex = Array.IndexOf(WindowScales, windowScale_);
			SettingRow("Window size", "This window, the TRC spec badge and the pop-up", () => {
				int pickedScale = Choice(WindowScales.Select(s => $"{s * 100:0}%").ToArray(), scaleIndex);
				if (pickedScale != scaleIndex) {
					windowScale_ = WindowScales[pickedScale];
					SavePref(WindowScalePref, windowScale_);
				}
			}, wide: true, first: true);
			DrawTextSizeRow();
			EndSection();
		}
	}
}
