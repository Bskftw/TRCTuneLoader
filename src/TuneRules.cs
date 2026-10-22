using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TRC.TuneLoader {
	/// <summary>
	/// Small text rules shared by .trctune and .knd reading. tools/tune_code.py follows exactly
	/// the same rules, so a file always gives the same tune code in the mod and in the bot.
	/// Only ASCII is trimmed and lowercased on purpose: C# and Python treat some other
	/// characters differently.
	/// </summary>
	public static class TuneRules {
		/// <summary>
		/// Values must be smaller than this (either sign). Bigger numbers could round or print
		/// differently in C# and Python, which would break the tune code. Same limit in tools/tune_code.py.
		/// </summary>
		public const float MaxValue = 1000000000f;

		private static readonly char[] AsciiWhitespace = { ' ', '\t', '\r', '\n', '\f', '\v' };
		private static readonly Regex IdPattern = new Regex("^[0-9]{1,9}$", RegexOptions.CultureInvariant);

		public static string Trim(string text) => (text ?? "").Trim(AsciiWhitespace);

		public static string Lower(string text) {
			var result = new StringBuilder(text ?? "");
			for (int i = 0; i < result.Length; i++) {
				char c = result[i];
				if (c >= 'A' && c <= 'Z') {
					result[i] = (char)(c + ('a' - 'A'));
				}
			}
			return result.ToString();
		}

		/// <summary>How a setting or field name is compared: trimmed and lowercased.</summary>
		public static string Key(string text) => Lower(Trim(text));

		/// <summary>A car or setup ID as plain digits without leading zeros, or "" when it isn't one.</summary>
		public static string Id(string text) {
			string trimmed = Trim(text);
			return IdPattern.IsMatch(trimmed)
				? int.Parse(trimmed, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
				: "";
		}

		public static bool IsUsableValue(float value) =>
			!float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < MaxValue;

		/// <summary>
		/// Parses a tune value: optional sign, digits with an optional dot, optional exponent.
		/// No "nan", "inf" or "1_000", and nothing as big as <see cref="MaxValue"/>.
		/// </summary>
		public static bool TryParseValue(string text, out float value) =>
			float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && IsUsableValue(value);
	}
}
