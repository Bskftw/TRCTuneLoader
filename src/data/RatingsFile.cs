// The ratings file (TimeAttack/ratings_<SteamID>.dat): saved sealed, read back, and started again when it was changed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	public sealed partial class RatingBook {
		/// <summary>
		/// The sealed file (<see cref="RatingSeal"/>), one per SteamID so two Steam accounts on one PC keep their own;
		/// and the plain text one up to 0.15.1 (read once, then removed).
		/// </summary>
		public static string FileFor(ulong steamId) => $"ratings_{steamId.ToString(CultureInfo.InvariantCulture)}.dat";
		public const string OldFileName = "ratings.txt";

		/// <summary>A sealed file that had been changed, kept for a look (it never counts again).</summary>
		public static string ChangedFileFor(ulong steamId) => $"ratings_{steamId.ToString(CultureInfo.InvariantCulture)}-changed.dat";

		/// <summary>True when the file had been changed outside the mod (or was another player's), so the ratings started again.</summary>
		public bool WasChanged { get; private set; }

		public void Save(string folder, ulong steamId) {
			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, FileFor(steamId));
			string temp = path + ".tmp";
			File.WriteAllText(temp, RatingSeal.Seal(ToText(steamId), steamId), new UTF8Encoding(false));
			// Written next to it, then swapped in, so a crash halfway never leaves half a file (which would read as changed).
			if (File.Exists(path)) {
				File.Replace(temp, path, null);
			} else {
				File.Move(temp, path);
			}
		}

		private string ToText(ulong steamId) {
			var text = new StringBuilder("# TRC ratings, kept by the TRC Tune Loader on this PC.\n");
			text.Append("version = ").Append(Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
			text.Append("steam_id = ").Append(steamId.ToString(CultureInfo.InvariantCulture)).Append('\n');
			text.Append("elo_touge = ").Append(Elo[0].ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
			text.Append("elo_highway = ").Append(Elo[1].ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
			text.Append("elo_circuit = ").Append(Elo[2].ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
			foreach (var pair in RuleElo.OrderBy(p => p.Key, StringComparer.Ordinal)) {
				string name = RuleNames.TryGetValue(pair.Key, out string n) ? n.Replace("|", " ").Replace("\n", " ") : "";
				text.Append("elo_rule = ").Append(pair.Key).Append('|')
					.Append(string.Join("|", pair.Value.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture)))).Append('|').Append(name).Append('\n');
			}
			text.Append("safety = ").Append(Safety.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
			text.Append("license = ").Append(License.ToString()).Append('\n');
			text.Append("events = ").Append(Events.ToString(CultureInfo.InvariantCulture)).Append('\n');
			foreach (string line in History) {
				text.Append("history = ").Append(line.Replace("\n", " ")).Append('\n');
			}
			return text.ToString();
		}

		/// <summary>
		/// The player's ratings. A sealed file that doesn't open (changed by hand, damaged, or another player's) is
		/// moved aside and the ratings start again, with a note saying why. Up to 0.15.1 the file was plain text:
		/// that one is read once, sealed, and removed.
		/// </summary>
		public static RatingBook Load(string folder, ulong steamId) {
			string path = Path.Combine(folder, FileFor(steamId));
			string old = Path.Combine(folder, OldFileName);
			if (File.Exists(path)) {
				if (RatingSeal.TryOpen(File.ReadAllText(path, Encoding.UTF8), steamId, out string text) && FromText(text, out RatingBook book, out ulong owner) && owner == steamId) {
					return book;
				}
				File.Copy(path, Path.Combine(folder, ChangedFileFor(steamId)), true);
				File.Delete(path);
				var fresh = new RatingBook { WasChanged = true };
				fresh.Note(DateTime.Now, "The ratings file was changed outside the TRC Tune Loader (or was another player's), so your ratings started again");
				fresh.Save(folder, steamId);
				return fresh;
			}
			if (File.Exists(old)) {
				FromText(File.ReadAllText(old, Encoding.UTF8), out RatingBook book, out _);
				book.Save(folder, steamId);
				File.Delete(old);
				return book;
			}
			return new RatingBook();
		}

		/// <summary>The ratings in the file's text; false when it isn't a ratings file.</summary>
		private static bool FromText(string file, out RatingBook book, out ulong owner) {
			book = new RatingBook();
			owner = 0;
			int version = 1;
			bool any = false;
			foreach (string raw in file.Split('\n')) {
				int eq = raw.IndexOf('=');
				if (raw.StartsWith("#", StringComparison.Ordinal) || eq <= 0) {
					continue;
				}
				any = true;
				string key = raw.Substring(0, eq).Trim();
				string value = raw.Substring(eq + 1).Trim();
				double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number);
				switch (key) {
					case "elo_touge": book.Elo[0] = number; break;
					case "elo_highway": book.Elo[1] = number; break;
					case "elo_circuit": book.Elo[2] = number; break;
					case "elo_rule": {
						string[] parts = value.Split('|');
						if (parts.Length >= 4 && parts[0].StartsWith("REG-", StringComparison.Ordinal)) {
							var pool = new double[3];
							for (int i = 0; i < 3; i++) {
								double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out pool[i]);
							}
							book.RuleElo[parts[0]] = pool;
							if (parts.Length >= 5) {
								book.RuleNames[parts[0]] = parts[4];
							}
						}
						break;
					}
					case "safety": book.Safety = Math.Max(0, Math.Min(MaxSafety, number)); break;
					case "license": Enum.TryParse(value, out book.License); break;
					case "events": book.Events = (int)number; break;
					case "history": book.History.Add(value); break;
					case "version": version = (int)number; break;
					case "steam_id": ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out owner); break;
				}
			}
			if (version < 2) {
				// New rules (0.15.0): the old Safety Rating counted every light touch and every time attack.
				book.Safety = StartSafety;
				book.License = License.Rookie;
				book.Events = 0;
				book.Note(DateTime.Now, "Safety Rating and license reset for the new rules (Elo kept)");
			}
			return any;
		}
	}
}
