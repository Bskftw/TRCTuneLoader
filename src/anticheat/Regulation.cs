// A regulation (a community's own rules for cars): its limits, checking a car against them, and its rules in a few words.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// A tune regulation (bskftw, Nov 1): for communities that don't run TRC tunes but keep tunes balanced with
	/// rules, like the Gunma Discord's "under 600 hp, tyres under 300, no drivetrain swaps". Anyone makes one, saves
	/// it (Regulations/*.trcreg) and shares it; races, tournaments and 1v1 battles can be run to it.
	///
	/// The file is plain text, so it can be read in Discord:
	///   [info]    name, author, notes
	///   [rules]   setups, cars, max_hp, min_weight, max_weight, min_kg_per_hp, and what's allowed (yes/no)
	///   [limits]  a tune setting = min .. max (either end may be left out), in CarX's shown numbers
	///   [tyres]   a weight class: up to kg = widest tyre (mm)
	/// Its code (REG-XXXX-XXXX) is worked out from the rules alone, so renaming it keeps it the same regulation.
	/// </summary>
	public sealed partial class Regulation {
		public const string Extension = ".trcreg";
		public const string SharePrefix = "TRCREG1.";

		/// <summary>CarX's setups, in its order, with the names its garage shows.</summary>
		public static readonly string[] SetupNames = { "Stock", "Turbo", "Racing", "Drift", "Ultimate", "UltimatePro" };
		public static readonly string[] SetupLabels = { "Stock", "Turbo", "Race", "Drift", "Ultimate", "Ultimate Pro" };

		public string Name = "";
		public string Author = "";
		public string Notes = "";

		/// <summary>The setups allowed (CarX's names), none for any.</summary>
		public List<string> Setups = new List<string>();

		/// <summary>The cars allowed (CarX car IDs), none for any; with their names for the list.</summary>
		public List<int> Cars = new List<int>();
		public Dictionary<int, string> CarNames = new Dictionary<int, string>();

		public float? MaxHp;
		public float? MinWeight;
		public float? MaxWeight;

		/// <summary>Power to weight: at least this many kg per hp (the first way; still read, and kept on older regulations).</summary>
		public float? MinKgPerHp;

		/// <summary>
		/// Power to weight, the way leagues write it (bskftw, Nov 3): at most this many hp per kg (0.22: a car can't go
		/// over 0.22 hp for each kg).
		/// </summary>
		public float? MaxHpPerKg;

		/// <summary>
		/// A player spec the car must run (its title, like "Drift Kings v2"; "" for none): each driver's tune has to be that
		/// spec's tune for their car (bskftw, Nov 3: spec racing with players' own specs). Drivers need the spec installed.
		/// </summary>
		public string Spec = "";

		/// <summary>CarX's diff types, by the number its tuning keeps (CarX.DiffType): what the tuning screen shows.</summary>
		public static readonly string[] DiffTypeNames = { "Open", "Welded", "Viscous", "Clutch type" };
		public const string DiffTypeKey = "diff_type";

		/// <summary>
		/// The diff types allowed, by number (empty: any). bskftw, Nov 3: a limit on the diff type was a least and a most of a
		/// number, but each number is a named type, so it's a list of the types allowed, picked by name.
		/// </summary>
		public List<int> DiffTypes = new List<int>();

		public static string DiffTypeName(int type) => type >= 0 && type < DiffTypeNames.Length ? DiffTypeNames[type] : $"type {type}";

		public bool EngineSwaps = true;
		public bool DrivetrainChange = true;
		public bool OutsideSwaps = true;
		public bool OutsideWeight = true;
		public bool OutsideSuspension = true;
		public bool Widebody = true;

		public List<ValueLimit> Limits = new List<ValueLimit>();
		public List<TyreBracket> Tyres = new List<TyreBracket>();

		/// <summary>Where it was loaded from ("" when it isn't saved).</summary>
		public string FilePath = "";

		public string Code => CodeOf(RulesText());

		public static string SetupLabel(string setup) {
			int i = Array.FindIndex(SetupNames, s => string.Equals(s, setup, StringComparison.OrdinalIgnoreCase));
			return i >= 0 ? SetupLabels[i] : setup;
		}

		// ---- Checking ----

		/// <summary>
		/// How far past a limit still counts (bskftw, Nov 1: nobody can hit 600 hp on the dot, least of all on a
		/// controller; a tyre stretch that shows 10 can really be 10.3). A value is checked the way CarX shows it: it
		/// passes when it rounds to the limit at the decimals CarX shows, so up to half its last digit over. Power and
		/// weight show as whole numbers (half a hp or kg), kg per hp to two decimals. A setting whose decimals aren't
		/// known gets 0.5% (at least 0.01).
		/// </summary>
		public static float Leeway(int? decimals, float limit) =>
			decimals.HasValue ? 0.5f * (float)Math.Pow(10, -decimals.Value) + 1e-4f : Math.Max(0.01f, Math.Abs(limit) * 0.005f);

		private static bool Over(float value, float max, float leeway) => value > max + leeway;
		private static bool Under(float value, float min, float leeway) => value < min - leeway;

		/// <summary>The decimals CarX shows a setting with, when known.</summary>
		private static int? DecimalsFor(CarFacts car, string key) => car.Decimals.TryGetValue(key, out int d) ? d : (int?)null;

		public RegulationResult Check(CarFacts car) {
			var r = new RegulationResult();
			if (Cars.Count > 0 && !Cars.Contains(car.CarId)) {
				r.Broken.Add($"The {(car.CarName.Length > 0 ? car.CarName : "car")} isn't one of the cars allowed");
			}
			if (Setups.Count > 0 && !Setups.Any(s => string.Equals(s, car.Setup, StringComparison.OrdinalIgnoreCase))) {
				r.Broken.Add(car.Setup.Length == 0 ? "Setup unknown" : $"The {SetupLabel(car.Setup)} setup isn't allowed (only {string.Join(", ", Setups.Select(SetupLabel))})");
			}
			if (MaxHp.HasValue) {
				if (car.Hp <= 0) {
					r.Unknown.Add("power");
				} else if (Over(car.Hp, MaxHp.Value, Leeway(0, MaxHp.Value))) {
					r.Broken.Add($"{car.Hp:0} hp: the most is {MaxHp.Value:0} hp");
				}
			}
			if (MinWeight.HasValue || MaxWeight.HasValue) {
				if (car.Mass <= 0) {
					r.Unknown.Add("weight");
				} else if (MinWeight.HasValue && Under(car.Mass, MinWeight.Value, Leeway(0, MinWeight.Value))) {
					r.Broken.Add($"{car.Mass:0} kg: the least is {MinWeight.Value:0} kg");
				} else if (MaxWeight.HasValue && Over(car.Mass, MaxWeight.Value, Leeway(0, MaxWeight.Value))) {
					r.Broken.Add($"{car.Mass:0} kg: the most is {MaxWeight.Value:0} kg");
				}
			}
			if (MinKgPerHp.HasValue) {
				if (car.Hp <= 0 || car.Mass <= 0) {
					r.Unknown.Add("power to weight");
				} else if (Under(car.Mass / car.Hp, MinKgPerHp.Value, Leeway(2, MinKgPerHp.Value))) {
					r.Broken.Add($"{car.Mass / car.Hp:0.00} kg per hp: at least {MinKgPerHp.Value:0.00} kg per hp");
				}
			}
			if (MaxHpPerKg.HasValue) {
				if (car.Hp <= 0 || car.Mass <= 0) {
					r.Unknown.Add("power to weight");
				} else if (Over(car.Hp / car.Mass, MaxHpPerKg.Value, Leeway(3, MaxHpPerKg.Value))) {
					r.Broken.Add($"{car.Hp / car.Mass:0.000} hp per kg: the most is {MaxHpPerKg.Value:0.000} hp per kg");
				}
			}
			if (Spec.Length > 0) {
				if (car.Specs == null || car.KnownSpecs != null && !car.KnownSpecs.Any(s => string.Equals(s, Spec, StringComparison.OrdinalIgnoreCase))) {
					r.Unknown.Add("spec");
				} else if (!car.Specs.Any(s => string.Equals(s, Spec, StringComparison.OrdinalIgnoreCase))) {
					r.Broken.Add($"Not on the {Spec} spec: put on its tune for this car (Specs), or install the spec if you don't have it");
				}
			}
			if (!EngineSwaps) {
				if (car.EngineSwap == null) {
					r.Unknown.Add("engine swap");
				} else if (car.EngineSwap.Value) {
					r.Broken.Add("CarX engine swaps aren't allowed");
				}
			}
			if (!DrivetrainChange) {
				if (car.DrivetrainChanged == null) {
					r.Unknown.Add("drivetrain");
				} else if (car.DrivetrainChanged.Value) {
					r.Broken.Add("Drivetrain changes (CarX's drivetrain setting) aren't allowed");
				}
			}
			if (!OutsideSwaps || !OutsideWeight || !OutsideSuspension) {
				if (car.OutsideChanges == null) {
					r.Unknown.Add("changes outside CarX's tuning (KINO)");
				} else {
					var kinds = car.OutsideChanges.Select(KindOf).ToList();
					if (!OutsideSwaps && kinds.Contains(OutsideKind.Engine)) {
						r.Broken.Add("Engine, gearbox or drivetrain changed outside CarX's tuning (a KINO swap): not allowed");
					}
					if (!OutsideWeight && kinds.Contains(OutsideKind.Weight)) {
						r.Broken.Add("Weight or balance changed outside CarX's tuning (KINO's weight menu): not allowed");
					}
					if (!OutsideSuspension && kinds.Contains(OutsideKind.Suspension)) {
						r.Broken.Add("Suspension changed outside CarX's tuning (KINO's air suspension): not allowed");
					}
				}
			}
			if (!Widebody) {
				if (car.Stance == null) {
					r.Unknown.Add("body kit");
				} else if (car.Stance.Count > 0) {
					r.Broken.Add("Body kits that move the wheels (widebody) aren't allowed");
				}
			}
			if (DiffTypes.Count > 0) {
				if (!car.Numbers.TryGetValue(DiffTypeKey, out float diff)) {
					r.Unknown.Add("diff type");
				} else if (!DiffTypes.Contains((int)Math.Round(diff))) {
					r.Broken.Add($"Diff type {DiffTypeName((int)Math.Round(diff))} isn't allowed (only {string.Join(", ", DiffTypes.OrderBy(d => d).Select(DiffTypeName))})");
				}
			}
			foreach (ValueLimit limit in Limits) {
				if (!car.Numbers.TryGetValue(limit.Key, out float value)) {
					r.Unknown.Add(TuneLabels.Label(limit.Key).ToLowerInvariant());
				} else if (limit.Max.HasValue && Over(value, limit.Max.Value, Leeway(DecimalsFor(car, limit.Key), limit.Max.Value))) {
					r.Broken.Add($"{TuneLabels.Label(limit.Key)} {Shown(car, limit.Key, value)}: the most is {Number(limit.Max.Value)}");
				} else if (limit.Min.HasValue && Under(value, limit.Min.Value, Leeway(DecimalsFor(car, limit.Key), limit.Min.Value))) {
					r.Broken.Add($"{TuneLabels.Label(limit.Key)} {Shown(car, limit.Key, value)}: the least is {Number(limit.Min.Value)}");
				}
			}
			TyreBracket bracket = BracketFor(car.Mass);
			if (Tyres.Count > 0 && car.Mass <= 0) {
				r.Unknown.Add("weight class tyres");
			} else if (bracket != null) {
				foreach (string key in new[] { "front_tyre_width", "rear_tyre_width" }) {
					if (!car.Numbers.TryGetValue(key, out float width)) {
						r.Unknown.Add(TuneLabels.Label(key).ToLowerInvariant());
					} else if (Over(width, bracket.MaxWidth, Leeway(DecimalsFor(car, key), bracket.MaxWidth))) {
						r.Broken.Add($"{TuneLabels.Label(key)} {Shown(car, key, width)}: {car.Mass:0} kg cars may run up to {Number(bracket.MaxWidth)}");
					}
				}
			}
			return r;
		}

		/// <summary>A value the way CarX shows it (10.3 with no decimals: "10.3, shown as 10"), so a player sees why it's out.</summary>
		private static string Shown(CarFacts car, string key, float value) {
			if (key == DiffTypeKey) {
				return DiffTypeName((int)Math.Round(value));
			}
			int? decimals = DecimalsFor(car, key);
			if (!decimals.HasValue) {
				return Number(value);
			}
			string shown = value.ToString("F" + decimals.Value, CultureInfo.InvariantCulture);
			string real = Number(value);
			return real == shown ? shown : $"{real} (shows as {shown})";
		}

		/// <summary>The weight class a car is in: the first one it's under, or the heaviest; null without any.</summary>
		public TyreBracket BracketFor(float mass) {
			if (Tyres.Count == 0 || mass <= 0) {
				return null;
			}
			List<TyreBracket> sorted = Tyres.OrderBy(t => t.UpToKg).ToList();
			return sorted.FirstOrDefault(t => mass <= t.UpToKg + 0.5f) ?? sorted[sorted.Count - 1];
		}

		public enum OutsideKind { Engine, Weight, Suspension }

		/// <summary>What a value changed outside CarX's tuning is ("Live Front spring stiffness: ..." is suspension).</summary>
		public static OutsideKind KindOf(string difference) {
			string name = (difference ?? "").StartsWith("Live ", StringComparison.Ordinal) ? difference.Substring(5) : difference ?? "";
			string[] weight = { "Weight", "Centre of gravity", "Front balance from", "Side balance from", "Body inertia" };
			if (weight.Any(w => name.StartsWith(w, StringComparison.OrdinalIgnoreCase))) {
				return OutsideKind.Weight;
			}
			if ((name.StartsWith("Front ", StringComparison.Ordinal) || name.StartsWith("Rear ", StringComparison.Ordinal)) &&
				(name.Contains("spring") || name.Contains("bump") || name.Contains("rebound"))) {
				return OutsideKind.Suspension;
			}
			return OutsideKind.Engine;
		}

		/// <summary>The rules in a few words, for lists and lobbies.</summary>
		public string Summary() {
			var parts = new List<string>();
			if (Spec.Length > 0) {
				parts.Add($"{Spec} spec");
			}
			if (DiffTypes.Count > 0) {
				parts.Add($"{string.Join("/", DiffTypes.OrderBy(d => d).Select(DiffTypeName))} diff");
			}
			if (Setups.Count > 0) {
				parts.Add(string.Join("/", Setups.Select(SetupLabel)));
			}
			if (MaxHp.HasValue) {
				parts.Add($"≤{MaxHp.Value:0} hp");
			}
			if (MinKgPerHp.HasValue) {
				parts.Add($"≥{MinKgPerHp.Value:0.##} kg/hp");
			}
			if (MaxHpPerKg.HasValue) {
				parts.Add($"≤{MaxHpPerKg.Value:0.###} hp/kg");
			}
			if (MinWeight.HasValue) {
				parts.Add($"≥{MinWeight.Value:0} kg");
			}
			if (MaxWeight.HasValue) {
				parts.Add($"≤{MaxWeight.Value:0} kg");
			}
			if (Cars.Count > 0) {
				parts.Add($"{Cars.Count} car(s)");
			}
			var banned = new List<string>();
			if (!EngineSwaps) banned.Add("engine swaps");
			if (!DrivetrainChange) banned.Add("drivetrain changes");
			if (!OutsideSwaps) banned.Add("KINO swaps");
			if (!OutsideWeight) banned.Add("KINO weight");
			if (!OutsideSuspension) banned.Add("KINO air");
			if (!Widebody) banned.Add("widebody");
			if (banned.Count > 0) {
				parts.Add("no " + string.Join(", ", banned));
			}
			if (Limits.Count > 0) {
				parts.Add($"{Limits.Count} tune limit(s)");
			}
			if (Tyres.Count > 0) {
				parts.Add($"{Tyres.Count} weight class(es)");
			}
			return parts.Count == 0 ? "no rules yet" : string.Join(" · ", parts);
		}
	}
}
