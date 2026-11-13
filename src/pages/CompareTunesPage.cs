// The Compare tunes page: two tunes for your car side by side, only the settings that differ.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Compare tunes (IMPROVEMENT_PLAN.md F7), a page in TRC spec: two tunes for the car you're in, side by side, showing
	/// only the settings that differ, in CarX's own numbers. Either side is one of the official sets' tunes for this car
	/// or the setup saved on the car now. It only reads and shows: nothing is put on the car, and "Check my tune" and the
	/// spec checks don't use anything here.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int CompareTab = 15;

		/// <summary>One side's choice: "" is the car's own saved setup, otherwise a tune set's ID.</summary>
		private const string CompareMyCar = "";

		private string compareA_ = CompareMyCar, compareB_;
		private string compareKey_ = "";
		private float compareAt_;
		private readonly List<string> compareIds_ = new List<string>();
		private string[] compareLabels_ = new string[0];
		private List<string> compareLines_ = new List<string>();
		private Vector2 compareScroll_;

		private void DrawCompareTab() {
			RefreshCarIfDue();
			// What can be picked, and the lines, are worked out in the layout pass only, so both passes of a frame draw the same.
			if (Event.current.type == EventType.Layout) {
				BuildCompare();
			}
			if (compareIds_.Count == 0) {
				GUILayout.Label("Get in a car to compare its tunes.", Theme.Small);
				return;
			}
			GUILayout.Label($"Two tunes for the car you're in, and only what differs between them. Nothing here changes your car.", Theme.Small);
			GUILayout.BeginHorizontal();
			GUILayout.Label("This", Theme.Small, GUILayout.Width(50));
			int a = Choice(compareLabels_, Math.Max(0, compareIds_.IndexOf(compareA_)));
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			GUILayout.Label("against", Theme.Small, GUILayout.Width(50));
			int b = Choice(compareLabels_, Math.Max(0, compareIds_.IndexOf(compareB_)));
			GUILayout.EndHorizontal();
			if (a < compareIds_.Count && b < compareIds_.Count) {
				compareA_ = compareIds_[a];
				compareB_ = compareIds_[b];
			}
			DrawLines(ref compareScroll_, compareLines_);
		}

		/// <summary>The choices for the car, and the comparison (made again when the car or a choice changes, and every 2 s for the car's own setup).</summary>
		private void BuildCompare() {
			if (car_ == null) {
				compareIds_.Clear();
				compareKey_ = "";
				return;
			}
			string carKey = $"{car_.CarId}|{car_.SlotId}";
			if (!compareKey_.StartsWith(carKey + "|", StringComparison.Ordinal)) {
				List<TuneFile> tunes = official_.ForCar(car_.CarId, car_.SlotId);
				compareIds_.Clear();
				compareIds_.Add(CompareMyCar);
				compareIds_.AddRange(official_.Sets.Where(s => tunes.Any(t => t.SetId == s)));
				compareLabels_ = compareIds_.Select(id => id == CompareMyCar ? "My car now" : TuneSets.NameOf(id)).ToArray();
				compareA_ = CompareMyCar;
				compareB_ = compareIds_.Count > 1 ? compareIds_[1] : CompareMyCar;
				compareScroll_ = Vector2.zero;
			}
			if (compareB_ == null || !compareIds_.Contains(compareB_)) {
				compareB_ = compareIds_[compareIds_.Count - 1];
			}
			if (!compareIds_.Contains(compareA_)) {
				compareA_ = CompareMyCar;
			}
			string key = $"{carKey}|{compareA_}|{compareB_}";
			bool mine = compareA_ == CompareMyCar || compareB_ == CompareMyCar;
			if (key == compareKey_ && (!mine || Time.unscaledTime < compareAt_)) {
				return;
			}
			compareKey_ = key;
			compareAt_ = Time.unscaledTime + 2f;
			try {
				compareLines_ = CompareLines(car_, compareA_, compareB_);
			} catch (Exception e) {
				compareLines_ = new List<string> { $"Couldn't compare: {e.Message}" };
				TrcLog.Error(e);
			}
		}

		/// <summary>A side's values (false with why when they can't be read), and what to call it.</summary>
		private bool CompareSide(CarInfo car, string id, out IDictionary<string, float> values, out string name, out string problem) {
			problem = "";
			if (id == CompareMyCar) {
				name = "My car now";
				bool ok = adapter_.TryReadCurrentTune(car, out Dictionary<string, float> saved, out problem);
				values = saved;
				return ok && saved != null;
			}
			TuneFile tune = official_.ForCar(car.CarId, car.SlotId, id);
			name = TuneSets.NameOf(id);
			values = tune?.Values;
			if (tune == null) {
				problem = $"there's no {name} tune for this car";
			}
			return tune != null;
		}

		private List<string> CompareLines(CarInfo car, string a, string b) {
			var lines = new List<string>();
			if (!CompareSide(car, a, out IDictionary<string, float> valuesA, out string nameA, out string problemA)) {
				lines.Add($"{nameA} can't be read: {problemA}.");
				return lines;
			}
			if (!CompareSide(car, b, out IDictionary<string, float> valuesB, out string nameB, out string problemB)) {
				lines.Add($"{nameB} can't be read: {problemB}.");
				return lines;
			}
			if (a == b) {
				lines.Add("That's the same tune on both sides: pick two different ones.");
				return lines;
			}
			// A setting a tune doesn't list is at the car's default (CarX's own rule), so both sides are filled in with those.
			adapter_.TryGetDefaults(car.SlotId, out Dictionary<string, float> defaults, out _);
			defaults = defaults ?? new Dictionary<string, float>();
			var keys = new HashSet<string>(valuesA.Keys, StringComparer.Ordinal);
			keys.UnionWith(valuesB.Keys);
			var fullA = new Dictionary<string, float>(StringComparer.Ordinal);
			var fullB = new Dictionary<string, float>(StringComparer.Ordinal);
			var unknown = new List<string>();
			foreach (string key in keys) {
				bool hasA = valuesA.TryGetValue(key, out float va), hasB = valuesB.TryGetValue(key, out float vb);
				bool hasDefault = defaults.TryGetValue(key, out float d);
				if ((!hasA || !hasB) && !hasDefault) {
					unknown.Add(key); // one side doesn't list it and the car's default isn't known: can't be compared
					continue;
				}
				fullA[key] = hasA ? va : d;
				fullB[key] = hasB ? vb : d;
			}
			adapter_.TryDescribe(car.SlotId, fullA, out Dictionary<string, string> shownA, out _);
			adapter_.TryDescribe(car.SlotId, fullB, out Dictionary<string, string> shownB, out _);
			shownA = shownA ?? new Dictionary<string, string>();
			shownB = shownB ?? new Dictionary<string, string>();

			// The same rounding as the tune codes: a difference smaller than a slider step isn't one.
			List<string> differing = fullA.Keys.Where(k => TuneFingerprint.Counts(k) && TuneFingerprint.FormatValue(fullA[k]) != TuneFingerprint.FormatValue(fullB[k])).ToList();
			int compared = fullA.Keys.Count(TuneFingerprint.Counts);
			lines.Add(differing.Count == 0
				? $"<color={Theme.GoodHex}>No differences</color>: {nameA} and {nameB} are the same on all {compared} settings."
				: $"<b>{differing.Count}</b> of {compared} settings differ. Shown as {nameA} → {nameB}, in CarX's numbers.");
			string group = null;
			foreach (string key in TuneLabels.InDisplayOrder(differing)) {
				if (TuneLabels.Group(key) != group) {
					group = TuneLabels.Group(key);
					lines.Add($"--- {group} ---");
				}
				string textA = shownA.TryGetValue(key, out string ta) ? ta : $"slider {TuneFingerprint.FormatValue(fullA[key])}";
				string textB = shownB.TryGetValue(key, out string tb) ? tb : $"slider {TuneFingerprint.FormatValue(fullB[key])}";
				lines.Add($"{TuneLabels.Label(key)}: {textA}  <color={Theme.MutedHex}>→</color>  <b>{textB}</b>");
			}
			if (unknown.Count > 0) {
				lines.Add("--- Couldn't be compared ---");
				lines.Add($"<color={Theme.MutedHex}>Only one side lists these, and the car's own value for them isn't known here: {string.Join(", ", TuneLabels.InDisplayOrder(unknown).Select(TuneLabels.Label))}.</color>");
			}
			return lines;
		}
	}
}
