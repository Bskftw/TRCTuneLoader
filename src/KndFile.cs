using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// A tune saved by KINO's Tune tab (.knd). Read-only: TRC keeps making tunes in KINO,
	/// and the loader imports them. Layout (little-endian, .NET BinaryWriter strings):
	///
	///   4 bytes   format version
	///   int32     car ID, int32 setup slot ID, string slot name ("Ultimate")
	///   int32     count, then count x (string setting name, float32 slider position)
	///   int32     engine ID (0 = stock), int32 count, then count x (string, float32)   [optional]
	///   a short tail that isn't used
	///
	/// tools/tune_code.py reads the same layout with the same rules. See docs/carx-tuning-notes.md.
	/// </summary>
	public sealed class KndFile {
		public const string Extension = ".knd";

		private const int MaxEntries = 10000;

		public string FilePath { get; private set; } = "";
		public string FormatVersion { get; private set; } = "";
		public int CarId { get; private set; }
		public int SlotId { get; private set; }
		public string SlotName { get; private set; } = "";

		/// <summary>Slider positions by setting name, the same as CarX's saved tune.</summary>
		public SortedDictionary<string, float> Values { get; } = new SortedDictionary<string, float>(StringComparer.Ordinal);

		/// <summary>Engine ID from the engine section (0 = stock), or -1 when the file has none.</summary>
		public int EngineId { get; private set; } = -1;

		public List<string> Warnings { get; } = new List<string>();

		public string Fingerprint => TuneFingerprint.Compute(CarId, SlotId, Values);

		public static KndFile Load(string path) {
			var knd = Read(File.ReadAllBytes(path));
			knd.FilePath = path;
			return knd;
		}

		/// <summary>Reads a .knd file's bytes. Throws InvalidDataException when it isn't a KINO tune.</summary>
		public static KndFile Read(byte[] data) {
			var knd = new KndFile();
			using (var reader = new BinaryReader(new MemoryStream(data), new UTF8Encoding(false, false))) {
				try {
					knd.FormatVersion = BitConverter.ToString(reader.ReadBytes(4)).Replace("-", " ");
					knd.CarId = reader.ReadInt32();
					knd.SlotId = reader.ReadInt32();
					knd.SlotName = reader.ReadString();
					knd.ReadValues(reader, knd.Values);
				} catch (Exception e) when (e is IOException || e is FormatException) {
					throw new InvalidDataException("The file is damaged or isn't a KINO tune.", e);
				}

				if (knd.CarId <= 0 || knd.SlotId <= 0) {
					throw new InvalidDataException("No valid car or setup ID: not a KINO tune.");
				}

				// The engine section is optional; a broken one doesn't spoil the tune itself.
				try {
					int engineId = reader.ReadInt32();
					knd.ReadValues(reader, new SortedDictionary<string, float>(StringComparer.Ordinal));
					knd.EngineId = engineId;
				} catch (Exception e) when (e is IOException || e is FormatException || e is InvalidDataException) {
					knd.Warnings.Add("No engine section");
				}
			}

			return knd;
		}

		private void ReadValues(BinaryReader reader, SortedDictionary<string, float> into) {
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxEntries) {
				throw new InvalidDataException($"Unexpected number of settings ({count}): not a KINO tune.");
			}

			for (int i = 0; i < count; i++) {
				string key = TuneRules.Key(reader.ReadString());
				float value = reader.ReadSingle();

				if (key.Length == 0) {
					Warnings.Add("A setting without a name was skipped");
				} else if (!TuneRules.IsUsableValue(value)) {
					Warnings.Add($"'{key}' has an unusable value and was skipped");
				} else {
					into[key] = value;
				}
			}
		}
	}
}
