// The TRC Tunes page: your car, its TRC tune sets and checks, and its backups beside them.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private bool showNumbers_;
		private List<string> carNumbers_ = new List<string>();
		private Vector2 carScroll_;

		/// <summary>My car: the car, its TRC tune sets and checks on the left, its backups in a column beside them (since 0.15.0; Backups was its own tab).</summary>
		private void DrawMyCarTab() {
			RefreshCarIfDue();
			if (car_ == null) {
				GUILayout.Label(carError_, Theme.PillNeutral);
				return;
			}
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			DrawMyCarMain();
			GUILayout.EndVertical();
			GUILayout.Space(10);
			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(245), GUILayout.ExpandHeight(true));
			DrawBackupsColumn();
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		private void DrawMyCarMain() {
			GUILayout.Label(car_.CarName, Theme.Heading);
			string where = car_.InDyno ? "in CarX's tuning screen" : car_.InGarage ? "in the garage" : "on track";
			GUILayout.Label($"{car_.SlotName} setup · {where}", Theme.Small);
			GUILayout.Space(6);

			RefreshStatusIfDue();
			DrawStatusPill(carStatus_);
			if (carStatus_ != null) {
				if (carStatus_.Drivetrain != null) {
					GUILayout.Label(carStatus_.Drivetrain.Summary(), Theme.Small);
				}
				foreach (WorkshopItem item in carStatus_.Workshop) {
					GUILayout.Label(item.Describe(), Theme.Small);
				}
			}

			List<TuneFile> tunes = official_.ForCar(car_.CarId, car_.SlotId);
			if (tunes.Count == 0) {
				return;
			}

			TuneFile tune = tunes.Find(t => t.SetId == chosenSet_) ?? tunes[0];
			GUILayout.Label("Tune set", Theme.SectionLabel);
			GUILayout.BeginHorizontal();
			foreach (TuneFile option in tunes) {
				if (NavButton(TuneSets.NameOf(option.SetId), option == tune ? Theme.ChipActive : Theme.Chip)) {
					chosenSet_ = option.SetId;
					showNumbers_ = false;
					lastResult_ = "";
					lastDiffs_ = new List<string>();
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(6);

			GUILayout.BeginHorizontal();
			if (NavButton(car_.InDyno ? $"Load {tune.Name} into the dyno" : $"Apply {tune.Name}", Theme.ButtonPrimary)) {
				showNumbers_ = false;
				ApplyOfficial(tune);
				nextStatusRefresh_ = 0;
			}
			if (NavButton("Check my tune", Theme.Button)) {
				showNumbers_ = false;
				CheckOwnCarFromMenu();
				nextStatusRefresh_ = 0;
			}
			if (NavButton(showNumbers_ ? "Hide numbers" : "Real numbers", Theme.Button)) {
				showNumbers_ = !showNumbers_;
				if (showNumbers_) {
					carNumbers_ = ValueLines(tune);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Label(car_.InDyno
				? $"Code {tune.Fingerprint} · loads onto CarX's tuning screen; leave it and choose Yes to save (in a room, CarX shares it with everyone) · your own tune is backed up first"
				: $"Code {tune.Fingerprint} · your own tune is backed up first · Apply works on the main garage screen; in a room, open CarX's tuning screen and load it there", Theme.Small);

			if (showNumbers_) {
				DrawLines(ref carScroll_, carNumbers_);
				return;
			}

			if (!string.IsNullOrEmpty(lastResult_)) {
				GUILayout.Space(4);
				GUILayout.Label(lastResult_, Theme.Body);
			}
			if (lastDiffs_.Count > 0) {
				DrawLines(ref carScroll_, lastDiffs_);
			}
		}

		private void RefreshStatusIfDue() {
			if (Time.unscaledTime < nextStatusRefresh_) {
				return;
			}

			nextStatusRefresh_ = Time.unscaledTime + StatusRefreshSeconds;
			try {
				carStatus_ = car_ != null ? CheckOwnCar(recordInGarage: false) : null;
			} catch (Exception e) {
				carStatus_ = null;
				TrcLog.Error(e);
			}
		}

		private void DrawStatusPill(OwnCheck status) {
			if (status == null) {
				return;
			}

			if (status.Saved == null) {
				GUILayout.Label(status.Error, Theme.PillNeutral);
			} else if (!status.Saved.Matches) {
				GUILayout.Label($"Not TRC spec: {status.Saved.Differences.Count} difference(s) from {status.Official.Name}", Theme.PillBad);
			} else if (status.StanceDifferences.Count > 0) {
				GUILayout.Label($"Not TRC spec: {SetLabel(status.Official)} saved, but the car's stance (ride height, track width or axles) isn't stock. Press Check my tune for details.", Theme.PillBad);
			} else if (status.DrivetrainChanged) {
				GUILayout.Label($"Not TRC spec: {SetLabel(status.Official)} saved, but {DrivetrainProblem(status.Drivetrain)}. Press Check my tune for details.", Theme.PillBad);
			} else if (!status.LiveChecked) {
				GUILayout.Label($"{SetLabel(status.Official)} spec saved. The live car is checked a few seconds after it reaches the garage or the track.", Theme.PillWarn);
			} else if (status.LiveDifferences.Count > 0) {
				GUILayout.Label($"{SetLabel(status.Official)} saved, but the live car differs in {status.LiveDifferences.Count} place(s)", Theme.PillBad);
			} else {
				GUILayout.Label($"{SetLabel(status.Official)} spec: saved setup and live car", Theme.PillGood);
			}
		}

		private void DrawBackupsColumn() {
			GUILayout.Label($"Backups ({backups_.Count})", Theme.Group);
			GUILayout.Label("Your own tunes for this car and setup, kept whenever a TRC tune replaced one. Restoring backs up your current tune first.", Theme.Small);
			if (!string.IsNullOrEmpty(backupResult_)) {
				GUILayout.Label(backupResult_, Theme.Body);
			}
			backupScroll_ = GUILayout.BeginScrollView(backupScroll_);
			if (backups_.Count == 0) {
				GUILayout.Label("No backups yet.", Theme.Small);
			}
			foreach (TuneFile backup in backups_) {
				GUILayout.Label($"<b>{backup.Name}</b>\n<color={Theme.MutedHex}>{backup.Notes}</color>", Theme.Small);
				GUILayout.BeginHorizontal();
				if (NavButton("Restore", Theme.Button)) {
					RestoreBackup(backup);
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				DividerLine();
			}
			GUILayout.EndScrollView();
		}
	}
}
