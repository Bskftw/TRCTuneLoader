// The best line drawn on the road, coloured by speed.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace TRC.TuneLoader {
	/// <summary>
	/// The best line: chevrons on the road where the car drove on its best run or lap, in one colour or coloured by
	/// speed (red where it was slowest on the line, through amber and green to cyan where it was fastest).
	/// </summary>
	public sealed class SpeedLine {
		private static readonly Color[] Ramp = {
			new Color(0.9f, 0.25f, 0.25f), new Color(0.95f, 0.55f, 0.2f), new Color(0.95f, 0.85f, 0.3f),
			new Color(0.55f, 0.85f, 0.35f), new Color(0.3f, 0.85f, 0.6f), new Color(0.35f, 0.8f, 0.95f),
		};
		private static Material[] materials_;

		private readonly ChevronLine line_ = new ChevronLine("TRC best line");
		private Material[] plain_;
		private IList<float> kmh_;
		private float min_;
		private float max_;

		/// <summary>Called every frame: the line (null for none), its speeds, whether to colour by speed, and where the camera is.</summary>
		public void Show(IList<Vector3> line, IList<float> kmh, bool coloured, Vector3 camera) {
			if (line == null || !CourseMarkers.Available || !MakeMaterials()) {
				Clear();
				return;
			}
			if (!ReferenceEquals(kmh, kmh_)) {
				kmh_ = kmh;
				min_ = kmh.Count > 0 ? kmh.Min() : 0f;
				max_ = kmh.Count > 0 ? kmh.Max() : 1f;
			}
			if (coloured) {
				line_.Show(line, camera, materials_, i => {
					float share = max_ - min_ > 1f && i < kmh_.Count ? (kmh_[i] - min_) / (max_ - min_) : 1f;
					return Mathf.Clamp(Mathf.FloorToInt(share * Ramp.Length), 0, Ramp.Length - 1);
				});
			} else {
				if (plain_ == null || plain_[0] != CourseMarkers.DotMaterial) {
					plain_ = new[] { CourseMarkers.DotMaterial };
				}
				line_.Show(line, camera, plain_, i => 0);
			}
		}

		public void Clear() {
			line_.Clear();
			kmh_ = null;
		}

		private static bool MakeMaterials() {
			if (materials_ != null) {
				return true;
			}
			if (CourseMarkers.DotMaterial == null) {
				return false;
			}
			materials_ = Ramp.Select(c => {
				var material = new Material(CourseMarkers.DotMaterial) { hideFlags = HideFlags.DontSave, name = "TRC best line" };
				foreach (string property in new[] { "_UnlitColor", "_BaseColor", "_Color" }) {
					if (material.HasProperty(property)) {
						material.SetColor(property, c);
					}
				}
				return material;
			}).ToArray();
			return true;
		}
	}
}
