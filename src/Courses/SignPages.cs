// What the gantry signs say: STAGE START with its lamps, STAGE CLEAR, laps, checkpoints.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TRC.TuneLoader {
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
