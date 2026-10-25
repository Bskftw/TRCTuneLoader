using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>Another car near this one: who, where, the way it points and how fast (km/h), as the room's messages say.</summary>
	public struct NearbyCar {
		public ulong Id;
		public string Name;
		public Vector3 Position;
		public Vector3 Heading;
		public float Kmh;
	}

	/// <summary>
	/// Notices incidents from this car alone: a sudden loss of speed (much harder than braking) is a hit, "possible
	/// contact" with whoever was right next to it, or "hit something" with nobody near (a wall, a barrier). No blame:
	/// like iRacing, everyone near a contact gets one. A few seconds go by before the next counts.
	/// </summary>
	public sealed class ContactWatcher {
		/// <summary>Losing this much speed (km/h) within <see cref="WindowSeconds"/> is a hit: a hard stop on grip tyres loses ~4 in that time.</summary>
		public const float DropKmh = 15f;
		private const double WindowSeconds = 0.3;
		private const float NearMetres = 6f;
		private const double AgainSeconds = 3;

		private readonly Queue<(double At, float Kmh)> recent_ = new Queue<(double, float)>();
		private double lastIncident_ = double.MinValue;

		public void Reset() {
			recent_.Clear();
		}

		/// <summary>Called every frame; returns the incident, if one just happened.</summary>
		public string Tick(double now, Vector3 position, float kmh, IEnumerable<NearbyCar> others) {
			kmh = Mathf.Abs(kmh);
			recent_.Enqueue((now, kmh));
			while (recent_.Count > 0 && now - recent_.Peek().At > WindowSeconds) {
				recent_.Dequeue();
			}
			float highest = recent_.Max(r => r.Kmh);
			if (highest - kmh < DropKmh || now - lastIncident_ < AgainSeconds) {
				return null;
			}
			lastIncident_ = now;
			recent_.Clear();
			NearbyCar? near = others?.Where(o => Vector3.Distance(o.Position, position) <= NearMetres)
				.OrderBy(o => Vector3.Distance(o.Position, position)).Cast<NearbyCar?>().FirstOrDefault();
			return near.HasValue ? $"possible contact with {near.Value.Name}" : "hit something";
		}
	}

	/// <summary>
	/// Highway cut-up points, like Assetto Corsa's overtake servers: getting past another car closely and much faster
	/// scores, more the faster and closer, times a combo that builds while you keep passing. The combo is lost after a
	/// while without a pass, or on an incident.
	/// </summary>
	public sealed class CutUpScorer {
		public const float MinKmh = 80f;
		public const float FasterByKmh = 10f;
		public const float CloseMetres = 5f;
		public const double ComboSeconds = 6;
		public const int MaxCombo = 10;

		/// <summary>Points so far, the combo now (1 = none), and the last pass, for the flash.</summary>
		public float Points { get; private set; }
		public int Combo { get; private set; } = 1;
		public float LastPass { get; private set; }
		public string LastPassed { get; private set; } = "";

		private readonly Dictionary<ulong, float> ahead_ = new Dictionary<ulong, float>();
		private double lastPassAt_ = double.MinValue;

		/// <summary>An incident (a hit, a reset, off the road): the combo's gone.</summary>
		public void Break() {
			Combo = 1;
		}

		public void Reset() {
			Points = 0;
			Combo = 1;
			ahead_.Clear();
		}

		/// <summary>Called every frame. Returns the points scored by a pass this frame (0 for none).</summary>
		public float Tick(double now, Vector3 position, Vector3 forward, float kmh, IEnumerable<NearbyCar> others) {
			if (now - lastPassAt_ > ComboSeconds) {
				Combo = 1;
			}
			Vector3 along = CourseGeometry.Level(forward);
			float scored = 0f;
			var seen = new HashSet<ulong>();
			foreach (NearbyCar car in others ?? Enumerable.Empty<NearbyCar>()) {
				seen.Add(car.Id);
				Vector3 offset = car.Position - position;
				if (offset.sqrMagnitude > 60f * 60f || along == Vector3.zero) {
					ahead_.Remove(car.Id);
					continue;
				}
				float ahead = Vector3.Dot(offset, along);
				float side = Mathf.Abs(Vector3.Dot(offset, new Vector3(along.z, 0f, -along.x)));
				if (ahead_.TryGetValue(car.Id, out float before) && before > 0f && ahead <= 0f &&
					side <= CloseMetres && kmh >= MinKmh && kmh >= car.Kmh + FasterByKmh) {
					// Faster and closer is worth more: 100 at 100 km/h right next to them, times the combo.
					float pass = 100f * (kmh / 100f) * (kmh / 100f) * (1.5f - side / CloseMetres);
					scored += pass * Combo;
					LastPass = pass * Combo;
					LastPassed = car.Name;
					Combo = Mathf.Min(MaxCombo, Combo + 1);
					lastPassAt_ = now;
				}
				ahead_[car.Id] = ahead;
			}
			foreach (ulong gone in ahead_.Keys.Where(id => !seen.Contains(id)).ToList()) {
				ahead_.Remove(gone);
			}
			Points += scored;
			return scored;
		}
	}

	/// <summary>Race flags for one driver, worked out from where everyone is.</summary>
	public static class RaceFlags {
		/// <summary>A slow or stopped car (or one that's out) this far ahead along the course shows a yellow flag.</summary>
		public const float YellowMetres = 250f;
		public const float SlowKmh = 25f;

		/// <summary>A car a lap or more ahead closing in from this close behind shows a blue flag (let it through).</summary>
		public const float BlueMetres = 80f;

		/// <param name="mine">This driver's metres along the race (all laps).</param>
		/// <param name="lapLength">The course's length on a circuit (0 on point to point: no blue flags).</param>
		public static (string Yellow, string Blue) For(float mine, float lapLength, IEnumerable<(string Name, float Progress, float Kmh, bool Out)> others) {
			string yellow = "", blue = "";
			foreach (var o in others) {
				float gap = o.Progress - mine;
				if (lapLength > 0f) {
					// On a circuit, "ahead on the road" is ahead on this lap, whatever lap they're on.
					float road = ((gap % lapLength) + lapLength) % lapLength;
					if (yellow.Length == 0 && road > 1f && road <= YellowMetres && (o.Out || o.Kmh < SlowKmh)) {
						yellow = $"Yellow flag: slow car ahead ({o.Name}, {road:0} m)";
					}
					float behind = lapLength - road;
					if (blue.Length == 0 && gap >= lapLength * 0.5f && behind > 1f && behind <= BlueMetres && !o.Out) {
						blue = $"Blue flag: {o.Name} is a lap ahead, let them through";
					}
				} else if (yellow.Length == 0 && gap > 1f && gap <= YellowMetres && (o.Out || o.Kmh < SlowKmh)) {
					yellow = $"Yellow flag: slow car ahead ({o.Name}, {gap:0} m)";
				}
			}
			return (yellow, blue);
		}
	}
}
