// The map you're on: noticing when it changes, and scanning it for the courses a map maker placed in it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const float MapCheckSeconds = 1f;

		/// <summary>How long after reaching a map its objects are looked through, so it has finished loading.</summary>
		private const float MapScanDelaySeconds = 3f;

		private MapInfo map_;
		private float nextMapCheck_;
		private float mapSince_;
		private string scannedMapKey_ = "";
		private string scanSummary_ = "";

		/// <summary>The next scan reads every path again ("Look for course objects again"), not the remembered ones.</summary>
		private bool freshScan_;

		private void UpdateMap() {
			if (Time.unscaledTime >= nextMapCheck_) {
				nextMapCheck_ = Time.unscaledTime + MapCheckSeconds;
				MapInfo map = null;
				if (hasPose_ && adapter_.TryGetMap(out map, out _)) {
					CourseLibrary.Recognize(map);
				}

				if ((map?.Key ?? "") != (map_?.Key ?? "")) {
					mapSince_ = Time.unscaledTime;
					EndSession();
					visit_ = map != null ? new SessionStats(map.Name, Time.realtimeSinceStartupAsDouble) : null;
					// The ghost's look comes from the materials loaded, and a new map brings its own: made again next time.
					if (ghostBody_ != null) {
						UnityEngine.Object.Destroy(ghostBody_);
						ghostBody_ = null;
					}
					ghostBodyFailed_ = false;
					CourseMarkers.ForgetGhostMaterial();
					ForgetTrafficCars();
					// Courses measured on the last map (item 14: kept forever before).
					roadMeasured_.Clear();
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

		/// <summary>
		/// After saving a path or a grid: the map courses take the course files again, without looking through the
		/// whole map again (7.6 s on Project Dream; its objects didn't change). If the last scan wasn't of this map,
		/// it scans as before. "Look for course objects again" still always scans.
		/// </summary>
		private void MapCoursesFromFilesAgain() {
			if (map_ != null && scannedMapKey_ == map_.Key && courses_.TakeFilesAgain(map_.Key)) {
				roadMeasured_.RemoveWhere(c => c.Source == CourseSource.Map);
				RefreshTraps();
				TrcLog.Info($"{map_.Name}: course files taken again (no new scan of the map).");
				return;
			}
			scannedMapKey_ = "";
			mapSince_ = Time.unscaledTime - MapScanDelaySeconds;
		}

		private void ScanMap() {
			scannedMapKey_ = map_.Key;
			try {
				long began = PerfLog.Begin();
				// Paths remembered from earlier loads of this map (IMPROVEMENT_PLAN.md P1), unless asked to read them all again.
				string pathsFile = Path.Combine(CoursesFolder, TuneBackups.SafeFileName(map_.Key.Replace(':', '_')), SavedPaths.FileName);
				string loadError = "";
				SavedPaths saved = freshScan_ ? new SavedPaths(Version) : SavedPaths.Load(pathsFile, Version, out loadError);
				freshScan_ = false;
				if (loadError.Length > 0) {
					TrcLog.Warning($"Couldn't read the remembered paths ({loadError}); reading them from the map.");
				}
				List<Course> found = CourseScanner.Scan(map_.Key, map_.Name, out scanSummary_, saved);
				PerfLog.End("map scan", began);
				if (!saved.Save(pathsFile, out string saveError) && saveError.Length > 0) {
					TrcLog.Warning($"Couldn't save the paths for next time: {saveError}");
				}
				// The last scan's courses are replaced: their "already measured" marks go with them (item 14).
				roadMeasured_.RemoveWhere(c => c.Source == CourseSource.Map);
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
	}
}
