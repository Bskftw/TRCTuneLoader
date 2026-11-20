// Tools > Driver stats (F6), the daily and weekly challenges (F12) and the lap compare card (F4) on the Tracks page.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const int StatsTab = 18;
		private Vector2 statsScroll_;

		// ---- Kilometres per car (km_by_car.txt), counted while driving and saved once a minute.

		private Dictionary<string, double> kmByCar_;
		private Vector3 kmLast_;
		private bool kmHas_, kmChanged_;
		private float kmSaveAt_;

		private string KmPath => Path.Combine(TimesFolder, DriverStats.KmFile);

		private void UpdateDriverKm() {
			if (!hasPose_ || car_ == null || car_.InGarage) {
				kmHas_ = false;
				return;
			}
			if (kmByCar_ == null) {
				try {
					kmByCar_ = DriverStats.ReadKm(File.Exists(KmPath) ? File.ReadAllText(KmPath) : "");
				} catch (Exception e) {
					TrcLog.Once("km read", $"Driver stats: couldn't read {DriverStats.KmFile} ({e.Message}); starting from 0. Said once.");
					kmByCar_ = new Dictionary<string, double>();
				}
			}
			if (kmHas_) {
				float moved = Vector3.Distance(kmLast_, pose_.Position);
				if (moved > 0.05f && moved < 30f) {
					string name = car_.CarName ?? "";
					kmByCar_.TryGetValue(name, out double km);
					kmByCar_[name] = km + moved / 1000.0;
					kmChanged_ = true;
				}
			}
			kmLast_ = pose_.Position;
			kmHas_ = true;
			if (kmChanged_ && Time.unscaledTime >= kmSaveAt_) {
				kmSaveAt_ = Time.unscaledTime + 60f;
				kmChanged_ = false;
				try {
					Directory.CreateDirectory(TimesFolder);
					File.WriteAllText(KmPath, DriverStats.WriteKm(kmByCar_));
				} catch (Exception e) {
					TrcLog.Once("km write", $"Driver stats: couldn't save {DriverStats.KmFile} ({e.Message}). Said once.");
				}
			}
		}

		// ---- The stats page

		private void DrawStatsTab() {
			statsScroll_ = GUILayout.BeginScrollView(statsScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			GUILayout.Label("From your own times, battles and races on this PC.", Theme.PageLead);

			GUILayout.Label("Driving", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (kmByCar_ == null || kmByCar_.Count == 0) {
				GUILayout.Label("Kilometres per car are counted from now on, while you drive.", Theme.Small);
			} else {
				GUILayout.Label($"<b>{kmByCar_.Values.Sum():N0} km</b> in all (counted since Nov 18).", Theme.Body);
				foreach (var pair in kmByCar_.OrderByDescending(p => p.Value).Take(8)) {
					GUILayout.Label($"{pair.Key}  <color={Theme.MutedHex}>{pair.Value:N1} km</color>", Theme.Body);
				}
			}
			GUILayout.EndVertical();

			GUILayout.Space(6);
			GUILayout.Label("Time attack", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			var runs = times_.Where(r => !r.IsLap).ToList();
			if (runs.Count == 0) {
				GUILayout.Label("No runs yet.", Theme.Small);
			} else {
				var favourite = runs.GroupBy(r => r.CourseName).OrderByDescending(g => g.Count()).First();
				var topCar = runs.GroupBy(r => r.Car).OrderByDescending(g => g.Count()).First();
				GUILayout.Label($"<b>{runs.Count}</b> runs and <b>{times_.Count(r => r.IsLap)}</b> laps on <b>{runs.Select(r => r.CourseCode).Distinct().Count()}</b> tracks.", Theme.Body);
				GUILayout.Label($"Favourite track: <b>{favourite.Key}</b>  <color={Theme.MutedHex}>{favourite.Count()} runs</color>", Theme.Body);
				GUILayout.Label($"Most driven car: <b>{topCar.Key}</b>  <color={Theme.MutedHex}>{topCar.Count()} runs</color>", Theme.Body);
			}
			GUILayout.EndVertical();

			GUILayout.Space(6);
			GUILayout.Label("Battles and races", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			var battles = DriverStats.Battles(ReadCsv(BattleLog.FileName));
			var races = DriverStats.Races(ReadCsv("races.csv"));
			GUILayout.Label($"Battles: <b>{battles.All.Total}</b>, won {battles.All.Won}, lost {battles.All.Lost}  <color={Theme.MutedHex}>win rate {battles.All.WinRate}</color>", Theme.Body);
			foreach (var pair in battles.ByCar.Where(p => p.Key.Length > 0).OrderByDescending(p => p.Value.Total).Take(5)) {
				GUILayout.Label($"  {pair.Key}  <color={Theme.MutedHex}>{pair.Value.Won}-{pair.Value.Lost}, {pair.Value.WinRate}</color>", Theme.Small);
			}
			GUILayout.Label($"Races: <b>{races.Races}</b>, won {races.Won}, on the podium {races.Podiums}", Theme.Body);
			GUILayout.EndVertical();
			GUILayout.EndScrollView();
		}

		/// <summary>A CSV file in the times folder as rows (header first); none when it's missing.</summary>
		private List<List<string>> ReadCsv(string name) {
			string path = Path.Combine(TimesFolder, name);
			try {
				return File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0).Select(SpeedTrapLog.CsvFields).ToList() : new List<List<string>>();
			} catch (Exception) {
				return new List<List<string>>();
			}
		}

		// ---- Challenges

		private const string ChallengesFile = "challenges.txt";
		private Dictionary<string, long> challengeBests_;

		/// <summary>Today's and this week's track on this map (its own tracks only, so everyone gets the same), or null.</summary>
		private (Course Day, Course Week) ChallengeCourses() {
			if (map_ == null) {
				return (null, null);
			}
			List<Course> own = CoursesHere().Where(c => c.Source == CourseSource.Map && c.Length > 200f).ToList();
			DateTime today = DateTime.Now.Date;
			string day = Challenges.Pick(Challenges.DayKey(today), own.Select(c => c.Code));
			string week = Challenges.Pick(Challenges.WeekKey(today), own.Select(c => c.Code));
			return (own.FirstOrDefault(c => c.Code == day), own.FirstOrDefault(c => c.Code == week));
		}

		private Dictionary<string, long> ChallengeBests() {
			if (challengeBests_ == null) {
				challengeBests_ = new Dictionary<string, long>(StringComparer.Ordinal);
				try {
					string path = Path.Combine(TimesFolder, ChallengesFile);
					if (File.Exists(path)) {
						foreach (string line in File.ReadAllLines(path)) {
							string[] parts = line.Split('\t');
							if (parts.Length == 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms)) {
								challengeBests_[parts[0]] = ms;
							}
						}
					}
				} catch (Exception e) {
					TrcLog.Once("challenges read", $"Challenges: couldn't read {ChallengesFile} ({e.Message}). Said once.");
				}
			}
			return challengeBests_;
		}

		/// <summary>From a finished run: a time on today's or this week's track counts for it.</summary>
		private void NoteChallenge(Course course, long ms) {
			var (day, week) = ChallengeCourses();
			DateTime today = DateTime.Now.Date;
			foreach ((Course c, string key) in new[] { (day, "day " + Challenges.DayKey(today)), (week, "week " + Challenges.WeekKey(today)) }) {
				if (c == null || c.Code != course.Code) {
					continue;
				}
				string id = $"{key} {c.Code}";
				Dictionary<string, long> bests = ChallengeBests();
				if (bests.TryGetValue(id, out long was) && was <= ms) {
					continue;
				}
				bests[id] = ms;
				Challenges.Medal medal = Challenges.MedalFor(ms / 1000.0, c.Length, c.Kind);
				ShowNotice($"<b>{(key.StartsWith("day", StringComparison.Ordinal) ? "Daily" : "Weekly")} challenge: {Challenges.MedalName(medal)}</b>  {TimeAttackRun.FormatTime(ms / 1000.0)}", 4f);
				TrcLog.Info($"Challenge {id}: {TimeAttackRun.FormatTime(ms / 1000.0)}, {Challenges.MedalName(medal)}.");
				try {
					File.WriteAllLines(Path.Combine(TimesFolder, ChallengesFile), bests.Select(p => $"{p.Key}\t{p.Value.ToString(CultureInfo.InvariantCulture)}"));
				} catch (Exception e) {
					TrcLog.Warning($"Challenges: couldn't save ({e.Message}).");
				}
			}
		}

		/// <summary>Time attack > Challenges (its own page since Nov 18: on Tracks it squeezed the track list).</summary>
		private void DrawChallengesPage() {
			var (day, week) = ChallengeCourses();
			if (day == null && week == null) {
				GUILayout.Label(map_ == null ? "Drive onto a map to see its challenges." : "This map has no tracks of its own to pick challenges from.", Theme.Small);
				return;
			}
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label($"<color={Theme.MutedHex}>Picked from the date: everyone on this map gets the same. Bronze, silver and gold from the track's length.</color>", Theme.Small);
			DateTime today = DateTime.Now.Date;
			foreach ((Course c, string key, string label) in new[] { (day, "day " + Challenges.DayKey(today), "Today"), (week, "week " + Challenges.WeekKey(today), "This week") }) {
				if (c == null) {
					continue;
				}
				var t = Challenges.Times(c.Length, c.Kind);
				string mine = ChallengeBests().TryGetValue($"{key} {c.Code}", out long ms)
					? $"your best {TimeAttackRun.FormatTime(ms / 1000.0)} ({Challenges.MedalName(Challenges.MedalFor(ms / 1000.0, c.Length, c.Kind))})" : "no time yet";
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{label}: {c.Name}</b>  <color={Theme.MutedHex}>gold {TimeAttackRun.FormatTime(t.Gold)} · silver {TimeAttackRun.FormatTime(t.Silver)} · " +
					$"bronze {TimeAttackRun.FormatTime(t.Bronze)} · {mine}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && hasPose_ && !InBattle && !InRace;
				if (NavButton("Go", Theme.Button)) {
					StartTimeAttack(c);
				}
				GUI.enabled = enabled;
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
			GUILayout.Space(6);
		}

		// ---- Lap compare (F4): the last run or lap against the best it was compared with.

		private string compareTitle_ = "";
		private List<LapCompare.Stretch> compare_;
		private float compareTotal_;

		/// <summary>From a finished run or lap: kept for the card on My times.</summary>
		private void NoteLapCompare(Course course, float[] mine, float[] best, long ms) {
			if (mine == null || best == null || mine.Length < 3 || best.Length < 3) {
				return;
			}
			string title = $"{course.Name}, {TimeAttackRun.FormatTime(ms / 1000.0)}";
			ShowLapCompare(title, mine, best);
			try {
				TimeAttackTraces.SaveLastCompare(TimesFolder, title, mine, best);
			} catch (Exception e) {
				TrcLog.Once("lap_compare_save", $"Couldn't keep the Lap compare: {e.Message}");
			}
		}

		private void ShowLapCompare(string title, float[] mine, float[] best) {
			compare_ = LapCompare.Stretches(mine, best, TimeAttackRun.TraceStep, 200f);
			compareTotal_ = compare_.Sum(s => s.Lost);
			compareTitle_ = title;
		}

		private bool compareLoaded_;

		private void DrawLapCompare() {
			if (compare_ == null && !compareLoaded_) {
				compareLoaded_ = true; // after a restart: the last one saved, read once
				var saved = TimeAttackTraces.LoadLastCompare(TimesFolder);
				if (saved != null && saved.Value.Mine.Length >= 3 && saved.Value.Best.Length >= 3) {
					ShowLapCompare(saved.Value.Title, saved.Value.Mine, saved.Value.Best);
				}
			}
			if (compare_ == null || compare_.Count == 0) {
				return;
			}
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label($"<b>Last run against your best</b>  <color={Theme.MutedHex}>{compareTitle_} · {TimeAttackRun.FormatGap(compareTotal_)}</color>", Theme.Body);
			// A bar a stretch: red where time went, green where it came, as tall as it was.
			Rect bars = GUILayoutUtility.GetRect(10f, 36f, GUILayout.ExpandWidth(true));
			if (Event.current.type == EventType.Repaint) {
				float most = Mathf.Max(0.05f, compare_.Max(s => Mathf.Abs(s.Lost)));
				float w = bars.width / compare_.Count;
				for (int i = 0; i < compare_.Count; i++) {
					float h = Mathf.Abs(compare_[i].Lost) / most * (bars.height / 2f);
					var r = compare_[i].Lost > 0 ? new Rect(bars.x + i * w, bars.center.y, Mathf.Max(1f, w - 1f), h) : new Rect(bars.x + i * w, bars.center.y - h, Mathf.Max(1f, w - 1f), h);
					GUI.DrawTexture(r, compare_[i].Lost > 0 ? Theme.BadFill : Theme.GoodFill);
				}
			}
			foreach (LapCompare.Stretch s in LapCompare.Worst(compare_, 3)) {
				GUILayout.Label($"<color={Theme.BadHex}>+{s.Lost:0.00} s</color> from {s.From:N0} to {s.To:N0} m  <color={Theme.MutedHex}>{s.MyKmh:0} km/h, best {s.BestKmh:0} km/h</color>", Theme.Small);
			}
			foreach (LapCompare.Stretch s in LapCompare.Best(compare_, 1)) {
				GUILayout.Label($"<color={Theme.GoodHex}>{s.Lost:0.00} s</color> from {s.From:N0} to {s.To:N0} m: your best stretch  <color={Theme.MutedHex}>{s.MyKmh:0} km/h</color>", Theme.Small);
			}
			GUILayout.EndVertical();
			GUILayout.Space(6);
		}
	}
}
