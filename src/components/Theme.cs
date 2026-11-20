// The window's look: colours, styles and rounded textures for IMGUI.
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The TRC window's look: dark panels with TRC sakura pink, drawn with Unity's IMGUI.
	/// Styles need an active GUI, so they're built on first use inside OnGUI. The rounded
	/// shapes are small textures generated here, stretched with 9-slice borders.
	///
	/// Since 0.15.0 the player can pick the background, accent and text colours and a text size (Options):
	/// <see cref="Configure"/> works out the other shades from those and builds the styles again.
	/// </summary>
	internal static partial class Theme {
		// TRC Night (UI redesign, Nov 18: docs/archive/ui-redesign-plan.md). Players who never picked colours get it.
		public static readonly Color DefaultBackground = Hex(0x1A1D27);
		public static readonly Color DefaultAccent = Hex(0xE0628F);
		public static readonly Color DefaultText = Hex(0xE9EAF0);

		public static Color Background = DefaultBackground;
		public static Color Panel = Hex(0x1D2029);
		public static Color Border = Hex(0x363C52);
		public static Color Control = Hex(0x434A62);
		public static Color Hover = Hex(0x2B3042);

		/// <summary>The redesign's layers: the menu rail (darker than the window), and raised controls (lighter than a panel).</summary>
		public static Color Rail = Hex(0x15171F);
		public static Color Raised = Hex(0x2B3042);
		public static Color Text = DefaultText;
		public static Color Muted = Hex(0x9A9AA3);
		public static Color Accent = DefaultAccent;
		public static Color AccentHover = Hex(0xE0668F);
		public static Color AccentSoft = Hex(0x4B1528);
		public static Color AccentText = Hex(0xF4C0D1);

		/// <summary>
		/// The buttons' colour (bskftw, Nov 3: one colour for every button, chosen in Look, not each part's colour):
		/// the main buttons, picked chips and picked rows. Follows the accent unless the player picks one.
		/// </summary>
		public static Color ButtonColour = DefaultAccent;
		public static Color ButtonHover = Hex(0xE0668F);
		public static Color ButtonSoft = Hex(0x4B1528);

		/// <summary>Text on a filled button: dark on a light button colour, white on a dark one.</summary>
		public static Color ButtonText = Color.white;

		/// <summary>Text size, on top of the window size: 1 is the normal size.</summary>
		public static float TextScale { get; private set; } = 1f;

		/// <summary>Goes up every time the colours or text size change, so styles made from them know to be made again.</summary>
		public static int Version { get; private set; }

		/// <summary>The textures the styles use, so a rebuild can let the old ones go.</summary>
		private static readonly System.Collections.Generic.List<Texture2D> made_ = new System.Collections.Generic.List<Texture2D>();

		/// <summary>
		/// The player's colours and text size. Panels, borders and buttons are shades between the background and
		/// the text (lighter on a dark background, darker on a light one); the accent gives the pink parts. The
		/// default colours give exactly the TRC look.
		/// </summary>
		/// <param name="muted">The second text colour (notes, labels, the grey text), or null to work it out from the text.</param>
		public static void Configure(Color background, Color accent, Color text, float textScale, Color? muted = null, Color? button = null) {
			TextScale = Mathf.Clamp(textScale, 0.8f, 1.6f);
			// The default look's own muted grey; anyone else's is worked out from their text (LookTokens).
			if (muted == null && Same(background, DefaultBackground) && Same(text, DefaultText)) {
				muted = LookTokens.Default.Muted;
			}
			LookTokens look = LookTokens.From(background, accent, text, muted, button);
			Background = look.Night;
			Rail = look.Rail;
			Panel = look.Panel;
			Raised = look.Raised;
			Hover = look.Raised;
			Border = look.Line;
			Control = Color.Lerp(look.Line, look.Text, 0.12f);
			Text = look.Text;
			Muted = look.Muted;
			Accent = look.Accent;
			AccentHover = look.AccentHover;
			AccentSoft = look.AccentSoft;
			AccentText = look.AccentText;
			ButtonColour = look.Button;
			ButtonHover = look.ButtonHover;
			ButtonSoft = look.ButtonSoft;
			ButtonText = look.ButtonText;
			MutedHex = ToHex(Muted);
			ready_ = false;
			Version++;
		}

		// ---- Fonts (UI redesign, Nov 18) ----

		/// <summary>"Road sign" (Bahnschrift for headings, buttons and numbers, Segoe UI for text) or false for Unity's own font.</summary>
		public static bool RoadSign { get; private set; } = true;

		/// <summary>The fonts in use (null: Unity's own), and a line for the log and the Look page saying which.</summary>
		public static Font SignFont, SignBold, BodyFont;
		public static string FontNote { get; private set; } = "";
		private static bool fontsTried_;

		// Letters a font lacks come from the next in the list: Bahnschrift has no Japanese, Korean or Cyrillic (risk 3).
		private static readonly string[] Fallbacks = { "Segoe UI", "Yu Gothic UI", "Meiryo", "Malgun Gothic", "Microsoft YaHei", "Arial" };

		/// <summary>
		/// Headings and buttons in Unity's bold of Bahnschrift instead of its regular (bskftw's PC has no SemiBold family,
		/// Nov 18). The ui_font_bold pref; tried on the Look book.
		/// </summary>
		public static bool HeavyHeadings { get; private set; }

		public static void UseHeavyHeadings(bool on) {
			if (on != HeavyHeadings) {
				HeavyHeadings = on;
				ready_ = false;
				Version++;
			}
		}

		/// <summary>The weight strong text is drawn in: the sign font's own, or Unity's bold when asked for (or with Unity's font).</summary>
		private static FontStyle StrongWeight => SignStrong == null || HeavyHeadings ? FontStyle.Bold : FontStyle.Normal;

		/// <summary>The HUD plates' opacity, 0.4 to 1 (Options > Look, "Over the track"; the hud_opacity pref).</summary>
		public static float HudOpacity { get; private set; } = 0.86f;

		/// <summary>True: the plates are the menu's own background colour. False: classic dark, whatever the menu looks like.</summary>
		public static bool HudFollowsMenu { get; private set; } = true;

		public static void UseHud(float opacity, bool followMenu) {
			opacity = Mathf.Clamp(opacity, 0.4f, 1f);
			if (Mathf.Abs(opacity - HudOpacity) > 0.001f || followMenu != HudFollowsMenu) {
				HudOpacity = opacity;
				HudFollowsMenu = followMenu;
				ready_ = false;
				Version++;
			}
		}

		public static void UseRoadSign(bool on) {
			if (on != RoadSign) {
				RoadSign = on;
				ready_ = false;
				Version++;
			}
		}

		/// <summary>
		/// Windows' own fonts, made once. Missing ones (an old Windows, Proton on Linux) leave Unity's font, said once
		/// in the log (risk 4). The semibold is its own family name, so bold isn't smeared from the regular (risk 5).
		/// </summary>
		private static void LoadFonts() {
			if (fontsTried_) {
				return;
			}
			fontsTried_ = true;
			try {
				var installed = new System.Collections.Generic.HashSet<string>(Font.GetOSInstalledFontNames(), System.StringComparer.OrdinalIgnoreCase);
				Font Make(params string[] first) {
					string[] have = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(first, installed.Contains));
					return have.Length == 0 ? null : Font.CreateDynamicFontFromOSFont(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(have, Fallbacks)), 14);
				}
				SignFont = Make("Bahnschrift");
				SignBold = Make("Bahnschrift SemiBold", "Bahnschrift");
				BodyFont = Make("Segoe UI");
				FontNote = SignFont != null
					? $"Road sign fonts: Bahnschrift{(installed.Contains("Bahnschrift SemiBold") ? " (and SemiBold)" : "")}, {(BodyFont != null ? "Segoe UI" : "Unity's font for text")}."
					: "Bahnschrift isn't on this PC, so the menu uses Unity's own font.";
			} catch (System.Exception e) {
				SignFont = SignBold = BodyFont = null;
				FontNote = $"The road sign fonts couldn't be loaded ({e.Message}), so the menu uses Unity's own font.";
			}
			TrcLog.Info($"Menu: {FontNote}");
		}

		private static Font Sign => RoadSign ? SignFont : null;
		private static Font SignStrong => RoadSign ? (SignBold ?? SignFont) : null;
		private static Font TextFont => RoadSign ? BodyFont : null;

		public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

		private static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.003f && Mathf.Abs(a.g - b.g) < 0.003f && Mathf.Abs(a.b - b.b) < 0.003f;

		/// <summary>"#D4537E" for a colour (saved in the prefs), and back (false when it isn't one).</summary>
		public static string ToHex(Color c) => $"#{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}";

		public static bool TryHex(string text, out Color color) {
			color = Color.black;
			string hex = (text ?? "").Trim().TrimStart('#');
			if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int rgb)) {
				return false;
			}
			color = Hex(rgb);
			return true;
		}
		public static readonly Color Good = Hex(0x173404);
		public static readonly Color GoodText = Hex(0xC0DD97);
		public static readonly Color Bad = Hex(0x501313);
		public static readonly Color BadText = Hex(0xF7C1C1);
		public static readonly Color Warn = Hex(0x412402);
		public static readonly Color WarnText = Hex(0xFAC775);
		public static readonly Color Neutral = Hex(0x26262C);

		/// <summary>The grey text inside rich text: follows <see cref="Muted"/>, so Options > Look changes it too.</summary>
		public static string MutedHex { get; private set; } = "#9A9AA3";
		public const string GoodHex = "#C0DD97";
		public const string BadHex = "#F7C1C1";
		public const string WarnHex = "#FAC775";
		public const string AccentHex = "#F4C0D1";

		/// <summary>The best a sector or lap has ever been driven (like purple sectors in racing).</summary>
		public const string PurpleHex = "#C9A7FF";

		/// <summary>
		/// The tagline, and the four parts of the mod with their colours: the same on the README, the images and
		/// Discord. Tunes pink, time attack purple, battles amber, traps/games/ratings green.
		/// </summary>
		public const string Tagline = "Official TRC tunes · Time attack · Course builder · Battles & races";
		public static readonly string[] Pillars = { "Tunes & TRC spec", "Time attack & course builder", "Battles, races & tournaments", "Speed traps, room games & ratings" };
		public static readonly string[] PillarHex = { AccentHex, PurpleHex, WarnHex, GoodHex };

		public static GUIStyle Window, Title, Subtitle, Tab, TabActive, Close;
		public static GUIStyle Heading, Body, Small, SectionLabel, Group;
		public static GUIStyle Chip, ChipActive, Button, ButtonPrimary, ButtonQuiet;
		public static GUIStyle Row, RowSelected, Card, Guide;
		public static GUIStyle PillGood, PillBad, PillWarn, PillNeutral;
		public static GUIStyle BadgeGood, BadgeBad, BadgeWarn, Popup;
		public static GUIStyle ScrollBar, ScrollThumb;
		public static GUIStyle Field, FieldWrapped;
		public static GUIStyle HudPanel, HudPill, HudOutline, HudSmall, HudTimer, HudBig, MarkerStart, MarkerFinish;
		public static Texture2D AccentLine, Divider, StartLine, FinishLine, ProgressBack, GoodFill, BadFill;

		/// <summary>A copy of Unity's skin with TRC scrollbars and plain scroll areas, used only while the window draws.</summary>
		public static GUISkin Skin;

		private static bool ready_;

		public static void Ensure() {
			if (ready_ && Window?.normal.background != null && Skin != null) {
				return;
			}
			foreach (Texture2D old in made_) {
				if (old != null) {
					Object.Destroy(old);
				}
			}
			made_.Clear();
			tiles_.Clear();
			LoadFonts();

			Window = Box(Rounded(Background, Border, 10), 10, new RectOffset(0, 0, 0, 0));

			Title = Strong(TextStyle(16, Theme.Text, FontStyle.Bold));
			Subtitle = TextStyle(11, Muted);
			Heading = Strong(TextStyle(16, Theme.Text, FontStyle.Bold));
			Body = TextStyle(13, Theme.Text);
			Small = TextStyle(12, Muted);
			// Stage 4: the label over a card reads as its section's title (98 uses), in the text colour.
			SectionLabel = Strong(TextStyle(13, Theme.Text, FontStyle.Bold));
			SectionLabel.margin = new RectOffset(0, 0, 10, 4);
			Group = Strong(TextStyle(12, AccentText, FontStyle.Bold));
			Group.margin = new RectOffset(0, 0, 10, 2);

			Tab = SignStyle(TextStyle(13, Muted));
			Tab.wordWrap = false;
			Tab.padding = new RectOffset(10, 10, 9, 9);
			Tab.hover.textColor = Theme.Text;
			TabActive = new GUIStyle(Tab) { normal = { textColor = AccentText }, hover = { textColor = AccentText } };

			Close = TextStyle(18, Muted);
			Close.alignment = TextAnchor.MiddleCenter;
			Close.hover.textColor = Theme.Text;
			Close.fixedWidth = 28;
			Close.fixedHeight = 28;

			// The choices not picked are in the second text colour, so Look changes them too (bskftw, Nov 3).
			Chip = SignStyle(Clickable(Rounded(Raised, Border, 12), Rounded(Color.Lerp(Raised, Theme.Text, 0.06f), Control, 12), 12, Muted, new RectOffset(12, 12, 4, 5)));
			Chip.hover.textColor = Theme.Text;
			ChipActive = Strong(Clickable(Rounded(ButtonColour, ButtonColour, 12), Rounded(ButtonHover, ButtonHover, 12), 12, ButtonText, new RectOffset(12, 12, 4, 5)));
			ChipActive.fontStyle = StrongWeight;

			// Three kinds of button (the plan's components): Primary filled, Button raised with an edge, Quiet just text.
			Button = Strong(Clickable(Rounded(Raised, Border, 6), Rounded(Color.Lerp(Raised, Theme.Text, 0.06f), Control, 6), 13, Theme.Text, new RectOffset(14, 14, 8, 8)));
			ButtonPrimary = Strong(Clickable(Rounded(ButtonColour, ButtonColour, 6), Rounded(ButtonHover, ButtonHover, 6), 13, ButtonText, new RectOffset(14, 14, 8, 8)));
			ButtonPrimary.fontStyle = StrongWeight;
			ButtonQuiet = Strong(Clickable(Rounded(Color.clear, Color.clear, 6), Rounded(Raised, Raised, 6), 13, Muted, new RectOffset(8, 8, 8, 8)));
			ButtonQuiet.hover.textColor = Theme.Text;

			Row = Clickable(Rounded(Color.clear, Color.clear, 6), Rounded(Hover, Color.clear, 6), 13, Theme.Text, new RectOffset(10, 10, 6, 6));
			Row.alignment = TextAnchor.MiddleLeft;
			Row.richText = true;
			Row.wordWrap = true;
			RowSelected = new GUIStyle(Row) {
				normal = { background = Rounded(ButtonSoft, Color.Lerp(ButtonSoft, ButtonColour, 0.6f), 6), textColor = Theme.Text },
				hover = { background = Rounded(ButtonSoft, ButtonHover, 6), textColor = Theme.Text },
			};

			Card = Box(Rounded(Panel, Border, 8), 8, new RectOffset(12, 12, 10, 10));
			Guide = Box(Rounded(AccentSoft, Color.Lerp(AccentSoft, Accent, 0.55f), 8), 8, new RectOffset(14, 14, 10, 10));
			Guide.margin = new RectOffset(0, 0, 0, 10);

			PillGood = Pill(Good, GoodText);
			PillBad = Pill(Bad, BadText);
			PillWarn = Pill(Warn, WarnText);
			PillNeutral = Pill(Neutral, Muted);

			BadgeGood = Badge(Good, GoodText);
			BadgeBad = Badge(Bad, BadText);
			BadgeWarn = Badge(Warn, WarnText);
			Popup = Box(Rounded(Bad, BadText, 8), 8, new RectOffset(16, 16, 12, 12));
			Popup.font = TextFont;
			Popup.fontSize = Size(14);
			Popup.wordWrap = true;
			Popup.richText = true;
			Popup.alignment = TextAnchor.MiddleCenter;
			Popup.normal.textColor = BadText;
			Popup.hover.background = Popup.normal.background;
			Popup.hover.textColor = Theme.Text;
			Popup.active.background = Popup.normal.background;
			Popup.active.textColor = Theme.Text;

			ScrollBar = new GUIStyle { normal = { background = Solid(Panel) }, fixedWidth = 8, margin = new RectOffset(4, 0, 0, 0) };
			ScrollThumb = new GUIStyle { normal = { background = Rounded(Control, Control, 3) }, border = new RectOffset(4, 4, 4, 4), fixedWidth = 8 };

			Field = Box(Rounded(Panel, Control, 6), 6, new RectOffset(10, 10, 7, 7));
			Field.font = TextFont;
			Field.fontSize = Size(13);
			Field.normal.textColor = Theme.Text;
			Field.hover.background = Rounded(Panel, Muted, 6);
			Field.hover.textColor = Theme.Text;
			Field.focused.background = Rounded(Panel, Accent, 6);
			Field.focused.textColor = Theme.Text;
			Field.clipping = TextClipping.Clip;
			Field.margin = new RectOffset(0, 6, 3, 3);

			// For boxes you write a few lines in (the bug report note): the text goes onto the next line instead of off the right.
			FieldWrapped = new GUIStyle(Field) { wordWrap = true };

			// Over the track (Stage 5): one plate for every box, the menu's colour (or classic dark) at the player's opacity, a faint light edge.
			Color plate = HudFollowsMenu ? Background : Hex(0x14161E);
			Texture2D hudBackground = Rounded(new Color(plate.r, plate.g, plate.b, HudOpacity), new Color(1f, 1f, 1f, 0.08f), 8);
			HudPanel = Box(hudBackground, 8, new RectOffset(0, 0, 0, 0));
			HudPill = Box(hudBackground, 8, new RectOffset(14, 14, 8, 9));
			HudPill.font = TextFont;
			HudPill.fontSize = Size(13);
			HudPill.wordWrap = true;
			HudPill.richText = true;
			HudPill.alignment = TextAnchor.MiddleCenter;
			HudPill.normal.textColor = Theme.Text;
			HudOutline = Box(Rounded(Color.clear, Accent, 8), 8, new RectOffset(0, 0, 0, 0));
			HudSmall = TextStyle(11, Muted);
			HudSmall.alignment = TextAnchor.UpperCenter;
			HudTimer = TextStyle(28, Theme.Text, FontStyle.Bold);
			HudTimer.alignment = TextAnchor.MiddleCenter;
			HudTimer.wordWrap = false;
			HudBig = TextStyle(110, AccentText, FontStyle.Bold);
			HudBig.alignment = TextAnchor.MiddleCenter;
			HudBig.wordWrap = false;
			// Stage 5: numbers in the sign font (all digits one width, so a running time doesn't jiggle), words in the text font.
			HudTimer = Strong(HudTimer);
			HudBig = Strong(HudBig);
			MakeRedesignStyles();
			MarkerStart = Badge(Good, GoodText);
			MarkerFinish = Badge(AccentSoft, AccentText);

			AccentLine = Solid(Accent);
			Divider = Solid(Border);
			StartLine = Solid(GoodText);
			FinishLine = Solid(Accent);
			ProgressBack = Solid(Control);
			GoodFill = Solid(GoodText);
			BadFill = Solid(BadText);

			Skin = Object.Instantiate(GUI.skin);
			Skin.hideFlags = HideFlags.HideAndDontSave;
			Skin.verticalScrollbar = new GUIStyle(ScrollBar) { name = "verticalscrollbar" };
			Skin.verticalScrollbarThumb = new GUIStyle(ScrollThumb) { name = "verticalscrollbarthumb" };
			Skin.verticalScrollbarUpButton = new GUIStyle { name = "verticalscrollbarupbutton", fixedHeight = 0 };
			Skin.verticalScrollbarDownButton = new GUIStyle { name = "verticalscrollbardownbutton", fixedHeight = 0 };
			Skin.horizontalScrollbar = new GUIStyle { name = "horizontalscrollbar", fixedHeight = 0 };
			Skin.scrollView = new GUIStyle { name = "scrollview" };
			if (TextFont != null) {
				Skin.font = TextFont;
			}
			ready_ = true;
		}

		/// <summary>A font size with the player's text size on it.</summary>
		private static int Size(int size) => Mathf.Max(8, Mathf.RoundToInt(size * TextScale));

		private static GUIStyle TextStyle(int size, Color color, FontStyle style = FontStyle.Normal) =>
			new GUIStyle {
				font = TextFont,
				fontSize = Size(size),
				fontStyle = style,
				wordWrap = true,
				richText = true,
				normal = { textColor = color },
				margin = new RectOffset(0, 0, 2, 2),
			};

		private static GUIStyle Box(Texture2D background, int border, RectOffset padding) =>
			new GUIStyle {
				normal = { background = background },
				border = new RectOffset(border, border, border, border),
				padding = padding,
			};

		/// <summary>The sign font on a style (headings, labels), regular weight.</summary>
		private static GUIStyle SignStyle(GUIStyle style) {
			if (Sign != null) {
				style.font = Sign;
			}
			return style;
		}

		/// <summary>The sign font's semibold on a style: its own weight, so "bold" isn't smeared on top (risk 5).</summary>
		private static GUIStyle Strong(GUIStyle style) {
			if (SignStrong != null) {
				style.font = SignStrong;
				style.fontStyle = StrongWeight;
			}
			return style;
		}

		private static GUIStyle Clickable(Texture2D normal, Texture2D hover, int fontSize, Color textColor, RectOffset padding) {
			int border = normal.width / 2 - 1;
			return new GUIStyle {
				font = TextFont,
				fontSize = Size(fontSize),
				alignment = TextAnchor.MiddleCenter,
				normal = { background = normal, textColor = textColor },
				hover = { background = hover, textColor = textColor },
				active = { background = hover, textColor = textColor },
				border = new RectOffset(border, border, border, border),
				padding = padding,
				margin = new RectOffset(0, 6, 3, 3),
			};
		}

		private static GUIStyle Pill(Color fill, Color text) {
			GUIStyle style = Box(Rounded(fill, fill, 6), 6, new RectOffset(10, 10, 8, 8));
			style.font = TextFont;
			style.fontSize = Size(13);
			style.wordWrap = true;
			style.richText = true;
			style.normal.textColor = text;
			style.margin = new RectOffset(0, 0, 4, 8);
			return style;
		}

		/// <summary>A one-line pill for the on-screen TRC spec badge, bordered so it stands out over the track.</summary>
		private static GUIStyle Badge(Color fill, Color text) {
			GUIStyle style = Strong(Box(Rounded(fill, text, 6), 6, new RectOffset(12, 12, 6, 7)));
			style.fontSize = Size(13);
			style.fontStyle = SignStrong != null ? StrongWeight : FontStyle.Bold;
			style.wordWrap = false;
			style.normal.textColor = text;
			return style;
		}

		private static Texture2D Solid(Color color) {
			var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
			texture.SetPixel(0, 0, color);
			texture.Apply();
			made_.Add(texture);
			return texture;
		}

		/// <summary>A rounded rectangle with a 1px border, for 9-slice stretching.</summary>
		internal static Texture2D Rounded(Color fill, Color border, int radius) {
			int size = radius * 2 + 2;
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
			};

			var half = new Vector2(size / 2f, size / 2f);
			var inner = half - new Vector2(radius, radius);
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - half;
					Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - inner;
					float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
					float depth = -outside;

					Color color = depth < 1.5f ? Color.Lerp(border, fill, Mathf.Clamp01(depth - 0.5f)) : fill;
					color.a *= Mathf.Clamp01(depth + 0.5f);
					texture.SetPixel(x, y, color);
				}
			}

			texture.Apply();
			made_.Add(texture);
			return texture;
		}

		private static Color Hex(int rgb) =>
			new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
	}
}
