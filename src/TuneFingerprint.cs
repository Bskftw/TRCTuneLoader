using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// Turns a car, its setup slot and its tune values into a short code like "TRC-1A2B-3C4D".
	///
	/// The same tune always gives the same code. Change any value by more than
	/// the rounding step and the code changes, so the bot can compare a driver's
	/// code against the official one for that tune.
	///
	/// Keep this exactly in sync with the Discord bot's version (tools/tune_code.py):
	/// if the rounding or text layout changes here, every published code changes too.
	/// </summary>
	public static class TuneFingerprint {
		/// <summary>Values are rounded to this many decimals before hashing, to ignore float noise.</summary>
		public const int Decimals = 3;

		/// <summary>Sound-only settings: drivers can pick these freely, so they don't count toward the code.</summary>
		public static readonly HashSet<string> IgnoredKeys = new HashSet<string>(StringComparer.Ordinal) {
			"seq_transmision_sound",
			"wastegate_sound",
		};

		public static bool Counts(string key) => !IgnoredKeys.Contains(key);

		/// <param name="carId">CarX car ID as plain digits, or "" when unknown.</param>
		/// <param name="slotId">CarX setup slot ID as plain digits, or "" when unknown.</param>
		public static string Compute(string carId, string slotId, IEnumerable<KeyValuePair<string, float>> values) {
			var text = new StringBuilder();
			text.Append("car=").Append(carId ?? "").Append('\n');
			text.Append("slot=").Append(slotId ?? "").Append('\n');

			foreach (var pair in values.Where(p => Counts(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal)) {
				text.Append(pair.Key).Append('=').Append(FormatValue(pair.Value)).Append('\n');
			}

			byte[] hash;
			using (var sha = SHA256.Create()) {
				hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
			}

			string hex = BitConverter.ToString(hash, 0, 4).Replace("-", "");
			return $"TRC-{hex.Substring(0, 4)}-{hex.Substring(4, 4)}";
		}

		public static string Compute(int carId, int slotId, IEnumerable<KeyValuePair<string, float>> values) =>
			Compute(carId.ToString(CultureInfo.InvariantCulture), slotId.ToString(CultureInfo.InvariantCulture), values);

		public static string FormatValue(float value) {
			double rounded = Math.Round((double)value, Decimals, MidpointRounding.AwayFromZero);
			if (rounded == 0) {
				rounded = 0; // turn -0 into 0 so both print the same
			}

			return rounded.ToString("F" + Decimals, CultureInfo.InvariantCulture);
		}
	}
}
