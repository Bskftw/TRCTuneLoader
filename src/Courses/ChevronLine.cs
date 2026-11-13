// Lines of chevrons on the road pointing the way you drive, and a course's path shown with them (PathDots).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace TRC.TuneLoader {
	/// <summary>
	/// A line of chevrons on the road pointing the way you drive (bskftw, Nov 3: the flat squares were hard to see, and
	/// sank out of sight on slopes). Each lies along the road's slope a little above it, so it shows uphill too. All of
	/// them are one low-poly mesh (a submesh per colour), built again only when the camera has moved a few metres.
	/// </summary>
	public sealed class ChevronLine {
		private const float Range = 150f;
		private const int Every = 2;
		private const float Lift = 0.1f;
		private const float Rebuild = 6f;

		private readonly string name_;
		private GameObject object_;
		private Mesh mesh_;
		private MeshRenderer renderer_;
		private IList<Vector3> builtFor_;
		private Material[] builtWith_;
		private Vector3 builtAt_;
		private readonly Dictionary<int, Vector3> ground_ = new Dictionary<int, Vector3>();

		public ChevronLine(string name) {
			name_ = name;
		}

		/// <summary>
		/// Shows the points near the camera as chevrons, each in <paramref name="materials"/>[<paramref name="colourOf"/>(i)].
		/// </summary>
		public void Show(IList<Vector3> points, Vector3 camera, Material[] materials, System.Func<int, int> colourOf) {
			if (points == null || points.Count < 2) {
				Clear();
				return;
			}
			if (!ReferenceEquals(points, builtFor_)) {
				ground_.Clear();
			}
			if (ReferenceEquals(points, builtFor_) && ReferenceEquals(materials, builtWith_) && (camera - builtAt_).sqrMagnitude < Rebuild * Rebuild && object_ != null) {
				return;
			}
			builtFor_ = points;
			builtWith_ = materials;
			builtAt_ = camera;
			if (object_ == null) {
				object_ = new GameObject(name_) { hideFlags = HideFlags.DontSave };
				object_.AddComponent<MeshFilter>().sharedMesh = mesh_ = new Mesh { name = name_, hideFlags = HideFlags.DontSave };
				renderer_ = object_.AddComponent<MeshRenderer>();
				renderer_.shadowCastingMode = ShadowCastingMode.Off;
				renderer_.receiveShadows = false;
			}

			var vertices = new List<Vector3>();
			var triangles = new List<int>[materials.Length];
			for (int m = 0; m < materials.Length; m++) {
				triangles[m] = new List<int>();
			}
			for (int i = Every; i + Every < points.Count; i += Every) {
				if ((points[i] - camera).sqrMagnitude > Range * Range) {
					continue;
				}
				Vector3 at = GroundAt(points, i);
				Vector3 along = GroundAt(points, i + Every) - GroundAt(points, i - Every);
				Vector3 level = new Vector3(along.x, 0f, along.z);
				if (level.sqrMagnitude < 1e-4f) {
					continue;
				}
				Vector3 forward = along.normalized;
				Vector3 right = new Vector3(level.z, 0f, -level.x).normalized;
				Vector3 up = Vector3.Cross(forward, right);
				Vector3 centre = at + up * Lift;
				// A chevron: two arms meeting at a tip ahead, each a thin quad.
				Vector3 tip = centre + forward * 0.45f;
				int sub = Mathf.Clamp(colourOf(i), 0, materials.Length - 1);
				foreach (float side in new[] { -1f, 1f }) {
					Vector3 back = centre - forward * 0.35f + right * side * 0.55f;
					int a = vertices.Count;
					vertices.Add(back);
					vertices.Add(back - forward * 0.22f);
					vertices.Add(tip - forward * 0.22f);
					vertices.Add(tip);
					// Both faces, so it shows whichever way the triangles turn.
					triangles[sub].AddRange(new[] { a, a + 1, a + 2, a, a + 2, a + 3, a, a + 2, a + 1, a, a + 3, a + 2 });
				}
			}
			mesh_.Clear();
			mesh_.SetVertices(vertices);
			mesh_.subMeshCount = materials.Length;
			for (int m = 0; m < materials.Length; m++) {
				mesh_.SetTriangles(triangles[m], m);
			}
			mesh_.RecalculateBounds();
			renderer_.sharedMaterials = materials;
			if (!object_.activeSelf) {
				object_.SetActive(true);
			}
		}

		private Vector3 GroundAt(IList<Vector3> points, int i) {
			if (!ground_.TryGetValue(i, out Vector3 on)) {
				ground_[i] = on = CourseMarkers.Ground(points[i]);
			}
			return on;
		}

		public void Clear() {
			if (object_ != null) {
				Object.Destroy(object_);
				Object.Destroy(mesh_);
			}
			object_ = null;
			mesh_ = null;
			builtFor_ = null;
			builtWith_ = null;
			ground_.Clear();
		}
	}

	/// <summary>
	/// A course's path shown as light-blue chevrons on the road pointing the way you drive, near the camera only:
	/// while the course builder or Record path records it (so you see what you drove), and for a course you look at.
	/// No colliders, like the lines.
	/// </summary>
	public sealed class PathDots {
		private readonly ChevronLine line_ = new ChevronLine("TRC path");
		private Material[] materials_;

		/// <summary>Called every frame with the path to show (null for none) and where the camera is.</summary>
		public void Show(IList<Vector3> points, Vector3 camera) {
			if (points == null || !CourseMarkers.Available) {
				Clear();
				return;
			}
			if (materials_ == null || materials_[0] != CourseMarkers.DotMaterial) {
				materials_ = new[] { CourseMarkers.DotMaterial };
			}
			line_.Show(points, camera, materials_, i => 0);
		}

		public void Clear() => line_.Clear();
	}
}
