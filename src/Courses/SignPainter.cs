using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The letters for the gantry signs (Resources/gantry_font.png, gantry_glow.png and gantry_font.txt, made by
	/// tools/make_gantry_font.py): where each letter is in the picture, and its alpha, sharp and blurred. Rows run top
	/// to bottom here.
	/// </summary>
	public sealed class GlyphAtlas {
		public struct Cell {
			public int X, Y, Width, Height;
			public float Advance;
		}

		public int Width { get; private set; }
		public int Height { get; private set; }

		/// <summary>The height of a capital in the picture, and how far below a cell's top capitals start (pixels).</summary>
		public float Cap { get; private set; }
		public float CapTop { get; private set; }
		public float Pad { get; private set; }

		internal byte[] Sharp;
		internal byte[] Glow;
		private readonly Dictionary<char, Cell> cells_ = new Dictionary<char, Cell>();

		public bool TryGet(char c, out Cell cell) => cells_.TryGetValue(char.ToUpperInvariant(c), out cell);

		/// <summary>From the metrics text and the two pictures' alpha (top row first). Null when they don't fit together.</summary>
		public static GlyphAtlas Parse(string metrics, byte[] sharp, byte[] glow, int width, int height) {
			if (string.IsNullOrEmpty(metrics) || sharp == null || glow == null || sharp.Length != width * height || glow.Length != width * height) {
				return null;
			}
			var atlas = new GlyphAtlas { Width = width, Height = height, Sharp = sharp, Glow = glow };
			string[] lines = metrics.Replace("\r", "").Split('\n');
			string[] head = lines[0].Split(' ');
			if (head.Length < 4) {
				return null;
			}
			atlas.Cap = Number(head[1]);
			atlas.CapTop = Number(head[2]);
			atlas.Pad = Number(head[3]);
			for (int i = 1; i < lines.Length; i++) {
				string[] f = lines[i].Split(' ');
				if (f.Length < 6) {
					continue;
				}
				var cell = new Cell { X = (int)Number(f[1]), Y = (int)Number(f[2]), Width = (int)Number(f[3]), Height = (int)Number(f[4]), Advance = Number(f[5]) };
				if (cell.X < 0 || cell.Y < 0 || cell.X + cell.Width > width || cell.Y + cell.Height > height) {
					continue;
				}
				atlas.cells_[(char)(int)Number(f[0])] = cell;
			}
			return atlas.Cap > 0 && atlas.cells_.Count > 0 ? atlas : null;
		}

		private static float Number(string s) =>
			float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

		/// <summary>The alpha at a point in the picture, smoothly between pixels.</summary>
		internal float Sample(byte[] alpha, float x, float y) {
			int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
			float fx = x - x0, fy = y - y0;
			float a = At(alpha, x0, y0), b = At(alpha, x0 + 1, y0), c = At(alpha, x0, y0 + 1), d = At(alpha, x0 + 1, y0 + 1);
			return ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy) / 255f;
		}

		private float At(byte[] alpha, int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? 0f : alpha[y * Width + x];
	}

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

	/// <summary>
	/// What the gantry signs say (bskftw's designs, Nov 2): STAGE START with four lamps that light with the countdown,
	/// and STAGE CLEAR with the time, top speed and rank. Blue at the start, green at the finish.
	/// </summary>
	public static class SignPages {
		public const int Width = 1024;
		/// <summary>Shorter than the first design (bskftw, Nov 2: half as tall; Nov 3: three quarters of that again).</summary>
		public const int Height = 192;

		/// <summary>The start sign's steps: nothing yet, 3, 2, 1, GO.</summary>
		public const int Idle = 0, Go = 4;

		private static readonly Color32 StartGlow = new Color32(64, 170, 255, 255);
		private static readonly Color32 StartBack = new Color32(12, 30, 58, 255);
		private static readonly Color32 FinishGlow = new Color32(60, 240, 150, 255);
		private static readonly Color32 FinishBack = new Color32(8, 42, 38, 255);
		private static readonly Color32 CheckGlow = new Color32(190, 255, 40, 255);
		private static readonly Color32 CheckBack = new Color32(26, 46, 12, 255);
		private static readonly Color32 Behind = new Color32(255, 170, 60, 255);
		private static readonly Color32 White = new Color32(240, 248, 255, 255);
		private static readonly Color32 Gold = new Color32(255, 214, 90, 255);

		private static readonly Color32 RedOff = new Color32(100, 22, 40, 255), RedOn = new Color32(255, 46, 70, 255);
		private static readonly Color32 AmberOff = new Color32(100, 76, 18, 255), AmberOn = new Color32(255, 186, 40, 255);
		private static readonly Color32 GreenOff = new Color32(18, 84, 48, 255), GreenOn = new Color32(40, 255, 120, 255);

		private static void Panel(SignPainter p, Color32 back, Color32 glow) {
			p.Fill(back);
			p.Speckle(glow, 60, 7);
			p.Scanlines(4, 0.18f);
			p.Frame(8f, 18f, 5f, glow, 8f);
		}

		/// <summary>STAGE START, the lamps for this step (<see cref="Idle"/>, 3, 2, 1 or <see cref="Go"/>) and GET READY · 3 · 2 · 1 · GO.</summary>
		public static void Start(SignPainter p, int step) {
			Panel(p, StartBack, StartGlow);
			p.Text("STAGE START", Width / 2f, 16f, 28f, White, spacing: 6f, glowColor: StartGlow);
			// 3: the first lamp red; 2: two red; 1: and the amber; GO: only the green.
			bool[] lit = {
				step == 3 || step == 2 || step == 1,
				step == 2 || step == 1,
				step == 1,
				step == Go,
			};
			Color32[] on = { RedOn, RedOn, AmberOn, GreenOn };
			Color32[] off = { RedOff, RedOff, AmberOff, GreenOff };
			for (int i = 0; i < 4; i++) {
				p.Lamp(Width / 2f + (i - 1.5f) * 92f, 92f, 27f, lit[i] ? on[i] : off[i], StartGlow, 4f, lit[i]);
			}
			string[] words = { "GET READY", "3", "2", "1", "GO" };
			int current = step == Idle ? 0 : step == Go ? 4 : 4 - step;
			var dim = new Color32(40, 120, 200, 255);
			float cap = 20f, spacing = 5f, dot = p.TextWidth(" • ", cap, spacing);
			float total = 0f;
			foreach (string w in words) {
				total += p.TextWidth(w, cap, spacing);
			}
			total += dot * (words.Length - 1);
			float x = Width / 2f - total / 2f;
			for (int i = 0; i < words.Length; i++) {
				bool now = i == current && step != Idle;
				Color32 color = now ? (i == 4 ? GreenOn : White) : dim;
				p.Text(words[i], x, 144f, cap, color, align: 0, spacing: spacing, glowColor: now && i == 4 ? GreenOn : StartGlow, glow: now ? 1f : 0.5f);
				x += p.TextWidth(words[i], cap, spacing);
				if (i < words.Length - 1) {
					p.Text(" • ", x, 144f, cap, dim, align: 0, spacing: spacing, glowColor: StartGlow, glow: 0.4f);
					x += dot;
				}
			}
		}

		/// <summary>The finish before anyone gets there: FINISH and the course's name.</summary>
		public static void Finish(SignPainter p, string course) {
			Panel(p, FinishBack, FinishGlow);
			p.Text("FINISH", Width / 2f, 28f, 56f, White, spacing: 10f, glowColor: FinishGlow);
			p.Text(Fit(p, course, 22f, Width - 100f, 4f), Width / 2f, 124f, 22f, FinishGlow, spacing: 4f, glowColor: FinishGlow, glow: 0.6f);
		}

		/// <summary>STAGE CLEAR: the time, the top speed and the rank (each "" to leave it out), and NEW PERSONAL BEST.</summary>
		public static void Clear(SignPainter p, string time, string topSpeed, string rank, bool personalBest) {
			Panel(p, FinishBack, FinishGlow);
			p.Text("STAGE CLEAR", Width / 2f, 16f, 30f, White, spacing: 8f, glowColor: FinishGlow);
			var columns = new List<(string Label, string Value)>();
			if (time.Length > 0) {
				columns.Add(("TIME", time));
			}
			if (topSpeed.Length > 0) {
				columns.Add(("TOP SPEED", topSpeed));
			}
			if (rank.Length > 0) {
				columns.Add(("RANK", rank));
			}
			float slot = (Width - 120f) / Math.Max(1, columns.Count);
			for (int i = 0; i < columns.Count; i++) {
				float x = 60f + slot * (i + 0.5f);
				p.Text(columns[i].Label, x, 62f, 14f, FinishGlow, spacing: 4f, glowColor: FinishGlow, glow: 0.6f);
				float cap = columns[i].Label == "RANK" ? 32f : 27f;
				p.Text(Fit(p, columns[i].Value, cap, slot - 20f, 3f), x, columns[i].Label == "RANK" ? 82f : 85f, cap, White, spacing: 3f, glowColor: FinishGlow, glow: 0.7f);
			}
			if (personalBest) {
				p.Text("NEW PERSONAL BEST", Width / 2f, 140f, 18f, Gold, spacing: 10f, glowColor: Gold, glow: 0.9f);
			}
		}

		/// <summary>A circuit's line during the race: LAP n / N (or just n) and the last lap's time.</summary>
		public static void Lap(SignPainter p, int lap, int laps, string lastLap) {
			Panel(p, StartBack, StartGlow);
			p.Text(laps > 0 ? $"LAP {lap} / {laps}" : $"LAP {lap}", Width / 2f, 26f, 56f, White, spacing: 8f, glowColor: StartGlow);
			if (lastLap.Length > 0) {
				p.Text($"LAST LAP {lastLap}", Width / 2f, 122f, 24f, StartGlow, spacing: 5f, glowColor: StartGlow, glow: 0.6f);
			}
		}

		/// <summary>
		/// A sector checkpoint (bskftw's design, Nov 2): CHECKPOINT n, then once passed this lap the gap to the PB
		/// ("-0.842", AHEAD OF PB or BEHIND PB in amber), or the sector's time when there's no PB to compare with.
		/// Before it's passed, dashes.
		/// </summary>
		public static void Checkpoint(SignPainter p, int number, string big, string under, bool behind) {
			Panel(p, CheckBack, CheckGlow);
			p.Text($"CHECKPOINT {number}", Width / 2f, 16f, 26f, CheckGlow, spacing: 8f, glowColor: CheckGlow, glow: 0.8f);
			bool passed = big.Length > 0;
			p.Text(passed ? big : "- - -", Width / 2f, 58f, 64f, passed ? White : new Color32(110, 150, 60, 255), spacing: 6f, glowColor: CheckGlow, glow: passed ? 0.9f : 0.3f);
			if (under.Length > 0) {
				Color32 colour = behind ? Behind : CheckGlow;
				p.Text(under, Width / 2f, 146f, 18f, colour, spacing: 10f, glowColor: colour, glow: 0.7f);
			}
		}

		/// <summary>The upright banner on a checkpoint's towers: SECTOR n, painted lying down (turn it with <see cref="SignPainter.UprightBottomUp"/>).</summary>
		public static void Banner(SignPainter p, string text) {
			Panel(p, CheckBack, CheckGlow);
			p.Text(text, Width / 2f, 64f, 64f, White, spacing: 24f, glowColor: CheckGlow, glow: 0.9f);
		}

		/// <summary>The text, cut short with ".." so it fits in <paramref name="width"/> pixels.</summary>
		private static string Fit(SignPainter p, string text, float cap, float width, float spacing) {
			text = (text ?? "").ToUpperInvariant();
			if (p.TextWidth(text, cap, spacing) <= width) {
				return text;
			}
			while (text.Length > 1 && p.TextWidth(text + "..", cap, spacing) > width) {
				text = text.Substring(0, text.Length - 1);
			}
			return text.TrimEnd() + "..";
		}
	}
}
