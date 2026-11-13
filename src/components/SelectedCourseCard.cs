// The selected course card on Tracks: the course's best times and its theoretical best.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private string selectedKey_;
		private object selectedTimes_;
		private int selectedCount_ = -1;
		private double? selectedTheoretical_;
		private int selectedRuns_;

		private void DrawSelectedCourse(Course course) {
			EnsureNavStyles();
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("SELECTED COURSE", kicker_);
			GUILayout.Label(course.Name.ToUpperInvariant(), courseTitle_);
			string sectors = course.Usable ? $" · {course.SectorEnds.Length} sectors{(course.ManualSectorsOn ? " (manual)" : "")}" : "";
			GUILayout.Label($"{course.KindName} · {(course.Usable ? TimeAttackRun.Distance(course.Length) : "can't be used yet")}{sectors} · {course.SourceName.ToLowerInvariant()}", Theme.Small);

			TimeAttackRecord best = CourseBest(course.Code, true) ?? CourseBest(course.Code, false);
			// The theoretical best and the number of runs: worked out again only when the course, its sectors, the rule or the saved times change (item 7).
			string selectedKey = $"{course.Code}|{course.SectorEnds.Length}|{course.IsCircuit}|{TimeAttackRule}";
			if (selectedKey != selectedKey_ || !ReferenceEquals(selectedTimes_, times_) || selectedCount_ != times_.Count) {
				selectedKey_ = selectedKey;
				selectedTimes_ = times_;
				selectedCount_ = times_.Count;
				selectedTheoretical_ = TheoreticalBest(course);
				selectedRuns_ = times_.Count(r => r.CourseCode == course.Code && !r.IsLap);
			}
			double? theoretical = selectedTheoretical_;
			int runs = selectedRuns_;
			GUILayout.BeginHorizontal();
			StatBox("Best", best != null ? TimeAttackRun.FormatTime(best.Seconds) : "–", Theme.PurpleHex);
			StatBox("Theoretical", theoretical.HasValue ? TimeAttackRun.FormatTime(theoretical.Value) : "–", null);
			StatBox("Runs", runs.ToString(CultureInfo.InvariantCulture), null);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUIStyle start = Theme.ButtonPrimary;
			if (run_?.Course == course || pathRecorder_?.Course == course) {
				GUI.enabled = enabled && hasPose_;
				if (NavButton("Teleport to start", start)) {
					TeleportToStart();
				}
				GUI.enabled = enabled;
				if (run_?.Course == course && NavButton("Stop", Theme.Button)) {
					StopTimeAttack();
				}
			} else {
				GUI.enabled = enabled && course.Usable && hasPose_;
				if (NavButton("Teleport & start", start)) {
					StartTimeAttack(course);
					if (run_?.Course == course) {
						TeleportToStart();
					}
				}
				if (NavButton("Ready, drive there", Theme.Button)) {
					StartTimeAttack(course);
				}
				GUI.enabled = enabled;
			}
			DrawGhostsButton(course);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		/// <summary>The best of each sector added up (laps on a circuit), when every sector has a time; to the rules picked for time attack.</summary>
		private double? TheoreticalBest(Course course) {
			int sectors = course.SectorEnds.Length;
			if (sectors == 0) {
				return null;
			}
			string rule = TimeAttackRule;
			long?[] best = TimeAttackLog.BestSectorsMs(times_, r => r.CourseCode == course.Code && r.IsLap == course.IsCircuit && (r.Rule ?? "") == rule, sectors);
			return best.All(b => b.HasValue) ? best.Sum(b => b.Value) / 1000.0 : (double?)null;
		}
	}
}
