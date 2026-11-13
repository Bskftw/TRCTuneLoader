// The player's ratings: Elo for touge, highway and circuits, Safety Rating and licence, moved by battles, races and driving.
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
	///   Safety Rating, 0 to 4.99, 2.50 to start, like iRacing's: no blame, it's what happened to this car. Only
	///   battles and races count (time attack is the player alone, often just messing about). Clean kilometres raise
	///   it; hard contact, a hard hit and a reset take it down. Light touches in close racing don't count, and one
	///   event can move it only so far either way.
	///
	///   License, Rookie to A: up a step at SR 3.00 or more once enough events are driven; down a step below 1.00.
	///
	///   Regulation Elo (0.16): its own Elo, per regulation code and kind of course, for battles and races run to a
	///   regulation (bskftw: "regulations should have their own elo"). TRC spec Elo isn't touched by them.
	///
	/// A "just for fun" event changes nothing.
	/// All of it is still being tested (Oct 29): it isn't official, and it will be reset before it counts.
	/// Version 2 (0.15.0) reset the Safety Rating and license once, for the new rules; Elo was kept. Since 0.15.2
	/// the file is sealed (<see cref="RatingSeal"/>) and tied to the player's SteamID (TimeAttack/ratings_&lt;SteamID&gt;.dat).
	/// </summary>
	public sealed partial class RatingBook {
		public const double StartElo = 1500;
		public const double StartSafety = 2.5;
		public const double MaxSafety = 4.99;
		private const double BattleK = 32;
		private const double RaceK = 32;

		/// <summary>Safety Rating per clean kilometre (up to 10 km an event), per incident point, and the most one event can take away.</summary>
		private const double SafetyPerKm = 0.04;
		private const double MaxKmPerEvent = 10;
		private const double SafetyPerPoint = 0.08;
		public const double MaxDropPerEvent = 0.3;

		/// <summary>The rating rules' version, saved in the file: an older file gets the Safety Rating and license reset once.</summary>
		public const int Version = 2;

		/// <summary>Said wherever the ratings show.</summary>
		public const string TestingNote = "Ratings are being tested: they're not official yet, and Elo and Safety Rating will be reset before they count.";

		/// <summary>Promotion: SR at least this, and this many events driven, for D, C, B and A. Below the other, down a step.</summary>
		public const double PromoteSafety = 3.0;
		public const double DemoteSafety = 1.0;
		private static readonly int[] EventsFor = { 0, 3, 8, 15, 25 };

		/// <summary>
		/// Incident points. A light touch costs nothing (close racing, Oct 29: a clean, close battle took a player to
		/// 0.16); hard contact with another car, a hard hit and a reset do. Leaving the road and jump starts don't:
		/// the run or race already deals with those.
		/// </summary>
		public const int ResetPoints = 1;
		public const int HitPoints = 1;
		public const int ContactPoints = 2;

		public double[] Elo = { StartElo, StartElo, StartElo };

		/// <summary>Elo for events run to a regulation, by its code (REG-XXXX-XXXX): touge, highway, circuit.</summary>
		public Dictionary<string, double[]> RuleElo = new Dictionary<string, double[]>(StringComparer.Ordinal);

		/// <summary>The names of the regulations with an Elo here, for the ratings page.</summary>
		public Dictionary<string, string> RuleNames = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>The Elo pool: TRC spec ("" code), or a regulation's.</summary>
		public double[] Pool(string code) {
			if (string.IsNullOrEmpty(code)) {
				return Elo;
			}
			if (!RuleElo.TryGetValue(code, out double[] pool)) {
				RuleElo[code] = pool = new[] { StartElo, StartElo, StartElo };
			}
			return pool;
		}

		/// <summary>The Elo for a kind of course: TRC spec, or the regulation's (1500 for one not driven yet).</summary>
		public double EloFor(string code, CourseKind kind) =>
			string.IsNullOrEmpty(code) ? Elo[(int)kind] : RuleElo.TryGetValue(code, out double[] pool) ? pool[(int)kind] : StartElo;

		private string PoolName(string code) =>
			string.IsNullOrEmpty(code) ? "" : $" ({(RuleNames.TryGetValue(code, out string name) && name.Length > 0 ? name : code)})";
		public double Safety = StartSafety;
		public License License;
		public int Events;
		public List<string> History = new List<string>();

		public static string Discipline(CourseKind kind) => Course.KindNames[(int)kind];

		/// <summary>The expected score against a rating (0 to 1).</summary>
		public static double Expected(double mine, double theirs) => 1.0 / (1.0 + Math.Pow(10, (theirs - mine) / 400.0));

		/// <summary>A TRC spec 1v1 battle: 1 for a win, 0.5 a draw, 0 a loss. Returns the change.</summary>
		public double Battle(CourseKind kind, double opponent, double score, string against, DateTime when, string rule = "") {
			int k = (int)kind;
			double[] elo = Pool(rule);
			double change = BattleK * (score - Expected(elo[k], opponent));
			elo[k] += change;
			Note(when, $"{Discipline(kind)} battle{PoolName(rule)} vs {against}: {(score >= 1 ? "won" : score > 0 ? "drew" : "lost")}, {Signed(change)} → {elo[k]:0}");
			return change;
		}

		/// <summary>A TRC spec race: every other finisher is a little battle (beat them or not), each worth K/(drivers - 1). Returns the change.</summary>
		public double Race(CourseKind kind, int myPosition, IList<(double Elo, int Position)> others, DateTime when, string rule = "") {
			int k = (int)kind;
			if (others.Count == 0) {
				return 0;
			}
			double[] pool = Pool(rule);
			double change = 0;
			foreach (var (elo, position) in others) {
				double score = myPosition < position ? 1 : myPosition == position ? 0.5 : 0;
				change += RaceK / others.Count * (score - Expected(pool[k], elo));
			}
			pool[k] += change;
			Note(when, $"{Discipline(kind)} race{PoolName(rule)}: P{myPosition} of {others.Count + 1}, {Signed(change)} → {pool[k]:0}");
			return change;
		}

		/// <summary>An event driven (a run, a battle or a race): clean kilometres up, incidents down, then the license. Returns the SR change.</summary>
		public double Drive(double km, int incidentPoints, string what, DateTime when) {
			double before = Safety;
			double change = SafetyPerKm * Math.Min(Math.Max(0, km), MaxKmPerEvent) - SafetyPerPoint * Math.Max(0, incidentPoints);
			Safety += Math.Max(-MaxDropPerEvent, change);
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
	}
}
