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
					foreach (PlayerSpec spec in specs_) {
						TuneFile tune = spec.For(car_.CarId, car_.SlotId);
						if (tune == null) {
							continue;
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

		// ---- Specs, Check my car: your car against your specs and regulations ----

		private string checkSpec_ = "";
		private string checkRule_ = "";
		private Vector2 specCarScroll_;

		/// <summary>
		/// Specs > Check my car, like TRC Tunes but for your specs and regulations only (bskftw, Nov 3: "an entirely
		/// separate category under specs... strictly for checking specs and regulations"): pick a spec to see whether the
		/// car is on it and what's different, and each regulation, met or not and why.
		/// </summary>
		private void DrawSpecCarPage() {
			RefreshCarIfDue();
			if (car_ == null) {
				GUILayout.Label(carError_, Theme.PillNeutral);
				return;
			}
			specCarScroll_ = GUILayout.BeginScrollView(specCarScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			GUILayout.Label(car_.CarName, Theme.Heading);
			string where = car_.InDyno ? "in CarX's tuning screen" : car_.InGarage ? "in the garage" : "on track";
			GUILayout.Label($"{car_.SlotName} setup · {where}", Theme.Small);
			if (specResult_.Length > 0) {
				GUILayout.Label(specResult_, Theme.Body);
			}

			List<string> on = MySpecs();
			GUILayout.Label("Specs", Theme.SectionLabel);
			List<PlayerSpec> forCar = specs_.Where(s => s.For(car_.CarId, car_.SlotId) != null).ToList();
			if (forCar.Count == 0) {
				GUILayout.Label(specs_.Count == 0
					? "You have no specs yet: make one or install one in My specs."
					: $"None of your specs has a tune for the {car_.CarName} ({car_.SlotName}). Save one in My specs.", Theme.PillNeutral);
			} else {
				if (forCar.All(s => s.Title != checkSpec_)) {
					checkSpec_ = (forCar.FirstOrDefault(s => on.Contains(s.Title)) ?? forCar[0]).Title;
				}
				int picked = forCar.FindIndex(s => s.Title == checkSpec_);
				int choice = Choice(forCar.Select(s => on.Contains(s.Title) ? $"{s.Title} (on)" : s.Title).ToArray(), picked, 4);
				if (choice != picked && choice >= 0) {
					checkSpec_ = forCar[choice].Title;
				}
				PlayerSpec spec = forCar.First(s => s.Title == checkSpec_);
				TuneFile tune = spec.For(car_.CarId, car_.SlotId);
				mySpecChecks_.TryGetValue(spec.Title, out OwnCheck check);
				var (text, pill, _) = SpecVerdict(spec.Title, check);
				GUILayout.Label(text, pill);
				if (check?.Drivetrain != null) {
					GUILayout.Label(check.Drivetrain.Summary(), Theme.Small);
				}
				GUILayout.BeginHorizontal();
				if (NavButton(car_.InDyno ? $"Load {spec.Title} into the dyno" : $"Put {spec.Title} on", Theme.ButtonPrimary)) {
					ApplySpecTune(spec, tune);
				}
				if (NavButton("Check again", Theme.Button)) {
					mySpecsUntil_ = 0;
					specResult_ = "";
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.Label($"Spec code {tune.Fingerprint} · checked like TRC spec: the saved setup, the live car, the stance, and engine or drivetrain swaps", Theme.Small);
				List<string> different = SpecDifferences(check);
				if (different.Count > 0) {
					GUILayout.BeginVertical(Theme.Card);
					foreach (string line in different) {
						GUILayout.Label(line, Theme.Body);
					}
					GUILayout.EndVertical();
				}
			}

			GUILayout.Space(8);
			GUILayout.Label("Regulations", Theme.SectionLabel);
			if (regulations_.Count == 0) {
				GUILayout.Label("You have no regulations yet: make one or import one in Regulations.", Theme.PillNeutral);
			} else {
				CarFacts facts = MyCarFacts();
				var results = regulations_.Select(r => (Regulation: r, Result: r.Check(facts))).ToList();
				if (results.All(r => r.Regulation.Code != checkRule_)) {
					checkRule_ = results[0].Regulation.Code;
				}
				int picked = results.FindIndex(r => r.Regulation.Code == checkRule_);
				int choice = Choice(results.Select(r => $"{r.Regulation.Name} ({(r.Result.Passes ? "meets" : "breaks")})").ToArray(), picked, 4);
				if (choice != picked && choice >= 0) {
					checkRule_ = results[choice].Regulation.Code;
				}
				var (regulation, result) = results.First(r => r.Regulation.Code == checkRule_);
				GUILayout.Label(!result.Passes ? $"Breaks {regulation.Name}" : result.Unknown.Count > 0 ? $"Meets {regulation.Name} as far as can be told" : $"Meets {regulation.Name}",
					!result.Passes ? Theme.PillBad : result.Unknown.Count > 0 ? Theme.PillWarn : Theme.PillGood);
				GUILayout.Label(regulation.Summary(), Theme.Small);
				if (result.Broken.Count > 0 || result.Unknown.Count > 0) {
					GUILayout.BeginVertical(Theme.Card);
					foreach (string line in result.Broken) {
						GUILayout.Label(line, Theme.Body);
					}
					if (result.Unknown.Count > 0) {
						GUILayout.Label($"Couldn't be checked: {string.Join(", ", result.Unknown)}", Theme.Small);
					}
					GUILayout.EndVertical();
				}
			}
			GUILayout.EndScrollView();
		}

		// ---- Specs, Players: the room against your specs and regulations ----

		private Vector2 specPlayersScroll_;
		private Vector2 specPlayerDetailScroll_;

		/// <summary>Something worth a look for specs and regulations: a claim that doesn't hold, or a regulation broken.</summary>
		private static bool RulesProblem(PlayerRow row) => RulesClaimWrong(row) || row.Regulations.Any(r => !r.Result.Passes);

		/// <summary>Specs > Players: every player, the spec they're on and the regulations they meet, checked here; their claims beside it.</summary>
		private void DrawSpecPlayersPage() {
			RefreshPlayersIfDue();
			GUILayout.Label("Checked here from what CarX shares of each car, against your specs and regulations, so it doesn't rest on what their mod says. " +
				"A spec you don't have can't be checked: install it from My specs.", Theme.Small);
			if (!string.IsNullOrEmpty(playersError_)) {
				GUILayout.Label(playersError_, Theme.PillNeutral);
				return;
			}
			List<PlayerRow> shown = problemsOnly_ ? players_.Where(RulesProblem).ToList() : players_;
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(290), GUILayout.ExpandHeight(true));
			specPlayersScroll_ = GUILayout.BeginScrollView(specPlayersScroll_);
			if (shown.Count == 0) {
				GUILayout.Label(problemsOnly_ ? "Nothing to look at: no broken regulations or false claims." : "Nobody in the room yet.", Theme.Small);
			}
			foreach (PlayerRow row in shown) {
				bool selected = selectedPlayer_ != null && SamePlayer(row, selectedPlayer_);
				if (NavButton(RulesPlayerRowText(row), selected ? Theme.RowSelected : Theme.Row)) {
					selectedPlayer_ = row;
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			PlayerRow picked = selectedPlayer_ == null ? null : players_.FirstOrDefault(r => SamePlayer(r, selectedPlayer_));
			if (picked == null) {
				GUILayout.Label(selectedPlayer_ == null ? "Pick a player on the left." : "This player has left the room.", Theme.Small);
			} else {
				GUILayout.Label($"{picked.Player.Name}{(picked.Player.IsLocal ? " (you)" : "")}", Theme.Heading);
				GUILayout.Label($"{picked.Player.CarName} · {picked.Player.SlotName} setup", Theme.Small);
				bool values = picked.Player.IsLocal || picked.Report?.ShareValues == true;
				List<string> lines = RulesDetailLines(picked, values ? (Func<string, string>)(l => l) : TuneCheck.WithoutValues);
				if (lines.Count == 0) {
					lines.Add(specs_.Count == 0 && regulations_.Count == 0
						? "You have no specs or regulations to check against yet."
						: "None of your specs has a tune for their car, you have no regulations, and their mod didn't say it's on any.");
				}
				if (picked.Report == null && !picked.Player.IsLocal) {
					lines.Add($"<color={Theme.MutedHex}>No TRC Tune Loader report from them: only what's checked here shows.</color>");
				}
				DrawLines(ref specPlayerDetailScroll_, lines);
			}
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
			if (NavButton("Refresh", Theme.Button, GUILayout.Width(100))) {
				nextPlayersRefresh_ = 0;
				nextReportRequest_ = 0;
			}
		}

		/// <summary>A player in Specs > Players: name, car, the spec they're on, the regulations they meet, and any false claim.</summary>
		private static string RulesPlayerRowText(PlayerRow row) {
			string spec = RulesRowText(row);
			int meets = row.Regulations.Count(r => r.Result.Passes);
			string rules = row.Regulations.Count == 0 ? ""
				: $"\n<color={(meets == row.Regulations.Count ? Theme.GoodHex : Theme.BadHex)}>Meets {meets} of {row.Regulations.Count} regulation(s)</color>";
			string wrong = RulesClaimWrong(row) ? $"\n<color={Theme.BadHex}>Says it's on a spec or regulation it isn't</color>" : "";
			return $"<b>{row.Player.Name}{(row.Player.IsLocal ? " (you)" : "")}</b>\n" +
				$"<color={Theme.MutedHex}>{row.Player.CarName} ({row.Player.SlotName})</color>\n" +
				(spec.Length > 0 ? spec : $"<color={Theme.MutedHex}>No spec</color>") + rules + wrong;
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
