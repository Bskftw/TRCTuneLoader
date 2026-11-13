// A suspension log: each wheel's travel, damper speeds, loads, hits, packing and contact lost, added frame by frame.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
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
	public sealed partial class SuspensionLog {
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
	}
}
