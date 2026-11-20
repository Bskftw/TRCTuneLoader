// How busy lane traffic is over time (docs/archive/IMPROVEMENT_PLAN.md T12): quiet, normal, rush hour, or a cycle through them.
using UnityEngine;

namespace TRC.TuneLoader {
	public enum BusyPattern {
		Steady = 0,
		Quiet = 1,
		Rush = 2,
		/// <summary>Quiet to rush hour and back, every <see cref="TrafficBusy.CycleMinutes"/>.</summary>
		Cycle = 3,
	}

	/// <summary>The share of the chosen busyness wanted now. Game-free (SelfTest.TrafficScore.cs).</summary>
	public static class TrafficBusy {
		public const float QuietShare = 0.5f, RushShare = 1.6f;
		public const float CycleMinutes = 20f;

		/// <summary>The most it ever wants: the lane traffic keeps room for this many times the chosen cars.</summary>
		public const float Most = RushShare;

		public static readonly string[] Names = {
			"Steady (as picked)", "Quiet (half)", "Rush hour (1.6 times)", $"Rush hour comes and goes (every {CycleMinutes:0} min)",
		};

		/// <summary>The share at <paramref name="minutes"/> since traffic started.</summary>
		public static float Share(BusyPattern pattern, float minutes) {
			switch (pattern) {
				case BusyPattern.Quiet: return QuietShare;
				case BusyPattern.Rush: return RushShare;
				case BusyPattern.Cycle:
					// Quiet at the start, rush hour at half way, quiet again at the end; smooth all the way.
					float t = Mathf.Repeat(minutes / CycleMinutes, 1f);
					return Mathf.Lerp(QuietShare, RushShare, 0.5f - 0.5f * Mathf.Cos(t * 2f * Mathf.PI));
				default: return 1f;
			}
		}

		/// <summary>"rush hour", "quiet"... for the status line.</summary>
		public static string Now(BusyPattern pattern, float minutes) {
			float share = Share(pattern, minutes);
			return share >= 1.3f ? "rush hour" : share <= 0.7f ? "quiet" : "normal";
		}
	}
}
