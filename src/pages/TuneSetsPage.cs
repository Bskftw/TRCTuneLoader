// The Tune sets page: every official tune set, its cars and each tune's numbers.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private TuneFile setTune_;
		private List<string> setNumbers_ = new List<string>();
		private Vector2 setListScroll_;
		private Vector2 setNumbersScroll_;

		private void DrawTuneSetsTab() {
			GUILayout.BeginHorizontal();
			foreach (string set in official_.Sets) {
				if (NavButton(TuneSets.NameOf(set), set == listSet_ ? Theme.ChipActive : Theme.Chip)) {
					listSet_ = set;
					setTune_ = null;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			List<TuneFile> inSet = official_.Tunes.Where(t => t.SetId == listSet_).ToList();
			GUILayout.Label($"{TuneSets.NameOf(listSet_)}: {inSet.Count} cars. Pick one to see its numbers.", Theme.Small);

			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(250), GUILayout.ExpandHeight(true));
			setListScroll_ = GUILayout.BeginScrollView(setListScroll_);
			foreach (TuneFile tune in inSet) {
				bool mine = car_ != null && tune.IsFor(car_.CarId, car_.SlotId);
				string text = (string.IsNullOrEmpty(tune.Car) ? tune.DisplayName : tune.Car) + (mine ? $" <color={Theme.AccentHex}>(your car)</color>" : "");
				if (NavButton(text, tune == setTune_ ? Theme.RowSelected : Theme.Row)) {
					setTune_ = tune;
					setNumbers_ = ValueLines(tune);
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();

			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			if (setTune_ == null) {
				GUILayout.Label("Pick a car on the left to see every setting the way CarX's tuning screen shows it.", Theme.Small);
			} else {
				GUILayout.Label(setTune_.DisplayName, Theme.Heading);
				DrawLines(ref setNumbersScroll_, setNumbers_);
			}
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}
	}
}
