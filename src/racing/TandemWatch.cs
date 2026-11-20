// Tandem aids (docs/archive/IMPROVEMENT_PLAN.md F10): the gap to the car you're chasing, both cars' drift angles, and how long you've held it close.
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Measurements for a tandem, never a judgement (bskftw: drift judging "maybe never"): the gap between the two cars' bodies
	/// (door to door, seen from above: bskftw, Nov 18, a foot from a door showed 2.5 m middle to middle), each
	/// car's drift angle (how far its nose points off the way it's moving, from where it is frame to frame, so the other
	/// car's is rougher than your own), and "held close": how long the gap has stayed under <see cref="CloseMetres"/>
	/// without a break, with the best this session. Game-free (SelfTest.Stage6.cs).
	/// </summary>
	public sealed class TandemWatch {
		public const float CloseMetres = 10f;
		/// <summary>Moving slower than this (m/s), the drift angle isn't worked out (it would only be noise).</summary>
		public const float MovingAtLeast = 3f;

		public float Gap { get; private set; }
		public float MyAngle { get; private set; }
		public float TheirAngle { get; private set; }
		public float Held { get; private set; }
		public float BestHeld { get; private set; }

		private Vector3 lastMine_, lastTheirs_;
		private bool has_;

		public void Reset() {
			has_ = false;
			Held = 0f;
			MyAngle = TheirAngle = 0f;
		}

		/// <summary>One frame of <paramref name="dt"/> seconds: both cars' places and which way their noses point.</summary>
		public void Tick(float dt, Vector3 myPosition, Vector3 myForward, Bounds myBody, Vector3 theirPosition, Vector3 theirForward, Bounds theirBody) {
			Gap = BodyGap(myPosition, myForward, myBody, theirPosition, theirForward, theirBody);
			if (has_ && dt > 0f) {
				MyAngle = Mathf.Lerp(MyAngle, DriftAngle(myForward, (myPosition - lastMine_) / dt), 0.25f);
				TheirAngle = Mathf.Lerp(TheirAngle, DriftAngle(theirForward, (theirPosition - lastTheirs_) / dt), 0.15f);
			}
			lastMine_ = myPosition;
			lastTheirs_ = theirPosition;
			has_ = true;
			Held = Gap <= CloseMetres ? Held + dt : 0f;
			BestHeld = Mathf.Max(BestHeld, Held);
		}

		/// <summary>A body when the car's own isn't known: an ordinary car, 1.8 m by 4.4 m.</summary>
		public static readonly Bounds UsualBody = new Bounds(Vector3.zero, new Vector3(1.8f, 1.3f, 4.4f));

		/// <summary>The shortest gap (m) between two car bodies seen from above; 0 when they overlap.</summary>
		public static float BodyGap(Vector3 aPosition, Vector3 aForward, Bounds aBody, Vector3 bPosition, Vector3 bForward, Bounds bBody) {
			Vector2[] a = Outline(aPosition, aForward, aBody);
			Vector2[] b = Outline(bPosition, bForward, bBody);
			if (!Apart(a, b) && !Apart(b, a)) {
				return 0f;
			}
			float best = float.MaxValue;
			for (int i = 0; i < 4; i++) {
				for (int j = 0; j < 4; j++) {
					best = Mathf.Min(best, Mathf.Min(ToEdge(a[i], b[j], b[(j + 1) % 4]), ToEdge(b[i], a[j], a[(j + 1) % 4])));
				}
			}
			return best;
		}

		/// <summary>The body's four corners on the level (x, z), going round.</summary>
		private static Vector2[] Outline(Vector3 position, Vector3 forward, Bounds body) {
			if (body.size.x <= 0f || body.size.z <= 0f) {
				body = UsualBody;
			}
			var f = new Vector2(forward.x, forward.z);
			f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector2.up;
			var r = new Vector2(f.y, -f.x);
			var p = new Vector2(position.x, position.z);
			Vector3 lo = body.min, hi = body.max;
			return new[] { p + r * lo.x + f * lo.z, p + r * hi.x + f * lo.z, p + r * hi.x + f * hi.z, p + r * lo.x + f * hi.z };
		}

		/// <summary>True when one of <paramref name="a"/>'s edges has all of <paramref name="b"/> on its far side (so they don't overlap).</summary>
		private static bool Apart(Vector2[] a, Vector2[] b) {
			for (int i = 0; i < 2; i++) {
				Vector2 axis = a[i + 1] - a[i];
				float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
				for (int k = 0; k < 4; k++) {
					float pa = Vector2.Dot(a[k], axis), pb = Vector2.Dot(b[k], axis);
					aMin = Mathf.Min(aMin, pa);
					aMax = Mathf.Max(aMax, pa);
					bMin = Mathf.Min(bMin, pb);
					bMax = Mathf.Max(bMax, pb);
				}
				if (bMin > aMax || bMax < aMin) {
					return true;
				}
			}
			return false;
		}

		private static float ToEdge(Vector2 point, Vector2 from, Vector2 to) {
			Vector2 edge = to - from;
			float t = Mathf.Clamp01(Vector2.Dot(point - from, edge) / Mathf.Max(1e-6f, edge.sqrMagnitude));
			return Vector2.Distance(point, from + edge * t);
		}

		/// <summary>The angle (degrees, 0 to 180) between where the nose points and where the car is going, on the level; 0 when nearly still.</summary>
		public static float DriftAngle(Vector3 forward, Vector3 velocity) {
			var f = new Vector3(forward.x, 0f, forward.z);
			var v = new Vector3(velocity.x, 0f, velocity.z);
			if (f.sqrMagnitude < 1e-6f || v.magnitude < MovingAtLeast) {
				return 0f;
			}
			return Vector3.Angle(f, v);
		}
	}
}
