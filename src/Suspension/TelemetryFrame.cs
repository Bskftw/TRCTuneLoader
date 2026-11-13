// One frame of your own car's telemetry (inputs, gear and RPM, g-force, wheel slip), and the small sums the overlay needs.
using System;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Your own car right now, as CarX's physics has it (filled by the adapter's TryGetTelemetry every frame while the
	/// telemetry overlay is on). Read-only: nothing here changes the car.
	/// </summary>
	public sealed class TelemetryFrame {
		/// <summary>Pedals and handbrake, 0 (off) to 1 (fully on).</summary>
		public float Throttle, Brake, Handbrake, Clutch;

		/// <summary>The steering, -1 (full lock one way) to 1 (the other).</summary>
		public float Steer;

		/// <summary>-1 reverse, 0 neutral, 1 and up the gears (CarX's own numbering).</summary>
		public int Gear;

		public float Rpm, RevLimit, Kmh;

		/// <summary>Acceleration in g: forwards positive (braking is negative), and to the car's right.</summary>
		public float LongG, SideG;

		/// <summary>
		/// Each wheel's slip against the slip where its tyre grips best (CarX's own "best slip angle" and "best slip ratio"):
		/// 1 is right at the peak, under 1 still gripping, over 1 sliding or spinning. Front left, front right, rear left, rear right.
		/// </summary>
		public readonly float[] Slip = new float[4];

		/// <summary>The wheel touches the road.</summary>
		public readonly bool[] Contact = new bool[4];

		/// <summary>"R", "N" or the gear's number.</summary>
		public static string GearText(int gear) => gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString(System.Globalization.CultureInfo.InvariantCulture);

		/// <summary>
		/// How far past its grip a wheel is: the bigger of its slip angle and slip ratio, each against its best. 0 when CarX
		/// gives no best (a wheel not set up yet), so a broken value never shows as sliding.
		/// </summary>
		public static float PastGrip(float slipAngle, float bestSlipAngle, float slipRatio, float bestSlipRatio) {
			float angle = bestSlipAngle > 1e-4f && !float.IsNaN(slipAngle) ? Math.Abs(slipAngle) / bestSlipAngle : 0f;
			float ratio = bestSlipRatio > 1e-4f && !float.IsNaN(slipRatio) ? Math.Abs(slipRatio) / bestSlipRatio : 0f;
			return Math.Min(10f, Math.Max(angle, ratio));
		}

		/// <summary>
		/// The g-force dot, smoothed so it doesn't shake with every bump: moves <paramref name="now"/>'s way over about
		/// <paramref name="seconds"/>, whatever the frame rate.
		/// </summary>
		public static Vector2 Smooth(Vector2 shown, Vector2 now, float deltaTime, float seconds = 0.12f) {
			if (deltaTime <= 0f || seconds <= 0f) {
				return now;
			}
			float k = 1f - Mathf.Exp(-deltaTime / seconds);
			return shown + (now - shown) * k;
		}
	}
}
