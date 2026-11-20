// The traffic score (docs/archive/IMPROVEMENT_PLAN.md T9, T10): near misses and tight squeezes past traffic cars, a combo that builds, and what a crash costs.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>What a crash into traffic costs (the host picks, T10; decided Nov 12).</summary>
	public enum CrashRule {
		/// <summary>The combo's gone; the points stay.</summary>
		ComboLost = 0,
		/// <summary>The whole score goes back to 0.</summary>
		ScoreReset = 1,
		/// <summary>The combo's gone and nothing scores for a few seconds.</summary>
		TimeOut = 2,
	}

	/// <summary>
	/// Scoring passes of traffic cars, on this PC only (for fun, never rated). A <b>near miss</b> is getting past a car closely
	/// and much faster; more the faster and closer. Two near misses on opposite sides close together are a <b>tight squeeze</b>,
	/// worth double. Each one adds to the combo (times the points); it's lost after a while without one, on a full stop or
	/// on a crash (<see cref="Crash"/>, by <see cref="Rule"/>). Game-free, so the self-tests drive it (SelfTest.TrafficScore.cs).
	/// </summary>
	public sealed class TrafficScore {
		public const float MinKmh = 60f;
		public const float FasterByKmh = 15f;
		/// <summary>Centre to centre, sideways: two cars side by side in neighbouring lanes are about 3.5 m apart.</summary>
		public const float NearMetres = 3.2f;
		public const double SqueezeSeconds = 0.6;
		public const double ComboSeconds = 8;
		public const double TimeOutSeconds = 5;
		public const float StopKmh = 10f;
		public const int MaxCombo = 10;

		public CrashRule Rule;

		/// <summary>Points now, the best this traffic run (and before, when set), and the combo (1 = none).</summary>
		public float Points { get; private set; }
		public float Best;
		public int Combo { get; private set; } = 1;
		public int NearMisses { get; private set; }
		public int Squeezes { get; private set; }

		/// <summary>The last thing that happened ("Near miss", "Tight squeeze", "Crash: combo lost"...) and its points, for the pop-up.</summary>
		public string Last { get; private set; } = "";
		public float LastPoints { get; private set; }

		/// <summary>Until when (the clock passed to <see cref="Tick"/>) nothing scores, after a crash with <see cref="CrashRule.TimeOut"/>.</summary>
		public double TimeOutUntil { get; private set; } = double.MinValue;

		private struct Seen {
			public float Ahead;
			public Vector3 At;
			public double When;
			public float Kmh;
			public bool KnowsKmh;
		}

		private readonly Dictionary<long, Seen> seen_ = new Dictionary<long, Seen>();
		private readonly List<long> gone_ = new List<long>();
		private readonly HashSet<long> now_ = new HashSet<long>();
		private double lastNearAt_ = double.MinValue;
		private int lastNearSide_;

		/// <summary>
		/// Called every frame with the traffic cars near the player (their key and where they are). Returns the points scored
		/// this frame (0 for none).
		/// </summary>
		public float Tick(double now, Vector3 position, Vector3 forward, float kmh, IList<(long Key, Vector3 Position)> cars) {
			kmh = Mathf.Abs(kmh);
			if (now - lastNearAt_ > ComboSeconds) {
				Combo = 1;
			}
			if (kmh < StopKmh && Combo > 1) {
				Combo = 1;
				Last = "Stopped: combo lost";
				LastPoints = 0f;
			}
			Vector3 along = new Vector3(forward.x, 0f, forward.z);
			if (along.sqrMagnitude < 1e-6f) {
				return 0f;
			}
			along.Normalize();
			var right = new Vector3(along.z, 0f, -along.x);
			float scored = 0f;
			now_.Clear();
			for (int i = 0; i < cars.Count; i++) {
				(long key, Vector3 at) = cars[i];
				now_.Add(key);
				Vector3 offset = at - position;
				float ahead = Vector3.Dot(offset, along);
				float side = Vector3.Dot(offset, right);
				var seen = new Seen { Ahead = ahead, At = at, When = now };
				if (seen_.TryGetValue(key, out Seen before)) {
					double dt = now - before.When;
					if (dt > 1e-4) {
						float moved = (float)(new Vector3(at.x - before.At.x, 0f, at.z - before.At.z).magnitude / dt) * 3.6f;
						seen.Kmh = before.KnowsKmh ? Mathf.Lerp(before.Kmh, moved, 0.3f) : moved;
						seen.KnowsKmh = true;
					} else {
						seen.Kmh = before.Kmh;
						seen.KnowsKmh = before.KnowsKmh;
					}
					if (before.Ahead > 0f && ahead <= 0f && before.KnowsKmh && Mathf.Abs(side) <= NearMetres &&
						kmh >= MinKmh && kmh >= before.Kmh + FasterByKmh && now >= TimeOutUntil) {
						scored += Pass(now, kmh, side);
					}
				}
				seen_[key] = seen;
			}
			gone_.Clear();
			foreach (long key in seen_.Keys) {
				if (!now_.Contains(key)) {
					gone_.Add(key);
				}
			}
			foreach (long key in gone_) {
				seen_.Remove(key);
			}
			Points += scored;
			Best = Mathf.Max(Best, Points);
			return scored;
		}

		private float Pass(double now, float kmh, float side) {
			// 50 at 100 km/h a lane's width away, up to 100 right alongside; faster counts more.
			float points = 50f * (kmh / 100f) * (kmh / 100f) * (1f + (NearMetres - Mathf.Abs(side)) / NearMetres);
			int sideSign = side >= 0f ? 1 : -1;
			bool squeeze = now - lastNearAt_ <= SqueezeSeconds && lastNearSide_ == -sideSign;
			if (squeeze) {
				points *= 2f;
				Squeezes++;
			}
			NearMisses++;
			points *= Combo;
			Last = squeeze ? "Tight squeeze" : "Near miss";
			LastPoints = points;
			Combo = Math.Min(MaxCombo, Combo + 1);
			lastNearAt_ = now;
			lastNearSide_ = sideSign;
			return points;
		}

		/// <summary>From nothing again (a score attack starting); the best stays.</summary>
		public void Restart() {
			Points = 0f;
			Combo = 1;
			NearMisses = 0;
			Squeezes = 0;
			TimeOutUntil = double.MinValue;
			Last = "";
			LastPoints = 0f;
		}

		/// <summary>The player hit a traffic car: what it costs, by <see cref="Rule"/>. Returns what to show.</summary>
		public string Crash(double now) {
			Combo = 1;
			LastPoints = 0f;
			switch (Rule) {
				case CrashRule.ScoreReset:
					Points = 0f;
					Last = "Crash: score reset";
					break;
				case CrashRule.TimeOut:
					TimeOutUntil = now + TimeOutSeconds;
					Last = $"Crash: no points for {TimeOutSeconds:0} s";
					break;
				default:
					Last = "Crash: combo lost";
					break;
			}
			return Last;
		}

		public static string RuleName(CrashRule rule) {
			switch (rule) {
				case CrashRule.ScoreReset: return "Score reset";
				case CrashRule.TimeOut: return $"Time-out (no points for {TimeOutSeconds:0} s)";
				default: return "Combo lost";
			}
		}
	}
}
