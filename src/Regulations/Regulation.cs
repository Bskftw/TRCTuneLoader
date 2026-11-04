using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>A tune value's allowed range, in the numbers CarX's tuning screen shows (mm, Nm, degrees...). Either end may be open.</summary>
	public sealed class ValueLimit {
		public string Key = "";
		public float? Min;
		public float? Max;
	}

	/// <summary>A weight class's widest tyres: cars up to <see cref="UpToKg"/> may run tyres up to <see cref="MaxWidth"/> mm.</summary>
	public sealed class TyreBracket {
		public float UpToKg;
		public float MaxWidth;
	}

	/// <summary>
	/// What a regulation is checked against: a car as CarX built it, the player's own or someone else's in the room.
	/// Anything the mod couldn't find out is left null (or 0), and shows as "couldn't check" instead of a broken rule.
	/// </summary>
	public sealed class CarFacts {
		public int CarId;
		public string CarName = "";

		/// <summary>CarX's name for the setup: Stock, Turbo, Racing, Drift, Ultimate or UltimatePro.</summary>
		public string Setup = "";

		/// <summary>Every tune setting in the numbers CarX's tuning screen shows, by setting name.</summary>
		public Dictionary<string, float> Numbers = new Dictionary<string, float>(StringComparer.Ordinal);

		/// <summary>How many decimals CarX shows each setting with (missing: not known).</summary>
		public Dictionary<string, int> Decimals = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Peak power (hp) and weight (kg) of the car as built; 0 when unknown.</summary>
		public float Hp;
		public float Mass;

		public bool? EngineSwap;

		/// <summary>CarX's drivetrain setting makes it drive other wheels than its own (RWD to AWD...).</summary>
		public bool? DrivetrainChanged;

		/// <summary>Engine, gearbox, suspension and weight values changed outside CarX's tuning (KINO), null when not compared.</summary>
		public List<string> OutsideChanges;

		/// <summary>How the body moves the wheels (widebody kits, Workshop bodies), null when unknown.</summary>
		public List<string> Stance;

		/// <summary>The player specs (their titles) whose tune is on the car, null when unknown.</summary>
		public List<string> Specs;

		/// <summary>The specs whoever is checking has (titles): a spec not among them can't be checked here. Null: all of them.</summary>
		public List<string> KnownSpecs;
	}

	/// <summary>What a car does against a regulation: the rules it breaks, and the ones that couldn't be checked.</summary>
	public sealed class RegulationResult {
		public List<string> Broken = new List<string>();
		public List<string> Unknown = new List<string>();
		public bool Passes => Broken.Count == 0;
	}

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
	public sealed class Regulation {
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

		// ---- The file ----

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

		// ---- The folder (Regulations/*.trcreg) ----

		public static List<Regulation> LoadAll(string folder) {
			var list = new List<Regulation>();
			if (!Directory.Exists(folder)) {
				return list;
			}
			foreach (string path in Directory.GetFiles(folder, "*" + Extension).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
				try {
					Regulation reg = Parse(File.ReadAllText(path, Encoding.UTF8));
					if (reg != null) {
						reg.FilePath = path;
						list.Add(reg);
					}
				} catch (Exception) {
					// A file that can't be read is left out.
				}
			}
			return list;
		}

		public void Save(string folder) {
			Directory.CreateDirectory(folder);
			if (FilePath.Length == 0 || !FilePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) {
				string baseName = new string((Name.Length > 0 ? Name : "regulation").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
				string path = Path.Combine(folder, baseName + Extension);
				for (int i = 2; File.Exists(path); i++) {
					path = Path.Combine(folder, $"{baseName} {i}{Extension}");
				}
				FilePath = path;
			}
			File.WriteAllText(FilePath, ToText(), new UTF8Encoding(false));
		}
	}
}
