// The gantry signs' letters: where each one is in the letter picture.
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
}
