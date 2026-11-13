// The Specs page: your player specs, making one, putting a spec's tune on, and saving your car's tune into one.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Specs made by players (bskftw, Nov 3): save your car's tune into a spec of your own (like TRC's V1, V2, V3 and
	/// Revival), put a spec's tune on, check your car against it, and share it as a zip that others drop into their
	/// specs folder. A regulation can require a spec, so races and battles can be run to it. The files are in
	/// src/data/PlayerSpecs.cs and src/data/SpecFolders.cs, the checks in src/anticheat/SpecChecking.cs; TRC spec
	/// itself stays in TRC Tunes and Tune sets.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private List<PlayerSpec> specs_ = new List<PlayerSpec>();
		private string pickedSpec_ = "";
		private string specResult_ = "";
		private Vector2 specsScroll_;
		private bool makingSpec_;
		private string newSpecName_ = "";
		private string newSpecVersion_ = "v1";
		private string newSpecNotes_ = "";
		private string removingSpec_ = "";

		// Which specs the car runs, worked out at most every 2 s (a regulation or time attack can ask every frame).
		private List<string> mySpecs_ = new List<string>();
		private float mySpecsUntil_;

		private string SpecsFolder => Path.Combine(modFolder_, PlayerSpecs.FolderName);

		private void LoadSpecs() {
			try {
				Directory.CreateDirectory(SpecsFolder);
				foreach (string line in PlayerSpecs.InstallZips(SpecsFolder)) {
					TrcLog.Info($"Specs: {line}");
					specResult_ = specResult_.Length == 0 ? line : $"{specResult_} {line}";
				}
				specs_ = PlayerSpecs.LoadAll(SpecsFolder);
				mySpecsUntil_ = 0;
				TrcLog.Info($"Specs: {specs_.Count} in {SpecsFolder} ({string.Join(", ", specs_.Select(s => $"{s.Title}: {s.Tunes.Count} tune(s)"))}).");
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private PlayerSpec SpecByTitle(string title) => specs_.FirstOrDefault(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));

		/// <summary>Specs: the player's specs down the left, the picked one's tunes and buttons on the right.</summary>
		private void DrawSpecsPage() {
			RefreshCarIfDue();
			GUILayout.Label("Your own specs: a tune for each car, like TRC's V1, V2, V3 and Revival sets, for spec racing with your friends or league. " +
				"Pick one in a regulation (Regulations) to race to it, or in Time attack's rules.", Theme.Small);
			if (specResult_.Length > 0) {
				GUILayout.Label(specResult_, Theme.Body);
			}
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(200), GUILayout.ExpandHeight(true));
			List<string> mine = MySpecs();
			foreach (PlayerSpec spec in specs_) {
				string on = mine.Contains(spec.Title) ? $"  <color={Theme.GoodHex}>on</color>" : "";
				if (NavButton($"<b>{spec.Title}</b>{on}\n<color={Theme.MutedHex}>{spec.Tunes.Count} car(s){(spec.Author.Length > 0 ? $" · {spec.Author}" : "")}</color>",
					spec.Id == pickedSpec_ && !makingSpec_ ? Theme.RowSelected : Theme.Row)) {
					pickedSpec_ = spec.Id;
					makingSpec_ = false;
					removingSpec_ = "";
					specResult_ = "";
				}
			}
			if (specs_.Count == 0) {
				GUILayout.Label("No specs yet.", Theme.Small);
			}
			GUILayout.FlexibleSpace();
			if (NavButton("New spec", makingSpec_ ? Theme.ButtonPrimary : Theme.Button)) {
				makingSpec_ = !makingSpec_;
				specResult_ = "";
			}
			if (NavButton("Look again", Theme.Button)) {
				specResult_ = "";
				LoadSpecs();
				if (specResult_.Length == 0) {
					specResult_ = $"{specs_.Count} spec(s) found.";
				}
			}
			if (NavButton("Open specs folder", Theme.Button)) {
				OpenFolder(SpecsFolder);
			}
			GUILayout.EndVertical();
			GUILayout.Space(10);

			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			specsScroll_ = GUILayout.BeginScrollView(specsScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			PlayerSpec picked = specs_.FirstOrDefault(s => s.Id == pickedSpec_);
			if (makingSpec_) {
				DrawNewSpec();
			} else if (picked != null) {
				DrawSpec(picked, mine.Contains(picked.Title));
			} else {
				GUILayout.BeginVertical(Theme.Guide);
				GUILayout.Label("Make a spec", Theme.Heading);
				GUILayout.Label("1. Press New spec and name it (and a version, like v1).", Theme.Body);
				GUILayout.Label("2. Tune a car the way the spec should have it, then pick the spec and press Save my car's tune to it. Do that for each car.", Theme.Body);
				GUILayout.Label("3. Share it: Make a zip to share, and send the zip. Others drop it in their specs folder and press Look again.", Theme.Body);
				GUILayout.Label("Each spec is a folder in specs, so you can also copy a spec's folder in yourself.", Theme.Small);
				GUILayout.EndVertical();
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		private void DrawNewSpec() {
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("New spec", Theme.Heading);
			GUILayout.Label("Name", Theme.SectionLabel);
			newSpecName_ = GUILayout.TextField(newSpecName_ ?? "", 40, Theme.Field);
			GUILayout.Label("Version (like v1, v2 or 2026)", Theme.SectionLabel);
			newSpecVersion_ = GUILayout.TextField(newSpecVersion_ ?? "", 12, Theme.Field, GUILayout.Width(120));
			GUILayout.Label("Notes", Theme.SectionLabel);
			newSpecNotes_ = GUILayout.TextField(newSpecNotes_ ?? "", 200, Theme.Field);
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			string title = $"{TuneRules.Trim(newSpecName_)} {TuneRules.Trim(newSpecVersion_)}".Trim();
			GUI.enabled = enabled && TuneRules.Trim(newSpecName_).Length > 0 && SpecByTitle(title) == null;
			if (NavButton("Make it", Theme.ButtonPrimary)) {
				try {
					string folder = PlayerSpecs.Create(SpecsFolder, newSpecName_, adapter_.PlayerName ?? "", TuneRules.Trim(newSpecVersion_), newSpecNotes_);
					LoadSpecs();
					pickedSpec_ = Path.GetFileName(folder);
					makingSpec_ = false;
					specResult_ = $"Made {title}. Now save each car's tune into it.";
					TrcLog.Info($"Spec made: {title} in {folder}.");
					newSpecName_ = "";
					newSpecNotes_ = "";
				} catch (Exception e) {
					specResult_ = $"Couldn't make the spec: {e.Message}";
					TrcLog.Error(e);
				}
			}
			GUI.enabled = enabled;
			if (NavButton("Cancel", Theme.Button)) {
				makingSpec_ = false;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (SpecByTitle(title) != null) {
				GUILayout.Label($"You already have {title}: give this one another name or version.", Theme.Small);
			}
			GUILayout.EndVertical();
		}

		private void DrawSpec(PlayerSpec spec, bool carOnIt) {
			GUILayout.Label(spec.Title, Theme.Heading);
			GUILayout.Label($"{(spec.Author.Length > 0 ? $"By {spec.Author} · " : "")}{spec.Tunes.Count} car(s){(spec.Notes.Length > 0 ? $" · {spec.Notes}" : "")}", Theme.Small);
			foreach (string problem in spec.Problems) {
				GUILayout.Label($"<color={Theme.WarnHex}>{problem}</color>", Theme.Small);
			}

			TuneFile forCar = car_ != null ? spec.For(car_.CarId, car_.SlotId) : null;
			if (car_ == null) {
				GUILayout.Label(carError_, Theme.PillNeutral);
			} else if (forCar == null) {
				GUILayout.Label($"No tune for the {car_.CarName} ({car_.SlotName}) in this spec yet.", Theme.PillNeutral);
			} else {
				mySpecChecks_.TryGetValue(spec.Title, out OwnCheck check);
				var (text, pill, _) = SpecVerdict(spec.Title, check);
				GUILayout.Label(text, pill);
				List<string> different = SpecDifferences(check);
				if (different.Count > 0) {
					GUILayout.BeginVertical(Theme.Card);
					foreach (string line in different.Take(20)) {
						GUILayout.Label(line, Theme.Small);
					}
					if (different.Count > 20) {
						GUILayout.Label($"... and {different.Count - 20} more", Theme.Small);
					}
					GUILayout.EndVertical();
				}
			}

			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && forCar != null;
			if (NavButton(car_ != null && car_.InDyno ? "Load its tune into the dyno" : "Put its tune on", Theme.ButtonPrimary)) {
				ApplySpecTune(spec, forCar);
			}
			GUI.enabled = enabled && car_ != null;
			if (NavButton(forCar != null ? "Save my car's tune over it" : "Save my car's tune to it", Theme.Button)) {
				SaveCarToSpec(spec);
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Label("Putting a tune on backs your own up first (TRC Tunes, Backups), like the TRC tunes.", Theme.Small);

			GUILayout.Label("Cars", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (spec.Tunes.Count == 0) {
				GUILayout.Label("No cars yet: tune a car and press Save my car's tune to it.", Theme.Small);
			}
			foreach (TuneFile tune in spec.Tunes.OrderBy(t => t.Car, StringComparer.OrdinalIgnoreCase).ToList()) {
				GUILayout.BeginHorizontal();
				bool yours = car_ != null && tune.IsFor(car_.CarId, car_.SlotId);
				GUILayout.Label($"{(yours ? "<b>" : "")}{(tune.Car.Length > 0 ? tune.Car : $"Car {tune.CarId}")}{(yours ? "</b>" : "")}  <color={Theme.MutedHex}>{(tune.Slot.Length > 0 ? tune.Slot : $"setup {tune.SlotId}")} · {tune.Fingerprint}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				if (NavButton("Remove", Theme.Button)) {
					try {
						PlayerSpecs.RemoveTune(spec, tune);
						mySpecsUntil_ = 0;
						specResult_ = $"Removed the {tune.Car} from {spec.Title}.";
					} catch (Exception e) {
						specResult_ = $"Couldn't remove it: {e.Message}";
					}
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();

			GUILayout.Label("Share", Theme.SectionLabel);
			GUILayout.BeginHorizontal();
			GUI.enabled = enabled && spec.Tunes.Count > 0;
			if (NavButton("Make a zip to share", Theme.Button)) {
				try {
					string zip = PlayerSpecs.Export(spec, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
					specResult_ = $"Saved {Path.GetFileName(zip)} on your Desktop. Others put it in their specs folder (Specs, Open specs folder) and press Look again.";
					TrcLog.Info($"Spec {spec.Title} shared as {zip}.");
				} catch (Exception e) {
					specResult_ = $"Couldn't make the zip: {e.Message}";
					TrcLog.Error(e);
				}
			}
			GUI.enabled = enabled;
			if (NavButton("Open its folder", Theme.Button)) {
				OpenFolder(spec.Folder);
			}
			GUILayout.FlexibleSpace();
			if (removingSpec_ == spec.Id) {
				if (NavButton("Remove the spec", Theme.ButtonPrimary)) {
					RemoveSpec(spec);
				}
				if (NavButton("Keep", Theme.Button)) {
					removingSpec_ = "";
				}
			} else if (NavButton("Remove spec", Theme.Button)) {
				removingSpec_ = spec.Id;
			}
			GUILayout.EndHorizontal();
		}

		private void ApplySpecTune(PlayerSpec spec, TuneFile tune) {
			try {
				if (!RefreshCar()) {
					specResult_ = $"Couldn't put it on: {carError_}";
					return;
				}
				bool intoDyno = car_.InDyno;
				if (!ApplyWithBackup(tune, $"Your tune before putting on {spec.Title}", out string error, out string backupNote)) {
					specResult_ = $"Couldn't {(intoDyno ? "load" : "put on")} {spec.Title}: {error}";
					return;
				}
				mySpecsUntil_ = 0;
				nextStatusRefresh_ = 0;
				specResult_ = intoDyno
					? $"Loaded {spec.Title} into CarX's tuning screen: leave it and choose Yes to keep it. {backupNote}"
					: $"Put on {spec.Title}. {backupNote}";
				TrcLog.Info($"Spec {spec.Title} ({tune.Fingerprint}) {(intoDyno ? "loaded into the dyno" : "applied")} on {car_.Describe()}. {backupNote}");
			} catch (Exception e) {
				specResult_ = $"Couldn't put it on: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private void SaveCarToSpec(PlayerSpec spec) {
			try {
				if (!RefreshCar() || !adapter_.TryReadCurrentTune(car_, out var current, out string error)) {
					specResult_ = $"Couldn't read your tune: {carError_}";
					return;
				}
				var draft = new TuneDraft {
					Name = $"{spec.Title} - {car_.CarName}",
					Car = car_.CarName,
					CarId = car_.CarId,
					SlotId = car_.SlotId,
					Slot = car_.SlotName,
					Author = adapter_.PlayerName ?? "",
					Version = spec.Version,
					Notes = $"{spec.Title} spec",
					Values = new SortedDictionary<string, float>(current, StringComparer.Ordinal),
				};
				if (adapter_.TryDescribe(car_.SlotId, current, out var shown, out _)) {
					draft.Comments = shown;
				}
				string path = PlayerSpecs.SaveTune(spec, draft);
				mySpecsUntil_ = 0;
				specResult_ = $"Saved the {car_.CarName}'s tune ({car_.SlotName}) to {spec.Title}.";
				TrcLog.Info($"Spec {spec.Title}: saved {car_.Describe()} ({draft.Fingerprint}) to {path}.");
			} catch (Exception e) {
				specResult_ = $"Couldn't save it: {e.Message}";
				TrcLog.Error(e);
			}
		}
	}
}
