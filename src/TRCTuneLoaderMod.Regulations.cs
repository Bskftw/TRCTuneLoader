using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The Regulations tab (0.16, bskftw: for communities that don't run TRC tunes, like the Gunma Discord): make a
	/// regulation, check a car against it, share it, and pick it for a 1v1 battle, a race or a tournament. The rules
	/// and the checking are in src/Regulations/Regulation.cs; this is the window and the car's facts from CarX.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int RegulationsTab = 7;
		private const string RegulationsFolderName = "Regulations";
		private static readonly string[] RegulationSections = { "Regulations", "Make or edit", "Shared with you" };

		/// <summary>The rule an event is run to, in the 1v1, race and tournament forms.</summary>
		private static readonly string[] RuleModes = { "TRC spec", "Regulation", "Open" };

		private string RegulationsFolder => Path.Combine(modFolder_, RegulationsFolderName);
		private List<Regulation> regulations_ = new List<Regulation>();
		private int regulationsView_;
		private Vector2 regulationsScroll_;
		private string regulationResult_ = "";
		private string checkedRegulation_ = "";
		private string sharingRegulation_ = "";
		private ulong shareRegulationTo_;

		// The editor: a copy of the regulation, and the text in its number fields.
		private Regulation editingRegulation_;
		private readonly Dictionary<string, string> regulationFields_ = new Dictionary<string, string>(StringComparer.Ordinal);
		private string limitGroup_ = "";

		// The player's own car, checked at most every 2 s.
		private CarFacts myFacts_;
		private float myFactsUntil_;
		private List<(PlayerRow Player, RegulationResult Result)> roomChecks_ = new List<(PlayerRow, RegulationResult)>();
		private string roomCheckFor_ = "";
		private float roomCheckUntil_;

		private void LoadRegulations() {
			try {
				regulations_ = Regulation.LoadAll(RegulationsFolder);
				TrcLog.Info($"Regulations: {regulations_.Count} in {RegulationsFolder}.");
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private Regulation RegulationByCode(string code) => regulations_.FirstOrDefault(r => r.Code == code);

		// ---- The car's facts ----

		/// <summary>The player's own car as CarX built it: the setup, every tune number, power, weight, swaps and stance.</summary>
		private CarFacts MyCarFacts() {
			if (myFacts_ != null && Time.unscaledTime < myFactsUntil_) {
				return myFacts_;
			}
			myFactsUntil_ = Time.unscaledTime + 2f;
			var facts = new CarFacts();
			try {
				if (!RefreshCar()) {
					return myFacts_ = facts;
				}
				facts.CarId = car_.CarId;
				facts.CarName = car_.CarName;
				facts.Setup = car_.SlotName;
				facts.EngineSwap = !car_.StockEngine;
				if (adapter_.TryReadCurrentTune(car_, out Dictionary<string, float> tune, out _)) {
					if (adapter_.TryGetTuneNumbers(car_.SlotId, tune, out Dictionary<string, float> numbers, out Dictionary<string, int> decimals, out _)) {
						facts.Numbers = numbers;
						facts.Decimals = decimals;
					}
					if (adapter_.TryCheckDrivetrain(car_, tune, out DrivetrainCheck drivetrain, out _) && drivetrain != null) {
						FillFrom(facts, drivetrain);
					}
				}
				if (adapter_.TryCheckStance(car_, out List<string> stance, out _)) {
					facts.Stance = stance;
				}
				facts.Specs = MySpecs();
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			return myFacts_ = facts;
		}

		/// <summary>Another player's car, from what CarX shares and built here.</summary>
		private CarFacts FactsFor(RoomPlayer player) {
			var facts = new CarFacts { CarId = player.CarId, CarName = player.CarName, Setup = player.SlotName, EngineSwap = player.EngineId > 0 };
			try {
				if (adapter_.TryGetTuneNumbers(player.SlotId, player.SharedTune, out Dictionary<string, float> numbers, out Dictionary<string, int> decimals, out _)) {
					facts.Numbers = numbers;
					facts.Decimals = decimals;
				}
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			if (player.Drivetrain != null) {
				FillFrom(facts, player.Drivetrain);
				facts.Stance = player.StanceDifferences;
			} else if (player.StanceDifferences.Count > 0) {
				facts.Stance = player.StanceDifferences;
			}
			if (UnknownWorkshop(player) && player.StanceDifferences.Count == 0) {
				facts.Stance = null;
			}
			return facts;
		}

		private static void FillFrom(CarFacts facts, DrivetrainCheck drivetrain) {
			facts.Hp = drivetrain.Hp;
			facts.Mass = drivetrain.Mass;
			facts.EngineSwap = drivetrain.EngineSwap;
			facts.DrivetrainChanged = drivetrain.StockDrivetrain.Length > 0;
			facts.OutsideChanges = drivetrain.Compared ? drivetrain.Differences : null;
		}

		/// <summary>The regulation the race or battle this player is in is run to, or null.</summary>
		private Regulation ActiveRegulation() {
			if (race_ != null && !race_.Ended && race_.Settings.Regulation != null) {
				return race_.Settings.Regulation;
			}
			return battle_ != null && battle_.Active ? battle_.Regulation : null;
		}

		/// <summary>Whether the player's own car meets the active regulation (false without one).</summary>
		private bool MyCarMeetsActiveRule() {
			Regulation rule = ActiveRegulation();
			if (rule == null) {
				return false;
			}
			myFactsUntil_ = 0;
			return rule.Check(MyCarFacts()).Passes;
		}

		// ---- The rule picker (1v1, races, tournaments) ----

		/// <summary>TRC spec, a regulation or Open, and "just for fun". Returns the regulation picked (null for TRC spec or Open).</summary>
		private Regulation DrawRulePicker(ref int mode, ref string code, ref bool fun) {
			GUILayout.Label("Rules", Theme.SectionLabel);
			mode = Choice(RuleModes, mode);
			Regulation picked = null;
			if (mode == 0) {
				GUILayout.Label("Every car TRC spec, all on the same tune set, no flagged mods.", Theme.Small);
			} else if (mode == 2) {
				GUILayout.Label("Any setup.", Theme.Small);
			} else if (regulations_.Count == 0) {
				GUILayout.Label("You have no regulations yet: make one or import one in the Regulations tab.", Theme.PillWarn);
			} else {
				if (RegulationByCode(code) == null) {
					code = regulations_[0].Code;
				}
				foreach (Regulation r in regulations_) {
					if (NavButton($"<b>{r.Name}</b>  <color={Theme.MutedHex}>{r.Code} · {r.Summary()}</color>", r.Code == code ? Theme.RowSelected : Theme.Row)) {
						code = r.Code;
					}
				}
				picked = RegulationByCode(code);
				RegulationResult mine = picked?.Check(MyCarFacts());
				if (mine != null) {
					GUILayout.Label(mine.Passes ? "Your car meets it." : $"Your car doesn't meet it: {string.Join("; ", mine.Broken.Take(3))}", mine.Passes ? Theme.PillGood : Theme.PillWarn);
				}
				GUILayout.Label("Each driver's mod checks their own car before the start. Drivers need TRC Tune Loader 0.16 or newer.", Theme.Small);
			}
			GUILayout.Label("Ratings", Theme.SectionLabel);
			fun = Choice(new[] { "Counts for ratings", "Just for fun" }, fun ? 1 : 0) == 1;
			GUILayout.Label(fun ? "Nobody's Elo or Safety Rating changes." : mode == 2 ? "Open events never change ratings."
				: mode == 1 ? "Moves the regulation's own Elo, not TRC spec Elo." : "Moves TRC spec Elo and Safety Rating.", Theme.Small);
			return mode == 1 ? picked : null;
		}

		// ---- Time attack rules (bskftw, Nov 3: "an option for time attack to be open trc spec or regulations") ----

		private const string TimeAttackRulesPref = "time_attack_rules";
		private const string TimeAttackRegulationPref = "time_attack_regulation";
		private const string TimeAttackSpecPref = "time_attack_spec";
		private static readonly string[] TimeAttackRuleModes = { "TRC spec", "Regulation", "Open", "Player spec" };

		/// <summary>A time attack driven to a player spec: its record's rule is this and the spec's title.</summary>
		private const string SpecRulePrefix = "spec:";
		private int timeAttackMode_ = -1;
		private string timeAttackRule_ = "";
		private string timeAttackSpec_ = "";

		/// <summary>The rules the time attack running was started with (its record's <see cref="TimeAttackRecord.Rule"/>).</summary>
		private string runRule_ = "";

		/// <summary>What a new time attack is driven to: "" TRC spec, "open", or the picked regulation's code.</summary>
		private string TimeAttackRule {
			get {
				LoadTimeAttackRules();
				return timeAttackMode_ == 2 ? TimeAttackLog.OpenRule
					: timeAttackMode_ == 1 && RegulationByCode(timeAttackRule_) != null ? timeAttackRule_
					: timeAttackMode_ == 3 && SpecByTitle(timeAttackSpec_) != null ? SpecRulePrefix + timeAttackSpec_
					: "";
			}
		}

		private void LoadTimeAttackRules() {
			if (timeAttackMode_ < 0) {
				timeAttackMode_ = Mathf.Clamp(KSL.API.Kino.Prefs.Get(TimeAttackRulesPref, 0), 0, TimeAttackRuleModes.Length - 1);
				timeAttackRule_ = KSL.API.Kino.Prefs.Get(TimeAttackRegulationPref, "");
				timeAttackSpec_ = KSL.API.Kino.Prefs.Get(TimeAttackSpecPref, "");
			}
		}

		/// <summary>Tracks: what time attacks are driven to. TRC spec is what counts for TRC; the others have their own times.</summary>
		private void DrawTimeAttackRules() {
			LoadTimeAttackRules();
			GUILayout.BeginHorizontal();
			GUILayout.Label("Rules", Theme.SectionLabel, GUILayout.Width(60));
			int mode = Choice(TimeAttackRuleModes, timeAttackMode_);
			GUILayout.EndHorizontal();
			if (mode != timeAttackMode_) {
				timeAttackMode_ = mode;
				SavePref(TimeAttackRulesPref, mode);
			}
			if (mode == 1) {
				if (regulations_.Count == 0) {
					GUILayout.Label("You have no regulations yet: make one or import one in the Regulations tab.", Theme.PillWarn);
				} else {
					if (RegulationByCode(timeAttackRule_) == null) {
						timeAttackRule_ = regulations_[0].Code;
					}
					GUILayout.BeginHorizontal();
					GUILayout.Space(64);
					int index = regulations_.FindIndex(r => r.Code == timeAttackRule_);
					int pick = Choice(regulations_.Select(r => r.Name).ToArray(), Math.Max(0, index), 4);
					GUILayout.EndHorizontal();
					if (pick != index) {
						timeAttackRule_ = regulations_[pick].Code;
						SavePref(TimeAttackRegulationPref, timeAttackRule_);
					}
				}
			}
			if (mode == 3) {
				if (specs_.Count == 0) {
					GUILayout.Label("You have no specs yet: make one or install one in Specs.", Theme.PillWarn);
				} else {
					if (SpecByTitle(timeAttackSpec_) == null) {
						timeAttackSpec_ = specs_[0].Title;
					}
					GUILayout.BeginHorizontal();
					GUILayout.Space(64);
					int index = specs_.FindIndex(s => s.Title == timeAttackSpec_);
					int pick = Choice(specs_.Select(s => s.Title).ToArray(), Math.Max(0, index), 4);
					GUILayout.EndHorizontal();
					if (pick != index) {
						timeAttackSpec_ = specs_[pick].Title;
						SavePref(TimeAttackSpecPref, timeAttackSpec_);
					}
				}
			}
			GUILayout.Label(mode == 0 ? "TRC spec: the times that count for TRC." : mode == 2 ? "Open: any setup. Open times are kept apart from TRC spec ones."
				: mode == 3 ? "To a player spec: your car has to run the spec's tune for it, and its times are kept apart."
				: "To a regulation: your car is checked against it at the start and finish, and its times are kept apart.", Theme.Small);
		}

		/// <summary>A regulation's name for its code (the code when it's not saved here).</summary>
		private string RuleName(string code) =>
			code.StartsWith(SpecRulePrefix, StringComparison.Ordinal) ? $"{code.Substring(SpecRulePrefix.Length)} spec" : RegulationByCode(code)?.Name ?? code;

		/// <summary>Puts the time attack's rules on its record, and for a regulation whether the car meets it now.</summary>
		private void SetRules(TimeAttackRecord record) {
			record.Rule = runRule_ ?? "";
			if (record.Rule.StartsWith(SpecRulePrefix, StringComparison.Ordinal)) {
				record.RuleOk = MySpecs().Contains(record.Rule.Substring(SpecRulePrefix.Length));
			} else if (record.Rule.Length > 0 && record.Rule != TimeAttackLog.OpenRule) {
				RegulationResult result = RegulationByCode(record.Rule)?.Check(MyCarFacts());
				record.RuleOk = result != null && result.Passes;
			}
		}

		private static string RuleText(bool open, Regulation regulation, bool fun) =>
			(regulation != null ? $"{regulation.Name} regulation" : open ? "Open" : "TRC spec") + (fun ? ", just for fun" : "");

		// ---- The tab ----

		private void DrawRegulationsTab() {
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			int shared = roomBoard_.SharedRegulations.Count;
			int view = SideNav(RegulationSections, regulationsView_, new[] { regulations_.Count > 0 ? regulations_.Count.ToString(CultureInfo.InvariantCulture) : "", editingRegulation_ != null ? "editing" : "", shared > 0 ? $"{shared} new" : "" });
			if (view != regulationsView_) {
				regulationsView_ = view;
				regulationResult_ = "";
				if (view == 1 && editingRegulation_ == null) {
					StartEditingRegulation(new Regulation { Author = BattleMessage.PlainName(MyName()) });
				}
			}
			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			// Up and down only: nothing on these pages should need scrolling sideways.
			regulationsScroll_ = GUILayout.BeginScrollView(regulationsScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			if (regulationResult_.Length > 0) {
				GUILayout.Label(regulationResult_, Theme.Body);
			}
			switch (regulationsView_) {
				case 0: DrawRegulationList(); break;
				case 1: DrawRegulationEditor(); break;
				default: DrawSharedRegulations(); break;
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		private void DrawRegulationList() {
			GUILayout.Label("Regulations keep tunes balanced without everyone running the same tune: a most power, tyre widths, no swaps, which setup. " +
				"Make one, share it, and run 1v1 battles, races and tournaments to it. Each driver's mod checks their own car.", Theme.Small);
			if (regulations_.Count == 0) {
				GUILayout.BeginVertical(Theme.Card);
				GUILayout.Label("No regulations yet. Make one (Make or edit), or import one someone gave you below.", Theme.Small);
				GUILayout.EndVertical();
			}
			foreach (Regulation r in regulations_.ToList()) {
				GUILayout.BeginVertical(Theme.Card);
				GUILayout.Label($"<b>{r.Name}</b>  <color={Theme.MutedHex}>{r.Code}{(r.Author.Length > 0 ? $" · by {r.Author}" : "")}</color>", Theme.Body);
				GUILayout.Label(r.Summary(), Theme.Small);
				if (r.Notes.Length > 0) {
					GUILayout.Label(r.Notes, Theme.Small);
				}
				GUILayout.BeginHorizontal();
				if (NavButton(checkedRegulation_ == r.Code ? "Hide check" : "Check my car", Theme.ButtonPrimary)) {
					checkedRegulation_ = checkedRegulation_ == r.Code ? "" : r.Code;
					myFactsUntil_ = 0;
				}
				if (NavButton("Edit", Theme.Button)) {
					StartEditingRegulation(r);
					regulationsView_ = 1;
				}
				if (NavButton(sharingRegulation_ == r.Code ? "Done sharing" : "Share...", Theme.Button)) {
					sharingRegulation_ = sharingRegulation_ == r.Code ? "" : r.Code;
				}
				if (NavButton("Copy for Discord", Theme.Button)) {
					GUIUtility.systemCopyBuffer = $"{r.Name} ({r.Code}): {r.ShareText()}";
					regulationResult_ = $"Copied {r.Name}. Paste it in Discord: anyone with TRC Tune Loader imports it here with Paste.";
				}
				GUILayout.FlexibleSpace();
				if (NavButton("Delete", Theme.Button)) {
					DeleteRegulation(r);
				}
				GUILayout.EndHorizontal();
				if (checkedRegulation_ == r.Code) {
					DrawRegulationCheck(r);
				}
				if (sharingRegulation_ == r.Code) {
					DrawShareRegulation(r);
				}
				GUILayout.EndVertical();
			}

			GUILayout.Label("Import", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Paste a regulation someone copied for Discord (the line starting TRCREG1.), or a whole .trcreg file's text.", Theme.Small);
			GUILayout.BeginHorizontal();
			if (NavButton("Paste from clipboard", Theme.ButtonPrimary)) {
				ImportRegulation(GUIUtility.systemCopyBuffer);
			}
			if (NavButton("Open the folder", Theme.Button)) {
				Directory.CreateDirectory(RegulationsFolder);
				Application.OpenURL("file:///" + RegulationsFolder.Replace('\\', '/'));
			}
			if (NavButton("Reload", Theme.Button)) {
				LoadRegulations();
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		/// <summary>The player's car against a regulation, and everyone in the room.</summary>
		private void DrawRegulationCheck(Regulation r) {
			CarFacts mine = MyCarFacts();
			RegulationResult result = r.Check(mine);
			string car = mine.CarName.Length > 0 ? $"{mine.CarName} ({Regulation.SetupLabel(mine.Setup)})" : "your car";
			string power = mine.Hp > 0 ? $" · {mine.Hp:0} hp" : "";
			string weight = mine.Mass > 0 ? $" · {mine.Mass:0} kg{(mine.Hp > 0 ? $" · {mine.Hp / mine.Mass:0.000} hp/kg" : "")}" : "";
			GUILayout.Label($"{car}{power}{weight}", Theme.Small);
			if (result.Passes) {
				GUILayout.Label($"Your car meets {r.Name}.", Theme.PillGood);
			} else {
				GUILayout.Label($"Your car doesn't meet {r.Name}:", Theme.PillBad);
				foreach (string broken in result.Broken) {
					GUILayout.Label($"<color={Theme.BadHex}>· {broken}</color>", Theme.Small);
				}
			}
			if (result.Unknown.Count > 0) {
				GUILayout.Label($"<color={Theme.WarnHex}>Couldn't check: {string.Join(", ", result.Unknown.Distinct())}. Drive onto a map, or open the garage, and check again.</color>", Theme.Small);
			}
			RefreshPlayersIfDue();
			// Worked out every 2 s, not every frame: each player's numbers come from CarX's data.
			if (roomCheckFor_ != r.Code || Time.unscaledTime >= roomCheckUntil_) {
				roomCheckFor_ = r.Code;
				roomCheckUntil_ = Time.unscaledTime + 2f;
				roomChecks_ = players_.Where(p => !p.Player.IsLocal).Select(p => (p, r.Check(FactsFor(p.Player)))).ToList();
			}
			if (roomChecks_.Count > 0) {
				GUILayout.Label("In this room", Theme.SectionLabel);
				foreach (var (p, theirs) in roomChecks_) {
					string state = theirs.Passes ? $"<color={Theme.GoodHex}>meets it</color>" : $"<color={Theme.BadHex}>{theirs.Broken[0]}</color>";
					string unknown = theirs.Unknown.Count > 0 ? $" <color={Theme.WarnHex}>(couldn't check {theirs.Unknown.Count} rule(s))</color>" : "";
					GUILayout.Label($"<b>{p.Player.Name}</b>{(p.Player.IsConsole ? $" ({p.Player.Platform})" : "")}  <color={Theme.MutedHex}>{p.Player.CarName}</color> · {state}{unknown}", Theme.Small);
				}
			}
		}

		private void DrawShareRegulation(Regulation r) {
			List<RoomMessage> players = roomBoard_.Fresh(BattleNow).Where(s => s.From != MySteamId).ToList();
			if (shareRegulationTo_ != 0 && !players.Any(p => p.From == shareRegulationTo_)) {
				shareRegulationTo_ = 0;
			}
			GUILayout.Label("Share with", Theme.SectionLabel);
			var who = new List<string> { "Everyone in the room" };
			who.AddRange(players.Select(p => BattleMessage.PlainName(p.Name)));
			int picked = Choice(who.ToArray(), shareRegulationTo_ == 0 ? 0 : players.FindIndex(p => p.From == shareRegulationTo_) + 1);
			shareRegulationTo_ = picked <= 0 ? 0 : players[picked - 1].From;
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && roomSync_.IsAvailable;
			string target = shareRegulationTo_ == 0 ? "the room" : who[picked];
			if (NavButton($"Send to {target}", Theme.ButtonPrimary)) {
				var m = new RoomMessage { Kind = RoomKind.RegulationShare, From = MySteamId, Name = MyName(), To = shareRegulationTo_, Regulation = r };
				int pieces = roomSync_.Send(m);
				regulationResult_ = pieces > 0 ? $"Sent {r.Name} to {target}. They need TRC Tune Loader 0.16 or newer, and save it in Regulations, Shared with you." : "Couldn't send it: is the room's connection all right?";
				TrcLog.Info($"Shared the regulation {r.Name} ({r.Code}) with {target}: {pieces} packet(s).");
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void DrawSharedRegulations() {
			if (roomBoard_.SharedRegulations.Count == 0) {
				GUILayout.Label("Nothing shared with you right now. When someone in the room shares a regulation, it shows here to save.", Theme.Small);
				return;
			}
			foreach (RoomBoard.SharedItem item in roomBoard_.SharedRegulations.ToList()) {
				Regulation r = item.Regulation;
				GUILayout.BeginVertical(Theme.Card);
				GUILayout.Label($"<b>{r.Name}</b>  <color={Theme.MutedHex}>{r.Code} · from {item.FromName}{(item.JustForMe ? ", just for you" : "")}</color>", Theme.Body);
				GUILayout.Label(r.Summary(), Theme.Small);
				GUILayout.BeginHorizontal();
				bool have = RegulationByCode(r.Code) != null;
				if (NavButton(have ? "Already saved" : "Save it", Theme.ButtonPrimary) && !have) {
					SaveRegulation(r.Copy(), $"Saved {r.Name}.");
					roomBoard_.SharedRegulations.Remove(item);
				}
				if (NavButton("Dismiss", Theme.Button)) {
					roomBoard_.SharedRegulations.Remove(item);
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
		}

		private void ImportRegulation(string text) {
			Regulation r = Regulation.FromPasted(text);
			if (r == null) {
				regulationResult_ = "That isn't a regulation: copy the whole TRCREG1. line (or a .trcreg file's text) and try again.";
				return;
			}
			Regulation same = RegulationByCode(r.Code);
			if (same != null) {
				regulationResult_ = $"You already have it: {same.Name} ({same.Code}).";
				return;
			}
			if (r.Name.Length == 0) {
				r.Name = "Imported regulation";
			}
			SaveRegulation(r, $"Imported {r.Name} ({r.Code}).");
		}

		private void SaveRegulation(Regulation r, string done) {
			try {
				r.Save(RegulationsFolder);
				LoadRegulations();
				regulationResult_ = done;
				TrcLog.Info($"Regulation saved: {r.Name} ({r.Code}) at {r.FilePath}.");
			} catch (Exception e) {
				regulationResult_ = $"Couldn't save it: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private void DeleteRegulation(Regulation r) {
			try {
				if (r.FilePath.Length > 0 && File.Exists(r.FilePath)) {
					File.Delete(r.FilePath);
				}
				LoadRegulations();
				regulationResult_ = $"Deleted {r.Name}.";
			} catch (Exception e) {
				regulationResult_ = $"Couldn't delete it: {e.Message}";
			}
		}

		// ---- The editor ----

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

		/// <summary>Limits on any tune value, in CarX's own numbers, and a list of every setting to add one for.</summary>
		private void DrawLimitsEditor(Regulation r, CarFacts mine) {
			GUILayout.Label("Tune limits (in the numbers CarX's tuning screen shows; empty: no limit)", Theme.SectionLabel);
			GUILayout.Label("Checked the way CarX shows them: a tyre stretch of 10.3 shows as 10, so it counts as 10 (10.6 shows as 11). Power and weight are checked to the whole hp and kg: 600.4 hp is 600.", Theme.Small);
			GUILayout.BeginVertical(Theme.Card);
			if (r.Limits.Count == 0) {
				GUILayout.Label("None yet: add one below, like rear tyre width at most 285.", Theme.Small);
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
				var left = TuneLabels.Settings.Where(s => s.Group == limitGroup_ && r.Limits.All(l => l.Key != s.Key)).ToList();
				GUILayout.Label(left.Count == 0 ? $"{limitGroup_}: every setting here has a limit already." : $"{limitGroup_}: click a setting to add a limit for it", Theme.Small);
				GUILayout.BeginHorizontal();
				int inRow = 0;
				foreach (TuneLabels.Setting setting in left) {
					if (NavButton("+ " + setting.Label, Theme.Chip)) {
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
