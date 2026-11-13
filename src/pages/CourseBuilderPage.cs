// The Course builder page: making your own practice course one step at a time.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private static readonly string[] CourseKinds = { "Touge", "Highway" };
		private static readonly string[] CourseShapes = { "Point to point", "Circuit" };

		/// <summary>The course builder, one step at a time: 1 start line, 2 drive, 3 finish line, 4 name and save.</summary>
		private void DrawCourseBuilder() {
			GUILayout.Label("Make your own practice course. Official TRC courses come with the mod.", Theme.Small);
			if (!hasPose_ || map_ == null) {
				GUILayout.Label("Drive onto a map first: the course builder uses your car's position.", Theme.PillNeutral);
				return;
			}
			if (run_ != null || InBattle) {
				GUILayout.Label(run_ != null ? "Stop the time attack first (in Tracks)." : "Finish or leave your battle first.", Theme.PillNeutral);
				return;
			}

			bool circuit = recorder_.State == CourseRecorder.Phase.Idle ? newCourseShape_ == 1 : recorder_.Circuit;
			int step = recorder_.State == CourseRecorder.Phase.Idle ? 1
				: recorder_.State == CourseRecorder.Phase.Done ? 4
				: circuit || recorder_.Recorded < CourseGeometry.MinLength ? 2
				: 3;
			DrawBuilderSteps(step, circuit);

			GUILayout.BeginVertical(Theme.Card);
			switch (step) {
				case 1:
					GUILayout.Label("Step 1 · Start line", Theme.Heading);
					if (recorder_.Problem.Length > 0) {
						GUILayout.Label(recorder_.Problem, Theme.PillWarn);
					}
					GUILayout.Label("Kind of course", Theme.SectionLabel);
					newCourseShape_ = Choice(CourseShapes, newCourseShape_);
					GUILayout.Label(newCourseShape_ == 1
						? "A circuit is a loop: set the start line, drive once round, and drive back through the start line. It closes by itself there."
						: "Point to point: set the start line, drive the course, then stop on the finish line and set it.", Theme.Small);
					GUILayout.Label("Stop with the middle of your car on the start line, facing the way the course goes.", Theme.Body);
					GUILayout.Label(newCourseShape_ == 1 ? "Line width" : "Line width (start and finish)", Theme.SectionLabel);
					int widthIndex = Array.IndexOf(LineWidths, newCourseWidth_);
					int pickedWidth = Choice(LineWidths.Select(w => $"{w:0} m").ToArray(), widthIndex);
					if (pickedWidth != widthIndex && pickedWidth >= 0) {
						newCourseWidth_ = LineWidths[pickedWidth];
					}
					GUILayout.Space(4);
					BuilderButtons("Set start here", SetCourseStart, startOver: false);
					break;

				case 2:
				case 3:
					GUILayout.Label(circuit ? "Step 2 · Drive the loop" : step == 2 ? "Step 2 · Drive the course" : "Step 3 · Finish line", Theme.Heading);
					GUILayout.Label($"Recording the route: <b>{TimeAttackRun.Distance(recorder_.Recorded)}</b> (the light-blue dots on the road)", Theme.Body);
					if (circuit) {
						GUILayout.Label(recorder_.Recorded < CourseGeometry.CircuitMinLength
							? $"Close this window ({shortcut_}) and drive the loop down the middle of the road. A lap must be at least {CourseGeometry.CircuitMinLength:0} m. Resetting the car loses the route."
							: $"Drive back through the start line, the way you set off, to close the loop ({TimeAttackRun.Distance(recorder_.DistanceToStart)} from it now).", Theme.Body);
						GUILayout.Space(4);
						BuilderButtons("Start over", () => { recorder_.Clear(); courseResult_ = ""; }, startOver: false);
					} else {
						GUILayout.Label(step == 2
							? $"Close this window ({shortcut_}) and drive the course down the middle of the road. Resetting the car loses the route."
							: "Stop with the middle of your car on the finish line, then press Set finish here.", Theme.Body);
						GUILayout.Space(4);
						bool enabled = GUI.enabled;
						GUI.enabled = enabled && step == 3;
						BuilderButtons("Set finish here", SetCourseFinish, startOver: true);
						GUI.enabled = enabled;
					}
					break;

				default:
					GUILayout.BeginHorizontal();
					GUILayout.Label("Step 4 · Name and save", Theme.Heading);
					GUILayout.FlexibleSpace();
					GUILayout.Label($"{TimeAttackRun.Distance(recorder_.Recorded)} recorded", Theme.Small);
					GUILayout.EndHorizontal();
					if (circuit && TuneRules.Trim(newCourseName_).Length == 0) {
						newCourseName_ = $"{map_.Name} circuit {courses_.ForMap(map_.Key).Count(c => c.Source == CourseSource.Custom && c.IsCircuit) + 1}";
					}
					GUILayout.Label("Name", Theme.SectionLabel);
					newCourseName_ = GUILayout.TextField(newCourseName_ ?? "", 60, Theme.Field);
					if (circuit) {
						GUILayout.Label("A circuit: the loop is closed.", Theme.Small);
					} else {
						GUILayout.Label("Kind", Theme.SectionLabel);
						newCourseKind_ = Choice(CourseKinds, newCourseKind_);
					}
					GUILayout.Label("The other way", Theme.SectionLabel);
					string typed = TuneRules.Trim(newCourseName_ ?? "");
					newCourseReverse_ = Choice(new[] { $"Also make {(typed.Length > 0 ? ReverseName(typed) : "the reverse")}", "Just this one" }, newCourseReverse_ ? 0 : 1) == 0;
					GUILayout.Label("Random speed traps", Theme.SectionLabel);
					newCourseTraps_ = Choice(new[] { "None" }.Concat(RandomTrapCounts.Select(n => n.ToString(CultureInfo.InvariantCulture))).ToArray(), newCourseTraps_);
					GUILayout.Space(4);
					BuilderButtons("Save course", SaveCourse, startOver: true);
					break;
			}
			GUILayout.EndVertical();
		}

		/// <summary>"1 Start › 2 Drive › 3 Finish › 4 Save" (a circuit: "1 Start › 2 Drive the loop › 3 Save"), with the current step highlighted.</summary>
		private static void DrawBuilderSteps(int current, bool circuit) {
			string[] names = circuit ? new[] { "Start", "Drive the loop", "Save" } : new[] { "Start", "Drive", "Finish", "Save" };
			if (circuit && current == 4) {
				current = 3;
			}
			GUILayout.BeginHorizontal();
			for (int i = 0; i < names.Length; i++) {
				int number = i + 1;
				GUILayout.Label($"{number} {names[i]}", number == current ? Theme.ChipActive : Theme.Chip);
				if (i < names.Length - 1) {
					GUILayout.Label("›", Theme.Small);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(4);
		}

		private void BuilderButtons(string primary, Action onPrimary, bool startOver) {
			GUILayout.BeginHorizontal();
			if (NavButton(primary, Theme.ButtonPrimary)) {
				onPrimary();
			}
			bool enabled = GUI.enabled;
			GUI.enabled = true;
			if (startOver && NavButton("Start over", Theme.Button)) {
				recorder_.Clear();
				courseResult_ = "";
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
	}
}
