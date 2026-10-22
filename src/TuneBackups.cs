using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>Copies of the player's own tune, saved before a TRC tune replaces it.</summary>
	public static class TuneBackups {
		public static string Save(string folder, CarInfo car, IDictionary<string, float> values, string notes, ICarTuneAdapter adapter) {
			DateTime now = DateTime.Now;
			var draft = new TuneDraft {
				Name = $"Backup {now:yyyy-MM-dd HH:mm:ss}",
				Car = car.CarName,
				CarId = car.CarId,
				SlotId = car.SlotId,
				Slot = car.SlotName,
				Notes = notes,
				Values = new SortedDictionary<string, float>(values, StringComparer.Ordinal),
			};

			if (adapter.TryDescribe(car.SlotId, values, out var shown, out _)) {
				draft.Comments = shown;
			}

			string path = Path.Combine(folder, $"{SafeFileName(car.CarName)}_{car.SlotId}_{now:yyyyMMdd-HHmmss}{TuneFile.Extension}");
			draft.Save(path);
			return path;
		}

		/// <summary>Backups for this car and setup slot, newest first.</summary>
		public static List<TuneFile> ForCar(string folder, CarInfo car) {
			if (car == null || !Directory.Exists(folder)) {
				return new List<TuneFile>();
			}

			var backups = new List<TuneFile>();
			foreach (string path in Directory.GetFiles(folder, "*" + TuneFile.Extension).OrderByDescending(File.GetLastWriteTimeUtc)) {
				try {
					TuneFile backup = TuneFile.Load(path);
					if (backup.IsFor(car.CarId, car.SlotId)) {
						backups.Add(backup);
					}
				} catch (Exception) {
					// A damaged backup is simply left out of the list.
				}
			}
			return backups;
		}

		/// <summary>
		/// The backups beyond the newest <paramref name="keep"/> for each car and setup slot. Files that
		/// can't be read as backups are never included.
		/// </summary>
		public static List<TuneFile> OverLimit(string folder, int keep) {
			var over = new List<TuneFile>();
			if (keep <= 0 || !Directory.Exists(folder)) {
				return over;
			}

			var byCar = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (string path in Directory.GetFiles(folder, "*" + TuneFile.Extension).OrderByDescending(File.GetLastWriteTimeUtc)) {
				TuneFile backup;
				try {
					backup = TuneFile.Load(path);
				} catch (Exception) {
					continue;
				}
				string key = $"{backup.CarId}:{backup.SlotId}";
				byCar.TryGetValue(key, out int seen);
				byCar[key] = seen + 1;
				if (seen >= keep) {
					over.Add(backup);
				}
			}
			return over;
		}

		/// <summary>Deletes these backup files. Returns how many were deleted.</summary>
		public static int Delete(IEnumerable<TuneFile> backups) {
			int deleted = 0;
			foreach (TuneFile backup in backups ?? Enumerable.Empty<TuneFile>()) {
				try {
					if (!string.IsNullOrEmpty(backup.FilePath) && File.Exists(backup.FilePath)) {
						File.Delete(backup.FilePath);
						deleted++;
					}
				} catch (Exception) {
					// A file that can't be deleted (in use, read-only) is simply kept.
				}
			}
			return deleted;
		}

		/// <summary>True when the values match (after rounding) and there's nothing new to back up.</summary>
		public static bool SameValues(IDictionary<string, float> a, IDictionary<string, float> b) =>
			a.Count == b.Count &&
			a.All(pair => b.TryGetValue(pair.Key, out float other) &&
				TuneFingerprint.FormatValue(pair.Value) == TuneFingerprint.FormatValue(other));

		public static string SafeFileName(string name) {
			var safe = new StringBuilder();
			foreach (char c in name ?? "") {
				safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
			}
			return safe.Length == 0 ? "tune" : safe.ToString();
		}
	}
}
