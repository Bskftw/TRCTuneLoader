// The rule picker in the 1v1, race and tournament forms: TRC spec, a regulation or Open, and just for fun.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>The rule an event is run to, in the 1v1, race and tournament forms.</summary>
		private static readonly string[] RuleModes = { "TRC spec", "Regulation", "Open" };

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
	}
}
