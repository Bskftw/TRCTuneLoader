// Changing speed traps: placing one where the car is, editing, deleting, random traps on a course, a course's own traps.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		// Placing and editing.
		private bool placingTrap_;
		private string newTrapName_ = "";
		private float newTrapWidth_ = 10f;
		private SpeedTrap trapEditing_;
		private string trapEditName_ = "";
		private float trapEditWidth_;
		private CourseGate trapPreview_;

		/// <summary>The line for the trap being placed (where the car is, as wide as picked) or edited.</summary>
		private void UpdateTrapPreview() {
			if (placingTrap_ && windowOpen_ && CourseGate.TryMake(pose_.Position, pose_.Forward, newTrapWidth_, out CourseGate here)) {
				// Built again only when the car has moved or the width changed, not every frame.
				if (trapPreview_ == null || Vector3.Distance(trapPreview_.Center, here.Center) > 0.5f || Math.Abs(trapPreview_.Width - newTrapWidth_) > 0.01f ||
					Vector3.Dot(trapPreview_.Forward, here.Forward) < 0.995f) {
					trapPreview_ = here;
				}
				return;
			}
			if (trapEditing_ != null && windowOpen_) {
				if (trapPreview_ == null || Math.Abs(trapPreview_.Width - trapEditWidth_) > 0.01f || Vector3.Distance(trapPreview_.Center, trapEditing_.Gate.Center) > 0.01f) {
					trapPreview_ = new CourseGate { Center = trapEditing_.Gate.Center, Forward = trapEditing_.Gate.Forward, Width = trapEditWidth_ };
				}
				return;
			}
			trapPreview_ = null;
		}

		private void StartPlacingTrap() {
			placingTrap_ = true;
			trapEditing_ = null;
			SpeedTrap last = mapTraps_.LastOrDefault(t => t.Source == CourseSource.Custom);
			newTrapName_ = SpeedTrap.NextName(last?.Name ?? "", mapTraps_.Count(t => t.Source == CourseSource.Custom));
		}

		/// <summary>A free-roam trap where the car is, across the road the way it faces (either way through it counts).</summary>
		private void AddFreeRoamTrap() {
			if (!CourseGate.TryMake(pose_.Position, pose_.Forward, newTrapWidth_, out CourseGate gate)) {
				courseResult_ = "Couldn't tell which way the car faces.";
				return;
			}
			try {
				string name = TuneRules.Trim(newTrapName_);
				List<SpeedTrap> mine = SpeedTrapFile.Load(TrapsFile, map_.Key);
				mine.Add(new SpeedTrap { Name = name, Gate = gate, MapKey = map_.Key, Source = CourseSource.Custom });
				SpeedTrapFile.Save(TrapsFile, mine);
				RefreshTraps();
				SavePref(TrapWidthPref, newTrapWidth_);
				string group = SpeedTrap.GroupOf(name);
				if (group.Length > 0) {
					trapGroupsOpen_.Add(group);
				}
				courseResult_ = $"{name} placed here ({newTrapWidth_:0} m wide). Drive on to place the next one, or press Done.";
				newTrapName_ = SpeedTrap.NextName(name, mine.Count);
				TrcLog.Info($"Speed trap {name} added on {map_.Name} at {pose_.Position}, {newTrapWidth_:0} m wide.");
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the speed trap: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>Renames a trap, changes its width, or moves it to where the car is (moving it starts its bests again: they belong to the old spot).</summary>
		private void EditFreeRoamTrap(SpeedTrap trap, string name, float width, bool move) {
			if (name.Length == 0) {
				courseResult_ = "Give the speed trap a name.";
				return;
			}
			CourseGate moved = null;
			if (move && !CourseGate.TryMake(pose_.Position, pose_.Forward, width, out moved)) {
				courseResult_ = "Couldn't tell which way the car faces.";
				return;
			}
			try {
				List<SpeedTrap> mine = SpeedTrapFile.Load(TrapsFile, map_.Key);
				SpeedTrap saved = mine.FirstOrDefault(t => t.Key == trap.Key);
				if (saved == null) {
					courseResult_ = "Couldn't find that speed trap in your file any more.";
					return;
				}
				saved.Name = name;
				saved.Gate = moved ?? new CourseGate { Center = saved.Gate.Center, Forward = saved.Gate.Forward, Width = width };
				SpeedTrapFile.Save(TrapsFile, mine);
				RefreshTraps();
				trapEditing_ = null;
				trapDeletePending_ = null;
				courseResult_ = move ? $"Moved {name} here." : $"Saved {name}.";
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the speed trap: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>The group name random traps on a course get.</summary>
		private static string RandomGroup(Course course) => $"{course.Name} Random";

		/// <summary>
		/// Puts <paramref name="count"/> free-roam traps along the course's path at random: one in each equal stretch
		/// (so they never bunch up), away from the start and finish, across the road the way it goes there. Earlier random
		/// traps on this course are replaced.
		/// </summary>
		private void PlaceRandomTraps(Course course, int count) {
			if (course == null || map_ == null || !course.HasPath || course.Length <= 0f) {
				return;
			}
			try {
				string group = RandomGroup(course);
				List<SpeedTrap> mine = SpeedTrapFile.Load(TrapsFile, map_.Key);
				mine.RemoveAll(t => string.Equals(t.Group, group, StringComparison.OrdinalIgnoreCase));
				var random = new System.Random();
				float margin = Mathf.Min(80f, course.Length * 0.1f);
				float stretch = (course.Length - 2f * margin) / count;
				ICollection<int> road = course.RoadColliders.Count > 0 ? course.RoadColliders : new HashSet<int>(CoursesHere().SelectMany(c => c.RoadColliders));
				int placed = 0;
				for (int i = 0; i < count; i++) {
					float distance = margin + stretch * (i + 0.2f + 0.6f * (float)random.NextDouble());
					course.PointAt(distance, out Vector3 at, out Vector3 forward);
					Vector3 right = new Vector3(forward.z, 0f, -forward.x);
					float width = adapter_.TryMeasureRoadWidth(at, right, road, out float measured) ? Mathf.Clamp(measured + 1f, 4f, 30f) : 12f;
					if (CourseGate.TryMake(at, forward, width, out CourseGate gate)) {
						mine.Add(new SpeedTrap { Name = $"{group} Speed Trap {++placed}", Gate = gate, MapKey = map_.Key, Source = CourseSource.Custom });
					}
				}
				SpeedTrapFile.Save(TrapsFile, mine);
				RefreshTraps();
				trapGroupsOpen_.Add(group);
				courseResult_ = $"Placed {placed} random speed trap(s) on {course.Name}, grouped as {group}.";
				TrcLog.Info($"{placed} random speed trap(s) placed on {course.Name} ({course.Code}).");
			} catch (Exception e) {
				courseResult_ = $"Couldn't place them: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private void DeleteTrapGroup(string group) {
			trapGroupDeletePending_ = "";
			try {
				List<SpeedTrap> mine = SpeedTrapFile.Load(TrapsFile, map_.Key);
				int removed = mine.RemoveAll(t => string.Equals(t.Group, group, StringComparison.OrdinalIgnoreCase));
				SpeedTrapFile.Save(TrapsFile, mine);
				RefreshTraps();
				trapEditing_ = null;
				courseResult_ = $"Deleted the {removed} speed trap(s) in {group}. Their speeds stay saved.";
			} catch (Exception e) {
				courseResult_ = $"Couldn't delete them: {e.Message}";
			}
		}

		private void DeleteFreeRoamTrap(SpeedTrap trap) {
			trapDeletePending_ = null;
			trapEditing_ = null;
			try {
				List<SpeedTrap> mine = SpeedTrapFile.Load(TrapsFile, map_.Key);
				mine.RemoveAll(t => t.Key == trap.Key);
				SpeedTrapFile.Save(TrapsFile, mine);
				RefreshTraps();
				courseResult_ = $"Deleted {trap.Name}. Its speeds stay saved.";
			} catch (Exception e) {
				courseResult_ = $"Couldn't delete it: {e.Message}";
			}
		}

		/// <summary>Edit: a speed trap on this course where the car is (in driving order along the course).</summary>
		private void AddCourseTrap(Course course) {
			if (!CourseGate.TryMake(pose_.Position, pose_.Forward, course.Start.Width, out CourseGate gate)) {
				courseResult_ = "Couldn't tell which way the car faces.";
				return;
			}
			PathPosition at = CourseGeometry.Project(course.Path, course.Distances, pose_.Position, 0, course.Path.Count);
			if (at.Offset > 30f) {
				courseResult_ = "Drive onto the course first: a speed trap goes across its road.";
				return;
			}
			if (Vector3.Dot(gate.Forward, CourseGeometry.Level(course.Path[Math.Min(at.Segment + 1, course.Path.Count - 1)] - course.Path[at.Segment])) < 0f) {
				gate.Forward = -gate.Forward;
			}
			ReplaceCourse(course, Copy(course, c => {
				c.Traps.Add(gate);
				c.Traps = c.Traps.OrderBy(t => CourseGeometry.Project(c.Path, c.Distances, t.Center, 0, c.Path.Count).Progress).ToList();
			}), "Speed trap added.");
		}

		private void RemoveCourseTraps(Course course) {
			ReplaceCourse(course, Copy(course, c => c.Traps.Clear()), "Speed traps removed.");
		}
	}
}
