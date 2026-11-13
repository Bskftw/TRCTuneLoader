// The course builder's actions: laying out a grid, setting the start and finish, saving and deleting a course.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>Faster than this (km/h), the course builder won't set a line: the car has to stop on it.</summary>
		private const float SetLineKmh = 3f;

		private static readonly float[] LineWidths = { 8f, 12f, 16f, 20f, 30f };

		/// <summary>Lay out grid, while it runs, and the layout picked for it.</summary>
		private GridRecorder gridRecorder_;
		private int gridLayoutPick_;
		private static readonly string[] GridLayouts = { "Two-wide grid", "Line-up" };

		private string newCourseName_ = "";
		private int newCourseKind_;
		private int newCourseShape_;

		/// <summary>Saving a course also makes its reverse (driven the other way), when picked.</summary>
		private bool newCourseReverse_ = true;
		private float newCourseWidth_ = CourseGeometry.DefaultWidth;

		/// <summary>Starts (or, with null, stops) laying out a race grid for a course: drive up to its start line down the middle of the road.</summary>
		private void StartGrid(Course course) {
			gridRecorder_ = course != null ? new GridRecorder(course) : null;
			if (course != null) {
				pathShownFor_ = null;
				courseResult_ = $"Lay out grid for {course.Name}: drive {GridRecorder.MinLength:0}–{GridRecorder.MaxLength:0} m back from the start line, turn round, and drive down the middle of the road up to the line and through it.";
				TrcLog.Info($"Lay out grid started on {course.Name} ({GridLayouts[gridLayoutPick_]}).");
				CloseWindow();
			}
		}

		/// <summary>
		/// Keeps the laid-out grid: in your own course's file, or for a map course in the file with its recorded path
		/// (or a small file of its own), which the map course then takes like a recorded path.
		/// </summary>
		private void SaveGrid() {
			GridRecorder done = gridRecorder_;
			gridRecorder_ = null;
			Course course = done.Course;
			List<Vector3> grid = done.GridPath();
			GridLayout layout = gridLayoutPick_ == 1 ? GridLayout.LineUp : GridLayout.Grid;
			try {
				Course file = course.Source == CourseSource.Custom ? course
					: courses_.Custom.FirstOrDefault(c => c.IsCircuit == course.IsCircuit && c.SameLines(course));
				if (file != null) {
					Course updated = Copy(file, c => {
						c.GridPath = grid;
						c.GridLayout = layout;
					});
					CourseFile.Save(updated, file.FilePath);
				} else {
					var lines = new Course {
						Name = $"{course.Name} grid", Kind = course.Kind, Source = CourseSource.Custom, MapKey = course.MapKey, MapName = course.MapName,
						Author = $"steam:{MySteamId.ToString(CultureInfo.InvariantCulture)}",
						Start = new CourseGate { Center = course.Start.Center, Forward = course.Start.Forward, Width = course.Start.Width },
						Finish = new CourseGate { Center = course.Finish.Center, Forward = course.Finish.Forward, Width = course.Finish.Width },
						GridPath = grid, GridLayout = layout,
					};
					lines.Build(resample: false);
					CourseLibrary.SaveCustom(CoursesFolder, lines);
				}
				courses_.LoadCustom(CoursesFolder);
				MapCoursesFromFilesAgain();
				courseResult_ = $"Grid laid out for {course.Name}: {TimeAttackRun.Distance(done.Recorded)}, {GridLayouts[gridLayoutPick_].ToLowerInvariant()}. Races on it line up along it.";
				TrcLog.Info($"Grid laid out for {course.Name}: {grid.Count} points, {done.Recorded:0} m, {layout}.");
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the grid: {e.Message}";
				TrcLog.Error(e);
			}
		}

		// ---- Course builder ----

		private void SetCourseStart() {
			if (pose_.SpeedKmh > SetLineKmh) {
				courseResult_ = "Stop the car on the start line first.";
				return;
			}
			if (!recorder_.SetStart(pose_.Position, pose_.Forward, newCourseWidth_, out string error, circuit: newCourseShape_ == 1)) {
				courseResult_ = error;
				return;
			}
			courseResult_ = "";
			TrcLog.Info($"Course builder: start set on {map_.Name} at {pose_.Position}.");
		}

		private void SetCourseFinish() {
			if (pose_.SpeedKmh > SetLineKmh) {
				courseResult_ = "Stop the car on the finish line first.";
				return;
			}
			if (!recorder_.SetFinish(pose_.Position, pose_.Forward, newCourseWidth_, out string error)) {
				courseResult_ = error;
				return;
			}
			courseResult_ = "";
			if (TuneRules.Trim(newCourseName_).Length == 0) {
				newCourseName_ = $"{map_.Name} course {courses_.ForMap(map_.Key).Count(c => c.Source == CourseSource.Custom) + 1}";
			}
		}

		/// <summary>The course builder's random speed traps: 0 none, else an index into RandomTrapCounts plus one.</summary>
		private int newCourseTraps_;

		private void SaveCourse() {
			string name = TuneRules.Trim(newCourseName_);
			if (name.Length == 0) {
				courseResult_ = "Give the course a name first.";
				return;
			}
			try {
				Course course = recorder_.Build(name, (CourseKind)newCourseKind_, map_.Key, map_.Name, $"steam:{Kino.Info.SteamId.ToString(CultureInfo.InvariantCulture)}");
				string path = CourseLibrary.SaveCustom(CoursesFolder, course);
				TrcLog.Info($"Course saved: {path} ({course.Length:0} m, {course.Code}).");
				string also = "";
				if (newCourseReverse_) {
					Course reverse = course.ReversedAs(ReverseName(name));
					reverse.FilePath = "";
					reverse.Reversed = false;
					reverse.Build(resample: false);
					string reversePath = CourseLibrary.SaveCustom(CoursesFolder, reverse);
					also = $" Also saved {reverse.Name} (the other way).";
					TrcLog.Info($"Course saved: {reversePath} ({reverse.Length:0} m, {reverse.Code}), the reverse of {course.Name}.");
				}
				courses_.LoadCustom(CoursesFolder);
				recorder_.Clear();
				newCourseName_ = "";
				coursesView_ = 0;
				courseResult_ = $"Saved {course.Name}: {TimeAttackRun.Distance(course.Length)}, code {course.Code}.{also} It's in Tracks.";
				if (newCourseTraps_ > 0) {
					string saved = courseResult_;
					PlaceRandomTraps(course, RandomTrapCounts[newCourseTraps_ - 1]);
					courseResult_ = $"{saved} {courseResult_}";
				}
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the course: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>The reverse course's name: "Akina Downhill" becomes "Akina Uphill" (and back), anything else gets "Reverse".</summary>
		private static string ReverseName(string name) {
			if (name.EndsWith(" Downhill", StringComparison.OrdinalIgnoreCase)) {
				return name.Substring(0, name.Length - " Downhill".Length) + " Uphill";
			}
			if (name.EndsWith(" Uphill", StringComparison.OrdinalIgnoreCase)) {
				return name.Substring(0, name.Length - " Uphill".Length) + " Downhill";
			}
			return name.EndsWith(" Reverse", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - " Reverse".Length) : name + " Reverse";
		}

		private void DeleteCourse(Course course) {
			deletePending_ = null;
			if (!CourseLibrary.DeleteCustom(course, out string error)) {
				courseResult_ = $"Couldn't delete it: {error}";
				return;
			}
			courses_.LoadCustom(CoursesFolder);
			courseResult_ = $"Deleted {course.Name}. Its times stay in My times.";
			TrcLog.Info($"Course deleted: {course.FilePath}");
		}
	}
}
