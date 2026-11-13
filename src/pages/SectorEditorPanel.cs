// A track's Sectors panel on the Tracks page: its manual sector ends, add, remove, copy and paste.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>A track's Sectors panel: the manual sector ends, add one where the car is, remove, copy and paste.</summary>
		private void DrawSectorEditor(Course course) {
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("Sectors", Theme.Heading);
			GUILayout.Label(course.ManualSectors.Count == 0
				? $"This track uses {(course.SectorPoints.Count > 0 ? "the map's sectors" : "sectors split evenly")}: {course.SectorEnds.Length} of them. Drive to where a sector should end and press Add sector end here."
				: course.ManualSectorsOn
					? $"Your {course.ManualSectors.Count + 1} manual sectors are used in place of the automatic ones."
					: "Manual sectors are off in Options, so the automatic ones are used.", Theme.Small);

			var ends = new List<(Vector3 Point, float At)>();
			foreach (Vector3 point in course.ManualSectors) {
				PathPosition at = CourseGeometry.Project(course.Path, course.Distances, point, 0, course.Path.Count);
				ends.Add((point, at.Progress));
			}
			ends.Sort((a, b) => a.At.CompareTo(b.At));
			bool enabled = GUI.enabled;
			bool running = run_?.Course == course;
			for (int i = 0; i < ends.Count; i++) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"Sector {i + 1} ends at {TimeAttackRun.Distance(ends[i].At)}", Theme.Body);
				GUILayout.FlexibleSpace();
				GUI.enabled = enabled && !running;
				if (NavButton("Remove", Theme.Button)) {
					Vector3 gone = ends[i].Point;
					SaveManualSectors(course, course.ManualSectors.Where(p => p != gone).ToList(), $"Sector end at {TimeAttackRun.Distance(ends[i].At)} removed.");
				}
				GUI.enabled = enabled;
				GUILayout.EndHorizontal();
			}

			GUILayout.BeginHorizontal();
			GUI.enabled = enabled && !running && hasPose_ && map_ != null && course.MapKey == map_.Key && course.HasPath;
			if (NavButton("Add sector end here", Theme.ButtonPrimary)) {
				AddSectorHere(course);
			}
			GUI.enabled = enabled && !running && course.ManualSectors.Count > 0;
			if (NavButton("Clear", Theme.Button)) {
				SaveManualSectors(course, new List<Vector3>(), "Manual sectors cleared: the automatic ones are back.");
			}
			GUI.enabled = enabled && course.ManualSectors.Count > 0;
			if (NavButton("Copy sectors code", Theme.Button)) {
				GUIUtility.systemCopyBuffer = SectorOverlay.ToCode(course.Code, course.ManualSectors);
				sectorsResult_ = "Sectors code copied: paste it to a friend, who opens this track and presses Paste sectors code.";
			}
			GUI.enabled = enabled && !running;
			if (NavButton("Paste sectors code", Theme.Button)) {
				PasteSectors(course);
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (running) {
				GUILayout.Label("Stop the time attack on this track to change its sectors.", Theme.Small);
			}
			if (sectorsResult_.Length > 0) {
				GUILayout.Label(sectorsResult_, Theme.Small);
			}
			GUILayout.Label(course.Source == CourseSource.Custom
				? "Saved in the course file, so they go with it when you share the course."
				: "Saved on this PC for this track (sectors.txt). Share them with Copy sectors code.", Theme.Small);
			GUILayout.EndVertical();
		}
	}
}
