// Checking your own car against the official tunes: the saved setup, the live car, stance and drivetrain, and the live reference.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod : BaseMod {
		/// <summary>The result of checking the player's own car.</summary>
		private sealed class OwnCheck {
			/// <summary>The official tune the saved setup matches, or (when none matches) the one it was compared with.</summary>
			public TuneFile Official;
			public bool Matched;
			public TuneCheck.Result Saved;
			public bool LiveChecked;
			public List<string> LiveDifferences = new List<string>();
			public string LiveNote = "";

			/// <summary>How body parts (widebody kits) change the stance compared with the stock body.</summary>
			public List<string> StanceDifferences = new List<string>();

			/// <summary>Steam Workshop bodies on the car.</summary>
			public List<WorkshopItem> Workshop = new List<WorkshopItem>();

			/// <summary>Engine, gearbox and drivetrain against CarX's own data (null when it couldn't be checked).</summary>
			public DrivetrainCheck Drivetrain;
			public string Error = "";

			public bool DrivetrainChanged => Drivetrain != null && !Drivetrain.Stock;
		}

		/// <summary>The next own-car check also lists the physics engine's values for the log (the Check button's, only).</summary>
		private bool physicsLinesWanted_;

		private void CheckOwnCarFromMenu() {
			try {
				if (!RefreshCar()) {
					lastResult_ = $"Couldn't check: {carError_}";
					lastDiffs_ = new List<string>();
					return;
				}

				OwnCheck check;
				physicsLinesWanted_ = true;
				try {
					check = CheckOwnCar(recordInGarage: true);
				} finally {
					physicsLinesWanted_ = false;
				}
				lastDiffs_ = new List<string>();
				if (check.Saved != null) {
					lastDiffs_.AddRange(check.Saved.Differences);
				}
				lastDiffs_.AddRange(check.StanceDifferences);
				lastDiffs_.AddRange(check.Workshop.Select(w => w.Describe()));
				if (check.Drivetrain != null) {
					lastDiffs_.Add($"Engine & drivetrain: {check.Drivetrain.Summary()}");
					lastDiffs_.AddRange(check.Drivetrain.Differences);
					if (check.Drivetrain.Differences.Count > 0 && car_.InGarage) {
						// Seen in game: KINO leaves its clutch value behind after its swap is set back to stock.
						lastDiffs_.Add("Just took a KINO swap off? It can leave values behind: press Apply to put CarX's own values back.");
					}
				}
				lastDiffs_.AddRange(check.LiveDifferences);
				lastResult_ = Summarize(check);

				TrcLog.Info($"Check on {car_.Describe()}: {lastResult_}");
				foreach (string diff in lastDiffs_) {
					TrcLog.Info($"  {diff}");
				}
				if (check.Drivetrain != null) {
					TrcLog.Info("  Physics engine / car description:");
					foreach (string line in check.Drivetrain.PhysicsLines) {
						TrcLog.Info($"    {line}");
					}
				}
			} catch (Exception e) {
				lastResult_ = $"Check failed: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>
		/// Checks the saved setup against the official tunes, then the live car against the reference CarX
		/// produced for it. In the garage, with an official saved setup, the reference is (re)recorded first.
		/// </summary>
		private OwnCheck CheckOwnCar(bool recordInGarage) =>
			CheckCarAgainst(official_.ForCar(car_.CarId, car_.SlotId), chosenSet_, recordInGarage,
				$"There's no TRC tune for this car on its {car_.SlotName} setup. The Tune sets tab lists the cars that have one.");

		/// <summary>
		/// The full check of the player's own car against these tunes (TRC's sets, or a player spec's tune for this car):
		/// the saved setup, stance, Workshop bodies, engine and drivetrain, and the live car against a reference recorded
		/// for the matching tune. The same check whichever it is, so a player spec is checked as strictly as TRC spec.
		/// </summary>
		/// <summary>Only timed here (for the performance log); the check itself is <see cref="CheckCarAgainstNow"/>.</summary>
		private OwnCheck CheckCarAgainst(List<TuneFile> tunes, string preferredSet, bool recordInGarage, string noTune) {
			long began = PerfLog.Begin();
			try {
				return CheckCarAgainstNow(tunes, preferredSet, recordInGarage, noTune);
			} finally {
				PerfLog.End("tune check", began);
			}
		}

		private OwnCheck CheckCarAgainstNow(List<TuneFile> tunes, string preferredSet, bool recordInGarage, string noTune) {
			var check = new OwnCheck();
			if (tunes.Count == 0) {
				check.Error = noTune;
				return check;
			}

			if (!adapter_.TryReadCurrentTune(car_, out var current, out string error) ||
				!adapter_.TryGetDefaults(car_.SlotId, out var defaults, out error)) {
				check.Error = $"Couldn't read your tune: {error}";
				return check;
			}

			// Which official set (if any) is on the car? If none, compare with the set the player picked.
			int slotId = car_.SlotId;
			Func<TuneFile, TuneCheck.Result> compare = t => TuneCheck.Compare(car_.CarIdText, car_.SlotIdText, t.Fingerprint, t.Values, current, defaults,
				(key, value) => DescribeValue(slotId, key, value));
			foreach (TuneFile tune in tunes) {
				TuneCheck.Result result = compare(tune);
				if (result.Matches) {
					check.Official = tune;
					check.Matched = true;
					check.Saved = result;
					break;
				}
			}
			if (!check.Matched) {
				check.Official = tunes.Find(t => t.SetId == preferredSet) ?? tunes[0];
				check.Saved = compare(check.Official);
			}

			if (adapter_.TryCheckStance(car_, out var stance, out _)) {
				check.StanceDifferences = stance;
			}
			if (adapter_.TryGetWorkshopItems(car_, out var workshop, out _)) {
				// Your car is built with its Workshop body, so the live stance check is what counts.
				workshop.ForEach(w => w.MovesWheels &= check.StanceDifferences.Count > 0);
				check.Workshop = workshop;
			}
			if (adapter_.TryCheckDrivetrain(car_, current, physicsLinesWanted_, out var drivetrain, out _)) {
				check.Drivetrain = drivetrain;
			}

			if (recordInGarage && car_.InGarage && check.Saved.Matches &&
				!adapter_.TryRecordReference(car_, check.Official.Fingerprint, out string recordError)) {
				check.LiveNote = recordError;
			}

			check.LiveChecked = adapter_.TryCheckLive(car_, check.Official.Fingerprint, out var liveDifferences, out string note);
			check.LiveDifferences = liveDifferences;
			if (!check.LiveChecked && string.IsNullOrEmpty(check.LiveNote)) {
				check.LiveNote = note;
			}
			return check;
		}

		/// <summary>
		/// Records the live check's reference without the player pressing Check, every time a car with an official
		/// saved setup has none yet this session: on the main garage screen the way Check does (CarX puts the saved
		/// setup back on the car first), or, for a car that went straight to the track (joining a room, swapping car
		/// in one), from the car CarX built there, a few seconds after it arrives.
		/// </summary>
		private void UpdateLiveReference() {
			if (Time.unscaledTime < nextReferenceCheck_) {
				return;
			}
			nextReferenceCheck_ = Time.unscaledTime + ReferenceCheckSeconds;
			if (!RefreshCar() || car_.InDyno) {
				referencePlace_ = "";
				return;
			}

			// Wait until the car has been in the same place (garage or track) for a moment, so CarX has finished building it.
			string place = $"{car_.CarId}:{car_.SlotId}:{(car_.InGarage ? "garage" : "track")}";
			if (place != referencePlace_) {
				referencePlace_ = place;
				referenceSince_ = Time.unscaledTime;
				return;
			}
			if (Time.unscaledTime - referenceSince_ < (car_.InGarage ? GarageReferenceSeconds : ArrivalCheckSeconds)) {
				return;
			}

			if (!adapter_.TryReadCurrentTune(car_, out var current, out _)) {
				return;
			}
			TuneFile official = MatchingOfficial(current) ?? MatchingSpecTune(current);
			if (official == null || adapter_.HasReference(car_, official.Fingerprint)) {
				return;
			}

			bool recorded = car_.InGarage
				? adapter_.TryRecordReference(car_, official.Fingerprint, out string error)
				: adapter_.TryRecordReferenceOnTrack(car_, official.Fingerprint, out error);
			string result = recorded
				? $"Live check reference recorded {(car_.InGarage ? "in the garage" : "when the car reached the track")} for {car_.Describe()} ({official.Name}, {official.Fingerprint})."
				: $"Live check reference for {car_.Describe()} not recorded yet: {error}";
			// A garage screen that isn't the main one fails every few seconds; say so once.
			if (result != lastReferenceResult_) {
				lastReferenceResult_ = result;
				TrcLog.Info(result);
			}
			if (recorded) {
				nextStatusRefresh_ = 0;
			}
		}

		/// <summary>"TRC Revival", or "TRC Revival (same tune as TRC V2.0)" when other sets have the identical tune.</summary>
		private string SetLabel(TuneFile tune) {
			if (!int.TryParse(tune.CarId, out int carId) || !int.TryParse(tune.SlotId, out int slotId)) {
				return tune.Name;
			}
			List<string> same = official_.ForCar(carId, slotId)
				.Where(t => t != tune && t.Fingerprint == tune.Fingerprint)
				.Select(t => t.Name)
				.ToList();
			return same.Count == 0 ? tune.Name : $"{tune.Name} (same tune as {string.Join(", ", same)})";
		}

		private string Summarize(OwnCheck check) {
			if (check.Saved == null) {
				return check.Error;
			}

			string code = check.Saved.Code;
			string set = check.Matched ? SetLabel(check.Official) : check.Official.Name;
			string extra = check.LiveDifferences.Count > 0 ? $" The live car differs in {check.LiveDifferences.Count} place(s)." : "";

			if (!check.Saved.Matches) {
				return $"Not TRC spec: your saved setup matches no official set; {check.Saved.Differences.Count} difference(s) from {set}.{extra} Your code: {code}";
			}
			if (check.StanceDifferences.Count > 0) {
				return $"Not TRC spec: your saved setup is {set} spec, but the car's stance (ride height, track width or axles) isn't stock (see below).{extra} Code of the saved setup: {code}";
			}
			if (check.DrivetrainChanged) {
				return $"Not TRC spec: your saved setup is {set} spec, but {DrivetrainProblem(check.Drivetrain)} (see below).{extra} Code of the saved setup: {code}";
			}
			if (!check.LiveChecked) {
				return $"Saved setup is {set} spec (code {code}). Live car not checked: {check.LiveNote}";
			}
			return check.LiveDifferences.Count == 0
				? $"{set} spec: saved setup and live car. Your code: {code}"
				: $"Saved setup is {set} spec, but the live car differs in {check.LiveDifferences.Count} place(s), so this isn't TRC spec. Code of the saved setup: {code}";
		}

		private static string DrivetrainProblem(DrivetrainCheck drivetrain) => drivetrain.EngineSwap
			? "a CarX engine swap is installed"
			: "the engine, gearbox, drivetrain or suspension was changed outside CarX's tuning (a KINO swap or air suspension, or another mod)";

		private string DescribeValue(int slotId, string key, float value) {
			var single = new Dictionary<string, float> { [key] = value };
			return adapter_.TryDescribe(slotId, single, out var text, out _) && text.TryGetValue(key, out string shown)
				? shown
				: TuneFingerprint.FormatValue(value);
		}

		/// <summary>Every setting of a tune the way CarX's tuning screen shows it, grouped ("---" lines are headings).</summary>
		private List<string> ValueLines(TuneFile tune) {
			var lines = new List<string>();
			try {
				int slotId = int.TryParse(tune.SlotId, out int id) ? id : 0;
				lines.Add($"{tune.Slot} setup · code {tune.Fingerprint} · slider positions run from 0 to 1");
				adapter_.TryDescribe(slotId, tune.Values, out var shown, out string error);

				string group = null;
				foreach (string key in TuneLabels.InDisplayOrder(tune.Values.Keys)) {
					if (TuneLabels.Group(key) != group) {
						group = TuneLabels.Group(key);
						lines.Add($"--- {group} ---");
					}
					string real = shown.TryGetValue(key, out string text) ? text : "?";
					lines.Add($"{TuneLabels.Label(key)}: {real}   <color={Theme.MutedHex}>(slider {TuneFingerprint.FormatValue(tune.Values[key])})</color>");
				}

				if (adapter_.TryGetDefaults(slotId, out var defaults, out _)) {
					var notListed = defaults.Keys.Where(k => !tune.Values.ContainsKey(k)).ToList();
					if (notListed.Count > 0) {
						lines.Add("--- Not in this tune: must stay at the car's default ---");
						lines.AddRange(TuneLabels.InDisplayOrder(notListed).Select(TuneLabels.Label));
					}
				}

				if (shown.Count == 0) {
					lines.Insert(1, $"Real numbers aren't available ({error}). Slider positions are shown instead.");
				}
			} catch (Exception e) {
				lines.Add($"Couldn't read the real numbers: {e.Message}");
				TrcLog.Error(e);
			}
			return lines;
		}
	}
}
