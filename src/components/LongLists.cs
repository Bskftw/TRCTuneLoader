// Long lists without lag (only the rows on screen are drawn), and the course picker.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Long lists without lag (bskftw, Nov 3: Project Dream has 107 tracks, and the Tracks, Races and 1v1 lists lagged).
	/// Only the rows near the visible part of a scroll view are drawn; the rest are blank space of the same height,
	/// measured the frame before. The courses on this map and each course's best time are worked out once, not for every
	/// row on every redraw.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		/// <summary>One long list's measurements, from the last frame drawn.</summary>
		private sealed class LongList {
			/// <summary>Where the first row starts inside the scroll view, how tall the visible part is, and a plain row.</summary>
			public float Top;
			public float View = 600f;
			public float Row = 32f;

			/// <summary>Tall rows (an opened track) by key.</summary>
			public readonly Dictionary<string, float> Tall = new Dictionary<string, float>(StringComparer.Ordinal);

			public float PendingTop = -1f;
			public float PendingRow = -1f;
		}

		/// <summary>Rows drawn this far outside the visible part anyway (pixels), so estimates a little off never leave a gap.</summary>
		private const float LongListSlack = 150f;

		/// <summary>
		/// Draws <paramref name="items"/> in the scroll view that's open, only those near the visible part. Call with the
		/// scroll position from before <c>BeginScrollView</c>, and call <see cref="EndLongList"/> after <c>EndScrollView</c>.
		/// Every decision uses the last frame's measurements, so the layout and repaint passes always match.
		/// </summary>
		private void LongRows<T>(LongList list, IList<T> items, float scrollY, Func<T, string> key, Func<T, bool> tall, Action<T> draw) {
			Rect marker = GUILayoutUtility.GetRect(0f, 0f);
			bool repaint = Event.current.type == EventType.Repaint;
			if (repaint) {
				list.PendingTop = marker.y;
			}
			float y = list.Top;
			float lastPlainY = float.NaN;
			bool lastWasPlain = false;
			for (int i = 0; i < items.Count; i++) {
				T item = items[i];
				string id = key(item);
				bool isTall = tall(item);
				float height = isTall && list.Tall.TryGetValue(id, out float h) ? h : list.Row;
				bool near = isTall || (y + height >= scrollY - LongListSlack && y <= scrollY + list.View + LongListSlack);
				if (!near) {
					GUILayout.Space(height);
					lastWasPlain = false;
				} else {
					GUILayout.BeginVertical();
					draw(item);
					GUILayout.EndVertical();
					if (repaint) {
						Rect drawn = GUILayoutUtility.GetLastRect();
						if (isTall) {
							list.Tall[id] = drawn.height;
						} else if (lastWasPlain && list.PendingRow < 0f) {
							// The distance from one plain row to the next, margins and all.
							list.PendingRow = drawn.y - lastPlainY;
						}
						lastPlainY = drawn.y;
					}
					lastWasPlain = !isTall;
				}
				y += height;
			}
		}

		/// <summary>After the scroll view: keeps this frame's measurements for the next one.</summary>
		private static void EndLongList(LongList list) {
			if (Event.current.type != EventType.Repaint) {
				return;
			}
			list.View = Mathf.Max(100f, GUILayoutUtility.GetLastRect().height);
			if (list.PendingTop >= 0f) {
				list.Top = list.PendingTop;
			}
			if (list.PendingRow > 4f) {
				list.Row = list.PendingRow;
			}
			list.PendingTop = -1f;
			list.PendingRow = -1f;
		}

		// ---- The courses here, once a frame ----

		private List<Course> coursesHere_ = new List<Course>();
		private int coursesHereFrame_ = -1;
		private string coursesHereMap_ = "";

		/// <summary>The courses on this map (<see cref="CourseLibrary.ForMap"/>), worked out once a frame.</summary>
		private List<Course> CoursesHere() {
			string mapKey = map_?.Key ?? "";
			if (coursesHereFrame_ != Time.frameCount || coursesHereMap_ != mapKey) {
				coursesHere_ = courses_.ForMap(mapKey);
				coursesHereFrame_ = Time.frameCount;
				coursesHereMap_ = mapKey;
			}
			return coursesHere_;
		}

		// ---- Each course's best time, worked out when the times change ----

		private readonly Dictionary<string, TimeAttackRecord> bestSpec_ = new Dictionary<string, TimeAttackRecord>(StringComparer.Ordinal);
		private readonly Dictionary<string, TimeAttackRecord> bestAny_ = new Dictionary<string, TimeAttackRecord>(StringComparer.Ordinal);
		private int bestsFor_ = -1;
		private List<TimeAttackRecord> bestsList_;

		/// <summary>The time to show for a course (its best spec time, or with <paramref name="specOnly"/> false any time), or null.</summary>
		private TimeAttackRecord CourseBest(string courseCode, bool specOnly) {
			if (bestsList_ != times_ || bestsFor_ != times_.Count) {
				bestsList_ = times_;
				bestsFor_ = times_.Count;
				bestSpec_.Clear();
				bestAny_.Clear();
				foreach (var group in times_.GroupBy(r => r.CourseCode, StringComparer.Ordinal)) {
					bestSpec_[group.Key] = TimeAttackLog.BestSpec(group, group.Key);
					bestAny_[group.Key] = TimeAttackLog.Best(group, group.Key);
				}
			}
			Dictionary<string, TimeAttackRecord> bests = specOnly ? bestSpec_ : bestAny_;
			return bests.TryGetValue(courseCode, out TimeAttackRecord best) ? best : null;
		}

		// ---- A course to pick, for races and 1v1 battles ----

		private sealed class CoursePicker {
			public string Filter = "";
			public Vector2 Scroll;
			public readonly LongList List = new LongList();
		}

		private readonly CoursePicker racePicker_ = new CoursePicker();
		private readonly CoursePicker battlePicker_ = new CoursePicker();

		/// <summary>
		/// A course picker for long lists: a search box once there are more than a few, and the courses in a scroll view
		/// of their own (only the visible ones drawn). Returns the picked course's code.
		/// </summary>
		private string PickCourse(CoursePicker picker, List<Course> courses, string picked, Func<Course, string> label, float height = 260f) {
			if (courses.Count > 6) {
				GUILayout.BeginHorizontal();
				GUILayout.Label("Find", Theme.Small, GUILayout.Width(40));
				picker.Filter = GUILayout.TextField(picker.Filter ?? "", 40, Theme.Field, GUILayout.Width(220));
				if (picker.Filter.Length > 0 && NavButton("Clear", Theme.Button)) {
					picker.Filter = "";
				}
				Course chosen = courses.FirstOrDefault(c => c.Code == picked);
				if (chosen != null) {
					GUILayout.Label($"Picked: <b>{chosen.Name}</b>", Theme.Small);
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			string filter = (picker.Filter ?? "").Trim();
			List<Course> shown = filter.Length == 0 ? courses : courses.Where(c => c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			float scrollY = picker.Scroll.y;
			picker.Scroll = GUILayout.BeginScrollView(picker.Scroll, GUILayout.Height(Mathf.Min(height, 12f + shown.Count * picker.List.Row)));
			LongRows(picker.List, shown, scrollY, c => c.Code, c => false, course => {
				if (NavButton(label(course), picked == course.Code ? Theme.RowSelected : Theme.Row)) {
					picked = course.Code;
				}
			});
			GUILayout.EndScrollView();
			EndLongList(picker.List);
			return picked;
		}
	}
}
