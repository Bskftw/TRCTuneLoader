// Manual sectors: loading them at start, switching them on, adding one where the car is, pasting, and saving them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Manual sectors (bskftw, Nov 3): drive to where a sector should end on a sprint or circuit and add it there.
	/// They replace the automatic sectors while Options has them on. The player's own courses save them in the
	/// course file (so they're shared with it); map and official courses in sectors.txt, shared with a sectors code.
	/// The Sectors panel is src/pages/SectorEditorPanel.cs; the start key is src/core/StartKey.cs.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string ManualSectorsPref = "manual_sectors";
		private Course sectorsEditing_;
		private string sectorsResult_ = "";

		private string SectorOverlayPath => Path.Combine(modFolder_, SectorOverlay.FileName);

		/// <summary>Called at startup before the courses load, so they're built with the player's sectors.</summary>
		private void LoadManualSectors() {
			try {
				Course.UseManualSectors = Kino.Prefs.Get(ManualSectorsPref, true);
				SectorOverlay.Load(SectorOverlayPath);
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't read the manual sectors: {e.Message}");
			}
		}

		/// <summary>The Options switch: every course works its sectors out again (not while a time attack is running).</summary>
		private void SetManualSectorsOn(bool on) {
			Course.UseManualSectors = on;
			SavePref(ManualSectorsPref, on);
			foreach (Course course in courses_.Official.Concat(courses_.FromMap).Concat(courses_.Custom)) {
				course.RefreshSectors();
			}
			TrcLog.Info($"Manual sectors {(on ? "on" : "off")}.");
		}

		private void AddSectorHere(Course course) {
			PathPosition at = CourseGeometry.Project(course.Path, course.Distances, pose_.Position, 0, course.Path.Count);
			if (at.Offset > 40f) {
				sectorsResult_ = "You're too far from the track: drive onto it where the sector should end.";
				return;
			}
			if (at.Progress < CourseGeometry.MinSectorMetres || at.Progress > course.Length - CourseGeometry.MinSectorMetres) {
				sectorsResult_ = $"Too close to the {(at.Progress < course.Length / 2f ? "start" : "finish")}: a sector needs at least {CourseGeometry.MinSectorMetres:0} m.";
				return;
			}
			foreach (Vector3 other in course.ManualSectors) {
				float there = CourseGeometry.Project(course.Path, course.Distances, other, 0, course.Path.Count).Progress;
				if (Mathf.Abs(there - at.Progress) < CourseGeometry.MinSectorMetres) {
					sectorsResult_ = $"There's already a sector end {Mathf.Abs(there - at.Progress):0} m from here: they need {CourseGeometry.MinSectorMetres:0} m between them.";
					return;
				}
			}
			course.PointAt(at.Progress, out Vector3 onPath, out _);
			var points = new List<Vector3>(course.ManualSectors) { onPath };
			SaveManualSectors(course, points, $"Sector end added at {TimeAttackRun.Distance(at.Progress)}.");
		}

		private void PasteSectors(Course course) {
			if (!SectorOverlay.TryParseCode(GUIUtility.systemCopyBuffer, out string code, out List<Vector3> points)) {
				sectorsResult_ = "There's no sectors code to paste: copy one first (it starts with TRCSEC).";
				return;
			}
			if (!string.Equals(code, course.Code, StringComparison.OrdinalIgnoreCase)) {
				Course other = courses_.ByCode(code);
				sectorsResult_ = $"That sectors code is for {(other != null ? other.Name : $"another track ({code})")}, not this one.";
				return;
			}
			SaveManualSectors(course, points, $"Pasted {points.Count + 1} sectors.");
		}

		/// <summary>Keeps a course's manual sectors: in its file (the player's own) or sectors.txt, and works its sectors out again.</summary>
		private void SaveManualSectors(Course course, List<Vector3> points, string done) {
			try {
				course.ManualSectors = points;
				if (course.Source == CourseSource.Custom && !string.IsNullOrEmpty(course.FilePath)) {
					CourseFile.Save(course, course.FilePath);
				} else {
					SectorOverlay.Set(course.Code, points);
				}
				course.RefreshSectors();
				sectorsResult_ = $"{done} {course.Name} has {course.SectorEnds.Length} sectors now.";
				TrcLog.Info($"Manual sectors on {course.Name} ({course.Code}): {points.Count} end(s), {course.SectorEnds.Length} sectors.");
			} catch (Exception e) {
				sectorsResult_ = $"Couldn't save the sectors: {e.Message}";
				TrcLog.Error(e);
			}
		}
	}
}
