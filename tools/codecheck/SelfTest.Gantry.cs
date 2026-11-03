using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>
	/// Nov 2: the gantry signs (STAGE START with the countdown lamps, STAGE CLEAR) are painted from the mod's letter
	/// picture. Set TRC_SIGN_PREVIEW to a folder to save each page as a PNG to look at.
	/// </summary>
	internal static partial class SelfTest {
		private static void GantryTests() {
			string resources = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Resources");
			if (!File.Exists(Path.Combine(resources, "gantry_font.txt"))) {
				resources = Path.Combine(Directory.GetCurrentDirectory(), "Resources");
			}
			GlyphAtlas atlas = LoadAtlas(resources);
			Expect(atlas != null, $"the gantry letters load ({resources})");
			if (atlas == null) {
				return;
			}
			Expect(atlas.TryGet('A', out _) && atlas.TryGet('a', out _) && atlas.TryGet('7', out _) && atlas.TryGet(':', out _) && atlas.TryGet('•', out _),
				"the letters have capitals, small letters (drawn as capitals), digits and the dot");

			var painter = new SignPainter(SignPages.Width, SignPages.Height, atlas);
			string preview = Environment.GetEnvironmentVariable("TRC_SIGN_PREVIEW");
			int Bright() => painter.Pixels.Count(c => c.r > 200 && c.g > 200 && c.b > 200);

			SignPages.Start(painter, SignPages.Idle);
			int idle = Bright();
			Save(painter, preview, "start_idle");
			SignPages.Start(painter, 3);
			Save(painter, preview, "start_3");
			int redLit = painter.Pixels.Count(c => c.r > 230 && c.g < 90);
			SignPages.Start(painter, SignPages.Go);
			Save(painter, preview, "start_go");
			int greenLit = painter.Pixels.Count(c => c.g > 230 && c.r < 90);
			Expect(idle > 1500 && redLit > 1500 && greenLit > 1500, $"the start sign has its title, a red lamp at 3 and the green at GO ({idle}, {redLit}, {greenLit} px)");

			SignPages.Clear(painter, "1:42.387", "184 KM/H", "#1", true);
			Save(painter, preview, "stage_clear");
			int gold = painter.Pixels.Count(c => c.r > 230 && c.g > 190 && c.b < 140);
			Expect(Bright() > 3000 && gold > 1000, "STAGE CLEAR shows the time and NEW PERSONAL BEST");
			SignPages.Finish(painter, "Okutama Circuit 1 Reverse with a very long name indeed");
			Save(painter, preview, "finish");
			SignPages.Lap(painter, 2, 5, "1:02.345");
			Save(painter, preview, "lap");
			SignPages.Checkpoint(painter, 2, "-0.842", "AHEAD OF PB", false);
			Save(painter, preview, "checkpoint_ahead");
			SignPages.Checkpoint(painter, 3, "+1.205", "BEHIND PB", true);
			Save(painter, preview, "checkpoint_behind");
			SignPages.Checkpoint(painter, 1, "", "", false);
			Save(painter, preview, "checkpoint_idle");
			SignPages.Banner(painter, "SECTOR 2");
			Save(painter, preview, "banner");
			Color32[] upright = painter.UprightBottomUp();
			// The banner's first letter (left of the picture) ends up at the bottom of the upright one.
			Expect(upright[(SignPages.Height / 2) + 40 * SignPages.Height].Equals(painter.Pixels[(SignPages.Height / 2) * SignPages.Width + 40]),
				"the upright banner is the picture turned a quarter left (text reads bottom to top)");
			Expect(painter.BottomUp()[0].Equals(painter.Pixels[(SignPages.Height - 1) * SignPages.Width]), "the picture is turned the way Unity's textures want it");
		}

		private static GlyphAtlas LoadAtlas(string folder) {
			try {
				byte[] Alpha(string file, out int w, out int h) {
					using (var bitmap = new Bitmap(Path.Combine(folder, file))) {
						w = bitmap.Width;
						h = bitmap.Height;
						var alpha = new byte[w * h];
						for (int y = 0; y < h; y++) {
							for (int x = 0; x < w; x++) {
								alpha[y * w + x] = bitmap.GetPixel(x, y).A;
							}
						}
						return alpha;
					}
				}
				byte[] sharp = Alpha("gantry_font.png", out int width, out int height);
				byte[] glow = Alpha("gantry_glow.png", out _, out _);
				return GlyphAtlas.Parse(File.ReadAllText(Path.Combine(folder, "gantry_font.txt")), sharp, glow, width, height);
			} catch (Exception e) {
				Console.WriteLine($"gantry letters: {e.Message}");
				return null;
			}
		}

		private static void Save(SignPainter painter, string folder, string name) {
			if (string.IsNullOrEmpty(folder)) {
				return;
			}
			Directory.CreateDirectory(folder);
			using (var bitmap = new Bitmap(painter.Width, painter.Height, PixelFormat.Format32bppArgb)) {
				for (int y = 0; y < painter.Height; y++) {
					for (int x = 0; x < painter.Width; x++) {
						var c = painter.Pixels[y * painter.Width + x];
						bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(255, c.r, c.g, c.b));
					}
				}
				bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
			}
		}
	}
}
