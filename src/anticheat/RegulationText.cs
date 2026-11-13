// A regulation as text: its code, the .trcreg file format, and the one-line share for Discord.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	public sealed partial class Regulation {
		public static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
		private static string YesNo(bool value) => value ? "yes" : "no";

		/// <summary>The rules only, in a fixed order: what the code is worked out from.</summary>
		private string RulesText() {
			var text = new StringBuilder();
			text.Append("[rules]\n");
			text.Append("setups = ").Append(string.Join(", ", Setups.Select(s => s.ToLowerInvariant()).OrderBy(s => s, StringComparer.Ordinal))).Append('\n');
			text.Append("cars = ").Append(string.Join(", ", Cars.OrderBy(c => c).Select(c => c.ToString(CultureInfo.InvariantCulture)))).Append('\n');
			text.Append("max_hp = ").Append(MaxHp.HasValue ? Number(MaxHp.Value) : "").Append('\n');
			text.Append("min_weight = ").Append(MinWeight.HasValue ? Number(MinWeight.Value) : "").Append('\n');
			text.Append("max_weight = ").Append(MaxWeight.HasValue ? Number(MaxWeight.Value) : "").Append('\n');
			text.Append("min_kg_per_hp = ").Append(MinKgPerHp.HasValue ? Number(MinKgPerHp.Value) : "").Append('\n');
			// Only written when set, so regulations made before it keep their codes.
			if (MaxHpPerKg.HasValue) {
				text.Append("max_hp_per_kg = ").Append(Number(MaxHpPerKg.Value)).Append('\n');
			}
			// Only written when set, so regulations made before it keep their codes.
			if (DiffTypes.Count > 0) {
				text.Append("diff_types = ").Append(string.Join(", ", DiffTypes.Distinct().OrderBy(d => d).Select(d => d.ToString(CultureInfo.InvariantCulture)))).Append('\n');
			}
			if (Spec.Length > 0) {
				text.Append("spec = ").Append(Spec.Replace("#", "").Replace("\n", " ").Trim()).Append('\n');
			}
			text.Append("engine_swaps = ").Append(YesNo(EngineSwaps)).Append('\n');
			text.Append("drivetrain_change = ").Append(YesNo(DrivetrainChange)).Append('\n');
			text.Append("kino_swaps = ").Append(YesNo(OutsideSwaps)).Append('\n');
			text.Append("kino_weight = ").Append(YesNo(OutsideWeight)).Append('\n');
			text.Append("kino_suspension = ").Append(YesNo(OutsideSuspension)).Append('\n');
			text.Append("widebody = ").Append(YesNo(Widebody)).Append('\n');
			text.Append("[limits]\n");
			foreach (ValueLimit limit in Limits.Where(l => l.Min.HasValue || l.Max.HasValue).OrderBy(l => l.Key, StringComparer.Ordinal)) {
				text.Append(limit.Key).Append(" = ").Append(limit.Min.HasValue ? Number(limit.Min.Value) : "").Append(" .. ")
					.Append(limit.Max.HasValue ? Number(limit.Max.Value) : "").Append('\n');
			}
			text.Append("[tyres]\n");
			foreach (TyreBracket t in Tyres.OrderBy(t => t.UpToKg)) {
				text.Append(Number(t.UpToKg)).Append(" = ").Append(Number(t.MaxWidth)).Append('\n');
			}
			return text.ToString();
		}

		public static string CodeOf(string rules) {
			using (var sha = SHA256.Create()) {
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(rules));
				return $"REG-{hash[0]:X2}{hash[1]:X2}-{hash[2]:X2}{hash[3]:X2}";
			}
		}

		private static string OneLine(string text) => (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

		public string ToText() {
			var text = new StringBuilder("# TRC Tune Loader regulation. Load it in the Regulations tab.\n");
			text.Append("[info]\n");
			text.Append("name = ").Append(OneLine(Name)).Append('\n');
			text.Append("author = ").Append(OneLine(Author)).Append('\n');
			text.Append("notes = ").Append(OneLine(Notes)).Append('\n');
			foreach (int car in Cars.OrderBy(c => c)) {
				if (CarNames.TryGetValue(car, out string name) && name.Length > 0) {
					text.Append("car_name ").Append(car.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(OneLine(name)).Append('\n');
				}
			}
			text.Append(RulesText());
			text.Append("# code ").Append(Code).Append('\n');
			return text.ToString();
		}

		private static float? Float(string value) =>
			float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1e7f ? f : (float?)null;

		private static bool Yes(string value) => !value.Trim().StartsWith("n", StringComparison.OrdinalIgnoreCase);

		/// <summary>Reads a regulation file's text; null when it isn't one.</summary>
		public static Regulation Parse(string file) {
			if (string.IsNullOrEmpty(file)) {
				return null;
			}
			var reg = new Regulation();
			string section = "";
			bool any = false;
			foreach (string raw in file.Split('\n')) {
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) {
					continue;
				}
				if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) {
					section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
					continue;
				}
				int eq = line.IndexOf('=');
				if (eq <= 0) {
					continue;
				}
				string key = line.Substring(0, eq).Trim().ToLowerInvariant();
				string value = line.Substring(eq + 1).Trim();
				switch (section) {
					case "info":
						any = true;
						if (key == "name") reg.Name = Clip(value, 60);
						else if (key == "author") reg.Author = Clip(value, 60);
						else if (key == "notes") reg.Notes = Clip(value, 400);
						else if (key.StartsWith("car_name ", StringComparison.Ordinal) && int.TryParse(key.Substring(9).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int id)) {
							reg.CarNames[id] = Clip(value, 60);
						}
						break;
					case "rules":
						any = true;
						switch (key) {
							case "setups":
								foreach (string s in value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0)) {
									string known = SetupNames.FirstOrDefault(n => string.Equals(n, s.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
									if (known != null && !reg.Setups.Contains(known)) {
										reg.Setups.Add(known);
									}
								}
								break;
							case "cars":
								foreach (string c in value.Split(',')) {
									if (int.TryParse(c.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int car) && !reg.Cars.Contains(car) && reg.Cars.Count < 500) {
										reg.Cars.Add(car);
									}
								}
								break;
							case "max_hp": reg.MaxHp = Float(value); break;
							case "min_weight": reg.MinWeight = Float(value); break;
							case "max_weight": reg.MaxWeight = Float(value); break;
							case "min_kg_per_hp": reg.MinKgPerHp = Float(value); break;
							case "max_hp_per_kg": reg.MaxHpPerKg = Float(value); break;
							case "spec": reg.Spec = value.Trim(); break;
							case "diff_types":
								foreach (string d in value.Split(',')) {
									if (int.TryParse(d.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int type) && type < DiffTypeNames.Length && !reg.DiffTypes.Contains(type)) {
										reg.DiffTypes.Add(type);
									}
								}
								break;
							case "engine_swaps": reg.EngineSwaps = Yes(value); break;
							case "drivetrain_change": reg.DrivetrainChange = Yes(value); break;
							case "kino_swaps": reg.OutsideSwaps = Yes(value); break;
							case "kino_weight": reg.OutsideWeight = Yes(value); break;
							case "kino_suspension": reg.OutsideSuspension = Yes(value); break;
							case "widebody": reg.Widebody = Yes(value); break;
						}
						break;
					case "limits": {
						int dots = value.IndexOf("..", StringComparison.Ordinal);
						string min = dots >= 0 ? value.Substring(0, dots) : "";
						string max = dots >= 0 ? value.Substring(dots + 2) : value;
						var limit = new ValueLimit { Key = Clip(key, 60), Min = Float(min), Max = Float(max) };
						if ((limit.Min.HasValue || limit.Max.HasValue) && reg.Limits.Count < 200 && !reg.Limits.Any(l => l.Key == limit.Key)) {
							reg.Limits.Add(limit);
							any = true;
						}
						break;
					}
					case "tyres": {
						float? kg = Float(key);
						float? width = Float(value);
						if (kg.HasValue && width.HasValue && kg > 0 && width > 0 && reg.Tyres.Count < 20) {
							reg.Tyres.Add(new TyreBracket { UpToKg = kg.Value, MaxWidth = width.Value });
							any = true;
						}
						break;
					}
				}
			}
			return any ? reg : null;
		}

		private static string Clip(string text, int max) {
			string clean = new string((text ?? "").Where(c => c != '<' && c != '>' && !char.IsControl(c)).ToArray()).Trim();
			return clean.Length > max ? clean.Substring(0, max) : clean;
		}

		/// <summary>One line to paste in Discord: the file packed (deflate) and written in base64 after "TRCREG1.".</summary>
		public string ShareText() {
			byte[] raw = Encoding.UTF8.GetBytes(ToText());
			using (var memory = new MemoryStream()) {
				using (var deflate = new DeflateStream(memory, CompressionMode.Compress, true)) {
					deflate.Write(raw, 0, raw.Length);
				}
				return SharePrefix + Convert.ToBase64String(memory.ToArray());
			}
		}

		/// <summary>A regulation from pasted text: a share line (anywhere in it), or a whole regulation file. Null when there's none.</summary>
		public static Regulation FromPasted(string pasted) {
			if (string.IsNullOrEmpty(pasted)) {
				return null;
			}
			int at = pasted.IndexOf(SharePrefix, StringComparison.Ordinal);
			if (at < 0) {
				return Parse(pasted);
			}
			string data = new string(pasted.Substring(at + SharePrefix.Length).TakeWhile(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=').ToArray());
			try {
				using (var input = new DeflateStream(new MemoryStream(Convert.FromBase64String(data)), CompressionMode.Decompress))
				using (var output = new MemoryStream()) {
					var buffer = new byte[4096];
					int read;
					while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
						output.Write(buffer, 0, read);
						if (output.Length > 64 * 1024) {
							return null;
						}
					}
					return Parse(Encoding.UTF8.GetString(output.ToArray()));
				}
			} catch (Exception) {
				return null;
			}
		}

		public Regulation Copy() => Parse(ToText()) ?? new Regulation();
	}
}
