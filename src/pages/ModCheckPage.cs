// The Mod check page: the mods KSL has loaded and any it flags.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private Vector2 modScroll_;

		private void DrawModCheckTab() {
			GUILayout.Label(modCheck_.AllVerified ? "All mods are verified by KSL" : "Some mods aren't verified by KSL", modCheck_.AllVerified ? Theme.PillGood : Theme.PillWarn);
			GUILayout.Label(modCheck_.ProtectedMode ? "KSL protected mode is on" : "KSL protected mode is off", modCheck_.ProtectedMode ? Theme.PillGood : Theme.PillNeutral);
			foreach (string flag in modCheck_.Flags) {
				GUILayout.Label(flag, Theme.PillBad);
			}

			GUILayout.Label($"Loaded mods ({modCheck_.Entries.Count})", Theme.SectionLabel);
			DrawLines(ref modScroll_, modCheck_.Mods);

			if (NavButton("Refresh", Theme.Button, GUILayout.Width(100))) {
				modCheck_.Refresh();
			}
		}
	}
}
