// The Report a bug page.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private Vector2 bugScroll_;

		private void DrawBugTab() {
			bugScroll_ = GUILayout.BeginScrollView(bugScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			DrawBugReport();
			GUILayout.EndScrollView();
		}

		private string bugNote_ = "";
		private string lastBugReport_ = "";

		private void DrawBugReport() {
			GUILayout.Label("Report a bug", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Something didn't work? Say what happened (optional), then press Make a bug report. A zip lands on your Desktop with the mod's logs, " +
				"what you were doing, your options and your mods. Send it to bskftw on Discord. It has the names of players you raced, and no tune values of anyone.", Theme.Small);
			bugNote_ = GUILayout.TextArea(bugNote_ ?? "", 1000, Theme.Field, GUILayout.MinHeight(48));
			GUILayout.BeginHorizontal();
			if (NavButton("Make a bug report", Theme.ButtonPrimary)) {
				MakeBugReport();
			}
			if (lastBugReport_.Length > 0 && NavButton("Show the file", Theme.Button)) {
				try {
					Application.OpenURL("file:///" + (Path.GetDirectoryName(lastBugReport_) ?? "").Replace('\\', '/'));
				} catch (Exception e) {
					homeResult_ = $"Couldn't open the folder: {e.Message}";
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (lastBugReport_.Length > 0) {
				GUILayout.Label($"Saved: {lastBugReport_}", Theme.Small);
			}
			GUILayout.EndVertical();
		}
	}
}
