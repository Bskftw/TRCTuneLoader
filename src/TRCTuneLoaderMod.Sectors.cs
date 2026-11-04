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
	/// Also the start key: a key (and the TRC button) that starts a time attack on the track open in Tracks.
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

		private void DrawManualSectorsOption() {
			GUILayout.Label("<b>Manual sectors</b>: sectors you set yourself on a track (Tracks, open a track, Sectors) replace its automatic ones. Off keeps them but uses the automatic ones.", Theme.Body);
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && run_ == null;
			bool on = OnOff(Course.UseManualSectors);
			GUI.enabled = enabled;
			if (on != Course.UseManualSectors) {
				SetManualSectorsOn(on);
			}
		}

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

		// ---- Start key (bskftw, Nov 3: "keybindable start for time attack") ----

		private const string StartKeyPref = "start_shortcut";
		private Shortcut startShortcut_ = Shortcut.StartDefault;
		private bool capturingStart_;
		private bool startDown_;

		/// <summary>The track a start key or the TRC button starts: the one open in Tracks, else the last one driven on this map.</summary>
		private Course TrackToStart() {
			if (map_ == null) {
				return null;
			}
			List<Course> here = CoursesHere();
			Course open = tracksOpen_.Count == 1 ? here.FirstOrDefault(c => c.Code == tracksOpen_.First()) : null;
			return open ?? here.FirstOrDefault(c => c.Code == lastStartedCode_);
		}

		private string lastStartedCode_ = "";

		/// <summary>Starts a time attack on <see cref="TrackToStart"/> and teleports to its start line, or says why not.</summary>
		private void StartFromKey() {
			if (run_ != null) {
				RestartFromKey();
				return;
			}
			Course course = TrackToStart();
			if (course == null || !course.Usable) {
				ShowNotice("Open a track in Time attack, Tracks first: the start key starts that one.", 3f);
				return;
			}
			StartTimeAttack(course);
			if (run_ == null) {
				ShowNotice(courseResult_, 3f);
				return;
			}
			TeleportToStart();
			ShowNotice($"<b>{course.Name}</b>\n{(run_.Flying ? "Flying start" : "Standing start")}", 2f);
		}

		private void DrawStartKeyOption() {
			GUILayout.Space(6);
			GUILayout.Label(capturingStart_
				? "Press the new start key now. Press Esc to cancel."
				: $"<b>{startShortcut_}</b> starts a time attack on the track open in Tracks (or the last one you drove) and puts you on its start line, flying or standing as picked in Tracks.", Theme.Body);
			GUILayout.BeginHorizontal();
			if (capturingStart_) {
				if (NavButton("Cancel", Theme.Button)) {
					capturingStart_ = false;
					optionsResult_ = "Start key not changed.";
				}
			} else {
				if (NavButton("Change start key", Theme.Button)) {
					capturingStart_ = true;
					capturingShortcut_ = false;
					capturingRestart_ = false;
					optionsResult_ = "";
				}
				if (!startShortcut_.SameAs(Shortcut.StartDefault) && NavButton($"Reset to {Shortcut.StartDefault}", Theme.Button)) {
					SetStartShortcut(Shortcut.StartDefault);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void SetStartShortcut(Shortcut shortcut) {
			capturingStart_ = false;
			if (shortcut.SameAs(shortcut_) || shortcut.SameAs(restartShortcut_)) {
				optionsResult_ = $"{shortcut} is already used: pick another key to start.";
				return;
			}
			startShortcut_ = shortcut;
			SavePref(StartKeyPref, shortcut.Save());
			optionsResult_ = $"Start key set to {shortcut}.";
			TrcLog.Info($"Start key set to {shortcut}.");
		}

		/// <summary>From HandleShortcutEvent: picking the start key, or pressing it. True when the event was the start key's.</summary>
		private bool HandleStartKey(Event e) {
			if (e.type == EventType.KeyUp && e.keyCode == startShortcut_.Key) {
				startDown_ = false;
				return false;
			}
			if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) {
				return false;
			}
			if (capturingStart_) {
				if (e.keyCode == KeyCode.Escape) {
					capturingStart_ = false;
					optionsResult_ = "Start key not changed.";
				} else {
					Shortcut picked = Shortcut.FromEvent(e);
					if (picked == null) {
						return false;
					}
					SetStartShortcut(picked);
					startDown_ = true;
				}
				e.Use();
				return true;
			}
			if (startShortcut_.Matches(e) && !startDown_ && !TypingInGame() && !InBattle && !InRace) {
				startDown_ = true;
				StartFromKey();
				e.Use();
				return true;
			}
			return false;
		}
	}
}
