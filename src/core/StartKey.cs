// The start key (and the TRC button): starts a time attack on the open track and teleports to its start line.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const string StartKeyPref = "start_shortcut";
		private Shortcut startShortcut_ = Shortcut.StartDefault;
		private bool capturingStart_;
		private bool startDown_;

		/// <summary>The track a start key or the TRC button starts: the one open in Tracks, else the last one driven on this map.</summary>
		private Course TrackToStart() {
			if (map_ == null) {
				return null;
			}
			List<Course> here = CoursesHere();
			Course open = tracksOpen_.Count == 1 ? here.FirstOrDefault(c => c.Code == tracksOpen_.First()) : null;
			return open ?? here.FirstOrDefault(c => c.Code == lastStartedCode_);
		}

		private string lastStartedCode_ = "";

		/// <summary>Starts a time attack on <see cref="TrackToStart"/> and teleports to its start line, or says why not.</summary>
		private void StartFromKey() {
			if (run_ != null) {
				RestartFromKey();
				return;
			}
			Course course = TrackToStart();
			if (course == null || !course.Usable) {
				ShowNotice("Open a track in Time attack, Tracks first: the start key starts that one.", 3f);
				return;
			}
			StartTimeAttack(course);
			if (run_ == null) {
				ShowNotice(courseResult_, 3f);
				return;
			}
			TeleportToStart();
			ShowNotice($"<b>{course.Name}</b>\n{(run_.Flying ? "Flying start" : "Standing start")}", 2f);
		}

		private void DrawStartKeyOption() {
			GUILayout.Space(6);
			GUILayout.Label(capturingStart_
				? "Press the new start key now. Press Esc to cancel."
				: $"<b>{startShortcut_}</b> starts a time attack on the track open in Tracks (or the last one you drove) and puts you on its start line, flying or standing as picked in Tracks.", Theme.Body);
			GUILayout.BeginHorizontal();
			if (capturingStart_) {
				if (NavButton("Cancel", Theme.Button)) {
					capturingStart_ = false;
					optionsResult_ = "Start key not changed.";
				}
			} else {
				if (NavButton("Change start key", Theme.Button)) {
					capturingStart_ = true;
					capturingShortcut_ = false;
					capturingRestart_ = false;
					optionsResult_ = "";
				}
				if (!startShortcut_.SameAs(Shortcut.StartDefault) && NavButton($"Reset to {Shortcut.StartDefault}", Theme.Button)) {
					SetStartShortcut(Shortcut.StartDefault);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void SetStartShortcut(Shortcut shortcut) {
			capturingStart_ = false;
			if (shortcut.SameAs(shortcut_) || shortcut.SameAs(restartShortcut_)) {
				optionsResult_ = $"{shortcut} is already used: pick another key to start.";
				return;
			}
			startShortcut_ = shortcut;
			SavePref(StartKeyPref, shortcut.Save());
			optionsResult_ = $"Start key set to {shortcut}.";
			TrcLog.Info($"Start key set to {shortcut}.");
		}

		/// <summary>From HandleShortcutEvent: picking the start key, or pressing it. True when the event was the start key's.</summary>
		private bool HandleStartKey(Event e) {
			if (e.type == EventType.KeyUp && e.keyCode == startShortcut_.Key) {
				startDown_ = false;
				return false;
			}
			if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) {
				return false;
			}
			if (capturingStart_) {
				if (e.keyCode == KeyCode.Escape) {
					capturingStart_ = false;
					optionsResult_ = "Start key not changed.";
				} else {
					Shortcut picked = Shortcut.FromEvent(e);
					if (picked == null) {
						return false;
					}
					SetStartShortcut(picked);
					startDown_ = true;
				}
				e.Use();
				return true;
			}
			if (startShortcut_.Matches(e) && !startDown_ && !TypingInGame() && !InBattle && !InRace) {
				startDown_ = true;
				StartFromKey();
				e.Use();
				return true;
			}
			return false;
		}
	}
}
