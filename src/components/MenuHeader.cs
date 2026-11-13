// The top of the window: the logo, the title or the category bar, and the TRC spec pill.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>The top: on Home the logo, title, map and car; inside, the logo (back to Home) and the categories. Then the TRC spec pill and close.</summary>
		private void DrawNavHeader() {
			EnsureNavStyles();
			int current = CurrentCategory;
			GUILayout.BeginHorizontal(HeaderArea);
			if (logo_ != null) {
				float size = current < 0 ? 44f : 36f;
				Rect logoRect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
				GUI.DrawTexture(logoRect, logo_, ScaleMode.ScaleToFit);
				if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && logoRect.Contains(Event.current.mousePosition)) {
					Event.current.Use();
					LogoClicked();
					if (current >= 0) {
						GoHome();
					}
				}
				GUILayout.Space(10);
			}
			if (current < 0) {
				GUILayout.BeginVertical();
				GUILayout.Label("TRC Tune Loader", Theme.Title);
				string where = map_ != null ? map_.Name : "Not on a map";
				GUILayout.Label($"{where}{(car_ != null ? $" · {car_.CarName}" : "")} · v{Version}", Theme.Subtitle);
				GUILayout.EndVertical();
			} else {
				GUILayout.BeginVertical();
				GUILayout.Space(4);
				GUILayout.BeginHorizontal();
				if (NavButton("Home", Theme.Tab)) {
					GoHome();
				}
				NavCategory[] all = Categories;
				for (int c = 0; c < all.Length; c++) {
					GUIStyle[] look = CategoryStyles(all[c].Hex);
					if (NavButton(all[c].Name, c == current ? look[1] : look[0]) && c != current) {
						OpenCategory(c);
					}
				}
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
			GUILayout.FlexibleSpace();
			DrawSpecPill();
			if (NavButton("×", Theme.Close)) {
				CloseWindow();
			}
			GUILayout.EndHorizontal();
			DividerLine();
		}

		/// <summary>The little pill top right: whether the car is on TRC spec, and which set (or a player spec).</summary>
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
