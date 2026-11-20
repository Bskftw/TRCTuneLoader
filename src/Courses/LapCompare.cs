// Lap compare (docs/archive/IMPROVEMENT_PLAN.md F4): the last run against the best, stretch by stretch, to show where time went.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TRC.TuneLoader {
	/// <summary>
	/// Compares two traces (the time at every <see cref="TimeAttackRun.TraceStep"/> m of a run, as the live delta uses) in
	/// stretches of a set length: the time each took over each stretch, and the speed that works out to. The stretches with
	/// the most time lost are where to look. Game-free (SelfTest.Stage6.cs).
	/// </summary>
	public static class LapCompare {
		public struct Stretch {
			public float From, To;
			/// <summary>Seconds lost here (negative: gained).</summary>
			public float Lost;
			public float MyKmh, BestKmh;
		}

		public static List<Stretch> Stretches(float[] mine, float[] best, float step, float stretchMetres) {
			var list = new List<Stretch>();
			if (mine == null || best == null || step <= 0f) {
				return list;
			}
			int n = Math.Min(mine.Length, best.Length);
			int per = Math.Max(1, (int)Math.Round(stretchMetres / step));
			for (int i = 0; i + 1 < n; i += per) {
				int j = Math.Min(n - 1, i + per);
				float myTime = mine[j] - mine[i], bestTime = best[j] - best[i];
				float metres = (j - i) * step;
				if (myTime <= 0f || bestTime <= 0f) {
					continue;
				}
				list.Add(new Stretch {
					From = i * step, To = j * step, Lost = myTime - bestTime,
					MyKmh = metres / myTime * 3.6f, BestKmh = metres / bestTime * 3.6f,
				});
			}
			return list;
		}

		/// <summary>The <paramref name="count"/> stretches where the most time was lost, most first (only ones that lost time).</summary>
		public static List<Stretch> Worst(List<Stretch> stretches, int count) =>
			stretches.Where(s => s.Lost > 0.005f).OrderByDescending(s => s.Lost).Take(count).ToList();

		/// <summary>The <paramref name="count"/> stretches where the most time was gained, most first.</summary>
		public static List<Stretch> Best(List<Stretch> stretches, int count) =>
			stretches.Where(s => s.Lost < -0.005f).OrderBy(s => s.Lost).Take(count).ToList();
	}
}
