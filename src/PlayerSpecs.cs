using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// A spec made by players (bskftw, Nov 3): like TRC's V1, V2, V3 and Revival sets, a named set of tunes, one per car
	/// and setup, for spec racing. Each is a folder in specs/: spec.txt (name, author, version, notes) and a .trctune
	/// per car. Shared as a zip of that folder; installed by putting the folder (or the zip) in specs/.
	/// </summary>
	public sealed class PlayerSpec {
		public const string InfoFile = "spec.txt";

		/// <summary>The folder's name: what time attack records and regulations call it by.</summary>
		public string Id = "";
		public string Name = "";
		public string Author = "";
		public string Version = "";
		public string Notes = "";
		public string Folder = "";
		public List<TuneFile> Tunes = new List<TuneFile>();
		public List<string> Problems = new List<string>();

		/// <summary>"Drift Kings v2", or the name alone without a version.</summary>
		public string Title => Version.Length > 0 ? $"{Name} {Version}" : Name;

		public TuneFile For(int carId, int slotId) => Tunes.FirstOrDefault(t => t.IsFor(carId, slotId));
	}

	public static class PlayerSpecs {
		public const string FolderName = "specs";

		/// <summary>Where installed zips go, and removed specs: never read as specs.</summary>
		public const string InstalledFolder = "installed";
		public const string RemovedFolder = "removed";

		/// <summary>Every spec in <paramref name="root"/>, by name. Zips dropped there are unpacked first (<see cref="InstallZips"/>).</summary>
		public static List<PlayerSpec> LoadAll(string root) {
			var specs = new List<PlayerSpec>();
			if (!Directory.Exists(root)) {
				return specs;
			}
			foreach (string folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
				string name = Path.GetFileName(folder);
				if (string.Equals(name, InstalledFolder, StringComparison.OrdinalIgnoreCase) || string.Equals(name, RemovedFolder, StringComparison.OrdinalIgnoreCase)) {
					continue;
				}
				specs.Add(Load(folder));
			}
			return specs.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		public static PlayerSpec Load(string folder) {
			var spec = new PlayerSpec { Folder = folder, Id = Path.GetFileName(folder) };
			string info = Path.Combine(folder, PlayerSpec.InfoFile);
			if (File.Exists(info)) {
				foreach (string raw in File.ReadAllLines(info)) {
					int hash = raw.IndexOf('#');
					string line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
					int eq = line.IndexOf('=');
					if (eq <= 0) {
						continue;
					}
					string value = line.Substring(eq + 1).Trim();
					switch (TuneRules.Key(line.Substring(0, eq))) {
						case "name": spec.Name = value; break;
						case "author": spec.Author = value; break;
						case "version": spec.Version = value; break;
						case "notes": spec.Notes = value; break;
					}
				}
			} else {
				spec.Problems.Add($"No {PlayerSpec.InfoFile}: named after its folder.");
			}
			if (spec.Name.Length == 0) {
				spec.Name = spec.Id;
			}
			foreach (string file in Directory.GetFiles(folder, "*" + TuneFile.Extension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
				try {
					TuneFile tune = TuneFile.Load(file);
					if (tune.Values.Count == 0 || tune.CarId.Length == 0) {
						spec.Problems.Add($"{Path.GetFileName(file)}: not a tune for a car.");
						continue;
					}
					if (spec.Tunes.Any(t => t.CarId == tune.CarId && t.SlotId == tune.SlotId)) {
						spec.Problems.Add($"{Path.GetFileName(file)}: a second tune for the same car and setup, left out.");
						continue;
					}
					spec.Tunes.Add(tune);
				} catch (Exception e) {
					spec.Problems.Add($"{Path.GetFileName(file)}: {e.Message}");
				}
			}
			return spec;
		}

		/// <summary>A new, empty spec. Returns its folder; the folder name is the name made safe (and numbered if taken).</summary>
		public static string Create(string root, string name, string author, string version, string notes) {
			string clean = Clean(name);
			if (clean.Length == 0) {
				throw new ArgumentException("A spec needs a name.");
			}
			string folder = Path.Combine(root, SafeName(clean));
			for (int n = 2; Directory.Exists(folder); n++) {
				folder = Path.Combine(root, $"{SafeName(clean)} {n}");
			}
			Directory.CreateDirectory(folder);
			WriteInfo(folder, clean, author, version, notes);
			return folder;
		}

		public static void WriteInfo(string folder, string name, string author, string version, string notes) {
			var text = new StringBuilder("# A TRC Tune Loader spec: a tune per car (the .trctune files next to this one).\n");
			text.Append("name    = ").Append(Clean(name)).Append('\n');
			text.Append("author  = ").Append(Clean(author)).Append('\n');
			text.Append("version = ").Append(Clean(version)).Append('\n');
			text.Append("notes   = ").Append(Clean(notes)).Append('\n');
			File.WriteAllText(Path.Combine(folder, PlayerSpec.InfoFile), text.ToString(), new UTF8Encoding(false));
		}

		/// <summary>Saves a car's tune into the spec, in place of the one it had for that car and setup. Returns the file.</summary>
		public static string SaveTune(PlayerSpec spec, TuneDraft draft) {
			foreach (TuneFile old in spec.Tunes.Where(t => t.IsFor(draft.CarId, draft.SlotId)).ToList()) {
				File.Delete(old.FilePath);
				spec.Tunes.Remove(old);
			}
			string path = Path.Combine(spec.Folder, $"{SafeName(draft.Car.Length > 0 ? draft.Car : "car " + draft.CarId)}_{draft.SlotId}{TuneFile.Extension}");
			draft.Save(path);
			spec.Tunes.Add(TuneFile.Load(path));
			return path;
		}

		public static void RemoveTune(PlayerSpec spec, TuneFile tune) {
			File.Delete(tune.FilePath);
			spec.Tunes.Remove(tune);
		}

		/// <summary>Zips the spec's folder (spec.txt and its tunes) to share. Returns the zip's path.</summary>
		public static string Export(PlayerSpec spec, string toFolder) {
			Directory.CreateDirectory(toFolder);
			string zip = Path.Combine(toFolder, $"{SafeName(spec.Title)} (TRC spec).zip");
			if (File.Exists(zip)) {
				File.Delete(zip);
			}
			using (var file = new FileStream(zip, FileMode.CreateNew))
			using (var archive = new ZipArchive(file, ZipArchiveMode.Create)) {
				foreach (string path in Directory.GetFiles(spec.Folder).Where(IsSpecFile)) {
					ZipArchiveEntry entry = archive.CreateEntry($"{spec.Id}/{Path.GetFileName(path)}", CompressionLevel.Optimal);
					using (Stream to = entry.Open())
					using (Stream from = File.OpenRead(path)) {
						from.CopyTo(to);
					}
				}
			}
			return zip;
		}

		/// <summary>
		/// Unpacks every .zip in <paramref name="root"/> into a spec folder (only spec.txt and .trctune files, never over
		/// a folder that's there), then moves the zip into root/installed. Returns what happened, one line each.
		/// </summary>
		public static List<string> InstallZips(string root) {
			var done = new List<string>();
			if (!Directory.Exists(root)) {
				return done;
			}
			foreach (string zip in Directory.GetFiles(root, "*.zip")) {
				try {
					string name = Path.GetFileNameWithoutExtension(zip).Replace("(TRC spec)", "").Trim();
					string folder = Path.Combine(root, SafeName(name));
					if (Directory.Exists(folder)) {
						done.Add($"{Path.GetFileName(zip)}: there's already a spec folder called {Path.GetFileName(folder)}. Delete or rename it first.");
						continue;
					}
					int files = 0;
					using (var stream = File.OpenRead(zip))
					using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) {
						foreach (ZipArchiveEntry entry in archive.Entries) {
							string file = Path.GetFileName(entry.FullName);
							if (!IsSpecFile(file) || entry.Length > 1_000_000) {
								continue;
							}
							Directory.CreateDirectory(folder);
							using (Stream from = entry.Open())
							using (Stream to = File.Create(Path.Combine(folder, file))) {
								from.CopyTo(to);
							}
							files++;
						}
					}
					string installed = Path.Combine(root, InstalledFolder);
					Directory.CreateDirectory(installed);
					string moved = Path.Combine(installed, Path.GetFileName(zip));
					if (File.Exists(moved)) {
						File.Delete(moved);
					}
					File.Move(zip, moved);
					done.Add(files > 0 ? $"Installed {name} ({files} file(s))." : $"{Path.GetFileName(zip)} had no spec in it.");
				} catch (Exception e) {
					done.Add($"{Path.GetFileName(zip)}: {e.Message}");
				}
			}
			return done;
		}

		private static bool IsSpecFile(string path) {
			string name = Path.GetFileName(path) ?? "";
			return string.Equals(name, PlayerSpec.InfoFile, StringComparison.OrdinalIgnoreCase) ||
				name.EndsWith(TuneFile.Extension, StringComparison.OrdinalIgnoreCase);
		}

		private static string Clean(string text) => (text ?? "").Replace("#", "").Replace("\r", " ").Replace("\n", " ").Trim();

		/// <summary>A folder or file name: no characters Windows refuses, and not empty.</summary>
		public static string SafeName(string text) {
			var invalid = Path.GetInvalidFileNameChars();
			string safe = new string((text ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
			return safe.Length == 0 ? "spec" : safe.Length > 60 ? safe.Substring(0, 60).Trim() : safe;
		}
	}
}
