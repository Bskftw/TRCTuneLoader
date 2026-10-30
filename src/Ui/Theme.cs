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
	internal static class Theme {
		public static readonly Color DefaultBackground = Hex(0x16161A);
		public static readonly Color DefaultAccent = Hex(0xD4537E);
		public static readonly Color DefaultText = Hex(0xEDEDED);

		public static Color Background = DefaultBackground;
		public static Color Panel = Hex(0x1D1D22);
		public static Color Border = Hex(0x2E2E35);
		public static Color Control = Hex(0x3A3A42);
		public static Color Hover = Hex(0x26262C);
		public static Color Text = DefaultText;
		public static Color Muted = Hex(0x9A9AA3);
		public static Color Accent = DefaultAccent;
		public static Color AccentHover = Hex(0xE0668F);
		public static Color AccentSoft = Hex(0x4B1528);
		public static Color AccentText = Hex(0xF4C0D1);

		/// <summary>Text size, on top of the window size: 1 is the normal size.</summary>
		public static float TextScale { get; private set; } = 1f;

		/// <summary>The textures the styles use, so a rebuild can let the old ones go.</summary>
		private static readonly System.Collections.Generic.List<Texture2D> made_ = new System.Collections.Generic.List<Texture2D>();

		/// <summary>
		/// The player's colours and text size. Panels, borders and buttons are shades between the background and
		/// the text (lighter on a dark background, darker on a light one); the accent gives the pink parts. The
		/// default colours give exactly the TRC look.
		/// </summary>
		public static void Configure(Color background, Color accent, Color text, float textScale) {
			TextScale = Mathf.Clamp(textScale, 0.8f, 1.6f);
			bool standard = Same(background, DefaultBackground) && Same(accent, DefaultAccent) && Same(text, DefaultText);
			if (standard) {
				Background = DefaultBackground;
				Panel = Hex(0x1D1D22);
				Border = Hex(0x2E2E35);
				Control = Hex(0x3A3A42);
				Hover = Hex(0x26262C);
				Text = DefaultText;
				Muted = Hex(0x9A9AA3);
				Accent = DefaultAccent;
				AccentHover = Hex(0xE0668F);
				AccentSoft = Hex(0x4B1528);
				AccentText = Hex(0xF4C0D1);
			} else {
				background.a = 1f;
				accent.a = 1f;
				text.a = 1f;
				Color toward = Luminance(background) > 0.5f ? Color.black : Color.white;
				Background = background;
				Panel = Color.Lerp(background, toward, 0.04f);
				Hover = Color.Lerp(background, toward, 0.07f);
				Border = Color.Lerp(background, toward, 0.12f);
				Control = Color.Lerp(background, toward, 0.17f);
				Text = text;
				Muted = Color.Lerp(text, background, 0.38f);
				Accent = accent;
				AccentHover = Color.Lerp(accent, Color.white, 0.12f);
				AccentSoft = Color.Lerp(background, accent, 0.28f);
				AccentText = Color.Lerp(accent, toward == Color.white ? Color.white : Color.black, 0.6f);
			}
			ready_ = false;
		}

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

		public const string MutedHex = "#9A9AA3";
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
		public static GUIStyle Chip, ChipActive, Button, ButtonPrimary;
		public static GUIStyle Row, RowSelected, Card, Guide;
		public static GUIStyle PillGood, PillBad, PillWarn, PillNeutral;
		public static GUIStyle BadgeGood, BadgeBad, BadgeWarn, Popup;
		public static GUIStyle ScrollBar, ScrollThumb;
		public static GUIStyle Field;
		public static GUIStyle HudPanel, HudPill, HudOutline, HudSmall, HudTimer, HudBig, MarkerStart, MarkerFinish;
		public static Texture2D AccentLine, Divider, StartLine, FinishLine, ProgressBack;

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

			Window = Box(Rounded(Background, Border, 10), 10, new RectOffset(0, 0, 0, 0));

			Title = TextStyle(16, Theme.Text, FontStyle.Bold);
			Subtitle = TextStyle(11, Muted);
			Heading = TextStyle(16, Theme.Text, FontStyle.Bold);
			Body = TextStyle(13, Theme.Text);
			Small = TextStyle(12, Muted);
			SectionLabel = TextStyle(12, Muted);
			Group = TextStyle(12, AccentText, FontStyle.Bold);
			Group.margin = new RectOffset(0, 0, 10, 2);

			Tab = TextStyle(13, Muted);
			Tab.wordWrap = false;
			Tab.padding = new RectOffset(10, 10, 9, 9);
			Tab.hover.textColor = Theme.Text;
			TabActive = new GUIStyle(Tab) { normal = { textColor = AccentText }, hover = { textColor = AccentText } };

			Close = TextStyle(18, Muted);
			Close.alignment = TextAnchor.MiddleCenter;
			Close.hover.textColor = Theme.Text;
			Close.fixedWidth = 28;
			Close.fixedHeight = 28;

			Chip = Clickable(Rounded(Color.clear, Control, 12), Rounded(Hover, Control, 12), 12, new Color(0.78f, 0.78f, 0.81f), new RectOffset(12, 12, 4, 5));
			ChipActive = Clickable(Rounded(AccentSoft, Accent, 12), Rounded(AccentSoft, AccentHover, 12), 12, AccentText, new RectOffset(12, 12, 4, 5));

			Button = Clickable(Rounded(Color.clear, Control, 6), Rounded(Hover, Control, 6), 13, Theme.Text, new RectOffset(14, 14, 8, 8));
			ButtonPrimary = Clickable(Rounded(Accent, Accent, 6), Rounded(AccentHover, AccentHover, 6), 13, Color.white, new RectOffset(14, 14, 8, 8));
			ButtonPrimary.fontStyle = FontStyle.Bold;

			Row = Clickable(Rounded(Color.clear, Color.clear, 6), Rounded(Hover, Color.clear, 6), 13, Theme.Text, new RectOffset(10, 10, 6, 6));
			Row.alignment = TextAnchor.MiddleLeft;
			Row.richText = true;
			Row.wordWrap = true;
			RowSelected = new GUIStyle(Row) {
				normal = { background = Rounded(AccentSoft, Accent, 6), textColor = Theme.Text },
				hover = { background = Rounded(AccentSoft, AccentHover, 6), textColor = Theme.Text },
			};

			Card = Box(Rounded(Panel, Border, 8), 8, new RectOffset(10, 10, 8, 8));
			Guide = Box(Rounded(AccentSoft, Accent, 8), 8, new RectOffset(14, 14, 10, 10));
			Guide.margin = new RectOffset(0, 0, 0, 10);

			PillGood = Pill(Good, GoodText);
			PillBad = Pill(Bad, BadText);
			PillWarn = Pill(Warn, WarnText);
			PillNeutral = Pill(Neutral, Muted);

			BadgeGood = Badge(Good, GoodText);
			BadgeBad = Badge(Bad, BadText);
			BadgeWarn = Badge(Warn, WarnText);
			Popup = Box(Rounded(Bad, BadText, 8), 8, new RectOffset(16, 16, 12, 12));
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
			Field.fontSize = Size(13);
			Field.normal.textColor = Theme.Text;
			Field.hover.background = Rounded(Panel, Muted, 6);
			Field.hover.textColor = Theme.Text;
			Field.focused.background = Rounded(Panel, Accent, 6);
			Field.focused.textColor = Theme.Text;
			Field.clipping = TextClipping.Clip;
			Field.margin = new RectOffset(0, 6, 3, 3);

			// Over the track while time attack or the course tool runs.
			Texture2D hudBackground = Rounded(new Color(Background.r, Background.g, Background.b, 0.78f), new Color(Border.r, Border.g, Border.b, 0.78f), 8);
			HudPanel = Box(hudBackground, 8, new RectOffset(0, 0, 0, 0));
			HudPill = Box(hudBackground, 8, new RectOffset(14, 14, 8, 9));
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
			MarkerStart = Badge(Good, GoodText);
			MarkerFinish = Badge(AccentSoft, AccentText);

			AccentLine = Solid(Accent);
			Divider = Solid(Border);
			StartLine = Solid(GoodText);
			FinishLine = Solid(Accent);
			ProgressBack = Solid(Control);

			Skin = Object.Instantiate(GUI.skin);
			Skin.hideFlags = HideFlags.HideAndDontSave;
			Skin.verticalScrollbar = new GUIStyle(ScrollBar) { name = "verticalscrollbar" };
			Skin.verticalScrollbarThumb = new GUIStyle(ScrollThumb) { name = "verticalscrollbarthumb" };
			Skin.verticalScrollbarUpButton = new GUIStyle { name = "verticalscrollbarupbutton", fixedHeight = 0 };
			Skin.verticalScrollbarDownButton = new GUIStyle { name = "verticalscrollbardownbutton", fixedHeight = 0 };
			Skin.horizontalScrollbar = new GUIStyle { name = "horizontalscrollbar", fixedHeight = 0 };
			Skin.scrollView = new GUIStyle { name = "scrollview" };
			ready_ = true;
		}

		/// <summary>A font size with the player's text size on it.</summary>
		private static int Size(int size) => Mathf.Max(8, Mathf.RoundToInt(size * TextScale));

		private static GUIStyle TextStyle(int size, Color color, FontStyle style = FontStyle.Normal) =>
			new GUIStyle {
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

		private static GUIStyle Clickable(Texture2D normal, Texture2D hover, int fontSize, Color textColor, RectOffset padding) {
			int border = normal.width / 2 - 1;
			return new GUIStyle {
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
			style.fontSize = Size(13);
			style.wordWrap = true;
			style.richText = true;
			style.normal.textColor = text;
			style.margin = new RectOffset(0, 0, 4, 8);
			return style;
		}

		/// <summary>A one-line pill for the on-screen TRC spec badge, bordered so it stands out over the track.</summary>
		private static GUIStyle Badge(Color fill, Color text) {
			GUIStyle style = Box(Rounded(fill, text, 6), 6, new RectOffset(12, 12, 6, 7));
			style.fontSize = Size(13);
			style.fontStyle = FontStyle.Bold;
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
		private static Texture2D Rounded(Color fill, Color border, int radius) {
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
