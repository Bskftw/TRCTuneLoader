// Options > Sharing and backups: sharing tune values, the Players page filter, and keeping or cleaning up backups.
using System;
using System.Collections.Generic;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private string backupOptionsResult_ = "";

		private void DrawOptionsSharing() {
			BeginSection("Sharing");
			SettingRow("Share my tune values", "Off: others with the mod see only what you changed from the official tune, not your numbers", () => {
				bool share = Switch(shareValues_);
				if (share != shareValues_) {
					shareValues_ = share;
					SavePref(ShareValuesPref, shareValues_);
					sync_?.SendReportNow();
					TrcLog.Info(shareValues_ ? "Sharing tune values with other players." : "Tune values kept private.");
				}
			}, first: true);
			EndSection();

			BeginSection("Backups", "When a new backup is saved, the oldest over this number are deleted, per car and setup.");
			int keepIndex = Array.IndexOf(BackupLimits, backupsKeep_);
			SettingRow("Keep", "", () => {
				int pickedKeep = Choice(BackupLimits.Select(n => n == 0 ? "Keep all" : $"Last {n}").ToArray(), keepIndex);
				if (pickedKeep != keepIndex) {
					backupsKeep_ = BackupLimits[pickedKeep];
					SavePref(BackupsKeepPref, backupsKeep_);
					cleanupPending_ = null;
					backupOptionsResult_ = "";
				}
			}, wide: true, first: true);
			if (backupsKeep_ > 0) {
				GUILayout.BeginHorizontal();
				if (cleanupPending_ == null) {
					if (NavButton("Clean up now", Theme.Button)) {
						FindOldBackups();
					}
				} else {
					if (NavButton($"Delete {cleanupPending_.Count} old backup(s)", Theme.ButtonPrimary)) {
						DeleteOldBackups();
					}
					if (NavButton("Cancel", Theme.ButtonQuiet)) {
						cleanupPending_ = null;
						backupOptionsResult_ = "Nothing was deleted.";
					}
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			if (!string.IsNullOrEmpty(backupOptionsResult_)) {
				GUILayout.Label(backupOptionsResult_, Theme.SectionNote);
			}
			EndSection();

			BeginSection(null);
			SettingRow("Tour", "A quick walk through every part of this window", () => {
				if (NavButton("Take the tour", Theme.Button)) {
					StartTour();
				}
			}, first: true);
			EndSection();
		}

		/// <summary>Old backups the Clean up now button found, waiting for the second click that deletes them.</summary>
		private List<TuneFile> cleanupPending_;

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
	}
}
