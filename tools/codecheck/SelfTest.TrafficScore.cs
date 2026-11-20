using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>The traffic score (Stage 4.2): near misses, squeezes, the combo and the crash rules, driven past made-up cars.</summary>
	internal static partial class SelfTest {
		/// <summary>The player drives north at <paramref name="kmh"/> for <paramref name="seconds"/>; each car drives north at its own speed from its start.</summary>
		private static TrafficScore Drive(TrafficScore score, float kmh, float seconds, params (long Key, float Side, float Ahead, float Kmh)[] cars) {
			double t = 0;
			var list = new List<(long, Vector3)>();
			for (int frame = 0; frame <= seconds * 60; frame++, t += 1.0 / 60) {
				list.Clear();
				foreach (var car in cars) {
					list.Add((car.Key, new Vector3(car.Side, 0f, car.Ahead + car.Kmh / 3.6f * (float)t)));
				}
				score.Tick(t, new Vector3(0f, 0f, kmh / 3.6f * (float)t), Vector3.forward, kmh, list);
			}
			return score;
		}

		private static void TrafficScoreTests() {
			TrafficScore near = Drive(new TrafficScore(), 120f, 3f, (1, 2.5f, 20f, 60f));
			Expect(near.NearMisses == 1 && near.Points > 50f && near.Combo == 2, $"traffic score: a close fast pass is a near miss ({near.Points:0} points)");

			TrafficScore far = Drive(new TrafficScore(), 120f, 3f, (1, 6f, 20f, 60f));
			Expect(far.NearMisses == 0 && far.Points == 0f, "traffic score: a pass two lanes away scores nothing");
			TrafficScore slow = Drive(new TrafficScore(), 70f, 6f, (1, 2.5f, 10f, 60f));
			Expect(slow.NearMisses == 0, "traffic score: only a little faster than the car scores nothing");

			TrafficScore squeeze = Drive(new TrafficScore(), 140f, 3f, (1, 2.5f, 20f, 60f), (2, -2.5f, 20.5f, 60f));
			Expect(squeeze.Squeezes == 1 && squeeze.NearMisses == 2, "traffic score: cars on both sides at once are a tight squeeze");

			TrafficScore combo = Drive(new TrafficScore(), 120f, 8f, (1, 2.5f, 20f, 60f), (2, 2.5f, 60f, 60f), (3, -2.5f, 100f, 60f));
			Expect(combo.Combo == 4, $"traffic score: each near miss adds to the combo (combo {combo.Combo})");

			float before = combo.Points;
			combo.Rule = CrashRule.ComboLost;
			combo.Crash(10);
			Expect(combo.Combo == 1 && combo.Points == before, "traffic score: combo lost keeps the points");
			combo.Rule = CrashRule.ScoreReset;
			combo.Crash(10);
			Expect(combo.Points == 0f && combo.Best == before, "traffic score: score reset empties it, the best stays");

			var timeOut = new TrafficScore { Rule = CrashRule.TimeOut };
			timeOut.Crash(0);
			Drive(timeOut, 120f, 3f, (1, 2.5f, 20f, 60f));
			Expect(timeOut.NearMisses == 0, "traffic score: nothing scores during a time-out");

			var stop = Drive(new TrafficScore(), 120f, 3f, (1, 2.5f, 20f, 60f));
			stop.Tick(4, Vector3.zero, Vector3.forward, 0f, new List<(long, Vector3)>());
			Expect(stop.Combo == 1, "traffic score: a full stop loses the combo");

			Expect(TrafficBusy.Share(BusyPattern.Steady, 7f) == 1f && TrafficBusy.Share(BusyPattern.Quiet, 7f) == TrafficBusy.QuietShare, "rush hour: steady and quiet stay put");
			Expect(Mathf.Abs(TrafficBusy.Share(BusyPattern.Cycle, 0f) - TrafficBusy.QuietShare) < 0.01f && Mathf.Abs(TrafficBusy.Share(BusyPattern.Cycle, TrafficBusy.CycleMinutes / 2f) - TrafficBusy.RushShare) < 0.01f
				&& Mathf.Abs(TrafficBusy.Share(BusyPattern.Cycle, TrafficBusy.CycleMinutes) - TrafficBusy.QuietShare) < 0.01f, "rush hour: the cycle goes quiet, rush, quiet");
			Expect(TrafficBusy.Share(BusyPattern.Cycle, 3.3f) <= TrafficBusy.Most && TrafficBusy.Share(BusyPattern.Rush, 0f) <= TrafficBusy.Most, "rush hour: never more than the lanes keep room for");
		}
	}
}
