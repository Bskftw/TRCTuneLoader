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
	/// Also the controller in the menu: every button the mod draws goes through <see cref="NavButton(string, GUIStyle, GUILayoutOption[])"/>,
	/// which remembers where it is, so the D-pad moves between them, A presses one, B goes back and LB/RB change category.
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
						new NavPage { Name = "TRC Tunes", Tab = MyCarTab },
						new NavPage { Name = "Tune sets", Tab = TuneSetsTab },
						new NavPage { Name = "Players", Tab = PlayersTab, Note = () => Count(players_.Count) },
					} },
					new NavCategory { Name = "Specs", Part = 1, Pages = new[] {
						new NavPage { Name = "My specs", Tab = SpecsTab, Note = () => Count(specs_.Count) },
						new NavPage { Name = "Check my car", Tab = SpecCarTab, Note = () => MySpecs().Count > 0 ? "on a spec" : "" },
						new NavPage { Name = "Players", Tab = SpecPlayersTab, Note = () => Count(players_.Count(RulesProblem)) },
						new NavPage { Name = "Regulations", Tab = RegulationsTab, View = 0, Note = () => Count(regulations_.Count) },
						new NavPage { Name = "Make or edit", Tab = RegulationsTab, View = 1, Note = () => editingRegulation_ != null ? "editing" : "" },
						new NavPage { Name = "Shared with you", Tab = RegulationsTab, View = 2 },
					} },
					new NavCategory { Name = "Time attack", Part = 2, Pages = new[] {
						new NavPage { Name = "Tracks", Tab = CoursesTab, View = 0, Note = () => map_ == null ? "" : Count(CoursesHere().Count) },
						new NavPage { Name = "My times", Tab = CoursesTab, View = 2 },
						new NavPage { Name = "Course builder", Tab = CoursesTab, View = 3, Note = () => recorder_.State != CourseRecorder.Phase.Idle ? "recording" : "" },
					} },
					new NavCategory { Name = "Battles", Part = 3, Pages = new[] {
						new NavPage { Name = "1v1 battles", Tab = BattlesTab, View = 0, Note = () => invites_.Count > 0 ? $"{invites_.Count} waiting" : battle_ != null && battle_.Active ? "on" : "" },
						new NavPage { Name = "Races", Tab = BattlesTab, View = RacesSection, Note = () => OpenCount(RacesSection) },
						new NavPage { Name = "Tournaments", Tab = BattlesTab, View = TournamentsSection, Note = () => OpenCount(TournamentsSection) },
					} },
					new NavCategory { Name = "Free roam", Part = 4, Pages = new[] {
						new NavPage { Name = "Speed traps", Tab = CoursesTab, View = 1, Note = () => Count(mapTraps_.Count) },
						new NavPage { Name = "Fun games", Tab = BattlesTab, View = GamesSection, Note = () => OpenCount(GamesSection) },
						new NavPage { Name = "My ratings", Tab = BattlesTab, View = RatingsSection },
					} },
					new NavCategory { Name = "Tools", Part = 5, Pages = new[] {
						new NavPage { Name = "Mod check", Tab = ModCheckTab },
						new NavPage { Name = "Suspension logger", Tab = SuspensionTab },
						new NavPage { Name = "Options", Tab = OptionsTab },
						new NavPage { Name = "About", Tab = AboutTab },
						new NavPage { Name = "Report a bug", Tab = BugTab },
					} },
				};
				return categories_;
			}
		}

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

		// ---- Looks ----

		private static readonly Dictionary<string, GUIStyle[]> categoryStyles_ = new Dictionary<string, GUIStyle[]>(StringComparer.Ordinal);
		private static GUIStyle courseTitle_, kicker_, statBox_, statLabel_, statValue_, keyCap_, cardTitle_, pageTitle_, tableHead_, tableCell_;

		private static Color HexColor(string hex) => Theme.TryHex(hex, out Color c) ? c : Theme.Accent;

		/// <summary>
		/// A category's looks: [0] its top button, [1] the same picked, [2] its Home card, [3] a primary button in its colour,
		/// [4] its live line, [5] a picked chip (filled), [6] a picked row (tinted).
		/// </summary>
		private static GUIStyle[] CategoryStyles(string hex) {
			EnsureNavStyles();
			if (categoryStyles_.TryGetValue(hex, out GUIStyle[] styles) && styles[2].normal.background != null) {
				return styles;
			}
			Color colour = HexColor(hex);
			Color dark = Theme.Luminance(Theme.Background) < 0.5f ? new Color(0.08f, 0.08f, 0.09f) : Color.white;
			var top = new GUIStyle(Theme.Tab) { normal = { textColor = colour }, hover = { textColor = Color.Lerp(colour, Color.white, 0.3f) }, fontStyle = FontStyle.Bold };
			var picked = new GUIStyle(top) {
				normal = { background = Theme.Rounded(colour, colour, 7), textColor = dark },
				hover = { background = Theme.Rounded(colour, colour, 7), textColor = dark },
				border = new RectOffset(7, 7, 7, 7),
			};
			var card = new GUIStyle(Theme.Card) {
				normal = { background = Theme.Rounded(Color.Lerp(Theme.Panel, colour, 0.10f), Color.Lerp(Theme.Border, colour, 0.55f), 10) },
				border = new RectOffset(10, 10, 10, 10),
				padding = new RectOffset(18, 18, 14, 14),
				margin = new RectOffset(0, 0, 0, 0),
			};
			var primary = new GUIStyle(Theme.ButtonPrimary) {
				normal = { background = Theme.Rounded(colour, colour, 6), textColor = dark },
				hover = { background = Theme.Rounded(Color.Lerp(colour, Color.white, 0.15f), Color.Lerp(colour, Color.white, 0.15f), 6), textColor = dark },
				active = { background = Theme.Rounded(colour, colour, 6), textColor = dark },
			};
			var live = new GUIStyle(Theme.Body) { normal = { textColor = colour } };
			var chip = new GUIStyle(Theme.ChipActive) {
				normal = { background = Theme.Rounded(colour, colour, 12), textColor = dark },
				hover = { background = Theme.Rounded(colour, colour, 12), textColor = dark },
				active = { background = Theme.Rounded(colour, colour, 12), textColor = dark },
				fontStyle = FontStyle.Bold,
			};
			var row = new GUIStyle(Theme.RowSelected) {
				normal = { background = Theme.Rounded(Color.Lerp(Theme.Panel, colour, 0.16f), Color.Lerp(Theme.Border, colour, 0.75f), 6), textColor = Theme.Text },
				hover = { background = Theme.Rounded(Color.Lerp(Theme.Panel, colour, 0.22f), colour, 6), textColor = Theme.Text },
			};
			styles = new[] { top, picked, card, primary, live, chip, row };
			categoryStyles_[hex] = styles;
			return styles;
		}

		/// <summary>The <see cref="Theme.Version"/> the menu's own styles were made for.</summary>
		private static int navStylesVersion_ = -1;

		/// <summary>
		/// Makes the menu's own styles (again when the look changed): titles, tables, stat boxes, and the parts' and
		/// cards' looks, which are cleared so they're made in the new colours.
		/// </summary>
		private static void EnsureNavStyles() {
			if (courseTitle_ != null && navStylesVersion_ == Theme.Version && statBox_?.normal.background != null) {
				return;
			}
			navStylesVersion_ = Theme.Version;
			categoryStyles_.Clear();
			foreach (GUIStyle old in cardStyles_.Values) {
				if (old.normal.background != null) {
					UnityEngine.Object.Destroy(old.normal.background);
				}
			}
			cardStyles_.Clear();
			cardTitle_ = new GUIStyle(Theme.Heading) { fontSize = Mathf.RoundToInt(18 * Theme.TextScale) };
			// Page titles and the "selected course" label are in the accent colour, so Look's accent shows inside the menu.
			pageTitle_ = new GUIStyle(Theme.Heading) { fontSize = Mathf.RoundToInt(20 * Theme.TextScale), wordWrap = false, margin = new RectOffset(0, 10, 4, 4), normal = { textColor = Theme.Accent } };
			tableHead_ = new GUIStyle(Theme.Small) { wordWrap = false, clipping = TextClipping.Clip, margin = new RectOffset(0, 0, 4, 4), padding = new RectOffset(4, 4, 0, 0) };
			tableCell_ = new GUIStyle(Theme.Body) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(4, 4, 0, 0) };
			courseTitle_ = new GUIStyle(Theme.Heading) { fontSize = Mathf.RoundToInt(28 * Theme.TextScale), fontStyle = FontStyle.BoldAndItalic, wordWrap = false };
			kicker_ = new GUIStyle(Theme.Small) { fontStyle = FontStyle.Bold, normal = { textColor = Theme.Accent } };
			statBox_ = new GUIStyle(Theme.Card) { padding = new RectOffset(12, 12, 8, 8), margin = new RectOffset(0, 8, 4, 4) };
			statLabel_ = new GUIStyle(Theme.Small) { margin = new RectOffset(0, 0, 0, 0) };
			statValue_ = new GUIStyle(Theme.Heading) { margin = new RectOffset(0, 0, 0, 0), wordWrap = false };
			keyCap_ = new GUIStyle(Theme.Small) {
				normal = { background = Theme.Rounded(Theme.Control, Theme.Muted, 4), textColor = Theme.Text },
				border = new RectOffset(4, 4, 4, 4),
				padding = new RectOffset(6, 6, 1, 2),
				margin = new RectOffset(0, 5, 3, 3),
				alignment = TextAnchor.MiddleCenter,
				wordWrap = false,
			};
		}

		// ---- The header and the category bar ----

		/// <summary>The top: on Home the logo, title, map and car; inside, the logo (back to Home) and the categories. Then the TRC spec pill and close.</summary>
		private void DrawNavHeader() {
			EnsureNavStyles();
			int current = CurrentCategory;
			GUILayout.BeginHorizontal(HeaderArea);
			if (logo_ != null) {
				float size = current < 0 ? 44f : 36f;
				Rect logoRect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
				GUI.DrawTexture(logoRect, logo_, ScaleMode.ScaleToFit);
				if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && logoRect.Contains(Event.current.mousePosition)) {
					Event.current.Use();
					LogoClicked();
					if (current >= 0) {
						GoHome();
					}
				}
				GUILayout.Space(10);
			}
			if (current < 0) {
				GUILayout.BeginVertical();
				GUILayout.Label("TRC Tune Loader", Theme.Title);
				string where = map_ != null ? map_.Name : "Not on a map";
				GUILayout.Label($"{where}{(car_ != null ? $" · {car_.CarName}" : "")} · v{Version}", Theme.Subtitle);
				GUILayout.EndVertical();
			} else {
				GUILayout.BeginVertical();
				GUILayout.Space(4);
				GUILayout.BeginHorizontal();
				if (NavButton("Home", Theme.Tab)) {
					GoHome();
				}
				NavCategory[] all = Categories;
				for (int c = 0; c < all.Length; c++) {
					GUIStyle[] look = CategoryStyles(all[c].Hex);
					if (NavButton(all[c].Name, c == current ? look[1] : look[0]) && c != current) {
						OpenCategory(c);
					}
				}
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
			GUILayout.FlexibleSpace();
			DrawSpecPill();
			if (NavButton("×", Theme.Close)) {
				CloseWindow();
			}
			GUILayout.EndHorizontal();
			DividerLine();
		}

		/// <summary>The little pill top right: whether the car is on TRC spec, and which set (or a player spec).</summary>
		private void DrawSpecPill() {
			RefreshStatusIfDue();
			string text;
			GUIStyle style;
			OwnCheck status = carStatus_;
			if (status == null || status.Saved == null) {
				List<string> mine = MySpecs();
				text = mine.Count > 0 ? $"{mine[0]} spec" : car_ == null ? "No car" : "No TRC tune";
				style = mine.Count > 0 ? Theme.BadgeGood : Theme.PillNeutral;
			} else if (status.Saved.Matches && status.StanceDifferences.Count == 0 && !status.DrivetrainChanged && status.LiveDifferences.Count == 0) {
				text = $"TRC spec · {TuneSets.NameOf(status.Official.SetId).Replace("TRC ", "")}";
				style = Theme.BadgeGood;
			} else {
				text = "Not TRC spec";
				style = Theme.BadgeWarn;
			}
			GUILayout.BeginVertical();
			GUILayout.Space(6f);
			GUILayout.Label(text, style);
			GUILayout.EndVertical();
		}

		// ---- Inside a category: the page list down the left ----

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
			foreach (NavPage page in category.Pages) {
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

		private Vector2 bugScroll_;

		private void DrawBugTab() {
			bugScroll_ = GUILayout.BeginScrollView(bugScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			DrawBugReport();
			GUILayout.EndScrollView();
		}

		// ---- Home: a card for each part ----

		private const float CardGap = 12f;

		/// <summary>
		/// Home's cards like the hub picture (bskftw, Nov 3): the four parts in equal cards, two by two, with TRC spec on
		/// top as wide as two of them; each fades from its colour in the top-left corner to dark in the bottom-right.
		/// </summary>
		private void DrawHomeCards() {
			EnsureNavStyles();
			NavCategory[] all = Categories;
			// The content's width, less its padding and room for the scroll bar, split in two.
			float inside = WindowWidth - ContentArea.padding.horizontal - 14f;
			float card = Mathf.Floor((inside - CardGap) / 2f);
			float tall = Mathf.Round(150f * (1f + (Theme.TextScale - 1f) * 0.5f));
			// Tall enough for the title, the line and two rows of page buttons (Specs' wrap onto a second row), all four the same.
			float chipRow = Theme.Chip.CalcSize(new GUIContent("Ag")).y + Theme.Chip.margin.vertical;
			float lineHigh = Theme.Body.CalcSize(new GUIContent("Ag")).y + Theme.Body.margin.vertical;
			tall = Mathf.Max(tall, Mathf.Ceil(30f + cardTitle_.CalcSize(new GUIContent("Ag")).y + 4f + lineHigh + 8f + chipRow * 2f));
			DrawHomeCard(all[0], 0, TrcCardLine(), card * 2f + CardGap, Mathf.Round(tall * 0.72f));
			GUILayout.Space(CardGap);
			GUILayout.BeginHorizontal();
			DrawHomeCard(all[1], 1, SpecsCardLine(), card, tall);
			GUILayout.Space(CardGap);
			DrawHomeCard(all[2], 2, TimeAttackCardLine(), card, tall);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(CardGap);
			GUILayout.BeginHorizontal();
			DrawHomeCard(all[3], 3, BattlesCardLine(), card, tall);
			GUILayout.Space(CardGap);
			DrawHomeCard(all[4], 4, FreeRoamCardLine(), card, tall);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void DrawHomeCard(NavCategory category, int index, string line, float width, float height) {
			GUIStyle[] look = CategoryStyles(category.Hex);
			GUILayout.BeginVertical(CardStyle(category.Hex, width, height), GUILayout.Width(width), GUILayout.Height(height));
			GUILayout.BeginHorizontal();
			GUILayout.Label(category.Name, cardTitle_);
			GUILayout.FlexibleSpace();
			Rect icon = GUILayoutUtility.GetRect(30f, 30f, GUILayout.Width(30f), GUILayout.Height(30f));
			Texture2D picture = CardIcon(index);
			if (picture != null && Event.current.type == EventType.Repaint) {
				Color before = GUI.color;
				GUI.color = HexColor(category.Hex);
				GUI.DrawTexture(icon, picture, ScaleMode.ScaleToFit);
				GUI.color = before;
			}
			GUILayout.EndHorizontal();
			GUILayout.Label(line, look[4]);
			GUILayout.FlexibleSpace();
			// The page buttons wrap onto another row when they don't fit, so the card keeps its size (bskftw, Nov 3: Specs'
			// six pages stretched its card and pushed Time attack off the window).
			GUIStyle style = CardStyle(category.Hex, width, height);
			float room = width - style.padding.horizontal;
			float used = 0f;
			GUILayout.BeginHorizontal();
			foreach (NavPage page in category.Pages) {
				float wide = Theme.Chip.CalcSize(new GUIContent(page.Name)).x + Theme.Chip.margin.horizontal;
				if (used > 0f && used + wide > room) {
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
					GUILayout.BeginHorizontal();
					used = 0f;
				}
				used += wide;
				if (NavButton(page.Name, Theme.Chip)) {
					OpenPage(page);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			Rect card = GUILayoutUtility.GetLastRect();
			// The rest of the card opens the category (the page buttons on it are already taken by then).
			if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && card.Contains(Event.current.mousePosition)) {
				Event.current.Use();
				OpenCategory(index);
			}
			if (Event.current.type == EventType.Repaint && card.Contains(Event.current.mousePosition)) {
				GUI.DrawTexture(new Rect(card.x + 10f, card.yMax - 3f, card.width - 20f, 2f), Theme.AccentLine);
			}
		}

		private string TrcCardLine() {
			OwnCheck status = carStatus_;
			if (car_ == null) {
				return "Get in a car to check it and put a TRC tune on.";
			}
			if (status == null || status.Saved == null) {
				return official_.ForCar(car_.CarId, car_.SlotId).Count > 0 ? $"{car_.CarName}: checking..." : $"No TRC tune for the {car_.CarName} ({car_.SlotName}).";
			}
			string set = TuneSets.NameOf(status.Official.SetId);
			return status.Saved.Matches ? $"{set} on · {car_.CarName} · spec" : $"{car_.CarName} isn't on a TRC tune: {set} is one press away.";
		}

		private string SpecsCardLine() {
			List<string> mine = MySpecs();
			return mine.Count > 0 ? $"On {mine[0]}" : specs_.Count > 0 ? $"{specs_.Count} spec(s) · {regulations_.Count} regulation(s)" : $"Make your own spec · {regulations_.Count} regulation(s)";
		}

		private string TimeAttackCardLine() {
			DateTime today = DateTime.Now.Date;
			TimeAttackRecord best = times_.Where(r => !r.IsLap && r.When.Date == today).OrderByDescending(r => r.When).FirstOrDefault();
			if (best != null) {
				Course course = courses_.ByCode(best.CourseCode);
				return $"Last today: {TimeAttackRun.FormatTime(best.Seconds)}{(course != null ? $" on {course.Name}" : "")}";
			}
			int here = map_ != null ? CoursesHere().Count : 0;
			return here > 0 ? $"{here} track(s) on this map" : "Tracks, your times and the course builder";
		}

		private string BattlesCardLine() {
			if (invites_.Count > 0) {
				return $"{invites_.Count} challenge(s) waiting";
			}
			int open = openRaces_.Values.Count(v => SectionFor(v.Lobby) != GamesSection);
			return race_ != null ? "You're in a race" : open > 0 ? $"{open} race(s) open" : "Challenge someone in the room";
		}

		private string FreeRoamCardLine() {
			int games = openRaces_.Values.Count(v => SectionFor(v.Lobby) == GamesSection);
			return games > 0 ? $"{games} game(s) open" : mapTraps_.Count > 0 ? $"{mapTraps_.Count} speed trap(s) on this map" : "Speed traps, fun games and your ratings";
		}

		/// <summary>Home's last line: the tools, and Report a bug on the right.</summary>
		private void DrawToolsRow() {
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<color={partHex_[5]}><b>TOOLS</b></color>", Theme.Small, GUILayout.Width(52));
			NavCategory tools = Categories[5];
			foreach (NavPage page in tools.Pages.Where(p => p.Tab != BugTab)) {
				if (NavButton(page.Name, Theme.Button)) {
					OpenPage(page);
				}
			}
			if (NavButton("Arrange screen", Theme.Button)) {
				StartArranging();
			}
			GUILayout.FlexibleSpace();
			if (NavButton("Report a bug", Theme.Button)) {
				OpenPage(tools.Pages.First(p => p.Tab == BugTab));
			}
			GUILayout.EndHorizontal();
		}

		// ---- The selected track (Tracks), like the "selected course" picture ----

		private void DrawSelectedCourse(Course course) {
			EnsureNavStyles();
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("SELECTED COURSE", kicker_);
			GUILayout.Label(course.Name.ToUpperInvariant(), courseTitle_);
			string sectors = course.Usable ? $" · {course.SectorEnds.Length} sectors{(course.ManualSectorsOn ? " (manual)" : "")}" : "";
			GUILayout.Label($"{course.KindName} · {(course.Usable ? TimeAttackRun.Distance(course.Length) : "can't be used yet")}{sectors} · {course.SourceName.ToLowerInvariant()}", Theme.Small);

			TimeAttackRecord best = CourseBest(course.Code, true) ?? CourseBest(course.Code, false);
			double? theoretical = TheoreticalBest(course);
			int runs = times_.Count(r => r.CourseCode == course.Code && !r.IsLap);
			GUILayout.BeginHorizontal();
			StatBox("Best", best != null ? TimeAttackRun.FormatTime(best.Seconds) : "–", Theme.PurpleHex);
			StatBox("Theoretical", theoretical.HasValue ? TimeAttackRun.FormatTime(theoretical.Value) : "–", null);
			StatBox("Runs", runs.ToString(CultureInfo.InvariantCulture), null);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUIStyle start = Theme.ButtonPrimary;
			if (run_?.Course == course || pathRecorder_?.Course == course) {
				GUI.enabled = enabled && hasPose_;
				if (NavButton("Teleport to start", start)) {
					TeleportToStart();
				}
				GUI.enabled = enabled;
				if (run_?.Course == course && NavButton("Stop", Theme.Button)) {
					StopTimeAttack();
				}
			} else {
				GUI.enabled = enabled && course.Usable && hasPose_;
				if (NavButton("Teleport & start", start)) {
					StartTimeAttack(course);
					if (run_?.Course == course) {
						TeleportToStart();
					}
				}
				if (NavButton("Ready, drive there", Theme.Button)) {
					StartTimeAttack(course);
				}
				GUI.enabled = enabled;
			}
			DrawGhostsButton(course);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		private static void StatBox(string label, string value, string hex, string below = null) {
			EnsureNavStyles();
			GUILayout.BeginVertical(statBox_, GUILayout.MinWidth(120));
			GUILayout.Label(label, statLabel_);
			GUILayout.Label(hex != null ? $"<color={hex}>{value}</color>" : value, statValue_);
			if (!string.IsNullOrEmpty(below)) {
				GUILayout.Label(below, statLabel_);
			}
			GUILayout.EndVertical();
		}

		// ---- Page titles, their options, and tables (bskftw, Nov 3: "look cleaner like in this image") ----

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

		/// <summary>A table's header row: the column names over a line.</summary>
		private static void TableHead(string[] names, float[] widths) {
			EnsureNavStyles();
			GUILayout.BeginHorizontal();
			for (int i = 0; i < names.Length; i++) {
				if (widths[i] > 0f) {
					GUILayout.Label(names[i], tableHead_, GUILayout.Width(widths[i]));
				} else {
					GUILayout.Label(names[i], tableHead_);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			DividerLine();
		}

		/// <summary>One cell of a table row, as wide as its column, the text cut off at the edge.</summary>
		private static void TableCell(string text, float width) {
			EnsureNavStyles();
			GUILayout.Label(text, tableCell_, GUILayout.Width(width), GUILayout.MinHeight(32));
		}

		/// <summary>A status with a coloured dot: green good, amber waiting or unsure, red not spec, grey unknown.</summary>
		private static string Dot(string hex, string text) => $"<color={hex}><b>•</b></color> {text}";

		// ---- Home cards' looks ----

		private static readonly Dictionary<string, GUIStyle> cardStyles_ = new Dictionary<string, GUIStyle>(StringComparer.Ordinal);
		private static readonly Texture2D[] cardIcons_ = new Texture2D[5];

		/// <summary>
		/// A card's background at its own size (so its corners stay round): a rounded rectangle with a border in the
		/// part's colour, filled from the colour in the top-left to dark in the bottom-right. Made again when the look changes.
		/// </summary>
		private static GUIStyle CardStyle(string hex, float width, float height) {
			EnsureNavStyles();
			int w = Mathf.Clamp(Mathf.RoundToInt(width), 16, 2048), h = Mathf.Clamp(Mathf.RoundToInt(height), 16, 1024);
			string key = $"{hex} {w}x{h} {Theme.ToHex(Theme.Panel)} {Theme.ToHex(Theme.Border)}";
			if (cardStyles_.TryGetValue(key, out GUIStyle style) && style.normal.background != null) {
				return style;
			}
			Color colour = HexColor(hex);
			Color dark = Theme.Luminance(Theme.Background) < 0.5f ? Color.black : Color.white;
			Color from = Color.Lerp(Theme.Panel, colour, 0.24f);
			Color to = Color.Lerp(Theme.Panel, dark, 0.35f);
			Color edge = Color.Lerp(Theme.Border, colour, 0.6f);
			const float radius = 12f;
			var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			var pixels = new Color32[w * h];
			var half = new Vector2(w / 2f, h / 2f);
			var inner = half - new Vector2(radius, radius);
			for (int y = 0; y < h; y++) {
				for (int x = 0; x < w; x++) {
					Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - half;
					Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - inner;
					float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
					float depth = -outside;
					// Row 0 is the bottom: the top-left corner is x 0, y h.
					float t = Mathf.Clamp01((x / (float)w + (1f - y / (float)h)) / 2f);
					Color fill = Color.Lerp(from, to, t);
					Color c = depth < 1.5f ? Color.Lerp(edge, fill, Mathf.Clamp01(depth - 0.5f)) : fill;
					c.a = Mathf.Clamp01(depth + 0.5f);
					pixels[y * w + x] = c;
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			if (style?.normal.background != null) {
				UnityEngine.Object.Destroy(style.normal.background);
			}
			style = new GUIStyle {
				normal = { background = texture },
				border = new RectOffset(0, 0, 0, 0),
				padding = new RectOffset(20, 18, 16, 14),
				margin = new RectOffset(0, 0, 0, 0),
			};
			cardStyles_[key] = style;
			return style;
		}

		/// <summary>The cards' little pictures, white (tinted with the part's colour when drawn): a note, a list, a stopwatch, a flag, a target.</summary>
		private static Texture2D CardIcon(int index) {
			if (index < 0 || index >= cardIcons_.Length) {
				return null;
			}
			if (cardIcons_[index] != null) {
				return cardIcons_[index];
			}
			System.Func<float, float, bool> shape;
			switch (index) {
				case 0: // a music note (the tunes)
					shape = (x, y) => Ellipse(x, y, 22f, 16f, 11f, 8f) || x >= 30f && x <= 35f && y >= 16f && y <= 56f || Segment(x, y, 33f, 56f, 50f, 42f, 3f);
					break;
				case 1: // a list (specs)
					shape = (x, y) => Ring(x, y, 12f, 48f, 0f, 4.5f) || Ring(x, y, 12f, 32f, 0f, 4.5f) || Ring(x, y, 12f, 16f, 0f, 4.5f) ||
						x >= 22f && x <= 56f && (Mathf.Abs(y - 48f) <= 3f || Mathf.Abs(y - 32f) <= 3f || Mathf.Abs(y - 16f) <= 3f);
					break;
				case 2: // a stopwatch
					shape = (x, y) => Ring(x, y, 32f, 28f, 18f, 23f) || x >= 28f && x <= 36f && y >= 50f && y <= 58f || Segment(x, y, 32f, 28f, 40f, 40f, 2.5f);
					break;
				case 3: // a flag
					shape = (x, y) => x >= 12f && x <= 17f && y >= 6f && y <= 58f || x > 17f && x <= 52f && y >= 32f && y <= 58f && !(x > 40f && Mathf.Abs(y - 45f) < (x - 40f) * 0.9f);
					break;
				default: // a target
					shape = (x, y) => Ring(x, y, 32f, 32f, 19f, 24f) || Ring(x, y, 32f, 32f, 0f, 8f);
					break;
			}
			const int size = 64;
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Trilinear };
			var pixels = new Color32[size * size];
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					int cover = 0;
					for (int s = 0; s < 4; s++) {
						if (shape(x + (s % 2 == 0 ? 0.25f : 0.75f), y + (s < 2 ? 0.25f : 0.75f))) {
							cover++;
						}
					}
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)(cover * 255 / 4));
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(true, true);
			cardIcons_[index] = texture;
			return texture;
		}

		private static bool Ellipse(float x, float y, float cx, float cy, float rx, float ry) {
			float dx = (x - cx) / rx, dy = (y - cy) / ry;
			return dx * dx + dy * dy <= 1f;
		}

		private static bool Ring(float x, float y, float cx, float cy, float inner, float outer) {
			float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
			return d >= inner && d <= outer;
		}

		/// <summary>Within <paramref name="half"/> of the line from (ax, ay) to (bx, by).</summary>
		private static bool Segment(float x, float y, float ax, float ay, float bx, float by, float half) {
			Vector2 a = new Vector2(ax, ay), b = new Vector2(bx, by), p = new Vector2(x, y);
			Vector2 ab = b - a;
			float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
			return (a + ab * t - p).magnitude <= half;
		}

		/// <summary>The best of each sector added up (laps on a circuit), when every sector has a time; to the rules picked for time attack.</summary>
		private double? TheoreticalBest(Course course) {
			int sectors = course.SectorEnds.Length;
			if (sectors == 0) {
				return null;
			}
			string rule = TimeAttackRule;
			long?[] best = TimeAttackLog.BestSectorsMs(times_, r => r.CourseCode == course.Code && r.IsLap == course.IsCircuit && (r.Rule ?? "") == rule, sectors);
			return best.All(b => b.HasValue) ? best.Sum(b => b.Value) / 1000.0 : (double?)null;
		}

		// ---- The controller in the menu ----

		private const string PadMenuPref = "pad_menu";
		private const string PadHoldPref = "pad_hold_car";
		private bool padMenuOn_ = true;
		private bool padHoldCar_ = true;
		private readonly WheelButtons pad_ = new WheelButtons();
		private bool padSeen_;
		private bool padHolding_;

		// Every NavButton this frame, in order, where it is on screen (filled while the window repaints).
		private static readonly List<Rect> navRects_ = new List<Rect>();
		private static readonly List<Rect> navBuilding_ = new List<Rect>();
		private static int navCount_;
		private static int navFocus_ = -1;
		private static bool navInWindow_;
		private static bool navPress_;
		private static bool navScroll_;
		private static Vector2 navMove_;
		private static float navShownUntil_;

		/// <summary>Every button in the window: <see cref="GUILayout.Button(string, GUIStyle, GUILayoutOption[])"/>, and the controller can reach and press it.</summary>
		private static bool NavButton(string text, GUIStyle style, params GUILayoutOption[] options) =>
			NavButton(new GUIContent(text), style, options);

		private static bool NavButton(GUIContent content, GUIStyle style, params GUILayoutOption[] options) {
			bool clicked = GUILayout.Button(content, style, options);
			if (!navInWindow_) {
				return clicked;
			}
			int index = navCount_++;
			Event e = Event.current;
			if (e.type == EventType.Repaint) {
				Rect rect = GUILayoutUtility.GetLastRect();
				Vector2 at = GUIUtility.GUIToScreenPoint(rect.position);
				navBuilding_.Add(GUI.enabled ? new Rect(at, rect.size) : Rect.zero);
				if (index == navFocus_ && Time.unscaledTime < navShownUntil_) {
					DrawFocus(rect);
					if (navScroll_) {
						navScroll_ = false;
						GUI.ScrollTo(rect);
					}
				}
			} else if (e.type == EventType.Layout && navPress_ && index == navFocus_) {
				// Pressed with A: answered in the layout pass, so what it changes is drawn from the start of this frame.
				navPress_ = false;
				return GUI.enabled;
			}
			return clicked;
		}

		private static void DrawFocus(Rect rect) {
			Texture2D line = Theme.AccentLine;
			GUI.DrawTexture(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, 2f), line);
			GUI.DrawTexture(new Rect(rect.x - 2f, rect.yMax, rect.width + 4f, 2f), line);
			GUI.DrawTexture(new Rect(rect.x - 2f, rect.y, 2f, rect.height), line);
			GUI.DrawTexture(new Rect(rect.xMax, rect.y, 2f, rect.height), line);
		}

		/// <summary>Start and end of drawing the window: counts the buttons, and after a repaint moves the focus as the D-pad asked.</summary>
		private static void BeginNavFrame() {
			navInWindow_ = true;
			navCount_ = 0;
			if (Event.current.type == EventType.Repaint) {
				navBuilding_.Clear();
			}
		}

		private static void EndNavFrame() {
			navInWindow_ = false;
			if (Event.current.type != EventType.Repaint) {
				return;
			}
			navRects_.Clear();
			navRects_.AddRange(navBuilding_);
			if (navFocus_ >= navRects_.Count) {
				navFocus_ = -1;
			}
			if (navMove_ != Vector2.zero) {
				MoveFocus(navMove_);
				navMove_ = Vector2.zero;
			}
		}

		/// <summary>The nearest button the way the D-pad points (straight on counts more than sideways).</summary>
		private static void MoveFocus(Vector2 direction) {
			if (navFocus_ < 0 || navFocus_ >= navRects_.Count || navRects_[navFocus_].width <= 0f) {
				// First press: the first button that can be pressed, below the header when there is one.
				int first = navRects_.FindIndex(r => r.width > 0f);
				navFocus_ = first;
				navScroll_ = true;
				return;
			}
			Vector2 from = navRects_[navFocus_].center;
			int bestIndex = -1;
			float bestScore = float.MaxValue;
			for (int i = 0; i < navRects_.Count; i++) {
				Rect r = navRects_[i];
				if (i == navFocus_ || r.width <= 0f) {
					continue;
				}
				// Measured to the button's nearest edge across, and its middle along the way the D-pad points.
				Vector2 to = direction.x == 0f
					? new Vector2(Mathf.Clamp(from.x, r.xMin, r.xMax), r.center.y)
					: new Vector2(r.center.x, Mathf.Clamp(from.y, r.yMin, r.yMax));
				Vector2 d = to - from;
				float ahead = Vector2.Dot(d, direction);
				if (ahead <= 1f) {
					continue;
				}
				float side = Mathf.Abs(direction.x == 0f ? d.x : d.y);
				float score = ahead + side * 3f;
				if (score < bestScore) {
					bestScore = score;
					bestIndex = i;
				}
			}
			if (bestIndex >= 0) {
				navFocus_ = bestIndex;
				navScroll_ = true;
			}
		}

		private void LoadPadOptions() {
			padMenuOn_ = Kino.Prefs.Get(PadMenuPref, true);
			padHoldCar_ = Kino.Prefs.Get(PadHoldPref, true);
		}

		/// <summary>The menu's buttons, by what CarX's controller library calls them (Xbox and PlayStation pads).</summary>
		private static readonly string[][] PadNames = {
			new[] { "d-pad up", "dpad up", "up" },
			new[] { "d-pad down", "dpad down", "down" },
			new[] { "d-pad left", "dpad left", "left" },
			new[] { "d-pad right", "dpad right", "right" },
			new[] { "a", "cross", "button a" },
			new[] { "b", "circle", "button b" },
			new[] { "left shoulder", "left bumper", "l1", "lb" },
			new[] { "right shoulder", "right bumper", "r1", "rb" },
		};

		private bool PadPressed(int action) {
			foreach (WheelButtons.Binding pressed in pad_.Pressed) {
				if (pad_.ButtonNames.TryGetValue(pressed.Device, out string[] names) && pressed.Button < names.Length &&
					PadNames[action].Contains(names[pressed.Button].Trim().ToLowerInvariant())) {
					return true;
				}
			}
			return false;
		}

		/// <summary>Called from Update while the window is open: reads the pad and moves, presses or goes back.</summary>
		private void UpdatePadMenu() {
			if (!windowOpen_ || !padMenuOn_ || arranging_) {
				ReleasePad();
				return;
			}
			pad_.Poll();
			if (!padSeen_ && pad_.ButtonNames.Values.Any(names => names.Any(n => PadNames[4].Contains(n.Trim().ToLowerInvariant())))) {
				padSeen_ = true;
				TrcLog.Info($"Controller in the menu: {string.Join(", ", pad_.Devices)}.");
			}
			if (padSeen_ && padHoldCar_ && !padHolding_) {
				padHolding_ = pad_.SetDriving(false);
			}
			if (pad_.Pressed.Count == 0) {
				return;
			}
			bool any = true;
			if (PadPressed(0)) {
				navMove_ = Vector2.down;
			} else if (PadPressed(1)) {
				navMove_ = Vector2.up;
			} else if (PadPressed(2)) {
				navMove_ = Vector2.left;
			} else if (PadPressed(3)) {
				navMove_ = Vector2.right;
			} else if (PadPressed(4)) {
				if (navFocus_ >= 0 && Time.unscaledTime < navShownUntil_) {
					navPress_ = true;
				} else {
					navMove_ = Vector2.down;
				}
			} else if (PadPressed(5)) {
				if (CurrentCategory >= 0) {
					GoHome();
				} else {
					CloseWindow();
				}
			} else if (PadPressed(6) || PadPressed(7)) {
				int count = Categories.Length;
				int current = CurrentCategory;
				int next = current < 0 ? (PadPressed(7) ? 0 : count - 1) : (current + (PadPressed(7) ? 1 : count - 1)) % count;
				OpenCategory(next);
			} else {
				any = false;
			}
			if (any) {
				navShownUntil_ = Time.unscaledTime + 30f;
			}
		}

		/// <summary>Gives the pad back to CarX (window closed, or the option turned off).</summary>
		private void ReleasePad() {
			if (padHolding_) {
				pad_.SetDriving(true);
				padHolding_ = false;
			}
			navPress_ = false;
			navMove_ = Vector2.zero;
		}

		/// <summary>The bottom bar with the controller's buttons, like a console menu, once a pad has been seen.</summary>
		private void DrawPadBar() {
			if (!padMenuOn_ || !padSeen_) {
				return;
			}
			EnsureNavStyles();
			DividerLine();
			GUILayout.BeginHorizontal(TabArea);
			GUILayout.FlexibleSpace();
			PadHint("A", "Select");
			PadHint("B", CurrentCategory >= 0 ? "Home" : "Close");
			PadHint("LB", null);
			PadHint("RB", "Category");
			PadHint(shortcut_.ToString().Replace(" ", ""), "Close");
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private static void PadHint(string key, string what) {
			GUILayout.Label(key, keyCap_);
			if (what != null) {
				GUILayout.Label(what, Theme.Small);
				GUILayout.Space(14);
			}
		}

		private void DrawPadOptions() {
			GUILayout.Label("<b>Controller in the menu</b>: the D-pad moves between buttons, A presses, B goes back (or closes on Home), LB and RB change category. " +
				"Open the menu from the pad with a button set under Wheel and controller buttons.", Theme.Body);
			bool on = OnOff(padMenuOn_);
			if (on != padMenuOn_) {
				padMenuOn_ = on;
				SavePref(PadMenuPref, on);
				if (!on) {
					ReleasePad();
				}
			}
			GUILayout.Label("While the menu has the controller, keep the car still (CarX doesn't get the pad's buttons until the menu closes):", Theme.Small);
			bool hold = OnOff(padHoldCar_);
			if (hold != padHoldCar_) {
				padHoldCar_ = hold;
				SavePref(PadHoldPref, hold);
				if (!hold) {
					ReleasePad();
				}
			}
			if (pad_.Devices.Count > 0) {
				GUILayout.Label($"Controllers seen: {string.Join(", ", pad_.Devices)}{(padSeen_ ? "" : " (none with an A button yet)")}.", Theme.Small);
			} else if (!pad_.Available && pad_.Problem.Length > 0) {
				GUILayout.Label(pad_.Problem, Theme.Small);
			}
		}
	}
}
