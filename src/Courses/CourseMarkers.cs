using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace TRC.TuneLoader {
	/// <summary>
	/// The start and finish lines drawn in the world while a time attack runs or a path is recorded: a strip
	/// laid on the road across the line and a post at each end, green for the start and pink for the finish.
	/// They're real objects without colliders, so the car hides them like anything else and they stay put
	/// when the camera moves. They only show within <see cref="Range"/> metres of the camera.
	///
	/// A map course whose start or finish object is a visible sign (it has a renderer) gets no marker there:
	/// the sign is the line.
	/// </summary>
	public sealed class CourseMarkers {
		public const float Range = 100f;

		/// <summary>CarX's road surfaces are on this layer (CarPlacementModel puts cars on it).</summary>
		private const int RoadLayerMask = 1 << 11;
		private const int StripSegments = 12;
		private const float StripDepth = 0.6f;
		private const float StripLift = 0.04f;
		private const float PostHeight = 2.2f;
		private const float PostSize = 0.18f;

		private static readonly Color StartColor = new Color(0.55f, 0.85f, 0.35f, 1f);
		private static readonly Color FinishColor = new Color(0.83f, 0.33f, 0.49f, 1f);

		private static Material startMaterial_;
		private static Material finishMaterial_;
		private static bool materialsTried_;

		private GameObject start_;
		private GameObject finish_;
		private CourseGate startGate_;
		private CourseGate finishGate_;

		/// <summary>False when no shader to draw with was found; the mod then draws the lines over the screen instead.</summary>
		public static bool Available => MakeMaterials();

		/// <summary>Which shader the markers use, for the log.</summary>
		public static string ShaderName { get; private set; } = "";

		/// <summary>Shows these lines (and removes any others). Gates that are null, or that a sign already shows, get no marker.</summary>
		public void Show(CourseGate start, CourseGate finish, bool startHasSign, bool finishHasSign) {
			CourseGate wantStart = startHasSign ? null : start;
			CourseGate wantFinish = finishHasSign ? null : finish;
			if (wantStart == startGate_ && wantFinish == finishGate_ && (startGate_ == null || start_ != null) && (finishGate_ == null || finish_ != null)) {
				return;
			}
			Clear();
			if (!MakeMaterials()) {
				return;
			}
			startGate_ = wantStart;
			finishGate_ = wantFinish;
			if (startGate_ != null) {
				start_ = BuildGate(startGate_, "TRC start line", startMaterial_);
			}
			if (finishGate_ != null) {
				finish_ = BuildGate(finishGate_, "TRC finish line", finishMaterial_);
			}
		}

		public void Clear() {
			if (start_ != null) {
				Object.Destroy(start_);
			}
			if (finish_ != null) {
				Object.Destroy(finish_);
			}
			start_ = null;
			finish_ = null;
			startGate_ = null;
			finishGate_ = null;
		}

		/// <summary>Called every frame: shows each marker only within <see cref="Range"/> of the camera.</summary>
		public void UpdateVisibility(Vector3 camera) {
			SetVisible(start_, startGate_, camera);
			SetVisible(finish_, finishGate_, camera);
		}

		private static void SetVisible(GameObject marker, CourseGate gate, Vector3 camera) {
			if (marker == null || gate == null) {
				return;
			}
			bool near = (gate.Center - camera).sqrMagnitude <= Range * Range;
			if (marker.activeSelf != near) {
				marker.SetActive(near);
			}
		}

		private static GameObject BuildGate(CourseGate gate, string name, Material material) {
			var root = new GameObject(name) { hideFlags = HideFlags.DontSave };
			root.SetActive(false);

			// The strip: points across the line, each put on the road below it, so it follows a cambered road.
			var vertices = new List<Vector3>();
			var triangles = new List<int>();
			Vector3 half = gate.Forward * (StripDepth / 2f);
			for (int i = 0; i <= StripSegments; i++) {
				Vector3 across = Vector3.Lerp(gate.LeftEnd, gate.RightEnd, i / (float)StripSegments);
				Vector3 ground = Ground(across) + Vector3.up * StripLift;
				vertices.Add(ground - half);
				vertices.Add(ground + half);
				if (i > 0) {
					int a = vertices.Count - 4, b = a + 1, c = a + 2, d = a + 3;
					// Both sides, so it shows whichever way round the triangles face.
					triangles.AddRange(new[] { a, b, c, c, b, d, a, c, b, c, d, b });
				}
			}
			var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateBounds();
			mesh.RecalculateNormals();
			AddRenderer(new GameObject("strip"), root, mesh, material);

			// A post at each end of the line.
			foreach (Vector3 end in new[] { gate.LeftEnd, gate.RightEnd }) {
				GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
				Object.DestroyImmediate(post.GetComponent<Collider>());
				post.name = "post";
				post.hideFlags = HideFlags.DontSave;
				post.transform.SetParent(root.transform, false);
				post.transform.position = Ground(end) + Vector3.up * (PostHeight / 2f);
				post.transform.rotation = Quaternion.LookRotation(gate.Forward, Vector3.up);
				post.transform.localScale = new Vector3(PostSize, PostHeight, PostSize);
				SetUp(post.GetComponent<MeshRenderer>(), material);
			}
			return root;
		}

		private static void AddRenderer(GameObject child, GameObject root, Mesh mesh, Material material) {
			child.hideFlags = HideFlags.DontSave;
			child.transform.SetParent(root.transform, false);
			child.AddComponent<MeshFilter>().sharedMesh = mesh;
			SetUp(child.AddComponent<MeshRenderer>(), material);
		}

		private static void SetUp(MeshRenderer renderer, Material material) {
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}

		/// <summary>The road surface under a point (CarX's road layer first, then anything), or the point itself.</summary>
		private static Vector3 Ground(Vector3 point) {
			var from = point + Vector3.up * 4f;
			if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 10f, RoadLayerMask, QueryTriggerInteraction.Ignore) ||
				Physics.Raycast(from, Vector3.down, out hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) {
				return hit.point;
			}
			return point;
		}

		/// <summary>
		/// Materials for the two colours. CarX renders with HDRP, so the best start is one of the game's own HDRP
		/// Unlit materials (already set up for the renderer), copied and recoloured; otherwise a new one from a shader.
		/// </summary>
		private static bool MakeMaterials() {
			if (materialsTried_) {
				return startMaterial_ != null;
			}
			materialsTried_ = true;

			Material template = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "HDRP/Unlit");
			Shader shader = template != null ? null : new[] { "HDRP/Unlit", "Unlit/Color", "Sprites/Default", "HDRP/Lit" }.Select(Shader.Find).FirstOrDefault(s => s != null);
			if (template == null && shader == null) {
				return false;
			}

			startMaterial_ = Make(template, shader, StartColor);
			finishMaterial_ = Make(template, shader, FinishColor);
			ShaderName = template != null ? "HDRP/Unlit (copied from the game)" : shader.name;
			return true;
		}

		private static Material Make(Material template, Shader shader, Color color) {
			Material material = template != null ? new Material(template) : new Material(shader);
			material.hideFlags = HideFlags.DontSave;
			material.name = "TRC course marker";
			foreach (string property in new[] { "_UnlitColor", "_BaseColor", "_Color" }) {
				if (material.HasProperty(property)) {
					material.SetColor(property, color);
				}
			}
			foreach (string texture in new[] { "_UnlitColorMap", "_BaseColorMap", "_MainTex" }) {
				if (material.HasProperty(texture)) {
					material.SetTexture(texture, null);
				}
			}
			return material;
		}
	}
}
