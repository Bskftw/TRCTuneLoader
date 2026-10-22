using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>
	/// Prints "file name, tab, tune code" for every .trctune and .knd file given (files or folders),
	/// using the mod's own code. A .knd file that isn't a KINO tune prints "SKIP".
	/// tools/check_codes.py compares the output with tools/tune_code.py.
	///
	/// "--selftest" runs a few checks of TuneCheck instead.
	/// </summary>
	internal static class Program {
		private static int Main(string[] args) {
			if (args.Length == 1 && args[0] == "--selftest") {
				return SelfTest.Run();
			}

			var files = new List<string>();
			foreach (string arg in args) {
				if (Directory.Exists(arg)) {
					files.AddRange(Directory.GetFiles(arg, "*", SearchOption.AllDirectories)
						.Where(f => f.EndsWith(TuneFile.Extension, StringComparison.OrdinalIgnoreCase) ||
									f.EndsWith(KndFile.Extension, StringComparison.OrdinalIgnoreCase)));
				} else {
					files.Add(arg);
				}
			}

			foreach (string path in files.OrderBy(p => p, StringComparer.Ordinal)) {
				Console.WriteLine($"{Path.GetFileName(path)}\t{CodeOf(path)}");
			}

			return 0;
		}

		private static string CodeOf(string path) {
			if (!path.EndsWith(KndFile.Extension, StringComparison.OrdinalIgnoreCase)) {
				return TuneFile.Load(path).Fingerprint;
			}

			try {
				return KndFile.Load(path).Fingerprint;
			} catch (InvalidDataException) {
				return "SKIP";
			}
		}
	}
}
