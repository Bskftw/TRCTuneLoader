using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>0.16: suspension logging, driven by made-up wheels: packing down, contact lost after a compression, jumps, bottoming, the histogram.</summary>
	internal static partial class SelfTest {
		private static void SuspensionTests() {
			SuspensionPackingDown();
			SuspensionContactAndJumps();
			SuspensionHistogramAndBottoming();
			SuspensionSetupSheet();
		}

		private static SuspensionLog NewLog() {
			var log = new SuspensionLog { Label = "Test" };
			for (int w = 0; w < 4; w++) {
				log.Setup[w] = new CornerSetup { Travel = 0.15f, Spring = 40000, CornerMass = 300, SlowBump = 3000, FastBump = 1500, SlowRebound = 5000, FastRebound = 2500 };
			}
			return log;
		}

		/// <summary>Four wheels at rest at 45%, all on the road, with <paramref name="change"/> applied.</summary>
		private static SuspensionFrame Wheels(double t, Action<WheelFrame[]> change = null) {
			var frame = new SuspensionFrame { Time = t, Position = new Vector3(0, 0, (float)t * 20f), Kmh = 72 };
			for (int w = 0; w < 4; w++) {
				frame.Wheels[w] = new WheelFrame { Compression = 0.45f, Load = 300 * 9.81f, Contact = true };
			}
			change?.Invoke(frame.Wheels);
			return frame;
		}

		private static void SuspensionPackingDown() {
			// Front left hits a bump every 0.3 s; rebound is too slow to get it back out in between, so it sinks onto the stop.
			SuspensionLog log = NewLog();
			float c = 0.4f;
			for (double t = 0; t < 6; t += 0.01) {
				bool bumpy = t >= 2 && t < 3.4;
				double phase = (t - 2) % 0.3;
				if (bumpy && phase < 0.05) {
					c = Mathf.Min(1f, c + 0.04f); // hit: 0.2 compression in 0.05 s = 0.6 m/s
				} else {
					c = Mathf.Max(0.4f, c - (bumpy ? 0.0035f : 0.01f)); // slow rebound
				}
				float now = c;
				log.Add(Wheels(t, w => w[0].Compression = now));
			}
			log.Finish();
			Expect(log.Wheels[0].Packing >= 1 && log.Wheels[1].Packing == 0 && log.Events.Any(e => e.What == SuspensionEvent.Kind.Packing && e.Wheel == 0 && e.To >= 0.85f),
				$"packing down is found: a wheel that doesn't extend back between bumps and ends near the stop ({log.Wheels[0].Packing}, peak {log.Wheels[0].MaxCompression:0.00})");
			Expect(log.Advice().Any(a => a.StartsWith("Front left: packed down", StringComparison.Ordinal)), "packing down comes with advice about rebound");

			// The same bumps with quick rebound: it comes back out every time, so no packing.
			SuspensionLog quick = NewLog();
			c = 0.4f;
			for (double t = 0; t < 6; t += 0.01) {
				bool bumpy = t >= 2 && t < 3.4;
				double phase = (t - 2) % 0.3;
				c = bumpy && phase < 0.05 ? Mathf.Min(1f, c + 0.04f) : Mathf.Max(0.4f, c - 0.02f);
				float now = c;
				quick.Add(Wheels(t, w => w[0].Compression = now));
			}
			quick.Finish();
			Expect(quick.Wheels[0].Packing == 0, "the same bumps with quick rebound don't pack down");
		}

		private static void SuspensionContactAndJumps() {
			SuspensionLog log = NewLog();
			for (double t = 0; t < 10; t += 0.01) {
				double second = t % 2;
				log.Add(Wheels(t, w => {
					// Rear right: a hard compression, then thrown off the road for 0.15 s, every 2 s.
					if (second >= 1 && second < 1.05) {
						w[3].Compression = 0.45f + (float)(second - 1) * 8f;
					} else if (second >= 1.1 && second < 1.25) {
						w[3].Contact = false;
						w[3].Load = 0;
						w[3].Compression = 0.1f;
					}
					// Every wheel in the air together at 9 s: a jump, not lost contact.
					if (t >= 9 && t < 9.4) {
						for (int i = 0; i < 4; i++) {
							w[i].Contact = false;
							w[i].Load = 0;
						}
					}
				}));
			}
			log.Finish();
			WheelReport rr = log.Wheels[3];
			// Five throws, but the one at 9.1 s is inside the jump (all four off), so it isn't one.
			Expect(rr.ContactLost == 4 && rr.ContactLostAfterCompression == 4 && log.Wheels[0].ContactLost == 0,
				$"a tyre thrown off the road after a compression is counted, and a jump with all four in the air isn't ({rr.ContactLost} lost, {rr.ContactLostAfterCompression} after a compression)");
			SuspensionEvent first = log.Events.First(e => e.What == SuspensionEvent.Kind.ContactLost);
			Expect(first.AfterCompression && Math.Abs(first.Seconds - 0.15) < 0.03, $"each time says how long the tyre was off ({first.Seconds:0.00} s)");
			Expect(log.Advice().Any(a => a.StartsWith("Rear right: left the road", StringComparison.Ordinal)), "being thrown off comes with advice about fast bump");
		}

		private static void SuspensionHistogramAndBottoming() {
			SuspensionLog log = NewLog();
			// Front right moves in a sine (2 Hz, ±30% of 150 mm): the same speed both ways, so bump and rebound are even.
			for (double t = 0; t < 10; t += 0.005) {
				float c = 0.5f + 0.3f * Mathf.Sin((float)(2 * Math.PI * 2 * t));
				// Rear left bottoms out three times (at 2.5, 5 and 7.5 s).
				float rl = t % 2.5 < 0.1 && t > 0.5 ? 0.99f : 0.5f;
				log.Add(Wheels(t, w => {
					w[1].Compression = c;
					w[2].Compression = rl;
				}));
			}
			log.Finish();
			WheelReport fr = log.Wheels[1];
			float expected = 0.3f * 0.15f * 2f * Mathf.PI * 2f * 1000f; // fastest damper speed, mm/s
			Expect(Math.Abs(fr.BumpShare - 0.5f) < 0.05f && Math.Abs(fr.FastestBumpMmS - expected) < 25f && Math.Abs(fr.FastestReboundMmS - expected) < 25f,
				$"an even motion shows as even bump and rebound in the histogram ({fr.BumpShare * 100:0}% bump, fastest {fr.FastestBumpMmS:0}/{fr.FastestReboundMmS:0} mm/s, expected about {expected:0})");
			Expect(Math.Abs(fr.Histogram.Sum() - fr.Seconds) < 1e-6 && fr.Seconds > 9.9, "every moment lands in one bar of the histogram");
			Expect(log.Wheels[2].Bottomed == 3, $"each time a wheel reaches full compression is counted once ({log.Wheels[2].Bottomed})");

			// The game showing the same physics step twice: skipped, so it doesn't look like a stop and a jump.
			SuspensionLog repeats = NewLog();
			for (int i = 0; i < 400; i++) {
				// Frames every 5 ms, but the physics only moves every other one (10 ms steps): 1 Hz, ±20% of 150 mm, at most 188 mm/s.
				int step = i / 2;
				float c = 0.5f + 0.2f * Mathf.Sin((float)(step * 0.01 * 2 * Math.PI));
				repeats.Add(Wheels(i * 0.005, w => w[0].Compression = c));
			}
			Expect(repeats.Wheels[0].FastestBumpMmS < 250f, $"a frame where nothing moved isn't taken as a stop ({repeats.Wheels[0].FastestBumpMmS:0} mm/s)");
			string csv = log.ToCsv();
			Expect(csv.StartsWith("time_s,kmh,x,y,z,FL_compression_pct", StringComparison.Ordinal) && csv.Split('\n').Length >= log.Frames.Count + 1,
				"the CSV has a header and one line per frame");
		}

		private static void SuspensionSetupSheet() {
			var c = new CornerSetup { Spring = 40000, CornerMass = 300, SlowRebound = 2 * Mathf.Sqrt(40000 * 300) * 0.7f };
			Expect(Math.Abs(c.Frequency - 1.838f) < 0.01f && Math.Abs(c.Ratio(c.SlowRebound) - 0.7f) < 0.001f && new CornerSetup().Frequency == 0,
				$"the setup sheet works out frequency and damping ratio ({c.Frequency:0.000} Hz, {c.Ratio(c.SlowRebound):0.00})");
		}
	}
}
