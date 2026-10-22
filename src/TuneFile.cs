using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// One TRC spec tune, read from a .trctune text file.
	///
	/// File format (plain text, easy to edit and review in Discord):
	///
	///   # comments start with #
	///   [info]
	///   name      = AE86 TRC V2.1
	///   car       = toyota_sprinter_trueno_ae86
	///   car_id    = 3                (CarX car ID)
	///   slot      = Ultimate
	///   slot_id   = 119              (CarX setup slot ID for this car)
	///   engine_id = 0                (0 = stock engine)
	///   author    = TRC
	///   version   = 2.1
	///   source    = toyota_sprinter_trueno_ae86_V2.1.knd
	///   code      = TRC-1A2B-3C4D    (optional: the official code TRC publishes for this tune)
	///
	///   [values]
	///   front_camber = 0.851932   # what CarX's tuning screen shows, for reading only
	///   ...
	///
	/// Values are CarX slider positions (0 = far left, 1 = far right), exactly as CarX and
	/// KINO save them, under CarX's own setting names. See docs/carx-tuning-notes.md.
	/// </summary>
	public sealed class TuneFile {
		public const string Extension = ".trctune";

		public string FilePath { get; private set; } = "";
		public string Name { get; private set; } = "";
		public string Car { get; private set; } = "";
		public string Slot { get; private set; } = "";
		public string Author { get; private set; } = "";
		public string Version { get; private set; } = "";
		public string Notes { get; private set; } = "";
		public string Source { get; private set; } = "";

		/// <summary>CarX IDs as plain digits ("" when missing). They're part of the tune code.</summary>
		public string CarId { get; private set; } = "";
		public string SlotId { get; private set; } = "";
		public string EngineId { get; private set; } = "";

		/// <summary>The official code written in the file, if any. Compare it with <see cref="Fingerprint"/>.</summary>
		public string PublishedCode { get; private set; } = "";

		/// <summary>Tune values, sorted by name so the fingerprint is stable.</summary>
		public SortedDictionary<string, float> Values { get; } = new SortedDictionary<string, float>(StringComparer.Ordinal);

		/// <summary>Code computed from the car, setup slot and values. Any change to a value changes this code.</summary>
		public string Fingerprint { get; private set; } = "";

		/// <summary>Problems found while reading the file (bad lines, duplicate names).</summary>
		public List<string> Warnings { get; } = new List<string>();

		public string DisplayName => string.IsNullOrEmpty(Car) ? Name : $"{Car} - {Name}";

		/// <summary>True when the file carries an official code and its values still produce that code.</summary>
		public bool MatchesPublishedCode =>
			!string.IsNullOrEmpty(PublishedCode) &&
			string.Equals(PublishedCode, Fingerprint, StringComparison.OrdinalIgnoreCase);

		/// <summary>True when this tune is for the given car and setup slot.</summary>
		public bool IsFor(int carId, int slotId) =>
			CarId == carId.ToString(CultureInfo.InvariantCulture) &&
			SlotId == slotId.ToString(CultureInfo.InvariantCulture);

		/// <summary>For official tunes: the set's ID, like "Revival" or "V3.0" (see TuneSets). Empty otherwise.</summary>
		public string SetId { get; private set; } = "";

		/// <summary>An official tune built into the mod from a KINO .knd file (see OfficialTunes).</summary>
		public static TuneFile FromKnd(KndFile knd, string setId, string source) {
			var tune = new TuneFile {
				FilePath = source,
				SetId = setId,
				Name = TuneSets.NameOf(setId),
				Version = setId,
				Author = "TRC",
				Source = source,
				CarId = knd.CarId.ToString(CultureInfo.InvariantCulture),
				SlotId = knd.SlotId.ToString(CultureInfo.InvariantCulture),
				Slot = knd.SlotName,
				EngineId = Math.Max(knd.EngineId, 0).ToString(CultureInfo.InvariantCulture),
			};

			foreach (var pair in knd.Values) {
				tune.Values[pair.Key] = pair.Value;
			}

			tune.Fingerprint = TuneFingerprint.Compute(tune.CarId, tune.SlotId, tune.Values);
			return tune;
		}

		/// <summary>Sets CarX's name for the car, once the game can tell us (it isn't part of the code).</summary>
		public void SetCarName(string car) {
			if (!string.IsNullOrEmpty(car)) {
				Car = car;
			}
		}

		public static TuneFile Load(string path) {
			var tune = new TuneFile { FilePath = path };
			string section = "";
			int lineNumber = 0;

			foreach (string rawLine in File.ReadAllLines(path)) {
				lineNumber++;
				string line = TuneRules.Trim(StripComment(rawLine));
				if (line.Length == 0) {
					continue;
				}

				if (line.Length >= 2 && line[0] == '[' && line[line.Length - 1] == ']') {
					section = TuneRules.Key(line.Substring(1, line.Length - 2));
					continue;
				}

				int eq = line.IndexOf('=');
				if (eq <= 0) {
					tune.Warnings.Add($"Line {lineNumber}: expected 'name = value'");
					continue;
				}

				string key = TuneRules.Key(line.Substring(0, eq));
				string value = TuneRules.Trim(line.Substring(eq + 1));

				if (section == "info") {
					tune.SetInfo(key, value, lineNumber);
				} else if (section == "values") {
					tune.SetValue(key, value, lineNumber);
				} else {
					tune.Warnings.Add($"Line {lineNumber}: '{key}' is outside an [info] or [values] section");
				}
			}

			if (string.IsNullOrEmpty(tune.Name)) {
				tune.Name = Path.GetFileNameWithoutExtension(path);
			}

			if (tune.Values.Count == 0) {
				tune.Warnings.Add("No [values] found");
			}

			tune.Fingerprint = TuneFingerprint.Compute(tune.CarId, tune.SlotId, tune.Values);
			return tune;
		}

		private void SetInfo(string key, string value, int lineNumber) {
			switch (key) {
				case "name": Name = value; break;
				case "car": Car = value; break;
				case "slot": Slot = value; break;
				case "author": Author = value; break;
				case "version": Version = value; break;
				case "notes": Notes = value; break;
				case "source": Source = value; break;
				case "code": PublishedCode = value; break;
				case "car_id": CarId = ParseId(key, value, lineNumber); break;
				case "slot_id": SlotId = ParseId(key, value, lineNumber); break;
				case "engine_id": EngineId = ParseId(key, value, lineNumber); break;
				default: Warnings.Add($"Line {lineNumber}: unknown info field '{key}'"); break;
			}
		}

		private string ParseId(string key, string value, int lineNumber) {
			string id = TuneRules.Id(value);
			if (id.Length == 0) {
				Warnings.Add($"Line {lineNumber}: {key} '{value}' should be a whole number");
			}
			return id;
		}

		private void SetValue(string key, string value, int lineNumber) {
			if (!TuneRules.TryParseValue(value, out float number)) {
				Warnings.Add($"Line {lineNumber}: '{value}' is not a usable number (use a dot for decimals)");
				return;
			}

			if (Values.ContainsKey(key)) {
				Warnings.Add($"Line {lineNumber}: '{key}' is listed twice; the last one is used");
			}

			Values[key] = number;
		}

		private static string StripComment(string line) {
			int hash = line.IndexOf('#');
			return hash >= 0 ? line.Substring(0, hash) : line;
		}
	}

	/// <summary>A tune to be written as a .trctune file: by the KINO importer and for backups.</summary>
	public sealed class TuneDraft {
		public string Name = "";
		public string Car = "";
		public int CarId;
		public string Slot = "";
		public int SlotId;
		public int EngineId;
		public string Author = "";
		public string Version = "";
		public string Notes = "";
		public string Source = "";
		public IDictionary<string, float> Values = new Dictionary<string, float>();

		/// <summary>Optional text after each value, usually the number CarX's tuning screen shows.</summary>
		public IDictionary<string, string> Comments = new Dictionary<string, string>();

		public string Fingerprint => TuneFingerprint.Compute(CarId, SlotId, Values);

		public string ToText() {
			var text = new StringBuilder();
			text.Append("# TRC spec tune, written by the TRC Tune Loader.\n");
			text.Append("# Values are CarX slider positions (0 = far left, 1 = far right), exactly as CarX and KINO save them.\n");
			text.Append("# The text after # is what CarX's tuning screen shows. It's only for reading: change the number before it.\n\n");

			text.Append("[info]\n");
			AppendInfo(text, "name", Name);
			AppendInfo(text, "car", Car);
			AppendInfo(text, "car_id", CarId.ToString(CultureInfo.InvariantCulture));
			AppendInfo(text, "slot", Slot);
			AppendInfo(text, "slot_id", SlotId.ToString(CultureInfo.InvariantCulture));
			AppendInfo(text, "engine_id", EngineId.ToString(CultureInfo.InvariantCulture));
			AppendInfo(text, "author", Author);
			AppendInfo(text, "version", Version);
			AppendInfo(text, "notes", Notes);
			AppendInfo(text, "source", Source);
			AppendInfo(text, "code", Fingerprint);

			text.Append("\n[values]\n");
			foreach (string key in TuneLabels.InDisplayOrder(Values.Keys)) {
				string line = $"{key,-26} = {Values[key].ToString("R", CultureInfo.InvariantCulture)}";
				if (Comments.TryGetValue(key, out string comment) && !string.IsNullOrEmpty(comment)) {
					line = $"{line,-42} # {Clean(comment)}";
				}
				text.Append(line).Append('\n');
			}

			return text.ToString();
		}

		public void Save(string path) {
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, ToText(), new UTF8Encoding(false));
		}

		private static void AppendInfo(StringBuilder text, string key, string value) {
			if (!string.IsNullOrEmpty(value)) {
				text.Append($"{key,-9} = {Clean(value)}\n");
			}
		}

		// '#' would start a comment and a line break would end the line, so neither can be kept.
		private static string Clean(string value) =>
			(value ?? "").Replace("#", "").Replace("\r", " ").Replace("\n", " ").Trim();
	}
}
