// Player specs checked like TRC spec: your car against each spec with a tune for it, and other players against this PC's specs and regulations.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Specs and regulations checked like TRC spec (bskftw, Nov 3: "specs dont have a way to check if they are proper
	/// spec like trc does... the my car tab [should] tell me if someone is on a spec tune, a regulated rule list, or trc
	/// spec... displaying to all other tune loaders... and if someone is cheating").
	///
	/// The player's own car gets the same full check as TRC spec against each spec that has a tune for it (saved setup,
	/// live car, stance, engine and drivetrain), and is checked against every regulation; My car shows all of it. Other
	/// players are checked here from what CarX shares of their car, against this PC's specs and regulations, so it doesn't
	/// rest on what their mod says; their mod's own claims are shown next to it, and a claim this check disagrees with is
	/// flagged.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		/// <summary>The last full check of the car against each spec with a tune for it, by title (made with <see cref="MySpecs"/>).</summary>
		private readonly Dictionary<string, OwnCheck> mySpecChecks_ = new Dictionary<string, OwnCheck>(StringComparer.OrdinalIgnoreCase);

		/// <summary>On a spec the way TRC spec counts it: its tune saved, stock stance, no drivetrain changes, the live car matching.</summary>
		private static bool FullyOn(OwnCheck check) =>
			check?.Saved != null && check.Saved.Matches && check.StanceDifferences.Count == 0 && !check.DrivetrainChanged && check.LiveDifferences.Count == 0;

		/// <summary>The titles of the player specs the car is fully on (see <see cref="FullyOn"/>), checked at most every 2 s.</summary>
		private List<string> MySpecs() {
			if (Time.unscaledTime < mySpecsUntil_) {
				return mySpecs_;
			}
			mySpecsUntil_ = Time.unscaledTime + 2f;
			var found = new List<string>();
			mySpecChecks_.Clear();
			try {
				if (specs_.Count > 0 && RefreshCar()) {
					// The saved setup is read once and compared with each spec's tune first (cheap). Only a spec whose tune is
					// saved (it could be fully on) and the one shown on Check my car get the full check: live car, stance and
					// drivetrain (bskftw, Nov 8: Check my car took 31 ms a frame; every spec got the full check every 2 s).
					Dictionary<string, float> saved = null;
					Dictionary<string, float> defaults = null;
					if (adapter_.TryReadCurrentTune(car_, out Dictionary<string, float> current, out _) && adapter_.TryGetDefaults(car_.SlotId, out var slotDefaults, out _)) {
						saved = current;
						defaults = slotDefaults;
					}
					int slotId = car_.SlotId;
					foreach (PlayerSpec spec in specs_) {
						TuneFile tune = spec.For(car_.CarId, car_.SlotId);
						if (tune == null) {
							continue;
						}
						if (saved != null && spec.Title != checkSpec_) {
							TuneCheck.Result quick = TuneCheck.Compare(car_.CarIdText, car_.SlotIdText, tune.Fingerprint, tune.Values, saved, defaults,
								(key, value) => DescribeValue(slotId, key, value));
							if (!quick.Matches) {
								mySpecChecks_[spec.Title] = new OwnCheck { Official = tune, Saved = quick };
								continue;
							}
						}
						OwnCheck check = CheckCarAgainst(new List<TuneFile> { tune }, "", recordInGarage: false, noTune: "");
						mySpecChecks_[spec.Title] = check;
						if (FullyOn(check)) {
							found.Add(spec.Title);
						}
					}
				}
			} catch (Exception e) {
				TrcLog.Warning($"Spec check: {e.Message}");
			}
			return mySpecs_ = found;
		}

		/// <summary>The spec tune (any spec) these saved values match on the player's car, or null: its live reference is recorded like TRC's.</summary>
		private TuneFile MatchingSpecTune(IDictionary<string, float> values) {
			if (specs_.Count == 0 || !adapter_.TryGetDefaults(car_.SlotId, out var defaults, out _)) {
				return null;
			}
			return specs_.Select(s => s.For(car_.CarId, car_.SlotId)).FirstOrDefault(t => t != null &&
				TuneCheck.Compare(car_.CarIdText, car_.SlotIdText, t.Fingerprint, t.Values, values, defaults).Matches);
		}

		/// <summary>What one spec check says, in a few words, and how it looks.</summary>
		private static (string Text, GUIStyle Pill, string Hex) SpecVerdict(string title, OwnCheck check) {
			if (check == null || check.Saved == null) {
				return ($"{title}: couldn't be checked{(check != null && check.Error.Length > 0 ? $" ({check.Error})" : "")}", Theme.PillNeutral, Theme.MutedHex);
			}
			if (!check.Saved.Matches) {
				return ($"Not {title}: {check.Saved.Differences.Count} difference(s) from its tune", Theme.PillBad, Theme.BadHex);
			}
			if (check.StanceDifferences.Count > 0) {
				return ($"Not {title}: its tune is saved, but the stance isn't stock", Theme.PillBad, Theme.BadHex);
			}
			if (check.DrivetrainChanged) {
				return ($"Not {title}: its tune is saved, but the engine or drivetrain is changed", Theme.PillBad, Theme.BadHex);
			}
			if (check.LiveDifferences.Count > 0) {
				return ($"Not {title}: saved, but the live car differs in {check.LiveDifferences.Count} place(s)", Theme.PillBad, Theme.BadHex);
			}
			if (!check.LiveChecked) {
				return ($"{title} saved; the live car is checked a few seconds after it reaches the garage or the track", Theme.PillWarn, Theme.WarnHex);
			}
			return ($"{title}: saved setup and live car", Theme.PillGood, Theme.GoodHex);
		}

		/// <summary>The differences a spec check found, for a list.</summary>
		private static List<string> SpecDifferences(OwnCheck check) {
			var lines = new List<string>();
			if (check?.Saved == null) {
				return lines;
			}
			lines.AddRange(check.Saved.Differences);
			lines.AddRange(check.StanceDifferences);
			if (check.Drivetrain != null) {
				lines.AddRange(check.Drivetrain.Differences);
			}
			lines.AddRange(check.LiveDifferences);
			return lines;
		}

		// ---- Other players: checked here against this PC's specs and regulations ----

		/// <summary>Fills a player's row with this PC's specs (from their shared setup) and regulations (from their car as CarX shares it).</summary>
		private void CheckPlayerRules(PlayerRow row) {
			RoomPlayer player = row.Player;
			row.Specs.Clear();
			row.Regulations.Clear();
			try {
				if (specs_.Count > 0 && adapter_.TryGetDefaults(player.SlotId, out var defaults, out _)) {
					int slotId = player.SlotId;
					foreach (PlayerSpec spec in specs_) {
						TuneFile tune = spec.For(player.CarId, player.SlotId);
						if (tune == null) {
							continue;
						}
						TuneCheck.Result result = TuneCheck.Compare(player.CarIdText, player.SlotIdText, tune.Fingerprint, tune.Values, player.SharedTune, defaults,
							(key, value) => DescribeValue(slotId, key, value));
						row.Specs.Add((spec, result, result.Matches && player.EngineId <= 0 && !CarNotSpec(player)));
					}
				}
				foreach (Regulation regulation in regulations_) {
					row.Regulations.Add((regulation, regulation.Check(FactsFor(player))));
				}
			} catch (Exception e) {
				TrcLog.Warning($"Checking {player.Name} against your specs and regulations: {e.Message}");
			}
		}

		/// <summary>The titles of this PC's specs a player's car is on, from their shared setup (for regulations that need a spec).</summary>
		private List<string> PlayerSpecsOn(RoomPlayer player) {
			var on = new List<string>();
			if (specs_.Count == 0 || !adapter_.TryGetDefaults(player.SlotId, out var defaults, out _)) {
				return on;
			}
			foreach (PlayerSpec spec in specs_) {
				TuneFile tune = spec.For(player.CarId, player.SlotId);
				if (tune != null && player.EngineId <= 0 && !CarNotSpec(player) &&
					TuneCheck.Compare(player.CarIdText, player.SlotIdText, tune.Fingerprint, tune.Values, player.SharedTune, defaults).Matches) {
					on.Add(spec.Title);
				}
			}
			return on;
		}

		/// <summary>A player's row in the list: the specs they're on, as checked here (one line, or "").</summary>
		private static string RulesRowText(PlayerRow row) {
			List<string> on = row.Specs.Where(s => s.On).Select(s => s.Spec.Title).ToList();
			if (on.Count == 0 && row.Report != null) {
				// Not checkable here (a spec this PC doesn't have): what their mod says, marked as theirs.
				on = row.Report.Specs.Select(s => $"{s} (says)").ToList();
			}
			return on.Count == 0 ? "" : $"<color={SpecsTextHex}>Spec: {string.Join(", ", on)}</color>";
		}

		private static string SpecsTextHex => partHex_[1];

		/// <summary>The Players page details: each of this PC's specs and regulations against the player, and what their mod claims.</summary>
		private List<string> RulesDetailLines(PlayerRow row, Func<string, string> show) {
			var lines = new List<string>();
			TrcSync.Report report = row.Report;
			if (row.Specs.Count == 0 && row.Regulations.Count == 0 && (report == null || report.Specs.Count == 0 && report.Regulations.Count == 0)) {
				return lines;
			}
			lines.Add("--- Specs and regulations ---");
			foreach (var (spec, result, on) in row.Specs) {
				lines.Add(on ? $"<color={Theme.GoodHex}>On {spec.Title}</color> (checked here from their shared setup)"
					: result.Matches ? $"<color={Theme.BadHex}>Not {spec.Title}: its tune, but the stance, engine or drivetrain is changed</color>"
					: $"<color={Theme.BadHex}>Not {spec.Title}: {result.Differences.Count} difference(s) from its tune</color>");
				if (!on && !result.Matches) {
					lines.AddRange(result.Differences.Take(8).Select(show).Select(l => $"   {l}"));
				}
			}
			foreach (var (regulation, result) in row.Regulations) {
				lines.Add(!result.Passes ? $"<color={Theme.BadHex}>Breaks {regulation.Name}: {string.Join("; ", result.Broken.Take(3))}</color>"
					: result.Unknown.Count > 0 ? $"<color={Theme.WarnHex}>Meets {regulation.Name} as far as can be told here (not known: {string.Join(", ", result.Unknown)})</color>"
					: $"<color={Theme.GoodHex}>Meets {regulation.Name}</color>");
			}
			if (report != null && (report.Specs.Count > 0 || report.Regulations.Count > 0)) {
				var claims = new List<string>();
				claims.AddRange(report.Specs.Select(s => $"on {s}"));
				claims.AddRange(report.Regulations.Select(r => $"{(r.Ok ? "meets" : "breaks")} {r.Name}"));
				lines.Add($"Their mod says: {string.Join(", ", claims)} (self-reported)");
				// A claim this PC can check, and doesn't agree with: flagged, like a modified TRC setup.
				foreach (string title in report.Specs) {
					var mine = row.Specs.FirstOrDefault(s => string.Equals(s.Spec.Title, title, StringComparison.OrdinalIgnoreCase));
					if (mine.Spec == null) {
						lines.Add($"<color={Theme.MutedHex}>You don't have {title}, so it can't be checked here (install it from Specs).</color>");
					} else if (!mine.On) {
						lines.Add($"<color={Theme.BadHex}>Says it's on {title}, but its car isn't, as checked here.</color>");
					}
				}
				foreach (TrcSync.RuleClaim claim in report.Regulations.Where(c => c.Ok)) {
					var mine = row.Regulations.FirstOrDefault(r => r.Regulation.Code == claim.Code);
					if (mine.Regulation != null && !mine.Result.Passes) {
						lines.Add($"<color={Theme.BadHex}>Says it meets {claim.Name}, but it doesn't, as checked here.</color>");
					}
				}
			}
			return lines;
		}

		/// <summary>True when something this PC checked disagrees with what the player's mod claims (shown red on the Players page).</summary>
		private static bool RulesClaimWrong(PlayerRow row) {
			TrcSync.Report report = row.Report;
			if (report == null) {
				return false;
			}
			bool spec = report.Specs.Any(title => row.Specs.Any(s => string.Equals(s.Spec.Title, title, StringComparison.OrdinalIgnoreCase) && !s.On));
			bool rule = report.Regulations.Any(c => c.Ok && row.Regulations.Any(r => r.Regulation.Code == c.Code && !r.Result.Passes));
			return spec || rule;
		}
	}
}
