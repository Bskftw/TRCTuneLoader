// The mod's log: writes every line to TRCTuneLoader.log and KSL's log.
using System;
using System.Collections.Generic;
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

		/// <summary>
		/// The file stays open (docs/archive/OPTIMIZATION_REPORT.md item 5: opening it for every line cost hundreds of opens at a map load, each
		/// one looked at by antivirus). Info lines go to disk at most half a second later (<see cref="FlushIfDue"/>, every frame);
		/// warnings and errors at once; everything before a bug report reads the file and when the mod closes. Shared, so the bug
		/// report and anyone else can read it while it's open.
		/// </summary>
		private static StreamWriter writer_;
		private static bool unwritten_;
		private static DateTime lastFlush_;
		private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(0.5);

		/// <summary>Starts a fresh log file in the mod's folder, keeping the last session's as the previous log.</summary>
		public static void Start(string folder, string header) {
			lock (lock_) {
				try {
					writer_?.Dispose();
				} catch (Exception) {
					// A fresh file follows.
				}
				writer_ = null;
				unwritten_ = false;
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

		private static readonly HashSet<string> once_ = new HashSet<string>();

		/// <summary>
		/// A warning said once a session per <paramref name="key"/>: for something that fails quietly and often (a game call
		/// every frame after a CarX update), so the log names it without filling up (IMPROVEMENT_PLAN.md C5).
		/// </summary>
		public static void Once(string key, string message) {
			lock (once_) {
				if (!once_.Add(key)) {
					return;
				}
			}
			Warning(message);
		}

		private static void Write(string level, object message) {
			lock (lock_) {
				if (path_ == null || fileFailed_) {
					return;
				}
				try {
					if (writer_ == null) {
						var stream = new FileStream(path_, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
						writer_ = new StreamWriter(stream, Utf8, 16 * 1024) { AutoFlush = false, NewLine = "\n" };
						lastFlush_ = DateTime.UtcNow;
					}
					writer_.Write('[');
					writer_.Write(DateTime.Now.ToString("HH:mm:ss"));
					writer_.Write("] ");
					writer_.Write(level);
					writer_.Write(": ");
					writer_.Write(message);
					writer_.Write('\n');
					unwritten_ = true;
					if (level != "Info") {
						FlushLocked();
					}
				} catch (Exception e) {
					Fail(e);
				}
			}
		}

		/// <summary>Called every frame: puts the waiting info lines on disk when half a second has passed.</summary>
		public static void FlushIfDue() {
			if (!unwritten_) {
				return;
			}
			lock (lock_) {
				if (unwritten_ && DateTime.UtcNow - lastFlush_ >= FlushEvery) {
					FlushLocked();
				}
			}
		}

		/// <summary>Everything written so far onto disk now (before something reads the file).</summary>
		public static void Flush() {
			lock (lock_) {
				FlushLocked();
			}
		}

		/// <summary>When the mod closes: the rest goes to disk and the file is closed.</summary>
		public static void Close() {
			lock (lock_) {
				FlushLocked();
				try {
					writer_?.Dispose();
				} catch (Exception) {
					// Closing anyway.
				}
				writer_ = null;
			}
		}

		private static void FlushLocked() {
			lastFlush_ = DateTime.UtcNow;
			if (writer_ == null || !unwritten_) {
				return;
			}
			try {
				writer_.Flush();
				unwritten_ = false;
			} catch (Exception e) {
				Fail(e);
			}
		}

		private static void Fail(Exception e) {
			// One failure (disk full, file locked) stops the file; KSL's log still gets everything.
			fileFailed_ = true;
			unwritten_ = false;
			try {
				writer_?.Dispose();
			} catch (Exception) {
				// Already broken.
			}
			writer_ = null;
			Kino.Log.Warning($"Stopped writing the TRC Tune Loader log file: {e.Message}");
		}

	}
}
