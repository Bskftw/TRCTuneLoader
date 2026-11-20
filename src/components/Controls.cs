// The redesign's controls (docs/archive/ui-redesign-plan.md, Stage 1): page header, sections, setting rows, switch, choice, slider, stat, tile, empty state.
using System;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Every control here is pressed through <see cref="NavButton(string, GUIStyle, GUILayoutOption[])"/>, so the pad
	/// reaches it like any button (plan risk 2). A control only draws and returns the new value: saving it stays with
	/// the page, the same way as the button it replaces (risk 11).
	/// </summary>
	public partial class TRCTuneLoaderMod {
		/// <summary>A page's title, one line on what it's for, and its main button on the right (true when pressed).</summary>
		private static bool PageHeader(string title, string lead, string mainButton = null) {
			bool pressed = false;
			GUILayout.BeginHorizontal();
			GUILayout.BeginVertical();
			GUILayout.Label(title, Theme.PageTitle);
			if (!string.IsNullOrEmpty(lead)) {
				GUILayout.Label(lead, Theme.PageLead);
			}
			GUILayout.EndVertical();
			GUILayout.FlexibleSpace();
			if (!string.IsNullOrEmpty(mainButton)) {
				GUILayout.BeginVertical();
				GUILayout.FlexibleSpace();
				pressed = NavButton(mainButton, Theme.ButtonPrimary);
				GUILayout.EndVertical();
			}
			GUILayout.EndHorizontal();
			Hairline(10f);
			return pressed;
		}

		/// <summary>A section: a panel with its title and at most one short muted line. Close it with <see cref="EndSection"/>.</summary>
		private static void BeginSection(string title, string note = null, params GUILayoutOption[] options) {
			GUILayout.BeginVertical(Theme.Section, options);
			if (!string.IsNullOrEmpty(title)) {
				GUILayout.Label(title, Theme.SectionTitle);
			}
			if (!string.IsNullOrEmpty(note)) {
				GUILayout.Label(note, Theme.SectionNote);
			}
		}

		private static void EndSection() => GUILayout.EndVertical();

		/// <summary>A 1 px line across, with space under it.</summary>
		private static void Hairline(float space = 0f) {
			Rect line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
			if (Event.current.type == EventType.Repaint) {
				GUI.DrawTexture(line, Theme.Divider);
			}
			if (space > 0f) {
				GUILayout.Space(space);
			}
		}

		/// <summary>
		/// A setting: its name and one muted line on the left, the control on the right. <paramref name="wide"/> puts the
		/// control under the name (three or more choices, or a slider: bskftw found the squeezed one looked bad).
		/// </summary>
		private static void SettingRow(string label, string note, Action control, bool wide = false, bool first = false) {
			if (!first) {
				Hairline(2f);
			}
			GUILayout.Space(4f);
			if (wide) {
				GUILayout.Label(label, Theme.SettingLabel);
				if (!string.IsNullOrEmpty(note)) {
					GUILayout.Label(note, Theme.SettingNote);
				}
				GUILayout.Space(4f);
				GUILayout.BeginHorizontal();
				control();
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			} else {
				GUILayout.BeginHorizontal();
				GUILayout.BeginVertical(GUILayout.MinWidth(150f));
				GUILayout.Label(label, Theme.SettingLabel);
				if (!string.IsNullOrEmpty(note)) {
					GUILayout.Label(note, Theme.SettingNote);
				}
				GUILayout.EndVertical();
				GUILayout.FlexibleSpace();
				GUILayout.BeginVertical();
				GUILayout.FlexibleSpace();
				control();
				GUILayout.FlexibleSpace();
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();
			}
			GUILayout.Space(4f);
		}

		/// <summary>An on/off switch: returns the new state (flipped when pressed).</summary>
		private static bool Switch(bool on) {
			bool pressed = NavButton(GUIContent.none, on ? Theme.SwitchOn : Theme.SwitchOff);
			if (Event.current.type == EventType.Repaint) {
				Rect track = GUILayoutUtility.GetLastRect();
				const float knob = 16f;
				var spot = new Rect(on ? track.xMax - knob - 3f : track.x + 3f, track.y + 3f, knob, knob);
				GUI.DrawTexture(spot, on ? Theme.Knob : Theme.KnobOff);
			}
			return pressed ? !on : on;
		}

		/// <summary>One of 2 to 4 choices in a strip: returns the picked index.</summary>
		private static int Segmented(string[] options, int picked) {
			int result = picked;
			GUILayout.BeginHorizontal(Theme.SegBack, GUILayout.ExpandWidth(false));
			for (int i = 0; i < options.Length; i++) {
				bool on = i == picked;
				if (NavButton(options[i], on ? Theme.SegOn : Theme.SegOff)) {
					result = i;
				}
				if (on && Event.current.type == EventType.Repaint) {
					Rect r = GUILayoutUtility.GetLastRect();
					GUI.DrawTexture(new Rect(r.x + 4f, r.yMax - 2f, r.width - 8f, 2f), Theme.SegLine);
				}
			}
			GUILayout.EndHorizontal();
			return result;
		}

		/// <summary>
		/// A slider with its value shown, and − / + buttons so the pad can change it too (risk 2). Returns the new value,
		/// kept between <paramref name="min"/> and <paramref name="max"/> and on <paramref name="step"/>.
		/// </summary>
		private static float Slider(float value, float min, float max, float step, string shown, float width = 160f) {
			GUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
			if (NavButton("−", Theme.ButtonQuiet)) {
				value -= step;
			}
			Rect bar = GUILayoutUtility.GetRect(width, 22f, GUILayout.Width(width));
			Event e = Event.current;
			if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && bar.Contains(e.mousePosition)) {
				value = Mathf.Lerp(min, max, Mathf.InverseLerp(bar.x + 7f, bar.xMax - 7f, e.mousePosition.x));
				e.Use();
			}
			float t = Mathf.InverseLerp(min, max, value);
			if (e.type == EventType.Repaint) {
				var track = new Rect(bar.x + 7f, bar.center.y - 2f, bar.width - 14f, 4f);
				GUI.DrawTexture(track, Theme.ProgressBack);
				GUI.DrawTexture(new Rect(track.x, track.y, track.width * t, track.height), Theme.SegLine);
				GUI.DrawTexture(new Rect(track.x + track.width * t - 7f, bar.center.y - 7f, 14f, 14f), Theme.Knob);
			}
			if (NavButton("+", Theme.ButtonQuiet)) {
				value += step;
			}
			GUILayout.Label(shown, Theme.SettingLabel, GUILayout.ExpandWidth(false));
			GUILayout.EndHorizontal();
			value = step > 0f ? Mathf.Round(value / step) * step : value;
			return Mathf.Clamp(value, min, max);
		}

		/// <summary>A big number with a small muted label under it.</summary>
		private static void Stat(string value, string label, params GUILayoutOption[] options) {
			GUILayout.BeginVertical(Theme.StatBox, options);
			GUILayout.Label(value, Theme.StatValue);
			GUILayout.Label(label, Theme.StatLabel);
			GUILayout.EndVertical();
		}

		/// <summary>
		/// A Home tile: its colour as a gradient wash, a title, a live line in its colour, and its pages as small links along
		/// the bottom. Returns the index of the link pressed, or -1.
		/// </summary>
		private static int Tile(string title, string state, Color colour, string[] links, float height = 150f, params GUILayoutOption[] options) {
			int pressed = -1;
			var all = new GUILayoutOption[options.Length + 1];
			options.CopyTo(all, 0);
			all[options.Length] = GUILayout.MinHeight(height);
			GUILayout.BeginVertical(Theme.Tile(colour), all);
			GUILayout.Label(title, Theme.TileTitle);
			if (!string.IsNullOrEmpty(state)) {
				GUILayout.Label($"<color={Theme.ToHex(Color.Lerp(colour, Color.white, 0.25f))}>{state}</color>", Theme.TileState);
			}
			GUILayout.FlexibleSpace();
			GUILayout.BeginHorizontal();
			for (int i = 0; i < links.Length; i++) {
				if (NavButton(links[i], Theme.TileLink)) {
					pressed = i;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			return pressed;
		}

		/// <summary>An empty spot: one line on what to do, and the button that does it (true when pressed).</summary>
		private static bool EmptyState(string text, string button = null) {
			GUILayout.Label(text, Theme.SectionNote);
			return !string.IsNullOrEmpty(button) && NavButton(button, Theme.Button, GUILayout.ExpandWidth(false));
		}
	}
}
