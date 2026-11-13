// Home's search box: finds pages by name or by a word on them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private string menuSearch_ = "";
		private string searchShownFor_ = "";
		private readonly List<(NavPage Page, string Label)> searchShown_ = new List<(NavPage, string)>();

		/// <summary>
		/// The pages a search finds, best first: the page's own name before a word on it. A page can't be found by its
		/// category alone (that would list every page in it).
		/// </summary>
		private void SearchMenu(string query, List<(NavPage Page, string Label)> into) {
			into.Clear();
			string q = (query ?? "").Trim().ToLowerInvariant();
			if (q.Length < 2) {
				return;
			}
			var found = new List<(int Score, int Order, NavPage Page, string Label)>();
			int order = 0;
			foreach (NavCategory category in Categories) {
				foreach (NavPage page in category.Pages) {
					order++;
					string name = page.Name.ToLowerInvariant();
					string where = page.Group.Length > 0 ? $"{category.Name}, {page.Group}" : category.Name;
					int score = name.StartsWith(q, StringComparison.Ordinal) ? 0 : name.Contains(q) ? 1 : page.Group.Length > 0 && page.Group.ToLowerInvariant().StartsWith(q, StringComparison.Ordinal) ? 2 : -1;
					string word = "";
					if (score < 0) {
						word = page.Keywords.FirstOrDefault(k => k.StartsWith(q, StringComparison.Ordinal)) ?? page.Keywords.FirstOrDefault(k => k.Contains(q)) ?? "";
						if (word.Length == 0) {
							continue;
						}
						score = word.StartsWith(q, StringComparison.Ordinal) ? 3 : 4;
					}
					found.Add((score, order, page, $"<b>{page.Name}</b>  <color={Theme.MutedHex}>{where}{(word.Length > 0 ? $" · {word}" : "")}</color>"));
				}
			}
			foreach (var hit in found.OrderBy(f => f.Score).ThenBy(f => f.Order).Take(8)) {
				into.Add((hit.Page, hit.Label));
			}
		}

		/// <summary>
		/// Home's first rows: a search box (type a page or a setting, press what it finds) and the pages pinned from their
		/// titles. What's drawn is decided in the layout pass only, so typing never changes the rows between a frame's passes.
		/// </summary>
		private void DrawHomeSearch() {
			if (Event.current.type == EventType.Layout) {
				string typed = menuSearch_ ?? "";
				if (typed != searchShownFor_) {
					searchShownFor_ = typed;
					SearchMenu(typed, searchShown_);
				}
				pinnedShown_.Clear();
				foreach (string key in pinnedKeys_) {
					NavPage page = Categories.SelectMany(c => c.Pages).FirstOrDefault(p => p.Key == key);
					if (page != null) {
						pinnedShown_.Add(page);
					}
				}
			}
			bool searching = searchShownFor_.Trim().Length >= 2;
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<color={Theme.MutedHex}><b>SEARCH</b></color>", Theme.Small, GUILayout.Width(58));
			menuSearch_ = GUILayout.TextField(menuSearch_ ?? "", 30, Theme.Field, GUILayout.Width(220));
			if (searchShownFor_.Length > 0) {
				if (NavButton("Clear", Theme.Chip)) {
					menuSearch_ = "";
				}
			} else {
				GUILayout.Label($"<color={Theme.MutedHex}>A page or a setting: ghosts, shortcut, colours, backups...</color>", Theme.Small);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (searching) {
				if (searchShown_.Count == 0) {
					GUILayout.Label($"<color={Theme.MutedHex}>Nothing called that. Try one word, like traps or colours.</color>", Theme.Small);
				}
				NavPage open = null;
				foreach (var (page, label) in searchShown_) {
					if (NavButton(label, Theme.Row)) {
						open = page;
					}
				}
				if (open != null) {
					menuSearch_ = "";
					OpenPage(open);
				}
				GUILayout.Space(6);
			} else if (pinnedShown_.Count > 0) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<color={Theme.MutedHex}><b>PINNED</b></color>", Theme.Small, GUILayout.Width(58));
				NavPage open = null;
				foreach (NavPage page in pinnedShown_) {
					if (NavButton(page.Name, Theme.Button)) {
						open = page;
					}
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				if (open != null) {
					OpenPage(open);
				}
				GUILayout.Space(6);
			} else {
				GUILayout.Space(4);
			}
		}
	}
}
