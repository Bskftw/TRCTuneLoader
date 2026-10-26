using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Checks of courses and time attack: simulated cars driven through TimeAttackRun, path ordering, files and the run log.</summary>
	internal static partial class SelfTest {
		private const double Frame = 1.0 / 60.0;

		private static void CourseTests() {
			StraightRunIsTimed();
			JumpStartIsCaught();
			WaitingHints();
			HairpinRunIsTimed();
			CuttingAcrossIsStopped();
			ResetIsStopped();
			EarlyFinishDoesntCount();
			StripVerticesAreOrdered();
			CourseFileRoundTrip();
			RecorderMakesACourse();
			RunLogRoundTrip();
			ProjectDreamUploadsAreOneMap();
			UphillIsDownhillReversed();
			OffRoadIsStopped();
			RecordedPathIsAdopted();
			CircuitTests();
			RaceTests();
			ModeTests();
			HomeTests();
		}

		private static void UphillIsDownhillReversed() {
			Course downhill = MakeCourse(Hairpin(300, 5, 1f));
			Course uphill = downhill.ReversedAs("Test Uphill");
			Expect(Vector3.Distance(uphill.Start.Center, downhill.Finish.Center) < 0.01f && Vector3.Distance(uphill.Finish.Center, downhill.Start.Center) < 0.01f,
				"an uphill course starts at the downhill finish and finishes at its start");
			Expect(Vector3.Dot(uphill.Start.Forward, downhill.Finish.Forward) < -0.99f, "an uphill course crosses its lines the other way");
			Expect(Math.Abs(uphill.Length - downhill.Length) < 0.01f && uphill.Code != downhill.Code, "an uphill course is as long as the downhill one, with its own code");

			// Drive it backwards: from 3 m behind the old finish, along the hairpin the other way.
			List<Vector3> back = Hairpin(300, 5, 1f);
			back.Reverse();
			TimeAttackRun run = Drive(uphill, back[0] - (back[1] - back[0]).normalized * 3f, Follow(back, 3f, 20f));
			Expect(run.State == TimeAttackRun.Phase.Finished, $"an uphill course can be driven ({run.State}: {run.Message})");
		}

		private static void OffRoadIsStopped() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			var run = new TimeAttackRun(course);
			double now = 0;
			for (; run.State != TimeAttackRun.Phase.Running; now += Frame) {
				run.Tick(now, new Vector3(0, 0, -3), Vector3.forward, 0f, 1f, onRoad: true);
			}
			// On the road (or in the air above it) for 2 s, then the whole car off it: still running after 0.3 s, stopped after 0.5 s.
			double offAt = now + 2;
			float z = -3;
			bool runningAfterABit = false;
			for (; run.State == TimeAttackRun.Phase.Running && now < offAt + 3; now += Frame, z += 0.3f) {
				run.Tick(now, new Vector3(0, 0, z), Vector3.forward, 65f, 1f, onRoad: now < offAt);
				if (now >= offAt + 0.3 && now < offAt + 0.3 + Frame) {
					runningAfterABit = run.State == TimeAttackRun.Phase.Running;
				}
			}
			Expect(runningAfterABit, "0.3 s with the whole car off the road is allowed");
			Expect(run.State == TimeAttackRun.Phase.Cancelled && run.Message.Contains("road") && now < offAt + TimeAttackRun.OffRoadSeconds + 0.1,
				$"0.5 s with the whole car off the road stops the run ({run.State}: {run.Message})");

			// Brief moments off the road, back on in between, never add up.
			var wobbly = new TimeAttackRun(course);
			now = 0;
			for (; wobbly.State != TimeAttackRun.Phase.Running; now += Frame) {
				wobbly.Tick(now, new Vector3(0, 0, -3), Vector3.forward, 0f, 1f, onRoad: true);
			}
			for (z = -3; now < 8; now += Frame, z += 0.3f) {
				wobbly.Tick(now, new Vector3(0, 0, z), Vector3.forward, 65f, 1f, onRoad: now % 1.0 > 0.4);
			}
			Expect(wobbly.State == TimeAttackRun.Phase.Running, "short moments off the road, each under 0.5 s, don't stop the run");
		}

		private static void RecordedPathIsAdopted() {
			// A map course with lines but no path, like TRC Start/Finish objects without a TRC Path.
			List<Vector3> road = Hairpin(200, 6, 0.5f);
			var mapCourse = new Course { Name = "Akina Downhill", MapKey = "test:pd", Source = CourseSource.Map };
			CourseGate.TryMake(road[0], Vector3.forward, 12f, out mapCourse.Start);
			CourseGate.TryMake(road[road.Count - 1], Vector3.back, 12f, out mapCourse.Finish);
			mapCourse.Build();
			Course uphill = mapCourse.ReversedAs("Akina Uphill");
			Expect(!mapCourse.HasPath, "a map course without a path object has no path");

			// Record path: drive through the start line, down the road, through the finish line.
			var recorder = new PathRecorder(mapCourse);
			recorder.Tick(road[0] - new Vector3(0, 0, 5));
			foreach (Vector3 point in road) {
				recorder.Tick(point);
			}
			recorder.Tick(road[road.Count - 1] + new Vector3(0, 0, -3));
			Expect(recorder.State == PathRecorder.Phase.Done, $"Record path stops by itself at the finish line ({recorder.State}: {recorder.Message})");

			string folder = Path.Combine(Path.GetTempPath(), "trc_codecheck_" + Guid.NewGuid().ToString("N"));
			try {
				CourseLibrary.SaveCustom(folder, recorder.Build("test"));
				var library = new CourseLibrary();
				library.LoadCustom(folder);
				library.SetMapCourses("test:pd", new List<Course> { mapCourse, uphill });
				List<Course> listed = library.ForMap("test:pd");
				float trueLength = CourseGeometry.Cumulative(road).Last();
				Expect(listed.Count == 2, $"the recorded path file isn't listed as a course of its own ({listed.Count} listed)");
				Expect(mapCourse.HasPath && mapCourse.PathFromPlayer && Math.Abs(mapCourse.Length - trueLength) / trueLength < 0.03f,
					$"the map course takes the recorded path ({mapCourse.Length:0} m of {trueLength:0} m)");
				Expect(uphill.HasPath && Math.Abs(uphill.Length - mapCourse.Length) < 0.5f, "the uphill twin takes the same path backwards");
				Expect(!mapCourse.Official, "a path recorded on this PC is practice only");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
					// Left in the temp folder: harmless.
				}
			}
		}

		private static void ProjectDreamUploadsAreOneMap() {
			var test = new MapInfo { Key = "workshop:3359213524", Name = "initialtrc" };
			var released = new MapInfo { Key = "workshop:3372162715", Name = "initialtrc" };
			var other = new MapInfo { Key = "workshop:3185598042", Name = "Maze Pass" };
			CourseLibrary.Recognize(test);
			CourseLibrary.Recognize(released);
			CourseLibrary.Recognize(other);
			Expect(test.Key == released.Key && test.UploadKey == "workshop:3359213524", "both Project Dream uploads share one map key");
			Expect(CourseLibrary.OfficialMaps.Contains(test.Key) && !CourseLibrary.OfficialMaps.Contains(other.Key), "Project Dream's map objects count as official, other maps' don't");
			Expect(other.Key == "workshop:3185598042" && other.UploadKey == "", "other maps keep their own key");
		}

		// ---- Courses to drive ----

		private static Course MakeCourse(List<Vector3> path) {
			var course = new Course { Name = "Test", MapKey = "test:1", Source = CourseSource.Custom, Path = path, HasPath = true };
			CourseGate.TryMake(path[0], path[1] - path[0], 12f, out course.Start);
			CourseGate.TryMake(path[path.Count - 1], path[path.Count - 1] - path[path.Count - 2], 12f, out course.Finish);
			course.Build();
			return course;
		}

		/// <summary>Up +z for <paramref name="leg"/> m, a half circle to the right, then back down -z: the finish is next to the start.</summary>
		private static List<Vector3> Hairpin(float leg, float radius, float step) {
			var points = new List<Vector3>();
			for (float z = 0; z <= leg; z += step) {
				points.Add(new Vector3(0, 0, z));
			}
			int arc = Mathf.CeilToInt(Mathf.PI * radius / step);
			for (int i = 1; i < arc; i++) {
				float angle = Mathf.PI - Mathf.PI * i / arc;
				points.Add(new Vector3(radius + radius * Mathf.Cos(angle), 0, leg + radius * Mathf.Sin(angle)));
			}
			for (float z = leg; z >= 0; z -= step) {
				points.Add(new Vector3(2 * radius, 0, z));
			}
			return points;
		}

		/// <summary>
		/// Drives a run: the car waits at <paramref name="grid"/> until GO, then follows <paramref name="route"/>
		/// (seconds after GO → position) until the run ends or <paramref name="maxSeconds"/> pass.
		/// </summary>
		/// <param name="speedKmh">The speed CarX would report (seconds after GO → km/h); by default worked out from the movement.</param>
		private static TimeAttackRun Drive(Course course, Vector3 grid, Func<double, Vector3> route, double maxSeconds = 120, Func<double, float> speedKmh = null) {
			var run = new TimeAttackRun(course);
			double go = -1;
			Vector3 last = grid;
			for (double now = 0; now < maxSeconds + 10 && run.State != TimeAttackRun.Phase.Finished && run.State != TimeAttackRun.Phase.Cancelled; now += Frame) {
				Vector3 position = go < 0 ? grid : route(now - go);
				float speed = go >= 0 && speedKmh != null ? speedKmh(now - go) : (float)((position - last).magnitude / Frame * 3.6);
				Vector3 forward = position - last;
				if (forward.sqrMagnitude < 1e-8f) {
					forward = course.Start.Forward;
				}
				run.Tick(now, position, forward, speed, 1f);
				last = position;
				if (go < 0 && run.State == TimeAttackRun.Phase.Running) {
					go = now - run.Elapsed;
				}
			}
			return run;
		}

		// ---- Tests ----

		private static void StraightRunIsTimed() {
			var path = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) };
			Course course = MakeCourse(path);
			Expect(Math.Abs(course.Length - 500f) < 0.01f, "a straight course is 500 m long");

			// Standing 3 m behind the line, then 5 m/s² from GO: the finish line is 503 m away.
			TimeAttackRun run = Drive(course, new Vector3(0, 0, -3), t => new Vector3(0, 0, -3 + (float)(0.5 * 5 * t * t)));
			double expected = Math.Sqrt(2 * 503 / 5.0);
			Expect(run.State == TimeAttackRun.Phase.Finished, $"a straight run finishes ({run.State}: {run.Message})");
			Expect(Math.Abs(run.Result - expected) < 0.002, $"the finish time is worked out between frames ({run.Result:0.0000} s, expected {expected:0.0000} s)");
			Expect(run.JumpStarts == 0, "no jump start on a clean start");
		}

		private static void JumpStartIsCaught() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			var run = new TimeAttackRun(course);
			var grid = new Vector3(0, 0, -3);
			double now = 0;
			for (; now < 1.5; now += Frame) {
				run.Tick(now, grid, Vector3.forward, 0f, 1f);
			}
			Expect(run.State == TimeAttackRun.Phase.Countdown, $"standing still behind the line starts the countdown ({run.State}: {run.Message})");
			run.Tick(now, grid + new Vector3(0, 0, 0.6f), Vector3.forward, 8f, 1f);
			Expect(run.State == TimeAttackRun.Phase.ToStart && run.JumpStarts == 1 && run.Message.Contains("Jump"), "moving during the countdown is a jump start");
		}

		private static void WaitingHints() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			var run = new TimeAttackRun(course);
			run.Tick(0, new Vector3(0, 0, 2), Vector3.forward, 0f, 1f);
			Expect(run.Message.Contains("Back up"), $"a car past the start line is told to back up ({run.Message})");
			run.Tick(0.1, new Vector3(0, 0, -3), Vector3.back, 0f, 1f);
			Expect(run.Message.Contains("Turn around"), $"a car facing the wrong way is told to turn around ({run.Message})");
			run.Tick(0.2, new Vector3(300, 0, 300), Vector3.forward, 50f, 1f);
			Expect(run.Message.Contains("Drive to the start"), $"a car far away is told to drive to the start ({run.Message})");
			for (double now = 0.3; now < 3; now += Frame) {
				run.Tick(now, new Vector3(0, 0, -3), Vector3.forward, 20f, 1f);
			}
			Expect(run.State == TimeAttackRun.Phase.ToStart, "a car rolling behind the line doesn't start the countdown");
		}

		/// <summary>Follows a path at a steady speed, starting <paramref name="behind"/> metres behind its first point.</summary>
		private static Func<double, Vector3> Follow(List<Vector3> path, float behind, float speed) {
			float[] distances = CourseGeometry.Cumulative(path);
			Vector3 back = (path[1] - path[0]).normalized;
			return t => {
				float along = (float)(speed * t) - behind;
				if (along < 0) {
					return path[0] + back * along;
				}
				for (int i = 1; i < path.Count; i++) {
					if (distances[i] >= along) {
						return Vector3.Lerp(path[i - 1], path[i], (along - distances[i - 1]) / (distances[i] - distances[i - 1]));
					}
				}
				Vector3 end = (path[path.Count - 1] - path[path.Count - 2]).normalized;
				return path[path.Count - 1] + end * (along - distances[distances.Length - 1]);
			};
		}

		private static void HairpinRunIsTimed() {
			List<Vector3> path = Hairpin(300, 5, 1f);
			Course course = MakeCourse(path);
			float length = CourseGeometry.Cumulative(path).Last();
			TimeAttackRun run = Drive(course, new Vector3(0, 0, -3), Follow(path, 3f, 20f));
			double expected = (length + 3) / 20.0;
			Expect(run.State == TimeAttackRun.Phase.Finished, $"a run round a hairpin finishes ({run.State}: {run.Message})");
			Expect(Math.Abs(run.Result - expected) < 0.05, $"the hairpin time is right ({run.Result:0.000} s, expected {expected:0.000} s)");
		}

		private static void CuttingAcrossIsStopped() {
			// Legs 10 m apart: cut straight across from one leg to the other near the top of the hairpin.
			List<Vector3> path = Hairpin(300, 5, 1f);
			Course course = MakeCourse(path);
			var cut = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 285), new Vector3(10, 0, 285), new Vector3(10, 0, -20) };
			TimeAttackRun run = Drive(course, new Vector3(0, 0, -3), Follow(cut, 3f, 20f));
			Expect(run.State == TimeAttackRun.Phase.Cancelled, $"cutting across a hairpin stops the run ({run.State}: {run.Message})");

			// Legs 30 m apart: the car ends up far from the road it was on.
			List<Vector3> wide = Hairpin(300, 15, 1f);
			Course wideCourse = MakeCourse(wide);
			var across = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 150), new Vector3(30, 0, 150), new Vector3(30, 0, -20) };
			TimeAttackRun wideRun = Drive(wideCourse, new Vector3(0, 0, -3), Follow(across, 3f, 20f));
			Expect(wideRun.State == TimeAttackRun.Phase.Cancelled, $"leaving the road for the other side stops the run ({wideRun.State}: {wideRun.Message})");
		}

		private static void ResetIsStopped() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 2000) });
			// CarX reports the car's real speed (72 km/h), not how far it was moved.
			TimeAttackRun run = Drive(course, new Vector3(0, 0, -3), t => new Vector3(0, 0, -3 + (float)(20 * t) + (t > 5 ? 300f : 0f)), speedKmh: t => 72f);
			Expect(run.State == TimeAttackRun.Phase.Cancelled && run.Message.Contains("reset"), $"a jump along the road (a reset) stops the run ({run.State}: {run.Message})");
		}

		private static void EarlyFinishDoesntCount() {
			// The finish line is 10 m to the right of the start, facing back down: drive up 20 m, cross over and come back through it.
			List<Vector3> path = Hairpin(300, 5, 1f);
			Course course = MakeCourse(path);
			var early = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 20), new Vector3(10, 0, 20), new Vector3(10, 0, -30) };
			TimeAttackRun run = Drive(course, new Vector3(0, 0, -3), Follow(early, 3f, 15f), maxSeconds: 8);
			Expect(run.State != TimeAttackRun.Phase.Finished, $"crossing the finish line before driving the course doesn't finish ({run.State})");
		}

		private static void StripVerticesAreOrdered() {
			// A thin strip down the middle of a hairpin road: two vertices 0.3 m apart every 3 m, in a jumbled order.
			List<Vector3> centre = Hairpin(300, 5, 3f);
			var vertices = new List<Vector3>();
			for (int i = 0; i < centre.Count; i++) {
				Vector3 along = (centre[Math.Min(i + 1, centre.Count - 1)] - centre[Math.Max(i - 1, 0)]).normalized;
				Vector3 side = new Vector3(along.z, 0, -along.x) * 0.15f;
				vertices.Add(centre[i] + side);
				vertices.Add(centre[i] - side);
			}
			var random = new System.Random(7);
			vertices = vertices.OrderBy(_ => random.Next()).ToList();

			List<Vector3> ordered = CourseGeometry.OrderPoints(vertices, centre[0], Vector3.forward, 1.2f, 25f, out int unused);
			float trueLength = CourseGeometry.Cumulative(centre).Last();
			float length = CourseGeometry.Cumulative(ordered).Last();
			Expect(unused == 0, $"every strip vertex is used ({unused} left over)");
			Expect(Math.Abs(length - trueLength) / trueLength < 0.03f, $"the ordered strip follows the hairpin ({length:0} m, the road is {trueLength:0} m)");
			Expect(Vector3.Distance(ordered[0], centre[0]) < 1f && Vector3.Distance(ordered[ordered.Count - 1], centre[centre.Count - 1]) < 1f,
				"the ordered strip runs from the start to the finish");
		}

		private static void CourseFileRoundTrip() {
			Course course = MakeCourse(Hairpin(300, 5, 1.37f));
			course.Name = "Akina, Downhill # test";
			course.MapName = "Project Dream";
			string text = CourseFile.ToText(course);
			Course back = CourseFile.Parse(text, CourseSource.Custom);
			Expect(back.Code == course.Code, $"a saved course reads back with the same code ({course.Code} / {back.Code})");
			Expect(Math.Abs(back.Length - course.Length) < 0.01f && back.Path.Count == course.Path.Count, "a saved course reads back with the same path");
			Expect(back.Problems.Count == 0, $"a saved course reads back without problems ({string.Join("; ", back.Problems)})");
			Expect(back.Name == "Akina, Downhill  test", $"'#' is taken out of names ({back.Name})");

			Course moved = CourseFile.Parse(text.Replace("[finish]\nposition  = ", "[finish]\nposition  = 1"), CourseSource.Custom);
			Expect(moved.Code != course.Code, "moving the finish changes the course code");
		}

		private static void RecorderMakesACourse() {
			var recorder = new CourseRecorder();
			List<Vector3> road = Hairpin(200, 8, 0.5f);
			Expect(recorder.SetStart(road[0], Vector3.forward, 12f, out _), "the course tool sets a start");
			foreach (Vector3 point in road) {
				recorder.Tick(point);
			}
			Expect(recorder.SetFinish(road[road.Count - 1], Vector3.back, 12f, out string error), $"the course tool sets a finish ({error})");
			Course course = recorder.Build("Tool test", CourseKind.Touge, "test:1", "Test map", "test");
			float trueLength = CourseGeometry.Cumulative(road).Last();
			Expect(course.Usable && Math.Abs(course.Length - trueLength) / trueLength < 0.02f, $"a recorded course follows the route ({course.Length:0} m of {trueLength:0} m)");

			TimeAttackRun run = Drive(course, road[0] - new Vector3(0, 0, 3), Follow(road, 3f, 25f));
			Expect(run.State == TimeAttackRun.Phase.Finished, $"a recorded course can be driven ({run.State}: {run.Message})");

			var jumpy = new CourseRecorder();
			jumpy.SetStart(Vector3.zero, Vector3.forward, 12f, out _);
			jumpy.Tick(new Vector3(0, 0, 1));
			jumpy.Tick(new Vector3(0, 0, 200));
			Expect(jumpy.State == CourseRecorder.Phase.Idle && jumpy.Problem.Length > 0, "a reset while recording loses the route");
		}

		private static void RunLogRoundTrip() {
			string folder = Path.Combine(Path.GetTempPath(), "trc_codecheck_" + Guid.NewGuid().ToString("N"));
			try {
				TimeAttackLog.Append(folder, new TimeAttackRecord {
					When = new DateTime(2026, 10, 22, 21, 0, 5), CourseCode = "CRS-0000-0001", CourseName = "Akina, \"Downhill\"", MapKey = "workshop:1",
					MapName = "Project Dream", CarId = 3, Car = "Toyota AE86", Setup = "Ultimate", TuneCode = "TRC-95F4-06D9", TuneSet = "TRC Revival",
					Spec = TimeAttackLog.SpecWord, TimeMs = 192345, LoaderVersion = "0.10.0",
				});
				TimeAttackLog.Append(folder, new TimeAttackRecord {
					When = new DateTime(2026, 10, 22, 21, 5, 0), CourseCode = "CRS-0000-0001", CourseName = "Akina, \"Downhill\"", MapKey = "workshop:1",
					Spec = "not spec", TimeMs = 180000,
				});
				TimeAttackLog.Append(folder, new TimeAttackRecord {
					When = new DateTime(2026, 10, 22, 21, 10, 0), CourseCode = "CRS-0000-0001", CourseName = "Akina", MapKey = "workshop:1",
					CarId = 3, Car = "Toyota AE86", TuneSet = "TRC V3.0", Spec = TimeAttackLog.SpecWord, TimeMs = 185000,
				});
				List<TimeAttackRecord> records = TimeAttackLog.Load(folder);
				Expect(records.Count == 3 && records[0].CourseName == "Akina, \"Downhill\"" && records[0].TimeMs == 192345, "the run log reads back what it wrote");
				Expect(TimeAttackLog.BestSpec(records, "CRS-0000-0001")?.TimeMs == 185000, "the best TRC spec time skips faster runs that weren't spec");
				Expect(TimeAttackLog.Best(records, "CRS-0000-0001")?.TimeMs == 180000, "the best time of all includes runs that weren't spec");
				Expect(TimeAttackLog.BestFor(records, "CRS-0000-0001", 3, "TRC Revival", specOnly: true)?.TimeMs == 192345 &&
					TimeAttackLog.BestFor(records, "CRS-0000-0001", 3, "TRC V3.0", specOnly: true)?.TimeMs == 185000,
					"the same car on another TRC tune set has its own best time");
				Expect(TimeAttackRun.FormatTime(192.3454) == "3:12.345" && TimeAttackRun.FormatTime(9.5) == "9.500", "times print as m:ss.fff");
				Expect(records[0].Laps == 1 && !records[0].Flying && !records[0].IsLap, "a run from before laps reads as a standing point-to-point run");

				// Laps and sectors (0.11): a flying lap of a 5-lap run, and two laps whose best sectors make a faster theoretical lap.
				TimeAttackLog.Append(folder, new TimeAttackRecord {
					When = new DateTime(2026, 10, 24, 12, 0, 0), CourseCode = "CRS-0000-0002", CourseName = "Ring", CarId = 3, TuneSet = "TRC Revival",
					Spec = TimeAttackLog.SpecWord, TimeMs = 60000, Flying = true, Laps = 5, LapNumber = 2, SectorMs = new long[] { 19000, 21000, 20000 },
				});
				TimeAttackLog.Append(folder, new TimeAttackRecord {
					When = new DateTime(2026, 10, 24, 12, 1, 0), CourseCode = "CRS-0000-0002", CourseName = "Ring", CarId = 3, TuneSet = "TRC Revival",
					Spec = TimeAttackLog.SpecWord, TimeMs = 60500, Flying = true, Laps = 5, LapNumber = 3, SectorMs = new long[] { 20000, 19500, 21000 },
				});
				TimeAttackLog.Append(folder, new TimeAttackRecord {
					When = new DateTime(2026, 10, 24, 12, 5, 0), CourseCode = "CRS-0000-0002", CourseName = "Ring", CarId = 3, TuneSet = "TRC Revival",
					Spec = TimeAttackLog.SpecWord, TimeMs = 305000, Laps = 5,
				});
				records = TimeAttackLog.Load(folder);
				TimeAttackRecord lap = records.FirstOrDefault(r => r.LapNumber == 2);
				Expect(records.Count == 6 && lap != null && lap.Flying && lap.Laps == 5 && lap.SectorMs.SequenceEqual(new long[] { 19000, 21000, 20000 }),
					"laps come back with their lap number, start and sectors");
				var lapKind = new TimeKind { Lap = true, Flying = true };
				Expect(TimeAttackLog.BestFor(records, "CRS-0000-0002", 3, "TRC Revival", true, lapKind)?.TimeMs == 60000 &&
					TimeAttackLog.BestFor(records, "CRS-0000-0002", 3, "TRC Revival", true, new TimeKind { Laps = 5 })?.TimeMs == 305000,
					"best laps and best 5-lap runs are kept apart");
				Expect(TimeAttackLog.TheoreticalBestMs(records, "CRS-0000-0002", 3, "TRC Revival", lapKind, 3) == 19000 + 19500 + 20000,
					"the theoretical best adds up the best of each sector");
				Expect(TimeAttackLog.BestSpec(records, "CRS-0000-0002")?.TimeMs == 60000, "a circuit's headline time is its best lap");

				string key = TimeAttackTraces.Key("CRS-0000-0002", 3, "TRC Revival", lapKind);
				bool first = TimeAttackTraces.SaveIfFaster(folder, key, new[] { 0f, 1.5f, 3.25f }, 60000);
				bool slower = TimeAttackTraces.SaveIfFaster(folder, key, new[] { 0f, 1.6f, 3.4f }, 61000);
				var saved = TimeAttackTraces.Load(folder, key);
				Expect(first && !slower && saved != null && saved.Value.TimeMs == 60000 && saved.Value.Trace.SequenceEqual(new[] { 0f, 1.5f, 3.25f }),
					"the delta's best lap is kept until a faster one comes");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
					// Left in the temp folder: harmless.
				}
			}
		}
	}
}
