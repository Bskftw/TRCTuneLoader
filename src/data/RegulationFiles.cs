// Loads and saves regulations in the Regulations folder (*.trcreg).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	public sealed partial class Regulation {
		public static List<Regulation> LoadAll(string folder) {
			var list = new List<Regulation>();
			if (!Directory.Exists(folder)) {
				return list;
			}
			foreach (string path in Directory.GetFiles(folder, "*" + Extension).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
				try {
					Regulation reg = Parse(File.ReadAllText(path, Encoding.UTF8));
					if (reg != null) {
						reg.FilePath = path;
						list.Add(reg);
					}
				} catch (Exception) {
					// A file that can't be read is left out.
				}
			}
			return list;
		}

		public void Save(string folder) {
			Directory.CreateDirectory(folder);
			if (FilePath.Length == 0 || !FilePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) {
				string baseName = new string((Name.Length > 0 ? Name : "regulation").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
				string path = Path.Combine(folder, baseName + Extension);
				for (int i = 2; File.Exists(path); i++) {
					path = Path.Combine(folder, $"{baseName} {i}{Extension}");
				}
				FilePath = path;
			}
			File.WriteAllText(FilePath, ToText(), new UTF8Encoding(false));
		}
	}
}
