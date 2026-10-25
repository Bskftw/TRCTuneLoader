using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Every course the mod knows: official ones built into the mod (official_courses/ in the project,
	/// embedded by TRCTuneLoader.csproj), ones found in the loaded map (CourseScanner), and the player's
	/// own from the placement tool (kino/mods/TRCTuneLoader/Courses).
	/// </summary>
	public sealed class CourseLibrary {
		private const string ResourcePrefix = "TRC.TuneLoader.Courses/";

		private const string ProjectDream = "project-dream";

		/// <summary>
		/// Maps uploaded more than once that are the same map, by upload key: they share one key, so a course
		/// (and its times) on one upload is the same course, with the same code, on the other.
		/// </summary>
		private static readonly Dictionary<string, (string Key, string Name)> SameMaps = new Dictionary<string, (string, string)>(StringComparer.Ordinal) {
			// bskftw's Project Dream: the public upload, and the newer test version that will replace it.
			["workshop:3372162715"] = (ProjectDream, "Project Dream"),
			["workshop:3359213524"] = (ProjectDream, "Project Dream (test version)"),
		};

		/// <summary>
		/// Maps whose TRC-named objects count as official courses, by map key (see MapInfo). Only maps
		/// TRC trusts belong here, because anyone can name objects in their own map.
		/// </summary>
		public static readonly HashSet<string> OfficialMaps = new HashSet<string>(StringComparer.Ordinal) {
			ProjectDream,
		};

		/// <summary>Gives one of several uploads of the same map that map's shared key and name.</summary>
		public static void Recognize(MapInfo map) {
			if (map != null && SameMaps.TryGetValue(map.Key, out var same)) {
				map.UploadKey = map.Key;
				map.Key = same.Key;
				map.Name = same.Name;
			}
		}

		public List<Course> Official { get; } = new List<Course>();
		public List<Course> Custom { get; } = new List<Course>();

		/// <summary>Courses from TRC objects in the loaded map, and which map they're from.</summary>
		public List<Course> FromMap { get; private set; } = new List<Course>();
		public string FromMapKey { get; private set; } = "";

		public List<string> Errors { get; } = new List<string>();

		public void LoadOfficial() {
			Official.Clear();
			var assembly = typeof(CourseLibrary).Assembly;
			foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal)) {
				try {
					string text;
					using (var stream = assembly.GetManifestResourceStream(resource))
					using (var reader = new StreamReader(stream, Encoding.UTF8)) {
						text = reader.ReadToEnd();
					}
					Course course = CourseFile.Parse(text, CourseSource.Official);
					course.FilePath = resource.Substring(ResourcePrefix.Length);
					Official.Add(course);
				} catch (Exception e) {
					Errors.Add($"Official course {resource.Substring(ResourcePrefix.Length)}: {e.Message}");
				}
			}
		}

		public void LoadCustom(string folder) {
			Custom.Clear();
			if (!Directory.Exists(folder)) {
				return;
			}
			foreach (string path in Directory.GetFiles(folder, "*" + CourseFile.Extension, SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
				try {
					Custom.Add(CourseFile.Load(path));
				} catch (Exception e) {
					Errors.Add($"{Path.GetFileName(path)}: {e.Message}");
				}
			}
		}

		/// <summary>Course files whose path a map course took over (shown as that map course, not on their own).</summary>
		private readonly HashSet<Course> usedForPaths_ = new HashSet<Course>();

		/// <summary>Map courses only line up with a course file's lines when they're this close (metres).</summary>
		private const float SameLineMetres = 1f;

		public void SetMapCourses(string mapKey, List<Course> courses) {
			FromMapKey = mapKey ?? "";
			FromMap = courses ?? new List<Course>();
			AdoptRecordedPaths();
		}

		/// <summary>
		/// A map course without a path takes one from a course file with the same start and finish lines: an
		/// official file first (built into the mod), else one the player recorded with Record path. An uphill
		/// twin takes its downhill course's path backwards.
		/// </summary>
		private void AdoptRecordedPaths() {
			usedForPaths_.Clear();
			List<Course> files = Official.Concat(Custom).Where(c => c.MapKey == FromMapKey && c.HasPath).ToList();
			// Any map course with both lines (its straight line can be short, like a loop that ends near its start;
			// a circuit's start and finish are the same line).
			foreach (Course course in FromMap.Where(c => !c.HasPath && (c.Path.Count >= 2 || c.IsCircuit))) {
				bool backwards = false;
				Course file = files.FirstOrDefault(f => f.IsCircuit == course.IsCircuit && course.SameLines(f, SameLineMetres));
				if (file == null) {
					file = files.FirstOrDefault(f => f.IsCircuit == course.IsCircuit && course.ReversedLinesOf(f, SameLineMetres));
					backwards = file != null;
				}
				if (file == null) {
					continue;
				}

				var path = new List<Vector3>(file.Path);
				if (backwards) {
					path.Reverse();
				}
				course.UsePath(path, fromPlayer: file.Source == CourseSource.Custom);
				usedForPaths_.Add(file);
			}

			// A race grid laid out for a map course (Course builder, Lay out grid) lives in a file with its lines,
			// with or without a path: the map course takes it, unless the map has grid objects of its own.
			List<Course> grids = Official.Concat(Custom).Where(c => c.MapKey == FromMapKey && c.GridPath.Count >= 2).ToList();
			foreach (Course course in FromMap.Where(c => c.GridObjects.Count == 0 && c.GridPath.Count < 2)) {
				Course file = grids.FirstOrDefault(f => f.IsCircuit == course.IsCircuit && course.SameLines(f, SameLineMetres));
				if (file != null) {
					course.GridPath = new List<Vector3>(file.GridPath);
					course.GridLayout = file.GridLayout;
					usedForPaths_.Add(file);
				}
			}
		}


		/// <summary>The courses on this map: official first, then the map's own objects, then the player's.</summary>
		public List<Course> ForMap(string mapKey) {
			var courses = new List<Course>();
			if (string.IsNullOrEmpty(mapKey)) {
				return courses;
			}
			courses.AddRange(Official.Where(c => c.MapKey == mapKey && !usedForPaths_.Contains(c)));
			if (FromMapKey == mapKey) {
				courses.AddRange(FromMap);
			}
			courses.AddRange(Custom.Where(c => c.MapKey == mapKey && !usedForPaths_.Contains(c)));
			return courses;
		}

		/// <summary>The course with this code anywhere the library knows, preferring official ones, or null.</summary>
		public Course ByCode(string code) =>
			Official.Concat(FromMap).Concat(Custom).FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

		/// <summary>Saves a placement-tool course as Courses/&lt;map&gt;/&lt;name&gt;.trccourse, never over another file.</summary>
		public static string SaveCustom(string folder, Course course) {
			string mapFolder = Path.Combine(folder, TuneBackups.SafeFileName(course.MapKey.Replace(':', '_')));
			string baseName = TuneBackups.SafeFileName(course.Name);
			string path = Path.Combine(mapFolder, baseName + CourseFile.Extension);
			for (int i = 2; File.Exists(path); i++) {
				path = Path.Combine(mapFolder, $"{baseName} ({i}){CourseFile.Extension}");
			}
			CourseFile.Save(course, path);
			return path;
		}

		/// <summary>Deletes a placement-tool course's file. Official and map courses can't be deleted.</summary>
		public static bool DeleteCustom(Course course, out string error) {
			error = "";
			if (course?.Source != CourseSource.Custom || string.IsNullOrEmpty(course.FilePath)) {
				error = "Only your own courses can be deleted.";
				return false;
			}
			try {
				if (File.Exists(course.FilePath)) {
					File.Delete(course.FilePath);
				}
				return true;
			} catch (Exception e) {
				error = e.Message;
				return false;
			}
		}
	}
}
