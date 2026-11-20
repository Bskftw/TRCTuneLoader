using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Stage 6: tandem aids, lap compare, challenges, driver stats, the start invite.</summary>
	internal static partial class SelfTest {
		private static void Stage6Tests() {
			// Tandem: two cars 6 m apart for 3 s (held close), one sliding 30 degrees.
			var tandem = new TandemWatch();
			for (int f = 0; f < 180; f++) {
				float t = f / 60f;
				tandem.Tick(1f / 60f, new Vector3(0f, 0f, 20f * t), new Vector3(0.5f, 0f, 0.8660254f), default, new Vector3(0f, 0f, 20f * t + 6f), Vector3.forward, default);
			}
			Expect(tandem.Gap > 1f && tandem.Gap < 2.5f && tandem.Held > 2.9f && Mathf.Abs(tandem.MyAngle - 30f) < 2f && tandem.TheirAngle < 2f,
				$"tandem: gap {tandem.Gap:0.0} m, held {tandem.Held:0.0} s, angles {tandem.MyAngle:0} and {tandem.TheirAngle:0}");
			tandem.Tick(1f / 60f, Vector3.zero, Vector3.forward, default, new Vector3(0f, 0f, 30f), Vector3.forward, default);
			// Door to door: side by side 2.1 m apart (middles), 1.8 m wide cars, is a 0.3 m gap; nose into a door; overlapping is 0.
			var body = new Bounds(Vector3.zero, new Vector3(1.8f, 1.3f, 4.4f));
			float doors = TandemWatch.BodyGap(Vector3.zero, Vector3.forward, body, new Vector3(2.1f, 0f, 0.5f), Vector3.forward, body);
			float nose = TandemWatch.BodyGap(Vector3.zero, Vector3.forward, body, new Vector3(-3.5f, 0f, 0f), Vector3.right, body);
			float overlap = TandemWatch.BodyGap(Vector3.zero, Vector3.forward, body, new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 1f), body);
			Expect(Mathf.Abs(doors - 0.3f) < 0.01f && Mathf.Abs(nose - 0.4f) < 0.01f && overlap == 0f,
				$"tandem: gaps measured body to body (doors {doors:0.00} m, nose to door {nose:0.00} m, overlapping {overlap:0.00} m)");
			Expect(tandem.Held == 0f && tandem.BestHeld > 2.9f, "tandem: dropping back resets held close, the best stays");
			Expect(TandemWatch.DriftAngle(Vector3.forward, Vector3.zero) == 0f, "tandem: no angle when standing");

			// Lap compare: lost 0.5 s between 200 and 400 m, gained 0.2 s from 600 m.
			int n = 101;
			var best = new float[n];
			var mine = new float[n];
			for (int i = 0; i < n; i++) {
				best[i] = i * 0.4f;
				mine[i] = best[i] + (i > 40 ? 0.5f : i > 20 ? (i - 20) * 0.025f : 0f) - (i > 60 ? Mathf.Min(0.2f, (i - 60) * 0.02f) : 0f);
			}
			List<LapCompare.Stretch> stretches = LapCompare.Stretches(mine, best, 10f, 200f);
			LapCompare.Stretch worst = LapCompare.Worst(stretches, 1).First();
			Expect(stretches.Count == 5 && worst.From == 200f && Mathf.Abs(worst.Lost - 0.5f) < 0.01f && worst.MyKmh < worst.BestKmh,
				$"lap compare: most time lost 200-400 m ({worst.From:0}-{worst.To:0}, {worst.Lost:0.00} s)");
			Expect(LapCompare.Best(stretches, 1).First().From == 600f, "lap compare: the best stretch is found too");
			Expect(LapCompare.Stretches(null, best, 10f, 200f).Count == 0, "lap compare: nothing to compare gives nothing");

			// Challenges: the same pick from the same date, medals from the length.
			var codes = new[] { "CRS-B", "CRS-A", "CRS-C" };
			string day = Challenges.Pick("2026-11-18", codes);
			Expect(day == Challenges.Pick("2026-11-18", codes.Reverse()) && codes.Contains(day), "challenges: the same date picks the same track in any order");
			Expect(Challenges.WeekKey(new DateTime(2026, 11, 18)) == "2026-W47" && Challenges.DayKey(new DateTime(2026, 11, 18)) == "2026-11-18", "challenges: day and week keys");
			var times = Challenges.Times(5000f, CourseKind.Touge);
			Expect(times.Gold < times.Silver && times.Silver < times.Bronze && Challenges.MedalFor(times.Gold - 1, 5000f, CourseKind.Touge) == Challenges.Medal.Gold
				&& Challenges.MedalFor(times.Bronze + 1, 5000f, CourseKind.Touge) == Challenges.Medal.None, "challenges: gold beats silver beats bronze");
			Expect(Challenges.Pick("x", new string[0]) == null, "challenges: no tracks, no challenge");

			// Driver stats: km per car round trip, battles and races from the logs.
			var km = new Dictionary<string, double> { ["AE86"] = 12.5, ["S13"] = 3.25 };
			var back = DriverStats.ReadKm(DriverStats.WriteKm(km) + "broken line\n");
			Expect(back.Count == 2 && Math.Abs(back["AE86"] - 12.5) < 1e-6, "driver stats: km per car comes back");
			var battleRows = new List<List<string>> {
				new List<string> { "date", "result", "my_car" }, new List<string> { "d", "win", "AE86" }, new List<string> { "d", "loss", "AE86" },
				new List<string> { "d", "win", "S13" }, new List<string> { "d", "draw", "S13" },
			};
			var battles = DriverStats.Battles(battleRows);
			Expect(battles.All.Won == 2 && battles.All.Lost == 1 && battles.All.Other == 1 && battles.ByCar["AE86"].WinRate == "50%", "driver stats: battles and win rate");
			var races = DriverStats.Races(new List<List<string>> { new List<string> { "position" }, new List<string> { "1" }, new List<string> { "3" }, new List<string> { "5" } });
			Expect(races.Races == 3 && races.Won == 1 && races.Podiums == 2, "driver stats: races, wins and podiums");

			// The start invite reads back.
			var invite = RoomMessage.Decode(new RoomMessage { Kind = RoomKind.Invite, From = 5, Name = "bskftw", MapKey = "pd", MapName = "Project Dream", Label = "CRS-1234-ABCD", Detail = "Akina Downhill" }.Encode());
			Expect(invite != null && invite.Kind == RoomKind.Invite && invite.Label == "CRS-1234-ABCD" && invite.Detail == "Akina Downhill" && invite.To == 0, "host tools: a start invite reads back");
		}
	}
}
