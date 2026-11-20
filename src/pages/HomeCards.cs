// Home's cards: one big card per part of the mod, each with its live line, and the tools row.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		// Home fits without scrolling (bskftw, Nov 19): small gaps, short tiles, links on one row.
		private const float CardGap = 8f;

		/// <summary>
		/// Home's tiles (UI redesign Stage 3; bskftw, Nov 18: "similar to the gradients ... just better"): every part in
		/// a gradient tile of its colour, two by two, with its icon, its live line and its pages. Tools has one too, in
		/// place of the old tools row (which no longer fit beside the rail).
		/// </summary>
		private void DrawHomeCards() {
			EnsureNavStyles();
			EnsureRail();
			NavCategory[] all = Categories;
			float inside = WindowWidth - RailWidth - ContentArea.padding.horizontal - 18f;
			float card = Mathf.Floor((inside - CardGap) / 2f);
			float tall = Mathf.Round(92f * (1f + (Theme.TextScale - 1f) * 0.6f));
			string[] lines = { TrcCardLine(), SpecsCardLine(), TimeAttackCardLine(), BattlesCardLine(), FreeRoamCardLine(), "HUD, host tools, your stats and more" };
			for (int row = 0; row < 3; row++) {
				GUILayout.BeginHorizontal();
				for (int col = 0; col < 2; col++) {
					int i = row * 2 + col;
					if (col > 0) {
						GUILayout.Space(CardGap);
					}
					DrawHomeTile(all[i], i, lines[i], card, tall);
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.Space(CardGap);
			}
		}

		private void DrawHomeTile(NavCategory category, int index, string line, float width, float height) {
			// Each part's own colour, or the player's accent for all of them (Options > Look, "Home tiles").
			Color colour = homeTilesAccent_ ? Theme.Accent : HexColor(category.Hex);
			GUIStyle tile = Theme.Tile(colour);
			GUILayout.BeginVertical(tile, GUILayout.Width(width), GUILayout.Height(height));
			GUILayout.BeginHorizontal();
			GUILayout.Label(category.Name, Theme.TileTitle);
			GUILayout.FlexibleSpace();
			Rect icon = GUILayoutUtility.GetRect(24f, 24f, GUILayout.Width(24f), GUILayout.Height(24f));
			Texture2D picture = category.Part >= 0 && category.Part < partIcons_.Length ? partIcons_[category.Part] : null;
			if (picture != null && Event.current.type == EventType.Repaint) {
				Color before = GUI.color;
				GUI.color = colour;
				GUI.DrawTexture(icon, picture, ScaleMode.ScaleToFit);
				GUI.color = before;
			}
			GUILayout.EndHorizontal();
			GUILayout.Label($"<color={Theme.ToHex(Color.Lerp(colour, Color.white, 0.2f))}>{line}</color>", Theme.TileState);
			GUILayout.FlexibleSpace();
			// The page links that fit on one row, so every tile is the same height and Home doesn't scroll (bskftw, Nov 19).
			// The rail lists them all.
			float room = width - tile.padding.horizontal;
			float used = 0f;
			GUILayout.BeginHorizontal();
			foreach (NavPage page in category.Pages) {
				float wide = Theme.TileLink.CalcSize(new GUIContent(page.Name)).x + Theme.TileLink.margin.horizontal;
				if (used + wide > room) {
					break;
				}
				used += wide;
				if (NavButton(page.Name, Theme.TileLink)) {
					OpenPage(page);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			Rect card = GUILayoutUtility.GetLastRect();
			// The rest of the tile opens the part (its page links are already taken by then).
			if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && card.Contains(Event.current.mousePosition)) {
				Event.current.Use();
				OpenCategory(index);
			}
		}

		// ---- Your last run and today's challenge, side by side (bskftw, Nov 18), worked out at most once a second ----

		private TimeAttackRecord homeRun_;
		private Course homeRunCourse_, homeChallenge_;
		private string homeRunNote_ = "";

		/// <summary>Home's tiles all in the accent colour instead of each part's (the home_tiles_accent pref).</summary>
		private bool homeTilesAccent_;

		/// <summary>The last run's sectors in their pace colours (bskftw, Nov 19: "the cool little coloured sector lines"), worked out with the rest.</summary>
		private readonly List<Color> homeRunSectors_ = new List<Color>();
		private float homePairAt_;

		/// <summary>The last run's track name: from the map's courses, else the name saved with the run (bskftw, Nov 19: off its map Home showed the code).</summary>
		private string HomeRunTrack() =>
			homeRunCourse_?.Name ?? (homeRun_.CourseName.Length > 0 ? homeRun_.CourseName : homeRun_.CourseCode);

		private void DrawHomePair() {
			if (Event.current.type == EventType.Layout && Time.unscaledTime >= homePairAt_) {
				homePairAt_ = Time.unscaledTime + 1f;
				homeRun_ = times_.Where(r => !r.IsLap).OrderByDescending(r => r.When).FirstOrDefault();
				homeRunCourse_ = homeRun_ != null ? courses_.ByCode(homeRun_.CourseCode) : null;
				homeRunNote_ = "";
				if (homeRun_ != null) {
					long best = times_.Where(r => !r.IsLap && r.CourseCode == homeRun_.CourseCode && r.CarId == homeRun_.CarId).Min(r => r.TimeMs);
					homeRunNote_ = homeRun_.TimeMs <= best ? $"<color={Theme.GoodHex}>Your best on it in this car</color>"
						: $"+{TimeAttackRun.FormatTime((homeRun_.TimeMs - best) / 1000.0)} on your best in this car";
				}
				homeRunSectors_.Clear();
				if (homeRun_ != null && homeRun_.SectorMs.Length > 1) {
					// The same colours as My times: purple best ever on this run's kind, green your best in this car, amber slower.
					TimeAttackRecord r = homeRun_;
					TimeKind kind = TimeKind.Of(r);
					int count = r.SectorMs.Length;
					long?[] best = TimeAttackLog.BestSectorsMs(times_, x => x.CourseCode == r.CourseCode && kind.Matches(x), count);
					long?[] mine = TimeAttackLog.BestSectorsMs(times_, x => x.CourseCode == r.CourseCode && x.CarId == r.CarId && x.TuneSet == r.TuneSet && kind.Matches(x), count);
					for (int i = 0; i < count; i++) {
						Theme.TryHex(PaceColour(r.SectorMs[i], best[i], mine[i], orEqual: true), out Color c);
						homeRunSectors_.Add(c);
					}
				}
				homeChallenge_ = ChallengeCourses().Day;
			}
			float inside = WindowWidth - RailWidth - ContentArea.padding.horizontal - 18f;
			float half = Mathf.Floor((inside - CardGap) / 2f);
			GUILayout.BeginHorizontal();
			BeginSection("Your last run", homeRun_ != null ? $"{HomeRunTrack()}, {homeRun_.Car}" : null, GUILayout.Width(half));
			if (homeRun_ == null) {
				if (EmptyState("No runs yet. Pick a track and go.", "Tracks")) {
					OpenPage(Categories.SelectMany(c => c.Pages).First(p => p.Tab == CoursesTab && p.View == 0));
				}
			} else {
				GUILayout.BeginHorizontal();
				GUILayout.Label(TimeAttackRun.FormatTime(homeRun_.Seconds), Theme.PageTitle, GUILayout.ExpandWidth(false));
				GUILayout.BeginVertical();
				GUILayout.Space(8f);
				GUILayout.Label(homeRunNote_, Theme.SectionNote);
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();
				if (homeRunSectors_.Count > 0) {
					Rect strip = GUILayoutUtility.GetRect(10f, 6f, GUILayout.ExpandWidth(true));
					if (Event.current.type == EventType.Repaint) {
						const float gap = 4f;
						float each = (strip.width - gap * (homeRunSectors_.Count - 1)) / homeRunSectors_.Count;
						Color before = GUI.color;
						for (int i = 0; i < homeRunSectors_.Count; i++) {
							GUI.color = homeRunSectors_[i];
							GUI.DrawTexture(new Rect(strip.x + i * (each + gap), strip.y, each, strip.height), Texture2D.whiteTexture);
						}
						GUI.color = before;
					}
					GUILayout.Space(8f);
				}
				GUILayout.BeginHorizontal();
				bool here = homeRunCourse_ != null && CoursesHere().Contains(homeRunCourse_);
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && here;
				if (NavButton("Go again", Theme.ButtonPrimary)) {
					StartTimeAttack(homeRunCourse_);
				}
				GUI.enabled = enabled;
				// Lap compare is on My times, at the top (since Nov 18).
				if (NavButton("Lap compare", Theme.Button)) {
					OpenPage(Categories.SelectMany(c => c.Pages).First(p => p.Tab == CoursesTab && p.View == 2));
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			EndSection();
			GUILayout.Space(CardGap);
			BeginSection("Today's challenge", homeChallenge_?.Name, GUILayout.Width(half));
			if (homeChallenge_ == null) {
				GUILayout.Label(map_ == null ? "Drive onto a map to see its challenge." : "This map has no tracks of its own to pick one from.", Theme.SectionNote);
			} else {
				var t = Challenges.Times(homeChallenge_.Length, homeChallenge_.Kind);
				GUILayout.Label($"<color=#F2C94C>Gold {TimeAttackRun.FormatTime(t.Gold)}</color>   <color=#C9CFDB>Silver {TimeAttackRun.FormatTime(t.Silver)}</color>   <color=#C08A55>Bronze {TimeAttackRun.FormatTime(t.Bronze)}</color>", Theme.SettingLabel);
				string key = $"day {Challenges.DayKey(DateTime.Now.Date)} {homeChallenge_.Code}";
				GUILayout.BeginHorizontal();
				GUILayout.Label(ChallengeBests().TryGetValue(key, out long ms)
					? $"Your best today {TimeAttackRun.FormatTime(ms / 1000.0)}: {Challenges.MedalName(Challenges.MedalFor(ms / 1000.0, homeChallenge_.Length, homeChallenge_.Kind))}"
					: "No time yet today.", Theme.SectionNote);
				GUILayout.FlexibleSpace();
				if (NavButton("Start it", Theme.Button)) {
					StartTimeAttack(homeChallenge_);
				}
				GUILayout.EndHorizontal();
			}
			EndSection();
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
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

		/// <summary>
		/// Home's last line: Arrange the screen and Report a bug. The other tools are on their tile and in the rail
		/// (the old row opened Options from Tools' pages, which broke when Options got its own part).
		/// </summary>
		private void DrawToolsRow() {
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			if (NavButton("Arrange the screen", Theme.ButtonQuiet)) {
				StartArranging();
			}
			if (NavButton("Report a bug", Theme.ButtonQuiet)) {
				OpenPage(Categories.SelectMany(c => c.Pages).First(p => p.Tab == BugTab));
			}
			GUILayout.EndHorizontal();
		}
	}
}
