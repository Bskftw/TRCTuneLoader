// The left rail (UI redesign Stage 2, docs/archive/ui-redesign-plan.md): the logo, Home and each part with its icon, the open part's pages under it, and the TRC spec state at the bottom.
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const float RailWidth = 196f;

		/// <summary>Each part's icon, in Part order (Resources/icon_*.png, tools/make_icons.py), and Home's.</summary>
		private static readonly string[] PartIconNames = { "tunes", "specs", "time", "battles", "roam", "tools" };
		private Texture2D optionsIcon_;
		private Texture2D[] partIcons_;
		private Texture2D homeIcon_;
		private bool iconsTried_;

		private Vector2 railScroll_;
		private GUIStyle railArea_, railItem_, railItemOn_, railPage_, railPageOn_, railGroup_, railBrand_, railVersion_, railName_;
		private int railStylesVersion_ = -1;

		private void EnsureRail() {
			if (!iconsTried_) {
				iconsTried_ = true;
				partIcons_ = new Texture2D[PartIconNames.Length];
				Assembly own = Assembly.GetExecutingAssembly();
				for (int i = 0; i < PartIconNames.Length; i++) {
					partIcons_[i] = Kino.Utils.LoadEmbeddedTexture(own, $"TRC.TuneLoader.Resources.icon_{PartIconNames[i]}.png");
				}
				homeIcon_ = Kino.Utils.LoadEmbeddedTexture(own, "TRC.TuneLoader.Resources.icon_home.png");
				optionsIcon_ = Kino.Utils.LoadEmbeddedTexture(own, "TRC.TuneLoader.Resources.icon_options.png");
			}
			if (railStylesVersion_ == Theme.Version && railArea_ != null && railArea_.normal.background != null) {
				return;
			}
			railStylesVersion_ = Theme.Version;
			railArea_ = new GUIStyle {
				normal = { background = Theme.Rounded(Theme.Rail, Theme.Rail, 10) },
				border = new RectOffset(10, 10, 10, 10),
				padding = new RectOffset(10, 10, 12, 10),
				margin = new RectOffset(1, 0, 1, 1),
			};
			railBrand_ = new GUIStyle(Theme.SectionTitle) { wordWrap = false };
			railVersion_ = new GUIStyle(Theme.StatLabel) { wordWrap = false };
			railItem_ = new GUIStyle(Theme.Row) { font = Theme.SectionTitle.font, fontSize = Theme.SectionTitle.fontSize - 1, padding = new RectOffset(36, 8, 8, 8), wordWrap = false };
			// The name is drawn by RailItem in the part's colour (with a shadow when open), so the button's own text is clear.
			railName_ = new GUIStyle(railItem_) { normal = { background = null }, hover = { background = null } };
			railItem_.normal.textColor = railItem_.hover.textColor = railItem_.active.textColor = Color.clear;
			railItemOn_ = new GUIStyle(railItem_);
			railItemOn_.normal.background = railItemOn_.hover.background = Theme.Rounded(Theme.Raised, Theme.Raised, 6);
			railPage_ = new GUIStyle(Theme.Row) { padding = new RectOffset(36, 8, 5, 5) };
			railPage_.normal.textColor = Theme.Muted;
			railPage_.hover.textColor = Theme.Text;
			railPageOn_ = new GUIStyle(railPage_);
			railPageOn_.normal.textColor = railPageOn_.hover.textColor = Theme.Text;
			railGroup_ = new GUIStyle(Theme.StatLabel) { padding = new RectOffset(36, 0, 6, 2) };
		}

		private void DrawRail() {
			EnsureRail();
			int current = CurrentCategory;
			GUILayout.BeginVertical(railArea_, GUILayout.Width(RailWidth), GUILayout.ExpandHeight(true));

			// The logo, the name and the version; the logo goes Home (and counts the frog's clicks, as before).
			GUILayout.BeginHorizontal();
			if (logo_ != null) {
				Rect logoRect = GUILayoutUtility.GetRect(34f, 34f, GUILayout.Width(34f), GUILayout.Height(34f));
				GUI.DrawTexture(logoRect, logo_, ScaleMode.ScaleToFit);
				if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && logoRect.Contains(Event.current.mousePosition)) {
					Event.current.Use();
					LogoClicked();
					if (current >= 0) {
						GoHome();
					}
				}
				GUILayout.Space(8f);
			}
			GUILayout.BeginVertical();
			GUILayout.Label("TRC Tune Loader", railBrand_);
			GUILayout.Label($"v{Version}", railVersion_);
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
			GUILayout.Space(10f);

			railScroll_ = GUILayout.BeginScrollView(railScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			if (RailItem(homeIcon_, "Home", Theme.ToHex(Theme.Accent), current < 0) && current >= 0) {
				GoHome();
			}
			NavCategory[] all = Categories;
			DrawRailParts(all, current, bottom: false);
			GUILayout.EndScrollView();
			// Options sits at the bottom, apart from the parts.
			Hairline(4f);
			DrawRailParts(all, current, bottom: true);

			GUILayout.Space(6f);
			DrawSpecPill();
			GUILayout.EndVertical();
		}

		private void DrawRailParts(NavCategory[] all, int current, bool bottom) {
			for (int c = 0; c < all.Length; c++) {
				NavCategory category = all[c];
				if (category.Bottom != bottom) {
					continue;
				}
				Texture2D icon = category.Icon == "options" ? optionsIcon_
					: category.Part >= 0 && category.Part < partIcons_.Length ? partIcons_[category.Part] : null;
				if (RailItem(icon, category.Name, category.Hex, c == current) && c != current) {
					OpenCategory(c);
				}
				if (c == current) {
					DrawRailPages(category);
				}
			}
		}

		/// <summary>
		/// One part in the rail: its icon and name always in the part's own colour (bskftw, Nov 19: "not to be grey to start";
		/// the colours set in Options > Look). The open one sits on a raised background with a soft dark shadow under its
		/// name and icon, for depth. True when pressed.
		/// </summary>
		private bool RailItem(Texture2D icon, string name, string hex, bool open) {
			bool pressed = NavButton(name, open ? railItemOn_ : railItem_);
			if (Event.current.type == EventType.Repaint) {
				Rect r = GUILayoutUtility.GetLastRect();
				Color tint = Theme.TryHex(hex, out Color c) ? c : Theme.Text;
				if (!open && !r.Contains(Event.current.mousePosition)) {
					tint = Color.Lerp(tint, Theme.Rail, 0.18f); // a touch softer until it's open or under the mouse
				}
				if (open) {
					GUI.DrawTexture(new Rect(r.x, r.y + 6f, 3f, r.height - 12f), Theme.AccentLine);
				}
				Color before = GUI.color;
				var iconRect = new Rect(r.x + 10f, r.center.y - 9f, 18f, 18f);
				if (open) {
					var shadow = new Color(0f, 0f, 0f, 0.55f);
					railName_.normal.textColor = shadow;
					railName_.Draw(new Rect(r.x + 1.5f, r.y + 2f, r.width, r.height), new GUIContent(name), false, false, false, false);
					if (icon != null) {
						GUI.color = shadow;
						GUI.DrawTexture(new Rect(iconRect.x + 1.5f, iconRect.y + 2f, iconRect.width, iconRect.height), icon, ScaleMode.ScaleToFit);
						GUI.color = before; // the shadow's tint would darken the name drawn next
					}
				}
				railName_.normal.textColor = tint;
				railName_.Draw(r, new GUIContent(name), false, false, false, false);
				if (icon != null) {
					GUI.color = tint;
					GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
				}
				GUI.color = before; // risk 8: a tint left set colours everything after it
			}
			return pressed;
		}

		/// <summary>The open part's pages under it, with their small notes and group headings (Options' pages), as the old left card had.</summary>
		private void DrawRailPages(NavCategory category) {
			string group = "";
			foreach (NavPage page in category.Pages) {
				if (page.Group != group) {
					group = page.Group;
					if (group.Length > 0) {
						GUILayout.Label(group, railGroup_);
					}
				}
				string note = page.Note();
				bool open = IsOpen(page);
				string text = $"{page.Name}{(note.Length > 0 ? $"\n<size={Mathf.RoundToInt(11 * Theme.TextScale)}><color={Theme.MutedHex}>{note}</color></size>" : "")}";
				if (NavButton(text, open ? railPageOn_ : railPage_) && !open) {
					OpenPage(page);
				}
				if (open && Event.current.type == EventType.Repaint) {
					Rect r = GUILayoutUtility.GetLastRect();
					GUI.DrawTexture(new Rect(r.x + 22f, r.y + 5f, 2f, r.height - 10f), Theme.AccentLine);
				}
			}
			if (category.Name == "Tools" && NavButton("Arrange the screen", Theme.Button)) {
				StartArranging();
			}
			GUILayout.Space(6f);
		}

		/// <summary>
		/// The top of the content: the page's name (or Home and where you are), pin, the page's own choices, and close.
		/// The same on every page (Stage 2); pages keep their insides until their own stage.
		/// </summary>
		private void DrawPageTop() {
			int current = CurrentCategory;
			GUILayout.BeginHorizontal();
			if (current < 0) {
				GUILayout.BeginVertical();
				GUILayout.Label("Home", Theme.PageTitle);
				string where = map_ != null ? map_.Name : "Not on a map";
				GUILayout.Label($"{where}{(car_ != null ? $", {car_.CarName}" : "")}", Theme.PageLead);
				GUILayout.EndVertical();
				GUILayout.FlexibleSpace();
				GUILayout.BeginVertical();
				GUILayout.Space(8f);
				DrawSearchBox();
				GUILayout.EndVertical();
			} else {
				NavCategory category = Categories[current];
				NavPage shown = null;
				foreach (NavPage page in category.Pages) {
					if (IsOpen(page)) {
						shown = page;
						break;
					}
				}
				GUILayout.Label(shown != null ? shown.Name : category.Name, Theme.PageTitle, GUILayout.ExpandWidth(false));
				if (shown != null) {
					GUILayout.BeginVertical();
					GUILayout.Space(8f);
					bool pinned = pinnedKeys_.Contains(shown.Key);
					if ((pinned || pinnedKeys_.Count < MostPinned) && NavButton(pinned ? "Unpin from Home" : "Pin to Home", Theme.Chip)) {
						TogglePin(shown);
					}
					GUILayout.EndVertical();
				}
				GUILayout.FlexibleSpace();
				if (shown != null) {
					GUILayout.BeginVertical();
					GUILayout.Space(6f);
					GUILayout.BeginHorizontal();
					DrawPageOptions(shown);
					GUILayout.EndHorizontal();
					GUILayout.EndVertical();
				}
			}
			if (NavButton("×", Theme.Close)) {
				CloseWindow();
			}
			GUILayout.EndHorizontal();
			Hairline(8f);
		}
	}
}
