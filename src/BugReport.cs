using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// "Report a bug": one zip on the player's Desktop with what bskftw needs to find a bug: the mod's log and the
	/// one from the session before, the end of KSL's log, a summary (version, map, car, options, mods) and what the
	/// player wrote. If a zip can't be made, the same goes into one text file instead.
	/// </summary>
	public static class BugReport {
		/// <summary>Logs bigger than this keep only their end (the part with the bug, usually).</summary>
		public const int MaxLogBytes = 2 * 1024 * 1024;

		public sealed class Part {
			public string Name = "";
			public string Text = "";
		}

		/// <summary>A log file as a part (its last <see cref="MaxLogBytes"/>), or null when it isn't there.</summary>
		public static Part LogPart(string name, string path) {
			try {
				if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
					return null;
				}
				// Shared read: the mod and KSL still have their logs open.
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
					long skip = Math.Max(0, stream.Length - MaxLogBytes);
					stream.Seek(skip, SeekOrigin.Begin);
					using (var reader = new StreamReader(stream, Encoding.UTF8)) {
						string text = reader.ReadToEnd();
						return new Part { Name = name, Text = skip > 0 ? $"[only the last {MaxLogBytes / 1024 / 1024} MB of {path}]\n{text}" : text };
					}
				}
			} catch (Exception e) {
				return new Part { Name = name, Text = $"Couldn't read {path}: {e.Message}" };
			}
		}

		/// <summary>A file name for now, like TRC-bug-report-2026-10-26-1530.zip.</summary>
		public static string FileName(DateTime when, string extension) => $"TRC-bug-report-{when:yyyy-MM-dd-HHmm}.{extension}";

		/// <summary>
		/// Writes the report into <paramref name="folder"/> and returns its path: a zip, or a .txt when the zip
		/// failed. Throws only when neither could be written.
		/// </summary>
		public static string Write(string folder, DateTime when, IEnumerable<Part> parts) {
			List<Part> all = parts.Where(p => p != null).ToList();
			Directory.CreateDirectory(folder);
			string zip = Unique(Path.Combine(folder, FileName(when, "zip")));
			try {
				using (var file = new FileStream(zip, FileMode.CreateNew))
				using (var archive = new ZipArchive(file, ZipArchiveMode.Create)) {
					foreach (Part part in all) {
						ZipArchiveEntry entry = archive.CreateEntry(Safe(part.Name), CompressionLevel.Optimal);
						using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) {
							writer.Write(part.Text ?? "");
						}
					}
				}
				return zip;
			} catch (Exception e) {
				try {
					File.Delete(zip);
				} catch (Exception) {
					// Nothing to clean up.
				}
				string txt = Unique(Path.Combine(folder, FileName(when, "txt")));
				var text = new StringBuilder();
				text.Append($"(No zip: {e.Message})\n");
				foreach (Part part in all) {
					text.Append($"\n===== {part.Name} =====\n").Append(part.Text ?? "").Append('\n');
				}
				File.WriteAllText(txt, text.ToString(), new UTF8Encoding(false));
				return txt;
			}
		}

		/// <summary>Reads a report back (zip or text) as name → text, for the self-test.</summary>
		public static Dictionary<string, string> Read(string path) {
			var parts = new Dictionary<string, string>(StringComparer.Ordinal);
			if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
				using (var file = File.OpenRead(path))
				using (var archive = new ZipArchive(file, ZipArchiveMode.Read)) {
					foreach (ZipArchiveEntry entry in archive.Entries) {
						using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) {
							parts[entry.FullName] = reader.ReadToEnd();
						}
					}
				}
			} else {
				parts[Path.GetFileName(path)] = File.ReadAllText(path, Encoding.UTF8);
			}
			return parts;
		}

		private static string Safe(string name) {
			string clean = new string((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
			return clean.Length == 0 ? "part.txt" : clean;
		}

		/// <summary>"x.zip", or "x (2).zip" and so on when it's taken (two reports in the same minute).</summary>
		private static string Unique(string path) {
			if (!File.Exists(path)) {
				return path;
			}
			string folder = Path.GetDirectoryName(path) ?? "";
			string name = Path.GetFileNameWithoutExtension(path);
			string extension = Path.GetExtension(path);
			for (int i = 2; ; i++) {
				string next = Path.Combine(folder, $"{name} ({i}){extension}");
				if (!File.Exists(next)) {
					return next;
				}
			}
		}
	}
}
