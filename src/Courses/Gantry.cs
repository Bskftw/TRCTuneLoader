using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace TRC.TuneLoader {
	/// <summary>What a gantry's sign shows. Compared with the last one, so the sign is only painted again when it changes.</summary>
	public struct GantrySign : IEquatable<GantrySign> {
		public enum Page : byte { Start, Lap, Finish, Clear, Checkpoint }

		public Page Kind;

		/// <summary>Start: <see cref="SignPages.Idle"/>, 3, 2, 1 or <see cref="SignPages.Go"/>. Lap: the lap.</summary>
		public int Step;
		public int Laps;

		/// <summary>Finish: the course's name. Lap: the last lap's time. Clear: the time.</summary>
		public string Text;
		public string TopSpeed;
		public string Rank;
		public bool PersonalBest;

		/// <summary>Checkpoint: the line under the gap (AHEAD OF PB...), and whether it's behind (amber).</summary>
		public string Under;
		public bool Behind;

		public bool Equals(GantrySign o) => Kind == o.Kind && Step == o.Step && Laps == o.Laps && Text == o.Text && TopSpeed == o.TopSpeed && Rank == o.Rank &&
			PersonalBest == o.PersonalBest && Under == o.Under && Behind == o.Behind;
		public override bool Equals(object obj) => obj is GantrySign o && Equals(o);
		public override int GetHashCode() => (int)Kind * 31 + Step;
	}

	/// <summary>
	/// A start or finish gantry (bskftw, Nov 2: "the one we use is very basic"): a tower at each edge of the road
	/// (a plinth, a pillar with neon edges and three chevrons pointing up, a diamond light on top) and a truss beam
	/// across with a neon strip. Real objects without colliders, like the old lines.
	///
	/// The sign (painted by <see cref="SignPages"/>) is two panels on the beam, one facing each way. Until Nov 3 it was
	/// drawn over the screen so trees couldn't hide it, but it couldn't be seen from the far side and broke into moving
	/// squares; bskftw: "just fixed to the poles and not worry about rendering it in front of things".
	/// </summary>
	public sealed class Gantry {
		private const float TowerHeight = 5.6f;
		private const float TowerWidth = 0.55f;
		private const float TrussDepth = 0.9f;
		private const float BarSize = 0.2f;
		private const float NeonSize = 0.07f;

		private static GlyphAtlas atlas_;
		private static bool atlasTried_;

		public GameObject Root { get; }
		public CourseGate Gate { get; }

		/// <summary>Where the sign hangs (the middle of the beam) and how wide it is there, in metres.</summary>
		public Vector3 SignCentre { get; }
		public float SignWidth { get; }

		/// <summary>The sign's top-left, top-right and bottom-left corners, as seen by a car coming towards it.</summary>
		public Vector3 SignTopLeft => SignCentre - Gate.Right * (SignWidth / 2f) + Vector3.up * (SignHeight / 2f);
		public Vector3 SignTopRight => SignCentre + Gate.Right * (SignWidth / 2f) + Vector3.up * (SignHeight / 2f);
		public Vector3 SignBottomLeft => SignCentre - Gate.Right * (SignWidth / 2f) - Vector3.up * (SignHeight / 2f);
		public float SignHeight => SignWidth * SignPages.Height / SignPages.Width;

		/// <summary>The sign's picture, on the two panels on the beam.</summary>
		public Texture2D Texture => texture_;

		private readonly SignPainter painter_;
		private readonly Texture2D texture_;
		private readonly Material glowMaterial_;
		private Material signMaterial_;
		private Texture2D bannerTexture_;
		private Material bannerMaterial_;
		private GantrySign shown_;
		private bool painted_;

		private Gantry(CourseGate gate, GameObject root, Texture2D texture, Material glowMaterial, Vector3 signCentre, float signWidth) {
			Gate = gate;
			Root = root;
			texture_ = texture;
			glowMaterial_ = glowMaterial;
			SignCentre = signCentre;
			SignWidth = signWidth;
			painter_ = new SignPainter(SignPages.Width, SignPages.Height, Letters);
		}

		/// <summary>The letters for the signs, from the mod's own pictures (null if they couldn't be read).</summary>
		public static GlyphAtlas Letters {
			get {
				if (!atlasTried_) {
					atlasTried_ = true;
					try {
						byte[] sharp = Alpha("gantry_font.png", out int width, out int height);
						byte[] glow = Alpha("gantry_glow.png", out _, out _);
						atlas_ = GlyphAtlas.Parse(ResourceText("gantry_font.txt"), sharp, glow, width, height);
					} catch (Exception e) {
						TrcLog.Warning($"Couldn't load the gantry signs' letters: {e.Message}");
					}
				}
				return atlas_;
			}
		}

		/// <summary>
		/// Builds a gantry across <paramref name="gate"/>, its towers <paramref name="span"/> metres apart, in the given
		/// neon colour. Null when the letters or a material couldn't be made (the mod then draws the plain line).
		/// </summary>
		public static Gantry Build(CourseGate gate, float span, string name, Color neon, Material structure, Func<Color, Texture, Material> makeMaterial, string banner = null) {
			if (Letters == null || structure == null) {
				return null;
			}
			Material glow = makeMaterial(neon, null);
			if (glow == null) {
				return null;
			}
			var texture = new Texture2D(SignPages.Width, SignPages.Height, TextureFormat.RGBA32, true) {
				name = name, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Trilinear, anisoLevel = 4, wrapMode = TextureWrapMode.Clamp,
			};

			var root = new GameObject(name) { hideFlags = HideFlags.DontSave };
			root.SetActive(false);
			Quaternion facing = Quaternion.LookRotation(gate.Forward, Vector3.up);
			Vector3 right = gate.Right;
			Vector3 back = -gate.Forward;
			float half = span / 2f;
			// The beam's height comes from the road, and each tower runs down from it to whatever is under it: shorter on
			// a bank, longer over a drop (bskftw, Nov 3: on a hillside one tower stood on the cliff top and lifted it all).
			float roadY = CourseMarkers.Ground(gate.Center).y;
			float topY = roadY + TowerHeight;
			var feet = new Vector3[2];

			for (int s = 0; s < 2; s++) {
				float side = s == 0 ? -1f : 1f;
				Vector3 foot = GroundUnder(gate.Center + right * side * half, topY - 0.6f, roadY);
				feet[s] = foot;
				float height = topY - foot.y;
				Vector3 inner = -right * side;
				// A plinth, the pillar, and neon down its two inner edges.
				Box(root, "plinth", foot + Vector3.up * 0.25f, facing, new Vector3(1.1f, 0.5f, 1.1f), structure);
				Box(root, "plinth neon", foot + Vector3.up * 0.52f, facing, new Vector3(1.12f, NeonSize, 1.12f), glow);
				Box(root, "tower", foot + Vector3.up * (height / 2f), facing, new Vector3(TowerWidth, height, TowerWidth), structure);
				foreach (float edge in new[] { -1f, 1f }) {
					Box(root, "edge neon", foot + Vector3.up * (height / 2f + 0.25f) + inner * (TowerWidth / 2f) + gate.Forward * edge * (TowerWidth / 2f),
						facing, new Vector3(NeonSize, Mathf.Max(0.2f, height - 0.5f), NeonSize), glow);
				}
				// Three chevrons pointing up on the face the cars come towards, as many as fit (a checkpoint has its banner there instead).
				for (int i = 0; i < 3 && banner == null && 1.6f + i * 0.75f + 0.4f < height - 1f; i++) {
					Vector3 at = foot + Vector3.up * (1.6f + i * 0.75f) + back * (TowerWidth / 2f + 0.02f);
					foreach (float arm in new[] { -1f, 1f }) {
						Box(root, "chevron", at + right * arm * 0.12f, facing * Quaternion.Euler(0f, 0f, arm * 40f), new Vector3(0.34f, 0.08f, 0.05f), glow);
					}
				}
				// A diamond light on top.
				Box(root, "light", new Vector3(foot.x, topY + 0.3f, foot.z), facing * Quaternion.Euler(0f, 0f, 45f), new Vector3(0.38f, 0.38f, 0.38f), glow);
			}

			// The truss: a top and a bottom bar, struts and braces between them, level at the higher tower's top.
			float topBar = topY - BarSize / 2f, bottomBar = topY - TrussDepth;
			Vector3 Across(float along, float y) => new Vector3(gate.Center.x, y, gate.Center.z) + right * along;
			Box(root, "top bar", Across(0f, topBar), facing, new Vector3(span, BarSize, BarSize), structure);
			Box(root, "bottom bar", Across(0f, bottomBar), facing, new Vector3(span, BarSize, BarSize), structure);
			Box(root, "beam neon", Across(0f, bottomBar - BarSize / 2f - NeonSize / 2f), facing, new Vector3(span - TowerWidth, NeonSize, NeonSize), glow);
			int bays = Math.Max(2, Mathf.RoundToInt(span / 1.6f));
			float bay = span / bays, rise = topBar - bottomBar;
			float braceLength = Mathf.Sqrt(bay * bay + rise * rise);
			float braceAngle = Mathf.Atan2(rise, bay) * Mathf.Rad2Deg;
			for (int i = 0; i < bays; i++) {
				float left = -half + i * bay;
				if (i > 0) {
					Box(root, "strut", Across(left, (topBar + bottomBar) / 2f), facing, new Vector3(0.1f, rise, 0.1f), structure);
				}
				float tilt = i % 2 == 0 ? braceAngle : -braceAngle;
				Box(root, "brace", Across(left + bay / 2f, (topBar + bottomBar) / 2f), facing * Quaternion.Euler(0f, 0f, tilt), new Vector3(braceLength, 0.08f, 0.08f), structure);
			}

			float signWidth = Mathf.Clamp(span * 0.55f, 3f, 6.5f);
			var gantry = new Gantry(gate, root, texture, glow, Across(0f, (topBar + bottomBar) / 2f), signWidth);
			gantry.AddSignPanels(makeMaterial);
			if (banner != null) {
				gantry.AddBanners(banner, feet, topY, back, facing, makeMaterial);
			}
			return gantry;
		}

		/// <summary>
		/// The ground under a tower: straight down from <paramref name="fromY"/> (just under the beam), the first surface
		/// facing up enough to stand on, up to 40 m below the road. Nothing found: a little under the road.
		/// </summary>
		private static Vector3 GroundUnder(Vector3 at, float fromY, float roadY) {
			var from = new Vector3(at.x, fromY, at.z);
			RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, fromY - roadY + 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			float best = float.MaxValue;
			Vector3 ground = new Vector3(at.x, roadY - 0.5f, at.z);
			foreach (RaycastHit hit in hits) {
				if (hit.normal.y > 0.5f && hit.distance < best) {
					best = hit.distance;
					ground = hit.point;
				}
			}
			// Never right up against the beam: at least a short tower on a bank as high as the road's gantry.
			ground.y = Mathf.Min(ground.y, fromY - 0.6f);
			return ground;
		}

		/// <summary>
		/// The sign as two panels hung on the beam, one facing each way, so it reads from both sides (bskftw, Nov 3: drawn
		/// over the screen it couldn't be seen from the far side, and broke into squares that moved with the camera).
		/// Part of the model: fixed to the gantry, and trees can hide it like the towers.
		/// </summary>
		private void AddSignPanels(Func<Color, Texture, Material> makeMaterial) {
			signMaterial_ = makeMaterial(Color.white, texture_);
			if (signMaterial_ == null) {
				return;
			}
			float clear = BarSize / 2f + 0.04f;
			foreach (bool front in new[] { true, false }) {
				// A quad shows its face to whoever looks along its blue arrow: the front panel to cars coming towards the
				// gantry, the back one (turned round, so it isn't mirrored) to cars coming the other way.
				Vector3 forward = front ? Gate.Forward : -Gate.Forward;
				GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
				UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
				quad.name = front ? "sign" : "sign back";
				quad.hideFlags = HideFlags.DontSave;
				quad.transform.SetParent(Root.transform, false);
				quad.transform.position = SignCentre - forward * clear;
				quad.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
				quad.transform.localScale = new Vector3(SignWidth, SignHeight, 1f);
				SetUp(quad.GetComponent<MeshRenderer>(), signMaterial_);
			}
		}

		/// <summary>
		/// A checkpoint's upright banners ("SECTOR 2", reading bottom to top) on the face of each tower the cars come
		/// towards. Part of the model, so trees can hide them like the towers.
		/// </summary>
		private void AddBanners(string text, Vector3[] feet, float topY, Vector3 back, Quaternion facing, Func<Color, Texture, Material> makeMaterial) {
			var painter = new SignPainter(SignPages.Width, SignPages.Height, Letters);
			SignPages.Banner(painter, text);
			bannerTexture_ = new Texture2D(SignPages.Height, SignPages.Width, TextureFormat.RGBA32, true) {
				name = text, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Trilinear, anisoLevel = 4, wrapMode = TextureWrapMode.Clamp,
			};
			bannerTexture_.SetPixels32(painter.UprightBottomUp());
			bannerTexture_.Apply(true);
			bannerMaterial_ = makeMaterial(Color.white, bannerTexture_);
			if (bannerMaterial_ == null) {
				return;
			}
			foreach (Vector3 foot in feet) {
				// 2 m tall, standing 1.75 m up the tower; shorter on a short tower (a bank), kept clear of the beam.
				float height = topY - foot.y;
				float tall = Mathf.Clamp(height - 1.6f, 0.6f, 2f);
				float middle = Mathf.Min(foot.y + 2.75f, topY - 1.2f - tall / 2f);
				GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
				UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
				quad.name = "banner";
				quad.hideFlags = HideFlags.DontSave;
				quad.transform.SetParent(Root.transform, false);
				quad.transform.position = new Vector3(foot.x, middle, foot.z) + back * (TowerWidth / 2f + 0.03f);
				quad.transform.rotation = facing;
				quad.transform.localScale = new Vector3(tall * SignPages.Height / SignPages.Width, tall, 1f);
				SetUp(quad.GetComponent<MeshRenderer>(), bannerMaterial_);
			}
		}

		/// <summary>Shows this on the sign, painting it again only when it changed.</summary>
		public void Show(GantrySign sign) {
			if (painted_ && sign.Equals(shown_)) {
				return;
			}
			painted_ = true;
			shown_ = sign;
			switch (sign.Kind) {
				case GantrySign.Page.Start:
					SignPages.Start(painter_, sign.Step);
					break;
				case GantrySign.Page.Lap:
					SignPages.Lap(painter_, sign.Step, sign.Laps, sign.Text ?? "");
					break;
				case GantrySign.Page.Finish:
					SignPages.Finish(painter_, sign.Text ?? "");
					break;
				case GantrySign.Page.Checkpoint:
					SignPages.Checkpoint(painter_, sign.Step, sign.Text ?? "", sign.Under ?? "", sign.Behind);
					break;
				default:
					SignPages.Clear(painter_, sign.Text ?? "", sign.TopSpeed ?? "", sign.Rank ?? "", sign.PersonalBest);
					break;
			}
			texture_.SetPixels32(painter_.BottomUp());
			texture_.Apply(true);
		}

		public void Destroy() {
			UnityEngine.Object.Destroy(Root);
			UnityEngine.Object.Destroy(texture_);
			UnityEngine.Object.Destroy(glowMaterial_);
			if (signMaterial_ != null) {
				UnityEngine.Object.Destroy(signMaterial_);
			}
			if (bannerTexture_ != null) {
				UnityEngine.Object.Destroy(bannerTexture_);
			}
			if (bannerMaterial_ != null) {
				UnityEngine.Object.Destroy(bannerMaterial_);
			}
		}

		private static void Box(GameObject root, string name, Vector3 at, Quaternion rotation, Vector3 size, Material material) {
			GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
			UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
			box.name = name;
			box.hideFlags = HideFlags.DontSave;
			box.transform.SetParent(root.transform, false);
			box.transform.position = at;
			box.transform.rotation = rotation;
			box.transform.localScale = size;
			SetUp(box.GetComponent<MeshRenderer>(), material);
		}

		private static void SetUp(MeshRenderer renderer, Material material) {
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}

		private static Stream Resource(string file) =>
			Assembly.GetExecutingAssembly().GetManifestResourceStream("TRC.TuneLoader.Resources." + file) ?? throw new FileNotFoundException(file);

		private static string ResourceText(string file) {
			using (var reader = new StreamReader(Resource(file))) {
				return reader.ReadToEnd();
			}
		}

		/// <summary>A picture's alpha, top row first.</summary>
		private static byte[] Alpha(string file, out int width, out int height) {
			byte[] bytes;
			using (Stream stream = Resource(file))
			using (var memory = new MemoryStream()) {
				stream.CopyTo(memory);
				bytes = memory.ToArray();
			}
			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			try {
				if (!ImageConversion.LoadImage(texture, bytes, false)) {
					throw new InvalidDataException(file);
				}
				width = texture.width;
				height = texture.height;
				Color32[] pixels = texture.GetPixels32();
				var alpha = new byte[width * height];
				for (int y = 0; y < height; y++) {
					for (int x = 0; x < width; x++) {
						alpha[y * width + x] = pixels[(height - 1 - y) * width + x].a;
					}
				}
				return alpha;
			} finally {
				UnityEngine.Object.Destroy(texture);
			}
		}
	}
}
