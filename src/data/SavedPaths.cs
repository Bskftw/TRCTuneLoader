// The map's course paths remembered between sessions (Courses/<map>/paths.cache), so a big map isn't read again each load.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Each map course's path as the scan worked it out, with the problems it found, under a fingerprint of everything
	/// that went into it (the path meshes' exact data, the start and finish lines, the mod build: see
	/// <c>CourseScanner.PathKey</c>). A path whose fingerprint matches is used as it is; anything changed in the map
	/// gives a new fingerprint, so that path is read again (IMPROVEMENT_PLAN.md P1: reading Project Dream's paths took
	/// 6.7 s of a 7.6 s freeze on every load). The points are kept exactly, so a course's code is the same either way.
	/// </summary>
	public sealed class SavedPaths {
		public const string FileName = "paths.cache";

		private const string Magic = "TRCPATHS";
		private const int FileVersion = 1;

		// Limits on what's read back: far more than any map needs, so a damaged file can't use up memory.
		private const int MostPaths = 2000;
		private const int MostPoints = 1000000;
		private const int MostProblems = 50;

		public sealed class Entry {
			public List<Vector3> Path = new List<Vector3>();
			public List<string> Problems = new List<string>();
		}

		/// <summary>The mod version, part of every fingerprint (with the build's ID): a new install reads every path again once.</summary>
		public string ModVersion { get; }

		public SavedPaths(string modVersion) {
			ModVersion = modVersion ?? "";
		}

		private readonly Dictionary<string, Entry> entries_ = new Dictionary<string, Entry>(StringComparer.Ordinal);
		private readonly HashSet<string> used_ = new HashSet<string>(StringComparer.Ordinal);
		private bool changed_;

		/// <summary>How many paths came from here, and how many were worked out fresh, since this was loaded.</summary>
		public int Hits { get; private set; }
		public int Misses { get; private set; }

		/// <summary>The saved path for this fingerprint (a copy), marking it as still used.</summary>
		public bool TryGet(string key, out Entry entry) {
			entry = null;
			if (!entries_.TryGetValue(key, out Entry saved)) {
				Misses++;
				return false;
			}
			used_.Add(key);
			Hits++;
			entry = new Entry { Path = new List<Vector3>(saved.Path), Problems = new List<string>(saved.Problems) };
			return true;
		}

		/// <summary>A path just worked out, to keep for next time.</summary>
		public void Put(string key, List<Vector3> path, List<string> problems) {
			entries_[key] = new Entry { Path = new List<Vector3>(path), Problems = new List<string>(problems) };
			used_.Add(key);
			changed_ = true;
		}

		/// <summary>The file's paths, or none if it's missing or can't be read (<paramref name="error"/> then says why).</summary>
		public static SavedPaths Load(string file, string modVersion, out string error) {
			error = "";
			var saved = new SavedPaths(modVersion);
			if (!File.Exists(file)) {
				return saved;
			}
			try {
				using (var stream = new GZipStream(File.OpenRead(file), CompressionMode.Decompress))
				using (var r = new BinaryReader(stream, Encoding.UTF8)) {
					if (r.ReadString() != Magic || r.ReadInt32() != FileVersion) {
						return saved; // an older or other file: everything is read fresh and saved again
					}
					int count = Limit(r.ReadInt32(), MostPaths);
					for (int i = 0; i < count; i++) {
						string key = r.ReadString();
						var entry = new Entry();
						int points = Limit(r.ReadInt32(), MostPoints);
						for (int p = 0; p < points; p++) {
							entry.Path.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
						}
						int problems = Limit(r.ReadInt32(), MostProblems);
						for (int p = 0; p < problems; p++) {
							entry.Problems.Add(r.ReadString());
						}
						saved.entries_[key] = entry;
					}
				}
			} catch (Exception e) {
				error = e.Message;
				return new SavedPaths(modVersion);
			}
			return saved;
		}

		/// <summary>
		/// Writes the paths used since loading (a course that's gone from the map drops out). Only when something
		/// changed: a load where every path came from the file writes nothing.
		/// </summary>
		public bool Save(string file, out string error) {
			error = "";
			if (!changed_ && used_.Count == entries_.Count) {
				return false;
			}
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(file));
				string temp = file + ".new";
				using (var stream = new GZipStream(File.Create(temp), CompressionMode.Compress))
				using (var w = new BinaryWriter(stream, Encoding.UTF8)) {
					w.Write(Magic);
					w.Write(FileVersion);
					w.Write(used_.Count);
					foreach (string key in used_) {
						Entry entry = entries_[key];
						w.Write(key);
						w.Write(entry.Path.Count);
						foreach (Vector3 point in entry.Path) {
							w.Write(point.x);
							w.Write(point.y);
							w.Write(point.z);
						}
						w.Write(entry.Problems.Count);
						foreach (string problem in entry.Problems) {
							w.Write(problem);
						}
					}
				}
				if (File.Exists(file)) {
					File.Delete(file);
				}
				File.Move(temp, file);
				changed_ = false;
				return true;
			} catch (Exception e) {
				error = e.Message;
				return false;
			}
		}

		private static int Limit(int value, int most) {
			if (value < 0 || value > most) {
				throw new InvalidDataException($"a count of {value} (at most {most})");
			}
			return value;
		}
	}
}
