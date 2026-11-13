// The Time attack tab and its Tracks page: the courses on this map, each with its times and buttons.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>The Time attack tab's sections, down the left (since 0.15.0: Battles is its own tab, speed traps have their own section).</summary>
		private static readonly string[] CourseViews = { "Tracks", "Speed traps", "My times", "Course builder" };

		private int coursesView_;

		private Vector2 coursesScroll_;
		private Vector2 timesScroll_;

		private void DrawCoursesTab() {
			GUILayout.Label(map_ == null
				? "Drive onto a map to see its tracks and start a time attack. Your times are here too."
				: $"Map: <b>{map_.Name}</b> <color={Theme.MutedHex}>({map_.Key}{(map_.UploadKey.Length > 0 ? $", upload {map_.UploadKey}" : "")})</color>", Theme.Body);
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			int tracks = map_ == null ? 0 : CoursesHere().Count;
			string building = recorder_.State != CourseRecorder.Phase.Idle ? "recording" : "";
			int view = SideNav(CourseViews, coursesView_, new[] {
				tracks > 0 ? tracks.ToString(CultureInfo.InvariantCulture) : "",
				mapTraps_.Count > 0 ? mapTraps_.Count.ToString(CultureInfo.InvariantCulture) : "",
				"", building,
			});
			if (view != coursesView_) {
				coursesView_ = view;
				courseResult_ = "";
				deletePending_ = null;
				placingTrap_ = false;
				trapEditing_ = null;
			}
			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			if (!string.IsNullOrEmpty(courseResult_)) {
				GUILayout.Label(courseResult_, Theme.Body);
			}
			switch (coursesView_) {
				case 0: DrawTracks(); break;
				case 1: DrawSpeedTraps(); break;
				case 2: DrawMyTimes(); break;
				default: DrawCourseBuilder(); break;
			}
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		private void DrawTracks() {
			if (run_ != null) {
				GUILayout.BeginVertical(Theme.Guide);
				GUILayout.Label($"Time attack: {run_.Course.Name}", Theme.Heading);
				GUILayout.Label(RunStatusText(), Theme.Body);
				GUILayout.BeginHorizontal();
				if (NavButton("Teleport to start", Theme.ButtonPrimary)) {
					TeleportToStart();
				}
				if (NavButton("Stop time attack", Theme.Button)) {
					StopTimeAttack();
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
			if (pathRecorder_ != null) {
				GUILayout.BeginVertical(Theme.Guide);
				GUILayout.Label($"Record path: {pathRecorder_.Course.Name}", Theme.Heading);
				GUILayout.Label(pathRecorder_.State == PathRecorder.Phase.ToStart
					? $"Drive through the start line, then down the middle of the road to the finish line. Recording stops by itself there. Close this window ({shortcut_}) to drive."
					: $"{pathRecorder_.Message}: {TimeAttackRun.Distance(pathRecorder_.Recorded)}", Theme.Body);
				GUILayout.BeginHorizontal();
				if (pathRecorder_.State != PathRecorder.Phase.Done && NavButton("Teleport to start", Theme.ButtonPrimary)) {
					TeleportToStart();
				}
				if (pathRecorder_ != null && NavButton(pathRecorder_.State == PathRecorder.Phase.Done ? "Close" : "Stop recording", Theme.Button)) {
					pathRecorder_ = null;
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
			if (map_ == null) {
				return;
			}

			List<Course> here = CoursesHere();
			if (run_ == null && here.Count > 0) {
				DrawStartOptions(here.Any(c => c.IsCircuit));
				GUILayout.BeginHorizontal();
				GUILayout.Label("Lay out grid as", Theme.SectionLabel, GUILayout.Width(110));
				gridLayoutPick_ = Choice(GridLayouts, gridLayoutPick_);
				GUILayout.EndHorizontal();
			}
			GUILayout.BeginVertical(Theme.Card, GUILayout.ExpandHeight(true));
			float scrollY = coursesScroll_.y;
			coursesScroll_ = GUILayout.BeginScrollView(coursesScroll_);
			if (here.Count == 0) {
				GUILayout.Label(scannedMapKey_ == map_.Key
					? "No courses on this map yet. Make your own with the Course builder."
					: "Looking for courses...", Theme.Small);
			}
			if (here.Count > 6) {
				// bskftw, Nov 1: maps with ~50 tracks. Each folds to one line, and the list narrows as you type.
				GUILayout.BeginHorizontal();
				GUILayout.Label("Find a track", Theme.Small, GUILayout.Width(90));
				trackFilter_ = GUILayout.TextField(trackFilter_ ?? "", 40, Theme.Field, GUILayout.Width(220));
				if (trackFilter_.Length > 0 && NavButton("Clear", Theme.Button)) {
					trackFilter_ = "";
				}
				GUILayout.FlexibleSpace();
				if (tracksOpen_.Count > 0 && NavButton("Close all", Theme.Button)) {
					tracksOpen_.Clear();
				}
				GUILayout.EndHorizontal();
			}
			string filter = (trackFilter_ ?? "").Trim();
			List<Course> shown = filter.Length == 0 ? here : here.Where(c => c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			if (shown.Count == 0 && here.Count > 0) {
				GUILayout.Label($"No track here has \"{filter}\" in its name.", Theme.Small);
			}
			LongRows(tracksList_, shown, scrollY, c => c.Code, IsTrackOpen, DrawCourseRow);
			DrawSharedRunsElsewhere();
			GUILayout.EndScrollView();
			EndLongList(tracksList_);
			GUILayout.EndVertical();

			GUILayout.BeginHorizontal();
			if (NavButton("Look for course objects again", Theme.Button)) {
				scannedMapKey_ = "";
				freshScan_ = true;
				mapSince_ = Time.unscaledTime - MapScanDelaySeconds;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!string.IsNullOrEmpty(scanSummary_)) {
				GUILayout.Label(scanSummary_, Theme.Small);
			}
		}

		/// <summary>The tracks opened in the list (by course code), and the name filter.</summary>
		private readonly HashSet<string> tracksOpen_ = new HashSet<string>(StringComparer.Ordinal);
		private string trackFilter_ = "";
		private readonly LongList tracksList_ = new LongList();

		/// <summary>Opened in the list, or being driven, recorded or edited (those stay open).</summary>
		private bool IsTrackOpen(Course course) =>
			run_?.Course == course || pathRecorder_?.Course == course || gridRecorder_?.Course == course || editing_ == course || tracksOpen_.Contains(course.Code);

		/// <summary>
		/// A track: one line with its name, kind, length and your best, like My times; click it to open its buttons,
		/// details, ghosts and editor. The one being driven, recorded or edited stays open.
		/// </summary>
		private void DrawCourseRow(Course course) {
			string length = course.Usable ? TimeAttackRun.Distance(course.Length) : "can't be used yet";
			bool busy = run_?.Course == course || pathRecorder_?.Course == course || gridRecorder_?.Course == course || editing_ == course;
			bool open = IsTrackOpen(course);
			TimeAttackRecord best = CourseBest(course.Code, true) ?? CourseBest(course.Code, false);
			string bestText = best != null ? $"  {TimeAttackRun.FormatTime(best.Seconds)}" : "";
			string flags = (course.Problems.Count > 0 ? $"  <color={Theme.WarnHex}>{course.Problems.Count} note(s)</color>" : "") +
				(racingGhost_ != null && racingGhost_.CourseCode == course.Code ? $"  <color=#8FB8FF>ghost</color>" : "") +
				(run_?.Course == course ? $"  <color={Theme.GoodHex}>driving</color>" : "");
			string header = $"<b>{course.Name}</b>  <color={Theme.MutedHex}>{course.KindName} · {length} · {course.SourceName}</color><b>{bestText}</b>{flags}";
			if (NavButton(header, open ? Theme.RowSelected : Theme.Row) && !busy) {
				// bskftw, Nov 3: opening a track closes the others.
				bool wasOpen = tracksOpen_.Contains(course.Code);
				tracksOpen_.Clear();
				if (!wasOpen) {
					tracksOpen_.Add(course.Code);
				}
			}
			if (!open) {
				return;
			}
			DrawSelectedCourse(course);
			GUILayout.Label($"Course code {course.Code}{BestText(course)}", Theme.Small);
			foreach (string problem in course.Problems) {
				GUILayout.Label($"<color={Theme.WarnHex}>{problem}</color>", Theme.Small);
			}

			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && course.Usable && hasPose_;
			// A map course gets its path from driving it once (or a new one, replacing the player's own).
			if (course.Source == CourseSource.Map && (!course.HasPath || course.PathFromPlayer) &&
				NavButton(course.HasPath ? "Record again" : "Record path", Theme.Button)) {
				StartRecordPath(course);
			}
			GUI.enabled = enabled && course.HasPath && hasPose_;
			if (NavButton(pathShownFor_ == course ? "Hide path" : "Show path", Theme.Button)) {
				pathShownFor_ = pathShownFor_ == course ? null : course;
			}
			GUI.enabled = enabled;
			// Race grids: drive the road up to the start line (official courses and maps with grid spots have their own).
			GUI.enabled = enabled && hasPose_ && course.Source != CourseSource.Official && course.GridObjects.Count == 0 && run_ == null && !InBattle && !InRace;
			if (NavButton(gridRecorder_?.Course == course ? "Stop grid" : "Lay out grid", Theme.Button)) {
				StartGrid(gridRecorder_?.Course == course ? null : course);
			}
			GUI.enabled = enabled;

			if (course.Usable && course.HasPath && NavButton(sectorsEditing_ == course ? "Hide sectors" : $"Sectors{(course.ManualSectors.Count > 0 ? " (manual)" : "")}", Theme.Button)) {
				sectorsEditing_ = sectorsEditing_ == course ? null : course;
				sectorsResult_ = "";
			}
			if (course.Source == CourseSource.Custom && NavButton(editing_ == course ? "Done" : "Edit", Theme.Button)) {
				StartEditing(editing_ == course ? null : course);
			}
			// Any track with a path, the map's own and official ones too (bskftw, Nov 6): opens Speed traps' random traps with it picked.
			if (course.HasPath && course.Length > 300f && NavButton("Random speed traps", Theme.Button)) {
				coursesView_ = 1;
				courseResult_ = "";
				randomTrapsOpen_ = true;
				randomTrapsCourse_ = course.Code;
				placingTrap_ = false;
				trapEditing_ = null;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (sectorsEditing_ == course) {
				DrawSectorEditor(course);
			}
			DrawGhostList(course);
			if (editing_ == course) {
				DrawCourseEditor(course);
			}
			GUILayout.Space(4);
			DividerLine();
			GUILayout.Space(4);
		}

		private string BestText(Course course) {
			TimeAttackRecord spec = CourseBest(course.Code, true);
			if (spec != null) {
				return $" · your best: <color={Theme.GoodHex}>{TimeAttackRun.FormatTime(spec.Seconds)}</color> ({spec.Car}, {spec.TuneSet})";
			}
			TimeAttackRecord any = CourseBest(course.Code, false);
			return any != null ? $" · your best: {TimeAttackRun.FormatTime(any.Seconds)} (not counted)" : "";
		}

		private string RunStatusText() {
			switch (run_.State) {
				case TimeAttackRun.Phase.Running:
					string lap = run_.Course.IsCircuit ? $" Lap {run_.Lap}{(run_.Laps > 0 ? $" of {run_.Laps}" : "")}{(run_.BestLap != null ? $", best {TimeAttackRun.FormatTime(run_.BestLap.Time)}" : "")}." : "";
					return $"Running: {TimeAttackRun.FormatTime(run_.Elapsed)}, {TimeAttackRun.Distance(run_.Progress)} of {TimeAttackRun.Distance(run_.Course.Length)}.{lap}";
				case TimeAttackRun.Phase.Finished:
				case TimeAttackRun.Phase.Cancelled:
					return runResultText_.Replace("\n", " · ");
				default:
					return $"{run_.Message}. Close this window ({shortcut_}) to drive.";
			}
		}

		/// <summary>Standing or flying, and (for circuits) how many laps: above the list of courses.</summary>
		private void DrawStartOptions(bool anyCircuit) {
			LoadLapPrefs();
			DrawTimeAttackRules();
			GUILayout.BeginHorizontal();
			GUILayout.Label("Start", Theme.SectionLabel, GUILayout.Width(60));
			int start = Choice(StartChoices, pickedStart_);
			GUILayout.EndHorizontal();
			if (start != pickedStart_) {
				pickedStart_ = start;
				SavePref(StartPref, start);
			}
			if (anyCircuit) {
				GUILayout.BeginHorizontal();
				GUILayout.Label("Laps", Theme.SectionLabel, GUILayout.Width(60));
				int laps = Choice(LapChoiceNames, pickedLaps_);
				GUILayout.EndHorizontal();
				if (laps != pickedLaps_) {
					pickedLaps_ = laps;
					SavePref(LapsPref, laps);
				}
			}
			GUILayout.Label(PickedFlying
				? "Flying: the clock starts when you drive through the start line, at any speed. Flying times are kept apart from standing ones."
				: "Standing: stop behind the start line and the clock starts at GO.", Theme.Small);
			if (anyCircuit && PickedLaps == 0) {
				GUILayout.Label("Unlimited laps (circuits) is practice: a lap with a shortcut or off the road just doesn't count, and you keep going. Only your best lap is kept.", Theme.Small);
			}
		}
	}
}
