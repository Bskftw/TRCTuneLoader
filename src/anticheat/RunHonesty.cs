// Whether a timed run was driven honestly: no no-clip, and the game's clock running at real speed. A run that wasn't is thrown out quietly.
using System;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Watches one timed run (bskftw, Nov 18): no-clip at any moment, or a game clock that runs faster or slower than
	/// the PC's real clock (a speed hack), and the run is thrown out quietly: no time saved, nothing on any board, no
	/// pop-up, nothing sent. Teleports and game speed changes are already caught by <see cref="TimeAttackRun"/>.
	/// No game code in here, so the self-test can drive it.
	/// </summary>
	public sealed class RunHonesty {
		/// <summary>How often the car is looked at.</summary>
		public const float CheckSeconds = 0.25f;

		/// <summary>A sign must be seen this many checks in a row (half a second), so a reset's one odd frame doesn't count.</summary>
		public const int SignChecks = 2;

		/// <summary>The clocks are compared over this many real seconds at a time.</summary>
		public const double ClockWindow = 5.0;

		/// <summary>The game's clock may differ from the real one by this much (5%) before the run is thrown out.</summary>
		public const double ClockSlack = 0.05;

		/// <summary>Why the run doesn't count ("" while it's fine). For the log only: the player isn't told.</summary>
		public string ThrownOut { get; private set; } = "";

		public bool Clean => ThrownOut.Length == 0;

		private int signsInARow_;
		private bool hasLast_;
		private Vector3 last_;
		private double lastAt_;
		private bool clockStarted_;
		private double gameFrom_;
		private DateTime wallFrom_;

		public void Reset() {
			ThrownOut = "";
			signsInARow_ = 0;
			clockStarted_ = false;
			hasLast_ = false;
		}

		/// <summary>
		/// The car moving further than its own speed allows since the last look (bskftw's no-clip log, Nov 18: KINO's no-clip
		/// leaves the physics alone and moves the car itself, about 400 m/s while its speed read 0). "" when it's fine.
		/// A reset or teleport is one jump, so it never makes two in a row (<see cref="SignChecks"/>).
		/// </summary>
		public string MovedWithoutSpeed(Vector3 position, double now, float speedKmh) {
			double seconds = now - lastAt_;
			bool had = hasLast_;
			Vector3 from = last_;
			hasLast_ = true;
			last_ = position;
			lastAt_ = now;
			if (!had || seconds <= 0 || seconds > 2) {
				return "";
			}
			float metres = Vector3.Distance(from, position);
			double allowed = Math.Max(0f, speedKmh) / 3.6 * seconds * 1.5 + 4 * seconds + 0.5;
			return metres > allowed ? $"moving {metres / seconds * 3.6:0} km/h while its speed reads {speedKmh:0}" : "";
		}

		/// <summary>One look at the car: <paramref name="sign"/> is what's wrong, or "".</summary>
		public void NoClip(string sign) {
			if (!Clean) {
				return;
			}
			signsInARow_ = string.IsNullOrEmpty(sign) ? 0 : signsInARow_ + 1;
			if (signsInARow_ >= SignChecks) {
				ThrownOut = $"no-clip ({sign})";
			}
		}

		/// <summary>
		/// The game's own clock (Unity's realtimeSinceStartup, which a speed hack speeds up) against the PC's wall
		/// clock (which it doesn't). Both keep going through a freeze, so a stutter isn't a speed hack.
		/// </summary>
		public void Clock(double gameSeconds, DateTime wallUtc) {
			if (!Clean) {
				return;
			}
			if (!clockStarted_) {
				clockStarted_ = true;
				gameFrom_ = gameSeconds;
				wallFrom_ = wallUtc;
				return;
			}
			double wall = (wallUtc - wallFrom_).TotalSeconds;
			if (wall < 0 || wall > ClockWindow * 6) {
				// The PC's clock was set, or the game was asleep: start again rather than guess.
				gameFrom_ = gameSeconds;
				wallFrom_ = wallUtc;
				return;
			}
			if (wall < ClockWindow) {
				return;
			}
			double ratio = (gameSeconds - gameFrom_) / wall;
			if (Math.Abs(ratio - 1.0) > ClockSlack) {
				ThrownOut = $"the game's clock ran at {ratio:0.00}x real time";
			}
			gameFrom_ = gameSeconds;
			wallFrom_ = wallUtc;
		}
	}
}
