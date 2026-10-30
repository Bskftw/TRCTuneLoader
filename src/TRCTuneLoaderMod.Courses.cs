using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Courses and time attack: the Time attack tab (courses on this map, your times, the course builder), the
	/// time attack and the course builder fed from Update, and what's drawn over the track while they run.
	/// The course logic itself (lines, paths, the run, the recorder, the run log) is in src/Courses/.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int CoursesTab = 5;
		private const string CoursesFolderName = "Courses";
		private const string TimesFolderName = "TimeAttack";
		private const float MapCheckSeconds = 1f;

		/// <summary>How long after reaching a map its objects are looked through, so it has finished loading.</summary>
		private const float MapScanDelaySeconds = 3f;

		/// <summary>How long a finished or stopped run's result stays on screen before the run is ready again.</summary>
		private const float ResultSeconds = 10f;

		/// <summary>Start and finish lines further than this from the camera (metres) aren't drawn.</summary>
		private const float MarkerRange = 100f;

		/// <summary>Teleporting puts the car this far (metres) behind the middle of the start line.</summary>
		private const float StartGridMetres = 4f;
		private const float TeleportCooldownSeconds = 3f;

		private const string HudPositionPref = "timer_position";

		/// <summary>The timer's top centre, as fractions of the screen: top middle.</summary>
		private static readonly Vector2 DefaultHudPosition = new Vector2(0.5f, 0.08f);

		/// <summary>Faster than this (km/h), the course builder won't set a line: the car has to stop on it.</summary>
		private const float SetLineKmh = 3f;

		private static readonly float[] LineWidths = { 8f, 12f, 16f, 20f, 30f };
		/// <summary>The Time attack tab's sections, down the left (since 0.15.0: Battles is its own tab, speed traps have their own section).</summary>
		private static readonly string[] CourseViews = { "Tracks", "Speed traps", "My times", "Course builder" };
		private static readonly string[] CourseKinds = { "Touge", "Highway" };
		private static readonly string[] CourseShapes = { "Point to point", "Circuit" };

		/// <summary>A flying start on a circuit teleports the car this far (metres) before the line, for a run-up.</summary>
		private const float FlyingRunUpMetres = 150f;

		private readonly CourseLibrary courses_ = new CourseLibrary();
		private readonly CourseRecorder recorder_ = new CourseRecorder();
		private bool hasPose_;
		private CarPose pose_;
		private int courseErrors_;
		private MapInfo map_;
		private float nextMapCheck_;
		private float mapSince_;
		private string scannedMapKey_ = "";
		private string scanSummary_ = "";

		private TimeAttackRun run_;
		private RunCheck runStart_;
		private float runResultUntil_;
		private string runResultText_ = "";
		private List<TimeAttackRecord> times_ = new List<TimeAttackRecord>();

		private Vector2 hudPosition_ = DefaultHudPosition;
		private bool draggingHud_;
		private Vector2 hudGrab_;

		/// <summary>Where the timer or message went this OnGUI pass (empty when nothing has the spot), so a challenge can show beside it.</summary>
		private Rect hudRect_;
		private const float HudGap = 8f;
		private float teleportReadyAt_;

		private readonly CourseMarkers markers_ = new CourseMarkers();
		private readonly PathDots dots_ = new PathDots();
		private bool markersLogged_;

		/// <summary>A course whose path is shown as dots on the road (Show path), or null.</summary>
		private Course pathShownFor_;

		/// <summary>Lay out grid, while it runs, and the layout picked for it.</summary>
		private GridRecorder gridRecorder_;
		private int gridLayoutPick_;
		private static readonly string[] GridLayouts = { "Two-wide grid", "Line-up" };

		/// <summary>Record path on a map course, while it runs (and for a few seconds after it's saved).</summary>
		private PathRecorder pathRecorder_;
		private float pathDoneUntil_;

		/// <summary>Map start/finish objects shown for the course that's running, with how they were before, to put back after.</summary>
		private readonly List<(GameObject Object, bool WasActive)> shownObjects_ = new List<(GameObject, bool)>();
		private readonly List<Renderer> shownRenderers_ = new List<Renderer>();
		private Course shownFor_;
		private bool startHasSign_;
		private bool finishHasSign_;

		private int coursesView_;
		private string courseResult_ = "";
		private Course deletePending_;
		private Vector2 coursesScroll_;
		private Vector2 timesScroll_;
		private string newCourseName_ = "";
		private int newCourseKind_;
		private int newCourseShape_;

		/// <summary>Saving a course also makes its reverse (driven the other way), when picked.</summary>
		private bool newCourseReverse_ = true;
		private float newCourseWidth_ = CourseGeometry.DefaultWidth;

		private string CoursesFolder => Path.Combine(modFolder_, CoursesFolderName);
		private string TimesFolder => Path.Combine(modFolder_, TimesFolderName);

		/// <summary>Time attack or the course builder is showing something over the track.</summary>
		private bool CourseHudVisible => hasPose_ && (BattleOnTrack || run_ != null || pathRecorder_ != null || recorder_.State != CourseRecorder.Phase.Idle || invites_.Count > 0 ||
			RaceHudVisible || RaceToShow() != null || gridRecorder_ != null || TrapFlashVisible);

		/// <summary>The car and its TRC spec state at the start or the finish of a run.</summary>
		private sealed class RunCheck {
			public SpecState State;
			public string Set = "";
			public string Code = "";
			public int Flagged;
			public int CarId;
			public string Car = "";
			public string Setup = "";
		}

		private void SetupCourses() {
			courses_.LoadOfficial();
			courses_.LoadCustom(CoursesFolder);
			foreach (string error in courses_.Errors) {
				TrcLog.Warning($"Course problem: {error}");
			}
			LoadTimes();
			LoadTrapRecords();
			TrcLog.Info($"Courses: {courses_.Official.Count} official, {courses_.Custom.Count} of your own. Time attack runs saved: {times_.Count}.");
		}

		private void LoadTimes() {
			try {
				times_ = TimeAttackLog.Load(TimesFolder);
			} catch (Exception e) {
				times_ = new List<TimeAttackRecord>();
				TrcLog.Error(e);
			}
		}

		/// <summary>Called from Update every frame.</summary>
		private void UpdateCourses() {
			hasPose_ = adapter_.TryGetCarPose(out pose_);
			UpdateMap();

			if (run_ != null && hasPose_) {
				TickRun();
			}
			if (pathRecorder_ != null && hasPose_) {
				TickPathRecorder();
			}
			if (recorder_.State == CourseRecorder.Phase.Recording && hasPose_) {
				recorder_.Tick(pose_.Position);
			}
			if (gridRecorder_ != null && hasPose_) {
				gridRecorder_.Tick(pose_.Position);
				if (gridRecorder_.State == GridRecorder.Phase.Done) {
					SaveGrid();
				}
			}
			UpdateCourseObjects();
			UpdateSpeedTraps();
			UpdateBestLine();
		}

		/// <summary>The start/finish markers and the map's own start/finish signs for whatever is running: a time attack, Record path or the course builder.</summary>
		private void UpdateCourseObjects() {
			Course battleCourse = battle_ != null && battle_.Started && battle_.Active ? battle_.Course : null;
			Course raceCourse = InRace ? race_.Course : null;
			Course course = hasPose_ ? raceCourse ?? battleCourse ?? run_?.Course ?? pathRecorder_?.Course ?? gridRecorder_?.Course : null;
			ShowSigns(course);
			UpdatePathDots();

			CourseGate start = course != null ? course.Start : recorder_.Start;
			CourseGate finish = course != null ? course.Finish : recorder_.Finish;
			if (!hasPose_ || (start == null && finish == null) || !CourseMarkers.Available) {
				markers_.Clear();
				return;
			}
			if (!markersLogged_) {
				markersLogged_ = true;
				TrcLog.Info($"Start and finish lines are drawn with {CourseMarkers.ShaderName}.");
			}
			markers_.Show(start, finish, course != null && startHasSign_, course != null && finishHasSign_);
			Camera camera = HudCamera();
			if (camera != null) {
				markers_.UpdateVisibility(camera.transform.position);
			}
		}

		/// <summary>
		/// Shows a map course's start and finish objects while it runs, for signs the map keeps hidden until then,
		/// and puts them back the way they were afterwards. An object with a renderer is a sign, and the mod then
		/// draws no line of its own there.
		/// </summary>
		private void ShowSigns(Course course) {
			if (course == shownFor_) {
				return;
			}
			RestoreSigns();
			shownFor_ = course;
			if (course != null) {
				startHasSign_ = Reveal(course.StartObject);
				finishHasSign_ = Reveal(course.FinishObject);
			}
		}

		private bool Reveal(Transform target) {
			if (target == null) {
				return false;
			}
			GameObject sign = target.gameObject;
			if (!sign.activeSelf) {
				shownObjects_.Add((sign, false));
				sign.SetActive(true);
			}
			bool hasRenderer = false;
			foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true)) {
				hasRenderer = true;
				if (!renderer.enabled) {
					renderer.enabled = true;
					shownRenderers_.Add(renderer);
				}
			}
			return hasRenderer;
		}

		private void RestoreSigns() {
			foreach (Renderer renderer in shownRenderers_) {
				if (renderer != null) {
					renderer.enabled = false;
				}
			}
			foreach (var (sign, wasActive) in shownObjects_) {
				if (sign != null) {
					sign.SetActive(wasActive);
				}
			}
			shownRenderers_.Clear();
			shownObjects_.Clear();
			shownFor_ = null;
			startHasSign_ = false;
			finishHasSign_ = false;
		}

		/// <summary>
		/// The path as dots on the road: what the course builder or Record path has recorded so far, or a course
		/// picked with Show path. Never during a battle.
		/// </summary>
		private void UpdatePathDots() {
			IList<Vector3> points = !hasPose_ || InBattle || InRace ? null
				: gridRecorder_ != null ? gridRecorder_.Points
				: recorder_.State != CourseRecorder.Phase.Idle ? recorder_.Points
				: pathRecorder_ != null && pathRecorder_.State != PathRecorder.Phase.ToStart ? pathRecorder_.Points
				: pathShownFor_?.Path;
			Camera camera = points != null ? HudCamera() : null;
			if (camera == null) {
				dots_.Clear();
				return;
			}
			dots_.Show(points, camera.transform.position);
		}

		/// <summary>Called from OnDestroy: nothing the mod made or showed stays in the map.</summary>
		private void CleanUpCourses() {
			markers_.Clear();
			dots_.Clear();
			trapMarkers_.Clear();
			speedLine_.Clear();
			RestoreSigns();
		}

		/// <summary>Notices a new map (or leaving one), and looks through a new map's objects for TRC courses once it has loaded.</summary>
		private void UpdateMap() {
			if (Time.unscaledTime >= nextMapCheck_) {
				nextMapCheck_ = Time.unscaledTime + MapCheckSeconds;
				MapInfo map = null;
				if (hasPose_ && adapter_.TryGetMap(out map, out _)) {
					CourseLibrary.Recognize(map);
				}

				if ((map?.Key ?? "") != (map_?.Key ?? "")) {
					mapSince_ = Time.unscaledTime;
					// Look through the new map's objects even when it shares its key with the last one (another upload of it).
					scannedMapKey_ = "";
					if (run_ != null) {
						TrcLog.Info($"Time attack on {run_.Course.Name} stopped: left the map.");
						courseResult_ = "Time attack stopped: you left the map.";
						EndCircuitRunEarly("stopped: left the map");
						run_ = null;
					}
					pathShownFor_ = null;
					if (recorder_.State != CourseRecorder.Phase.Idle) {
						recorder_.Clear();
						courseResult_ = "Course builder cleared: you left the map.";
					}
					pathRecorder_ = null;
					if (map != null) {
						TrcLog.Info($"On map {map.Name} ({map.Key}{(map.UploadKey.Length > 0 ? $", upload {map.UploadKey}" : "")}).");
					}
				}
				map_ = map;
			}

			if (map_ != null && hasPose_ && scannedMapKey_ != map_.Key && Time.unscaledTime - mapSince_ >= MapScanDelaySeconds) {
				ScanMap();
			}
		}

		private void ScanMap() {
			scannedMapKey_ = map_.Key;
			try {
				List<Course> found = CourseScanner.Scan(map_.Key, map_.Name, out scanSummary_);
				courses_.SetMapCourses(map_.Key, found);
				RefreshTraps();
				TrcLog.Info($"{map_.Name} ({map_.Key}): {scanSummary_}");
				foreach (string line in CourseScanner.Lines) {
					TrcLog.Info($"  {line}");
				}
				foreach (Course course in found) {
					TrcLog.Info($"  {course.Name}: {(course.Usable ? $"{course.Length:0} m, {(course.HasPath ? "with" : "without")} a path, code {course.Code}" : "not usable")}");
					foreach (string problem in course.Problems) {
						TrcLog.Info($"    {problem}");
					}
				}
			} catch (Exception e) {
				scanSummary_ = $"Couldn't look through the map: {e.Message}";
				TrcLog.Error(e);
			}
		}

		// ---- Time attack ----

		/// <summary>A battle is lined up or running, so time attack, Record path and the course builder wait.</summary>
		private bool InBattle => battle_ != null && battle_.Started && battle_.Active;

		private void StartTimeAttack(Course course) {
			if (InBattle || InRace) {
				courseResult_ = InRace ? "Finish or leave your race first." : "Finish or leave your battle first.";
				return;
			}
			gridRecorder_ = null;
			if (recorder_.State != CourseRecorder.Phase.Idle) {
				courseResult_ = "Finish or clear the course builder first.";
				return;
			}
			pathRecorder_ = null;
			pathShownFor_ = null;
			run_ = new TimeAttackRun(course, course.IsCircuit ? PickedLaps : 1, PickedFlying);
			runStart_ = null;
			runResultUntil_ = 0;
			ResetRunExtras();
			courseResult_ = "";
			string laps = course.IsCircuit ? (run_.Laps == 0 ? ", unlimited laps" : $", {run_.Laps} lap(s)") : "";
			TrcLog.Info($"Time attack ready on {course.Name} ({course.Code}, {course.SourceName}, {(run_.Flying ? "flying" : "standing")} start{laps}, {course.SectorEnds.Length} sectors).");
			CloseWindow();
		}

		private void StopTimeAttack() {
			courseResult_ = "";
			if (run_ != null) {
				TrcLog.Info($"Time attack on {run_.Course.Name} stopped by the player.");
				EndCircuitRunEarly("stopped by the player");
			}
			run_ = null;
		}

		// ---- Record path ----

		/// <summary>Starts recording a path for a map course: drive it from its start line to its finish line once.</summary>
		private void StartRecordPath(Course course) {
			if (recorder_.State != CourseRecorder.Phase.Idle || run_ != null || InBattle) {
				courseResult_ = "Stop the time attack and clear the course builder first.";
				return;
			}
			pathRecorder_ = new PathRecorder(course);
			courseResult_ = "";
			TrcLog.Info($"Record path started on {course.Name}.");
			CloseWindow();
		}

		private void TickPathRecorder() {
			if (pathRecorder_.State == PathRecorder.Phase.Done) {
				if (Time.unscaledTime >= pathDoneUntil_) {
					pathRecorder_ = null;
				}
				return;
			}
			pathRecorder_.Tick(pose_.Position);
			if (pathRecorder_.State == PathRecorder.Phase.Done) {
				pathDoneUntil_ = Time.unscaledTime + ResultSeconds;
				SaveRecordedPath();
			}
		}

		/// <summary>
		/// Saves the recorded path as a course file with the map course's own lines (replacing an older recording
		/// of the same course), then looks through the map again so the map course takes it.
		/// </summary>
		private void SaveRecordedPath() {
			try {
				Course recorded = pathRecorder_.Build($"steam:{Kino.Info.SteamId.ToString(CultureInfo.InvariantCulture)}");
				foreach (Course old in courses_.Custom.Where(c => c.IsCircuit == recorded.IsCircuit && c.SameLines(recorded)).ToList()) {
					CourseLibrary.DeleteCustom(old, out _);
				}
				string path = CourseLibrary.SaveCustom(CoursesFolder, recorded);
				courses_.LoadCustom(CoursesFolder);
				scannedMapKey_ = "";
				mapSince_ = Time.unscaledTime - MapScanDelaySeconds;
				courseResult_ = $"Path recorded for {recorded.Name}: {TimeAttackRun.Distance(recorded.Length)}. Recorded paths are for practice; official ones come with the mod.";
				TrcLog.Info($"Path recorded for {recorded.Name}: {path} ({recorded.Length:0} m, code {recorded.Code}).");
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the path: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private void TickRun() {
			// On a map course with TRC road objects: is any part of the car over them (null without road objects)?
			bool? onRoad = null;
			HashSet<int> road = run_.Course.RoadColliders;
			if (road.Count > 0 && run_.State == TimeAttackRun.Phase.Running && adapter_.TryCheckOverRoad(road, out bool overRoad)) {
				onRoad = overRoad;
			}

			TimeAttackRun.Phase before = run_.State;
			run_.Tick(Time.timeAsDouble, pose_.Position, pose_.Forward, pose_.SpeedKmh, Time.timeScale, onRoad);
			TimeAttackRun.Phase now = run_.State;

			if (before == TimeAttackRun.Phase.ToStart && now == TimeAttackRun.Phase.Countdown) {
				// Checked while the car stands still, so it doesn't cost a frame at GO.
				runStart_ = CheckRunCar();
			}
			if (run_.Flying && now == TimeAttackRun.Phase.ToStart && (runStart_ == null || runStart_.CarId != pose_.CarId)) {
				// A flying start has no standstill: the car is checked on the way to the line instead.
				runStart_ = CheckRunCar();
			}
			if (now == TimeAttackRun.Phase.Running && runStart_ != null && pose_.CarId != runStart_.CarId) {
				run_.Cancel("You changed car, so the run was stopped.");
				now = run_.State;
			}

			HandleRunProgress();
			if (before == TimeAttackRun.Phase.Running && now == TimeAttackRun.Phase.Finished) {
				FinishRun();
				RateRun(run_);
			} else if (before != TimeAttackRun.Phase.Cancelled && now == TimeAttackRun.Phase.Cancelled) {
				RateRun(run_);
				TrcLog.Info($"Time attack on {run_.Course.Name} stopped: {run_.Message}");
				string laps = LapSummary(run_);
				runResultText_ = laps.Length > 0 ? $"{run_.Message}\n<color={Theme.MutedHex}>{laps}</color>" : run_.Message;
				runResultUntil_ = Time.unscaledTime + ResultSeconds;
			}

			if (!run_.Active && Time.unscaledTime >= runResultUntil_) {
				run_.Restart();
				runStart_ = null;
				ResetRunExtras();
			}
		}

		private RunCheck CheckRunCar() {
			var check = new RunCheck { CarId = pose_.CarId };
			try {
				modCheck_.Refresh();
				check.Flagged = modCheck_.Flags.Count;
				if (RefreshCar()) {
					check.Car = car_.CarName;
					check.Setup = car_.SlotName;
					OwnCheck own = CheckOwnCar(recordInGarage: false);
					carStatus_ = own;
					check.State = StateOf(own);
					if (own.Saved != null) {
						check.Code = own.Saved.Code;
						check.Set = own.Matched ? own.Official.Name : "";
					}
				}
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			return check;
		}

		private void FinishRun() {
			RunCheck end = CheckRunCar();
			RunCheck start = runStart_ ?? end;
			Course course = run_.Course;

			TimeAttackRecord record = RecordFor(start, end.State, (long)Math.Round(run_.Result * 1000.0, MidpointRounding.AwayFromZero));
			record.FlaggedMods = Math.Max(start.Flagged, end.Flagged);
			record.Flying = run_.Flying;
			record.Laps = run_.Laps;
			record.TrapKmh = run_.TrapSpeeds.ToArray();
			LapTime only = !course.IsCircuit && run_.LapTimes.Count == 1 ? run_.LapTimes[0] : null;
			if (only != null) {
				record.SectorMs = only.Sectors.Select(s => (long)Math.Round(s * 1000.0, MidpointRounding.AwayFromZero)).ToArray();
			}

			// Compared with the same car on the same TRC tune set, started the same way: each has its own times.
			TimeKind kind = RunKind(run_);
			TimeAttackRecord best = TimeAttackLog.BestFor(times_, course.Code, record.CarId, record.TuneSet, record.IsSpec, kind);
			SaveRecord(record);
			if (only != null) {
				string key = TimeAttackTraces.Key(course.Code, record.CarId, record.TuneSet, kind);
				if (TimeAttackTraces.SaveIfFaster(TimesFolder, key, only.Trace, record.TimeMs)) {
					TimeAttackTraces.SaveLine(TimesFolder, key, only.Line, only.LineKmh, record.TimeMs);
				}
			}

			string compare = best == null ? $"first time in this car{(record.TuneSet.Length > 0 ? $" on {record.TuneSet}" : "")}"
				: record.TimeMs < best.TimeMs ? $"new best by {TimeAttackRun.FormatTime((best.TimeMs - record.TimeMs) / 1000.0)}"
				: $"+{TimeAttackRun.FormatTime((record.TimeMs - best.TimeMs) / 1000.0)} on your best";
			string status = !record.IsSpec ? $"<color={Theme.BadHex}>{DoesntCount(record)}</color>"
				: course.Official ? $"<color={Theme.GoodHex}>{record.TuneSet} spec</color>"
				: $"{record.TuneSet} spec · practice course";
			string laps = LapSummary(run_);
			string sectors = showSectors_ && only != null && only.Sectors.Length > 1 ? $"\n{ResultSectors(only)}" : "";
			if (record.TrapKmh.Length > 0) {
				sectors += $"\n<color={Theme.WarnHex}>Speed traps: {string.Join(" · ", record.TrapKmh.Select(k => $"{k:0}"))} km/h (total {record.TrapTotal:0})</color>";
			}
			runResultText_ = $"<b>{TimeAttackRun.FormatTime(record.Seconds)}</b>\n{compare} · {status}{(laps.Length > 0 ? $"\n<color={Theme.MutedHex}>{laps}</color>" : "")}{sectors}";
			runResultUntil_ = Time.unscaledTime + ResultSeconds;
			TrcLog.Info($"Time attack on {course.Name} ({course.Code}): {TimeAttackRun.FormatTime(record.Seconds)} in {record.Car}, " +
				$"tune {record.TuneCode} {record.TuneSet}, {record.Spec}, flagged mods {record.FlaggedMods}, {(run_.Flying ? "flying" : "standing")} start, " +
				$"jump starts {run_.JumpStarts}{(laps.Length > 0 ? $", {laps}" : "")}{(only != null ? $", sectors {string.Join(" / ", only.Sectors.Select(s => TimeAttackRun.FormatTime(s)))}" : "")}.");
		}

		/// <summary>"spec" only when the car was TRC spec at both the start and the finish.</summary>
		private static string SpecWord(SpecState start, SpecState end) {
			if (start == SpecState.Spec && end == SpecState.Spec) {
				return TimeAttackLog.SpecWord;
			}
			if (start == SpecState.NotSpec || end == SpecState.NotSpec) {
				return "not spec";
			}
			return start == SpecState.None || end == SpecState.None ? "no TRC tune" : "not fully checked";
		}

		private static string DoesntCount(TimeAttackRecord record) =>
			record.FlaggedMods > 0 ? $"{record.FlaggedMods} flagged mod(s): doesn't count"
			: record.Spec == "not fully checked" ? "the live car wasn't checked yet: doesn't count"
			: record.Spec == "no TRC tune" ? "no TRC tune for this car: doesn't count"
			: "not TRC spec: doesn't count";

		// ---- Lay out grid ----

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
				scannedMapKey_ = "";
				mapSince_ = Time.unscaledTime - MapScanDelaySeconds;
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

		// ---- Courses tab ----

		private void DrawCoursesTab() {
			GUILayout.Label(map_ == null
				? "Drive onto a map to see its tracks and start a time attack. Your times are here too."
				: $"Map: <b>{map_.Name}</b> <color={Theme.MutedHex}>({map_.Key}{(map_.UploadKey.Length > 0 ? $", upload {map_.UploadKey}" : "")})</color>", Theme.Body);
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			int tracks = map_ == null ? 0 : courses_.ForMap(map_.Key).Count;
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
				if (GUILayout.Button("Teleport to start", Theme.ButtonPrimary)) {
					TeleportToStart();
				}
				if (GUILayout.Button("Stop time attack", Theme.Button)) {
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
				if (pathRecorder_.State != PathRecorder.Phase.Done && GUILayout.Button("Teleport to start", Theme.ButtonPrimary)) {
					TeleportToStart();
				}
				if (pathRecorder_ != null && GUILayout.Button(pathRecorder_.State == PathRecorder.Phase.Done ? "Close" : "Stop recording", Theme.Button)) {
					pathRecorder_ = null;
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
			if (map_ == null) {
				return;
			}

			List<Course> here = courses_.ForMap(map_.Key);
			if (run_ == null && here.Count > 0) {
				DrawStartOptions(here.Any(c => c.IsCircuit));
				GUILayout.BeginHorizontal();
				GUILayout.Label("Lay out grid as", Theme.SectionLabel, GUILayout.Width(110));
				gridLayoutPick_ = Choice(GridLayouts, gridLayoutPick_);
				GUILayout.EndHorizontal();
			}
			GUILayout.BeginVertical(Theme.Card, GUILayout.ExpandHeight(true));
			coursesScroll_ = GUILayout.BeginScrollView(coursesScroll_);
			if (here.Count == 0) {
				GUILayout.Label(scannedMapKey_ == map_.Key
					? "No courses on this map yet. Make your own with the Course builder."
					: "Looking for courses...", Theme.Small);
			}
			foreach (Course course in here) {
				DrawCourseRow(course);
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Look for course objects again", Theme.Button)) {
				scannedMapKey_ = "";
				mapSince_ = Time.unscaledTime - MapScanDelaySeconds;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!string.IsNullOrEmpty(scanSummary_)) {
				GUILayout.Label(scanSummary_, Theme.Small);
			}
		}

		/// <summary>A track: its name and details, then its buttons on a line of their own (the list is narrower beside the sections).</summary>
		private void DrawCourseRow(Course course) {
			string length = course.Usable ? TimeAttackRun.Distance(course.Length) : "can't be used yet";
			GUILayout.Label($"<b>{course.Name}</b>  <color={Theme.MutedHex}>{course.KindName} · {length} · {course.SourceName}</color>", Theme.Body);
			GUILayout.Label($"Course code {course.Code}{BestText(course)}{(course.Usable ? $" · {course.SectorEnds.Length} sectors" : "")}", Theme.Small);
			foreach (string problem in course.Problems) {
				GUILayout.Label($"<color={Theme.WarnHex}>{problem}</color>", Theme.Small);
			}

			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && course.Usable && hasPose_;
			if (GUILayout.Button("Time attack", Theme.ButtonPrimary, GUILayout.Width(110))) {
				StartTimeAttack(course);
			}
			// A map course gets its path from driving it once (or a new one, replacing the player's own).
			if (course.Source == CourseSource.Map && (!course.HasPath || course.PathFromPlayer) &&
				GUILayout.Button(course.HasPath ? "Record again" : "Record path", Theme.Button)) {
				StartRecordPath(course);
			}
			GUI.enabled = enabled && course.HasPath && hasPose_;
			if (GUILayout.Button(pathShownFor_ == course ? "Hide path" : "Show path", Theme.Button)) {
				pathShownFor_ = pathShownFor_ == course ? null : course;
			}
			// Race grids: drive the road up to the start line (official courses and maps with grid spots have their own).
			GUI.enabled = enabled && hasPose_ && course.Source != CourseSource.Official && course.GridObjects.Count == 0 && run_ == null && !InBattle && !InRace;
			if (GUILayout.Button(gridRecorder_?.Course == course ? "Stop grid" : "Lay out grid", Theme.Button)) {
				StartGrid(gridRecorder_?.Course == course ? null : course);
			}
			GUI.enabled = enabled;

			if (course.Source == CourseSource.Custom && GUILayout.Button(editing_ == course ? "Done" : "Edit", Theme.Button)) {
				StartEditing(editing_ == course ? null : course);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (editing_ == course) {
				DrawCourseEditor(course);
			}
			GUILayout.Space(4);
			DividerLine();
			GUILayout.Space(4);
		}

		private string BestText(Course course) {
			TimeAttackRecord spec = TimeAttackLog.BestSpec(times_, course.Code);
			if (spec != null) {
				return $" · your best: <color={Theme.GoodHex}>{TimeAttackRun.FormatTime(spec.Seconds)}</color> ({spec.Car}, {spec.TuneSet})";
			}
			TimeAttackRecord any = TimeAttackLog.Best(times_, course.Code);
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
			if (GUILayout.Button(primary, Theme.ButtonPrimary)) {
				onPrimary();
			}
			bool enabled = GUI.enabled;
			GUI.enabled = true;
			if (startOver && GUILayout.Button("Start over", Theme.Button)) {
				recorder_.Clear();
				courseResult_ = "";
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		// ---- Over the track ----

		/// <summary>Called from OnGUI while time attack or the course builder runs: the lines on the road and the panel at the top.</summary>
		private void DrawCourseHud(float screenWidth, float screenHeight, float scale) {
			// The lines are 3D objects (CourseMarkers); drawn over the screen only when no shader for those was found.
			if (Event.current.type == EventType.Repaint && !CourseMarkers.Available) {
				Camera camera = HudCamera();
				Course course = (BattleOnTrack ? battle_.Course : null) ?? run_?.Course ?? pathRecorder_?.Course;
				if (camera != null) {
					DrawGate(camera, course != null ? course.Start : recorder_.Start, Theme.StartLine, "START", Theme.MarkerStart, scale);
					DrawGate(camera, course != null ? course.Finish : recorder_.Finish, Theme.FinishLine, "FINISH", Theme.MarkerFinish, scale);
				}
			}

			hudRect_ = Rect.zero;
			if (RaceHudVisible) {
				DrawRaceHud(screenWidth, screenHeight);
			} else if (BattleOnTrack) {
				DrawBattleHud(screenWidth, screenHeight);
			} else if (gridRecorder_ != null) {
				DrawHudPill(screenWidth, screenHeight, $"<b>Lay out grid · {gridRecorder_.Course.Name}</b> · {TimeAttackRun.Distance(gridRecorder_.Recorded)}\n<color={Theme.MutedHex}>{gridRecorder_.Message}</color>");
			} else if (run_ != null) {
				DrawRunHud(screenWidth, screenHeight);
			} else if (pathRecorder_ != null) {
				string recorded = pathRecorder_.State == PathRecorder.Phase.ToStart ? "" : $" · {TimeAttackRun.Distance(pathRecorder_.Recorded)}";
				DrawHudPill(screenWidth, screenHeight, $"<b>Record path · {pathRecorder_.Course.Name}</b>{recorded}\n<color={Theme.MutedHex}>{pathRecorder_.Message}</color>");
			} else if (recorder_.State != CourseRecorder.Phase.Idle) {
				string next = !recorder_.Circuit ? $"Stop on the finish line, then {shortcut_}: Set finish here"
					: recorder_.Recorded < CourseGeometry.CircuitMinLength ? "Drive once round the loop"
					: $"Back through the start line closes the loop: {TimeAttackRun.Distance(recorder_.DistanceToStart)}";
				DrawHudPill(screenWidth, screenHeight, recorder_.State == CourseRecorder.Phase.Recording
					? $"<b>Recording the {(recorder_.Circuit ? "circuit" : "course")}</b> · {TimeAttackRun.Distance(recorder_.Recorded)}\n<color={Theme.MutedHex}>{next}</color>"
					: $"<b>{(recorder_.Circuit ? "Loop closed" : "Course recorded")}</b>\n<color={Theme.MutedHex}>{shortcut_} to name and save it</color>");
			}
			// A challenge always shows, even during a time attack or a recording; so does a race someone just opened.
			if (invites_.Count > 0) {
				DrawInviteHud(screenWidth, screenHeight);
			} else if (RaceToShow() != null) {
				DrawRaceInviteHud(screenWidth, screenHeight);
			}
			DrawTrapFlash(screenWidth, screenHeight);
		}

		private void DrawRunHud(float screenWidth, float screenHeight) {
			double now = Time.timeAsDouble;
			string big = run_.State == TimeAttackRun.Phase.Countdown ? run_.CountdownNumber.ToString(CultureInfo.InvariantCulture)
				: run_.ShowGo(now) ? "GO!"
				: null;
			if (big != null) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), big, Theme.HudBig);
			}

			switch (run_.State) {
				case TimeAttackRun.Phase.Running:
					DrawRunTimer(screenWidth, screenHeight);
					break;
				case TimeAttackRun.Phase.Finished:
				case TimeAttackRun.Phase.Cancelled:
					DrawHudPill(screenWidth, screenHeight, $"<b>{run_.Course.Name}</b>\n{runResultText_}");
					break;
				default:
					DrawHudPill(screenWidth, screenHeight, $"<b>{run_.Course.Name}</b>\n<color={Theme.MutedHex}>{run_.Message}</color>");
					break;
			}
		}

		/// <summary>
		/// The running clock: the time (<paramref name="main"/>, else the run's), a thin line under it for how far
		/// along the course the car is, and short lines under that, like "Ahead by 12 m" or "Lap 2/5".
		/// </summary>
		private void DrawTimer(float screenWidth, float screenHeight, TimeAttackRun run, string below = null, double? main = null) {
			int lines = string.IsNullOrEmpty(below) ? 0 : below.Split('\n').Length;
			float width = Mathf.Max(lines > 0 ? 240f : 150f, Theme.HudTimer.CalcSize(new GUIContent("00:00.000")).x + 32f);
			bool extra = lines > 0;
			Rect rect = HudRect(screenWidth, screenHeight, width, 50f + 18f * lines);
			hudRect_ = rect;
			HandleHudDrag(rect, screenWidth, screenHeight);
			if (Event.current.type != EventType.Repaint) {
				return;
			}

			GUI.Box(rect, GUIContent.none, Theme.HudPanel);
			GUI.Label(new Rect(rect.x, rect.y + 3f, rect.width, 36f), TimeAttackRun.FormatTime(main ?? run.Elapsed), Theme.HudTimer);
			if (extra) {
				GUI.Label(new Rect(rect.x, rect.y + 38f, rect.width, 18f * lines), below, Theme.HudSmall);
			}
			var bar = new Rect(rect.x + 14f, rect.yMax - 9f, rect.width - 28f, 3f);
			float done = run.Course.Length > 0 ? Mathf.Clamp01(run.Progress / run.Course.Length) : 0f;
			GUI.DrawTexture(bar, Theme.ProgressBack);
			GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * done, bar.height), Theme.AccentLine);
			DrawMoveHint(rect);
		}

		/// <summary>A short message in the timer's spot: where to go before the start, the result after the finish.</summary>
		private void DrawHudPill(float screenWidth, float screenHeight, string text) {
			var content = new GUIContent(text);
			float width = Mathf.Min(340f, screenWidth - 2 * HudMargin);
			Rect rect = HudRect(screenWidth, screenHeight, width, Theme.HudPill.CalcHeight(content, width));
			hudRect_ = rect;
			HandleHudDrag(rect, screenWidth, screenHeight);
			if (Event.current.type == EventType.Repaint) {
				GUI.Label(rect, content, Theme.HudPill);
				DrawMoveHint(rect);
			}
		}

		/// <summary>The timer's place: its top centre sits at <see cref="hudPosition_"/> (fractions of the screen), kept on screen.</summary>
		private Rect HudRect(float screenWidth, float screenHeight, float width, float height) {
			float x = Mathf.Clamp(hudPosition_.x * screenWidth - width / 2f, 0f, Mathf.Max(0f, screenWidth - width));
			float y = Mathf.Clamp(hudPosition_.y * screenHeight, 0f, Mathf.Max(0f, screenHeight - height));
			return new Rect(x, y, width, height);
		}

		/// <summary>While the TRC window is open (so the mouse is free), the timer can be dragged anywhere. The place is saved.</summary>
		private void HandleHudDrag(Rect rect, float screenWidth, float screenHeight) {
			if (!windowOpen_) {
				draggingHud_ = false;
				return;
			}
			Event e = Event.current;
			switch (e.type) {
				case EventType.MouseDown:
					if (e.button == 0 && rect.Contains(e.mousePosition)) {
						draggingHud_ = true;
						hudGrab_ = e.mousePosition - new Vector2(rect.center.x, rect.y);
						e.Use();
					}
					break;
				case EventType.MouseDrag:
					if (draggingHud_) {
						Vector2 at = e.mousePosition - hudGrab_;
						hudPosition_ = new Vector2(Mathf.Clamp01(at.x / screenWidth), Mathf.Clamp01(at.y / screenHeight));
						e.Use();
					}
					break;
				case EventType.MouseUp:
					if (draggingHud_) {
						draggingHud_ = false;
						SavePref(HudPositionPref, hudPosition_);
						e.Use();
					}
					break;
			}
		}

		/// <summary>An outline and "drag to move" under the timer while the window is open.</summary>
		private void DrawMoveHint(Rect rect) {
			if (!windowOpen_) {
				return;
			}
			GUI.Box(rect, GUIContent.none, Theme.HudOutline);
			GUI.Label(new Rect(rect.x, rect.yMax + 2f, rect.width, 18f), "drag to move", Theme.HudSmall);
		}

		// ---- Teleport ----

		/// <summary>Puts the car just behind the start line with CarX's own placement, and gets the run ready to go again.</summary>
		private void TeleportToStart() {
			Course course = run_?.Course ?? pathRecorder_?.Course;
			if (course == null) {
				return;
			}
			if (Time.unscaledTime < teleportReadyAt_) {
				courseResult_ = "Wait a moment before teleporting again.";
				return;
			}
			CourseGate start = course.Start;
			Vector3 spot = start.Center - start.Forward * StartGridMetres;
			Vector3 facing = start.Forward;
			if (run_ != null && run_.Flying && course.IsCircuit && course.HasPath) {
				// A flying lap needs a run-up: back along the end of the loop.
				int at = Array.FindIndex(course.Distances, d => d >= course.Length - FlyingRunUpMetres);
				if (at > 0 && at < course.Path.Count - 1) {
					spot = course.Path[at];
					facing = CourseGeometry.Level(course.Path[at + 1] - course.Path[at]);
				}
			}
			if (!adapter_.TryMoveCarTo(spot, facing, out string error)) {
				courseResult_ = $"Couldn't teleport: {error}";
				return;
			}
			// Restarted in the same frame, so the move isn't taken for a reset in the middle of a run.
			teleportReadyAt_ = Time.unscaledTime + TeleportCooldownSeconds;
			if (run_ != null) {
				EndCircuitRunEarly("restarted");
				run_.Restart();
				runStart_ = null;
				runResultUntil_ = 0;
				ResetRunExtras();
			} else {
				pathRecorder_ = new PathRecorder(course);
			}
			courseResult_ = "";
			TrcLog.Info($"Teleported to the start of {course.Name}.");
			CloseWindow();
		}

		/// <summary>A start or finish line drawn on the road, with its name above the middle.</summary>
		private static void DrawGate(Camera camera, CourseGate gate, Texture2D line, string label, GUIStyle labelStyle, float scale) {
			if (gate == null || Vector3.Distance(camera.transform.position, gate.Center) > MarkerRange) {
				return;
			}
			Vector3 lift = Vector3.up * 0.3f;
			if (ToGui(camera, gate.LeftEnd + lift, scale, out Vector2 left) && ToGui(camera, gate.RightEnd + lift, scale, out Vector2 right)) {
				DrawLine(left, right, 4f, line);
			}
			if (ToGui(camera, gate.Center + Vector3.up * 2.5f, scale, out Vector2 top)) {
				var content = new GUIContent(label);
				Vector2 size = labelStyle.CalcSize(content);
				GUI.Label(new Rect(top.x - size.x / 2f, top.y - size.y, size.x, size.y), content, labelStyle);
			}
		}

		/// <summary>Where a point in the world is in the window's (scaled) coordinates. False when it's behind the camera.</summary>
		private static bool ToGui(Camera camera, Vector3 world, float scale, out Vector2 gui) {
			Vector3 viewport = camera.WorldToViewportPoint(world);
			gui = new Vector2(viewport.x * Screen.width / scale, (1f - viewport.y) * Screen.height / scale);
			return viewport.z > 0.5f;
		}

		private static void DrawLine(Vector2 from, Vector2 to, float width, Texture2D texture) {
			Matrix4x4 saved = GUI.matrix;
			float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
			GUIUtility.RotateAroundPivot(angle, from);
			GUI.DrawTexture(new Rect(from.x, from.y - width / 2f, (to - from).magnitude, width), texture);
			GUI.matrix = saved;
		}

		/// <summary>The camera the player sees the track through.</summary>
		private static Camera HudCamera() {
			Camera main = Camera.main;
			if (main != null && main.isActiveAndEnabled) {
				return main;
			}
			Camera best = null;
			foreach (Camera camera in Camera.allCameras) {
				if (camera.targetTexture == null && (best == null || camera.depth > best.depth)) {
					best = camera;
				}
			}
			return best;
		}
	}
}
