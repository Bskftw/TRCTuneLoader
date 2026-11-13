using System;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>The telemetry overlay's sums (Stage 2.2): slip against the tyre's best grip, the smoothed g-force dot, the gear's name.</summary>
	internal static partial class SelfTest {
		private static void TelemetryTests() {
			Expect(Math.Abs(TelemetryFrame.PastGrip(0.05f, 0.1f, 0.02f, 0.1f) - 0.5f) < 1e-4f, "slip: half the best slip angle is 0.5 (still gripping)");
			Expect(Math.Abs(TelemetryFrame.PastGrip(-0.3f, 0.1f, 0f, 0.1f) - 3f) < 1e-4f, "slip: sliding the other way counts the same (3x past the best)");
			Expect(Math.Abs(TelemetryFrame.PastGrip(0f, 0.1f, 0.25f, 0.1f) - 2.5f) < 1e-4f, "slip: wheelspin counts as well as the angle (the bigger one shows)");
			Expect(TelemetryFrame.PastGrip(0.5f, 0f, float.NaN, 0.1f) == 0f, "slip: no best slip from CarX, or a broken value, never shows as sliding");
			Expect(TelemetryFrame.PastGrip(1000f, 0.1f, 0f, 0.1f) == 10f, "slip: capped at 10x");

			// Smoothing: about 63% of the way after its time, the same whatever the frame rate.
			Vector2 fast = Vector2.zero, slow = Vector2.zero;
			for (int i = 0; i < 12; i++) {
				fast = TelemetryFrame.Smooth(fast, Vector2.one, 0.01f);
			}
			for (int i = 0; i < 2; i++) {
				slow = TelemetryFrame.Smooth(slow, Vector2.one, 0.06f);
			}
			Expect(Math.Abs(fast.x - 0.632f) < 0.01f && Math.Abs(fast.x - slow.x) < 1e-3f,
				$"the g-force dot moves the same way at 100 fps and at 17 fps ({fast.x:0.000} / {slow.x:0.000})");
			Expect(TelemetryFrame.Smooth(Vector2.zero, Vector2.one, 0f) == Vector2.one, "no time passed (paused): the dot shows the reading as it is");

			Expect(TelemetryFrame.GearText(-1) == "R" && TelemetryFrame.GearText(0) == "N" && TelemetryFrame.GearText(4) == "4", "gear names: R, N, 4");
		}
	}
}
