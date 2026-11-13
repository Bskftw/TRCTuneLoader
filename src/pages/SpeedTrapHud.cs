// Speed traps over the track: the flash when the car goes through one, and the floating SPEED CHECK signs (hidden behind hills).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>The sign floats this high (metres) over the middle of the trap, this wide, and shows from this far.</summary>
		private const float SignHeight = 5.5f;
		private const float SignWidth = 6f;
		private const float SignRange = 300f;

		/// <summary>A picture in the mod's folder by this name replaces the built-in sign.</summary>
		public const string SignFileName = "speed_trap_sign.png";

		// The signs.
		private Texture2D signTexture_;
		private bool signLoaded_;
		private GUIStyle signNumber_;
		private GUIStyle signName_;
		private readonly List<(string Key, CourseGate Gate, string Name)> signs_ = new List<(string, CourseGate, string)>();
		private readonly HashSet<string> signsHidden_ = new HashSet<string>(StringComparer.Ordinal);
		private float nextSignCheck_;
		private readonly Dictionary<string, string> signDetails_ = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>Signs behind a hill or a building are hidden: a ray from the sign back to the camera (the car right in front of it doesn't count).</summary>
		private void CheckSignsInSight(Vector3 camera) {
			signsHidden_.Clear();
			foreach (var (key, gate, _) in signs_) {
				Vector3 sign = gate.Center + Vector3.up * SignHeight;
				Vector3 toCamera = camera - sign;
				float distance = toCamera.magnitude;
				if (distance > SignRange) {
					signsHidden_.Add(key);
					continue;
				}
				try {
					if (distance > 12f && Physics.Raycast(sign, toCamera / distance, distance - 10f, ~0, QueryTriggerInteraction.Ignore)) {
						signsHidden_.Add(key);
					}
				} catch (Exception) {
					// No physics scene yet: shown.
				}
			}
		}

		/// <summary>The trap flash, a little below the timer's spot (or on its own in free roam).</summary>
		private void DrawTrapFlash(float screenWidth, float screenHeight) {
			if (Time.unscaledTime >= trapFlashUntil_ || trapFlash_.Length == 0) {
				return;
			}
			var content = new GUIContent(trapFlash_);
			float width = Mathf.Min(320f, screenWidth - 2 * HudMargin);
			float height = Theme.HudPill.CalcHeight(content, width);
			if (Event.current.type == EventType.Repaint) {
				GUI.Label(TrapFlashRect(width, height, screenWidth, screenHeight), content, Theme.HudPill);
			}
		}

		private bool TrapFlashVisible => hasPose_ && Time.unscaledTime < trapFlashUntil_;

		private bool TrapSignsVisible => hasPose_ && signs_.Count > 0;

		/// <summary>The sign picture: speed_trap_sign.png in the mod's folder if there is one, else the built-in one.</summary>
		private Texture2D SignTexture() {
			if (signLoaded_) {
				return signTexture_;
			}
			signLoaded_ = true;
			try {
				string file = Path.Combine(modFolder_, SignFileName);
				if (File.Exists(file)) {
					var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
					if (ImageConversion.LoadImage(texture, File.ReadAllBytes(file))) {
						signTexture_ = texture;
						TrcLog.Info($"Speed trap sign: using {file} ({texture.width}x{texture.height}).");
						return signTexture_;
					}
					TrcLog.Warning($"Speed trap sign: couldn't read {file}, so the built-in sign is used.");
				}
				signTexture_ = KSL.API.Kino.Utils.LoadEmbeddedTexture(Assembly.GetExecutingAssembly(), "TRC.TuneLoader.Resources.speed_trap_sign.png");
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't load the speed trap sign: {e.Message}");
			}
			return signTexture_;
		}

		/// <summary>
		/// Called from OnGUI: each trap's sign floating over the road, sized by how far away it is, with the last
		/// speed caught there (this car) in the middle. Drawn over the screen, so it needs no 3D model.
		/// </summary>
		private void DrawTrapSigns(float scale) {
			if (Event.current.type != EventType.Repaint || signs_.Count == 0) {
				return;
			}
			Camera camera = HudCamera();
			Texture2D texture = SignTexture();
			if (camera == null || texture == null) {
				return;
			}
			if (signNumber_ == null) {
				signNumber_ = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.86f, 0.98f, 1f) }, wordWrap = false };
				signName_ = new GUIStyle { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.88f, 1f) }, wordWrap = false, richText = false };
			}
			float focal = Screen.height / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
			Color before = GUI.color;
			// Far ones first, so near ones draw on top.
			foreach (var (key, gate, name) in signs_.OrderByDescending(s => (s.Gate.Center - camera.transform.position).sqrMagnitude)) {
				if (signsHidden_.Contains(key)) {
					continue;
				}
				Vector3 world = gate.Center + Vector3.up * SignHeight;
				Vector3 screen = camera.WorldToScreenPoint(world);
				if (screen.z < 2f || screen.z > SignRange) {
					continue;
				}
				float width = Mathf.Clamp(SignWidth * focal / screen.z, 36f, 560f) / scale;
				float height = width * texture.height / texture.width;
				float x = screen.x / scale - width / 2f;
				float y = (Screen.height - screen.y) / scale - height / 2f;
				if (x > Screen.width / scale || x + width < 0 || y > Screen.height / scale || y + height < 0) {
					continue;
				}
				// Never over the window (bskftw, Nov 3: a sign showed on top of the Tracks page).
				if (windowOpen_ && windowRect_.Overlaps(new Rect(x, y, width, height))) {
					continue;
				}
				float fade = Mathf.Clamp01((SignRange - screen.z) / 80f);
				GUI.color = new Color(1f, 1f, 1f, fade);
				GUI.DrawTexture(new Rect(x, y, width, height), texture, ScaleMode.ScaleToFit);
				string speed = trapSignMode_ == 2 ? FastestEverKmh(key) : trapSignMode_ == 1 ? FastestKmh(key)
					: lastTrapKmh_.TryGetValue(key, out float kmh) ? kmh.ToString("0", CultureInfo.InvariantCulture) : LastSavedKmh(key);
				signNumber_.fontSize = Mathf.Max(8, Mathf.RoundToInt(height * 0.34f));
				GUI.Label(new Rect(x, y + height * 0.27f, width, height * 0.45f), speed, signNumber_);
				if (width > 140f) {
					signName_.fontSize = Mathf.Max(9, Mathf.RoundToInt(height * 0.08f));
					GUI.Label(new Rect(x - width * 0.25f, y + height, width * 1.5f, height * 0.2f), name, signName_);
					if (timingDetailed_ && signDetails_.TryGetValue(key, out string detail) && detail.Length > 0) {
						GUI.Label(new Rect(x - width * 0.25f, y + height * 1.16f, width * 1.5f, height * 0.2f), detail, signName_);
					}
				}
			}
			GUI.color = before;
		}
	}
}
