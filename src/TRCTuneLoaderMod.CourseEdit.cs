using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Editing the player's own courses (Courses here, Edit): rename, change the line width, flip the direction,
	/// record the path again, or delete. Anything but the name gives the course a new code, so its old times stay
	/// under the old one. Also saving a course someone challenged you on, to practise it.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private Course editing_;
		private string editName_ = "";
		private const string ReversedSuffix = " (reversed)";

		private void StartEditing(Course course) {
			editing_ = course;
			editName_ = course?.Name ?? "";
			deletePending_ = null;
			courseResult_ = "";
		}

		private void DrawCourseEditor(Course course) {
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("Name", Theme.SectionLabel);
			GUILayout.BeginHorizontal();
			editName_ = GUILayout.TextField(editName_ ?? "", 60, Theme.Field);
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && TuneRules.Trim(editName_).Length > 0 && TuneRules.Trim(editName_) != course.Name;
			if (GUILayout.Button("Rename", Theme.Button, GUILayout.Width(90))) {
				string name = TuneRules.Trim(editName_);
				ReplaceCourse(course, Copy(course, c => c.Name = name), $"Renamed to {name}.");
			}
			GUI.enabled = enabled;
			GUILayout.EndHorizontal();

			GUILayout.Label(course.IsCircuit ? "Line width" : "Line width (start and finish)", Theme.SectionLabel);
			int widthIndex = Array.FindIndex(LineWidths, w => Mathf.Abs(w - course.Start.Width) < 0.05f);
			int pickedWidth = Choice(LineWidths.Select(w => $"{w:0} m").ToArray(), widthIndex);
			if (pickedWidth != widthIndex && pickedWidth >= 0) {
				float width = LineWidths[pickedWidth];
				ReplaceCourse(course, Copy(course, c => {
					c.Start.Width = width;
					c.Finish.Width = width;
				}), $"Lines are now {width:0} m wide.");
			}

			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			GUI.enabled = enabled && hasPose_ && map_ != null && course.MapKey == map_.Key && course.HasPath;
			if (GUILayout.Button("Add speed trap here", Theme.Button)) {
				AddCourseTrap(course);
			}
			GUI.enabled = enabled;
			if (course.Traps.Count > 0 && GUILayout.Button($"Remove its {course.Traps.Count} speed trap(s)", Theme.Button)) {
				RemoveCourseTraps(course);
			}
			GUILayout.FlexibleSpace();
			GUI.enabled = enabled && course.Usable && roomSync_.IsAvailable;
			if (GUILayout.Button("Share with the room", Theme.Button)) {
				ShareCourse(course);
			}
			GUI.enabled = enabled;
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			if (GUILayout.Button(course.IsCircuit ? "Drive it the other way" : "Flip direction", Theme.Button)) {
				string name = course.Name.EndsWith(ReversedSuffix, StringComparison.Ordinal)
					? course.Name.Substring(0, course.Name.Length - ReversedSuffix.Length)
					: course.Name + ReversedSuffix;
				Course flipped = course.ReversedAs(name);
				flipped.Reversed = false;
				ReplaceCourse(course, flipped, course.IsCircuit ? "The circuit now goes the other way round." : "The course now runs the other way: start and finish swapped.");
			}
			GUI.enabled = enabled && hasPose_ && map_ != null && course.MapKey == map_.Key && run_ == null && !InBattle;
			if (GUILayout.Button("Record the path again", Theme.Button)) {
				StartRecordPath(course);
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			if (deletePending_ == course) {
				if (GUILayout.Button("Delete it", Theme.ButtonPrimary)) {
					editing_ = null;
					DeleteCourse(course);
				}
				if (GUILayout.Button("Keep", Theme.Button)) {
					deletePending_ = null;
				}
			} else if (GUILayout.Button("Delete", Theme.Button)) {
				deletePending_ = course;
			}
			GUILayout.EndHorizontal();
			GUILayout.Label("Changing the lines, the direction or the path gives the course a new code, so your times on it so far stay under the old one in My times.", Theme.Small);
			GUILayout.EndVertical();
		}

		/// <summary>The course read back from its own text with a change made: a separate copy, rounded like a saved file.</summary>
		private static Course Copy(Course course, Action<Course> change) {
			Course copy = CourseFile.Parse(CourseFile.ToText(course), CourseSource.Custom);
			copy.FilePath = course.FilePath;
			change(copy);
			copy.Build(resample: false);
			return copy;
		}

		/// <summary>Saves <paramref name="updated"/> in place of <paramref name="old"/>: the same file when the name is the same, else a new one (then the old goes).</summary>
		private void ReplaceCourse(Course old, Course updated, string done) {
			try {
				string path;
				if (updated.Name == old.Name && !string.IsNullOrEmpty(old.FilePath)) {
					CourseFile.Save(updated, old.FilePath);
					path = old.FilePath;
				} else {
					path = CourseLibrary.SaveCustom(CoursesFolder, updated);
					CourseLibrary.DeleteCustom(old, out _);
				}
				courses_.LoadCustom(CoursesFolder);
				editing_ = courses_.Custom.FirstOrDefault(c => string.Equals(c.FilePath, path, StringComparison.OrdinalIgnoreCase));
				editName_ = editing_?.Name ?? "";
				if (pathShownFor_ == old) {
					pathShownFor_ = editing_;
				}
				courseResult_ = $"{done} Course code {updated.Code}.";
				TrcLog.Info($"Course edited: {old.Name} ({old.Code}) → {updated.Name} ({updated.Code}), {Path.GetFileName(path)}.");
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the course: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>A course that came with a challenge, kept as one of your own courses to practise on.</summary>
		private void SaveChallengeCourse(Course course, string from) {
			if (courses_.Custom.Any(c => c.Code == course.Code)) {
				battleResult_ = $"{course.Name} is already one of your courses.";
				return;
			}
			try {
				Course copy = Copy(course, c => c.Author = from);
				string path = CourseLibrary.SaveCustom(CoursesFolder, copy);
				courses_.LoadCustom(CoursesFolder);
				battleResult_ = $"Saved {copy.Name} to your courses (Courses here), to practise on.";
				TrcLog.Info($"Saved the battle course {copy.Name} ({copy.Code}) from {from}: {path}");
			} catch (Exception e) {
				battleResult_ = $"Couldn't save the course: {e.Message}";
				TrcLog.Error(e);
			}
		}
	}
}
