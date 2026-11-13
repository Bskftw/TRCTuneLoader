// The My times page: your times by map, course, car and tune set.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// My times: the player's times by map and course, each course opening to one row per car and TRC tune set
	/// (best standing and flying runs, best lap on a circuit, and the theoretical best from the best sectors),
	/// the best laps of every circuit, or the latest runs. "TRC spec only" hides times that don't count. Times can be
	/// deleted (a run, a car's times on a course, or a whole course), with a second click to be sure.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private static readonly string[] TimesViews = { "Best times", "Best laps", "Recent" };
		private static readonly string[] TimesFilters = { "All times", "TRC spec only" };
		private const int RecentCount = 40;

		private int timesView_;
		private int timesFilter_;
		private readonly HashSet<string> timesOpen_ = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>What the next click on a Delete button deletes (a key for the button), and what it said last.</summary>
		private string timesDeletePending_ = "";
		private string timesResult_ = "";

		private void DrawMyTimes() {
			// Best times / Best laps / Recent and the filter are beside the page's title (Nav.DrawPageOptions).
			EnsureTimesViews();
			List<TimeAttackRecord> shown = timesShown_;
			if (timesResult_.Length > 0) {
				GUILayout.Label(timesResult_, Theme.Body);
			}
			GUILayout.BeginVertical(Theme.Card, GUILayout.ExpandHeight(true));
			timesScroll_ = GUILayout.BeginScrollView(timesScroll_);
			if (shown.Count == 0) {
				GUILayout.Label(times_.Count == 0 ? "No times yet. Pick a track in Tracks and press Time attack." : "No TRC spec times yet.", Theme.Small);
			} else if (timesView_ == 2) {
				DrawRecentTimes(timesRecent_);
			} else {
				DrawTimesByCourse(lapsOnly: timesView_ == 1);
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			GUILayout.Label("Saved on this PC. Each car and TRC tune set has its own times, and flying starts are kept apart from standing ones. " +
				"A time counts as TRC spec when your car was TRC spec at the start and the finish, with no flagged mods.", Theme.Small);
		}

		/// <summary>A course's row on My times, worked out once per change of the saved times (not on every GUI pass).</summary>
		private sealed class TimesCourseRow {
			public IGrouping<string, TimeAttackRecord> Course;
			public TimeAttackRecord Last;
			public bool Circuit;
			public string Label;
		}

		private List<TimeAttackRecord> timesShown_ = new List<TimeAttackRecord>();
		private List<TimeAttackRecord> timesRecent_ = new List<TimeAttackRecord>();
		private List<(string Map, List<TimesCourseRow> Courses)> timesByMap_ = new List<(string, List<TimesCourseRow>)>();
		private object timesViewsOf_;
		private int timesViewsCount_ = -1, timesViewsFilter_ = -1, timesViewsView_ = -1;

		/// <summary>
		/// The page's grouped and sorted views of the saved times: made again only when the times, the filter or the view change
		/// (docs/archive/OPTIMIZATION_REPORT.md item 7: the whole history was grouped and sorted 2 to 4 times a frame), and only in a Layout
		/// pass, so every pass of a frame draws the same rows.
		/// </summary>
		private void EnsureTimesViews() {
			if (ReferenceEquals(timesViewsOf_, times_) && timesViewsCount_ == times_.Count && timesViewsFilter_ == timesFilter_ && timesViewsView_ == timesView_) {
				return;
			}
			if (timesViewsOf_ != null && Event.current != null && Event.current.type != EventType.Layout) {
				return;
			}
			timesViewsOf_ = times_;
			timesViewsCount_ = times_.Count;
			timesViewsFilter_ = timesFilter_;
			timesViewsView_ = timesView_;
			timesShown_ = timesFilter_ == 1 ? times_.Where(r => r.IsSpec).ToList() : new List<TimeAttackRecord>(times_);
			timesRecent_ = timesShown_.OrderByDescending(r => r.When).Take(RecentCount).ToList();
			timesByMap_.Clear();
			IEnumerable<IGrouping<string, TimeAttackRecord>> courses = timesShown_.GroupBy(r => r.CourseCode);
			if (timesView_ == 1) {
				courses = courses.Where(g => g.Any(r => r.IsLap));
			}
			var byMap = courses.GroupBy(g => g.OrderBy(r => r.When).Last().MapName).OrderByDescending(m => m.Max(g => g.Max(r => r.When)));
			foreach (var map in byMap) {
				var rows = new List<TimesCourseRow>();
				foreach (IGrouping<string, TimeAttackRecord> course in map.OrderByDescending(g => g.Max(r => r.When))) {
					TimeAttackRecord last = course.OrderBy(r => r.When).Last();
					bool circuit = course.Any(r => r.IsLap || r.Laps != 1);
					TimeAttackRecord headline = TimeAttackLog.BestSpec(course, course.Key) ?? TimeAttackLog.Best(course, course.Key);
					string best = headline != null ? $"{(headline.IsLap ? "best lap " : "")}<b>{TimeAttackRun.FormatTime(headline.Seconds)}</b>" : "";
					rows.Add(new TimesCourseRow {
						Course = course, Last = last, Circuit = circuit,
						Label = $"<b>{last.CourseName}</b>  <color={Theme.MutedHex}>{(circuit ? "circuit · " : "")}{(last.OfficialCourse ? "official · " : "")}" +
							$"{course.Count()} time(s)</color>  {best}",
					});
				}
				timesByMap_.Add((map.Key, rows));
			}
		}

		/// <summary>Maps, then their courses (newest first), each opening to its rows per car and set.</summary>
		private void DrawTimesByCourse(bool lapsOnly) {
			bool any = false;
			foreach (var map in timesByMap_) {
				any = true;
				GUILayout.Label(map.Map.Length > 0 ? map.Map : "Unknown map", Theme.Group);
				foreach (TimesCourseRow row in map.Courses) {
					DrawTimesCourse(row, lapsOnly);
				}
			}
			if (!any) {
				GUILayout.Label("No circuit laps yet. Drive a circuit (Tracks) to see your best laps here.", Theme.Small);
			}
		}

		private void DrawTimesCourse(TimesCourseRow row, bool lapsOnly) {
			IGrouping<string, TimeAttackRecord> course = row.Course;
			TimeAttackRecord last = row.Last;
			bool circuit = row.Circuit;
			string key = (lapsOnly ? "laps " : "") + course.Key;
			bool open = timesOpen_.Contains(key);
			string label = row.Label;
			if (NavButton(label, open ? Theme.RowSelected : Theme.Row)) {
				// Opening a course closes the others, like Tracks (bskftw, Nov 3).
				bool wasOpen = timesOpen_.Contains(key);
				timesOpen_.Clear();
				if (!wasOpen) {
					timesOpen_.Add(key);
				}
			}
			if (!open) {
				return;
			}

			// Open: like the selected course in Tracks (bskftw, Nov 3): the name big, then a card per car and set.
			EnsureNavStyles();
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label((last.MapName.Length > 0 ? last.MapName : "Course").ToUpperInvariant(), kicker_);
			GUILayout.Label(last.CourseName.ToUpperInvariant(), courseTitle_);
			GUILayout.Label($"{(circuit ? "Circuit" : "Sprint")}{(last.OfficialCourse ? " · official" : "")} · {course.Count()} time(s) · last {last.When:MMM d, HH:mm}", Theme.Small);
			foreach (var carSet in course.GroupBy(r => (r.CarId, r.TuneSet, r.Rule)).OrderBy(g => g.Key.Rule.Length > 0 ? 1 : 0).ThenBy(g => g.Min(r => r.TimeMs))) {
				DrawTimesCar(course.Key, last.CourseName, carSet.ToList(), circuit, lapsOnly);
			}
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<color={Theme.MutedHex}>Course code {course.Key}</color>", Theme.Small);
			GUILayout.FlexibleSpace();
			string courseCode = course.Key;
			DeleteTimesButton($"course {courseCode}", "Delete all on this course", $"every time on {last.CourseName}", r => r.CourseCode == courseCode);
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			GUILayout.Space(6);
		}

		/// <summary>One car and tune set on a course: its name, its best times in boxes, and its best run's sectors.</summary>
		private void DrawTimesCar(string code, string courseName, List<TimeAttackRecord> carSet, bool circuit, bool lapsOnly) {
			TimeAttackRecord any = carSet[0];
			string set = (any.TuneSet.Length > 0 ? any.TuneSet : "no TRC tune set") +
				(any.Rule == TimeAttackLog.OpenRule ? " · Open" : any.Rule.Length > 0 ? $" · {RuleName(any.Rule)}" : "");
			GUILayout.Space(6);
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<b>{any.Car}</b>  <color={Theme.MutedHex}>{set} · {carSet.Count} time(s), last {carSet.Max(r => r.When):MMM d}</color>", Theme.Body);
			GUILayout.FlexibleSpace();
			int carId = any.CarId;
			string tuneSet = any.TuneSet;
			string rule = any.Rule;
			DeleteTimesButton($"car {code} {carId} {tuneSet} {rule}", "Delete these", $"{any.Car}'s times on {courseName}",
				r => r.CourseCode == code && r.CarId == carId && r.TuneSet == tuneSet && r.Rule == rule);
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			if (circuit) {
				TimeBox("Best lap", carSet, LapKind, Theme.PurpleHex);
				TheoreticalBox(carSet, LapKind);
				if (!lapsOnly) {
					foreach (int laps in carSet.Where(r => !r.IsLap).Select(r => r.Laps).Distinct().OrderBy(l => l)) {
						TimeBox($"{laps} laps standing", carSet, new TimeKind { Laps = laps }, null);
						TimeBox($"{laps} laps flying", carSet, new TimeKind { Laps = laps, Flying = true }, null);
					}
				}
			} else {
				TimeBox("Standing", carSet, new TimeKind { Laps = 1 }, Theme.PurpleHex);
				TimeBox("Flying", carSet, new TimeKind { Laps = 1, Flying = true }, Theme.PurpleHex);
				TheoreticalBox(carSet, new TimeKind { Laps = 1 });
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			// The sectors of this car's best time (a circuit's best lap), coloured against every time on the course.
			TimeAttackRecord bestTime = carSet.Where(r => r.SectorMs.Length > 1 && (circuit ? r.IsLap : r.Laps == 1 && !r.Flying))
				.OrderBy(r => r.IsSpec ? 0 : 1).ThenBy(r => r.TimeMs).FirstOrDefault();
			if (showSectors_ && bestTime != null) {
				GUILayout.Label($"<color={Theme.MutedHex}>{(circuit ? "Best lap" : "Best run")} sectors</color>   {SavedSectors(bestTime, times_)}", Theme.Small);
			}
		}

		/// <summary>A box with the best time of this kind: the counted one, or the fastest marked "not counted"; nothing when there's none.</summary>
		private static void TimeBox(string name, IEnumerable<TimeAttackRecord> records, TimeKind kind, string hex) {
			List<TimeAttackRecord> these = records.Where(kind.Matches).ToList();
			if (these.Count == 0) {
				return;
			}
			TimeAttackRecord spec = these.Where(r => r.IsSpec).OrderBy(r => r.TimeMs).FirstOrDefault();
			TimeAttackRecord fastest = these.OrderBy(r => r.TimeMs).First();
			if (spec != null) {
				StatBox(name, TimeAttackRun.FormatTime(spec.Seconds), hex, fastest != spec ? $"{TimeAttackRun.FormatTime(fastest.Seconds)} not counted" : null);
			} else {
				StatBox(name, TimeAttackRun.FormatTime(fastest.Seconds), null, "not counted");
			}
		}

		/// <summary>The theoretical best (best sectors added up) in a box, when it's faster than the best time.</summary>
		private static void TheoreticalBox(IEnumerable<TimeAttackRecord> records, TimeKind kind) {
			List<TimeAttackRecord> these = records.Where(kind.Matches).Where(r => r.SectorMs.Length > 0).ToList();
			if (these.Count < 2) {
				return;
			}
			int sectors = these.GroupBy(r => r.SectorMs.Length).OrderByDescending(g => g.Count()).First().Key;
			TimeAttackRecord any = these[0];
			long? ms = TimeAttackLog.TheoreticalBestMs(these, any.CourseCode, any.CarId, any.TuneSet, kind, sectors);
			long best = these.Where(r => r.SectorMs.Length == sectors).Min(r => r.TimeMs);
			if (ms.HasValue && ms.Value < best) {
				StatBox("Theoretical", TimeAttackRun.FormatTime(ms.Value / 1000.0), null);
			}
		}

		/// <summary>
		/// A saved time's sectors in their pace colours: purple where it's the best that sector has been driven (this kind
		/// of time, any car), green where it's your best in this car on this set, amber where it's slower.
		/// </summary>
		private static string SavedSectors(TimeAttackRecord r, IEnumerable<TimeAttackRecord> all) {
			TimeKind kind = TimeKind.Of(r);
			int count = r.SectorMs.Length;
			long?[] best = TimeAttackLog.BestSectorsMs(all, x => x.CourseCode == r.CourseCode && kind.Matches(x), count);
			long?[] mine = TimeAttackLog.BestSectorsMs(all, x => x.CourseCode == r.CourseCode && x.CarId == r.CarId && x.TuneSet == r.TuneSet && kind.Matches(x), count);
			var parts = new List<string>();
			for (int i = 0; i < count; i++) {
				parts.Add($"<color={PaceColour(r.SectorMs[i], best[i], mine[i], orEqual: true)}>S{i + 1} {TimeAttackRun.FormatTime(r.SectorMs[i] / 1000.0)}</color>");
			}
			return string.Join("  ", parts);
		}

		/// <summary>The newest times first (already sorted and cut to <see cref="RecentCount"/> by <see cref="EnsureTimesViews"/>).</summary>
		private void DrawRecentTimes(List<TimeAttackRecord> records) {
			foreach (TimeAttackRecord r in records) {
				string what = r.IsLap ? $"lap {r.LapNumber}" : r.Laps == 0 ? "laps" : r.Laps > 1 ? $"{r.Laps} laps {(r.Flying ? "flying" : "standing")}" : r.Flying ? "flying" : "standing";
				string spec = r.IsSpec ? $"<color={Theme.GoodHex}>{r.TuneSet} spec</color>" : $"<color={Theme.MutedHex}>{DoesntCount(r)}</color>";
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<color={Theme.MutedHex}>{r.When:MMM d HH:mm}</color>  <b>{TimeAttackRun.FormatTime(r.Seconds)}</b>  {r.CourseName} · {what} · {r.Car} · {spec}", Theme.Body);
				GUILayout.FlexibleSpace();
				TimeAttackRecord one = r;
				DeleteTimesButton($"run {r.When.Ticks} {r.CourseCode} {r.TimeMs} {r.LapNumber}", "Delete", $"the {TimeAttackRun.FormatTime(r.Seconds)} on {r.CourseName}", x => TimeAttackLog.Same(x, one));
				GUILayout.EndHorizontal();
				if (showSectors_ && r.SectorMs.Length > 1) {
					GUILayout.Label($"      {SavedSectors(r, times_)}", Theme.Small);
				}
				if (r.TrapKmh.Length > 0) {
					GUILayout.Label($"      <color={Theme.WarnHex}>Speed traps {string.Join(" · ", r.TrapKmh.Select(k => $"{k:0}"))} km/h, total {r.TrapTotal:0}</color>", Theme.Small);
				}
			}
		}

		/// <summary>A Delete button: the first click asks, the second deletes.</summary>
		private void DeleteTimesButton(string key, string label, string what, Func<TimeAttackRecord, bool> which) {
			if (timesDeletePending_ != key) {
				if (NavButton(label, Theme.Button)) {
					timesDeletePending_ = key;
					timesResult_ = "";
				}
				return;
			}
			if (NavButton($"Delete {what}?", Theme.ButtonPrimary)) {
				DeleteTimes(which, what);
			}
			if (NavButton("Keep", Theme.Button)) {
				timesDeletePending_ = "";
			}
		}

		/// <summary>
		/// Deletes times from runs.csv. When one was a best whose trace the live delta and the best line use, that
		/// trace goes too, so the next run compares with what's left.
		/// </summary>
		private void DeleteTimes(Func<TimeAttackRecord, bool> which, string what) {
			timesDeletePending_ = "";
			try {
				List<TimeAttackRecord> going = times_.Where(which).ToList();
				int removed = TimeAttackLog.Delete(TimesFolder, which);
				foreach (TimeAttackRecord r in going) {
					TimeKind kind = r.IsLap ? LapKind : TimeKind.Of(r);
					string key = TimeAttackTraces.Key(r.CourseCode, r.CarId, r.TuneSet, kind);
					var trace = TimeAttackTraces.Load(TimesFolder, key);
					if (trace != null && trace.Value.TimeMs == r.TimeMs) {
						TimeAttackTraces.Delete(TimesFolder, key);
					}
				}
				times_ = TimeAttackLog.Load(TimesFolder);
				timesResult_ = $"Deleted {what} ({removed} time(s)). The file from before is kept as TimeAttack/runs-before-delete.csv.";
				TrcLog.Info($"My times: deleted {what} ({removed} time(s)).");
			} catch (Exception e) {
				timesResult_ = $"Couldn't delete: {e.Message}";
				TrcLog.Error(e);
			}
		}
	}
}
