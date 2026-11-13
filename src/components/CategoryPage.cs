// Draws the open page inside its category: the category's pages down the left, the page's title options on the right.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>Draws the open page with its category's pages down the left. Tabs that list their own sections don't, here.</summary>
		private void DrawCategoryPage(Action drawTab) {
			int current = CurrentCategory;
			if (current < 0) {
				drawTab();
				return;
			}
			NavCategory category = Categories[current];
			// Buttons keep the one colour picked in Look (bskftw, Nov 3: not each category's colour); only the names on top are coloured.
			DrawCategoryInside(category, drawTab);
		}

		private void DrawCategoryInside(NavCategory category, Action drawTab) {
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(SideNavWidth), GUILayout.ExpandHeight(true));
			GUILayout.Label($"<b><color={category.Hex}>{category.Name.ToUpperInvariant()}</color></b>", Theme.Small);
			string group = "";
			foreach (NavPage page in category.Pages) {
				if (page.Group != group) {
					group = page.Group;
					if (group.Length > 0) {
						GUILayout.Space(6);
						GUILayout.Label($"<b><color={Theme.MutedHex}>{group.ToUpperInvariant()}</color></b>", Theme.Small);
					}
				}
				string note = page.Note();
				bool open = IsOpen(page);
				if (NavButton($"{(open ? $"<color={category.Hex}>" : "")}{page.Name}{(open ? "</color>" : "")}{(note.Length > 0 ? $"\n<size={Mathf.RoundToInt(11 * Theme.TextScale)}><color={Theme.MutedHex}>{note}</color></size>" : "")}",
					open ? Theme.RowSelected : Theme.Row) && !open) {
					OpenPage(page);
				}
			}
			GUILayout.FlexibleSpace();
			if (category.Name == "Tools" && NavButton("Arrange the screen", Theme.Button)) {
				StartArranging();
			}
			GUILayout.EndVertical();
			GUILayout.Space(14);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			NavPage shown = category.Pages.FirstOrDefault(IsOpen);
			if (shown != null) {
				GUILayout.BeginHorizontal();
				GUILayout.Label(shown.Name, pageTitle_);
				bool pinned = pinnedKeys_.Contains(shown.Key);
				if ((pinned || pinnedKeys_.Count < MostPinned) && NavButton(pinned ? "Unpin from Home" : "Pin to Home", Theme.Chip)) {
					TogglePin(shown);
				}
				GUILayout.FlexibleSpace();
				DrawPageOptions(shown);
				GUILayout.EndHorizontal();
				GUILayout.Space(4);
			}
			// The tab's own section list is the one on the left now (Suspension keeps its own).
			sideNavHidden_ = tab_ != SuspensionTab;
			try {
				drawTab();
			} finally {
				sideNavHidden_ = false;
			}
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		/// <summary>While true, <see cref="SideNav"/> draws nothing: the category's page list stands in for it.</summary>
		private static bool sideNavHidden_;

		/// <summary>The choices that belong beside a page's title, as chips on the right.</summary>
		private void DrawPageOptions(NavPage page) {
			if (page.Tab == BattlesTab && page.View == 0 && (battle_ == null || !battle_.Active)) {
				pickedFormat_ = Choice(BattleFormats, pickedFormat_);
			} else if (page.Tab == CoursesTab && page.View == 2) {
				timesView_ = Choice(TimesViews, timesView_);
				GUILayout.Space(10);
				timesFilter_ = Choice(TimesFilters, timesFilter_);
			} else if (page.Tab == PlayersTab || page.Tab == SpecPlayersTab) {
				List<PlayerRow> problems = players_.Where(page.Tab == PlayersTab ? (Func<PlayerRow, bool>)HasProblem : RulesProblem).ToList();
				int filter = Choice(new[] { $"Everyone ({players_.Count})", $"Only problems ({problems.Count})" }, problemsOnly_ ? 1 : 0);
				if ((filter == 1) != problemsOnly_) {
					problemsOnly_ = filter == 1;
					SavePref(ProblemsOnlyPref, problemsOnly_);
				}
			}
		}
	}
}
