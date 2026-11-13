// Controller focus: every menu button remembers where it is, so the D-pad can move between them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
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
	}
}
