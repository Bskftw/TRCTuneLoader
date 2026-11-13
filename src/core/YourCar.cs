// Your car: which car you're in, its car names, and putting an official tune on it (with a backup first).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod : BaseMod {
		private void ApplyOfficial(TuneFile tune) {
			lastDiffs_ = new List<string>();
			try {
				if (!RefreshCar()) {
					lastResult_ = $"Couldn't apply: {carError_}";
					return;
				}

				bool intoDyno = car_.InDyno;
				if (!ApplyWithBackup(tune, $"Your tune before applying {tune.DisplayName}", out string error, out string backupNote)) {
					lastResult_ = $"Couldn't {(intoDyno ? "load" : "apply")}: {error}";
					return;
				}

				if (intoDyno) {
					lastResult_ = $"Loaded {tune.DisplayName} into CarX's tuning screen. To keep it, leave the tuning screen and choose Yes when CarX asks to save. {backupNote}";
					TrcLog.Info($"Loaded {tune.DisplayName} ({tune.Fingerprint}) into the dyno for {car_.Describe()}. {backupNote}");
					return;
				}

				TrcLog.Info($"Applied {tune.DisplayName} ({tune.Fingerprint}) to {car_.Describe()}. {backupNote}");
				CheckOwnCarFromMenu();
				lastResult_ = $"Applied {tune.DisplayName}. {backupNote} Check: {lastResult_}";
			} catch (Exception e) {
				lastResult_ = $"Couldn't apply: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>
		/// Backs up the player's saved tune (unless it's already official or already backed up), then applies
		/// it on the main garage screen, or loads it into CarX's tuning screen when that's open (also in rooms).
		/// </summary>
		private bool ApplyWithBackup(TuneFile tune, string backupNote, out string error, out string backupResult) {
			backupResult = "";
			bool intoDyno = car_.InDyno;
			error = "";
			if (!intoDyno && !adapter_.CanApply(car_, out error)) {
				return false;
			}
			if (!tune.IsFor(car_.CarId, car_.SlotId)) {
				error = $"This tune is for car {tune.CarId}, setup {tune.SlotId}. You're in {car_.Describe()}, car {car_.CarId}, setup {car_.SlotId}.";
				return false;
			}

			if (!adapter_.TryReadCurrentTune(car_, out var current, out error)) {
				error = $"couldn't back up your current tune, so nothing was changed ({error})";
				return false;
			}

			TuneFile latest = TuneBackups.ForCar(BackupsFolder, car_).FirstOrDefault();
			TuneFile alreadyOfficial = MatchingOfficial(current);

			if (alreadyOfficial != null) {
				backupResult = $"Your car had the official {alreadyOfficial.Name} tune, so no backup was needed.";
			} else if (latest != null && TuneBackups.SameValues(latest.Values, current)) {
				backupResult = "Your previous tune was already in Backups.";
			} else {
				string path = TuneBackups.Save(BackupsFolder, car_, current, backupNote, adapter_);
				backupResult = "Your previous tune is in Backups.";
				TrcLog.Info($"Backed up {car_.Describe()} to {path}");
				PruneBackups();
			}

			return intoDyno ? adapter_.TryLoadIntoDyno(car_, tune, out error) : adapter_.TryApplyTune(car_, tune, out error);
		}

		/// <summary>The official tune (any set) these values match on the player's car, or null.</summary>
		private TuneFile MatchingOfficial(IDictionary<string, float> values) {
			if (!adapter_.TryGetDefaults(car_.SlotId, out var defaults, out _)) {
				return null;
			}
			return official_.ForCar(car_.CarId, car_.SlotId).FirstOrDefault(t =>
				TuneCheck.Compare(car_.CarIdText, car_.SlotIdText, t.Fingerprint, t.Values, values, defaults).Matches);
		}

		private void NameCars() {
			try {
				official_.NameCars(adapter_);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private void RefreshCarIfDue() {
			if (Time.unscaledTime >= nextCarRefresh_) {
				RefreshCar();
			}
		}

		private bool RefreshCar() {
			nextCarRefresh_ = Time.unscaledTime + CarRefreshSeconds;
			try {
				if (adapter_.TryGetCurrentCar(out var car, out string error)) {
					car_ = car;
					carError_ = "";
					return true;
				}
				car_ = null;
				carError_ = error;
			} catch (Exception e) {
				car_ = null;
				carError_ = $"CarX connection problem: {e.Message}";
			}
			return false;
		}
	}
}
