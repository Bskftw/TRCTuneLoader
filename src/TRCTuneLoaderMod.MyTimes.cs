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
			GUILayout.Label("Saved on this PC. Each car and TRC tune set has its own times, and flying starts are kept apart from standing ones. " +
				"A time counts as TRC spec when your car was TRC spec at the start and the finish, with no flagged mods.", Theme.Small);
			GUILayout.BeginHorizontal();
			timesView_ = Choice(TimesViews, timesView_);
			GUILayout.FlexibleSpace();
			timesFilter_ = Choice(TimesFilters, timesFilter_);
			GUILayout.EndHorizontal();

			List<TimeAttackRecord> shown = timesFilter_ == 1 ? times_.Where(r => r.IsSpec).ToList() : times_;
			if (timesResult_.Length > 0) {
				GUILayout.Label(timesResult_, Theme.Body);
			}
			GUILayout.BeginVertical(Theme.Card, GUILayout.ExpandHeight(true));
			timesScroll_ = GUILayout.BeginScrollView(timesScroll_);
			if (shown.Count == 0) {
				GUILayout.Label(times_.Count == 0 ? "No times yet. Pick a track in Tracks and press Time attack." : "No TRC spec times yet.", Theme.Small);
			} else if (timesView_ == 2) {
				DrawRecentTimes(shown);
			} else {
				DrawTimesByCourse(shown, lapsOnly: timesView_ == 1);
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
		}

		/// <summary>Maps, then their courses (newest first), each opening to its rows per car and set.</summary>
		private void DrawTimesByCourse(List<TimeAttackRecord> records, bool lapsOnly) {
			IEnumerable<IGrouping<string, TimeAttackRecord>> courses = records.GroupBy(r => r.CourseCode);
			if (lapsOnly) {
				courses = courses.Where(g => g.Any(r => r.IsLap));
			}
			var byMap = courses.GroupBy(g => g.OrderBy(r => r.When).Last().MapName).OrderByDescending(m => m.Max(g => g.Max(r => r.When)));
			bool any = false;
			foreach (var map in byMap) {
				any = true;
				GUILayout.Label(map.Key.Length > 0 ? map.Key : "Unknown map", Theme.Group);
				foreach (IGrouping<string, TimeAttackRecord> course in map.OrderByDescending(g => g.Max(r => r.When))) {
					DrawTimesCourse(course, lapsOnly);
				}
			}
			if (!any) {
				GUILayout.Label("No circuit laps yet. Drive a circuit (Tracks) to see your best laps here.", Theme.Small);
			}
		}

		private void DrawTimesCourse(IGrouping<string, TimeAttackRecord> course, bool lapsOnly) {
			TimeAttackRecord last = course.OrderBy(r => r.When).Last();
			bool circuit = course.Any(r => r.IsLap || r.Laps != 1);
			string key = (lapsOnly ? "laps " : "") + course.Key;
			bool open = timesOpen_.Contains(key);
			TimeAttackRecord headline = TimeAttackLog.BestSpec(course, course.Key) ?? TimeAttackLog.Best(course, course.Key);
			string best = headline != null ? $"{(headline.IsLap ? "best lap " : "")}<b>{TimeAttackRun.FormatTime(headline.Seconds)}</b>" : "";
			string label = $"{(open ? "▾" : "▸")} <b>{last.CourseName}</b>  <color={Theme.MutedHex}>{(circuit ? "circuit · " : "")}{(last.OfficialCourse ? "official · " : "")}" +
				$"{course.Count()} time(s)</color>  {best}";
			if (GUILayout.Button(label, open ? Theme.RowSelected : Theme.Row)) {
				if (!timesOpen_.Remove(key)) {
					timesOpen_.Add(key);
				}
			}
			if (!open) {
				return;
			}

			foreach (var carSet in course.GroupBy(r => (r.CarId, r.TuneSet)).OrderBy(g => g.Min(r => r.TimeMs))) {
				TimeAttackRecord any = carSet.First();
				string set = any.TuneSet.Length > 0 ? any.TuneSet : "no TRC tune set";
				var parts = new List<string>();
				if (circuit) {
					parts.Add(Part("Best lap", carSet, LapKind));
					parts.Add(Theoretical(carSet, LapKind));
					if (!lapsOnly) {
						foreach (int laps in carSet.Where(r => !r.IsLap).Select(r => r.Laps).Distinct().OrderBy(l => l)) {
							parts.Add(Part($"{laps} laps standing", carSet, new TimeKind { Laps = laps }));
							parts.Add(Part($"{laps} laps flying", carSet, new TimeKind { Laps = laps, Flying = true }));
						}
					}
				} else {
					parts.Add(Part("Standing", carSet, new TimeKind { Laps = 1 }));
					parts.Add(Part("Flying", carSet, new TimeKind { Laps = 1, Flying = true }));
					parts.Add(Theoretical(carSet, new TimeKind { Laps = 1 }));
					parts.Add(Theoretical(carSet, new TimeKind { Laps = 1, Flying = true }, "flying "));
				}
				parts.RemoveAll(p => p.Length == 0);
				GUILayout.BeginHorizontal();
				GUILayout.Space(14);
				GUILayout.BeginVertical();
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{any.Car}</b> · {set}  <color={Theme.MutedHex}>{carSet.Count()} time(s), last {carSet.Max(r => r.When):MMM d}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				int carId = any.CarId;
				string tuneSet = any.TuneSet;
				string code = course.Key;
				DeleteTimesButton($"car {code} {carId} {tuneSet}", "Delete these", $"{any.Car}'s times on {last.CourseName}",
					r => r.CourseCode == code && r.CarId == carId && r.TuneSet == tuneSet);
				GUILayout.EndHorizontal();
				GUILayout.Label(string.Join("   ", parts), Theme.Small);
				// The sectors of this car's best time (a circuit's best lap), coloured against every time on the course.
				TimeAttackRecord bestTime = carSet.Where(r => r.SectorMs.Length > 1 && (circuit ? r.IsLap : r.Laps == 1 && !r.Flying))
					.OrderBy(r => r.IsSpec ? 0 : 1).ThenBy(r => r.TimeMs).FirstOrDefault();
				if (showSectors_ && bestTime != null) {
					GUILayout.Label($"<color={Theme.MutedHex}>{(circuit ? "Best lap" : "Best run")}:</color> {SavedSectors(bestTime, times_)}", Theme.Small);
				}
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();
			}
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<color={Theme.MutedHex}>Course code {course.Key} · last time {last.When:MMM d, HH:mm}</color>", Theme.Small);
			GUILayout.FlexibleSpace();
			string courseCode = course.Key;
			DeleteTimesButton($"course {courseCode}", "Delete all on this course", $"every time on {last.CourseName}", r => r.CourseCode == courseCode);
			GUILayout.EndHorizontal();
			GUILayout.Space(6);
		}

		/// <summary>"Standing 3:28.042" (green when it counts), with the fastest uncounted time when that's faster; "" when none.</summary>
		private static string Part(string name, IEnumerable<TimeAttackRecord> records, TimeKind kind) {
			List<TimeAttackRecord> these = records.Where(kind.Matches).ToList();
			if (these.Count == 0) {
				return "";
			}
			TimeAttackRecord spec = these.Where(r => r.IsSpec).OrderBy(r => r.TimeMs).FirstOrDefault();
			TimeAttackRecord fastest = these.OrderBy(r => r.TimeMs).First();
			string counted = spec != null ? $"<color={Theme.GoodHex}><b>{TimeAttackRun.FormatTime(spec.Seconds)}</b></color>" : "";
			string other = fastest != spec ? $"{TimeAttackRun.FormatTime(fastest.Seconds)} <color={Theme.MutedHex}>(not counted)</color>" : "";
			return $"{name} {counted}{(counted.Length > 0 && other.Length > 0 ? ", " : "")}{other}";
		}

		/// <summary>"Theoretical 3:25.910": the best sectors of these times added up, when there's more than one time to take them from.</summary>
		private static string Theoretical(IEnumerable<TimeAttackRecord> records, TimeKind kind, string what = "") {
			List<TimeAttackRecord> these = records.Where(kind.Matches).Where(r => r.SectorMs.Length > 0).ToList();
			if (these.Count < 2) {
				return "";
			}
			int sectors = these.GroupBy(r => r.SectorMs.Length).OrderByDescending(g => g.Count()).First().Key;
			TimeAttackRecord any = these[0];
			long? ms = TimeAttackLog.TheoreticalBestMs(these, any.CourseCode, any.CarId, any.TuneSet, kind, sectors);
			long best = these.Where(r => r.SectorMs.Length == sectors).Min(r => r.TimeMs);
			return ms.HasValue && ms.Value < best
				? $"<color={Theme.PurpleHex}>Theoretical {what}{TimeAttackRun.FormatTime(ms.Value / 1000.0)}</color>"
				: "";
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

		private void DrawRecentTimes(List<TimeAttackRecord> records) {
			foreach (TimeAttackRecord r in records.OrderByDescending(r => r.When).Take(RecentCount)) {
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
				if (GUILayout.Button(label, Theme.Button)) {
					timesDeletePending_ = key;
					timesResult_ = "";
				}
				return;
			}
			if (GUILayout.Button($"Delete {what}?", Theme.ButtonPrimary)) {
				DeleteTimes(which, what);
			}
			if (GUILayout.Button("Keep", Theme.Button)) {
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
