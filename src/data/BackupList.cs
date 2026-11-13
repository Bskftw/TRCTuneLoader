// Your tune backups: listing them, keeping only as many as Options says, and putting one back on.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod : BaseMod {
		private List<TuneFile> backups_ = new List<TuneFile>();
		private string backupResult_ = "";
		private Vector2 backupScroll_;

		private string BackupsFolder => Path.Combine(modFolder_, BackupsFolderName);

		private void RefreshBackups() {
			try {
				RefreshCar();
				backups_ = TuneBackups.ForCar(BackupsFolder, car_);
			} catch (Exception e) {
				backups_ = new List<TuneFile>();
				backupResult_ = $"Couldn't read backups: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>With a backup limit set in Options, deletes this car and setup's backups over the limit (oldest first).</summary>
		private void PruneBackups() {
			if (backupsKeep_ <= 0) {
				return;
			}
			try {
				List<TuneFile> old = TuneBackups.OverLimit(BackupsFolder, backupsKeep_).Where(b => b.IsFor(car_.CarId, car_.SlotId)).ToList();
				int deleted = TuneBackups.Delete(old);
				if (deleted > 0) {
					TrcLog.Info($"Deleted {deleted} old backup(s) of {car_.Describe()} (keeping the last {backupsKeep_}).");
				}
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private void RestoreBackup(TuneFile backup) {
			try {
				if (!RefreshCar()) {
					backupResult_ = $"Couldn't restore: {carError_}";
					return;
				}

				bool intoDyno = car_.InDyno;
				backupResult_ = !ApplyWithBackup(backup, $"Your tune before restoring {backup.Name}", out string error, out string backupNote)
					? $"Couldn't restore: {error}"
					: intoDyno
						? $"Loaded {backup.Name} into CarX's tuning screen. Leave it and choose Yes to save. {backupNote}"
						: $"Restored {backup.Name}. {backupNote}";
				TrcLog.Info($"Restore {backup.Name} on {car_.Describe()}: {backupResult_}");
				RefreshBackups();
			} catch (Exception e) {
				backupResult_ = $"Couldn't restore: {e.Message}";
				TrcLog.Error(e);
			}
		}
	}
}
