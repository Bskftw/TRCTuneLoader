// What a time attack is driven to (TRC spec, a regulation, Open or a player spec), its picker on Tracks, and the rule put on each record.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
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
	}
}
