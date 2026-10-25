using System;
using System.Collections.Generic;
using System.Linq;

namespace TRC.TuneLoader {
	/// <summary>
	/// Compares the tune on a car with an official TRC tune.
	///
	/// A setting the official tune doesn't list (for example one CarX added after the tune was made)
	/// should be at the car's default, which is what CarX itself uses for a missing setting.
	/// The resulting code covers the official tune's settings plus any other setting that isn't
	/// at its default, so it equals the official code only when the car really matches.
	/// </summary>
	public static class TuneCheck {
		private static readonly System.Text.RegularExpressions.Regex FirstNumber = new System.Text.RegularExpressions.Regex(@"\s*[(\[]?[+-]?\d");

		/// <summary>
		/// A difference line without its numbers: "Front spring stiffness: 120 N/mm (official 100)" becomes "Front spring
		/// stiffness: changed", "Stance: ride height at the front left wheel +12 mm (...)" becomes "Stance: ride height at
		/// the front left wheel changed". Lines with no numbers (like "not on this car") stay as they are.
		/// </summary>
		public static string WithoutValues(string line) {
			int colon = line.IndexOf(": ", StringComparison.Ordinal);
			if (colon < 0 || !line.Skip(colon).Any(char.IsDigit)) {
				return line;
			}
			string label = line.Substring(0, colon);
			string rest = line.Substring(colon + 2);
			if (label == "Stance" || label.StartsWith("Live ", StringComparison.Ordinal)) {
				System.Text.RegularExpressions.Match number = FirstNumber.Match(rest);
				string what = number.Success ? rest.Substring(0, number.Index).TrimEnd() : rest;
				return what.Length > 0 && !what.Any(char.IsDigit) ? $"{label}: {what} changed" : $"{label}: changed";
			}
			return $"{label}: changed";
		}


		public sealed class Result {
			public string Code = "";
			public bool Matches;
			public List<string> Differences = new List<string>();
		}

		/// <param name="official">The official tune's values.</param>
		/// <param name="current">The car's values, as CarX applies them.</param>
		/// <param name="defaults">The car's default value for each setting it has.</param>
		/// <param name="describe">Turns a setting's value into readable text, or null to show slider positions.</param>
		public static Result Compare(
			string carId, string slotId, string officialCode,
			IDictionary<string, float> official,
			IDictionary<string, float> current,
			IDictionary<string, float> defaults,
			Func<string, float, string> describe = null) {
			var result = new Result();
			var codeValues = new Dictionary<string, float>(StringComparer.Ordinal);
			describe = describe ?? ((key, value) => TuneFingerprint.FormatValue(value));

			var keys = official.Keys.Union(current.Keys).Where(TuneFingerprint.Counts);
			foreach (string key in TuneLabels.InDisplayOrder(keys)) {
				bool hasActual = TryGet(current, defaults, key, out float actual);
				bool isOfficialKey = official.TryGetValue(key, out float expected);

				if (!isOfficialKey && !defaults.TryGetValue(key, out expected)) {
					// No official value and no known default, so it can't be judged: report it and count it.
					if (hasActual) {
						codeValues[key] = actual;
						result.Differences.Add($"{TuneLabels.Label(key)}: {describe(key, actual)} (not in the official tune)");
					}
					continue;
				}

				if (!hasActual) {
					result.Differences.Add($"{TuneLabels.Label(key)}: not on this car");
					continue;
				}

				if (isOfficialKey || !Same(actual, expected)) {
					codeValues[key] = actual;
				}

				if (!Same(actual, expected)) {
					string actualText = describe(key, actual);
					string expectedText = describe(key, expected);
					if (actualText == expectedText) {
						// Close enough that CarX shows the same number: show the slider positions too.
						actualText += $" [{TuneFingerprint.FormatValue(actual)}]";
						expectedText += $" [{TuneFingerprint.FormatValue(expected)}]";
					}

					string officialText = isOfficialKey ? "official" : "should be the default";
					result.Differences.Add($"{TuneLabels.Label(key)}: {actualText} ({officialText} {expectedText})");
				}
			}

			result.Code = TuneFingerprint.Compute(carId, slotId, codeValues);
			result.Matches = result.Differences.Count == 0 &&
				string.Equals(result.Code, officialCode, StringComparison.OrdinalIgnoreCase);
			return result;
		}

		private static bool TryGet(IDictionary<string, float> current, IDictionary<string, float> defaults, string key, out float value) =>
			current.TryGetValue(key, out value) || defaults.TryGetValue(key, out value);

		private static bool Same(float a, float b) => TuneFingerprint.FormatValue(a) == TuneFingerprint.FormatValue(b);
	}
}
