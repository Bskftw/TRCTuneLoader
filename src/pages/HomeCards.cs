// Home's cards: one big card per part of the mod, each with its live line, and the tools row.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
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

		/// <summary>The Home card's line, made again at most once a second (docs/archive/OPTIMIZATION_REPORT.md item 7: it searched every saved time on every GUI pass).</summary>
		private string TimeAttackCardLine() {
			if (cardLine_ == null || Time.unscaledTime >= cardLineAt_) {
				cardLineAt_ = Time.unscaledTime + 1f;
				cardLine_ = MakeTimeAttackCardLine();
			}
			return cardLine_;
		}

		private string cardLine_;
		private float cardLineAt_;

		private string MakeTimeAttackCardLine() {
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
			foreach (NavPage page in tools.Pages.Where(p => p.Tab != BugTab && p.Tab != OptionsTab)) {
				if (NavButton(page.Name, Theme.Button)) {
					OpenPage(page);
				}
			}
			if (NavButton("Options", Theme.Button)) {
				OpenPage(tools.Pages.FirstOrDefault(p => p.Tab == OptionsTab && p.View == optionsView_) ?? tools.Pages.First(p => p.Tab == OptionsTab));
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
	}
}
