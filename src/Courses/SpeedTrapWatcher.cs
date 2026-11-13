// Watches the car in free roam: which trap's line it just went through, either way, without counting one twice.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Watches the car in free roam: going through a trap's line (either way, between its ends, not on a road above
	/// or below) measures the speed at that moment, worked out between frames. A trap counts again after a few seconds.
	/// </summary>
	public sealed class SpeedTrapWatcher {
		private const float NearMetres = 80f;
		private const double AgainSeconds = 4;
		private const float JumpMetres = 40f;

		private Vector3 last_;
		private float lastSpeed_;
		private bool hasLast_;
		private readonly Dictionary<string, double> lastHit_ = new Dictionary<string, double>(StringComparer.Ordinal);

		public void Reset() {
			hasLast_ = false;
		}

		/// <summary>Called every frame; returns the trap the car just went through, if any.</summary>
		public SpeedTrapHit? Tick(double now, Vector3 position, float speedKmh, IList<SpeedTrap> traps) {
			speedKmh = Mathf.Abs(speedKmh);
			SpeedTrapHit? hit = null;
			if (hasLast_ && Vector3.Distance(last_, position) < JumpMetres && traps != null) {
				foreach (SpeedTrap trap in traps) {
					if ((trap.Gate.Center - position).sqrMagnitude > NearMetres * NearMetres) {
						continue;
					}
					if (!CrossedEitherWay(trap.Gate, last_, position, out float fraction)) {
						continue;
					}
					if (lastHit_.TryGetValue(trap.Key, out double at) && now - at < AgainSeconds) {
						continue;
					}
					lastHit_[trap.Key] = now;
					hit = new SpeedTrapHit { Trap = trap, Kmh = Mathf.Lerp(lastSpeed_, speedKmh, fraction) };
					break;
				}
			}
			last_ = position;
			lastSpeed_ = speedKmh;
			hasLast_ = true;
			return hit;
		}

		public static bool CrossedEitherWay(CourseGate gate, Vector3 from, Vector3 to, out float fraction) {
			if (CourseGeometry.Crossed(gate, from, to, out fraction)) {
				return true;
			}
			var back = new CourseGate { Center = gate.Center, Forward = -gate.Forward, Width = gate.Width };
			return CourseGeometry.Crossed(back, from, to, out fraction);
		}
	}
}
