using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>
	/// Nov 2 (bskftw): cars line up close together side by side, single file where the road is too narrow (each timed
	/// from the start line), and circuits keep to their own paths.
	/// </summary>
	internal static partial class SelfTest {
		private static void StartTests016() {
			SideBySideIsClose();
			SingleFileLinesUp();
			SingleFileClockStartsAtTheLine();
			SingleFileTravels();
			LoopedPathReadsRound();
			ShortGridPathIsNoticed();
			CircuitGridFollowsTheLap();
			SkippedCopyNumbers();
			SplitsKnowTheGap();
			RulesAreSavedWithTimes();
		}

		/// <summary>bskftw, Nov 3: time attacks can be TRC spec, Open or to a regulation; each keeps its own times, and speed traps note the rules.</summary>
		private static void RulesAreSavedWithTimes() {
			string folder = Path.Combine(Path.GetTempPath(), "trc_rules_" + Guid.NewGuid().ToString("N"));
			try {
				var spec = new TimeAttackRecord { When = new DateTime(2026, 11, 3, 12, 0, 0), CourseCode = "CRS-1", CarId = 7, TuneSet = "Revival", Spec = TimeAttackLog.SpecWord, TimeMs = 60000 };
				var open = new TimeAttackRecord { When = spec.When, CourseCode = "CRS-1", CarId = 7, TuneSet = "Revival", Spec = TimeAttackLog.SpecWord, TimeMs = 55000, Rule = TimeAttackLog.OpenRule };
				var rule = new TimeAttackRecord { When = spec.When, CourseCode = "CRS-1", CarId = 7, TuneSet = "Revival", Spec = "not spec", TimeMs = 58000, Rule = "REG-1234-ABCD", RuleOk = true };
				foreach (var r in new[] { spec, open, rule }) {
					TimeAttackLog.Append(folder, r);
				}
				List<TimeAttackRecord> back = TimeAttackLog.Load(folder);
				Expect(back.Count == 3 && back[0].Rule == "" && back[1].Rule == TimeAttackLog.OpenRule && back[2].Rule == "REG-1234-ABCD" && back[2].RuleOk,
					"each time is saved with the rules it was driven to");
				Expect(back[0].IsSpec && !back[1].IsSpec && !back[2].IsSpec, "only TRC spec time attacks count for TRC");
				TimeAttackRecord bestSpec = TimeAttackLog.BestFor(back, "CRS-1", 7, "Revival", false, new TimeKind { Laps = 1, Rule = "" });
				TimeAttackRecord bestOpen = TimeAttackLog.BestFor(back, "CRS-1", 7, "Revival", false, new TimeKind { Laps = 1, Rule = TimeAttackLog.OpenRule });
				Expect(bestSpec?.TimeMs == 60000 && bestOpen?.TimeMs == 55000, "TRC spec and Open each have their own best (a faster Open time isn't a TRC spec PB)");
				Expect(TimeAttackTraces.Key("CRS-1", 7, "Revival", new TimeKind { Laps = 1 }) != TimeAttackTraces.Key("CRS-1", 7, "Revival", new TimeKind { Laps = 1, Rule = "open" })
					&& TimeAttackTraces.Key("CRS-1", 7, "Revival", new TimeKind { Laps = 1, Rule = "" }).EndsWith("standing1"),
					"Open times have their own best line, and TRC spec keeps its old file names");

				var trap = new SpeedTrapRecord { When = spec.When, TrapKey = "k", TrapName = "Bridge", Kmh = 201.5f, CarId = 7, Car = "AE86", Spec = false, Context = "freeroam", Rule = "REG-1234-ABCD" };
				SpeedTrapLog.Append(folder, trap);
				SpeedTrapRecord caught = SpeedTrapLog.Load(folder).Single();
				Expect(caught.CarId == 7 && caught.Car == "AE86" && caught.Rule == "REG-1234-ABCD", "a speed trap catch keeps the car and the rules");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
				}
			}
		}

		/// <summary>Nov 2, checkpoint signs: each sector split says how far ahead of (or behind) the PB the lap is there.</summary>
		private static void SplitsKnowTheGap() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 600) });
			double FirstGap(float speed, float[] reference) {
				var run = new TimeAttackRun(course) { Reference = reference };
				double now = 0;
				for (; now < 3; now += Frame) {
					run.Tick(now, new Vector3(0, 0, -4), Vector3.forward, 0f, 1f);
				}
				float z = -4f;
				for (; now < 60 && run.Active && !run.LastSplit.HasValue; now += Frame) {
					if (run.State == TimeAttackRun.Phase.Running) {
						z += speed * (float)Frame;
					}
					run.Tick(now, new Vector3(0, 0, z), Vector3.forward, run.State == TimeAttackRun.Phase.Running ? speed * 3.6f : 0f, 1f);
				}
				return run.LastSplit?.Delta ?? double.NaN;
			}
			// The PB went 20 m/s all the way: its time at every 10 m.
			float[] pb = Enumerable.Range(0, 62).Select(i => i * 10f / 20f).ToArray();
			double ahead = FirstGap(25f, pb), behind = FirstGap(15f, pb);
			Expect(ahead < -1 && behind > 1, $"a split is ahead (negative) when faster than the PB to there and behind when slower ({ahead:0.000}, {behind:+0.000})");
			Expect(double.IsNaN(FirstGap(25f, null)), "without a PB a split has no gap (the sign shows the sector time)");
		}

		/// <summary>bskftw, Nov 2: copies can skip numbers (".001", then ".003"): they're taken in order, ".001" first, ".003" next.</summary>
		private static void SkippedCopyNumbers() {
			string bare = CourseScanner.WithoutCopyNumber("TRC Main Road Okutama.003", out int copy);
			Expect(bare == "TRC Main Road Okutama" && copy == 3, "a road piece numbered .003 is a piece of the same road");

			SpeedTrap Trap() => new SpeedTrap { Name = "Akina Bridge" };
			var withPlain = new List<(string, int, SpeedTrap)> { ("Akina Bridge", 3, Trap()), ("Akina Bridge", 0, Trap()), ("Akina Bridge", 1, Trap()) };
			CourseScanner.NumberCopies(withPlain);
			Expect(withPlain[1].Item3.Name == "Akina Bridge" && withPlain[2].Item3.Name == "Akina Bridge 2" && withPlain[0].Item3.Name == "Akina Bridge 3",
				"speed trap copies are numbered in order, skipped numbers and all (plain, .001, .003 → 1, 2, 3)");
			var copiesOnly = new List<(string, int, SpeedTrap)> { ("Akina Bridge", 3, Trap()), ("Akina Bridge", 1, Trap()) };
			CourseScanner.NumberCopies(copiesOnly);
			Expect(copiesOnly[1].Item3.Name == "Akina Bridge 1" && copiesOnly[0].Item3.Name == "Akina Bridge 2", ".001 is 1 and .003 is 2 when there's no unnumbered one");
		}

		/// <summary>bskftw, Nov 2: a path that doesn't run far enough back for a full grid gets a note; races use what fits meanwhile.</summary>
		private static void ShortGridPathIsNoticed() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			Expect(course.GridWarning() == "" && course.GridRoom(GridLayout.Grid) == int.MaxValue, "no grid path: no note (the grid goes straight back)");
			course.GridPath = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, -30) };
			int two = course.GridRoom(GridLayout.Grid), line = course.GridRoom(GridLayout.LineUp);
			Expect(two == 7 && line == 4, $"a 30 m run-up fits 7 cars two-wide and 4 single file ({two}, {line})");
			string note = course.GridWarning();
			Expect(note.Contains("30 m") && note.Contains("64 m") && note.Contains("124 m"), $"the note says how far it runs and how far it should ({note})");
			List<GridSpot> grid = course.GridSpots(10, poleLeft: true, GridLayout.Grid);
			Expect(grid.Count == 10 && grid[9].Position.z < -30f, "a race still gets a spot for every car, the extra ones straight back from the path's end");
			course.GridPath = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, -130) };
			Expect(course.GridWarning() == "", "a run-up long enough for 16 single file gets no note");
		}

		/// <summary>A circuit without a grid path lines up along the end of its lap, round the last corner, not straight back.</summary>
		private static void CircuitGridFollowsTheLap() {
			var loop = new List<Vector3>();
			for (int i = 0; i <= 360; i += 3) {
				float a = i * Mathf.Deg2Rad;
				loop.Add(new Vector3(100f * Mathf.Cos(a), 0, 100f * Mathf.Sin(a)));
			}
			var course = new Course { Name = "Ring Circuit", MapKey = "test:1", Source = CourseSource.Custom, Kind = CourseKind.Circuit, Path = loop, HasPath = true };
			CourseGate.TryMake(loop[0], Vector3.forward, 12f, out course.Start);
			course.Finish = course.Start;
			course.Build();
			List<GridSpot> grid = course.GridSpots(12, poleLeft: true, GridLayout.LineUp);
			float worst = grid.Max(g => Mathf.Abs(new Vector2(g.Position.x, g.Position.z).magnitude - 100f));
			Expect(worst < 1f, $"every circuit grid spot is on the lap's road, round the bend ({worst:0.0} m off at most)");
		}

		private static void SideBySideIsClose() {
			foreach (float lineWidth in new[] { 8f, 12f, 24f }) {
				Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
				CourseGate.TryMake(course.Start.Center, course.Start.Forward, lineWidth, out course.Start);
				List<GridSpot> grid = course.GridSpots(2, poleLeft: true, GridLayout.Grid);
				float apart = Mathf.Abs(grid[0].Position.x - grid[1].Position.x);
				Expect(Mathf.Abs(apart - Course.SideBySideGap) < 0.01f, $"side by side, cars are {Course.SideBySideGap} m apart whatever the line's width ({lineWidth} m line: {apart:0.00} m)");
			}
		}

		private static void SingleFileLinesUp() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			Expect(!course.SingleFile, "a road that wasn't measured isn't single file");
			course.RoadWidth = 7.5f;
			Expect(!course.SingleFile, "a 7.5 m road fits two cars side by side");
			course.RoadWidth = 4.5f;
			Expect(course.SingleFile, "a 4.5 m road (Yabitsu) is single file");

			var settings = new RaceSettings { SingleFile = true, LayoutFromCourse = false, Layout = GridLayout.Grid };
			Expect(settings.GridShape == GridLayout.LineUp, "a single file race lines up in a line, even when the host picked a two-wide grid");
			List<GridSpot> grid = course.GridSpots(4, poleLeft: true, settings.GridShape);
			Expect(grid.All(g => Mathf.Abs(g.Position.x) < 0.01f) && grid[3].Position.z < grid[0].Position.z,
				$"single file, every car is in the middle of the road, one behind the other ({string.Join(", ", grid.Select(g => g.Position.z.ToString("0")))})");
			Expect(new RaceSettings { LayoutFromCourse = true }.GridShape == null && new RaceSettings { LayoutFromCourse = false, Layout = GridLayout.Grid }.GridShape == GridLayout.Grid,
				"on a wide road the grid is still the host's pick");

			var battle = BattleSession.Challenge(7, 1, "Host", 2, "Guest", course, BattleFormat.Race, false, () => null, 0);
			BattleMessage sent = battle.TakeOutbox().First(m => m.Kind == BattleKind.Challenge);
			BattleMessage heard = BattleMessage.Decode(sent.Encode());
			var guest = BattleSession.FromChallenge(heard, 2, "Guest", course, () => null, 0);
			Vector3 host = battle.MySpot(), behind = guest.MySpot();
			Expect(battle.SingleFile && guest.SingleFile && Mathf.Abs(host.x) < 0.01f && Mathf.Abs(behind.x) < 0.01f && behind.z < host.z - 5f,
				$"a 1v1 race on a narrow road: the challenger in front, the other driver behind, both in the middle ({host.z:0} and {behind.z:0} m)");

			course.RoadWidth = 9f;
			var wide = BattleSession.Challenge(8, 1, "Host", 2, "Guest", course, BattleFormat.Race, false, () => null, 0);
			Vector3 left = wide.MySpot();
			Expect(!wide.SingleFile && Mathf.Abs(Mathf.Abs(left.x) - Course.SideBySideGap / 2f) < 0.01f, $"a 1v1 race on a wide road is side by side, close together ({left.x:0.0} m from the middle)");
		}

		/// <summary>Two cars at the same speed, one lined up 8 m behind the other: from the line, their times are the same.</summary>
		private static void SingleFileClockStartsAtTheLine() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			double Time(float lineUpAt, bool fromLine) {
				var run = new TimeAttackRun(course) { AllowResets = true, ExternalStart = true, KeepGoing = true, ClockFromLine = fromLine };
				double now = 0;
				for (; now < 3; now += Frame) {
					run.Tick(now, new Vector3(0, 0, lineUpAt), Vector3.forward, 0f, 1f);
				}
				run.StartCountdown(now + 3.5);
				float z = lineUpAt;
				bool sawZero = false;
				for (; now < 90 && run.Active; now += Frame) {
					if (run.State == TimeAttackRun.Phase.Running) {
						z += 25f * (float)Frame;
						sawZero |= fromLine && z < -1f && run.Elapsed == 0;
					}
					run.Tick(now, new Vector3(0, 0, z), Vector3.forward, run.State == TimeAttackRun.Phase.Running ? 90f : 0f, 1f);
				}
				Expect(!fromLine || lineUpAt > -5f || sawZero, "single file, the clock shows 0 until the car reaches the line");
				return run.State == TimeAttackRun.Phase.Finished ? run.Result : -1;
			}
			double front = Time(-4f, true), back = Time(-12f, true);
			Expect(front > 0 && back > 0 && Math.Abs(front - back) < 0.05, $"single file, each clock starts at the line: lining up further back costs nothing ({front:0.000} s and {back:0.000} s)");
			double shared = Time(-12f, false);
			Expect(shared > back + 0.25, $"without it, the car further back loses the run-up to the line ({shared:0.000} s)");
		}

		private static void SingleFileTravels() {
			var challenge = new BattleMessage { Kind = BattleKind.Challenge, BattleId = 9, From = 1, To = 2, FromName = "bskftw", Format = BattleFormat.Race, CourseCode = "CRS-1", SingleFile = true };
			byte[] bytes = challenge.Encode();
			BattleMessage back = BattleMessage.Decode(bytes);
			BattleMessage older = BattleMessage.Decode(bytes.Take(bytes.Length - 1).ToArray());
			Expect(back != null && back.SingleFile && older != null && !older.SingleFile, "a challenge says it's single file, and one from an older copy reads without it");

			var lobby = new RaceMessage { Kind = RaceKind.Lobby, RaceId = 5, From = RaceHost, HostName = "Host", CourseCode = "CRS-1", Settings = new RaceSettings { SingleFile = true } };
			lobby.Entries.Add(new LobbyEntry { Id = RaceHost, Name = "Host" });
			RaceMessage heard = RaceMessage.Decode(lobby.Encode());
			Expect(heard != null && heard.Settings.SingleFile, "a race lobby says it's single file");
		}

		/// <summary>bskftw, Nov 2: "TRC Path Akina Circuit" is a loop: read round from the start line, nothing left behind it.</summary>
		private static void LoopedPathReadsRound() {
			var ring = new List<Vector3>();
			for (int i = 0; i < 360; i += 2) {
				float a = i * Mathf.Deg2Rad;
				// Both edges of a flat strip 0.2 m wide round a 150 m circle.
				ring.Add(new Vector3(150f * Mathf.Cos(a), 0, 150f * Mathf.Sin(a)));
				ring.Add(new Vector3(150.2f * Mathf.Cos(a), 0, 150.2f * Mathf.Sin(a)));
			}
			// The start at angle 0, driving anticlockwise (towards +z).
			List<Vector3> ordered = CourseGeometry.OrderPoints(ring, new Vector3(150f, 0, 0), Vector3.forward, 1.2f, 25f, out int unused, out List<Vector3> behind);
			Expect(unused == 0 && behind.Count <= 1 && ordered.Count > 150 && ordered[5].z > 0f && Vector3.Distance(ordered.Last(), new Vector3(150f, 0, 0)) < 10f,
				$"a looped path is read once round from the start line, the way the start faces ({ordered.Count} points, {behind.Count} behind, {unused} unused)");
		}
	}
}
