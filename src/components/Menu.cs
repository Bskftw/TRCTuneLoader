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

			/// <summary>Its icon in the rail (Resources/icon_&lt;name&gt;.png); "" for the part's own.</summary>
			public string Icon = "";

			/// <summary>Drawn at the bottom of the rail, apart from the others (Options).</summary>
			public bool Bottom;

			/// <summary>The page last open in it, opened again when the category is picked.</summary>
			public int LastPage;
		}

		private NavCategory[] categories_;

		/// <summary>
		/// Once, when the menu is built (UI redesign Stage 0, docs/archive/ui-redesign-plan.md risk 1): every page has its own key,
		/// and every renumbering step in PageKeys points at a page that exists. A pin saved as a key that's shared or gone
		/// would open the wrong page; this says so in the log the first time it's run.
		/// </summary>
		private static void CheckPageKeys(NavCategory[] categories) {
			var keys = categories.SelectMany(c => c.Pages).Select(p => p.Key).ToList();
			foreach (string twice in keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key)) {
				TrcLog.Warning($"Menu: two pages share the key {twice}, so a pin of one opens the other.");
			}
			foreach (string target in PageKeys.Targets.Where(k => !keys.Contains(k))) {
				TrcLog.Warning($"Menu: PageKeys moves pins to {target}, but no page has that key.");
			}
		}

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
						new NavPage { Name = "Regulations", Tab = RegulationsTab, View = 0, Note = () => editingRegulation_ != null ? "editing" : Count(regulations_.Count),
							Keywords = new[] { "rules", "class", "limits", "new regulation", "make a regulation", "edit a regulation", "import", "paste" } },
						new NavPage { Name = "Shared with you", Tab = RegulationsTab, View = 1, Keywords = new[] { "shared regulations" } },
					} },
					new NavCategory { Name = "Time attack", Part = 2, Pages = new[] {
						new NavPage { Name = "Tracks", Tab = CoursesTab, View = 0, Note = () => map_ == null ? "" : Count(CoursesHere().Count),
							Keywords = new[] { "courses", "ghosts", "teleport", "start a run", "record path", "grid" } },
						new NavPage { Name = "My times", Tab = CoursesTab, View = 2, Keywords = new[] { "laps", "records", "bests", "delete a time", "sectors", "lap compare" } },
						new NavPage { Name = "Challenges", Tab = CoursesTab, View = 4, Keywords = new[] { "daily", "weekly", "medals", "gold" } },
						new NavPage { Name = "Course builder", Tab = CoursesTab, View = 3, Note = () => recorder_.State != CourseRecorder.Phase.Idle ? "recording" : "",
							Keywords = new[] { "make a course", "course tool", "start line", "finish line" } },
					} },
					new NavCategory { Name = "Battles", Part = 3, Pages = new[] {
						new NavPage { Name = "1v1 battles", Tab = BattlesTab, View = 0, Note = () => invites_.Count > 0 ? $"{invites_.Count} waiting" : battle_ != null && battle_.Active ? "on" : "",
							Keywords = new[] { "challenge", "cat and mouse", "side by side", "touge" } },
						new NavPage { Name = "Races", Tab = BattlesTab, View = RacesSection, Note = () => OpenCount(RacesSection), Keywords = new[] { "host a race", "lobby", "console players" } },
						new NavPage { Name = "Tournaments", Tab = BattlesTab, View = TournamentsSection, Note = () => OpenCount(TournamentsSection), Keywords = new[] { "bracket", "knockout" } },
					} },
					new NavCategory { Name = "Free roam", Part = 4, Pages = FreeRoamPages() },
					new NavCategory { Name = "Tools", Part = 5, Pages = ToolsPages() },
					// Last, so Tools stays index 5 (HomeCards); drawn at the rail's bottom.
					new NavCategory { Name = "Options", Part = 5, Icon = "options", Bottom = true, Pages = OptionsPages() },
				};
				CheckPageKeys(categories_);
				return categories_;
			}
		}

		private NavPage[] FreeRoamPages() {
			var pages = new List<NavPage> {
				new NavPage { Name = "Speed traps", Tab = CoursesTab, View = 1,
					Note = () => mapTraps_.Count > 0 ? mapTraps_.Count.ToString(CultureInfo.InvariantCulture) : "", Keywords = new[] { "traps", "place a trap", "share traps", "top speed" } },
			};
			// The traffic lab goes here, with the free roam things (bskftw, Nov 12; moved from Tools on Nov 18).
			AddTrafficPage(pages);
			pages.Add(new NavPage { Name = "Fun games", Tab = BattlesTab, View = GamesSection, Note = () => OpenCount(GamesSection), Keywords = new[] { "games", "cops", "tag" } });
			pages.Add(new NavPage { Name = "My ratings", Tab = BattlesTab, View = RatingsSection, Keywords = new[] { "elo", "safety rating", "license" } });
			return pages.ToArray();
		}

		private NavPage[] ToolsPages() {
			var pages = new List<NavPage> {
				new NavPage { Name = "HUD", Tab = HudTab, Note = () => hudHidden_ ? "hidden" : "",
					Keywords = new[] { "timer", "sector times", "timing detail", "pop-up", "badge", "telemetry", "screen layout", "arrange", "hide hud", "presets", "size", "streaming" } },
				new NavPage { Name = "Host tools", Tab = HostToolsTab, Keywords = new[] { "host", "session", "who is ready", "invite", "grid", "come to the start" } },
				new NavPage { Name = "Driver stats", Tab = StatsTab, Keywords = new[] { "stats", "km", "favourite", "win rate", "battles won", "podiums" } },
				new NavPage { Name = "Mod check", Tab = ModCheckTab, Keywords = new[] { "mods", "ksl", "verified", "flagged" } },
				new NavPage { Name = "Suspension logger", Tab = SuspensionTab, Keywords = new[] { "suspension", "springs", "dampers", "telemetry" } },
				new NavPage { Name = "About", Tab = AboutTab, Keywords = new[] { "trc", "project dream", "credits", "version" } },
				new NavPage { Name = "Report a bug", Tab = BugTab, Keywords = new[] { "bug", "log", "problem" } },
			};
			AddLaneToolPage(pages);
			return pages.ToArray();
		}

		/// <summary>
		/// Options, its own part at the bottom of the rail (bskftw, Nov 18: "options should have its own category like tools").
		/// The pages keep their tab and views (OptionsTab, 0 to 3), so pins and Home's search still find them.
		/// </summary>
		private NavPage[] OptionsPages() {
			var pages = new List<NavPage>();
			pages.Add(new NavPage { Name = "Keys and buttons", Tab = OptionsTab, View = 0,
				Keywords = new[] { "shortcut", "hotkey", "open the window", "restart key", "start key", "quick menu", "wheel", "controller", "pad", "d-pad" } });
			pages.Add(new NavPage { Name = "Look", Tab = OptionsTab, View = 1,
				Keywords = new[] { "colours", "colors", "text size", "window size", "scale", "theme", "accent" } });
			pages.Add(new NavPage { Name = "While driving", Tab = OptionsTab, View = 2,
				Keywords = new[] { "track check", "manual sectors", "best line", "ghosts", "gantries", "start and finish", "map icons", "speed trap signs" } });
			pages.Add(new NavPage { Name = "Sharing and backups", Tab = OptionsTab, View = 3,
				Keywords = new[] { "share my values", "private", "backups", "clean up", "tour" } });
			return pages.ToArray();
		}

		/// <summary>The traffic lab's pages (traffic/TrafficLab.cs; a build without traffic/ has none): Free roam > Traffic lab.</summary>
		partial void AddTrafficPage(List<NavPage> pages);

		/// <summary>And Tools > Lane tool.</summary>
		partial void AddLaneToolPage(List<NavPage> pages);

		/// <summary>Draws a traffic page when it's open; nothing in a build without traffic/.</summary>
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
