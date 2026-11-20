// The quick menu while driving: a strip of actions opened with a key or one wheel/controller button, picked without letting go of the wheel.
using System;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The quick menu (docs/archive/IMPROVEMENT_PLAN.md U3, F8): restart the run, start the track, traffic on or off, hide the HUD, mark a clip, without the
	/// window or the cursor. With one button: the first press opens it, each press after moves to the next action, and the
	/// action runs by itself once you stop pressing (<see cref="QuickPickWait"/>). It never takes the pad from the car. The
	/// keyboard works the same way with the menu key; Esc closes it. No number or arrow keys: CarX uses 1, 5 and 6, and keyboard drivers steer with the arrows.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string QuickKeyPref = "quick_menu_key";
		private Shortcut quickShortcut_ = Shortcut.QuickDefault;
		private bool capturingQuick_;
		private bool quickDown_;

		private bool quickOpen_;

		/// <summary>The action picked (-1: none yet), and when the menu was last pressed (unscaled time).</summary>
		private int quickPick_ = -1;
		private float quickPressedAt_;

		/// <summary>After the last press, the picked action runs this long later (s); with nothing picked the menu closes after <see cref="QuickIdleClose"/>.</summary>
		private const float QuickPickWait = 1.5f, QuickIdleClose = 4f;

		private const int QuickRestart = 0, QuickStart = 1, QuickTraffic = 2, QuickHide = 3, QuickClip = 4, QuickClose = 5, QuickCount = 6;

		private string QuickName(int item) {
			switch (item) {
				case QuickRestart: return "Restart run";
				case QuickStart: return "Start track";
				case QuickTraffic: return TrafficRunning ? "Traffic off" : "Traffic on";
				case QuickHide: return hudHidden_ ? "Show HUD" : "Hide HUD";
				case QuickClip: return "Mark clip";
				default: return "Close";
			}
		}

		/// <summary>The quick menu button or key: open it, or move to the next action.</summary>
		private void QuickPress() {
			if (!quickOpen_) {
				quickOpen_ = true;
				quickPick_ = -1;
			} else {
				quickPick_ = (quickPick_ + 1) % QuickCount;
			}
			quickPressedAt_ = Time.unscaledTime;
		}

		/// <summary>Every frame: the picked action runs once the presses stop; an untouched menu closes.</summary>
		private void UpdateQuickMenu() {
			if (!quickOpen_) {
				return;
			}
			float idle = Time.unscaledTime - quickPressedAt_;
			if (quickPick_ >= 0 && idle >= QuickPickWait) {
				RunQuick(quickPick_);
			} else if (quickPick_ < 0 && idle >= QuickIdleClose) {
				quickOpen_ = false;
			}
		}

		private void RunQuick(int item) {
			quickOpen_ = false;
			quickPick_ = -1;
			TrcLog.Info($"Quick menu: {QuickName(item)}.");
			switch (item) {
				case QuickRestart:
					if (run_ != null && !InBattle) {
						RestartFromKey();
					} else {
						ShowNotice("No time attack running to restart.", 2f);
					}
					break;
				case QuickStart:
					if (InBattle || InRace) {
						ShowNotice("Not during a battle or race.", 2f);
					} else {
						StartFromKey();
					}
					break;
				case QuickTraffic:
					QuickTrafficSwitch();
					break;
				case QuickHide:
					SetHudHidden(!hudHidden_);
					if (!hudHidden_) {
						ShowNotice("<b>HUD on</b>", 1.5f);
					}
					break;
				case QuickClip:
					MarkClip();
					break;
			}
		}

		/// <summary>A line in clips.csv: when, where, the run's time and how fast, to find the moment later in a recording.</summary>
		private void MarkClip() {
			string course = run_?.Course.Name ?? (BattleOnTrack ? battle_.Course.Name : "");
			string runTime = run_ != null && run_.State == TimeAttackRun.Phase.Running ? TimeAttackRun.FormatTime(run_.Elapsed) : "";
			float kmh = hasPose_ ? Mathf.Abs(pose_.SpeedKmh) : 0f;
			try {
				ClipMarks.Append(modFolder_, DateTime.Now, map_?.Name ?? "", course, runTime, car_?.Describe() ?? "", kmh);
				TrcLog.Info($"Clip marked: {map_?.Name ?? "no map"}{(course.Length > 0 ? $", {course}" : "")}, {kmh:0} km/h.");
				ShowNotice("<b>Clip marked</b>", 1.5f);
			} catch (Exception e) {
				ShowNotice($"Couldn't mark the clip: {e.Message}", 3f);
			}
		}

		/// <summary>From HandleShortcutEvent: the quick menu's key, picking it, and its keys while it's open. True when the event was the menu's.</summary>
		private bool HandleQuickKeys(Event e) {
			if (e.type == EventType.KeyUp && e.keyCode == quickShortcut_.Key) {
				quickDown_ = false;
				return false;
			}
			if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) {
				return false;
			}
			if (capturingQuick_) {
				if (e.keyCode == KeyCode.Escape) {
					capturingQuick_ = false;
					optionsResult_ = "Quick menu key not changed.";
				} else {
					Shortcut picked = Shortcut.FromEvent(e);
					if (picked == null) {
						return false;
					}
					SetQuickShortcut(picked);
					quickDown_ = true;
				}
				e.Use();
				return true;
			}
			if (TypingInGame()) {
				return false;
			}
			if (quickShortcut_.Matches(e)) {
				if (!quickDown_) {
					quickDown_ = true;
					QuickPress();
				}
				e.Use();
				return true;
			}
			if (!quickOpen_) {
				return false;
			}
			if (e.keyCode != KeyCode.Escape) {
				return false;
			}
			quickOpen_ = false;
			e.Use();
			return true;
		}

		private void SetQuickShortcut(Shortcut shortcut) {
			capturingQuick_ = false;
			if (shortcut.SameAs(shortcut_) || shortcut.SameAs(restartShortcut_) || shortcut.SameAs(startShortcut_)) {
				optionsResult_ = $"{shortcut} is already used: pick another key for the quick menu.";
				return;
			}
			quickShortcut_ = shortcut;
			SavePref(QuickKeyPref, shortcut.Save());
			optionsResult_ = $"Quick menu key set to {shortcut}.";
			TrcLog.Info($"Quick menu key set to {shortcut}.");
		}

		/// <summary>Options > Keys and buttons: the quick menu's key.</summary>
		private void DrawQuickKeyOption() {
			KeyRow("Quick menu", "While driving: restart, start track, traffic, hide the HUD, mark a clip. Press again to move along", quickShortcut_.ToString(), capturingQuick_,
				() => { capturingQuick_ = true; capturingShortcut_ = false; capturingRestart_ = false; capturingStart_ = false; optionsResult_ = ""; },
				() => { capturingQuick_ = false; optionsResult_ = "Quick menu key not changed."; },
				!quickShortcut_.SameAs(Shortcut.QuickDefault), () => SetQuickShortcut(Shortcut.QuickDefault));
		}

		/// <summary>The strip near the bottom of the screen: each action, the picked one lit, and a bar for when it runs.</summary>
		private void DrawQuickMenu(float screenWidth, float screenHeight) {
			if (!quickOpen_ || Event.current.type != EventType.Repaint) {
				return;
			}
			const float itemHeight = 34f, gap = 6f;
			// Narrower items when the scales make the screen small, so the strip always fits.
			float itemWidth = Mathf.Min(120f, (screenWidth - 24f - (QuickCount - 1) * gap) / QuickCount);
			float width = QuickCount * itemWidth + (QuickCount - 1) * gap + 24f;
			var panel = new Rect((screenWidth - width) / 2f, screenHeight * 0.72f, width, itemHeight + 52f);
			GUI.Box(panel, GUIContent.none, Theme.HudPanel);
			GUI.Label(new Rect(panel.x, panel.y + 6f, panel.width, 18f),
				quickPick_ < 0 ? "<b>Quick menu</b> · press again to pick" : "<b>Quick menu</b> · stop pressing to do it, Esc closes", Theme.HudSmall);
			for (int i = 0; i < QuickCount; i++) {
				var item = new Rect(panel.x + 12f + i * (itemWidth + gap), panel.y + 28f, itemWidth, itemHeight);
				GUI.Label(item, QuickName(i), i == quickPick_ ? Theme.ChipActive : Theme.Chip);
				if (i == quickPick_ && quickPressedAt_ <= Time.unscaledTime) {
					float done = Mathf.Clamp01((Time.unscaledTime - quickPressedAt_) / QuickPickWait);
					GUI.DrawTexture(new Rect(item.x + 6f, item.yMax + 4f, (item.width - 12f) * done, 3f), Theme.AccentLine);
				}
			}
		}
	}
}
