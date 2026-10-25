using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	public enum License : byte { Rookie, D, C, B, A }

	/// <summary>
	/// The player's TRC ratings, kept on this PC until the TRC server holds them:
	///
	///   Elo, one each for touge, highway and circuits: 1500 to start, moved by TRC spec battles (win, draw or loss
	///   against the other driver's rating) and races (every other finisher counts as a little battle).
	///
	///   Safety Rating, 0 to 4.99, 2.50 to start, like iRacing's: no blame, it's what happened to this car. Clean
	///   kilometres raise it; incidents (a reset, a hit, possible contact, leaving the road, a jump start) take it down.
	///
	///   License, Rookie to A: up a step at SR 3.00 or more once enough events are driven; down a step below 1.00.
	/// </summary>
	public sealed class RatingBook {
		public const double StartElo = 1500;
		public const double StartSafety = 2.5;
		public const double MaxSafety = 4.99;
		private const double BattleK = 32;
		private const double RaceK = 32;

		/// <summary>Safety Rating per clean kilometre (up to 10 km an event), and per incident point.</summary>
		private const double SafetyPerKm = 0.04;
		private const double MaxKmPerEvent = 10;
		private const double SafetyPerPoint = 0.12;

		/// <summary>Promotion: SR at least this, and this many events driven, for D, C, B and A. Below the other, down a step.</summary>
		public const double PromoteSafety = 3.0;
		public const double DemoteSafety = 1.0;
		private static readonly int[] EventsFor = { 0, 3, 8, 15, 25 };

		/// <summary>Incident points, as iRacing counts them in spirit: a light touch is little, contact is a lot.</summary>
		public const int ResetPoints = 2;
		public const int HitPoints = 1;
		public const int ContactPoints = 4;
		public const int OffRoadPoints = 2;
		public const int JumpPoints = 1;

		public double[] Elo = { StartElo, StartElo, StartElo };
		public double Safety = StartSafety;
		public License License;
		public int Events;
		public List<string> History = new List<string>();

		public static string Discipline(CourseKind kind) => Course.KindNames[(int)kind];

		/// <summary>The expected score against a rating (0 to 1).</summary>
		public static double Expected(double mine, double theirs) => 1.0 / (1.0 + Math.Pow(10, (theirs - mine) / 400.0));

		/// <summary>A TRC spec 1v1 battle: 1 for a win, 0.5 a draw, 0 a loss. Returns the change.</summary>
		public double Battle(CourseKind kind, double opponent, double score, string against, DateTime when) {
			int k = (int)kind;
			double change = BattleK * (score - Expected(Elo[k], opponent));
			Elo[k] += change;
			Note(when, $"{Discipline(kind)} battle vs {against}: {(score >= 1 ? "won" : score > 0 ? "drew" : "lost")}, {Signed(change)} → {Elo[k]:0}");
			return change;
		}

		/// <summary>A TRC spec race: every other finisher is a little battle (beat them or not), each worth K/(drivers - 1). Returns the change.</summary>
		public double Race(CourseKind kind, int myPosition, IList<(double Elo, int Position)> others, DateTime when) {
			int k = (int)kind;
			if (others.Count == 0) {
				return 0;
			}
			double change = 0;
			foreach (var (elo, position) in others) {
				double score = myPosition < position ? 1 : myPosition == position ? 0.5 : 0;
				change += RaceK / others.Count * (score - Expected(Elo[k], elo));
			}
			Elo[k] += change;
			Note(when, $"{Discipline(kind)} race: P{myPosition} of {others.Count + 1}, {Signed(change)} → {Elo[k]:0}");
			return change;
		}

		/// <summary>An event driven (a run, a battle or a race): clean kilometres up, incidents down, then the license. Returns the SR change.</summary>
		public double Drive(double km, int incidentPoints, string what, DateTime when) {
			double before = Safety;
			Safety += SafetyPerKm * Math.Min(Math.Max(0, km), MaxKmPerEvent) - SafetyPerPoint * Math.Max(0, incidentPoints);
			Safety = Math.Max(0, Math.Min(MaxSafety, Safety));
			Events++;
			License was = License;
			if (License < License.A && Safety >= PromoteSafety && Events >= EventsFor[(int)License + 1]) {
				License++;
			} else if (License > License.Rookie && Safety < DemoteSafety) {
				License--;
				Safety = StartSafety;
			}
			string licence = License != was ? $", now {LicenseName(License)} license" : "";
			if (incidentPoints > 0 || License != was || Events % 5 == 0) {
				Note(when, $"{what}: {km:0.0} km, {incidentPoints} incident point(s), SR {Signed(Safety - before, "0.00")} → {Safety:0.00}{licence}");
			}
			return Safety - before;
		}

		public static string LicenseName(License license) => license == License.Rookie ? "Rookie" : license.ToString();

		/// <summary>"B 3.21": the license and Safety Rating, as other players see it.</summary>
		public string Badge => $"{LicenseName(License)} {Safety:0.00}";

		private void Note(DateTime when, string text) {
			History.Add($"{when.ToString("MMM d HH:mm", CultureInfo.InvariantCulture)}  {text}");
			if (History.Count > 200) {
				History.RemoveAt(0);
			}
		}

		private static string Signed(double value, string format = "0") => (value >= 0 ? "+" : "") + value.ToString(format, CultureInfo.InvariantCulture);

		// ---- File (TimeAttack/ratings.txt) ----

		public const string FileName = "ratings.txt";

		public void Save(string folder) {
			Directory.CreateDirectory(folder);
			var text = new StringBuilder("# TRC ratings, kept by the TRC Tune Loader on this PC.\n");
			text.Append("elo_touge = ").Append(Elo[0].ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
			text.Append("elo_highway = ").Append(Elo[1].ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
			text.Append("elo_circuit = ").Append(Elo[2].ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
			text.Append("safety = ").Append(Safety.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
			text.Append("license = ").Append(License.ToString()).Append('\n');
			text.Append("events = ").Append(Events.ToString(CultureInfo.InvariantCulture)).Append('\n');
			foreach (string line in History) {
				text.Append("history = ").Append(line.Replace("\n", " ")).Append('\n');
			}
			File.WriteAllText(Path.Combine(folder, FileName), text.ToString(), new UTF8Encoding(false));
		}

		public static RatingBook Load(string folder) {
			var book = new RatingBook();
			string path = Path.Combine(folder, FileName);
			if (!File.Exists(path)) {
				return book;
			}
			foreach (string raw in File.ReadAllLines(path, Encoding.UTF8)) {
				int eq = raw.IndexOf('=');
				if (raw.StartsWith("#", StringComparison.Ordinal) || eq <= 0) {
					continue;
				}
				string key = raw.Substring(0, eq).Trim();
				string value = raw.Substring(eq + 1).Trim();
				double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number);
				switch (key) {
					case "elo_touge": book.Elo[0] = number; break;
					case "elo_highway": book.Elo[1] = number; break;
					case "elo_circuit": book.Elo[2] = number; break;
					case "safety": book.Safety = Math.Max(0, Math.Min(MaxSafety, number)); break;
					case "license": Enum.TryParse(value, out book.License); break;
					case "events": book.Events = (int)number; break;
					case "history": book.History.Add(value); break;
				}
			}
			return book;
		}
	}
}
