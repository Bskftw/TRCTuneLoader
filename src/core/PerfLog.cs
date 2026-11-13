// A quiet performance log: one line a minute in the mod's log.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// A quiet performance log: one line a minute in TRCTuneLoader.log (so it's in bug reports too), to measure each
	/// optimization before and after (docs/archive/OPTIMIZATION_REPORT.md, item 0). It only watches: nothing it does changes what
	/// the mod does. The line has:
	/// - frames: the average frame time, the 1% low (the slowest 1% of frames, as fps) and the worst frame
	/// - the mod's own share: ms per frame in its Update and its OnGUI (all GUI passes of the frame together)
	/// - memory: garbage collections in the minute and the heap size (GC.GetTotalMemory, works in release builds)
	/// - packets sent and received per room channel
	/// - one-off jobs (the map scan, traffic loading, tune checks...): how many, their total and their slowest
	/// </summary>
	public static class PerfLog {
		private const float Every = 60f;

		private static readonly List<float> frames_ = new List<float>(20000);
		private static readonly Stopwatch update_ = new Stopwatch();
		private static readonly Stopwatch gui_ = new Stopwatch();
		private static readonly Dictionary<string, Counter> packets_ = new Dictionary<string, Counter>();
		private static readonly Dictionary<string, Job> jobs_ = new Dictionary<string, Job>();
		private static readonly object lock_ = new object();
		private static float started_ = -1f;
		private static int lastFrame_ = -1;
		private static int collections_;
		private static readonly StringBuilder line_ = new StringBuilder(512);

		private sealed class Counter {
			public int Sent, Received;
		}

		private sealed class Job {
			public int Count;
			public double TotalMs, WorstMs;
		}

		/// <summary>Called at the start of the mod's Update: counts the frame and writes the minute's line when due.</summary>
		public static void BeginUpdate() {
			int frame = Time.frameCount;
			if (frame != lastFrame_) {
				lastFrame_ = frame;
				if (frames_.Count < 60000) {
					frames_.Add(Time.unscaledDeltaTime);
				}
				float now = Time.unscaledTime;
				if (started_ < 0f) {
					Reset(now);
				} else if (now - started_ >= Every) {
					try {
						Write(now);
					} catch (Exception e) {
						TrcLog.Warning($"Performance log: {e.Message}");
					}
					Reset(now);
				}
			}
			update_.Start();
		}

		public static void EndUpdate() => update_.Stop();

		public static void BeginGui() => gui_.Start();

		public static void EndGui() => gui_.Stop();

		/// <summary>A packet sent or received on a room channel. Called from KSL's thread too, so it's locked.</summary>
		public static void Packet(string channel, bool sent) {
			lock (lock_) {
				if (!packets_.TryGetValue(channel, out Counter c)) {
					packets_[channel] = c = new Counter();
				}
				if (sent) {
					c.Sent++;
				} else {
					c.Received++;
				}
			}
		}

		/// <summary>Starts timing a one-off job; pass the result to <see cref="End"/>.</summary>
		public static long Begin() => Stopwatch.GetTimestamp();

		/// <summary>Ends a job started with <see cref="Begin"/>, under its name in the minute's line.</summary>
		public static void End(string job, long begin) =>
			Add(job, (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency);

		/// <summary>A job already timed elsewhere (ms), under its name in the minute's line.</summary>
		public static void Add(string job, double ms) {
			lock (lock_) {
				if (!jobs_.TryGetValue(job, out Job j)) {
					jobs_[job] = j = new Job();
				}
				j.Count++;
				j.TotalMs += ms;
				if (ms > j.WorstMs) {
					j.WorstMs = ms;
				}
			}
		}

		private static void Reset(float now) {
			started_ = now;
			frames_.Clear();
			update_.Reset();
			gui_.Reset();
			collections_ = GC.CollectionCount(0);
			lock (lock_) {
				foreach (Counter c in packets_.Values) {
					c.Sent = c.Received = 0;
				}
				jobs_.Clear();
			}
		}

		private static void Write(float now) {
			int count = frames_.Count;
			if (count < 10) {
				return;
			}
			double total = 0;
			for (int i = 0; i < count; i++) {
				total += frames_[i];
			}
			frames_.Sort();
			// The 1% low: the average of the slowest 1% of frames, shown as fps (the number that shows stutter).
			int slow = Math.Max(1, count / 100);
			double slowTotal = 0;
			for (int i = count - slow; i < count; i++) {
				slowTotal += frames_[i];
			}
			double averageMs = total * 1000.0 / count;
			double lowFps = slowTotal > 0 ? slow / slowTotal : 0;
			double worstMs = frames_[count - 1] * 1000.0;

			StringBuilder s = line_;
			s.Length = 0;
			s.Append("Performance (last ").Append((int)(now - started_)).Append(" s): ");
			s.Append(count).Append(" frames, average ").Append(averageMs.ToString("0.0")).Append(" ms (")
				.Append((1000.0 / Math.Max(0.01, averageMs)).ToString("0")).Append(" fps), 1% low ")
				.Append(lowFps.ToString("0")).Append(" fps, worst ").Append(worstMs.ToString("0")).Append(" ms. ");
			s.Append("Mod: Update ").Append((update_.Elapsed.TotalMilliseconds / count).ToString("0.00"))
				.Append(" ms/frame, GUI ").Append((gui_.Elapsed.TotalMilliseconds / count).ToString("0.00")).Append(" ms/frame. ");
			s.Append("Memory: ").Append(GC.CollectionCount(0) - collections_).Append(" GC, heap ")
				.Append((GC.GetTotalMemory(false) / (1024.0 * 1024.0)).ToString("0")).Append(" MB.");
			lock (lock_) {
				bool any = false;
				foreach (KeyValuePair<string, Counter> p in packets_) {
					if (p.Value.Sent == 0 && p.Value.Received == 0) {
						continue;
					}
					s.Append(any ? ", " : " Packets (sent/received): ");
					any = true;
					s.Append(p.Key).Append(' ').Append(p.Value.Sent).Append('/').Append(p.Value.Received);
				}
				if (any) {
					s.Append('.');
				}
				any = false;
				foreach (KeyValuePair<string, Job> j in jobs_) {
					s.Append(any ? ", " : " Jobs: ");
					any = true;
					s.Append(j.Key).Append(" x").Append(j.Value.Count).Append(' ')
						.Append(j.Value.TotalMs.ToString("0")).Append(" ms (worst ").Append(j.Value.WorstMs.ToString("0.0")).Append(')');
				}
				if (any) {
					s.Append('.');
				}
			}
			TrcLog.Info(s.ToString());
		}
	}
}
