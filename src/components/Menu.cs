// The menu's structure: the categories and their pages, which one is open, and opening a page.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The menu since 0.17 (bskftw, Nov 3, from the "Brainstorming images" pictures): Home is big cards, one per part of
	/// the mod in its colour; inside, the parts are categories across the top and each one's pages are a list down the
	/// left. The pages are the old tabs and their sections, so every screen is drawn as before; which category is open
	/// is worked out from the page that's open, so anything that opened a tab still lands in the right place.
	///
	/// The controller in the menu is in ControllerFocus.cs and ControllerMenu.cs: every button the mod draws goes through
	/// <see cref="NavButton(string, GUIStyle, GUILayoutOption[])"/>, which remembers where it is, so the D-pad moves between
	/// them, A presses one, B goes back and LB/RB change category.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int SpecsTab = 11;
		private const int BugTab = 12;

		/// <summary>Specs' own pages (bskftw, Nov 3): your car against your specs and regulations, and the room against them.</summary>
		private const int SpecCarTab = 13;
		private const int SpecPlayersTab = 14;

		private sealed class NavPage {
			public string Name;
			public int Tab;
			/// <summary>The section inside the tab (-1: the tab has none).</summary>
			public int View = -1;
			public Func<string> Note = () => "";

			/// <summary>A small heading over the page in its category's list ("" for none): Options' pages have one.</summary>
			public string Group = "";

			/// <summary>Other words the Home search finds the page by: what's on it, and its settings.</summary>
			public string[] Keywords = new string[0];

			/// <summary>What the page is saved as when it's pinned to Home.</summary>
			public string Key => $"{Tab}:{View}";
		}

		private sealed class NavCategory {
			public string Name;

			/// <summary>Which part it is (0 TRC spec ... 5 Tools): its colour is the one set in Look for that part.</summary>
			public int Part;
			public string Hex => partHex_[Part];
			public NavPage[] Pages;

			/// <summary>The page last open in it, opened again when the category is picked.</summary>
			public int LastPage;
		}

		private NavCategory[] categories_;

		private NavCategory[] Categories {
			get {
				if (categories_ != null) {
					return categories_;
				}
				string Count(int n) => n > 0 ? n.ToString(CultureInfo.InvariantCulture) : "";
				categories_ = new[] {
					new NavCategory { Name = "TRC spec", Part = 0, Pages = new[] {
						new NavPage { Name = "TRC Tunes", Tab = MyCarTab, Keywords = new[] { "tune", "load a tune", "apply", "check my tune", "backups", "restore", "dyno" } },
						new NavPage { Name = "Tune sets", Tab = TuneSetsTab, Keywords = new[] { "sets", "revival", "numbers", "values", "official tunes" } },
						new NavPage { Name = "Compare tunes", Tab = CompareTab, Keywords = new[] { "compare", "differences", "diff", "what changed" } },
						new NavPage { Name = "Players", Tab = PlayersTab, Note = () => Count(players_.Count), Keywords = new[] { "room", "reports", "their mods", "who's spec" } },
					} },
					new NavCategory { Name = "Specs", Part = 1, Pages = new[] {
						new NavPage { Name = "My specs", Tab = SpecsTab, Note = () => Count(specs_.Count), Keywords = new[] { "spec", "make a spec", "own tunes" } },
						new NavPage { Name = "Check my car", Tab = SpecCarTab, Note = () => MySpecs().Count > 0 ? "on a spec" : "", Keywords = new[] { "check", "meets a regulation" } },
						new NavPage { Name = "Players", Tab = SpecPlayersTab, Note = () => Count(players_.Count(RulesProblem)), Keywords = new[] { "room", "who meets the rules" } },
						new NavPage { Name = "Regulations", Tab = RegulationsTab, View = 0, Note = () => Count(regulations_.Count), Keywords = new[] { "rules", "class", "limits" } },
						new NavPage { Name = "Make or edit", Tab = RegulationsTab, View = 1, Note = () => editingRegulation_ != null ? "editing" : "", Keywords = new[] { "new regulation", "edit a regulation" } },
						new NavPage { Name = "Shared with you", Tab = RegulationsTab, View = 2, Keywords = new[] { "shared regulations" } },
					} },
					new NavCategory { Name = "Time attack", Part = 2, Pages = new[] {
						new NavPage { Name = "Tracks", Tab = CoursesTab, View = 0, Note = () => map_ == null ? "" : Count(CoursesHere().Count),
							Keywords = new[] { "courses", "ghosts", "teleport", "start a run", "record path", "grid" } },
						new NavPage { Name = "My times", Tab = CoursesTab, View = 2, Keywords = new[] { "laps", "records", "bests", "delete a time", "sectors" } },
						new NavPage { Name = "Course builder", Tab = CoursesTab, View = 3, Note = () => recorder_.State != CourseRecorder.Phase.Idle ? "recording" : "",
							Keywords = new[] { "make a course", "course tool", "start line", "finish line" } },
					} },
					new NavCategory { Name = "Battles", Part = 3, Pages = new[] {
						new NavPage { Name = "1v1 battles", Tab = BattlesTab, View = 0, Note = () => invites_.Count > 0 ? $"{invites_.Count} waiting" : battle_ != null && battle_.Active ? "on" : "",
							Keywords = new[] { "challenge", "cat and mouse", "side by side", "touge" } },
						new NavPage { Name = "Races", Tab = BattlesTab, View = RacesSection, Note = () => OpenCount(RacesSection), Keywords = new[] { "host a race", "lobby", "console players" } },
						new NavPage { Name = "Tournaments", Tab = BattlesTab, View = TournamentsSection, Note = () => OpenCount(TournamentsSection), Keywords = new[] { "bracket", "knockout" } },
					} },
					new NavCategory { Name = "Free roam", Part = 4, Pages = new[] {
						new NavPage { Name = "Speed traps", Tab = CoursesTab, View = 1, Note = () => Count(mapTraps_.Count), Keywords = new[] { "traps", "place a trap", "share traps", "top speed" } },
						new NavPage { Name = "Fun games", Tab = BattlesTab, View = GamesSection, Note = () => OpenCount(GamesSection), Keywords = new[] { "games", "cops", "tag" } },
						new NavPage { Name = "My ratings", Tab = BattlesTab, View = RatingsSection, Keywords = new[] { "elo", "safety rating", "license" } },
					} },
					new NavCategory { Name = "Tools", Part = 5, Pages = ToolsPages() },
				};
				return categories_;
			}
		}

		private NavPage[] ToolsPages() {
			var pages = new List<NavPage> {
				new NavPage { Name = "Mod check", Tab = ModCheckTab, Keywords = new[] { "mods", "ksl", "verified", "flagged" } },
				new NavPage { Name = "Suspension logger", Tab = SuspensionTab, Keywords = new[] { "suspension", "springs", "dampers", "telemetry" } },
				new NavPage { Name = "About", Tab = AboutTab, Keywords = new[] { "trc", "project dream", "credits", "version" } },
				new NavPage { Name = "Report a bug", Tab = BugTab, Keywords = new[] { "bug", "log", "problem" } },
			};
			AddDevPages(pages);
			// Options, in pages (IMPROVEMENT_PLAN.md U6): last in the list, under their own small heading.
			pages.Add(new NavPage { Name = "Keys and buttons", Tab = OptionsTab, View = 0, Group = "Options",
				Keywords = new[] { "shortcut", "hotkey", "open the window", "restart key", "start key", "wheel", "controller", "pad", "d-pad" } });
			pages.Add(new NavPage { Name = "Look", Tab = OptionsTab, View = 1, Group = "Options",
				Keywords = new[] { "colours", "colors", "text size", "window size", "scale", "theme", "accent" } });
			pages.Add(new NavPage { Name = "While driving", Tab = OptionsTab, View = 2, Group = "Options",
				Keywords = new[] { "badge", "track check", "sector times", "manual sectors", "best line", "ghosts", "gantries", "start and finish", "map icons", "speed trap signs", "screen layout", "arrange", "timer", "hud" } });
			pages.Add(new NavPage { Name = "Sharing and backups", Tab = OptionsTab, View = 3, Group = "Options",
				Keywords = new[] { "share my values", "private", "backups", "clean up", "tour" } });
			return pages.ToArray();
		}

		/// <summary>Empty in the player mod: bskftw's private builds add their own pages to Tools (the traffic lab).</summary>
		partial void AddDevPages(List<NavPage> pages);

		/// <summary>Empty in the player mod: draws a private build's own page when it's open.</summary>
		partial void DrawDevPage(int tab, ref bool drawn);

		/// <summary>Open races, tournaments or games for a Battles section ("in one" when the player is).</summary>
		private string OpenCount(int section) {
			if (race_ != null && SectionFor(race_) == section) {
				return "in one";
			}
			int open = openRaces_.Values.Count(v => SectionFor(v.Lobby) == section);
			return open > 0 ? $"{open} open" : "";
		}

		private int ViewOf(int tab) {
			switch (tab) {
				case CoursesTab: return coursesView_;
				case BattlesTab: return battleViewMode_;
				case RegulationsTab: return regulationsView_;
				case OptionsTab: return optionsView_;
				default: return -1;
			}
		}

		private bool IsOpen(NavPage page) => tab_ == page.Tab && (page.View < 0 || ViewOf(page.Tab) == page.View);

		/// <summary>The category the open page is in, or -1 for Home.</summary>
		private int CurrentCategory {
			get {
				if (tab_ == HomeTab) {
					return -1;
				}
				NavCategory[] all = Categories;
				for (int c = 0; c < all.Length; c++) {
					if (all[c].Pages.Any(IsOpen)) {
						return c;
					}
				}
				return -1;
			}
		}

		/// <summary>Opens a page the way its old tab and section button did.</summary>
		private void OpenPage(NavPage page) {
			bool sameTab = tab_ == page.Tab;
			tab_ = page.Tab;
			if (!sameTab) {
				OnTabOpened(page.Tab);
			}
			switch (page.Tab) {
				case CoursesTab:
					if (coursesView_ != page.View) {
						coursesView_ = page.View;
						courseResult_ = "";
						deletePending_ = null;
						placingTrap_ = false;
						trapEditing_ = null;
					}
					break;
				case BattlesTab:
					if (battleViewMode_ != page.View) {
						battleViewMode_ = page.View;
						raceResult_ = "";
					}
					break;
				case RegulationsTab:
					if (regulationsView_ != page.View) {
						regulationsView_ = page.View;
						regulationResult_ = "";
						if (page.View == 1 && editingRegulation_ == null) {
							StartEditingRegulation(new Regulation { Author = BattleMessage.PlainName(MyName()) });
						}
					}
					break;
				case OptionsTab:
					if (optionsView_ != page.View) {
						optionsView_ = Mathf.Clamp(page.View, 0, 3);
						optionsScroll_ = Vector2.zero;
						optionsResult_ = "";
						capturingShortcut_ = false;
						capturingRestart_ = false;
						capturingStart_ = false;
					}
					break;
			}
			NavCategory[] all = Categories;
			for (int c = 0; c < all.Length; c++) {
				int i = Array.IndexOf(all[c].Pages, page);
				if (i >= 0) {
					all[c].LastPage = i;
				}
			}
			navFocus_ = -1;
		}

		private void OpenCategory(int c) {
			NavCategory category = Categories[c];
			OpenPage(category.Pages[Mathf.Clamp(category.LastPage, 0, category.Pages.Length - 1)]);
		}

		private void GoHome() {
			tab_ = HomeTab;
			OnTabOpened(HomeTab);
			navFocus_ = -1;
		}
	}
}
