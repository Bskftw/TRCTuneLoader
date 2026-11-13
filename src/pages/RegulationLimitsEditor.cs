// The regulation editor's tables: limits on any tune value, and the tyre width for each weight class.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>Limits on any tune value, in CarX's own numbers, and a list of every setting to add one for.</summary>
		private void DrawLimitsEditor(Regulation r, CarFacts mine) {
			GUILayout.Label("Tune limits (in the numbers CarX's tuning screen shows; empty: no limit)", Theme.SectionLabel);
			GUILayout.Label("Checked the way CarX shows them: a tyre stretch of 10.3 shows as 10, so it counts as 10 (10.6 shows as 11). Power and weight are checked to the whole hp and kg: 600.4 hp is 600.", Theme.Small);
			GUILayout.BeginVertical(Theme.Card);
			if (r.Limits.Count == 0) {
				GUILayout.Label("None yet: add one below, like rear tyre width at most 285.", Theme.Small);
			}
			// An old least/most limit on the diff type becomes the named types in its range.
			ValueLimit oldDiff = r.Limits.FirstOrDefault(l => l.Key == Regulation.DiffTypeKey);
			if (oldDiff != null) {
				r.Limits.Remove(oldDiff);
				for (int type = 0; type < Regulation.DiffTypeNames.Length; type++) {
					if ((!oldDiff.Min.HasValue || type >= oldDiff.Min.Value - 0.5f) && (!oldDiff.Max.HasValue || type <= oldDiff.Max.Value + 0.5f) && !r.DiffTypes.Contains(type)) {
						r.DiffTypes.Add(type);
					}
				}
			}
			if (r.DiffTypes.Count > 0) {
				// Diff type: named types to allow, not a number (bskftw, Nov 3).
				GUILayout.BeginHorizontal();
				GUILayout.Label("Diff type allowed", Theme.Body, GUILayout.Width(200));
				for (int type = 0; type < Regulation.DiffTypeNames.Length; type++) {
					bool allowed = r.DiffTypes.Contains(type);
					if (NavButton(Regulation.DiffTypeNames[type], allowed ? Theme.ChipActive : Theme.Chip)) {
						if (allowed && r.DiffTypes.Count > 1) {
							r.DiffTypes.Remove(type);
						} else if (!allowed) {
							r.DiffTypes.Add(type);
						}
					}
				}
				GUILayout.Label(mine.Numbers.TryGetValue(Regulation.DiffTypeKey, out float diff) ? $"<color={Theme.MutedHex}>yours {Regulation.DiffTypeName(Mathf.RoundToInt(diff))}</color>" : "", Theme.Small);
				GUILayout.FlexibleSpace();
				if (NavButton("Remove", Theme.Button)) {
					r.DiffTypes.Clear();
				}
				GUILayout.EndHorizontal();
			}
			foreach (ValueLimit limit in r.Limits.ToList()) {
				GUILayout.BeginHorizontal();
				GUILayout.Label(TuneLabels.Label(limit.Key), Theme.Body, GUILayout.Width(200));
				GUILayout.Label("least", Theme.Small);
				limit.Min = NumberField("min:" + limit.Key, 70f);
				GUILayout.Label("most", Theme.Small);
				limit.Max = NumberField("max:" + limit.Key, 70f);
				GUILayout.Label(mine.Numbers.TryGetValue(limit.Key, out float value) ? $"<color={Theme.MutedHex}>yours {Regulation.Number(value)}</color>" : "", Theme.Small);
				GUILayout.FlexibleSpace();
				if (NavButton("Remove", Theme.Button)) {
					r.Limits.Remove(limit);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.Label("Add a limit: pick a part of the tune, then a setting", Theme.SectionLabel);
			// The parts are square buttons (the open one lit up), the settings in it round "+" chips in a box of their own
			// (bskftw: they looked the same).
			string[] groups = TuneLabels.Settings.Select(s => s.Group).Where(g => !g.StartsWith("Sound", StringComparison.Ordinal)).Distinct().ToArray();
			for (int start = 0; start < groups.Length; start += 4) {
				GUILayout.BeginHorizontal();
				for (int i = start; i < groups.Length && i < start + 4; i++) {
					bool open = groups[i] == limitGroup_;
					if (NavButton(groups[i], open ? Theme.ButtonPrimary : Theme.Button)) {
						limitGroup_ = open ? "" : groups[i];
					}
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			if (limitGroup_.Length > 0) {
				GUILayout.BeginVertical(Theme.Guide);
				var left = TuneLabels.Settings.Where(s => s.Group == limitGroup_ && r.Limits.All(l => l.Key != s.Key) &&
					!(s.Key == Regulation.DiffTypeKey && r.DiffTypes.Count > 0)).ToList();
				GUILayout.Label(left.Count == 0 ? $"{limitGroup_}: every setting here has a limit already." : $"{limitGroup_}: click a setting to add a limit for it", Theme.Small);
				GUILayout.BeginHorizontal();
				int inRow = 0;
				foreach (TuneLabels.Setting setting in left) {
					if (NavButton("+ " + setting.Label, Theme.Chip)) {
						if (setting.Key == Regulation.DiffTypeKey) {
							// Starts with every type allowed; click the ones to take away.
							r.DiffTypes = Enumerable.Range(0, Regulation.DiffTypeNames.Length).ToList();
							continue;
						}
						r.Limits.Add(new ValueLimit { Key = setting.Key });
						regulationFields_["min:" + setting.Key] = "";
						regulationFields_["max:" + setting.Key] = mine.Numbers.TryGetValue(setting.Key, out float v) ? Regulation.Number(v) : "";
					}
					if (++inRow == 3) {
						inRow = 0;
						GUILayout.FlexibleSpace();
						GUILayout.EndHorizontal();
						GUILayout.BeginHorizontal();
					}
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
			GUILayout.EndVertical();
		}

		/// <summary>Weight classes: cars up to so many kg may run tyres up to so wide.</summary>
		private void DrawTyreTableEditor(Regulation r) {
			GUILayout.Label("Tyres by weight (optional): up to this weight, tyres at most this wide", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			for (int i = 0; i < r.Tyres.Count; i++) {
				GUILayout.BeginHorizontal();
				GUILayout.Label("Up to", Theme.Small);
				float? kg = NumberField($"tyre_kg:{i}", 70f);
				GUILayout.Label("kg: tyres at most", Theme.Small);
				float? mm = NumberField($"tyre_mm:{i}", 70f);
				GUILayout.Label("wide", Theme.Small);
				if (kg.HasValue && kg > 0) {
					r.Tyres[i].UpToKg = kg.Value;
				}
				if (mm.HasValue && mm > 0) {
					r.Tyres[i].MaxWidth = mm.Value;
				}
				GUILayout.FlexibleSpace();
				if (NavButton("Remove", Theme.Button)) {
					r.Tyres.RemoveAt(i);
					for (int j = i; j < r.Tyres.Count; j++) {
						regulationFields_[$"tyre_kg:{j}"] = Regulation.Number(r.Tyres[j].UpToKg);
						regulationFields_[$"tyre_mm:{j}"] = Regulation.Number(r.Tyres[j].MaxWidth);
					}
					GUILayout.EndHorizontal();
					break;
				}
				GUILayout.EndHorizontal();
			}
			if (r.Tyres.Count > 0) {
				GUILayout.Label("A car heavier than every class uses the heaviest one.", Theme.Small);
			}
			GUILayout.BeginHorizontal();
			if (r.Tyres.Count < 20 && NavButton("Add a weight class", Theme.Button)) {
				float kg = r.Tyres.Count > 0 ? r.Tyres.Max(t => t.UpToKg) + 200 : 1200;
				float mm = r.Tyres.Count > 0 ? r.Tyres.Max(t => t.MaxWidth) + 20 : 265;
				r.Tyres.Add(new TyreBracket { UpToKg = kg, MaxWidth = mm });
				regulationFields_[$"tyre_kg:{r.Tyres.Count - 1}"] = Regulation.Number(kg);
				regulationFields_[$"tyre_mm:{r.Tyres.Count - 1}"] = Regulation.Number(mm);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}
	}
}
