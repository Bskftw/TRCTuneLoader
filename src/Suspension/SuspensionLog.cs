using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>One wheel at one moment: how far the suspension is compressed, the tyre's load and whether it touches the road.</summary>
	public struct WheelFrame {
		/// <summary>0 fully extended (drooped), 1 fully compressed.</summary>
		public float Compression;

		/// <summary>Vertical load on the tyre (N).</summary>
		public float Load;
		public bool Contact;
		public float SlipAngle;
		public float SlipRatio;
	}

	/// <summary>All four wheels at one moment (front left, front right, rear left, rear right, CarX's order).</summary>
	public sealed class SuspensionFrame {
		public double Time;
		public Vector3 Position;
		public float Kmh;
		public WheelFrame[] Wheels = new WheelFrame[4];
	}

	/// <summary>One corner's suspension from the tune, as CarX built it.</summary>
	public sealed class CornerSetup {
		/// <summary>Full spring travel (m).</summary>
		public float Travel;

		/// <summary>Spring rate (N/m) and the four damper rates (N per m/s, if CarX's units are real ones).</summary>
		public float Spring;
		public float SlowBump;
		public float FastBump;
		public float SlowRebound;
		public float FastRebound;

		/// <summary>Damper speeds (m/s) where slow damping ends and fast damping starts, for bump and rebound.</summary>
		public float BumpSlowLimit = 0.1f;
		public float BumpFastLimit = 1f;
		public float ReboundSlowLimit = 0.1f;
		public float ReboundFastLimit = 1f;

		/// <summary>The car's weight on this corner (kg), from the weight and its balance.</summary>
		public float CornerMass;

		/// <summary>The spring's natural frequency (Hz): how fast the corner bounces on its spring.</summary>
		public float Frequency => Spring > 0 && CornerMass > 0 ? Mathf.Sqrt(Spring / CornerMass) / (2f * Mathf.PI) : 0f;

		/// <summary>A damper rate as a share of critical damping (0.7 settles fast without bouncing; 1 never overshoots).</summary>
		public float Ratio(float damping) => Spring > 0 && CornerMass > 0 ? damping / (2f * Mathf.Sqrt(Spring * CornerMass)) : 0f;
	}

	/// <summary>Something that happened to a wheel, where it happened.</summary>
	public sealed class SuspensionEvent {
		public enum Kind { ContactLost, Packing, Bottomed, LoadSpike }

		public Kind What;
		public int Wheel;
		public double Time;
		public Vector3 Position;
		public float Kmh;

		/// <summary>Contact lost: right after a hard compression. Packing: compression from and to.</summary>
		public bool AfterCompression;
		public float From;
		public float To;

		/// <summary>Contact lost: how long the tyre was off the road (s).</summary>
		public double Seconds;

		/// <summary>Where on the course (m along it), when a course was being driven; -1 otherwise.</summary>
		public float Along = -1f;
	}

	/// <summary>One wheel's numbers over a session or a run.</summary>
	public sealed class WheelReport {
		/// <summary>Seconds at each damper speed: bins of <see cref="SuspensionLog.BinMmS"/> mm/s from -<see cref="SuspensionLog.MaxMmS"/> (rebound) to +MaxMmS (bump); the ends hold everything faster.</summary>
		public double[] Histogram = new double[SuspensionLog.Bins];

		/// <summary>Seconds at each 5% of travel (0 extended to 100 compressed).</summary>
		public double[] TravelTime = new double[20];

		public float MinCompression = 1f;
		public float MaxCompression;
		public double Seconds;
		public double BumpSeconds;
		public double ReboundSeconds;
		public double FastBumpSeconds;
		public double FastReboundSeconds;
		public float FastestBumpMmS;
		public float FastestReboundMmS;
		public int ContactLost;
		public int ContactLostAfterCompression;
		public int Bottomed;
		public int Packing;
		public int LoadSpikes;
		public float MeanLoad;
		public float PeakLoad;

		/// <summary>How much of the time the damper moved in bump rather than rebound (beyond the slow range), 0 to 1.</summary>
		public float BumpShare => BumpSeconds + ReboundSeconds > 0 ? (float)(BumpSeconds / (BumpSeconds + ReboundSeconds)) : 0.5f;
	}

	/// <summary>
	/// Suspension data logging (bskftw, Nov 1, for a player who wanted to see "compression stacking", road contact on
	/// rebound and a too-stiff bump "without relying on the lack of feeling in a game"). The mod feeds a frame of all
	/// four wheels every game frame while it's on; this keeps the last few seconds for the overlay and works out what
	/// real data engineers look at:
	///
	///   damper speed histogram: time at each speed in bump and rebound, with CarX's slow/fast limits
	///   packing down: a run of bumps where the wheel doesn't extend back in between and ends near the bump stop
	///   contact: the tyre leaving the road (or almost unloading) while the others stay down, and whether it came
	///     right after a hard compression (thrown off: too much fast bump, or rebound too slow to follow)
	///   bottoming: reaching full compression
	///   load spikes: the tyre's load jumping to several times its share of the car's weight on a bump
	///   travel: how much of it the run used
	///
	/// Nothing here knows about CarX, so the self-test drives it with made-up wheels.
	/// </summary>
	public sealed class SuspensionLog {
		public const int BinMmS = 25;
		public const int MaxMmS = 500;
		public const int Bins = 2 * MaxMmS / BinMmS + 2;

		/// <summary>The overlay's trace: this many seconds.</summary>
		public const double TraceSeconds = 4;

		/// <summary>A damper moving faster than this (m/s) into bump is a hit, for packing and "after a compression".</summary>
		public const float HitMs = 0.15f;

		/// <summary>Contact lost this soon after a hit counts as thrown off by it.</summary>
		public const double AfterHitSeconds = 0.35;

		/// <summary>Packing: hits closer together than this are one run of bumps.</summary>
		public const double PackingGapSeconds = 0.6;

		public const float BottomedAt = 0.97f;
		public const float NearlyUnloaded = 0.05f;
		public const float SpikeTimes = 2.5f;

		/// <summary>The most raw frames kept for the CSV (about 10 minutes at 100 frames a second).</summary>
		public const int MaxFrames = 60000;

		public static readonly string[] WheelNames = { "Front left", "Front right", "Rear left", "Rear right" };
		public static readonly string[] WheelShort = { "FL", "FR", "RL", "RR" };

		public CornerSetup[] Setup = new CornerSetup[4];
		public WheelReport[] Wheels = { new WheelReport(), new WheelReport(), new WheelReport(), new WheelReport() };
		public List<SuspensionEvent> Events { get; } = new List<SuspensionEvent>();
		public List<SuspensionFrame> Frames { get; } = new List<SuspensionFrame>();

		/// <summary>The last few seconds, for the overlay: compression of each wheel, and damper speed (m/s, + bump).</summary>
		public List<(double Time, float[] Compression)> Trace { get; } = new List<(double, float[])>();
		public float[] LastSpeedMs { get; } = new float[4];

		/// <summary>What it's about ("Free roam", "Akina Downhill") and when it started.</summary>
		public string Label = "";
		public DateTime Started = DateTime.Now;

		/// <summary>Where the car is along the course being driven (m), or null.</summary>
		public Func<Vector3, float?> AlongCourse;

		private SuspensionFrame last_;
		private readonly double[] lastHit_ = { double.MinValue, double.MinValue, double.MinValue, double.MinValue };
		private readonly bool[] off_ = new bool[4];
		private readonly double[] offSince_ = new double[4];
		private readonly bool[] bottomed_ = new bool[4];
		private readonly bool[] spiking_ = new bool[4];
		private readonly bool[] inHit_ = new bool[4];
		private readonly double[] loadSum_ = new double[4];
		private readonly double[] loadTime_ = new double[4];

		// Packing: each wheel's run of hits, the peak of each and the lowest it got back to in between.
		private readonly List<float>[] hitPeaks_ = { new List<float>(), new List<float>(), new List<float>(), new List<float>() };
		private readonly List<float>[] recoveries_ = { new List<float>(), new List<float>(), new List<float>(), new List<float>() };
		private readonly float[] lowSinceHit_ = { 1f, 1f, 1f, 1f };
		private readonly float[] peakThisHit_ = new float[4];

		public double Seconds => Wheels[0].Seconds;

		/// <summary>A wheel's share of the car's weight (N), from the tune, or the average load it carried.</summary>
		private float StaticLoad(int w) {
			float mass = Setup[w]?.CornerMass ?? 0f;
			if (mass > 0) {
				return mass * 9.81f;
			}
			return loadTime_[w] > 0 ? (float)(loadSum_[w] / loadTime_[w]) : 0f;
		}

		/// <summary>Takes the wheels as they are now. A frame where nothing moved (the game shows the same physics step twice) is skipped.</summary>
		public void Add(SuspensionFrame frame) {
			if (last_ != null) {
				double dt = frame.Time - last_.Time;
				if (dt <= 0 || (dt < 0.2 && SameAs(frame, last_))) {
					return;
				}
				if (dt > 0.5) {
					// A pause (menu, loading, teleport): start again from here without a jump in speed.
					last_ = frame;
					return;
				}
				int contact = frame.Wheels.Count(w => w.Contact);
				for (int w = 0; w < 4; w++) {
					AddWheel(w, frame, dt, contact);
				}
			}
			last_ = frame;
			if (Frames.Count < MaxFrames) {
				Frames.Add(frame);
			}
			Trace.Add((frame.Time, frame.Wheels.Select(x => x.Compression).ToArray()));
			while (Trace.Count > 0 && frame.Time - Trace[0].Time > TraceSeconds) {
				Trace.RemoveAt(0);
			}
		}

		private static bool SameAs(SuspensionFrame a, SuspensionFrame b) {
			for (int w = 0; w < 4; w++) {
				if (a.Wheels[w].Compression != b.Wheels[w].Compression || a.Wheels[w].Load != b.Wheels[w].Load) {
					return false;
				}
			}
			return true;
		}

		private void AddWheel(int w, SuspensionFrame frame, double dt, int wheelsDown) {
			WheelFrame now = frame.Wheels[w];
			WheelFrame before = last_.Wheels[w];
			WheelReport r = Wheels[w];
			CornerSetup setup = Setup[w];
			float travel = setup != null && setup.Travel > 0 ? setup.Travel : 0.15f;
			float speed = (float)((now.Compression - before.Compression) * travel / dt);
			LastSpeedMs[w] = speed;
			float mmS = speed * 1000f;

			r.Seconds += dt;
			int bin = mmS >= MaxMmS ? Bins - 1 : mmS < -MaxMmS ? 0 : 1 + (int)Math.Floor((mmS + MaxMmS) / BinMmS);
			r.Histogram[Mathf.Clamp(bin, 0, Bins - 1)] += dt;
			r.TravelTime[Mathf.Clamp((int)(now.Compression * 20f), 0, 19)] += dt;
			r.MinCompression = Mathf.Min(r.MinCompression, now.Compression);
			r.MaxCompression = Mathf.Max(r.MaxCompression, now.Compression);
			float slowBump = setup?.BumpSlowLimit ?? 0.1f;
			float slowRebound = setup?.ReboundSlowLimit ?? 0.1f;
			if (speed > slowBump) {
				r.BumpSeconds += dt;
			} else if (speed < -slowRebound) {
				r.ReboundSeconds += dt;
			}
			if (speed > (setup?.BumpFastLimit ?? 1f)) {
				r.FastBumpSeconds += dt;
			} else if (speed < -(setup?.ReboundFastLimit ?? 1f)) {
				r.FastReboundSeconds += dt;
			}
			r.FastestBumpMmS = Mathf.Max(r.FastestBumpMmS, mmS);
			r.FastestReboundMmS = Mathf.Max(r.FastestReboundMmS, -mmS);
			if (now.Contact) {
				loadSum_[w] += now.Load * dt;
				loadTime_[w] += dt;
				r.MeanLoad = (float)(loadSum_[w] / loadTime_[w]);
			}
			r.PeakLoad = Mathf.Max(r.PeakLoad, now.Load);

			// A hit: the damper moving fast into bump.
			bool hit = speed > HitMs;
			if (hit && !inHit_[w]) {
				lastHit_[w] = frame.Time;
				StartHit(w, frame);
			}
			inHit_[w] = hit;
			if (hit) {
				peakThisHit_[w] = Mathf.Max(peakThisHit_[w], now.Compression);
			} else {
				lowSinceHit_[w] = Mathf.Min(lowSinceHit_[w], now.Compression);
			}
			if (hitPeaks_[w].Count > 0 && frame.Time - lastHit_[w] > PackingGapSeconds) {
				EndHits(w);
			}

			// Off the road (or nearly unloaded) while at least two others are down: not a jump.
			float share = StaticLoad(w);
			bool unloaded = !now.Contact || (share > 0 && now.Load < share * NearlyUnloaded);
			if (unloaded && !off_[w] && wheelsDown - (now.Contact ? 1 : 0) >= 2) {
				off_[w] = true;
				offSince_[w] = frame.Time;
				bool afterHit = frame.Time - lastHit_[w] <= AfterHitSeconds;
				r.ContactLost++;
				if (afterHit) {
					r.ContactLostAfterCompression++;
				}
				Note(SuspensionEvent.Kind.ContactLost, w, frame, e => e.AfterCompression = afterHit);
			} else if (!unloaded && off_[w]) {
				off_[w] = false;
				SuspensionEvent open = Events.LastOrDefault(e => e.Wheel == w && e.What == SuspensionEvent.Kind.ContactLost);
				if (open != null) {
					open.Seconds = frame.Time - offSince_[w];
				}
			}

			if (now.Compression >= BottomedAt && !bottomed_[w]) {
				bottomed_[w] = true;
				r.Bottomed++;
				Note(SuspensionEvent.Kind.Bottomed, w, frame, null);
			} else if (now.Compression < BottomedAt - 0.05f) {
				bottomed_[w] = false;
			}

			bool spike = share > 0 && now.Load > share * SpikeTimes;
			if (spike && !spiking_[w]) {
				r.LoadSpikes++;
				Note(SuspensionEvent.Kind.LoadSpike, w, frame, e => e.To = share > 0 ? now.Load / share : 0f);
			}
			spiking_[w] = spike;
		}

		private void StartHit(int w, SuspensionFrame frame) {
			if (hitPeaks_[w].Count > 0) {
				recoveries_[w].Add(lowSinceHit_[w]);
			}
			hitPeaks_[w].Add(0f);
			peakThisHit_[w] = frame.Wheels[w].Compression;
			lowSinceHit_[w] = 1f;
			packingFrame_[w] = frame;
		}

		private readonly SuspensionFrame[] packingFrame_ = new SuspensionFrame[4];

		/// <summary>The end of a run of bumps: packing when the wheel kept getting lower in between (3 hits or more) and ended near the bump stop.</summary>
		private void EndHits(int w) {
			if (hitPeaks_[w].Count > 0) {
				hitPeaks_[w][hitPeaks_[w].Count - 1] = peakThisHit_[w];
			}
			List<float> lows = recoveries_[w];
			if (hitPeaks_[w].Count >= 3 && lows.Count >= 2) {
				bool climbing = true;
				for (int i = 1; i < lows.Count; i++) {
					climbing &= lows[i] > lows[i - 1] + 0.02f;
				}
				float end = Mathf.Max(peakThisHit_[w], lows[lows.Count - 1]);
				if (climbing && end >= 0.85f && lows[lows.Count - 1] - lows[0] >= 0.08f) {
					Wheels[w].Packing++;
					float from = lows[0];
					Note(SuspensionEvent.Kind.Packing, w, packingFrame_[w] ?? last_, e => {
						e.From = from;
						e.To = end;
					});
				}
			}
			hitPeaks_[w].Clear();
			lows.Clear();
			lowSinceHit_[w] = 1f;
		}

		private void Note(SuspensionEvent.Kind what, int w, SuspensionFrame frame, Action<SuspensionEvent> fill) {
			if (Events.Count >= 2000) {
				return;
			}
			var e = new SuspensionEvent { What = what, Wheel = w, Time = frame.Time, Position = frame.Position, Kmh = frame.Kmh };
			e.Along = AlongCourse?.Invoke(frame.Position) ?? -1f;
			fill?.Invoke(e);
			Events.Add(e);
		}

		/// <summary>Ends any run of bumps still open (call before reading the report).</summary>
		public void Finish() {
			for (int w = 0; w < 4; w++) {
				if (hitPeaks_[w].Count > 0) {
					EndHits(w);
				}
			}
		}

		// ---- What it means ----

		/// <summary>Plain-language notes on what the numbers point to, each with the wheel(s) it's about.</summary>
		public List<string> Advice() {
			var notes = new List<string>();
			for (int w = 0; w < 4; w++) {
				WheelReport r = Wheels[w];
				if (r.Seconds < 5) {
					continue;
				}
				string name = WheelNames[w];
				if (r.Packing > 0) {
					notes.Add($"{name}: packed down {r.Packing} time(s) through a run of bumps (it couldn't extend back before the next one). Try less rebound damping, slow and fast, or a little less fast bump.");
				}
				if (r.ContactLostAfterCompression >= 3) {
					notes.Add($"{name}: left the road {r.ContactLostAfterCompression} times right after a hard compression (thrown off the bump). Try less fast bump, or slightly less rebound so it follows the road down again.");
				}
				int otherLosses = r.ContactLost - r.ContactLostAfterCompression;
				if (otherLosses >= 3) {
					notes.Add($"{name}: unloaded {otherLosses} times over crests or dips. Rebound may be too slow for the wheel to follow the road down, or the spring too soft at that end.");
				}
				if (r.Bottomed >= 3) {
					notes.Add($"{name}: hit full compression {r.Bottomed} times. A stiffer spring, more slow bump, more ride height or more travel would keep it off the stops.");
				}
				if (r.LoadSpikes >= 5) {
					notes.Add($"{name}: load spiked to {SpikeTimes:0.#}x its share of the weight {r.LoadSpikes} times on bumps. Fast bump may be too stiff for this road.");
				}
				if (r.MaxCompression > 0 && r.MaxCompression < 0.6f && r.Bottomed == 0) {
					notes.Add($"{name}: used only up to {r.MaxCompression * 100:0}% of its travel. It could go softer (spring or slow bump) and use more of it.");
				}
				float share = r.BumpShare;
				if (r.BumpSeconds + r.ReboundSeconds > 2 && (share > 0.62f || share < 0.38f)) {
					notes.Add(share > 0.5f
						? $"{name}: the damper spends much longer moving in bump than rebound ({share * 100:0}% / {100 - share * 100:0}%): rebound is slow compared with bump, which leads to packing."
						: $"{name}: the damper spends much longer moving in rebound than bump ({share * 100:0}% / {100 - share * 100:0}%): bump is stiff compared with rebound.");
				}
			}
			if (notes.Count == 0 && Seconds >= 10) {
				notes.Add("Nothing stands out: no packing, no contact lost after compressions, no bottoming. Drive the bumpiest part of the road to see more.");
			}
			return notes;
		}

		public static string EventText(SuspensionEvent e) {
			string where = e.Along >= 0 ? $"at {TimeAttackRun.Distance(e.Along)}" : "";
			switch (e.What) {
				case SuspensionEvent.Kind.ContactLost:
					return $"{WheelNames[e.Wheel]} left the road{(e.AfterCompression ? " right after a compression" : "")}{(e.Seconds > 0 ? $" for {e.Seconds:0.00} s" : "")} {where}, {e.Kmh:0} km/h";
				case SuspensionEvent.Kind.Packing:
					return $"{WheelNames[e.Wheel]} packed down from {e.From * 100:0}% to {e.To * 100:0}% {where}, {e.Kmh:0} km/h";
				case SuspensionEvent.Kind.Bottomed:
					return $"{WheelNames[e.Wheel]} bottomed out {where}, {e.Kmh:0} km/h";
				default:
					return $"{WheelNames[e.Wheel]} load spike ({e.To:0.0}x its share) {where}, {e.Kmh:0} km/h";
			}
		}

		// ---- CSV ----

		/// <summary>Every frame as a CSV (time, speed, position, and each wheel's compression, damper speed, load, contact and slip), for a spreadsheet or a telemetry tool.</summary>
		public string ToCsv() {
			var text = new StringBuilder("time_s,kmh,x,y,z");
			foreach (string w in WheelShort) {
				text.Append($",{w}_compression_pct,{w}_damper_mm_s,{w}_load_n,{w}_contact,{w}_slip_angle,{w}_slip_ratio");
			}
			text.Append('\n');
			string F(double v, string format = "0.###") => v.ToString(format, CultureInfo.InvariantCulture);
			double start = Frames.Count > 0 ? Frames[0].Time : 0;
			for (int i = 0; i < Frames.Count; i++) {
				SuspensionFrame f = Frames[i];
				SuspensionFrame p = i > 0 ? Frames[i - 1] : f;
				double dt = f.Time - p.Time;
				text.Append(F(f.Time - start, "0.0000")).Append(',').Append(F(f.Kmh, "0.0")).Append(',')
					.Append(F(f.Position.x, "0.00")).Append(',').Append(F(f.Position.y, "0.00")).Append(',').Append(F(f.Position.z, "0.00"));
				for (int w = 0; w < 4; w++) {
					WheelFrame wf = f.Wheels[w];
					float travel = Setup[w]?.Travel > 0 ? Setup[w].Travel : 0.15f;
					double speed = dt > 0 ? (wf.Compression - p.Wheels[w].Compression) * travel / dt * 1000 : 0;
					text.Append(',').Append(F(wf.Compression * 100, "0.0")).Append(',').Append(F(speed, "0")).Append(',').Append(F(wf.Load, "0"))
						.Append(',').Append(wf.Contact ? "1" : "0").Append(',').Append(F(wf.SlipAngle, "0.000")).Append(',').Append(F(wf.SlipRatio, "0.000"));
				}
				text.Append('\n');
			}
			return text.ToString();
		}

		public string SaveCsv(string folder) {
			Directory.CreateDirectory(folder);
			string name = new string($"{Started.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)} {Label}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
			string path = Path.Combine(folder, name + ".csv");
			File.WriteAllText(path, ToCsv(), new UTF8Encoding(false));
			return path;
		}
	}
}
