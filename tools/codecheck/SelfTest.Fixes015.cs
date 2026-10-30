using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>0.15.0: the delta on a course without a path, speed trap names, deleting times, a race's traps on the wire.</summary>
	internal static partial class SelfTest {
		private static void Fixes015Tests() {
			DeltaFollowsTheBestLine();
			TrapNamesGroup();
			TimesCanBeDeleted();
			RaceCourseCarriesTraps();
		}

		/// <summary>The point <paramref name="metres"/> along a path (before its start: straight back from it).</summary>
		private static Vector3 Along(List<Vector3> path, float metres) {
			if (metres <= 0) {
				return path[0] + (path[0] - path[1]).normalized * -metres;
			}
			for (int i = 1; i < path.Count; i++) {
				float step = Vector3.Distance(path[i - 1], path[i]);
				if (metres <= step) {
					return Vector3.Lerp(path[i - 1], path[i], metres / step);
				}
				metres -= step;
			}
			return path[path.Count - 1] + (path[path.Count - 1] - path[path.Count - 2]).normalized * metres;
		}

		/// <summary>
		/// Oct 29: on a map course nobody had recorded a path for, the delta jumped to +50 s in hairpins (it measured
		/// along a straight start-to-finish line). Driving the best run's line at the best run's speed, it stays near 0.
		/// </summary>
		private static void DeltaFollowsTheBestLine() {
			List<Vector3> road = Hairpin(200, 8, 2f);
			var course = new Course { Name = "No path", MapKey = "test:1", Source = CourseSource.Map, HasPath = false };
			CourseGate.TryMake(road[0], road[1] - road[0], 12f, out course.Start);
			CourseGate.TryMake(road[road.Count - 1], road[road.Count - 1] - road[road.Count - 2], 12f, out course.Finish);
			course.Build();
			float length = CourseGeometry.Cumulative(road).Last();
			const float speed = 20f;
			var line = new List<Vector3>();
			var kmh = new List<float>();
			for (float s = 0; s <= length; s += TimeAttackRun.LineStep) {
				line.Add(Along(road, s));
				kmh.Add(speed * 3.6f);
			}
			var run = new TimeAttackRun(course, 1, flying: true);
			run.SetReferenceLine(line, kmh, length / speed);
			double worst = 0;
			bool sawDelta = false;
			for (double t = 0; t < 40 && run.State != TimeAttackRun.Phase.Finished && run.State != TimeAttackRun.Phase.Cancelled; t += Frame) {
				float s = (float)(t * speed) - 20f;
				Vector3 at = Along(road, s);
				Vector3 forward = Along(road, s + 1f) - at;
				run.Tick(t, at, forward, speed * 3.6f, 1f);
				if (run.Delta.HasValue) {
					sawDelta = true;
					worst = Math.Max(worst, Math.Abs(run.Delta.Value));
				}
			}
			Expect(sawDelta && worst < 0.3, $"without a path, the delta follows the best run's line, through the hairpin too (worst {worst:0.00} s)");
		}

		private static void TrapNamesGroup() {
			Expect(SpeedTrap.GroupOf("Tsubaki Speed Trap 3") == "Tsubaki" && SpeedTrap.GroupOf("Tsubaki speed traps #12") == "Tsubaki" && SpeedTrap.GroupOf("Akina Bridge") == "Akina Bridge",
				$"speed traps group by the name before \"Speed Trap 3\" ({SpeedTrap.GroupOf("Tsubaki Speed Trap 3")})");
			Expect(SpeedTrap.GroupOf("Speed trap 2") == "" && SpeedTrap.GroupOf("7") == "", "a name that's only \"Speed trap 2\" has no group");
			Expect(SpeedTrap.NextName("Tsubaki Speed Trap 3", 5) == "Tsubaki Speed Trap 4" && SpeedTrap.NextName("", 2) == "Speed trap 3",
				$"the next trap gets the next number ({SpeedTrap.NextName("Tsubaki Speed Trap 3", 5)})");
		}

		private static void TimesCanBeDeleted() {
			string folder = Path.Combine(Path.GetTempPath(), "trc_delete_" + Guid.NewGuid().ToString("N"));
			try {
				var when = new DateTime(2026, 10, 29, 20, 0, 0);
				for (int i = 0; i < 3; i++) {
					TimeAttackLog.Append(folder, new TimeAttackRecord {
						When = when.AddMinutes(i), CourseCode = i == 2 ? "CRS-BBBB-BBBB" : "CRS-AAAA-AAAA", CourseName = "Test", CarId = 6, Car = "Car",
						TimeMs = 60000 + i, Spec = "spec", Laps = 1,
					});
				}
				List<TimeAttackRecord> all = TimeAttackLog.Load(folder);
				TimeAttackRecord first = all[0];
				int one = TimeAttackLog.Delete(folder, r => TimeAttackLog.Same(r, first));
				List<TimeAttackRecord> left = TimeAttackLog.Load(folder);
				Expect(one == 1 && left.Count == 2 && !left.Any(r => TimeAttackLog.Same(r, first)), "one time can be deleted, the others stay");
				int course = TimeAttackLog.Delete(folder, r => r.CourseCode == "CRS-BBBB-BBBB");
				Expect(course == 1 && TimeAttackLog.Load(folder).Count == 1 && File.Exists(Path.Combine(folder, "runs-before-delete.csv")),
					"a course's times can be deleted, and the file from before is kept");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
				}
			}
		}

		private static void RaceCourseCarriesTraps() {
			Course course = MakeCourse(Hairpin(300, 5, 1.3f));
			CourseGate.TryMake(new Vector3(0, 0, 100), Vector3.forward, 10f, out CourseGate trap);
			course = course.WithTraps(new[] { trap });
			var m = new RaceMessage { Kind = RaceKind.Course, RaceId = 9, From = 1, Course = course };
			RaceMessage back = RaceMessage.Decode(m.Encode());
			Expect(back?.Course != null && back.Course.Code == course.Code && back.Course.Traps.Count == 1 &&
				Vector3.Distance(back.Course.Traps[0].Center, trap.Center) < 0.01f && Math.Abs(back.Course.Traps[0].Width - 10f) < 0.01f,
				"a race's course carries its speed traps, so a speed trap race counts the same traps for everyone");
		}
	}
}
