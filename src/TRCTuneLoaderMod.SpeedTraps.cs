using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Speed traps in game: free-roam traps on the map (its own "TRC Speed Trap" objects and the player's), the traps
	/// along a course during a run, the flash when the car goes through one, every speed saved, and placing traps.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string FreeRoamTrapsPref = "freeroam_speed_traps";
		private const float TrapFlashSeconds = 4f;

		private readonly SpeedTrapWatcher trapWatcher_ = new SpeedTrapWatcher();
		private readonly TrapMarkers trapMarkers_ = new TrapMarkers();
		private List<SpeedTrap> mapTraps_ = new List<SpeedTrap>();
		private List<SpeedTrapRecord> trapRecords_ = new List<SpeedTrapRecord>();
		private bool freeRoamTraps_ = true;
		private string trapFlash_ = "";
		private float trapFlashUntil_;
		private (int Index, float Kmh)? runTrapSeen_;
		private SpeedTrap trapDeletePending_;

		private string TrapsFile => map_ == null ? "" : Path.Combine(CoursesFolder, TuneBackups.SafeFileName(map_.Key.Replace(':', '_')), SpeedTrapFile.FileName);

		private void LoadTrapRecords() {
			try {
				trapRecords_ = SpeedTrapLog.Load(TimesFolder);
				freeRoamTraps_ = KSL.API.Kino.Prefs.Get(FreeRoamTrapsPref, true);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		/// <summary>The map's traps: its own objects (from the last scan) and the player's, for this map.</summary>
		private void RefreshTraps() {
			mapTraps_ = new List<SpeedTrap>(CourseScanner.SpeedTraps.Where(t => map_ != null && t.MapKey == map_.Key));
			if (map_ != null) {
				try {
					mapTraps_.AddRange(SpeedTrapFile.Load(TrapsFile, map_.Key));
				} catch (Exception e) {
					TrcLog.Error(e);
				}
			}
			trapWatcher_.Reset();
		}

		/// <summary>Called from UpdateCourses every frame: free-roam traps (not during a run, a race or a battle; those use the course's own).</summary>
		private void UpdateSpeedTraps() {
			if (!hasPose_) {
				trapWatcher_.Reset();
				return;
			}
			bool freeRoam = freeRoamTraps_ && run_ == null && !InRace && !InBattle && recorder_.State == CourseRecorder.Phase.Idle;
			if (freeRoam) {
				SpeedTrapHit? hit = trapWatcher_.Tick(Time.timeAsDouble, pose_.Position, pose_.SpeedKmh, mapTraps_);
				if (hit.HasValue) {
					TrapCaught(hit.Value.Trap, hit.Value.Kmh, "freeroam");
				}
			} else {
				trapWatcher_.Reset();
			}

			// A run's course traps: caught by the run itself; flashed and saved here.
			if (run_ != null && run_.LastTrap.HasValue && !run_.LastTrap.Equals(runTrapSeen_)) {
				runTrapSeen_ = run_.LastTrap;
				var (index, kmh) = run_.LastTrap.Value;
				TrapCaught(CourseTrap(run_.Course, index), kmh, run_.Course.Code);
			} else if (run_ == null || !run_.LastTrap.HasValue) {
				runTrapSeen_ = null;
			}

			Camera camera = HudCamera();
			List<CourseGate> shown = InRace && race_.Course != null ? race_.Course.Traps
				: InBattle ? battle_.Course.Traps
				: run_ != null ? run_.Course.Traps
				: freeRoam ? mapTraps_.Select(t => t.Gate).ToList() : null;
			trapMarkers_.Show(camera != null ? shown : null, camera != null ? camera.transform.position : Vector3.zero);
		}

		/// <summary>A course's trap as a speed trap of its own, for the records (named by the course and its number).</summary>
		private static SpeedTrap CourseTrap(Course course, int index) =>
			new SpeedTrap { Name = $"{course.Name} trap {index + 1}", Gate = course.Traps[index], MapKey = course.MapKey, Source = course.Source };

		/// <summary>The car went through a trap: the speed flashes with how it compares, and it's saved.</summary>
		private void TrapCaught(SpeedTrap trap, float kmh, string context) {
			var mine = trapRecords_.Where(r => r.TrapKey == trap.Key && r.CarId == pose_.CarId).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
			var all = trapRecords_.Where(r => r.TrapKey == trap.Key).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
			string colour = all > 0 && kmh > all ? Theme.PurpleHex : mine <= 0 || kmh >= mine ? Theme.GoodHex : Theme.WarnHex;
			string compare = all > 0 && kmh > all ? "best ever!" : mine > 0 ? $"your best {mine:0} km/h" : "first time through";
			trapFlash_ = $"<b>SPEED TRAP · {trap.Name}</b>\n<color={colour}><b>{kmh:0} km/h</b></color>  <color={Theme.MutedHex}>{compare}</color>";
			trapFlashUntil_ = Time.unscaledTime + TrapFlashSeconds;
			var record = new SpeedTrapRecord {
				When = DateTime.Now, TrapKey = trap.Key, TrapName = trap.Name, MapName = map_?.Name ?? "", Kmh = kmh, CarId = pose_.CarId,
				Car = car_?.CarName ?? "", TuneSet = carStatus_?.Matched == true ? carStatus_.Official.Name : "",
				Spec = carStatus_ != null && StateOf(carStatus_) == SpecState.Spec && modCheck_.Flags.Count == 0, Context = context,
			};
			trapRecords_.Add(record);
			try {
				SpeedTrapLog.Append(TimesFolder, record);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			TrcLog.Info($"Speed trap {trap.Name}: {kmh:0.0} km/h ({context}).");
		}

		/// <summary>The trap flash, a little below the timer's spot (or on its own in free roam).</summary>
		private void DrawTrapFlash(float screenWidth, float screenHeight) {
			if (Time.unscaledTime >= trapFlashUntil_ || trapFlash_.Length == 0) {
				return;
			}
			var content = new GUIContent(trapFlash_);
			float width = Mathf.Min(320f, screenWidth - 2 * HudMargin);
			float height = Theme.HudPill.CalcHeight(content, width);
			float y = hudRect_.width > 0 ? Mathf.Min(screenHeight - height, hudRect_.yMax + HudGap + 70f) : screenHeight * 0.2f;
			if (Event.current.type == EventType.Repaint) {
				GUI.Label(new Rect((screenWidth - width) / 2f, y, width, height), content, Theme.HudPill);
			}
		}

		private bool TrapFlashVisible => hasPose_ && Time.unscaledTime < trapFlashUntil_;

		// ---- The window ----

		/// <summary>Courses here: the map's speed traps with your bests, free roam on or off, and placing your own.</summary>
		private void DrawSpeedTraps() {
			GUILayout.Label("Speed traps", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.BeginHorizontal();
			int on = Choice(new[] { "Free-roam traps on", "Off" }, freeRoamTraps_ ? 0 : 1);
			if ((on == 0) != freeRoamTraps_) {
				freeRoamTraps_ = on == 0;
				SavePref(FreeRoamTrapsPref, freeRoamTraps_);
			}
			GUILayout.FlexibleSpace();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && hasPose_ && map_ != null;
			if (GUILayout.Button("Add speed trap here", Theme.Button)) {
				AddFreeRoamTrap();
			}
			GUI.enabled = enabled;
			GUILayout.EndHorizontal();
			if (mapTraps_.Count == 0) {
				GUILayout.Label("No speed traps on this map yet. Stop on the road where you want one, facing along it, and press Add speed trap here.", Theme.Small);
			}
			foreach (SpeedTrap trap in mapTraps_) {
				float mine = trapRecords_.Where(r => r.TrapKey == trap.Key && r.CarId == pose_.CarId).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
				float best = trapRecords_.Where(r => r.TrapKey == trap.Key).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
				float away = hasPose_ ? Vector3.Distance(pose_.Position, trap.Gate.Center) : 0f;
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{trap.Name}</b>  <color={Theme.MutedHex}>{(trap.Source == CourseSource.Map ? "map" : "yours")} · {TimeAttackRun.Distance(away)} away" +
					$"{(best > 0 ? $" · best {best:0} km/h" : "")}{(mine > 0 && mine < best ? $" · this car {mine:0}" : "")}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				if (trap.Source == CourseSource.Custom) {
					if (trapDeletePending_ == trap) {
						if (GUILayout.Button("Delete it", Theme.ButtonPrimary)) {
							DeleteFreeRoamTrap(trap);
						}
						if (GUILayout.Button("Keep", Theme.Button)) {
							trapDeletePending_ = null;
						}
					} else if (GUILayout.Button("Delete", Theme.Button)) {
						trapDeletePending_ = trap;
					}
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}

		/// <summary>A free-roam trap where the car is, across the road the way it faces (either way through it counts).</summary>
		private void AddFreeRoamTrap() {
			if (!CourseGate.TryMake(pose_.Position, pose_.Forward, newCourseWidth_, out CourseGate gate)) {
				courseResult_ = "Couldn't tell which way the car faces.";
				return;
			}
			try {
				List<SpeedTrap> mine = SpeedTrapFile.Load(TrapsFile, map_.Key);
				mine.Add(new SpeedTrap { Name = $"Speed trap {mine.Count + 1}", Gate = gate, MapKey = map_.Key, Source = CourseSource.Custom });
				SpeedTrapFile.Save(TrapsFile, mine);
				RefreshTraps();
				courseResult_ = $"Speed trap added here ({newCourseWidth_:0} m wide). Drive through it either way to catch your speed.";
				TrcLog.Info($"Speed trap added on {map_.Name} at {pose_.Position}.");
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the speed trap: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private void DeleteFreeRoamTrap(SpeedTrap trap) {
			trapDeletePending_ = null;
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
