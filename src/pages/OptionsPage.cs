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
		/// Options is four short pages in the menu's list since Stage 1 (IMPROVEMENT_PLAN.md U6: it was one long page), each
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
			GUILayout.Label("Shortcut", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label(capturingShortcut_
				? "Press the new shortcut now, for example Ctrl + T or F8. Press Esc to cancel."
				: $"Open or close this window with <b>{shortcut_}</b>.", Theme.Body);
			GUILayout.BeginHorizontal();
			if (capturingShortcut_) {
				if (NavButton("Cancel", Theme.Button)) {
					capturingShortcut_ = false;
					optionsResult_ = "Shortcut not changed.";
				}
			} else {
				if (NavButton("Change shortcut", Theme.ButtonPrimary)) {
					capturingShortcut_ = true;
					capturingRestart_ = false;
					optionsResult_ = "";
				}
				if (!shortcut_.SameAs(Shortcut.Default) && NavButton($"Reset to {Shortcut.Default}", Theme.Button)) {
					SetShortcut(Shortcut.Default);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(6);
			GUILayout.Label(capturingRestart_
				? "Press the new restart key now. Press Esc to cancel."
				: $"During a time attack, <b>{restartShortcut_}</b> sends your car back to the start line, ready to go again.", Theme.Body);
			GUILayout.BeginHorizontal();
			if (capturingRestart_) {
				if (NavButton("Cancel", Theme.Button)) {
					capturingRestart_ = false;
					optionsResult_ = "Restart key not changed.";
				}
			} else {
				if (NavButton("Change restart key", Theme.Button)) {
					capturingRestart_ = true;
					capturingShortcut_ = false;
					optionsResult_ = "";
				}
				if (!restartShortcut_.SameAs(Shortcut.RestartDefault) && NavButton($"Reset to {Shortcut.RestartDefault}", Theme.Button)) {
					SetRestartShortcut(Shortcut.RestartDefault);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			DrawStartKeyOption();
			if (!string.IsNullOrEmpty(optionsResult_)) {
				GUILayout.Label(optionsResult_, Theme.Small);
			}
			GUILayout.EndVertical();


			GUILayout.Space(10);
			DrawWheelOptions();
		}

		private void DrawOptionsLook() {
			GUILayout.Label("Window size", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Makes this window, the TRC spec badge and the pop-up bigger or smaller.", Theme.Body);
			int scaleIndex = Array.IndexOf(WindowScales, windowScale_);
			int pickedScale = Choice(WindowScales.Select(s => $"{s * 100:0}%").ToArray(), scaleIndex);
			if (pickedScale != scaleIndex) {
				windowScale_ = WindowScales[pickedScale];
				SavePref(WindowScalePref, windowScale_);
			}
			GUILayout.EndVertical();

			GUILayout.Space(10);
			GUILayout.Label("Look", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			DrawLookOptions();
			GUILayout.EndVertical();
		}
	}
}
