using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// A keyboard shortcut: Ctrl, Shift and Alt (either side) plus one key, like Ctrl + T. Saved in KSL's
	/// prefs as text ("Ctrl+T"). It's matched against Unity's IMGUI key events, which arrive whichever
	/// input system CarX uses.
	/// </summary>
	internal sealed class Shortcut {
		public static readonly Shortcut Default = new Shortcut(true, false, false, KeyCode.T);

		/// <summary>The time attack restart key: Ctrl + R.</summary>
		public static readonly Shortcut RestartDefault = new Shortcut(true, false, false, KeyCode.R);

		private static readonly KeyCode[] NotAllowed = {
			KeyCode.None, KeyCode.Escape,
			KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift,
			KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr,
			KeyCode.LeftCommand, KeyCode.RightCommand, KeyCode.LeftWindows, KeyCode.RightWindows,
		};

		public bool Ctrl { get; }
		public bool Shift { get; }
		public bool Alt { get; }
		public KeyCode Key { get; }

		public Shortcut(bool ctrl, bool shift, bool alt, KeyCode key) {
			Ctrl = ctrl;
			Shift = shift;
			Alt = alt;
			Key = key;
		}

		/// <summary>Keys that can end a shortcut: not a modifier, a mouse button or Escape (which cancels picking one).</summary>
		public static bool IsUsableKey(KeyCode key) => key < KeyCode.Mouse0 && !NotAllowed.Contains(key);

		/// <summary>The shortcut a key press makes, or null when that key can't be one.</summary>
		public static Shortcut FromEvent(Event e) =>
			e != null && IsUsableKey(e.keyCode) ? new Shortcut(e.control, e.shift, e.alt, e.keyCode) : null;

		/// <summary>A saved shortcut ("Ctrl+Shift+F8"), or <paramref name="fallback"/> (<see cref="Default"/>) when the text isn't one.</summary>
		public static Shortcut Parse(string text, Shortcut fallback = null) {
			Shortcut parsed = ParseOrNull(text);
			return parsed ?? fallback ?? Default;
		}

		private static Shortcut ParseOrNull(string text) {
			if (string.IsNullOrWhiteSpace(text)) {
				return null;
			}

			bool ctrl = false, shift = false, alt = false;
			KeyCode key = KeyCode.None;
			foreach (string part in text.Split('+').Select(p => p.Trim())) {
				switch (part.ToLowerInvariant()) {
					case "ctrl": ctrl = true; break;
					case "shift": shift = true; break;
					case "alt": alt = true; break;
					default:
						if (key != KeyCode.None || !Enum.TryParse(part, true, out key) || !IsUsableKey(key)) {
							return null;
						}
						break;
				}
			}
			return key == KeyCode.None ? null : new Shortcut(ctrl, shift, alt, key);
		}

		/// <summary>True for the key press of this shortcut with exactly its modifiers held.</summary>
		public bool Matches(Event e) => e.keyCode == Key && e.control == Ctrl && e.shift == Shift && e.alt == Alt;

		/// <summary>The text saved in KSL's prefs, like "Ctrl+T".</summary>
		public string Save() => string.Join("+", Modifiers().Concat(new[] { Key.ToString() }));

		/// <summary>The text shown to players, like "Ctrl + T".</summary>
		public override string ToString() => string.Join(" + ", Modifiers().Concat(new[] { KeyName(Key) }));

		public bool SameAs(Shortcut other) =>
			other != null && other.Ctrl == Ctrl && other.Shift == Shift && other.Alt == Alt && other.Key == Key;

		private IEnumerable<string> Modifiers() {
			if (Ctrl) {
				yield return "Ctrl";
			}
			if (Shift) {
				yield return "Shift";
			}
			if (Alt) {
				yield return "Alt";
			}
		}

		private static string KeyName(KeyCode key) {
			if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) {
				return ((int)(key - KeyCode.Alpha0)).ToString();
			}
			if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) {
				return $"Numpad {(int)(key - KeyCode.Keypad0)}";
			}
			switch (key) {
				case KeyCode.Return: return "Enter";
				case KeyCode.BackQuote: return "`";
				case KeyCode.Minus: return "-";
				case KeyCode.Equals: return "=";
				case KeyCode.LeftBracket: return "[";
				case KeyCode.RightBracket: return "]";
				case KeyCode.Semicolon: return ";";
				case KeyCode.Quote: return "'";
				case KeyCode.Comma: return ",";
				case KeyCode.Period: return ".";
				case KeyCode.Slash: return "/";
				case KeyCode.Backslash: return "\\";
				default: return key.ToString();
			}
		}
	}
}
