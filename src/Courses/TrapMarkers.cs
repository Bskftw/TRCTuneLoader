// Speed traps drawn on the road while one is placed or edited.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace TRC.TuneLoader {
	/// <summary>Speed traps drawn on the road (an amber strip and posts, like the start and finish lines), only near the camera.</summary>
	public sealed class TrapMarkers {
		private const float Range = 100f;
		private readonly List<(CourseGate Gate, GameObject Marker)> shown_ = new List<(CourseGate, GameObject)>();
		private List<CourseGate> for_;

		/// <summary>Called every frame with the traps to show (null for none) and where the camera is.</summary>
		public void Show(List<CourseGate> gates, Vector3 camera) {
			if (gates == null || gates.Count == 0 || !CourseMarkers.Available) {
				Clear();
				return;
			}
			if (for_ == null || for_.Count != gates.Count || !for_.SequenceEqual(gates)) {
				Clear();
				for_ = new List<CourseGate>(gates);
				foreach (CourseGate gate in gates) {
					shown_.Add((gate, null));
				}
			}
			for (int i = 0; i < shown_.Count; i++) {
				var (gate, marker) = shown_[i];
				bool near = (gate.Center - camera).sqrMagnitude <= Range * Range;
				if (near && marker == null) {
					// Built when first near, so the road under it has loaded.
					marker = CourseMarkers.BuildGate(gate, "TRC speed trap", CourseMarkers.TrapMaterial);
					shown_[i] = (gate, marker);
				}
				if (marker != null && marker.activeSelf != near) {
					marker.SetActive(near);
				}
			}
		}

		public void Clear() {
			foreach (var (_, marker) in shown_) {
				if (marker != null) {
					Object.Destroy(marker);
				}
			}
			shown_.Clear();
			for_ = null;
		}
	}
}
