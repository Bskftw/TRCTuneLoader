// The TRC spec pill (in the rail's bottom since the redesign's Stage 2; the old top bar is gone).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>The little pill at the bottom of the rail: whether the car is on TRC spec, and which set (or a player spec).</summary>
		private void DrawSpecPill() {
			RefreshStatusIfDue();
			string text;
			GUIStyle style;
			OwnCheck status = carStatus_;
			if (status == null || status.Saved == null) {
				List<string> mine = MySpecs();
				text = mine.Count > 0 ? $"{mine[0]} spec" : car_ == null ? "No car" : "No TRC tune";
				style = mine.Count > 0 ? Theme.BadgeGood : Theme.PillNeutral;
			} else if (status.Saved.Matches && status.StanceDifferences.Count == 0 && !status.DrivetrainChanged && status.LiveDifferences.Count == 0) {
				text = $"TRC spec · {TuneSets.NameOf(status.Official.SetId).Replace("TRC ", "")}";
				style = Theme.BadgeGood;
			} else {
				text = "Not TRC spec";
				style = Theme.BadgeWarn;
			}
			GUILayout.BeginVertical();
			GUILayout.Space(6f);
			GUILayout.Label(text, style);
			GUILayout.EndVertical();
		}
	}
}
