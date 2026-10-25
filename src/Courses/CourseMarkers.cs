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
		private static readonly Color DotColor = new Color(0.55f, 0.8f, 1f, 1f);
		private static readonly Color TrapColor = new Color(0.98f, 0.78f, 0.25f, 1f);

		/// <summary>For <see cref="TrapMarkers"/>: amber.</summary>
		internal static Material TrapMaterial { get; private set; }

		private static Material startMaterial_;
		private static Material finishMaterial_;

		/// <summary>For <see cref="PathDots"/>: light blue.</summary>
		internal static Material DotMaterial { get; private set; }
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

		internal static GameObject BuildGate(CourseGate gate, string name, Material material) {
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
		internal static Vector3 Ground(Vector3 point) {
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
			DotMaterial = Make(template, shader, DotColor);
			TrapMaterial = Make(template, shader, TrapColor);
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

	/// <summary>
	/// The best line: small squares on the road where the car drove on its best run or lap, near the camera only,
	/// in one colour or coloured by speed (red where it was slowest on the line, through amber and green to cyan
	/// where it was fastest).
	/// </summary>
	public sealed class SpeedLine {
		private const float Range = 70f;
		private const int Every = 2;
		private const float Size = 0.3f;
		private const float Lift = 0.06f;
		private static readonly Color[] Ramp = {
			new Color(0.9f, 0.25f, 0.25f), new Color(0.95f, 0.55f, 0.2f), new Color(0.95f, 0.85f, 0.3f),
			new Color(0.55f, 0.85f, 0.35f), new Color(0.3f, 0.85f, 0.6f), new Color(0.35f, 0.8f, 0.95f),
		};
		private static Material[] materials_;

		private readonly List<GameObject> pool_ = new List<GameObject>();
		private readonly Dictionary<int, Vector3> ground_ = new Dictionary<int, Vector3>();
		private IList<Vector3> shown_;
		private float min_;
		private float max_;

		/// <summary>Called every frame: the line (null for none), its speeds, whether to colour by speed, and where the camera is.</summary>
		public void Show(IList<Vector3> line, IList<float> kmh, bool coloured, Vector3 camera) {
			if (line == null || !CourseMarkers.Available || !MakeMaterials()) {
				Clear();
				return;
			}
			if (!ReferenceEquals(line, shown_)) {
				ground_.Clear();
				shown_ = line;
				min_ = kmh.Count > 0 ? kmh.Min() : 0f;
				max_ = kmh.Count > 0 ? kmh.Max() : 1f;
			}
			int used = 0;
			for (int i = 0; i < line.Count; i += Every) {
				if ((line[i] - camera).sqrMagnitude > Range * Range) {
					continue;
				}
				if (!ground_.TryGetValue(i, out Vector3 on)) {
					ground_[i] = on = CourseMarkers.Ground(line[i]) + Vector3.up * Lift;
				}
				GameObject dot = Dot(used++);
				dot.transform.position = on;
				float share = max_ - min_ > 1f && i < kmh.Count ? (kmh[i] - min_) / (max_ - min_) : 1f;
				Material material = coloured ? materials_[Mathf.Clamp(Mathf.FloorToInt(share * Ramp.Length), 0, Ramp.Length - 1)] : CourseMarkers.DotMaterial;
				MeshRenderer renderer = dot.GetComponent<MeshRenderer>();
				if (renderer.sharedMaterial != material) {
					renderer.sharedMaterial = material;
				}
				if (!dot.activeSelf) {
					dot.SetActive(true);
				}
			}
			for (int i = used; i < pool_.Count; i++) {
				if (pool_[i].activeSelf) {
					pool_[i].SetActive(false);
				}
			}
		}

		public void Clear() {
			foreach (GameObject dot in pool_) {
				if (dot != null) {
					Object.Destroy(dot);
				}
			}
			pool_.Clear();
			ground_.Clear();
			shown_ = null;
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

		private GameObject Dot(int index) {
			while (pool_.Count <= index) {
				GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
				Object.DestroyImmediate(dot.GetComponent<Collider>());
				dot.name = "TRC best line";
				dot.hideFlags = HideFlags.DontSave;
				dot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
				dot.transform.localScale = new Vector3(Size, Size, 1f);
				MeshRenderer renderer = dot.GetComponent<MeshRenderer>();
				renderer.shadowCastingMode = ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				pool_.Add(dot);
			}
			return pool_[index];
		}
	}

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

	/// <summary>
	/// A course's path shown as small light-blue squares on the road, every few metres, only near the camera:
	/// while the course builder or Record path records it (so you see what you drove), and for a course you look
	/// at. No colliders, like the lines.
	/// </summary>
	public sealed class PathDots {
		private const float Range = 60f;

		/// <summary>Every this many path points (about 2 m apart) gets a square.</summary>
		private const int Every = 2;
		private const float Size = 0.35f;
		private const float Lift = 0.05f;

		private readonly List<GameObject> pool_ = new List<GameObject>();
		private readonly Dictionary<int, Vector3> ground_ = new Dictionary<int, Vector3>();
		private IList<Vector3> shown_;

		/// <summary>Called every frame with the path to show (null for none) and where the camera is.</summary>
		public void Show(IList<Vector3> points, Vector3 camera) {
			if (points == null || !CourseMarkers.Available) {
				Clear();
				return;
			}
			if (!ReferenceEquals(points, shown_)) {
				ground_.Clear();
				shown_ = points;
			}
			int used = 0;
			for (int i = 0; i < points.Count; i += Every) {
				Vector3 point = points[i];
				if ((point - camera).sqrMagnitude > Range * Range) {
					continue;
				}
				if (!ground_.TryGetValue(i, out Vector3 on)) {
					ground_[i] = on = CourseMarkers.Ground(point) + Vector3.up * Lift;
				}
				GameObject dot = Dot(used++);
				dot.transform.position = on;
				if (!dot.activeSelf) {
					dot.SetActive(true);
				}
			}
			for (int i = used; i < pool_.Count; i++) {
				if (pool_[i].activeSelf) {
					pool_[i].SetActive(false);
				}
			}
		}

		public void Clear() {
			foreach (GameObject dot in pool_) {
				if (dot != null) {
					Object.Destroy(dot);
				}
			}
			pool_.Clear();
			ground_.Clear();
			shown_ = null;
		}

		private GameObject Dot(int index) {
			while (pool_.Count <= index) {
				GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
				Object.DestroyImmediate(dot.GetComponent<Collider>());
				dot.name = "TRC path dot";
				dot.hideFlags = HideFlags.DontSave;
				dot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
				dot.transform.localScale = new Vector3(Size, Size, 1f);
				MeshRenderer renderer = dot.GetComponent<MeshRenderer>();
				renderer.sharedMaterial = CourseMarkers.DotMaterial;
				renderer.shadowCastingMode = ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				pool_.Add(dot);
			}
			return pool_[index];
		}
	}
}
