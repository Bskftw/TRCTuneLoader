// Home cards' looks: each card's rounded, fading background and its little icon, drawn in code.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private static readonly Dictionary<string, GUIStyle> cardStyles_ = new Dictionary<string, GUIStyle>(StringComparer.Ordinal);
		private static readonly Texture2D[] cardIcons_ = new Texture2D[5];

		/// <summary>
		/// A card's background at its own size (so its corners stay round): a rounded rectangle with a border in the
		/// part's colour, filled from the colour in the top-left to dark in the bottom-right. Made again when the look changes.
		/// </summary>
		private static GUIStyle CardStyle(string hex, float width, float height) {
			EnsureNavStyles();
			int w = Mathf.Clamp(Mathf.RoundToInt(width), 16, 2048), h = Mathf.Clamp(Mathf.RoundToInt(height), 16, 1024);
			string key = $"{hex} {w}x{h} {Theme.ToHex(Theme.Panel)} {Theme.ToHex(Theme.Border)}";
			if (cardStyles_.TryGetValue(key, out GUIStyle style) && style.normal.background != null) {
				return style;
			}
			Color colour = HexColor(hex);
			Color dark = Theme.Luminance(Theme.Background) < 0.5f ? Color.black : Color.white;
			Color from = Color.Lerp(Theme.Panel, colour, 0.24f);
			Color to = Color.Lerp(Theme.Panel, dark, 0.35f);
			Color edge = Color.Lerp(Theme.Border, colour, 0.6f);
			const float radius = 12f;
			var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			var pixels = new Color32[w * h];
			var half = new Vector2(w / 2f, h / 2f);
			var inner = half - new Vector2(radius, radius);
			for (int y = 0; y < h; y++) {
				for (int x = 0; x < w; x++) {
					Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - half;
					Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - inner;
					float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
					float depth = -outside;
					// Row 0 is the bottom: the top-left corner is x 0, y h.
					float t = Mathf.Clamp01((x / (float)w + (1f - y / (float)h)) / 2f);
					Color fill = Color.Lerp(from, to, t);
					Color c = depth < 1.5f ? Color.Lerp(edge, fill, Mathf.Clamp01(depth - 0.5f)) : fill;
					c.a = Mathf.Clamp01(depth + 0.5f);
					pixels[y * w + x] = c;
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			if (style?.normal.background != null) {
				UnityEngine.Object.Destroy(style.normal.background);
			}
			style = new GUIStyle {
				normal = { background = texture },
				border = new RectOffset(0, 0, 0, 0),
				padding = new RectOffset(20, 18, 16, 14),
				margin = new RectOffset(0, 0, 0, 0),
			};
			cardStyles_[key] = style;
			return style;
		}

		/// <summary>The cards' little pictures, white (tinted with the part's colour when drawn): a note, a list, a stopwatch, a flag, a target.</summary>
		private static Texture2D CardIcon(int index) {
			if (index < 0 || index >= cardIcons_.Length) {
				return null;
			}
			if (cardIcons_[index] != null) {
				return cardIcons_[index];
			}
			System.Func<float, float, bool> shape;
			switch (index) {
				case 0: // a music note (the tunes)
					shape = (x, y) => Ellipse(x, y, 22f, 16f, 11f, 8f) || x >= 30f && x <= 35f && y >= 16f && y <= 56f || Segment(x, y, 33f, 56f, 50f, 42f, 3f);
					break;
				case 1: // a list (specs)
					shape = (x, y) => Ring(x, y, 12f, 48f, 0f, 4.5f) || Ring(x, y, 12f, 32f, 0f, 4.5f) || Ring(x, y, 12f, 16f, 0f, 4.5f) ||
						x >= 22f && x <= 56f && (Mathf.Abs(y - 48f) <= 3f || Mathf.Abs(y - 32f) <= 3f || Mathf.Abs(y - 16f) <= 3f);
					break;
				case 2: // a stopwatch
					shape = (x, y) => Ring(x, y, 32f, 28f, 18f, 23f) || x >= 28f && x <= 36f && y >= 50f && y <= 58f || Segment(x, y, 32f, 28f, 40f, 40f, 2.5f);
					break;
				case 3: // a flag
					shape = (x, y) => x >= 12f && x <= 17f && y >= 6f && y <= 58f || x > 17f && x <= 52f && y >= 32f && y <= 58f && !(x > 40f && Mathf.Abs(y - 45f) < (x - 40f) * 0.9f);
					break;
				default: // a target
					shape = (x, y) => Ring(x, y, 32f, 32f, 19f, 24f) || Ring(x, y, 32f, 32f, 0f, 8f);
					break;
			}
			const int size = 64;
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Trilinear };
			var pixels = new Color32[size * size];
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					int cover = 0;
					for (int s = 0; s < 4; s++) {
						if (shape(x + (s % 2 == 0 ? 0.25f : 0.75f), y + (s < 2 ? 0.25f : 0.75f))) {
							cover++;
						}
					}
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)(cover * 255 / 4));
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(true, true);
			cardIcons_[index] = texture;
			return texture;
		}

		private static bool Ellipse(float x, float y, float cx, float cy, float rx, float ry) {
			float dx = (x - cx) / rx, dy = (y - cy) / ry;
			return dx * dx + dy * dy <= 1f;
		}

		private static bool Ring(float x, float y, float cx, float cy, float inner, float outer) {
			float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
			return d >= inner && d <= outer;
		}

		/// <summary>Within <paramref name="half"/> of the line from (ax, ay) to (bx, by).</summary>
		private static bool Segment(float x, float y, float ax, float ay, float bx, float by, float half) {
			Vector2 a = new Vector2(ax, ay), b = new Vector2(bx, by), p = new Vector2(x, y);
			Vector2 ab = b - a;
			float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
			return (a + ab * t - p).magnitude <= half;
		}
	}
}
