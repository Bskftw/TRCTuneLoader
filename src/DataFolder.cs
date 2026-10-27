using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// Where the mod keeps its files: always kino/mods/TRCTuneLoader, wherever KSL put the .ksm itself (KSL's mod
	/// list can put it loose in kino/mods). Before 0.14.2 the files went next to the .ksm, so a loose .ksm filled
	/// kino/mods with TimeAttack, Courses and Backups folders: <see cref="MoveIn"/> brings those into the folder,
	/// only when they're clearly this mod's. <see cref="OtherCopies"/> finds a second TRCTuneLoader.ksm.
	/// </summary>
	public static class DataFolder {
		public const string Name = "TRCTuneLoader";
		public const string KsmName = "TRCTuneLoader.ksm";

		/// <summary>Appended to a file's name when the folder already has one by that name.</summary>
		public const string MovedSuffix = " (moved)";

		private static readonly string[] OurLogs = { "TRCTuneLoader.log", "TRCTuneLoader.previous.log" };

		/// <summary>The mod's folder: kino/mods/TRCTuneLoader.</summary>
		public static string For(string modsFolder) => Path.Combine(modsFolder, Name);

		/// <summary>Whether a folder next to the old .ksm holds this mod's files (and not another mod's with the same name).</summary>
		public static bool IsOurs(string name, string path) {
			if (!Directory.Exists(path)) {
				return false;
			}
			IEnumerable<string> files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f).ToLowerInvariant());
			switch (name) {
				case "TimeAttack":
					return files.Any(f => f == "runs.csv" || f == "battles.csv" || f == "races.csv" || f == "ratings.txt" || f == "speedtraps.csv" ||
						f == "events.jsonl" || f.EndsWith(".trace", StringComparison.Ordinal) || f.EndsWith(".line", StringComparison.Ordinal));
				case "Courses":
					return files.Any(f => f.EndsWith(".trccourse", StringComparison.Ordinal) || f.EndsWith(".trctraps", StringComparison.Ordinal));
				case "Backups":
					return files.Any(f => f.EndsWith(".trctune", StringComparison.Ordinal));
				case "bug-reports":
					return files.Any(f => f.StartsWith("trc-bug-report-", StringComparison.Ordinal));
				default:
					return false;
			}
		}

		/// <summary>
		/// Moves this mod's files from <paramref name="oldFolder"/> (where the .ksm is) into <paramref name="folder"/>.
		/// A file that's there already is kept: run, battle and race logs (.csv) get the old lines added to them, anything
		/// else comes in with <see cref="MovedSuffix"/> on its name. Returns what was done, one line each.
		/// </summary>
		public static List<string> MoveIn(string oldFolder, string folder) {
			var done = new List<string>();
			if (string.IsNullOrEmpty(oldFolder) || SamePath(oldFolder, folder) || !Directory.Exists(oldFolder)) {
				return done;
			}
			Directory.CreateDirectory(folder);
			foreach (string name in new[] { "TimeAttack", "Courses", "Backups", "bug-reports" }) {
				string from = Path.Combine(oldFolder, name);
				if (!IsOurs(name, from)) {
					continue;
				}
				int moved = 0;
				foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) {
					string relative = file.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					MoveFile(file, Path.Combine(folder, name, relative));
					moved++;
				}
				RemoveEmpty(from);
				done.Add($"moved {moved} file(s) from {from} into {Path.Combine(folder, name)}");
			}
			foreach (string log in OurLogs) {
				string from = Path.Combine(oldFolder, log);
				if (File.Exists(from)) {
					MoveFile(from, Path.Combine(folder, log));
					done.Add($"moved {from} into {folder}");
				}
			}
			return done;
		}

		private static void MoveFile(string from, string to) {
			Directory.CreateDirectory(Path.GetDirectoryName(to) ?? ".");
			if (!File.Exists(to)) {
				File.Move(from, to);
				return;
			}
			if (to.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) {
				// Logs of runs, battles and races: both sets of lines count. The old file's header is left out.
				List<string> lines = File.ReadAllLines(from, Encoding.UTF8).Skip(1).Where(l => l.Trim().Length > 0).ToList();
				if (lines.Count > 0) {
					string existing = File.ReadAllText(to, Encoding.UTF8);
					string glue = existing.Length > 0 && !existing.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "";
					File.AppendAllText(to, glue + string.Join("\n", lines) + "\n", new UTF8Encoding(false));
				}
				File.Delete(from);
				return;
			}
			string name = Path.GetFileNameWithoutExtension(to) + MovedSuffix;
			string target = Path.Combine(Path.GetDirectoryName(to) ?? ".", name + Path.GetExtension(to));
			for (int i = 2; File.Exists(target); i++) {
				target = Path.Combine(Path.GetDirectoryName(to) ?? ".", $"{name} {i}{Path.GetExtension(to)}");
			}
			File.Move(from, target);
		}

		private static void RemoveEmpty(string folder) {
			foreach (string sub in Directory.GetDirectories(folder)) {
				RemoveEmpty(sub);
			}
			if (!Directory.EnumerateFileSystemEntries(folder).Any()) {
				Directory.Delete(folder);
			}
		}

		/// <summary>
		/// Other copies of TRCTuneLoader.ksm than the one running: loose in kino/mods, in kino/mods/TRCTuneLoader,
		/// one folder deeper (where an old update could unzip it), or in the game folder.
		/// </summary>
		public static List<string> OtherCopies(string running, string modsFolder, string gameFolder) {
			var places = new List<string> {
				Path.Combine(modsFolder, KsmName),
				Path.Combine(modsFolder, Name, KsmName),
				Path.Combine(modsFolder, Name, Name, KsmName),
			};
			if (!string.IsNullOrEmpty(gameFolder)) {
				places.Add(Path.Combine(gameFolder, Name, KsmName));
				places.Add(Path.Combine(gameFolder, KsmName));
			}
			return places.Where(p => File.Exists(p) && !SamePath(p, running)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		public static bool SamePath(string a, string b) {
			if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) {
				return false;
			}
			try {
				return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
			} catch (Exception) {
				return false;
			}
		}
	}
}
