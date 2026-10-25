using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Checks of circuits, laps, flying starts, sectors, the live delta, practice laps and battle resets.</summary>
	internal static partial class SelfTest {
		private const float CircleRadius = 80f;

		private static void CircuitTests() {
			CircuitLapsAreTimed();
			FlyingStartStartsAtTheLine();
			SectorsSplitEachLap();
			DeltaComparesWithTheBestLap();
			PracticeSpoilsOnlyTheLap();
			BattleRunsSurviveResetsBack();
			CircuitFileRoundTrip();
			SectorObjectsSetTheSectors();
			ReversedCircuitIsItsOwnCourse();
			BuilderClosesTheLoop();
			OtherPlayersValuesStayHidden();
		}

		private static void OtherPlayersValuesStayHidden() {
			string[] lines = {
				"Front spring stiffness: 120 N/mm (official 100 N/mm)",
				"Tyre pressure rear: 1.9 bar [0.4521] (official 2.1 bar [0.5])",
				"Stance: ride height at the front left wheel +12 mm (380 mm, stock 368 mm)",
				"Stance: track width front -8 mm (1520 mm, stock 1528 mm)",
				"Live frontSuspension.stiffness: 152287 (expected 39700.63)",
				"Final drive: 3.9 (CarX 4.11 with the tune)",
			};
			string[] hidden = lines.Select(TuneCheck.WithoutValues).ToArray();
			Expect(hidden.All(h => !h.Any(char.IsDigit)), $"another player's differences show no numbers ({string.Join(" | ", hidden)})");
			Expect(hidden[0] == "Front spring stiffness: changed" && hidden[2] == "Stance: ride height at the front left wheel changed" &&
				hidden[3] == "Stance: track width front changed" && hidden[4] == "Live frontSuspension.stiffness: changed",
				$"they still say what's changed ({hidden[0]} / {hidden[2]} / {hidden[3]})");
			Expect(TuneCheck.WithoutValues("Gear 7 ratio: not on this car") == "Gear 7 ratio: not on this car" &&
				TuneCheck.WithoutValues("Body kit part that moves the wheels: Rocket Bunny (body)") == "Body kit part that moves the wheels: Rocket Bunny (body)",
				"lines without values stay as they are");
		}

		private static void BuilderClosesTheLoop() {
			var recorder = new CourseRecorder();
			recorder.SetStart(OnCircle(0f), Vector3.forward, 12f, out _, circuit: true);
			bool closedEarly = false;
			float lap = 2f * Mathf.PI * CircleRadius;
			for (float m = 0.4f; m < lap + 20f && recorder.State == CourseRecorder.Phase.Recording; m += 0.4f) {
				recorder.Tick(OnCircle(m));
				closedEarly |= recorder.State == CourseRecorder.Phase.Done && m < lap - 5f;
			}
			Expect(recorder.State == CourseRecorder.Phase.Done && !closedEarly, $"the course builder closes a circuit by itself back on the start line ({recorder.State})");
			Expect(!recorder.SetFinish(OnCircle(10f), Vector3.forward, 12f, out string error) && error.Length > 0, "a circuit has no finish to set");
			Course built = recorder.Build("Built ring", CourseKind.Touge, "test:1", "Test", "test");
			Expect(built.IsCircuit && built.Usable && Math.Abs(built.Length - lap) < 3f, $"the recorded loop is a usable circuit ({built.Length:0} m)");
		}

		/// <summary>A round circuit, 80 m across the middle: the start line at (80, 0, 0), driven anticlockwise seen from above (+z first).</summary>
		private static Course MakeCircuit() {
			var points = new List<Vector3>();
			for (int i = 0; i <= 360; i++) {
				points.Add(OnCircle(2f * Mathf.PI * CircleRadius * i / 360f));
			}
			var course = new Course { Name = "Ring", Kind = CourseKind.Circuit, MapKey = "test:1", Source = CourseSource.Custom, Path = points, HasPath = true };
			CourseGate.TryMake(points[0], Vector3.forward, 12f, out course.Start);
			course.Build();
			return course;
		}

		/// <summary>The point <paramref name="metres"/> round the circle from the start line (any number of laps; negative is before it).</summary>
		private static Vector3 OnCircle(float metres) {
			float angle = metres / CircleRadius;
			return new Vector3(CircleRadius * Mathf.Cos(angle), 0f, CircleRadius * Mathf.Sin(angle));
		}

		/// <summary>
		/// Drives <paramref name="run"/> round the circle: standing on the grid until GO (or, flying, from
		/// <paramref name="before"/> metres before the line), then <paramref name="metresAt"/> (seconds since moving → metres from the line).
		/// <paramref name="onFrame"/> sees the run every frame and may move the car (returns extra metres to add).
		/// </summary>
		private static void DriveCircle(TimeAttackRun run, Func<double, float> metresAt, double maxSeconds = 200, float before = 0f, Func<TimeAttackRun, float> onFrame = null) {
			Vector3 grid = run.Course.Start.Center - run.Course.Start.Forward * 3f;
			double moving = run.Flying ? 0 : -1;
			float extra = 0f;
			Vector3 last = run.Flying ? OnCircle(-before) : grid;
			for (double now = 0; now < maxSeconds && run.Active; now += Frame) {
				if (moving < 0 && run.State == TimeAttackRun.Phase.Running) {
					moving = now - run.Elapsed;
				}
				Vector3 position = moving < 0 ? grid : OnCircle(metresAt(now - moving) - before + extra);
				Vector3 forward = position - last;
				if (forward.sqrMagnitude < 1e-8f) {
					forward = run.Course.Start.Forward;
				}
				// The speed CarX reports doesn't jump when the car is reset, so a jump is never taken for driving.
				float speed = Mathf.Min((float)((position - last).magnitude / Frame * 3.6), 150f);
				run.Tick(now, position, forward, speed, 1f);
				last = position;
				if (onFrame != null) {
					extra += onFrame(run);
				}
			}
		}

		/// <summary>5 m/s² from a standstill up to 25 m/s, then steady.</summary>
		private static float Accelerate(double t) {
			const double a = 5, top = 25;
			double reach = top / a;
			return (float)(t < reach ? 0.5 * a * t * t : 0.5 * a * reach * reach + top * (t - reach));
		}

		private static void CircuitLapsAreTimed() {
			Course course = MakeCircuit();
			Expect(course.IsCircuit && Math.Abs(course.Length - 2 * Mathf.PI * CircleRadius) < 1f && course.Usable,
				$"a circuit is a usable loop as long as once round ({course.Length:0} m)");
			Expect(Vector3.Distance(course.Finish.Center, course.Start.Center) < 0.01f && Vector3.Dot(course.Finish.Forward, course.Start.Forward) > 0.99f,
				"a circuit's finish line is its start line");

			var run = new TimeAttackRun(course, laps: 3);
			DriveCircle(run, Accelerate);
			double steadyLap = course.Length / 25.0;
			Expect(run.State == TimeAttackRun.Phase.Finished && run.LapTimes.Count == 3, $"a 3-lap circuit run finishes after 3 laps ({run.State}: {run.Message}, {run.LapTimes.Count} laps)");
			Expect(run.LapTimes.Count == 3 && Math.Abs(run.LapTimes.Sum(l => l.Time) - run.Result) < 0.001,
				$"the laps add up to the total time ({run.LapTimes.Sum(l => l.Time):0.000} / {run.Result:0.000})");
			Expect(run.LapTimes.Count == 3 && run.LapTimes[0].Standing && !run.LapTimes[1].Standing &&
				Math.Abs(run.LapTimes[1].Time - steadyLap) < 0.01 && Math.Abs(run.LapTimes[2].Time - steadyLap) < 0.01 && run.LapTimes[0].Time > steadyLap,
				$"laps 2 and 3 are flying laps at steady speed, lap 1 is slower from the standing start ({string.Join(" / ", run.LapTimes.Select(l => l.Time.ToString("0.000")))}, expected {steadyLap:0.000})");
			Expect(run.BestLap == run.LapTimes.OrderBy(l => l.Time).FirstOrDefault(), "the best lap is the fastest lap");
		}

		private static void FlyingStartStartsAtTheLine() {
			Course course = MakeCircuit();
			var run = new TimeAttackRun(course, laps: 1, flying: true);
			Expect(run.Message.Contains("start line"), $"a flying start asks to drive through the start line ({run.Message})");
			DriveCircle(run, t => (float)(25 * t), before: 60f);
			double lap = course.Length / 25.0;
			Expect(run.State == TimeAttackRun.Phase.Finished && Math.Abs(run.Result - lap) < 0.002,
				$"a flying lap is timed from crossing the start line ({run.Result:0.000} s, expected {lap:0.000})");
			Expect(run.JumpStarts == 0 && run.LapTimes.Count == 1 && !run.LapTimes[0].Standing, "a flying start has no countdown or jump start");

			// Point to point too: from 40 m back at 30 m/s, 500 m to the finish.
			Course straight = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			var p2p = new TimeAttackRun(straight, flying: true);
			for (double now = 0; now < 30 && p2p.Active; now += Frame) {
				p2p.Tick(now, new Vector3(0, 0, -40f + 30f * (float)now), Vector3.forward, 108f, 1f);
			}
			Expect(p2p.State == TimeAttackRun.Phase.Finished && Math.Abs(p2p.Result - 500 / 30.0) < 0.002,
				$"a flying start works on a point-to-point course ({p2p.Result:0.000} s, expected {500 / 30.0:0.000})");
		}

		private static void SectorsSplitEachLap() {
			Course course = MakeCircuit();
			Expect(course.SectorEnds.Length == 3 && Math.Abs(course.SectorEnds[2] - course.Length) < 0.01f, $"a short course has 3 sectors ({course.SectorEnds.Length})");
			Course longOne = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 9000) });
			Expect(longOne.SectorEnds.Length == 5, $"a 9 km course has 5 sectors ({longOne.SectorEnds.Length})");

			var run = new TimeAttackRun(course, laps: 2, flying: true);
			var splits = new List<SectorSplit>();
			DriveCircle(run, t => (float)(25 * t), before: 30f, onFrame: r => {
				if (r.LastSplit.HasValue && (splits.Count == 0 || !splits[splits.Count - 1].Equals(r.LastSplit.Value))) {
					splits.Add(r.LastSplit.Value);
				}
				return 0f;
			});
			double third = course.Length / 3.0 / 25.0;
			bool even = run.LapTimes.Count == 2 && run.LapTimes.All(l => l.Sectors.Length == 3 && l.Sectors.All(s => Math.Abs(s - third) < 0.005) &&
				Math.Abs(l.Sectors.Sum() - l.Time) < 0.001);
			Expect(even, $"every lap has 3 sectors adding up to the lap ({string.Join(" | ", run.LapTimes.Select(l => string.Join(" ", l.Sectors.Select(s => s.ToString("0.000")))))}, expected {third:0.000} each)");
			Expect(splits.Count == 6 && splits.Select(s => s.Index).SequenceEqual(new[] { 0, 1, 2, 0, 1, 2 }), $"each sector is split as it ends, every lap ({splits.Count} splits)");

			var strip = new TimeAttackRun(course, laps: 2, flying: true);
			int midLap = -1;
			DriveCircle(strip, t => (float)(25 * t), before: 30f, onFrame: r => {
				if (midLap < 0 && r.Lap == 2 && r.Progress > course.Length * 0.5f) {
					midLap = r.CurrentSectors.Count;
				}
				return 0f;
			});
			Expect(midLap == 1 && strip.CurrentSectors.Count == 3, $"the sector strip holds this lap's finished sectors ({midLap} halfway round lap 2, {strip.CurrentSectors.Count} at the end)");
		}

		private static void DeltaComparesWithTheBestLap() {
			Course course = MakeCircuit();
			var run = new TimeAttackRun(course, laps: 2, flying: true);
			double? halfway = null;
			// Lap 1 at 25 m/s, lap 2 at 24 m/s: halfway round lap 2, it's (L/2)/24 - (L/2)/25 behind.
			float lap = course.Length;
			DriveCircle(run, t => {
				double first = lap / 25.0;
				return (float)(t < first ? 25 * t : lap + 24 * (t - first));
			}, before: 30f, onFrame: r => {
				if (r.Reference == null && r.LapTimes.Count == 1) {
					r.Reference = r.LapTimes[0].Trace;
				}
				if (r.Lap == 2 && halfway == null && r.Progress >= lap / 2f) {
					halfway = r.Delta;
				}
				return 0f;
			});
			double expected = lap / 2.0 / 24.0 - lap / 2.0 / 25.0;
			Expect(halfway.HasValue && Math.Abs(halfway.Value - expected) < 0.03,
				$"the live delta shows how far behind the best lap this lap is ({(halfway.HasValue ? TimeAttackRun.FormatGap(halfway.Value) : "none")}, expected {TimeAttackRun.FormatGap(expected)})");
			Expect(run.LapTimes.Count == 2 && run.LapTimes[0].Trace.Length >= (int)(lap / TimeAttackRun.TraceStep), "a lap keeps its time every 10 m for the delta");
		}

		private static void PracticeSpoilsOnlyTheLap() {
			Course course = MakeCircuit();
			var run = new TimeAttackRun(course, laps: 0, flying: true);
			Expect(run.Practice, "unlimited laps on a circuit is practice");
			bool cut = false;
			// In lap 2, a third of the way round, the car jumps 60 m ahead (a reset across the infield).
			DriveCircle(run, t => (float)(25 * t), maxSeconds: 4.5 * course.Length / 25.0, before: 30f, onFrame: r => {
				if (!cut && r.Lap == 2 && r.Progress > course.Length / 3f) {
					cut = true;
					return 60f;
				}
				return 0f;
			});
			Expect(run.Active && run.LapTimes.Count >= 3, $"a practice goes on after a spoiled lap ({run.State}: {run.Message}, {run.LapTimes.Count} laps)");
			Expect(run.LapTimes.Count >= 3 && run.LapTimes[0].Valid && !run.LapTimes[1].Valid && run.LapTimes[2].Valid && run.LapTimes[1].Invalid.Length > 0,
				$"only the spoiled lap is invalid ({string.Join(" / ", run.LapTimes.Select(l => l.Valid ? "ok" : l.Invalid))})");
			Expect(run.BestLap != null && run.BestLap.Valid, "the best lap is a valid one");

			var fixedLaps = new TimeAttackRun(course, laps: 3, flying: true);
			bool cut2 = false;
			DriveCircle(fixedLaps, t => (float)(25 * t), before: 30f, onFrame: r => {
				if (!cut2 && r.Lap == 2 && r.Progress > course.Length / 3f) {
					cut2 = true;
					return 60f;
				}
				return 0f;
			});
			Expect(fixedLaps.State == TimeAttackRun.Phase.Cancelled, $"with a set number of laps, the same reset stops the run ({fixedLaps.State})");
		}

		private static void BattleRunsSurviveResetsBack() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			TimeAttackRun Battle(float jump) {
				var run = new TimeAttackRun(course) { AllowResets = true, ExternalStart = true };
				double now = 0;
				for (; now < 3; now += Frame) {
					run.Tick(now, new Vector3(0, 0, -4), Vector3.forward, 0f, 1f);
				}
				run.StartCountdown(now + 3.5);
				float z = -4f;
				bool jumped = false;
				for (; now < 60 && run.Active; now += Frame) {
					if (run.State == TimeAttackRun.Phase.Running) {
						z += 25f * (float)Frame;
						if (!jumped && z > 200f) {
							jumped = true;
							z += jump;
						}
					}
					run.Tick(now, new Vector3(0, 0, z), Vector3.forward, run.State == TimeAttackRun.Phase.Running ? 90f : 0f, 1f);
				}
				return run;
			}
			TimeAttackRun back = Battle(-40f);
			Expect(back.State == TimeAttackRun.Phase.Finished && back.Resets == 1, $"in a battle, a reset back along the course doesn't stop the run ({back.State}: {back.Message})");
			TimeAttackRun ahead = Battle(60f);
			Expect(ahead.State == TimeAttackRun.Phase.Cancelled && ahead.Message.Contains("further along"),
				$"a reset that puts the car further along the course still stops it ({ahead.State}: {ahead.Message})");
		}

		private static void CircuitFileRoundTrip() {
			Course course = MakeCircuit();
			string text = CourseFile.ToText(course);
			Course back = CourseFile.Parse(text, CourseSource.Custom);
			Expect(text.Contains("kind     = circuit") && !text.Contains("[finish]"), "a circuit file says circuit and has no [finish]");
			Expect(back.IsCircuit && back.Code == course.Code && back.Problems.Count == 0 && Math.Abs(back.Length - course.Length) < 0.01f,
				$"a circuit file reads back as the same circuit ({back.Code} / {course.Code}; {string.Join("; ", back.Problems)})");
		}

		private static void SectorObjectsSetTheSectors() {
			var path = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) };
			var course = new Course { Name = "Marked", MapKey = "test:1", Source = CourseSource.Map, Path = path, HasPath = true };
			CourseGate.TryMake(path[0], Vector3.forward, 12f, out course.Start);
			CourseGate.TryMake(path[1], Vector3.forward, 12f, out course.Finish);
			course.SectorPoints = new List<Vector3> { new Vector3(3, 0, 350), new Vector3(-2, 0, 100) };
			course.Build();
			Expect(course.SectorEnds.Length == 3 && Math.Abs(course.SectorEnds[0] - 100) < 0.5f && Math.Abs(course.SectorEnds[1] - 350) < 0.5f,
				$"sector objects set where sectors end, in course order ({string.Join(", ", course.SectorEnds.Select(e => e.ToString("0")))})");
			Course uphill = course.ReversedAs("Marked Uphill");
			Expect(uphill.SectorEnds.Length == 3 && Math.Abs(uphill.SectorEnds[0] - 150) < 0.5f && Math.Abs(uphill.SectorEnds[1] - 400) < 0.5f,
				$"the reversed course has the same sector points, the other way round ({string.Join(", ", uphill.SectorEnds.Select(e => e.ToString("0")))})");
		}

		private static void ReversedCircuitIsItsOwnCourse() {
			Course ring = MakeCircuit();
			Course reverse = ring.ReversedAs("Ring Reverse");
			Expect(reverse.IsCircuit && reverse.Code != ring.Code && Vector3.Distance(reverse.Start.Center, ring.Start.Center) < 0.01f &&
				Vector3.Dot(reverse.Start.Forward, ring.Start.Forward) < -0.99f, "a reverse circuit starts on the same line, the other way");
			Expect(!reverse.SameLines(ring) && reverse.ReversedLinesOf(ring) && ring.SameLines(MakeCircuit()),
				"a reverse circuit isn't taken for the forward one (so it never borrows its path the wrong way round)");

			var run = new TimeAttackRun(reverse, laps: 1, flying: true);
			for (double now = 0; now < 40 && run.Active; now += Frame) {
				run.Tick(now, OnCircle(30f - 25f * (float)now), Vector3.back, 90f, 1f);
			}
			Expect(run.State == TimeAttackRun.Phase.Finished, $"a reverse circuit can be driven round the other way ({run.State}: {run.Message})");
		}
	}
}
