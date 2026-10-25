using System;
using System.Collections.Generic;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The Options tab, the shortcut that opens the window, and the first-run tour. Settings are saved
	/// with KSL's prefs (Kino.Prefs), which keeps them per mod between game sessions.
	///
	/// KSL has no way for a mod to change its own keybind, so the shortcut is handled here instead of
	/// with Kino.Input: it's matched in OnGUI against Unity's key events.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int OptionsTab = 6;
		private const string ShortcutPref = "open_shortcut";
		private const string RestartPref = "restart_shortcut";
		private const string ShareValuesPref = "share_tune_values";

		/// <summary>Let other TRC Tune Loader users see this player's tune values on their Players page (off by default).</summary>
		private bool shareValues_;
		private const string TourPref = "tour_seen";
		private const string WindowScalePref = "window_scale";
		private const string BadgePref = "badge";
		private const string BadgeCornerPref = "badge_corner";
		private const string AutoCheckPref = "auto_check";
		private const string BackupsKeepPref = "backups_keep";
		private const string ProblemsOnlyPref = "players_problems_only";

		private static readonly float[] WindowScales = { 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };
		private static readonly string[] Corners = { "Top left", "Top right", "Bottom left", "Bottom right" };

		/// <summary>How many backups to keep per car and setup; 0 keeps them all.</summary>
		private static readonly int[] BackupLimits = { 0, 5, 10, 20 };

		/// <summary>Raise this when the tour changes enough that everyone should see it again. 2: TRC and Project Dream, time attack and battles.</summary>
		private const int TourVersion = 2;

		/// <summary>How long the player is on the garage screen before the first-run tour opens.</summary>
		private const float TourGarageDelaySeconds = 2f;

		private Shortcut shortcut_ = Shortcut.Default;
		private bool capturingShortcut_;
		private bool shortcutDown_;

		/// <summary>The key that sends the car back to the start line during a time attack, ready to go again.</summary>
		private Shortcut restartShortcut_ = Shortcut.RestartDefault;
		private bool capturingRestart_;
		private bool restartDown_;
		private string optionsResult_ = "";

		/// <summary>Size of the window, badge and pop-up, on top of KSL's own UI scale.</summary>
		private float windowScale_ = 1f;
		private bool showBadge_ = true;
		private int badgeCorner_ = 1;
		private bool autoCheck_ = true;
		private int backupsKeep_;
		private bool problemsOnly_;

		/// <summary>Old backups the Clean up now button found, waiting for the second click that deletes them.</summary>
		private List<TuneFile> cleanupPending_;
		private string backupOptionsResult_ = "";
		private Vector2 optionsScroll_;

		private int tourStep_ = -1;
		private bool tourSeen_;
		private float garageSince_ = -1f;

		private sealed class TourStep {
			public string Title;
			public string Text;

			/// <summary>The tab shown under the step, or -1 for none.</summary>
			public int Tab;
		}

		private void LoadOptions() {
			try {
				shortcut_ = Shortcut.Parse(Kino.Prefs.Get(ShortcutPref, Shortcut.Default.Save()));
				restartShortcut_ = Shortcut.Parse(Kino.Prefs.Get(RestartPref, Shortcut.RestartDefault.Save()), Shortcut.RestartDefault);
				tourSeen_ = Kino.Prefs.Get(TourPref, 0) >= TourVersion;
				float scale = Kino.Prefs.Get(WindowScalePref, 1f);
				windowScale_ = WindowScales.OrderBy(s => Math.Abs(s - scale)).First();
				showBadge_ = Kino.Prefs.Get(BadgePref, true);
				badgeCorner_ = Mathf.Clamp(Kino.Prefs.Get(BadgeCornerPref, 1), 0, Corners.Length - 1);
				autoCheck_ = Kino.Prefs.Get(AutoCheckPref, true);
				int keep = Kino.Prefs.Get(BackupsKeepPref, 0);
				backupsKeep_ = BackupLimits.Contains(keep) ? keep : 0;
				problemsOnly_ = Kino.Prefs.Get(ProblemsOnlyPref, false);
				showSectors_ = Kino.Prefs.Get(SectorsPref, true);
				bestLineMode_ = Mathf.Clamp(Kino.Prefs.Get(BestLinePref, 0), 0, 2);
				shareValues_ = Kino.Prefs.Get(ShareValuesPref, false);
				Vector2 timer = Kino.Prefs.Get(HudPositionPref, DefaultHudPosition);
				hudPosition_ = new Vector2(Mathf.Clamp01(timer.x), Mathf.Clamp01(timer.y));
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't read the TRC Tune Loader options: {e.Message}");
			}
		}

		private static void SavePref<T>(string key, T value) {
			try {
				if (!Kino.Prefs.Set(key, value)) {
					TrcLog.Warning($"Couldn't save the option '{key}'.");
				}
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't save the option '{key}': {e.Message}");
			}
		}

		// ---- Shortcut ----

		/// <summary>Called from OnGUI for every event: opens or closes the window, or records a new shortcut in Options.</summary>
		private void HandleShortcutEvent(Event e) {
			if (e == null) {
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

		// ---- Options tab ----

		private void DrawOptionsTab() {
			optionsScroll_ = GUILayout.BeginScrollView(optionsScroll_);

			GUILayout.Label("Shortcut", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label(capturingShortcut_
				? "Press the new shortcut now, for example Ctrl + T or F8. Press Esc to cancel."
				: $"Open or close this window with <b>{shortcut_}</b>.", Theme.Body);
			GUILayout.BeginHorizontal();
			if (capturingShortcut_) {
				if (GUILayout.Button("Cancel", Theme.Button)) {
					capturingShortcut_ = false;
					optionsResult_ = "Shortcut not changed.";
				}
			} else {
				if (GUILayout.Button("Change shortcut", Theme.ButtonPrimary)) {
					capturingShortcut_ = true;
					capturingRestart_ = false;
					optionsResult_ = "";
				}
				if (!shortcut_.SameAs(Shortcut.Default) && GUILayout.Button($"Reset to {Shortcut.Default}", Theme.Button)) {
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
				if (GUILayout.Button("Cancel", Theme.Button)) {
					capturingRestart_ = false;
					optionsResult_ = "Restart key not changed.";
				}
			} else {
				if (GUILayout.Button("Change restart key", Theme.Button)) {
					capturingRestart_ = true;
					capturingShortcut_ = false;
					optionsResult_ = "";
				}
				if (!restartShortcut_.SameAs(Shortcut.RestartDefault) && GUILayout.Button($"Reset to {Shortcut.RestartDefault}", Theme.Button)) {
					SetRestartShortcut(Shortcut.RestartDefault);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!string.IsNullOrEmpty(optionsResult_)) {
				GUILayout.Label(optionsResult_, Theme.Small);
			}
			GUILayout.EndVertical();

			GUILayout.Space(10);
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
			GUILayout.Label("While driving", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("<b>TRC spec badge</b>: a small tag in a corner of the screen. Green when your car is TRC spec, " +
				"amber when the car you're driving hasn't been checked yet (press Check my tune in the garage), red when something isn't spec.", Theme.Body);
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
				"your car is checked. A short message pops up only if something isn't TRC spec.", Theme.Body);
			bool check = OnOff(autoCheck_);
			if (check != autoCheck_) {
				autoCheck_ = check;
				SavePref(AutoCheckPref, autoCheck_);
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Share my tune values</b>: when your tune isn't the official one, other players with TRC Tune Loader see only what you changed, not the values. " +
				"Turn this on to let them see your numbers too.", Theme.Body);
			int share = Choice(new[] { "Keep my values private", "Share my values" }, shareValues_ ? 1 : 0);
			if ((share == 1) != shareValues_) {
				shareValues_ = share == 1;
				SavePref(ShareValuesPref, shareValues_);
				sync_?.SendReportNow();
				TrcLog.Info(shareValues_ ? "Sharing tune values with other players." : "Tune values kept private.");
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Sector times</b>: your sectors stay on the timer during a run (purple: great pace, green: on pace, amber: below pace), with the result and in My times.", Theme.Body);
			int sectors = Choice(new[] { "Show sector times", "Hide sector times" }, showSectors_ ? 0 : 1);
			if ((sectors == 0) != showSectors_) {
				showSectors_ = sectors == 0;
				SavePref(SectorsPref, showSectors_);
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Best line</b>: during a time attack, where you drove on your best run (or best lap) in this car is drawn on the road: " +
				"red where you were slowest, through amber and green to blue where you were fastest, or in one colour.", Theme.Body);
			int lineMode = Choice(BestLineModes, bestLineMode_);
			if (lineMode != bestLineMode_) {
				bestLineMode_ = lineMode;
				SavePref(BestLinePref, bestLineMode_);
			}
			GUILayout.Space(8);
			GUILayout.Label("<b>Time attack timer</b>: drag it anywhere on the screen while this window is open during a run.", Theme.Body);
			GUILayout.BeginHorizontal();
			if (hudPosition_ != DefaultHudPosition && GUILayout.Button("Put the timer back at the top", Theme.Button)) {
				hudPosition_ = DefaultHudPosition;
				SavePref(HudPositionPref, hudPosition_);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();

			GUILayout.Space(10);
			GUILayout.Label("Backups", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("How many backups to keep for each car and setup. When a new backup is saved, the oldest ones over this number are deleted.", Theme.Body);
			int keepIndex = Array.IndexOf(BackupLimits, backupsKeep_);
			int pickedKeep = Choice(BackupLimits.Select(n => n == 0 ? "Keep all" : $"Last {n}").ToArray(), keepIndex);
			if (pickedKeep != keepIndex) {
				backupsKeep_ = BackupLimits[pickedKeep];
				SavePref(BackupsKeepPref, backupsKeep_);
				cleanupPending_ = null;
				backupOptionsResult_ = "";
			}
			if (backupsKeep_ > 0) {
				GUILayout.BeginHorizontal();
				if (cleanupPending_ == null) {
					if (GUILayout.Button("Clean up now", Theme.Button)) {
						FindOldBackups();
					}
				} else {
					if (GUILayout.Button($"Delete {cleanupPending_.Count} old backup(s)", Theme.ButtonPrimary)) {
						DeleteOldBackups();
					}
					if (GUILayout.Button("Cancel", Theme.Button)) {
						cleanupPending_ = null;
						backupOptionsResult_ = "Nothing was deleted.";
					}
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			if (!string.IsNullOrEmpty(backupOptionsResult_)) {
				GUILayout.Label(backupOptionsResult_, Theme.Small);
			}
			GUILayout.EndVertical();

			GUILayout.Space(10);
			GUILayout.Label("Tour", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("A quick walk through every part of this window.", Theme.Body);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Take the tour", Theme.Button)) {
				StartTour();
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();

			GUILayout.EndScrollView();
		}

		/// <summary>A row of chips, one picked. Returns the picked index.</summary>
		private static int Choice(string[] labels, int picked) {
			GUILayout.BeginHorizontal();
			for (int i = 0; i < labels.Length; i++) {
				if (GUILayout.Button(labels[i], i == picked ? Theme.ChipActive : Theme.Chip)) {
					picked = i;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			return picked;
		}

		private static bool OnOff(bool on) => Choice(new[] { "On", "Off" }, on ? 0 : 1) == 0;

		/// <summary>First click of Clean up now: finds what would be deleted, so the second click can confirm it.</summary>
		private void FindOldBackups() {
			try {
				cleanupPending_ = TuneBackups.OverLimit(BackupsFolder, backupsKeep_);
				if (cleanupPending_.Count == 0) {
					cleanupPending_ = null;
					backupOptionsResult_ = $"Nothing to clean up: no car has more than {backupsKeep_} backups.";
				} else {
					backupOptionsResult_ = $"{cleanupPending_.Count} backup(s) are older than the last {backupsKeep_} for their car. Deleting them can't be undone.";
				}
			} catch (Exception e) {
				cleanupPending_ = null;
				backupOptionsResult_ = $"Couldn't read the backups: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private void DeleteOldBackups() {
			int deleted = TuneBackups.Delete(cleanupPending_);
			backupOptionsResult_ = $"Deleted {deleted} old backup(s).";
			TrcLog.Info($"Backup clean-up: deleted {deleted} backup(s) over the last {backupsKeep_} per car.");
			cleanupPending_ = null;
			RefreshBackups();
		}

		// ---- First-run tour ----

		private TourStep[] TourSteps() => new[] {
			new TourStep {
				Title = "Welcome to TRC Tune Loader",
				Text = "This mod puts the official TRC tunes on your car and checks that it's TRC spec, so your runs count on the TRC leaderboards. " +
					"This short tour shows what each part of the window does. Use Next and Back, or Skip if you already know your way around.",
				Tab = -1,
			},
			new TourStep {
				Title = "TRC, the tunes and Project Dream",
				Text = "TRC (Touge Racing Community) was founded by WingsX for competitive grip racing, inspired by Japanese touge and Initial D. " +
					"WingsX spent over three years balancing the TRC Ultimate Grip Tunes, so races come down to driver skill, not setups. " +
					"Project Dream is a connected freeroam island of about 52 roads, built for TRC racing. The About tab tells the whole story.",
				Tab = AboutTab,
			},
			new TourStep {
				Title = "Opening this window",
				Text = $"Press <b>{shortcut_}</b> anytime in the game, or open the KSL menu and click TRCTuneLoader. " +
					"Press it again or click × to close the window. You can change the shortcut in the Options tab.",
				Tab = -1,
			},
			new TourStep {
				Title = "My car: the status bar",
				Text = "My car shows the car you're in and its setup. The coloured bar says whether it's TRC spec right now: " +
					"green is spec, amber means it hasn't been fully checked yet, and red means something isn't spec. " +
					"Under it you'll see your engine and drivetrain, and any Workshop body on the car.",
				Tab = 0,
			},
			new TourStep {
				Title = "Putting a TRC tune on",
				Text = "Pick a tune set (TRC Revival is the newest), then press Apply on the main garage screen. " +
					"When CarX's tuning screen is open, the button says Load into the dyno instead, and that works in online rooms too: " +
					"leave the tuning screen and choose Yes to save. Your own tune is always backed up first.",
				Tab = 0,
			},
			new TourStep {
				Title = "Checking your car",
				Text = "Check my tune lists anything that isn't TRC spec: your saved setup, the car you're driving, body kit stance, " +
					"Workshop bodies, engine swaps, and gearbox or drivetrain changes from mods like KINO. " +
					"The live check records what to compare with by itself, a few seconds after your car reaches the garage or the track. " +
					"Real numbers shows every setting the way CarX's tuning screen does. " +
					"While you drive, a small badge in a corner shows whether you're spec, and a message pops up if something isn't.",
				Tab = 0,
			},
			new TourStep {
				Title = "Your tune code",
				Text = "Your code (like TRC-95F4-06D9) is made from your car, its setup and every tune value. " +
					"It only matches the official code when your car really is spec, and it's what the TRC bot uses to verify leaderboard runs.",
				Tab = 0,
			},
			new TourStep {
				Title = "Tune sets",
				Text = "Every official set built into the mod: TRC Revival, V3.0, V2.6, V2.0 and V1.0. Click a car to see all its settings.",
				Tab = 1,
			},
			new TourStep {
				Title = "Players in the room",
				Text = "In an online room, every player's car gets checked: their setup, engine, stance and Workshop bodies. " +
					"Players who also run TRC Tune Loader send their own check and their mod list. Use those as hints, not proof. " +
					"Only problems shows just the players worth a closer look.",
				Tab = 2,
			},
			new TourStep {
				Title = "Mod check",
				Text = "Every mod KSL has loaded, whether KSL says they're verified, and a warning for tools that can edit the game, like Unity Explorer.",
				Tab = 3,
			},
			new TourStep {
				Title = "Backups",
				Text = "Whenever a TRC tune replaces your own tune, your tune is saved here first. Restore puts it back.",
				Tab = 4,
			},
			new TourStep {
				Title = "Time attack",
				Text = "Courses here lists the courses on the map you're driving: official TRC courses and your own. " +
					"Press Time attack, stop behind the start line, and a 3-2-1-GO countdown starts your run (or pick a flying start, and laps on a circuit). " +
					"Teleport to start, or Ctrl + R, puts you back on the line. Each course is split into sectors, and the timer shows how far ahead or behind your best you are. " +
					"Battle challenges another TRC Tune Loader player in the room to a side-by-side race or a cat and mouse, with a 3-2-1-GO on both screens at once. " +
					"My times keeps your best times for each car and tune set, and the Course builder lets you make your own practice courses.",
				Tab = CoursesTab,
			},
			new TourStep {
				Title = "Options",
				Text = "Change the shortcut that opens this window, the window size, the badge and track check, " +
					"and how many backups to keep, or take this tour again. " +
					"That's it: pick a set, press Apply, then Check my tune. See you on the touge!",
				Tab = OptionsTab,
			},
		};

		/// <summary>Called from Update until the tour has been seen: opens it once the player has been in the garage for a moment.</summary>
		private void StartTourWhenReady() {
			if (tourSeen_ || windowOpen_) {
				return;
			}

			RefreshCarIfDue();
			if (car_ == null || !car_.InGarage || car_.InDyno) {
				garageSince_ = -1f;
				return;
			}
			if (garageSince_ < 0f) {
				garageSince_ = Time.unscaledTime;
			}
			if (Time.unscaledTime - garageSince_ >= TourGarageDelaySeconds) {
				OpenWindow();
			}
		}

		private void StartTour() => GoToTourStep(0);

		private void GoToTourStep(int step) {
			TourStep[] steps = TourSteps();
			tourStep_ = Mathf.Clamp(step, 0, steps.Length - 1);
			int tab = steps[tourStep_].Tab;
			if (tab >= 0 && tab != tab_) {
				tab_ = tab;
				OnTabOpened(tab);
			}
		}

		/// <summary>Ends the tour (finished, skipped or the window closed) and remembers that it's been seen.</summary>
		private void EndTour() {
			tourStep_ = -1;
			if (tourSeen_) {
				return;
			}
			tourSeen_ = true;
			try {
				Kino.Prefs.Set(TourPref, TourVersion);
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't save that the tour was seen: {e.Message}");
			}
		}

		/// <summary>The tour's card above the tab. Returns whether the step shows a tab under it.</summary>
		private bool DrawTourCard() {
			TourStep[] steps = TourSteps();
			TourStep step = steps[Mathf.Clamp(tourStep_, 0, steps.Length - 1)];
			bool last = tourStep_ >= steps.Length - 1;

			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"Tour · {tourStep_ + 1} of {steps.Length}", Theme.Group);
			GUILayout.Label(step.Title, Theme.Heading);
			GUILayout.Label(step.Text, Theme.Body);
			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			if (!last && GUILayout.Button("Skip tour", Theme.Button)) {
				EndTour();
			}
			GUILayout.FlexibleSpace();
			if (tourStep_ > 0 && GUILayout.Button("Back", Theme.Button)) {
				GoToTourStep(tourStep_ - 1);
			}
			if (GUILayout.Button(last ? "Finish" : "Next", Theme.ButtonPrimary)) {
				if (last) {
					EndTour();
				} else {
					GoToTourStep(tourStep_ + 1);
				}
			}
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();

			return step.Tab >= 0;
		}
	}
}
