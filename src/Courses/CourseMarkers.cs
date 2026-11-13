// The start, finish and checkpoint lines drawn on the road while a run goes, and their signs.
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

		/// <summary>
		/// 0.16: for ghosts, see-through when the game has a see-through material to copy: one of its transparent HDRP
		/// Unlit or HDRP Lit materials (window glass and the like), whose shader is known to draw that way here. Else
		/// the solid pale blue copy of the course markers' material. (Nov 1: switching an opaque material to
		/// transparent by hand drew nothing at all in CarX, so that's not done.) <see cref="GhostLook"/> says which.
		/// Looked for again each time the map changes (<see cref="ForgetGhostMaterial"/>), since a map brings its own materials.
		/// </summary>
		internal static Material GhostMaterial {
			get {
				if (ghostMaterial_ == null && !ghostTried_) {
					ghostTried_ = true;
					List<Material> loaded = Resources.FindObjectsOfTypeAll<Material>().Where(m => m != null && m.shader != null).ToList();
					bool Transparent(Material m) => m.renderQueue >= 2900 && m.renderQueue <= 3500 &&
						(!m.HasProperty("_SurfaceType") || m.GetFloat("_SurfaceType") >= 0.5f);
					Material template = loaded.FirstOrDefault(m => m.shader.name == "HDRP/Unlit" && Transparent(m))
						?? loaded.FirstOrDefault(m => m.shader.name == "HDRP/Lit" && Transparent(m) && !m.IsKeywordEnabled("_REFRACTION_PLANE") && !m.IsKeywordEnabled("_REFRACTION_SPHERE"));
					if (template != null) {
						ghostMaterial_ = Make(template, null, GhostColor);
						foreach (string map in new[] { "_NormalMap", "_MaskMap", "_EmissiveColorMap", "_SpecularColorMap", "_DetailMap" }) {
							if (ghostMaterial_.HasProperty(map)) {
								ghostMaterial_.SetTexture(map, null);
							}
						}
						if (ghostMaterial_.HasProperty("_EmissiveColor")) {
							ghostMaterial_.SetColor("_EmissiveColor", new Color(0.15f, 0.25f, 0.35f));
						}
						GhostLook = $"see-through ({template.shader.name}, copied from the game's '{template.name}')";
					} else if (MakeMaterials()) {
						ghostMaterial_ = new Material(DotMaterial) { hideFlags = HideFlags.DontSave };
						GhostLook = "solid (no see-through material in the game to copy here)";
						var shaders = loaded.Where(m => m.renderQueue >= 2900).Select(m => m.shader.name).Distinct().Take(12);
						GhostLook += $"; see-through shaders loaded: {string.Join(", ", shaders)}";
					}
					if (ghostMaterial_ != null) {
						ghostMaterial_.name = "TRC ghost";
						SetGhostOpacity(ghostOpacity_);
					}
				}
				return ghostMaterial_;
			}
		}

		/// <summary>How the ghost is drawn, for the log.</summary>
		internal static string GhostLook { get; private set; } = "";

		/// <summary>A new map: look for a see-through material again next time.</summary>
		internal static void ForgetGhostMaterial() {
			if (ghostMaterial_ != null) {
				UnityEngine.Object.Destroy(ghostMaterial_);
			}
			ghostMaterial_ = null;
			ghostTried_ = false;
		}

		private static readonly Color GhostColor = new Color(0.55f, 0.8f, 1f, 0.25f);
		private static Material ghostMaterial_;
		private static bool ghostTried_;
		private static float ghostOpacity_ = 0.25f;

		/// <summary>How see-through ghosts are: 0 invisible, 1 solid.</summary>
		internal static void SetGhostOpacity(float opacity) {
			ghostOpacity_ = Mathf.Clamp01(opacity);
			if (ghostMaterial_ == null) {
				return;
			}
			var color = new Color(GhostColor.r, GhostColor.g, GhostColor.b, ghostOpacity_);
			foreach (string property in new[] { "_UnlitColor", "_BaseColor", "_Color" }) {
				if (ghostMaterial_.HasProperty(property)) {
					ghostMaterial_.SetColor(property, color);
				}
			}
		}
		private static bool materialsTried_;
		private static Material template_;
		private static Shader shader_;
		private static Material structureMaterial_;

		/// <summary>Nov 2: gantries with signs at the start and finish (Options); off: the plain lines.</summary>
		public static bool UseGantries = true;

		/// <summary>Gantries can be seen from further away than the lines.</summary>
		private const float GantryRange = 300f;

		private static readonly Color StartNeon = new Color(0.25f, 0.67f, 1f, 1f);
		private static readonly Color FinishNeon = new Color(0.24f, 0.94f, 0.59f, 1f);
		private static readonly Color StructureColor = new Color(0.07f, 0.08f, 0.11f, 1f);

		private GameObject start_;
		private GameObject finish_;
		private CourseGate startGate_;
		private CourseGate finishGate_;
		private Gantry startGantry_;
		private Gantry finishGantry_;
		private bool gantries_;

		/// <summary>Nov 2: a checkpoint gantry where each sector ends (not the last: that's the finish), and what they were built for.</summary>
		private readonly List<Gantry> checkpoints_ = new List<Gantry>();
		private Course checkpointsFor_;
		private float checkpointsSpan_;

		private static readonly Color CheckpointNeon = new Color(0.75f, 1f, 0.16f, 1f);

		/// <summary>False when no shader to draw with was found; the mod then draws the lines over the screen instead.</summary>
		public static bool Available => MakeMaterials();

		/// <summary>Which shader the markers use, for the log.</summary>
		public static string ShaderName { get; private set; } = "";

		/// <summary>
		/// Shows these lines (and removes any others). Gates that are null, or that a sign already shows, get no marker.
		/// With <see cref="UseGantries"/> (and a course running), each gets a gantry <paramref name="span"/> metres wide
		/// instead; a circuit, whose finish is its start line, gets one.
		/// </summary>
		public void Show(CourseGate start, CourseGate finish, bool startHasSign, bool finishHasSign, float span = 0f) {
			CourseGate wantStart = startHasSign ? null : start;
			CourseGate wantFinish = finishHasSign ? null : finish;
			bool gantries = UseGantries && span > 0f;
			if (gantries && wantStart != null && wantFinish != null && SameLine(wantStart, wantFinish)) {
				wantFinish = null;
			}
			bool built = (startGate_ == null || start_ != null || startGantry_ != null) && (finishGate_ == null || finish_ != null || finishGantry_ != null);
			if (wantStart == startGate_ && wantFinish == finishGate_ && gantries == gantries_ && built) {
				return;
			}
			Clear();
			if (!MakeMaterials()) {
				return;
			}
			startGate_ = wantStart;
			finishGate_ = wantFinish;
			gantries_ = gantries;
			if (startGate_ != null) {
				startGantry_ = gantries ? Gantry.Build(startGate_, span, "TRC start gantry", StartNeon, structureMaterial_, MarkerMaterial) : null;
				start_ = startGantry_ != null ? startGantry_.Root : BuildGate(startGate_, "TRC start line", startMaterial_);
			}
			if (finishGate_ != null) {
				finishGantry_ = gantries ? Gantry.Build(finishGate_, span, "TRC finish gantry", FinishNeon, structureMaterial_, MarkerMaterial) : null;
				finish_ = finishGantry_ != null ? finishGantry_.Root : BuildGate(finishGate_, "TRC finish line", finishMaterial_);
			}
		}

		/// <summary>The gantries shown right now (within range of the camera), for drawing their signs over the screen.</summary>
		public IEnumerable<Gantry> VisibleGantries {
			get {
				if (startGantry_ != null && startGantry_.Root.activeSelf) {
					yield return startGantry_;
				}
				if (finishGantry_ != null && finishGantry_.Root.activeSelf) {
					yield return finishGantry_;
				}
				foreach (Gantry checkpoint in checkpoints_) {
					if (checkpoint.Root.activeSelf) {
						yield return checkpoint;
					}
				}
			}
		}

		public bool HasVisibleGantries => (startGantry_ != null && startGantry_.Root.activeSelf) || (finishGantry_ != null && finishGantry_.Root.activeSelf) ||
			checkpoints_.Exists(c => c.Root.activeSelf);

		/// <summary>
		/// The checkpoint gantries of this course (null: none), one where each sector ends but the last, each across the
		/// road the way it goes there, as wide as <paramref name="widthAt"/> says the road is there (else
		/// <paramref name="span"/>). Built once per course.
		/// </summary>
		public void ShowCheckpoints(Course course, float span, System.Func<Vector3, Vector3, float> widthAt = null) {
			if (!UseGantries || course == null || !course.HasPath || course.SectorEnds.Length < 2) {
				course = null;
			}
			if (course == checkpointsFor_ && Mathf.Approximately(span, checkpointsSpan_)) {
				return;
			}
			ClearCheckpoints();
			checkpointsFor_ = course;
			checkpointsSpan_ = span;
			if (course == null || !MakeMaterials()) {
				return;
			}
			for (int i = 0; i < course.SectorEnds.Length - 1; i++) {
				course.PointAt(course.SectorEnds[i], out Vector3 at, out Vector3 forward);
				float width = widthAt?.Invoke(at, new Vector3(forward.z, 0f, -forward.x)) ?? 0f;
				float here = width > 2f ? width : span;
				if (!CourseGate.TryMake(at, forward, here, out CourseGate gate)) {
					continue;
				}
				Gantry checkpoint = Gantry.Build(gate, here, $"TRC checkpoint {i + 1}", CheckpointNeon, structureMaterial_, MarkerMaterial, $"SECTOR {i + 2}");
				if (checkpoint != null) {
					checkpoints_.Add(checkpoint);
				}
			}
		}

		/// <summary>What each checkpoint's sign shows, in order.</summary>
		public void ShowCheckpointSigns(System.Func<int, GantrySign> sign) {
			for (int i = 0; i < checkpoints_.Count; i++) {
				checkpoints_[i].Show(sign(i));
			}
		}

		private void ClearCheckpoints() {
			foreach (Gantry checkpoint in checkpoints_) {
				checkpoint.Destroy();
			}
			checkpoints_.Clear();
			checkpointsFor_ = null;
		}

		/// <summary>What the start and finish gantries' signs show (no gantries: nothing happens).</summary>
		public void ShowSigns(GantrySign start, GantrySign finish) {
			startGantry_?.Show(start);
			finishGantry_?.Show(finish);
		}

		private static bool SameLine(CourseGate a, CourseGate b) => a == b || (Vector3.Distance(a.Center, b.Center) < 1f && Vector3.Dot(a.Forward, b.Forward) > 0.9f);

		public void Clear() {
			if (startGantry_ != null) {
				startGantry_.Destroy();
			} else if (start_ != null) {
				Object.Destroy(start_);
			}
			if (finishGantry_ != null) {
				finishGantry_.Destroy();
			} else if (finish_ != null) {
				Object.Destroy(finish_);
			}
			startGantry_ = null;
			finishGantry_ = null;
			ClearCheckpoints();
			start_ = null;
			finish_ = null;
			startGate_ = null;
			finishGate_ = null;
		}

		/// <summary>Called every frame: shows each marker only within <see cref="Range"/> of the camera.</summary>
		public void UpdateVisibility(Vector3 camera) {
			SetVisible(start_, startGate_, camera, startGantry_ != null ? GantryRange : Range);
			SetVisible(finish_, finishGate_, camera, finishGantry_ != null ? GantryRange : Range);
			foreach (Gantry checkpoint in checkpoints_) {
				SetVisible(checkpoint.Root, checkpoint.Gate, camera, GantryRange);
			}
		}

		private static void SetVisible(GameObject marker, CourseGate gate, Vector3 camera, float range) {
			if (marker == null || gate == null) {
				return;
			}
			bool near = (gate.Center - camera).sqrMagnitude <= range * range;
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

			template_ = template;
			shader_ = shader;
			structureMaterial_ = Make(template, shader, StructureColor);
			startMaterial_ = Make(template, shader, StartColor);
			finishMaterial_ = Make(template, shader, FinishColor);
			DotMaterial = Make(template, shader, DotColor);
			TrapMaterial = Make(template, shader, TrapColor);
			ShaderName = template != null ? "HDRP/Unlit (copied from the game)" : shader.name;
			return true;
		}

		/// <summary>A new marker material in this colour, showing <paramref name="texture"/> when given (for the gantries' signs).</summary>
		internal static Material MarkerMaterial(Color color, Texture texture) {
			if (!MakeMaterials()) {
				return null;
			}
			Material material = Make(template_, shader_, color);
			if (texture != null) {
				foreach (string property in new[] { "_UnlitColorMap", "_BaseColorMap", "_MainTex" }) {
					if (material.HasProperty(property)) {
						material.SetTexture(property, texture);
					}
				}
			}
			return material;
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
