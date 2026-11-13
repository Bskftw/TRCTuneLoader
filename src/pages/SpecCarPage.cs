// Specs > Check my car: your car against your specs and regulations.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
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
					mySpecsUntil_ = 0; // the newly shown spec gets its full check now
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
	}
}
