// Speed traps as you drive: the map's traps, which ones count now (free roam, a run, a battle, a race), catching a speed, and the traps along a course.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Speed traps in game: free-roam traps on the map (its own "TRC Speed Trap" objects and the player's), the traps
	/// along a course during a run, the flash when the car goes through one, every speed saved, placing, naming and
	/// sizing traps, and the floating "SPEED CHECK" sign over each one. This file is the part that runs as you drive;
	/// the flash and signs are src/pages/SpeedTrapHud.cs, the pages SpeedTrapsPage.cs and ShareSpeedTrapsPage.cs, and
	/// placing and editing traps src/courses/SpeedTrapEditing.cs.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string FreeRoamTrapsPref = "freeroam_speed_traps";
		private const string TrapWidthPref = "trap_width";
		private const float TrapFlashSeconds = 4f;
		private static readonly float[] TrapWidths = { 4f, 6f, 8f, 10f, 12f, 16f, 20f, 30f };

		private readonly SpeedTrapWatcher trapWatcher_ = new SpeedTrapWatcher();
		private readonly TrapMarkers trapMarkers_ = new TrapMarkers();
		private List<SpeedTrap> mapTraps_ = new List<SpeedTrap>();
		private List<SpeedTrapRecord> trapRecords_ = new List<SpeedTrapRecord>();
		private bool freeRoamTraps_ = true;
		private string trapFlash_ = "";
		private float trapFlashUntil_;
		private TimeAttackRun runTrapRun_;
		private int runTrapsSeen_;
		private SpeedTrap trapDeletePending_;

		/// <summary>The last speed caught at each trap (this session, else the newest saved for this car).</summary>
		private readonly Dictionary<string, float> lastTrapKmh_ = new Dictionary<string, float>(StringComparer.Ordinal);

		private string TrapsFile => map_ == null ? "" : TrapsFileFor(map_.Key);

		private string TrapsFileFor(string mapKey) => Path.Combine(CoursesFolder, TuneBackups.SafeFileName(mapKey.Replace(':', '_')), SpeedTrapFile.FileName);

		private void LoadTrapRecords() {
			try {
				trapRecords_ = SpeedTrapLog.Load(TimesFolder);
				freeRoamTraps_ = KSL.API.Kino.Prefs.Get(FreeRoamTrapsPref, true);
				float width = KSL.API.Kino.Prefs.Get(TrapWidthPref, 10f);
				newTrapWidth_ = TrapWidths.OrderBy(w => Math.Abs(w - width)).First();
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

		// Read only: never added to.
		private static readonly List<CourseGate> NoGates = new List<CourseGate>();
		private static readonly List<SpeedTrap> NoTraps = new List<SpeedTrap>();

		private readonly List<SpeedTrap> watched_ = new List<SpeedTrap>();
		private List<SpeedTrap> watchedOf_;
		private int watchedTraps_ = -1;
		private List<CourseGate> watchedScored_;
		private int watchedScoredCount_ = -1;
		private float watchedAt_;

		/// <summary>
		/// The free-roam traps watched: the map's, less those the run scores itself. Worked out again when the traps or the run's
		/// change, and at least every second, not every frame (docs/archive/OPTIMIZATION_REPORT.md item 6). Where each trap is, is read live.
		/// </summary>
		private List<SpeedTrap> WatchedTraps(List<CourseGate> scored) {
			if (!ReferenceEquals(watchedOf_, mapTraps_) || watchedTraps_ != mapTraps_.Count || !ReferenceEquals(watchedScored_, scored) ||
				watchedScoredCount_ != scored.Count || Time.unscaledTime - watchedAt_ >= 1f) {
				watchedOf_ = mapTraps_;
				watchedTraps_ = mapTraps_.Count;
				watchedScored_ = scored;
				watchedScoredCount_ = scored.Count;
				watchedAt_ = Time.unscaledTime;
				watched_.Clear();
				foreach (SpeedTrap t in mapTraps_) {
					if (!scored.Any(c => Vector3.Distance(c.Center, t.Gate.Center) < 3f)) {
						watched_.Add(t);
					}
				}
			}
			return watched_;
		}

		/// <summary>The course being driven (a run, a battle or a race), whose own traps count in it.</summary>
		private Course ActiveCourse => InRace && race_.Course != null ? race_.Course : InBattle ? battle_.Course : run_?.Course;

		/// <summary>The run being driven: a race's, a battle's or a time attack.</summary>
		private TimeAttackRun ActiveRun => InRace ? race_.Run : InBattle ? battle_.Run : run_;

		/// <summary>Traps driven out of the course's (on a circuit, this lap's and the laps before): "3/8", or "3" on a circuit.</summary>
		private static string TrapCountText(TimeAttackRun run) =>
			run.Course.IsCircuit ? run.TrapSpeeds.Count.ToString(CultureInfo.InvariantCulture) : $"{run.TrapSpeeds.Count}/{run.Course.Traps.Count}";

		private static string Ordinal(int n) => n <= 0 ? "-" : n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };

		/// <summary>
		/// Called from UpdateCourses every frame. Free-roam traps work all the time when they're on, during a time
		/// attack, a battle or a race too (since 0.15.0), except one sitting where the course has its own trap.
		/// </summary>
		private void UpdateSpeedTraps() {
			if (!hasPose_) {
				trapWatcher_.Reset();
				trapMarkers_.Show(null, Vector3.zero);
				signs_.Clear();
				return;
			}
			Course active = ActiveCourse;
			TimeAttackRun activeRun = ActiveRun;
			List<CourseGate> courseTraps = active?.Traps ?? NoGates;
			// A free-roam trap where the course has its own is left to the run, but only while the run is really going:
			// after a reset, a stopped run or a race you're out of, every trap records again (bskftw, 0.15.1).
			bool runScores = activeRun != null && activeRun.State == TimeAttackRun.Phase.Running && !activeRun.BeforeLine;
			List<CourseGate> scored = runScores ? courseTraps : NoGates;
			bool freeRoam = freeRoamTraps_ && recorder_.State == CourseRecorder.Phase.Idle;
			List<SpeedTrap> watched = freeRoam ? WatchedTraps(scored) : NoTraps;
			if (watched.Count > 0) {
				SpeedTrapHit? hit = trapWatcher_.Tick(Time.timeAsDouble, pose_.Position, pose_.SpeedKmh, watched);
				if (hit.HasValue) {
					TrapCaught(hit.Value.Trap, hit.Value.Kmh, "freeroam");
				}
			} else {
				trapWatcher_.Reset();
			}

			// A run's course traps (a time attack, a battle or a race): caught by the run itself; flashed with the
			// total so far and saved here. Before 0.15.1 only a time attack's were shown.
			if (activeRun != null && activeRun.LastTrap.HasValue && (!ReferenceEquals(activeRun, runTrapRun_) || activeRun.TrapSpeeds.Count != runTrapsSeen_)) {
				runTrapRun_ = activeRun;
				runTrapsSeen_ = activeRun.TrapSpeeds.Count;
				var (index, kmh) = activeRun.LastTrap.Value;
				string total = activeRun.TrapSpeeds.Count > 1 ? $"Traps {TrapCountText(activeRun)} · total {activeRun.TrapTotal:0} km/h" : "";
				TrapCaught(CourseTrap(activeRun.Course, index), kmh, activeRun.Course.Code, total);
			} else if (activeRun == null || !activeRun.LastTrap.HasValue) {
				runTrapRun_ = null;
				runTrapsSeen_ = 0;
			}

			// The line on the road only for the trap being placed or edited; every trap gets its sign.
			Camera camera = HudCamera();
			UpdateTrapPreview();
			trapMarkers_.Show(camera != null && trapPreview_ != null ? new List<CourseGate> { trapPreview_ } : null, camera != null ? camera.transform.position : Vector3.zero);

			signs_.Clear();
			if (freeRoam) {
				foreach (SpeedTrap trap in mapTraps_) {
					signs_.Add((trap.Key, trap.Gate, trap.Name));
				}
			}
			if (active != null) {
				for (int i = 0; i < courseTraps.Count; i++) {
					SpeedTrap trap = CourseTrap(active, i);
					signs_.Add((trap.Key, trap.Gate, trap.Name));
				}
			}
			if (camera != null && Time.unscaledTime >= nextSignCheck_) {
				nextSignCheck_ = Time.unscaledTime + 0.25f;
				CheckSignsInSight(camera.transform.position);
				// Detailed timing's line under each sign, worked out here (four times a second), not while drawing.
				signDetails_.Clear();
				if (timingDetailed_) {
					foreach (var (key, _, _) in signs_) {
						signDetails_[key] = TrapSignDetail(key);
					}
				}
			}
		}

		/// <summary>A course's trap as a speed trap of its own, for the records (named by the course and its number).</summary>
		private static SpeedTrap CourseTrap(Course course, int index) =>
			new SpeedTrap { Name = $"{course.Name} trap {index + 1}", Gate = course.Traps[index], MapKey = course.MapKey, Source = course.Source };

		/// <summary>The car went through a trap: the speed flashes with how it compares, and it's saved.</summary>
		/// <param name="total">A line under it with the run's total so far, or "".</param>
		private void TrapCaught(SpeedTrap trap, float kmh, string context, string total = "") {
			var mine = trapRecords_.Where(r => r.TrapKey == trap.Key && r.CarId == pose_.CarId).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
			var all = trapRecords_.Where(r => r.TrapKey == trap.Key).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
			string colour = all > 0 && kmh > all ? Theme.PurpleHex : mine <= 0 || kmh >= mine ? Theme.GoodHex : Theme.WarnHex;
			string compare = all > 0 && kmh > all ? "best ever!" : mine > 0 ? $"your best {mine:0} km/h" : "first time through";
			string detail = timingDetailed_ ? TrapFlashDetail(trap.Key, kmh) : "";
			trapFlash_ = $"<b>SPEED TRAP · {trap.Name}</b>\n<color={colour}><b>{kmh:0} km/h</b></color>  <color={Theme.MutedHex}>{compare}</color>{detail}" +
				(total.Length > 0 ? $"\n<color={Theme.WarnHex}>{total}</color>" : "");
			trapFlashUntil_ = Time.unscaledTime + TrapFlashSeconds;
			lastTrapKmh_[trap.Key] = kmh;
			var record = new SpeedTrapRecord {
				When = DateTime.Now, TrapKey = trap.Key, TrapName = trap.Name, MapName = map_?.Name ?? "", Kmh = kmh, CarId = pose_.CarId,
				Car = car_?.CarName ?? "", TuneSet = carStatus_?.Matched == true ? carStatus_.Official.Name : "",
				Spec = carStatus_ != null && StateOf(carStatus_) == SpecState.Spec && modCheck_.Flags.Count == 0, Context = context, Rule = TrapRule(),
			};
			trapRecords_.Add(record);
			try {
				SpeedTrapLog.Append(TimesFolder, record);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			visit_?.Trap(trap.Name, kmh);
			TrcLog.Info($"Speed trap {trap.Name}: {kmh:0.0} km/h ({context}).");
		}

		/// <summary>The rules a speed is caught under: the race's, the battle's or the time attack's; "" in free roam.</summary>
		private string TrapRule() {
			if (InRace) {
				return race_.Settings.Regulation?.Code ?? (race_.Settings.Open ? TimeAttackLog.OpenRule : "");
			}
			if (battle_ != null && battle_.Active) {
				return battle_.Regulation?.Code ?? (battle_.Open ? TimeAttackLog.OpenRule : "");
			}
			return run_ != null && run_.State == TimeAttackRun.Phase.Running ? runRule_ ?? "" : "";
		}

		/// <summary>Options: the signs show your last speed through each trap (0), your fastest in this car (1), or your fastest ever (2).</summary>
		private const string TrapSignPref = "trap_sign_speed";
		private const string GantriesPref = "gantries";
		private int trapSignMode_;


		/// <summary>
		/// For a speed trap race on a course without traps of its own: the map's free-roam traps that sit across the
		/// course's road (near its path, facing along it), in driving order.
		/// </summary>
		private List<CourseGate> TrapsAlong(Course course) {
			var found = new List<(float Progress, CourseGate Gate)>();
			if (course == null || course.Path.Count < 2) {
				return new List<CourseGate>();
			}
			foreach (SpeedTrap trap in mapTraps_) {
				PathPosition at = CourseGeometry.Project(course.Path, course.Distances, trap.Gate.Center, 0, course.Path.Count);
				if (at.Offset > 12f) {
					continue;
				}
				Vector3 along = CourseGeometry.Level(course.Path[Math.Min(at.Segment + 1, course.Path.Count - 1)] - course.Path[at.Segment]);
				float dot = Vector3.Dot(trap.Gate.Forward, along);
				if (Mathf.Abs(dot) < 0.7f) {
					continue;
				}
				var gate = new CourseGate { Center = trap.Gate.Center, Forward = dot < 0 ? -trap.Gate.Forward : trap.Gate.Forward, Width = trap.Gate.Width };
				found.Add((at.Progress, gate));
			}
			return found.OrderBy(f => f.Progress).Select(f => f.Gate).ToList();
		}

		/// <summary>The traps a speed trap race on this course scores: its own, or the free-roam ones along it.</summary>
		private List<CourseGate> RaceTrapsFor(Course course) => course == null ? new List<CourseGate>() : course.Traps.Count > 0 ? course.Traps : TrapsAlong(course);

		/// <summary>
		/// <see cref="RaceTrapsFor"/> for the window, kept for 2 s: the race form lists every course on each redraw,
		/// and measuring every trap against every course's whole path each time cost a tester about 70 fps.
		/// </summary>
		private int RaceTrapCount(Course course) {
			if (course == null) {
				return 0;
			}
			if (Time.realtimeSinceStartup > raceTrapCountsAt_ + 2f) {
				raceTrapCounts_.Clear();
				raceTrapCountsAt_ = Time.realtimeSinceStartup;
			}
			if (!raceTrapCounts_.TryGetValue(course, out int count)) {
				count = RaceTrapsFor(course).Count;
				raceTrapCounts_[course] = count;
			}
			return count;
		}

		private readonly Dictionary<Course, int> raceTrapCounts_ = new Dictionary<Course, int>();
		private float raceTrapCountsAt_ = -10f;
	}
}
