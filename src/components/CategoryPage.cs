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

		/// <summary>The page itself: its pages are in the rail and its title is the top line (Stage 2, MenuRail.cs).</summary>
		private void DrawCategoryInside(NavCategory category, Action drawTab) {
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			// The tab's own section list is the rail now (Suspension keeps its own).
			sideNavHidden_ = tab_ != SuspensionTab;
			try {
				drawTab();
			} finally {
				sideNavHidden_ = false;
			}
			GUILayout.EndVertical();
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
