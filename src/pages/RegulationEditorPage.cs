// Making or editing a regulation: its name, setups, cars, power and weight, and what's allowed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		// The editor: a copy of the regulation, and the text in its number fields.
		private Regulation editingRegulation_;
		private readonly Dictionary<string, string> regulationFields_ = new Dictionary<string, string>(StringComparer.Ordinal);
		private string limitGroup_ = "";

		private void StartEditingRegulation(Regulation r) {
			editingRegulation_ = r.Copy();
			editingRegulation_.FilePath = r.FilePath;
			regulationFields_.Clear();
			string Num(float? v) => v.HasValue ? Regulation.Number(v.Value) : "";
			regulationFields_["max_hp"] = Num(r.MaxHp);
			regulationFields_["min_weight"] = Num(r.MinWeight);
			regulationFields_["max_weight"] = Num(r.MaxWeight);
			regulationFields_["min_kg_per_hp"] = Num(r.MinKgPerHp);
			regulationFields_["max_hp_per_kg"] = Num(r.MaxHpPerKg);
			foreach (ValueLimit l in editingRegulation_.Limits) {
				regulationFields_["min:" + l.Key] = Num(l.Min);
				regulationFields_["max:" + l.Key] = Num(l.Max);
			}
			for (int i = 0; i < editingRegulation_.Tyres.Count; i++) {
				regulationFields_[$"tyre_kg:{i}"] = Regulation.Number(editingRegulation_.Tyres[i].UpToKg);
				regulationFields_[$"tyre_mm:{i}"] = Regulation.Number(editingRegulation_.Tyres[i].MaxWidth);
			}
		}

		/// <summary>A number box; empty means no limit. Returns the number in it, or null.</summary>
		private float? NumberField(string key, float width = 80f) {
			regulationFields_.TryGetValue(key, out string text);
			text = GUILayout.TextField(text ?? "", 10, Theme.Field, GUILayout.Width(width));
			regulationFields_[key] = text;
			return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && !float.IsNaN(v) && !float.IsInfinity(v) ? v : (float?)null;
		}

		private void DrawRegulationEditor() {
			Regulation r = editingRegulation_;
			if (r == null) {
				return;
			}
			CarFacts mine = MyCarFacts();
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Name", Theme.SectionLabel);
			r.Name = GUILayout.TextField(r.Name ?? "", 60, Theme.Field);
			GUILayout.Label("Made by", Theme.SectionLabel);
			r.Author = GUILayout.TextField(r.Author ?? "", 60, Theme.Field);
			GUILayout.Label("Notes (what it's for, where it's raced)", Theme.SectionLabel);
			r.Notes = GUILayout.TextField(r.Notes ?? "", 400, Theme.Field);
			GUILayout.EndVertical();

			GUILayout.Label("Setups allowed (none picked: any)", Theme.SectionLabel);
			GUILayout.BeginHorizontal();
			for (int i = 0; i < Regulation.SetupNames.Length; i++) {
				if (i == 3) {
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
					GUILayout.BeginHorizontal();
				}
				string setup = Regulation.SetupNames[i];
				bool on = r.Setups.Contains(setup);
				if (NavButton(Regulation.SetupLabels[i], on ? Theme.ChipActive : Theme.Chip)) {
					if (on) {
						r.Setups.Remove(setup);
					} else {
						r.Setups.Add(setup);
					}
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Label("Cars allowed (none: any car)", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			foreach (int car in r.Cars.ToList()) {
				GUILayout.BeginHorizontal();
				string name = r.CarNames.TryGetValue(car, out string n) ? n : adapter_.TryGetCarName(car, out string carName) ? carName : $"car {car}";
				GUILayout.Label(name, Theme.Body);
				GUILayout.FlexibleSpace();
				if (NavButton("Remove", Theme.Button)) {
					r.Cars.Remove(car);
					r.CarNames.Remove(car);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && mine.CarId > 0 && !r.Cars.Contains(mine.CarId);
			if (NavButton(mine.CarId > 0 ? $"Add my {mine.CarName}" : "Add my car", Theme.Button)) {
				r.Cars.Add(mine.CarId);
				r.CarNames[mine.CarId] = mine.CarName;
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();

			GUILayout.Label("Power and weight (empty: no limit)", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.BeginHorizontal();
			GUILayout.Label("Most power (hp)", Theme.Body, GUILayout.Width(200));
			r.MaxHp = NumberField("max_hp");
			GUILayout.Label(mine.Hp > 0 ? $"<color={Theme.MutedHex}>yours {mine.Hp:0}</color>" : "", Theme.Small);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			GUILayout.Label("Weight (kg), least and most", Theme.Body, GUILayout.Width(200));
			r.MinWeight = NumberField("min_weight");
			r.MaxWeight = NumberField("max_weight");
			GUILayout.Label(mine.Mass > 0 ? $"<color={Theme.MutedHex}>yours {mine.Mass:0}</color>" : "", Theme.Small);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			GUILayout.Label("Power to weight: most hp per kg", Theme.Body, GUILayout.Width(200));
			r.MaxHpPerKg = NumberField("max_hp_per_kg");
			GUILayout.Label(mine.Mass > 0 && mine.Hp > 0 ? $"<color={Theme.MutedHex}>yours {mine.Hp / mine.Mass:0.000} (e.g. 0.22: no more than 0.22 hp per kg)</color>" : "<color=" + Theme.MutedHex + ">e.g. 0.22: no more than 0.22 hp per kg</color>", Theme.Small);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (r.MinKgPerHp.HasValue) {
				// Regulations made before Nov 3 used kg per hp (a least); kept so they still check the same.
				GUILayout.BeginHorizontal();
				GUILayout.Label("Older: least kg per hp", Theme.Body, GUILayout.Width(200));
				r.MinKgPerHp = NumberField("min_kg_per_hp");
				GUILayout.Label($"<color={Theme.MutedHex}>clear it to use hp per kg only</color>", Theme.Small);
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();

			GUILayout.Label("Spec", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			var specChoices = new List<string> { "Any tune" };
			specChoices.AddRange(specs_.Select(s => s.Title));
			if (r.Spec.Length > 0 && !specChoices.Contains(r.Spec)) {
				specChoices.Add(r.Spec);
			}
			int specPick = Choice(specChoices.ToArray(), r.Spec.Length == 0 ? 0 : specChoices.IndexOf(r.Spec), 3);
			r.Spec = specPick <= 0 ? "" : specChoices[specPick];
			GUILayout.Label(r.Spec.Length == 0
				? "Or make every driver run one of your specs' tunes (Specs): spec racing with your own tunes."
				: $"Every driver must run the {r.Spec} spec's tune for their car, so they need the spec installed (share it from Specs).", Theme.Small);
			GUILayout.EndVertical();

			GUILayout.Label("Allowed", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			r.EngineSwaps = AllowRow("CarX engine swaps", r.EngineSwaps);
			r.DrivetrainChange = AllowRow("Drivetrain changes (CarX's drivetrain setting, like RWD to AWD)", r.DrivetrainChange);
			r.OutsideSwaps = AllowRow("KINO engine, gearbox or drivetrain swaps", r.OutsideSwaps);
			r.OutsideWeight = AllowRow("KINO weight reduction and distribution", r.OutsideWeight);
			r.OutsideSuspension = AllowRow("KINO air suspension and hydraulics", r.OutsideSuspension);
			r.Widebody = AllowRow("Widebody kits (body parts that move the wheels)", r.Widebody);
			GUILayout.EndVertical();

			DrawLimitsEditor(r, mine);
			DrawTyreTableEditor(r);

			GUILayout.BeginHorizontal();
			GUI.enabled = enabled && r.Name.Trim().Length > 0;
			if (NavButton(r.FilePath.Length > 0 ? "Save changes" : "Save", Theme.ButtonPrimary)) {
				r.Name = r.Name.Trim();
				string code = r.Code;
				SaveRegulation(r, $"Saved {r.Name} ({code}).");
				editingRegulation_ = null;
				regulationsView_ = 0;
			}
			GUI.enabled = enabled;
			if (NavButton("Cancel", Theme.Button)) {
				editingRegulation_ = null;
				regulationsView_ = 0;
			}
			GUILayout.FlexibleSpace();
			GUILayout.Label($"<color={Theme.MutedHex}>code {r.Code}</color>", Theme.Small);
			GUILayout.EndHorizontal();
			if (r.Name.Trim().Length == 0) {
				GUILayout.Label("Give it a name to save it.", Theme.Small);
			}
		}

		/// <summary>One rule that's allowed or not: a button that switches it, then what it is (so a long name never pushes the row wide).</summary>
		private static bool AllowRow(string what, bool allowed) {
			GUILayout.BeginHorizontal();
			if (NavButton(allowed ? "Allowed" : "Not allowed", allowed ? Theme.ChipActive : Theme.Chip, GUILayout.Width(120))) {
				allowed = !allowed;
			}
			GUILayout.Label(what, Theme.Body);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			return allowed;
		}
	}
}
