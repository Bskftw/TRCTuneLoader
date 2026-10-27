using System;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>The mod's one data folder: moving old files in from next to a loose .ksm, and finding a second copy.</summary>
	internal static partial class SelfTest {
		private static void DataFolderTests() {
			string game = Path.Combine(Path.GetTempPath(), "trc-data-" + Guid.NewGuid().ToString("N"));
			try {
				string mods = Path.Combine(game, "kino", "mods");
				string folder = DataFolder.For(mods);
				Directory.CreateDirectory(mods);
				void Write(string path, string text) {
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					File.WriteAllText(path, text, new UTF8Encoding(false));
				}

				// An old loose install: the .ksm in kino/mods, its files next to it, plus another mod's "Courses" folder.
				Write(Path.Combine(mods, DataFolder.KsmName), "ksm");
				Write(Path.Combine(mods, "TimeAttack", "runs.csv"), "date,course\n2026-10-20,Akina\n");
				Write(Path.Combine(mods, "TimeAttack", "ratings.txt"), "old ratings");
				Write(Path.Combine(mods, "TimeAttack", "traces", "a.trace"), "1");
				Write(Path.Combine(mods, "Courses", "project-dream", "Mine.trccourse"), "course");
				Write(Path.Combine(mods, "Backups", "AE86_119.trctune"), "tune");
				Write(Path.Combine(mods, "TRCTuneLoader.log"), "old log");
				Write(Path.Combine(mods, "OtherMod", "Courses", "notes.txt"), "not ours");
				// The folder already has some files (from an install in the folder).
				Write(Path.Combine(folder, "TimeAttack", "runs.csv"), "date,course\n2026-10-25,Tsukuba\n");
				Write(Path.Combine(folder, "TimeAttack", "ratings.txt"), "new ratings");

				var done = DataFolder.MoveIn(mods, folder);
				Expect(done.Count == 4, $"the old TimeAttack, Courses, Backups and log are moved in ({done.Count} moves)");
				string runs = File.ReadAllText(Path.Combine(folder, "TimeAttack", "runs.csv"));
				Expect(runs == "date,course\n2026-10-25,Tsukuba\n2026-10-20,Akina\n", "both sets of runs are kept, with one header");
				Expect(File.ReadAllText(Path.Combine(folder, "TimeAttack", "ratings.txt")) == "new ratings" &&
					File.ReadAllText(Path.Combine(folder, "TimeAttack", "ratings" + DataFolder.MovedSuffix + ".txt")) == "old ratings",
					"a file that's there already is kept, and the old one comes in beside it");
				Expect(File.Exists(Path.Combine(folder, "TimeAttack", "traces", "a.trace")) && File.Exists(Path.Combine(folder, "Courses", "project-dream", "Mine.trccourse")) &&
					File.Exists(Path.Combine(folder, "Backups", "AE86_119.trctune")) && File.Exists(Path.Combine(folder, "TRCTuneLoader.log")), "sub-folders and files come along");
				Expect(!Directory.Exists(Path.Combine(mods, "TimeAttack")) && !Directory.Exists(Path.Combine(mods, "Courses")) && !Directory.Exists(Path.Combine(mods, "Backups")),
					"the old folders are gone from kino/mods");
				Expect(File.Exists(Path.Combine(mods, "OtherMod", "Courses", "notes.txt")), "another mod's files stay where they are");
				Expect(DataFolder.MoveIn(mods, folder).Count == 0 && DataFolder.MoveIn(folder, folder).Count == 0, "nothing to do the next time, or when the .ksm is in the folder");

				Write(Path.Combine(mods, "Courses", "readme.txt"), "another mod's");
				Expect(DataFolder.MoveIn(mods, folder).Count == 0 && File.Exists(Path.Combine(mods, "Courses", "readme.txt")), "a Courses folder without courses in it isn't touched");

				// Copies: running the loose one, an update left one in the folder and one a folder deeper.
				string loose = Path.Combine(mods, DataFolder.KsmName);
				Write(Path.Combine(folder, DataFolder.KsmName), "ksm");
				Write(Path.Combine(folder, DataFolder.Name, DataFolder.KsmName), "ksm");
				var others = DataFolder.OtherCopies(loose, mods, game);
				Expect(others.Count == 2 && !others.Any(o => DataFolder.SamePath(o, loose)), $"the other copies are found, not the one running ({others.Count})");
				File.Delete(Path.Combine(folder, DataFolder.KsmName));
				File.Delete(Path.Combine(folder, DataFolder.Name, DataFolder.KsmName));
				Expect(DataFolder.OtherCopies(loose, mods, game).Count == 0, "one copy, no warning");
			} finally {
				try {
					Directory.Delete(game, true);
				} catch (Exception) {
					// Temp files.
				}
			}
		}
	}
}
