// Paints a gantry sign into a picture: panels, lamps and text from the letter picture.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Paints a gantry sign into a picture (rows top to bottom; <see cref="BottomUp"/> turns it the way Unity's textures
	/// want it): panels, lamps, scanlines and glowing text. Knows nothing about the game, so the self-test can paint too.
	/// </summary>
	public sealed class SignPainter {
		public readonly int Width;
		public readonly int Height;
		public readonly Color32[] Pixels;
		private readonly GlyphAtlas atlas_;

		public SignPainter(int width, int height, GlyphAtlas atlas) {
			Width = width;
			Height = height;
			Pixels = new Color32[width * height];
			atlas_ = atlas;
		}

		public void Fill(Color32 color) {
			for (int i = 0; i < Pixels.Length; i++) {
				Pixels[i] = color;
			}
		}

		/// <summary>Faint dark lines every few rows, like an old screen.</summary>
		public void Scanlines(int every, float alpha) {
			for (int y = 0; y < Height; y += every) {
				for (int x = 0; x < Width; x++) {
					Blend(x, y, new Color32(0, 0, 0, 255), alpha);
				}
			}
		}

		/// <summary>A light speckle of dots (the sign's sparkle), the same every time.</summary>
		public void Speckle(Color32 color, int count, int seed) {
			var random = new System.Random(seed);
			for (int i = 0; i < count; i++) {
				int x = random.Next(Width), y = random.Next(Height);
				Blend(x, y, color, 0.25f + 0.5f * (float)random.NextDouble());
			}
		}

		/// <summary>A rounded rectangle's outline, with a soft glow round it.</summary>
		public void Frame(float inset, float radius, float thickness, Color32 color, float glow) {
			float left = inset, top = inset, right = Width - 1 - inset, bottom = Height - 1 - inset;
			for (int y = 0; y < Height; y++) {
				for (int x = 0; x < Width; x++) {
					// Distance to the rounded rectangle's edge.
					float dx = Math.Max(Math.Max(left + radius - x, x - (right - radius)), 0f);
					float dy = Math.Max(Math.Max(top + radius - y, y - (bottom - radius)), 0f);
					// In a corner (or outside): how far from the corner's arc; else from the nearest straight side.
					float edge = dx > 0 || dy > 0
						? Math.Abs((float)Math.Sqrt(dx * dx + dy * dy) - radius)
						: Math.Min(Math.Min(x - left, right - x), Math.Min(y - top, bottom - y));
					if (edge <= thickness / 2f) {
						Blend(x, y, color, 1f);
					} else if (glow > 0f && edge <= thickness / 2f + glow) {
						float t = 1f - (edge - thickness / 2f) / glow;
						Blend(x, y, color, 0.45f * t * t);
					}
				}
			}
		}

		/// <summary>A lamp: a filled circle with a ring round it and, when lit, a glow.</summary>
		public void Lamp(float cx, float cy, float radius, Color32 fill, Color32 ring, float ringWidth, bool lit) {
			float reach = radius + ringWidth + (lit ? radius * 0.9f : 2f);
			int x0 = Math.Max(0, (int)(cx - reach)), x1 = Math.Min(Width - 1, (int)(cx + reach));
			int y0 = Math.Max(0, (int)(cy - reach)), y1 = Math.Min(Height - 1, (int)(cy + reach));
			for (int y = y0; y <= y1; y++) {
				for (int x = x0; x <= x1; x++) {
					float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
					if (lit && d > radius + ringWidth) {
						float t = 1f - (d - radius - ringWidth) / (radius * 0.9f);
						if (t > 0f) {
							Blend(x, y, fill, 0.5f * t * t);
						}
					}
					if (d <= radius) {
						Blend(x, y, fill, Smooth(radius - d));
					}
					float r = Math.Abs(d - radius - ringWidth / 2f);
					if (r <= ringWidth / 2f + 1f) {
						Blend(x, y, ring, Smooth(ringWidth / 2f + 1f - r));
					}
				}
			}
		}

		/// <summary>How wide this text is at this capital height, with this much extra room between letters.</summary>
		public float TextWidth(string text, float cap, float spacing = 0f) {
			if (atlas_ == null || string.IsNullOrEmpty(text)) {
				return 0f;
			}
			float scale = cap / atlas_.Cap, width = 0f;
			foreach (char c in text) {
				if (atlas_.TryGet(c, out GlyphAtlas.Cell cell)) {
					width += cell.Advance * scale + spacing;
				}
			}
			return Math.Max(0f, width - spacing);
		}

		/// <summary>
		/// Text whose capitals start at <paramref name="top"/>: left of <paramref name="x"/> (align 0), centred on it
		/// (1) or ending at it (2), with a neon glow in <paramref name="glowColor"/>. Letters the atlas hasn't got are skipped.
		/// </summary>
		public void Text(string text, float x, float top, float cap, Color32 color, int align = 1, float spacing = 0f, Color32? glowColor = null, float glow = 0.8f) {
			if (atlas_ == null || string.IsNullOrEmpty(text)) {
				return;
			}
			float width = TextWidth(text, cap, spacing);
			float pen = align == 1 ? x - width / 2f : align == 2 ? x - width : x;
			float scale = cap / atlas_.Cap;
			foreach (char c in text) {
				if (!atlas_.TryGet(c, out GlyphAtlas.Cell cell)) {
					continue;
				}
				float left = pen - atlas_.Pad * scale, cellTop = top - atlas_.CapTop * scale;
				int w = (int)Math.Ceiling(cell.Width * scale), h = (int)Math.Ceiling(cell.Height * scale);
				for (int dy = 0; dy < h; dy++) {
					int y = (int)cellTop + dy;
					if (y < 0 || y >= Height) {
						continue;
					}
					for (int dx = 0; dx < w; dx++) {
						int px = (int)left + dx;
						if (px < 0 || px >= Width) {
							continue;
						}
						float ax = cell.X + (px - left) / scale, ay = cell.Y + (y - cellTop) / scale;
						if (glow > 0f) {
							float g = atlas_.Sample(atlas_.Glow, ax, ay);
							if (g > 0.01f) {
								Blend(px, y, glowColor ?? color, g * glow * 0.6f);
							}
						}
						float a = atlas_.Sample(atlas_.Sharp, ax, ay);
						if (a > 0.01f) {
							Blend(px, y, color, a);
						}
					}
				}
				pen += cell.Advance * scale + spacing;
			}
		}

		/// <summary>
		/// The picture turned a quarter left (so text runs bottom to top), bottom row first, for a texture
		/// <see cref="Height"/> wide and <see cref="Width"/> tall: the checkpoint towers' upright banners.
		/// </summary>
		public Color32[] UprightBottomUp() {
			var turned = new Color32[Pixels.Length];
			// Turned picture (top row first): column x', row y' comes from the original's x = Width-1-y', y = x'.
			for (int ty = 0; ty < Width; ty++) {
				int row = Width - 1 - ty; // bottom row first
				for (int tx = 0; tx < Height; tx++) {
					turned[row * Height + tx] = Pixels[tx * Width + (Width - 1 - ty)];
				}
			}
			return turned;
		}

		/// <summary>The picture with the bottom row first, as Texture2D.SetPixels32 wants it.</summary>
		public Color32[] BottomUp() {
			var flipped = new Color32[Pixels.Length];
			for (int y = 0; y < Height; y++) {
				Array.Copy(Pixels, y * Width, flipped, (Height - 1 - y) * Width, Width);
			}
			return flipped;
		}

		private static float Smooth(float v) => v <= 0f ? 0f : v >= 1f ? 1f : v;

		private void Blend(int x, int y, Color32 color, float alpha) {
			if (alpha <= 0f || x < 0 || y < 0 || x >= Width || y >= Height) {
				return;
			}
			alpha = Math.Min(1f, alpha);
			int i = y * Width + x;
			Color32 d = Pixels[i];
			Pixels[i] = new Color32(
				(byte)(d.r + (color.r - d.r) * alpha),
				(byte)(d.g + (color.g - d.g) * alpha),
				(byte)(d.b + (color.b - d.b) * alpha),
				255);
		}
	}
}
