// The mod's settings: their saved names (Kino.Prefs keys), their values, loading them all at start, and saving one.
using System;
using System.Collections.Generic;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Settings are saved with KSL's prefs (Kino.Prefs), which keeps them per mod between game sessions. The pref
	/// names never change, or players lose their settings. Some parts keep their own settings beside them (the
	/// look in LookSettings.cs, the box positions in ScreenPositions.cs); LoadOptions reads them all at start.
	/// KSL has no way for a mod to change its own keybind, so the shortcut is matched in OnGUI (OpenShortcut.cs).
	/// </summary>
	public partial class TRCTuneLoaderMod {
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

		/// <summary>Size of the window, badge and pop-up, on top of KSL's own UI scale.</summary>
		private float windowScale_ = 1f;
		private bool showBadge_;
		private int badgeCorner_ = 1;
		private bool autoCheck_;
		private int backupsKeep_;
		private bool problemsOnly_;

		private void LoadOptions() {
			try {
				shortcut_ = Shortcut.Parse(Kino.Prefs.Get(ShortcutPref, Shortcut.Default.Save()));
				restartShortcut_ = Shortcut.Parse(Kino.Prefs.Get(RestartPref, Shortcut.RestartDefault.Save()), Shortcut.RestartDefault);
				LoadPadOptions();
				LoadPins();
				startShortcut_ = Shortcut.Parse(Kino.Prefs.Get(StartKeyPref, Shortcut.StartDefault.Save()), Shortcut.StartDefault);
				tourSeen_ = Kino.Prefs.Get(TourPref, 0) >= 1;
				float scale = Kino.Prefs.Get(WindowScalePref, 1f);
				windowScale_ = WindowScales.OrderBy(s => Math.Abs(s - scale)).First();
				showBadge_ = Kino.Prefs.Get(BadgePref, false);
				badgeCorner_ = Mathf.Clamp(Kino.Prefs.Get(BadgeCornerPref, 1), 0, Corners.Length - 1);
				autoCheck_ = Kino.Prefs.Get(AutoCheckPref, false);
				int keep = Kino.Prefs.Get(BackupsKeepPref, 0);
				backupsKeep_ = BackupLimits.Contains(keep) ? keep : 0;
				problemsOnly_ = Kino.Prefs.Get(ProblemsOnlyPref, false);
				showSectors_ = Kino.Prefs.Get(SectorsPref, true);
				timingDetailed_ = Kino.Prefs.Get(TimingDetailPref, false);
				bestLineMode_ = Mathf.Clamp(Kino.Prefs.Get(BestLinePref, 0), 0, 2);
				ghostMode_ = Mathf.Clamp(Kino.Prefs.Get(GhostsPref, KeepBestGhosts), 0, 2);
				LoadSuspensionOptions();
				LoadTelemetryOptions();
				ghostOpacity_ = Mathf.Clamp(Kino.Prefs.Get(GhostOpacityPref, 1), 0, GhostOpacities.Length - 1);
				CourseMarkers.SetGhostOpacity(GhostOpacityValues[ghostOpacity_]);
				LoadLayout();
				trapSignMode_ = Mathf.Clamp(Kino.Prefs.Get(TrapSignPref, 0), 0, 2);
				CourseMarkers.UseGantries = Kino.Prefs.Get(GantriesPref, true);
				mapIconsOn_ = Kino.Prefs.Get(MapIconsPref, true);
				LoadFrog();
				shareValues_ = Kino.Prefs.Get(ShareValuesPref, false);
				LoadLook();
				Vector2 timer = Kino.Prefs.Get(HudPositionPref, DefaultHudPosition);
				hudPosition_ = new Vector2(Mathf.Clamp01(timer.x), Mathf.Clamp01(timer.y));
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't read the TRC Tune Loader options: {e.Message}");
			}
		}

		/// <summary>What's new and the wheel buttons' prefs, read when Home starts (after <see cref="LoadOptions"/>: what's
		/// new needs <c>tourSeen_</c>). Their own try, so a problem here can't stop the other options loading.</summary>
		private void LoadHomeOptions() {
			try {
				whatsNewPending_ = tourSeen_ && Kino.Prefs.Get(WhatsNewPref, "") != WhatsNewVersion;
				wheelAction_ = WheelButtons.Binding.Parse(Kino.Prefs.Get(WheelActionPref, ""));
				wheelWindow_ = WheelButtons.Binding.Parse(Kino.Prefs.Get(WheelWindowPref, ""));
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't read the Home options: {e.Message}");
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
	}
}
