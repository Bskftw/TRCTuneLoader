using System;
using System.IO;
using System.Text;
using KSL.API;

namespace TRC.TuneLoader {
	/// <summary>
	/// The mod's log. Every line goes to the mod's own file, kino/mods/TRCTuneLoader/TRCTuneLoader.log
	/// (the last session's file is kept as TRCTuneLoader.previous.log), and also to KSL's shared log,
	/// kino/output.log, where it sits next to what the game and the other mods did at the same moment.
	/// </summary>
	public static class TrcLog {
		public const string FileName = "TRCTuneLoader.log";
		public const string PreviousFileName = "TRCTuneLoader.previous.log";

		private static readonly object lock_ = new object();
		private static readonly Encoding Utf8 = new UTF8Encoding(false);
		private static string path_;
		private static bool fileFailed_;

		/// <summary>Starts a fresh log file in the mod's folder, keeping the last session's as the previous log.</summary>
		public static void Start(string folder, string header) {
			lock (lock_) {
				try {
					Directory.CreateDirectory(folder);
					string path = Path.Combine(folder, FileName);
					string previous = Path.Combine(folder, PreviousFileName);
					if (File.Exists(path)) {
						if (File.Exists(previous)) {
							File.Delete(previous);
						}
						File.Move(path, previous);
					}
					File.WriteAllText(path, header + "\n", Utf8);
					path_ = path;
				} catch (Exception e) {
					path_ = null;
					Kino.Log.Warning($"Couldn't start the TRC Tune Loader log file: {e.Message}");
				}
			}
		}

		public static void Info(object message) {
			Kino.Log.Info(message);
			Write("Info", message);
		}

		public static void Warning(object message) {
			Kino.Log.Warning(message);
			Write("Warning", message);
		}

		public static void Error(object message) {
			Kino.Log.Error(message);
			Write("Error", message);
		}

		private static void Write(string level, object message) {
			lock (lock_) {
				if (path_ == null || fileFailed_) {
					return;
				}
				try {
					File.AppendAllText(path_, $"[{DateTime.Now:HH:mm:ss}] {level}: {message}\n", Utf8);
				} catch (Exception e) {
					// One failure (disk full, file locked) stops the file; KSL's log still gets everything.
					fileFailed_ = true;
					Kino.Log.Warning($"Stopped writing the TRC Tune Loader log file: {e.Message}");
				}
			}
		}
	}
}
