// Daily and weekly challenges (docs/archive/IMPROVEMENT_PLAN.md F12): a track picked from the date, with bronze, silver and gold times.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TRC.TuneLoader {
	/// <summary>
	/// Everyone with the same map gets the same challenges: the day's (or the week's) track is picked from the date among the
	/// map's own tracks (the ones in the map, which every player has), sorted by code, so no message or server is needed.
	/// The medal times come from the track's length and kind (average speeds, to be tuned by feel). A leaderboard would need
	/// the TRC server. Game-free (SelfTest.Stage6.cs).
	/// </summary>
	public static class Challenges {
		public enum Medal { None, Bronze, Silver, Gold }

		/// <summary>The average speeds (km/h) for gold, silver and bronze, by kind of track. Raised Nov 18: bskftw beat touge gold by 21 s
		/// (Sadamine Downhill at 103 km/h); now gold is a few seconds behind that.</summary>
		public static (float Gold, float Silver, float Bronze) Speeds(CourseKind kind) {
			switch (kind) {
				case CourseKind.Highway: return (180f, 160f, 140f);
				case CourseKind.Circuit: return (122f, 108f, 95f);
				default: return (101f, 90f, 80f);
			}
		}

		/// <summary>The medal times (s) for a track this long.</summary>
		public static (double Gold, double Silver, double Bronze) Times(float lengthMetres, CourseKind kind) {
			var s = Speeds(kind);
			return (lengthMetres / (s.Gold / 3.6), lengthMetres / (s.Silver / 3.6), lengthMetres / (s.Bronze / 3.6));
		}

		public static Medal MedalFor(double seconds, float lengthMetres, CourseKind kind) {
			if (seconds <= 0) {
				return Medal.None;
			}
			var t = Times(lengthMetres, kind);
			return seconds <= t.Gold ? Medal.Gold : seconds <= t.Silver ? Medal.Silver : seconds <= t.Bronze ? Medal.Bronze : Medal.None;
		}

		/// <summary>"2026-11-18" for the day, "2026-W47" for the week (ISO weeks, Monday first): what picks the track and keys the results.</summary>
		public static string DayKey(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		public static string WeekKey(DateTime date) {
			int week = CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
			return $"{date.Year.ToString(CultureInfo.InvariantCulture)}-W{week.ToString("00", CultureInfo.InvariantCulture)}";
		}

		/// <summary>The track for <paramref name="key"/> among <paramref name="codes"/>: the same on every PC with the same tracks; null when there are none.</summary>
		public static string Pick(string key, IEnumerable<string> codes) {
			List<string> sorted = codes.Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
			if (sorted.Count == 0) {
				return null;
			}
			// A fixed hash (FNV-1a), not string.GetHashCode, which differs between runs.
			uint hash = 2166136261;
			foreach (char c in key) {
				hash = (hash ^ c) * 16777619;
			}
			return sorted[(int)(hash % (uint)sorted.Count)];
		}

		public static string MedalName(Medal medal) => medal == Medal.None ? "no medal" : medal.ToString().ToLowerInvariant();
	}
}
