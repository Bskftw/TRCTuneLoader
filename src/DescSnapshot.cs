using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace TRC.TuneLoader {
	/// <summary>
	/// A flat copy of every value in a car's live physics description (CarX's CarDesc plus wheel base),
	/// keyed by field path like "carXDesc.aero.frontDownforce".
	///
	/// The mod records one right after CarX itself puts the official tune on the car in the garage,
	/// then compares the live car with it. A value changed any other way (for example with Unity
	/// Explorer) shows up as a difference. It reads fields by reflection, so it covers everything
	/// CarX has: suspension, tyres, engine, gearbox, diff, brakes, weight and aero (downforce).
	/// </summary>
	public sealed class DescSnapshot {
		private const int MaxDepth = 8;
		private const string DescPrefix = "carXDesc.";

		/// <summary>Fields that change for reasons other than the tune: the player's gearbox mode and clutch input settings.</summary>
		public static readonly string[] Ignored = {
			"carXDesc.transmission.gearShiftType",
			"carXDesc.transmission.clutchMapCurveSamples",
		};

		public Dictionary<string, object> Values { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

		/// <summary>The official tune code this snapshot was recorded for.</summary>
		public string TuneCode { get; set; } = "";

		/// <param name="desc">CarX's BaseCar.Desc (or any object made of public fields).</param>
		/// <param name="extra">Other values to include, like the wheel offsets the car keeps outside its Desc.</param>
		public static DescSnapshot Of(object desc, IDictionary<string, object> extra = null) {
			var snapshot = new DescSnapshot();
			snapshot.Add("", desc, 0);
			if (extra != null) {
				foreach (var pair in extra) {
					snapshot.Values[pair.Key] = pair.Value;
				}
			}
			return snapshot;
		}

		/// <summary>Values in this (live) snapshot that differ from the reference, as readable lines.</summary>
		/// <param name="alsoIgnored">More field path prefixes to skip for this car, like a diff its drivetrain doesn't use.</param>
		public List<string> Differences(DescSnapshot reference, ICollection<string> alsoIgnored = null) {
			var differences = new List<string>();
			var skip = alsoIgnored == null ? Ignored : Ignored.Concat(alsoIgnored).ToArray();
			var paths = Values.Keys.Union(reference.Values.Keys)
				.Where(p => !skip.Any(i => p.StartsWith(i, StringComparison.Ordinal)))
				.OrderBy(p => p, StringComparer.Ordinal);

			foreach (string path in paths) {
				bool hasLive = Values.TryGetValue(path, out object live);
				bool hasExpected = reference.Values.TryGetValue(path, out object expected);
				if (hasLive && hasExpected && Same(live, expected)) {
					continue;
				}

				string name = path.StartsWith(DescPrefix, StringComparison.Ordinal) ? path.Substring(DescPrefix.Length) : path;
				differences.Add($"Live {name}: {Show(hasLive, live)} (expected {Show(hasExpected, expected)})");
			}

			return differences;
		}

		private void Add(string path, object value, int depth) {
			if (value == null) {
				Values[path] = null;
				return;
			}

			Type type = value.GetType();
			if (type.IsPrimitive || type.IsEnum || value is string || value is decimal) {
				Values[path] = value;
				return;
			}

			if (value is Array array) {
				Values[path + ".Length"] = array.Length;
				for (int i = 0; i < array.Length; i++) {
					Add($"{path}[{i}]", array.GetValue(i), depth + 1);
				}
				return;
			}

			if (depth >= MaxDepth) {
				return;
			}

			FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
			if (fields.Length == 0) {
				// A struct like Unity's Bounds keeps its data private; its text form is enough to compare.
				Values[path] = Convert.ToString(value, CultureInfo.InvariantCulture);
				return;
			}

			foreach (FieldInfo field in fields) {
				Add(path.Length == 0 ? field.Name : $"{path}.{field.Name}", field.GetValue(value), depth + 1);
			}
		}

		private static bool Same(object a, object b) {
			if (a == null || b == null) {
				return a == null && b == null;
			}
			if (IsNumber(a) && IsNumber(b)) {
				double x = Convert.ToDouble(a, CultureInfo.InvariantCulture);
				double y = Convert.ToDouble(b, CultureInfo.InvariantCulture);
				return Math.Abs(x - y) <= 1e-4 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
			}
			return a.Equals(b);
		}

		private static bool IsNumber(object value) => value is float || value is double || value is int || value is long;

		private static string Show(bool present, object value) {
			if (!present) {
				return "missing";
			}
			if (value is float || value is double) {
				return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.####", CultureInfo.InvariantCulture);
			}
			return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "none";
		}
	}
}
