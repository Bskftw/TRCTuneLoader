// Saves ghosts and loads them back, one folder per course (Ghosts/<course code>/).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class Ghost {
		public static string FolderFor(string ghostsFolder, string courseCode) =>
			Path.Combine(ghostsFolder, new string((courseCode ?? "").Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray()));

		public void Save(string ghostsFolder) {
			string folder = FolderFor(ghostsFolder, CourseCode);
			Directory.CreateDirectory(folder);
			if (FilePath.Length == 0) {
				string name = new string($"{When.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)} {Driver} {TimeAttackRun.FormatTime(Time).Replace(':', '.')}"
					.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
				FilePath = Path.Combine(folder, name + Extension);
			}
			File.WriteAllBytes(FilePath, Encode());
		}

		/// <summary>Every ghost saved for a course, fastest first.</summary>
		public static List<Ghost> LoadFor(string ghostsFolder, string courseCode) {
			var list = new List<Ghost>();
			string folder = FolderFor(ghostsFolder, courseCode);
			if (!Directory.Exists(folder)) {
				return list;
			}
			foreach (string path in Directory.GetFiles(folder, "*" + Extension)) {
				try {
					Ghost g = Decode(File.ReadAllBytes(path));
					if (g != null && g.CourseCode == courseCode) {
						g.FilePath = path;
						list.Add(g);
					}
				} catch (Exception) {
					// A file that can't be read is left out.
				}
			}
			return list.OrderBy(g => g.Time).ToList();
		}
	}
}
