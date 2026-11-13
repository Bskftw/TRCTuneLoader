// The keyboard shortcuts: opening the window (and the restart key), matched against Unity's key events, never while typing in CarX's chat.
using System;
using System.Collections.Generic;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private Shortcut shortcut_ = Shortcut.Default;
		private bool capturingShortcut_;
		private bool shortcutDown_;

		/// <summary>The key that sends the car back to the start line during a time attack, ready to go again.</summary>
		private Shortcut restartShortcut_ = Shortcut.RestartDefault;
		private bool capturingRestart_;
		private bool restartDown_;

		/// <summary>Called from OnGUI for every event: opens or closes the window, or records a new shortcut in Options.</summary>
		private void HandleShortcutEvent(Event e) {
			if (e == null) {
				return;
			}
			if (HandleStartKey(e)) {
				return;
			}
			if (e.type == EventType.KeyUp && e.keyCode == shortcut_.Key) {
				shortcutDown_ = false;
				return;
			}
			if (e.type == EventType.KeyUp && e.keyCode == restartShortcut_.Key) {
				restartDown_ = false;
				return;
			}
			if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) {
				return;
			}

			if (capturingRestart_) {
				if (e.keyCode == KeyCode.Escape) {
					capturingRestart_ = false;
					optionsResult_ = "Restart key not changed.";
					e.Use();
					return;
				}
				Shortcut picked = Shortcut.FromEvent(e);
				if (picked != null) {
					SetRestartShortcut(picked);
					restartDown_ = true;
					e.Use();
				}
				return;
			}

			if (capturingShortcut_) {
				if (e.keyCode == KeyCode.Escape) {
					capturingShortcut_ = false;
					optionsResult_ = "Shortcut not changed.";
					e.Use();
					return;
				}
				Shortcut picked = Shortcut.FromEvent(e);
				if (picked != null) {
					SetShortcut(picked);
					shortcutDown_ = true;
					e.Use();
				}
				return;
			}

			if (restartShortcut_.Matches(e) && !restartDown_ && run_ != null && !TypingInGame()) {
				restartDown_ = true;
				RestartFromKey();
				e.Use();
				return;
			}

			// Holding a key sends repeated key presses; only the first one counts.
			if (!shortcut_.Matches(e) || shortcutDown_ || TypingInGame()) {
				return;
			}
			shortcutDown_ = true;
			ToggleWindow();
			e.Use();
		}

		private void OnApplicationFocus(bool focused) {
			// A key released while the game was in the background never sends its key-up.
			shortcutDown_ = false;
			restartDown_ = false;
			startDown_ = false;
		}

		private void SetRestartShortcut(Shortcut shortcut) {
			capturingRestart_ = false;
			if (shortcut.SameAs(shortcut_)) {
				optionsResult_ = $"{shortcut} already opens this window: pick another key for restart.";
				return;
			}
			restartShortcut_ = shortcut;
			SavePref(RestartPref, shortcut.Save());
			optionsResult_ = $"Restart key set to {shortcut}.";
			TrcLog.Info($"Restart key set to {shortcut}.");
		}

		private void SetShortcut(Shortcut shortcut) {
			shortcut_ = shortcut;
			capturingShortcut_ = false;
			bool saved = false;
			try {
				saved = Kino.Prefs.Set(ShortcutPref, shortcut.Save());
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't save the shortcut: {e.Message}");
			}

			bool plainKey = !shortcut.Ctrl && !shortcut.Shift && !shortcut.Alt && shortcut.Key >= KeyCode.A && shortcut.Key <= KeyCode.Z;
			optionsResult_ = $"Shortcut set to {shortcut}." +
				(saved ? "" : " It couldn't be saved, so it goes back to the old one when the game restarts.") +
				(plainKey ? " Careful: a letter on its own may also do something in CarX. Ctrl + a letter is safer." : "");
			TrcLog.Info($"Shortcut set to {shortcut}.");
		}

		/// <summary>True while one of CarX's text boxes (like the chat) has the keyboard, so typing a message doesn't open or close the window.</summary>
		private static bool TypingInGame() {
			try {
				object eventSystem = CurrentEventSystem?.GetValue(null, null);
				var selected = eventSystem?.GetType().GetProperty("currentSelectedGameObject")?.GetValue(eventSystem, null) as GameObject;
				if (selected == null) {
					return false;
				}
				foreach (Component component in selected.GetComponents<Component>()) {
					Type type = component?.GetType();
					if (type != null && type.Name.Contains("InputField") &&
						type.GetProperty("isFocused")?.GetValue(component, null) is bool focused && focused) {
						return true;
					}
				}
			} catch (Exception) {
				// Not knowing just means the shortcut works as usual.
			}
			return false;
		}
	}
}
