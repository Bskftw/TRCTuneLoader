// The mod's icons on CarX's minimap and big map: start and finish lines, sectors, speed traps.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const string MapIconsPref = "map_icons";
		private bool mapIconsOn_ = true;
		private float mapIconsAt_;
		private readonly List<MapIcon> mapIcons_ = new List<MapIcon>();

		private static readonly Color MapStart = new Color(0.55f, 0.85f, 0.35f);
		private static readonly Color MapFinish = new Color(0.83f, 0.33f, 0.49f);
		private static readonly Color MapSector = new Color(0.75f, 1f, 0.16f);
		private static readonly Color MapTrap = new Color(0.98f, 0.78f, 0.25f);

		/// <summary>
		/// Twice a second: the start, finish, sectors and course traps of the course being driven (or, in free roam, the
		/// track opened in Tracks), and the map's free-roam speed traps, as icons on CarX's minimap and big map.
		/// </summary>
		private void UpdateMapIcons() {
			if (Time.unscaledTime < mapIconsAt_) {
				return;
			}
			mapIconsAt_ = Time.unscaledTime + 0.5f;
			mapIcons_.Clear();
			if (mapIconsOn_ && map_ != null && hasPose_) {
				Course battleCourse = battle_ != null && battle_.Started && battle_.Active ? battle_.Course : null;
				Course course = (InRace ? race_.Course : null) ?? battleCourse ?? run_?.Course;
				if (course == null && tracksOpen_.Count == 1) {
					string open = tracksOpen_.First();
					course = CoursesHere().FirstOrDefault(c => c.Code == open);
				}
				if (course != null && course.MapKey == map_.Key && course.Start != null) {
					mapIcons_.Add(new MapIcon { Position = course.Start.Center, Color = MapStart, Size = 22, Shape = MapIconShape.Line, Heading = course.Start.Forward });
					if (!course.IsCircuit && course.Finish != null) {
						mapIcons_.Add(new MapIcon { Position = course.Finish.Center, Color = MapFinish, Size = 22, Shape = MapIconShape.Line, Heading = course.Finish.Forward });
					}
					if (course.HasPath) {
						for (int i = 0; i < course.SectorEnds.Length - 1; i++) {
							course.PointAt(course.SectorEnds[i], out Vector3 at, out _);
							mapIcons_.Add(new MapIcon { Position = at, Color = MapSector, Size = 14 });
						}
					}
					foreach (CourseGate trap in course.Traps) {
						mapIcons_.Add(new MapIcon { Position = trap.Center, Color = MapTrap, Size = 16, Shape = MapIconShape.Camera });
					}
				}
				if (freeRoamTraps_) {
					foreach (SpeedTrap trap in mapTraps_) {
						mapIcons_.Add(new MapIcon { Position = trap.Gate.Center, Color = MapTrap, Size = 16, Shape = MapIconShape.Camera });
					}
				}
			}
			adapter_.TryShowMapIcons(mapIcons_);
		}
	}
}
