// The shapes of the mod's map icons: a bar for start and finish lines, a little camera for speed traps.
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The shapes of the mod's map icons other than CarX's dot (bskftw, Nov 3): a bar for start and finish lines and a
	/// little camera for speed traps. White on clear, 64 px, so CarX tints them with the icon's colour.
	/// </summary>
	internal static class MapIconSprites {
		private const int Size = 64;
		private static Sprite line_;
		private static Sprite camera_;

		/// <summary>The sprite for a shape; null for <see cref="MapIconShape.Dot"/> (CarX's own).</summary>
		public static Sprite For(MapIconShape shape) {
			switch (shape) {
				case MapIconShape.Line:
					return line_ != null ? line_ : line_ = Make("TRC map line", LinePixel);
				case MapIconShape.Camera:
					return camera_ != null ? camera_ : camera_ = Make("TRC map camera", CameraPixel);
				default:
					return null;
			}
		}

		/// <summary>A bar across the middle, the icon's whole width, a quarter of its height.</summary>
		internal static float LinePixel(float x, float y) {
			bool bar = Mathf.Abs(y - 32f) <= 9f && x >= 2f && x <= 62f;
			return bar ? 1f : 0f;
		}

		/// <summary>A camera from the side: a body with a lens on the front and a viewfinder bump on top.</summary>
		internal static float CameraPixel(float x, float y) {
			bool body = x >= 6f && x <= 50f && y >= 16f && y <= 46f;
			bool bump = x >= 14f && x <= 28f && y > 46f && y <= 54f;
			bool lens = x > 50f && x <= 60f && y >= 22f && y <= 40f;
			float dx = x - 28f, dy = y - 31f;
			bool window = dx * dx + dy * dy <= 64f; // the glass, left clear so it reads as a camera
			return (body || bump || lens) && !window ? 1f : 0f;
		}

		private static Sprite Make(string name, System.Func<float, float, float> pixel) {
			var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			var pixels = new Color32[Size * Size];
			for (int y = 0; y < Size; y++) {
				for (int x = 0; x < Size; x++) {
					// 4 samples a pixel, so the edges are smooth.
					float cover = (pixel(x + 0.25f, y + 0.25f) + pixel(x + 0.75f, y + 0.25f) + pixel(x + 0.25f, y + 0.75f) + pixel(x + 0.75f, y + 0.75f)) / 4f;
					pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(cover * 255f));
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			Sprite sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
			sprite.name = name;
			sprite.hideFlags = HideFlags.DontSave;
			return sprite;
		}
	}
}
