// Time attack in game: the shared state (courses, the run, times), setup, and the course objects on the road updated each frame.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Time attack in game, fed from Update: the courses, the run and the times every part shares. The pages are in
	/// src/pages/ (TracksPage, CourseBuilderPage, TimeAttackHud); running a time attack, the map, the course builder,
	/// the gantries and map icons are their own files here; the course logic itself is the other files in src/courses/.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int CoursesTab = 5;
		private const string CoursesFolderName = "Courses";
		private const string TimesFolderName = "TimeAttack";

		/// <summary>How long a finished or stopped run's result stays on screen before the run is ready again.</summary>
		private const float ResultSeconds = 10f;

		private readonly CourseLibrary courses_ = new CourseLibrary();
		private readonly CourseRecorder recorder_ = new CourseRecorder();
		private bool hasPose_;
		private CarPose pose_;
		private int courseErrors_;

		private TimeAttackRun run_;
		private RunCheck runStart_;
		private float runResultUntil_;
		private string runResultText_ = "";
		private List<TimeAttackRecord> times_ = new List<TimeAttackRecord>();

		private readonly CourseMarkers markers_ = new CourseMarkers();
		private readonly PathDots dots_ = new PathDots();
		private bool markersLogged_;

		/// <summary>A course whose path is shown as dots on the road (Show path), or null.</summary>
		private Course pathShownFor_;

		/// <summary>Map start/finish objects shown for the course that's running, with how they were before, to put back after.</summary>
		private readonly List<(GameObject Object, bool WasActive)> shownObjects_ = new List<(GameObject, bool)>();
		private readonly List<Renderer> shownRenderers_ = new List<Renderer>();
		private Course shownFor_;
		private bool startHasSign_;
		private bool finishHasSign_;

		private string courseResult_ = "";
		private Course deletePending_;

		private string CoursesFolder => Path.Combine(modFolder_, CoursesFolderName);
		private string TimesFolder => Path.Combine(modFolder_, TimesFolderName);

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
			LoadManualSectors();
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
			UpdateMapIcons();
			if (mapIcons_.Count > 0) {
				adapter_.SizeMapIcons();
			}
			UpdateSpeedTraps();
			UpdateBestLine();
		}

		/// <summary>The start/finish markers and the map's own start/finish signs for whatever is running: a time attack, Record path or the course builder.</summary>
		private Course checkpointCourse_;
		private Func<Vector3, Vector3, float> checkpointWidth_;

		/// <summary>How wide the road is at a checkpoint of <see cref="checkpointCourse_"/> (0 when unknown).</summary>
		private float CheckpointWidth(Vector3 at, Vector3 right) {
			Course course = checkpointCourse_;
			ICollection<int> road = course.RoadColliders.Count > 0 ? course.RoadColliders : new HashSet<int>(CoursesHere().SelectMany(c => c.RoadColliders));
			return adapter_.TryMeasureRoadWidth(at, right, road, out float width) ? width + 1.5f : 0f;
		}

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
			// Gantries only for a course that's running (not the course builder), as wide as the road where it's known.
			float span = course == null ? 0f : MeasureRoad(course).RoadWidth > 0f ? course.RoadWidth + 1.5f : course.Start.Width;
			markers_.Show(start, finish, course != null && startHasSign_, course != null && finishHasSign_, span);
			// The width finder is made once, not every frame (docs/archive/OPTIMIZATION_REPORT.md item 6); it's only used when the checkpoints are built.
			checkpointCourse_ = course;
			markers_.ShowCheckpoints(course, span, checkpointWidth_ ??= CheckpointWidth);
			if (course != null) {
				UpdateGantrySigns(course, raceCourse != null ? race_.Run : battleCourse != null ? battle_.Run : run_ != null && run_.Course == course ? run_ : null);
			}
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
		/// <summary>The traffic lab's kept cars go on a new map (traffic/TrafficTest.cs).</summary>
		partial void ForgetTrafficCars();

		/// <summary>Courses whose road was measured (or couldn't be): once each, since the race list asks every frame.</summary>
		private readonly HashSet<Course> roadMeasured_ = new HashSet<Course>();

		/// <summary>
		/// Nov 2: how wide the road is where the cars line up (the start line and 5, 10 and 15 m behind it; the
		/// narrowest counts), from the map's TRC road colliders, so races and battles know when side by side won't
		/// fit. Without road colliders, the start object's X scale when the map maker set it. Only on the course's own map.
		/// </summary>
		private Course MeasureRoad(Course course) {
			if (course == null || course.RoadWidth > 0f || map_ == null || course.MapKey != map_.Key || course.Start.Width <= 0f || !roadMeasured_.Add(course)) {
				return course;
			}
			ICollection<int> road = course.RoadColliders.Count > 0 ? course.RoadColliders
				: new HashSet<int>(courses_.ForMap(map_.Key).SelectMany(c => c.RoadColliders));
			var back = course.GridPath.Count >= 2 ? course.GridPath : new List<Vector3> { course.Start.Center, course.Start.Center - course.Start.Forward * 20f };
			float narrowest = float.MaxValue;
			foreach (float behind in new[] { 0f, 5f, 10f, 15f }) {
				CourseGeometry.AlongBack(back, behind, out Vector3 at, out Vector3 forward);
				if (behind == 0f) {
					at = course.Start.Center;
					forward = course.Start.Forward;
				}
				if (adapter_.TryMeasureRoadWidth(at, new Vector3(forward.z, 0f, -forward.x), road, out float width)) {
					narrowest = Mathf.Min(narrowest, width);
				}
			}
			if (narrowest < float.MaxValue) {
				course.RoadWidth = narrowest;
			} else if (Mathf.Abs(course.Start.Width - CourseGeometry.DefaultWidth) > 0.05f) {
				course.RoadWidth = course.Start.Width;
			}
			if (course.RoadWidth > 0f) {
				TrcLog.Info($"{course.Name}: the road is {course.RoadWidth:0.0} m wide at the start{(course.SingleFile ? ", too narrow for side by side: single file" : "")}.");
			}
			return course;
		}
	}
}
